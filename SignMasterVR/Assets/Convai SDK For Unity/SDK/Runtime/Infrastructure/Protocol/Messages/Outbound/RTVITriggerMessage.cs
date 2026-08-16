using System.Collections.Generic;
using Convai.Runtime.NarrativeDesign;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound trigger message.</summary>
    public class RTVITriggerMessage : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVITriggerMessage" /> class.
        /// </summary>
        /// <param name="request">Typed narrative trigger request to send.</param>
        public RTVITriggerMessage(ConvaiNarrativeTriggerRequest request)
        {
            Type = "trigger-message";
            Data = new Dictionary<string, string> { { request.WireFieldName, request.WireFieldValue } };
        }
    }
}
