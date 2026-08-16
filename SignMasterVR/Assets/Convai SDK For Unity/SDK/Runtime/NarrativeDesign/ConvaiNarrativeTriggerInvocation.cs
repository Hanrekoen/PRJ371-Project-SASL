namespace Convai.Runtime.NarrativeDesign
{
    /// <summary>
    ///     Local record of a narrative trigger request accepted by the SDK.
    /// </summary>
    public readonly struct ConvaiNarrativeTriggerInvocation
    {
        public ConvaiNarrativeTriggerInvocation(ConvaiNarrativeTriggerRequest request, bool queued)
        {
            Request = request;
            Queued = queued;
        }

        /// <summary>Typed narrative trigger request accepted by the SDK.</summary>
        public ConvaiNarrativeTriggerRequest Request { get; }

        /// <summary>Trigger name sent to Convai for saved-trigger requests.</summary>
        public string TriggerName => Request.Mode == ConvaiNarrativeTriggerMode.SavedTrigger ? Request.WireFieldValue : string.Empty;

        /// <summary>Trigger message sent to Convai for inline event or scripted speech requests.</summary>
        public string TriggerMessage => Request.Mode == ConvaiNarrativeTriggerMode.SavedTrigger ? string.Empty : Request.WireFieldValue;

        /// <summary>True when the request was queued because the character was not ready yet.</summary>
        public bool Queued { get; }
    }
}
