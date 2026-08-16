using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Focused interface for room-level audio control (mic muting, character audio).
    ///     Extracted from <see cref="IConvaiRoomController" /> so consumers that only
    ///     manage audio (e.g. audio preference managers) can depend on this narrow contract.
    /// </summary>
    public interface IRoomAudioControl
    {
        /// <summary>
        ///     Sets the microphone muted state.
        /// </summary>
        /// <param name="mute">True to mute, false to unmute.</param>
        public void SetMicMuted(bool mute);

        /// <summary>
        ///     Toggles the microphone muted state.
        /// </summary>
        public void ToggleMicMute();

        /// <summary>
        ///     Sets the audio muted state for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="mute">True to mute, false to unmute.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool mute);

        /// <summary>
        ///     Mutes audio for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool MuteCharacter(string characterId);

        /// <summary>
        ///     Unmutes audio for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool UnmuteCharacter(string characterId);

        /// <summary>
        ///     Gets whether a character's audio is currently muted.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the character is muted.</returns>
        public bool IsCharacterAudioMuted(string characterId);

        /// <summary>
        ///     Sets the audio subscription policy for remote participants.
        /// </summary>
        /// <param name="policy">Function that returns true if the character should receive audio.</param>
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy);

        /// <summary>
        ///     Applies remote audio preference for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="enabled">Whether audio should be enabled.</param>
        public void ApplyRemoteAudioPreference(string characterId, bool enabled);
    }
}
