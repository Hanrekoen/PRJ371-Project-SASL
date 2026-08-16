using System;
using Convai.Domain.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Represents the local player's session, providing microphone control and player identity.
    ///     Extends <see cref="IConvaiPlayerEvents" /> to receive transcription callbacks.
    /// </summary>
    public interface IPlayerSession : IConvaiPlayerEvents
    {
        /// <summary>Gets the unique identifier for the local player.</summary>
        public string PlayerId { get; }

        /// <summary>Gets the display name for the local player.</summary>
        public string PlayerName { get; }

        /// <summary>Gets whether the microphone is currently muted.</summary>
        public bool IsMicMuted { get; }

        /// <summary>Raised when microphone streaming starts.</summary>
        public event Action<string> MicrophoneStreamStarted;

        /// <summary>Raised when microphone streaming stops.</summary>
        public event Action<string> MicrophoneStreamStopped;

        /// <summary>Starts capturing audio from the specified microphone.</summary>
        /// <param name="microphoneIndex">Zero-based index of the microphone device.</param>
        public void StartListening(int microphoneIndex = 0);

        /// <summary>Stops microphone capture.</summary>
        public void StopListening();

        /// <summary>Sets the microphone mute state.</summary>
        /// <param name="mute">True to mute; false to unmute.</param>
        public void SetMicMuted(bool mute);

        /// <summary>Changes the active microphone device.</summary>
        /// <param name="index">Zero-based index of the microphone device.</param>
        public void SetMicrophoneIndex(int index);
    }

    internal interface IPlayerTypedTranscriptSink
    {
        void PublishTypedText(string transcript, string messageId, SpeakerInfo speakerInfo = default);
    }
}
