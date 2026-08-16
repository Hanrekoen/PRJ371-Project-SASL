using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Raised when a processed final user transcription arrives from the backend.
    /// </summary>
    public readonly struct FinalUserTranscriptionReceived
    {
        public FinalUserTranscriptionReceived(string text, SpeakerInfo speakerInfo, string messageId, DateTime timestamp)
        {
            Text = text ?? string.Empty;
            SpeakerInfo = speakerInfo.IsValid ? speakerInfo : SpeakerInfo.Empty;
            MessageId = messageId ?? string.Empty;
            Timestamp = timestamp;
        }

        public string Text { get; }
        public SpeakerInfo SpeakerInfo { get; }
        public string MessageId { get; }
        public DateTime Timestamp { get; }
        public string SpeakerId => SpeakerInfo.SpeakerId;
        public string SpeakerName => SpeakerInfo.SpeakerName;
        public string ParticipantId => SpeakerInfo.ParticipantId;

        public static FinalUserTranscriptionReceived Create(string text, SpeakerInfo speakerInfo, string messageId = null) =>
            new(text, speakerInfo, messageId, DateTime.UtcNow);
    }
}
