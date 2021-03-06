﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cloud.Core.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Cloud.Core.NotificationHub.Controllers
{
    /// <summary>
    /// Attachment controller for uploading attachments into the system.
    /// Implements the <see cref="ControllerBase" />
    /// </summary>
    /// <seealso cref="ControllerBase" />
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Produces("application/json")]
    public class AttachmentController : ControllerBase
    {
        private const string DefaultContentType = "application/octet-stream";
        private readonly IBlobStorage _blobStorage;
        private readonly AppSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailController"/> class.
        /// </summary>
        /// <param name="blobStorage">Blob storage.</param>
        /// <param name="settings">Application settings.</param>
        public AttachmentController(IBlobStorage blobStorage, AppSettings settings)
        {
            _blobStorage = blobStorage;
            _settings = settings;
        }

        /// <summary>Retrieve attachment from storage.</summary>
        /// <param name="id">Id of attachment to retrieve.</param>
        /// <returns>File attachment.</returns>
        [HttpGet("{id}")]
        [SwaggerResponse(404, "Attachment not found", typeof(ApiErrorResult))]
        [SwaggerResponse(200, "Attachment", typeof(FileStreamResult))]
        public async Task<IActionResult> GetAttachment(Guid id)
        {
            var filePath = $"{_settings.AttachmentContainerName}/{id}";

            // Return not found result if the blob does not exist.
            var exists = await _blobStorage.Exists(filePath);
            if (!exists)
            {
                ModelState.AddModelError("NotFound", "Attachment file cannot be found");
                return NotFound(new ApiErrorResult(ModelState));
            }

            // Fetch the blob metadata and return the filestream result with the blob.
            var blobData = await _blobStorage.GetBlob(filePath, true);
            var fileName = blobData.Metadata["name"];

            if (!blobData.Metadata.TryGetValue("type", out var contentType))
                contentType = DefaultContentType;

            return new FileStreamResult(await _blobStorage.DownloadBlob(filePath), contentType) 
            { 
                FileDownloadName = fileName 
            };
        }

        /// <summary>Add an attachment into the notification hub that can be sent along with notifications.</summary>
        /// <param name="attachment">The attachment to upload.</param>
        /// <returns>File attachment identifier.</returns>
        [HttpPost(Name = "UploadAttachmentV1")]
        [RequestFormLimits(MultipartBodyLengthLimit = AppSettings.IndividualFileSizeBytesLimit)] // 1mb limit
        [SwaggerResponse(400, "Invalid upload attachment request", typeof(ApiErrorResult))]
        [SwaggerResponse(201, "Attachment uploaded", typeof(Guid))]
        public async Task<IActionResult> UploadAttachment(IFormFile attachment)
        {
            // If the model state is invalid (i.e. required fields are missing), then return bad request.
            if (attachment == null)
            {
                ModelState.AddModelError("Attachment", "Attachment file is required");
                return BadRequest(new ApiErrorResult(ModelState));
            }

            var fileExt = Path.GetExtension(attachment.FileName)?.Replace(".", "");
           
            // If the extension for the file is not on the allowed list, return bad request.
            if (!_settings.AllowedAttachmentTypesList.Contains(fileExt))
            {
                ModelState.AddModelError("Extension", $"{fileExt} is not in list of valid extensions");
                return BadRequest(new ApiErrorResult(ModelState));
            }
            
            var id = Guid.NewGuid();
            var filePath = $"{_settings.AttachmentContainerName}/{id}";

            // Upload to storage.
            await using var uploadStream = attachment.OpenReadStream();
            await _blobStorage.UploadBlob(filePath, uploadStream, new Dictionary<string, string> {
                { "name", attachment.FileName },
                { "id", id.ToString() },
                { "type", attachment.ContentType }
            });
            await uploadStream.DisposeAsync();

            // Return the created response.
            return CreatedAtAction(nameof(GetAttachment), new { id, version = "1" }, id);
        }
    }
}
