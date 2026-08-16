using Convai.Domain.Logging;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        /// <summary>
        ///     Gets whether remote audio playback is enabled for this character.
        ///     When false (default), the character's audio track is unsubscribed and no audio packets are received.
        ///     When true, the character's audio track is subscribed and routed to the AudioSource.
        /// </summary>
        public bool IsRemoteAudioEnabled
        {
            get
            {
                if (!IsInjected || AudioService == null) return false;

                return AudioService.IsRemoteAudioEnabled(CharacterId);
            }
        }

        /// <summary>
        ///     Enables or disables remote audio playback for this character.
        ///     When disabled (default), the character's audio track is unsubscribed (no audio packets received).
        ///     When enabled, the character's audio track is subscribed and routed to the AudioSource.
        /// </summary>
        /// <param name="enabled">True to enable audio playback; false to disable.</param>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool SetRemoteAudioEnabled(bool enabled)
        {
            if (!IsInjected)
            {
                Logger?.Warning(
                    $"[{CharacterName}] Cannot set remote audio: dependencies not injected");
                return false;
            }

            if (AudioService == null)
            {
                Logger?.Warning(
                    $"[{CharacterName}] Cannot set remote audio: audio service not available");
                return false;
            }

            bool result = AudioService.SetRemoteAudioEnabled(CharacterId, enabled);
            if (result)
            {
                Logger?.Debug(
                    $"[{CharacterName}] Remote audio {(enabled ? "enabled" : "disabled")}");
                SafeEventInvoker.Invoke(
                    OnRemoteAudioEnabledChanged,
                    enabled,
                    Logger,
                    "ConvaiCharacter.OnRemoteAudioEnabledChanged",
                    LogCategory.Character);
            }

            return result;
        }

        /// <summary>
        ///     Enables remote audio playback for this character.
        ///     The character's audio track will be subscribed and routed to the AudioSource.
        /// </summary>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool EnableRemoteAudio() => SetRemoteAudioEnabled(true);

        /// <summary>
        ///     Disables remote audio playback for this character.
        ///     The character's audio track will be unsubscribed (no audio packets received).
        /// </summary>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool DisableRemoteAudio() => SetRemoteAudioEnabled(false);

        /// <summary>
        ///     Toggles remote audio playback for this character.
        ///     Intended for Unity UI button bindings.
        /// </summary>
        public void ToggleRemoteAudio() => SetRemoteAudioEnabled(!IsRemoteAudioEnabled);
    }
}
