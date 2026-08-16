using System;

namespace Convai.Domain.DomainEvents.LipSync
{
    /// <summary>
    ///     Raised when the backend maps a response's audio to its turn timeline
    ///     ("neurosync-audio-timeline-anchor"): the audio being released for the given response
    ///     owner begins at <see cref="AudioStartMs" /> on that turn's audio timeline. Consumers use
    ///     it to gate lip sync playback on the owner of the currently audible audio and to start the
    ///     visual clock at the correct timeline offset.
    /// </summary>
    public readonly struct LipSyncAudioTimelineAnchorReceived
    {
        public LipSyncAudioTimelineAnchorReceived(
            string characterId,
            string participantId,
            string responseId,
            int? neuroSyncTurnId,
            int? epoch,
            int? sequence,
            double audioStartMs,
            double audioDurationMs,
            int? sampleRate,
            int? channels,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            NeuroSyncTurnId = neuroSyncTurnId;
            Epoch = epoch;
            Sequence = sequence;
            AudioStartMs = audioStartMs;
            AudioDurationMs = audioDurationMs;
            SampleRate = sampleRate;
            Channels = channels;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public string ResponseId { get; }
        public int? NeuroSyncTurnId { get; }
        public int? Epoch { get; }
        public int? Sequence { get; }

        /// <summary>Cumulative position (ms) on the turn's audio timeline where this release begins.</summary>
        public double AudioStartMs { get; }

        /// <summary>Duration (ms) of the audio release this anchor precedes.</summary>
        public double AudioDurationMs { get; }

        public int? SampleRate { get; }
        public int? Channels { get; }
        public DateTime Timestamp { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(ResponseId) && AudioStartMs >= 0d;

        public static LipSyncAudioTimelineAnchorReceived Create(
            string characterId,
            string participantId,
            string responseId,
            int? neuroSyncTurnId,
            int? epoch,
            int? sequence,
            double audioStartMs,
            double audioDurationMs,
            int? sampleRate = null,
            int? channels = null)
        {
            return new LipSyncAudioTimelineAnchorReceived(
                characterId,
                participantId,
                responseId,
                neuroSyncTurnId,
                epoch,
                sequence,
                audioStartMs,
                audioDurationMs,
                sampleRate,
                channels,
                DateTime.UtcNow);
        }
    }
}
