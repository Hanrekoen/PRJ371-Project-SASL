using System.Collections.Generic;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound template keys update message.</summary>
    public class RTVIUpdateTemplateKeys : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUpdateTemplateKeys" /> class.
        /// </summary>
        /// <param name="templateKeys">Template key map to send to the server.</param>
        public RTVIUpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            Type = "update-template-keys";
            Data = new { template_keys = templateKeys };
        }
    }
}
