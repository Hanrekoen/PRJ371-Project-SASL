using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a character's remote audio stream starts or stops producing audible samples.
    ///     Used to gate lip sync playback so animation starts only when actual audio signal is present (e.g.
    ///     AudioStream.PlaybackStarted).
    /// </summary>
    public readonly struct CharacterAudioPlaybackStateChanged
    {
        /// <summary>The character's unique identifier.</summary>
        public string CharacterId { get; }

        /// <summary>True when playback (audible signal) has started; false when it has stopped.</summary>
        public bool IsPlaying { get; }

        /// <summary>When the playback state changed (UTC).</summary>
        public DateTime Timestamp { get; }

        public CharacterAudioPlaybackStateChanged(string characterId, bool isPlaying, DateTime timestamp)
        {
            CharacterId = characterId;
            IsPlaying = isPlaying;
            Timestamp = timestamp;
        }

        public static CharacterAudioPlaybackStateChanged Create(string characterId, bool isPlaying) =>
            new(characterId, isPlaying, DateTime.UtcNow);

        public static CharacterAudioPlaybackStateChanged Started(string characterId) => Create(characterId, true);
        public static CharacterAudioPlaybackStateChanged Stopped(string characterId) => Create(characterId, false);
    }
}
