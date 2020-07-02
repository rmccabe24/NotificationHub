﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cloud.Core.NotificationHub.Models.DTO;
using Cloud.Core.NotificationHub.Models.Events;
using Cloud.Core.NotificationHub.Providers;
using Cloud.Core.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Cloud.Core.NotificationHub.Controllers
{
    /// <summary>
    /// Class EmailController.
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

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailController" /> class.
        /// </summary>
        /// <param name="emailProviders">The email providers.</param>
        /// <param name="messengers">The messengers.</param>
        /// <param name="blobStorage">The BLOB storage.</param>
        /// <param name="settings">The settings.</param>
        public EmailController(NamedInstanceFactory<IEmailProvider> emailProviders, NamedInstanceFactory<IReactiveMessenger> messengers, IBlobStorage blobStorage, AppSettings settings)
        {
            _emailProviders = emailProviders;
            _messenger = messengers["notification"];
            _blobStorage = blobStorage;
            _settings = settings;
        }

        /// <summary>Send an email with attachments sychronously.</summary>
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

        /// <summary>Send an email with attachments sychronously.</summary>
        /// <param name="email">The email to queue for sending.</param>
        /// <returns>Async Task IActionResult.</returns>
        [HttpPost("async")]
        [SwaggerResponse(202, "Email queued for delivery")]
        [SwaggerResponse(400, "Invalid create email request", typeof(ApiErrorResult))]
        [RequestFormLimits(MultipartBodyLengthLimit = AppSettings.RequestSizeBytesLimit)] // 5mb limit
        public async Task<IActionResult> CreateEmailAsync([FromBody] CreateEmailEvent email)
        {
            // TODO: REPLACE WITH FLUENT VALIDATION AND CREATE EMAIL VALIDATOR.
            // If the model state is invalid (i.e. required fields are missing), then return bad request.
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiErrorResult(ModelState));
            }

            // Raise the Email queue event.
            EmailEvent @event = email;

            await _messenger.Send(@event, new KeyValuePair<string, object>[] { new KeyValuePair<string, object>("type", "email") });
            
            // Creation accepted, email will be sent via messaging queue.
            return Accepted();
        }
    }
}
