﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cloud.Core.Extensions;
using Cloud.Core.Notification;
using Cloud.Core.Notification.Events;
using Cloud.Core.NotificationHub.Models;
using Cloud.Core.Template.HtmlMapper;
using Cloud.Core.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Swashbuckle.AspNetCore.Annotations;

namespace Cloud.Core.NotificationHub.Controllers
{
    /// <summary>
    /// Send email synchronously and asynchronously.
    /// Implements the <see cref="ControllerBase" />
    /// </summary>
    /// <seealso cref="ControllerBase" />
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/notification/[controller]")]
    [Produces("application/json")]
    public class EmailController : ControllerBase
    {
        private readonly NamedInstanceFactory<IEmailProvider> _emailProviders;
        private readonly IReactiveMessenger _messenger;
        private readonly IBlobStorage _blobStorage;
        private readonly AppSettings _settings;
        private readonly ITemplateMapper _templateMapper;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailController" /> class.
        /// </summary>
        /// <param name="emailProviders">Configured email providers.</param>
        /// <param name="messengers">EDA messengers list.</param>
        /// <param name="blobStorage">Blob storage.</param>
        /// <param name="settings">Application settings.</param>
        public EmailController(NamedInstanceFactory<IEmailProvider> emailProviders, ITemplateMapper templateMapper, NamedInstanceFactory<IReactiveMessenger> messengers, IBlobStorage blobStorage, AppSettings settings)
        {
            _emailProviders = emailProviders;
            _messenger = messengers["notification"];
            _blobStorage = blobStorage;
            _settings = settings;
            _templateMapper = templateMapper;
        }

        /// <summary>Send an email with attachments synchronously.</summary>
        /// <param name="email">The email to send.</param>
        /// <returns>Async Task IActionResult.</returns>
        [HttpPost]
        [SwaggerResponse(200, "Email sent")]
        [SwaggerResponse(400, "Invalid create email request", typeof(ApiErrorResult))]
        [RequestFormLimits(MultipartBodyLengthLimit = AppSettings.RequestSizeBytesLimit)] // 5mb limit
        public async Task<IActionResult> CreateEmail([FromForm] CreateEmail email)
        {
            // TODO: REPLACE WITH FLUENT VALIDATION AND CREATE EMAIL VALIDATOR.
            // If the model state is invalid (i.e. required fields are missing), then return bad request.
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiErrorResult(ModelState));
            }

            // Default the provider if not set.
            if (email.Provider.IsNullOrDefault())
            {
                email.Provider = _settings.DefaultEmailProvider;
            }

            if (!_emailProviders.TryGetValue(email.Provider.ToString(), out _))
            {
                ModelState.AddModelError("Provider", $"{email.Provider.Value} has no implementation");
                return BadRequest(new ApiErrorResult(ModelState));
            }

            // Validate the attachments, if invalid type or exceeds max allowed size.
            foreach (var attachment in email.Attachments)
            {
                var fileExt = attachment.GetExtension();
                if (!_settings.AllowedAttachmentTypesList.Contains(fileExt))
                {
                    ModelState.AddModelError("Extension", $"{fileExt} is not in list of valid extensions");
                    return BadRequest(new ApiErrorResult(ModelState));
                }

                if (attachment.Length > AppSettings.IndividualFileSizeBytesLimit)
                {
                    ModelState.AddModelError("MaxFileSizeExceeded", $"{attachment.FileName} size of {attachment.Length} exceeds the max allowed size of {AppSettings.IndividualFileSizeBytesLimit}");
                    return BadRequest(new ApiErrorResult(ModelState));
                }
            }

            // Send email using the requested provider.
            var emailProvider = _emailProviders[email.Provider.ToString()];
            await emailProvider.SendAsync(email);
            return Ok();
        }

        /// <summary>Send an email with attachments, where its contents is generated using a template, synchronously.</summary>
        /// <param name="id">Template Id to use when creating the email.</param>
        /// <param name="email">The email to send.</param>
        /// <returns>Async Task IActionResult.</returns>
        [HttpPost("template/{id}")]
        [SwaggerResponse(200, "Email sent")]
        [SwaggerResponse(400, "Invalid create email request", typeof(ApiErrorResult))]
        [RequestFormLimits(MultipartBodyLengthLimit = AppSettings.RequestSizeBytesLimit)] // 5mb limit
        public async Task<IActionResult> CreateTemplatedEmail([FromRoute] string id, [FromForm] CreateTemplateEmail email)
        {
            // TODO: REPLACE WITH FLUENT VALIDATION AND CREATE EMAIL VALIDATOR.
            // If the model state is invalid (i.e. required fields are missing), then return bad request.
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiErrorResult(ModelState));
            }

            email.TemplateId = id;

            // Default the provider if not set.
            if (email.Provider.IsNullOrDefault())
                email.Provider = _settings.DefaultEmailProvider;

            // Attempt to get the provider (which will be used for sending the mail).
            if (!_emailProviders.TryGetValue(email.Provider.ToString(), out _))
            {
                ModelState.AddModelError("Provider", $"{email.Provider.Value} has no implementation");
                return BadRequest(new ApiErrorResult(ModelState));
            }

            // Validate the attachments, if invalid type or exceeds max allowed size.
            foreach (var attachment in email.Attachments)
            {
                var fileExt = attachment.GetExtension();
                if (!_settings.AllowedAttachmentTypesList.Contains(fileExt))
                {
                    ModelState.AddModelError("Extension", $"{fileExt} is not in list of valid extensions");
                    return BadRequest(new ApiErrorResult(ModelState));
                }

                if (attachment.Length > AppSettings.IndividualFileSizeBytesLimit)
                {
                    ModelState.AddModelError("MaxFileSizeExceeded", $"{attachment.FileName} size of {attachment.Length} exceeds the max allowed size of {AppSettings.IndividualFileSizeBytesLimit}");
                    return BadRequest(new ApiErrorResult(ModelState));
                }
            }

            var emailProvider = _emailProviders[email.Provider.ToString()];

            // We have a special case for sendgid, as it has its own templating engine.
            if (email.Provider.ToString().Contains("Sendgrid") && _settings.UseSendgridsTemplating)
            {
                var sendEmail = new EmailTemplateMessage
                {
                    TemplateId = email.TemplateId,
                    TemplateObject = email.TemplateContent,
                    Subject = email.Subject
                };
                sendEmail.To.AddRange(email.To);
                sendEmail.Attachments.AddRange(email.Attachments.Select(a => new EmailAttachment
                {
                    Content = a.OpenReadStream(),
                    Name = a.FileName,
                    ContentType = a.ContentDisposition
                }));

                // Send email using the requested provider.
                await emailProvider.SendAsync(sendEmail);
            }
            else
            {
                var template = await _templateMapper.GetTemplateContent(id);

                if (template == null)
                {
                    return NotFound();
                }

                var obj = JsonConvert.DeserializeObject<dynamic>(email.TemplateContent);
                var model = JToken.Parse(email.TemplateContent);


                var result = template.TemplateContent.SubstitutePlaceholders(model, "{{","}}");

                var sendEmail = new EmailMessage
                {
                    Content  = result.SubstitutedContent,
                    Subject = email.Subject
                };
                sendEmail.To.AddRange(email.To);
                sendEmail.Attachments.AddRange(email.Attachments.Select(a => new EmailAttachment
                {
                    Content = a.OpenReadStream(),
                    Name = a.FileName,
                    ContentType = a.ContentDisposition
                }));

                // Send email using the requested provider.
                await emailProvider.SendAsync(sendEmail);
            }
            
            return Ok();
        }

        /// <summary>Send an email with attachments synchronously.</summary>
        /// <param name="email">The email to queue for sending.</param>
        /// <returns>Async Task IActionResult.</returns>
        [HttpPost("async")]
        [SwaggerResponse(202, "Email queued for delivery")]
        [SwaggerResponse(400, "Invalid create email request", typeof(ApiErrorResult))]
        public async Task<IActionResult> CreateEmailAsync([FromBody] EmailEvent email)
        {
            // If the model state is invalid(i.e.required fields are missing), then return bad request.
            if (!ModelState.IsValid)
                return BadRequest(new ApiErrorResult(ModelState));

            // Default the provider if not set.
            if (email.Provider.IsNullOrDefault())
                email.Provider = _settings.DefaultSmsProvider.ToString();

            if (!_emailProviders.TryGetValue(email.Provider, out _))
            {
                ModelState.AddModelError("Provider", $"{email.Provider} has no implementation");
                return BadRequest(new ApiErrorResult(ModelState));
            }

            foreach (var attId in email.AttachmentIds)
            {
                var filePath = $"{_settings.AttachmentContainerName}/{attId}";
                if (await _blobStorage.Exists(filePath) == false)
                {
                    ModelState.AddModelError("AttachmentId", $"Attachment with id {attId} was not found");
                    return NotFound(new ApiErrorResult(ModelState));
                }
            }

            // Raise the Email queue event.
            EmailEvent @event = email;
            if (!email.TemplateId.IsNullOrDefault())
                @event.Content = JsonConvert.DeserializeObject<dynamic>(email.Content.ToString());
            else
                @event.Content = email.Content.ToString();

            await _messenger.Send(@event, new [] { new KeyValuePair<string, object>("type", "email") });
            
            // Creation accepted, email will be sent via messaging queue.
            return Accepted();
        }
    }
}
