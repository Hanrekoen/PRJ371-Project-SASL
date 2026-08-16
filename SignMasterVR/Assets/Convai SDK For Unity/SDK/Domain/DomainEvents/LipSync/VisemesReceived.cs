using System;
using System.Collections.Generic;

namespace Convai.Domain.DomainEvents.LipSync
{
    /// <summary>
    ///     Raised when raw viseme weights are received for a character.
    /// </summary>
    public readonly struct VisemesReceived
    {
        public VisemesReceived(
            string characterId,
            string participantId,
            IReadOnlyDictionary<string, float> visemes,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            Visemes = visemes ?? new Dictionary<string, float>();
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public IReadOnlyDictionary<string, float> Visemes { get; }
        public DateTime Timestamp { get; }

        public static VisemesReceived Create(string characterId, string participantId,
            IReadOnlyDictionary<string, float> visemes) =>
            new(characterId, participantId, visemes, DateTime.UtcNow);
    }
}
