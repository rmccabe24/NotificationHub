﻿using Cloud.Core.NotificationHub.Models.DTO;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Text;

namespace Cloud.Core.NotificationHub.Providers
{
    /// <summary>
    /// Class Email Message.
    /// </summary>
    public class EmailMessage
    {
        /// <summary>Gets or sets the recipient list (send as blind carbon copy).</summary>
        /// <value>List of string recipients.</value>
        public List<string> To { get; set; }

        /// <summary>Gets or sets the email subject.</summary>
        /// <value>The email subject.</value>
        public string Subject { get; set; }

        /// <summary>Gets or sets the name of the email template to use.</summary>
        /// <value>The name of the template.</value>
        public string TemplateName { get; set; }

        /// <summary>Gets or sets the email content.</summary>
        /// <value>The email content.</value>
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is plain text.
        /// </summary>
        /// <value><c>true</c> if this instance is plain text; otherwise, <c>false</c>.</value>
        public bool IsPlainText { get; set; }

        public List<ResourceLink> Links { get; set; }

        /// <summary>Gets or sets the email attachments.</summary>
        /// <value>The attachments.</value>
        public List<IFormFile> Attachments { get; set; } = new List<IFormFile>();

        internal string FullContent
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var link in Links)
                {
                    sb.Append(BuildLink(link));
                }
                return $"{Content}{sb}";
            }
        }
        private string BuildLink(ResourceLink resource)
        {
            return IsPlainText ? $"\n{resource.Name}: {resource.Link}" : $"<br><a href='{resource.Link}'>{resource.Name}</a>";
        }
    }
}
