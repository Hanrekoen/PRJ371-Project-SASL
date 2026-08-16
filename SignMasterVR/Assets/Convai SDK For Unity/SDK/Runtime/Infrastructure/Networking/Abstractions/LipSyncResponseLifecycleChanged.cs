using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Internal response-scoped speech lifecycle. Public speech events remain a compatibility projection.
    /// </summary>
    internal readonly struct LipSyncResponseLifecycleChanged
    {
        internal LipSyncResponseLifecycleChanged(
            string characterId,
            string participantId,
            bool isSpeaking,
            in LipSyncResponseOwner owner,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            IsSpeaking = isSpeaking;
            Owner = owner;
            Timestamp = timestamp;
        }

        internal string CharacterId { get; }
        internal string ParticipantId { get; }
        internal bool IsSpeaking { get; }
        internal LipSyncResponseOwner Owner { get; }
        internal DateTime Timestamp { get; }
    }
}
