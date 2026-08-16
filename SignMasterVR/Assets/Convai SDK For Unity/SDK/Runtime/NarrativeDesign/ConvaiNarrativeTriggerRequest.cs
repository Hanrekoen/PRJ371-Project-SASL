using System;

namespace Convai.Runtime.NarrativeDesign
{
    public enum ConvaiNarrativeTriggerMode
    {
        SavedTrigger,
        InlineEvent,
        ScriptedSpeech
    }

    /// <summary>
    ///     Explicit narrative trigger request that maps to exactly one RTVI trigger-message field.
    /// </summary>
    public readonly struct ConvaiNarrativeTriggerRequest
    {
        private const string TriggerNameField = "trigger_name";
        private const string TriggerMessageField = "trigger_message";

        private ConvaiNarrativeTriggerRequest(ConvaiNarrativeTriggerMode mode, string value)
        {
            Mode = mode;
            WireFieldValue = value;
            WireFieldName = mode == ConvaiNarrativeTriggerMode.SavedTrigger
                ? TriggerNameField
                : TriggerMessageField;
        }

        public ConvaiNarrativeTriggerMode Mode { get; }
        public string WireFieldName { get; }
        public string WireFieldValue { get; }

        public static ConvaiNarrativeTriggerRequest SavedTrigger(string triggerName)
        {
            string normalized = NormalizeRequired(triggerName, nameof(triggerName));
            return new ConvaiNarrativeTriggerRequest(ConvaiNarrativeTriggerMode.SavedTrigger, normalized);
        }

        public static ConvaiNarrativeTriggerRequest InlineEvent(string eventMessage)
        {
            string normalized = NormalizeRequired(eventMessage, nameof(eventMessage));
            return new ConvaiNarrativeTriggerRequest(ConvaiNarrativeTriggerMode.InlineEvent, normalized);
        }

        public static ConvaiNarrativeTriggerRequest ScriptedSpeech(string speechText)
        {
            string normalized = NormalizeRequired(speechText, nameof(speechText));
            return new ConvaiNarrativeTriggerRequest(
                ConvaiNarrativeTriggerMode.ScriptedSpeech,
                $"<speak>{normalized}</speak>");
        }

        private static string NormalizeRequired(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Narrative trigger payload cannot be empty.", parameterName);

            return value.Trim();
        }
    }
}
