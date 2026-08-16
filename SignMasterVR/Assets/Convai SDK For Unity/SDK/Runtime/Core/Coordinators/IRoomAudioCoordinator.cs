using System;
using System.Threading;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Coordinates room audio including microphone capture and per-character remote audio.
    ///     Extracted from ConvaiRoomManager to enable focused testing and clear responsibility boundaries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This coordinator owns microphone lifecycle and remote audio preference management.
    ///         It delegates actual audio operations to injected dependencies (IAudioTrackManager, etc.).
    ///     </para>
    ///     <para>
    ///         <b>Unity-Native Design</b>: This interface uses <see cref="IConvaiCharacterAgent" /> directly,
    ///         following the SDK's Unity-native architecture principle.
    ///     </para>
    /// </remarks>
    public interface IRoomAudioCoordinator
    {
        #region Microphone State

        /// <summary>
        ///     Whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Whether audio input is currently active (listening).
        /// </summary>
        public bool IsListening { get; }

        #endregion

        #region Microphone Control

        /// <summary>
        ///     Sets the microphone mute state.
        /// </summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public void SetMicMuted(bool muted);

        /// <summary>
        ///     Starts listening for microphone input.
        /// </summary>
        /// <param name="microphoneIndex">Index of the microphone device to use. 0 = default.</param>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation completing when listening has started.</returns>
        public IConvaiOperation<Unit> StartListeningAsync(int microphoneIndex = 0, CancellationToken ct = default);

        /// <summary>
        ///     Stops listening for microphone input.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation completing when listening has stopped.</returns>
        public IConvaiOperation<Unit> StopListeningAsync(CancellationToken ct = default);

        #endregion

        #region Remote Audio Control

        /// <summary>
        ///     Enables or disables remote audio playback for a specific character.
        /// </summary>
        /// <param name="character">The character whose audio to control.</param>
        /// <param name="enabled">True to enable, false to disable.</param>
        /// <remarks>
        ///     This affects whether audio from the character's response is played through speakers.
        ///     Use to implement "mute NPC" functionality or audio focus switching.
        /// </remarks>
        public void SetRemoteAudioEnabled(IConvaiCharacterAgent character, bool enabled);

        /// <summary>
        ///     Checks if remote audio is enabled for a specific character.
        /// </summary>
        /// <param name="character">The character to check.</param>
        /// <returns>True if remote audio is enabled for this character.</returns>
        public bool IsRemoteAudioEnabled(IConvaiCharacterAgent character);

        #endregion

        #region Events

        /// <summary>
        ///     Fired when the microphone mute state changes.
        ///     Parameter is the new muted state.
        /// </summary>
        public event Action<bool> MicMuteChanged;

        /// <summary>
        ///     Fired when listening state changes.
        ///     Parameter is the new listening state.
        /// </summary>
        public event Action<bool> ListeningStateChanged;

        #endregion
    }
}
