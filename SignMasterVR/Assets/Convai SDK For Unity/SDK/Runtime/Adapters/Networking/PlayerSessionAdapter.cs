using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Services.Transcript;
using TranscriptionPhase = Convai.Domain.Models.TranscriptionPhase;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Adapter for IPlayerSession that bridges IConvaiPlayerAgent to the Transport layer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This adapter bridges player ASR events to both:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>The player's <see cref="IConvaiPlayerEvents" /> implementation (if any)</description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     The domain transcript pipeline via <see cref="PlayerTranscriptAdapter" /> (if EventHub
    ///                     provided)
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Transcript Integration:</b>
    ///         When constructed with an <see cref="IEventHub" />, this adapter creates a
    ///         <see cref="PlayerTranscriptAdapter" />
    ///         that publishes <c>PlayerTranscriptReceived</c> domain events. This enables the unified transcript UI pipeline
    ///         to display player ASR results alongside character transcripts.
    ///     </para>
    /// </remarks>
    internal class PlayerSessionAdapter : IPlayerSession, IPlayerTypedTranscriptSink, IDisposable
    {
        private readonly IConvaiPlayerAgent _player;
        private readonly PlayerTranscriptAdapter _transcriptAdapter;

        /// <summary>
        ///     Creates a PlayerSessionAdapter without domain event publishing.
        /// </summary>
        /// <param name="player">The player agent. Required.</param>
        public PlayerSessionAdapter(IConvaiPlayerAgent player)
            : this(player, null)
        {
        }

        /// <summary>
        ///     Creates a PlayerSessionAdapter with optional domain event publishing.
        /// </summary>
        /// <param name="player">The player agent. Required.</param>
        /// <param name="eventHub">Optional event hub for publishing domain events. If provided, enables transcript UI pipeline.</param>
        public PlayerSessionAdapter(IConvaiPlayerAgent player, IEventHub eventHub)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));

            if (eventHub != null)
            {
                _transcriptAdapter = new PlayerTranscriptAdapter(
                    eventHub,
                    player.PlayerId,
                    player.PlayerName,
                    () => player.PlayerName
                );
            }
        }

        /// <inheritdoc />
        public void Dispose() => _transcriptAdapter?.Dispose();

        /// <inheritdoc />
        public string PlayerId => _player.PlayerId;

        /// <inheritdoc />
        public string PlayerName => _player.PlayerName;

        /// <inheritdoc />
        public bool IsMicMuted { get; private set; }

        /// <inheritdoc />
        public void StartListening(int microphoneIndex = 0)
        {
        }

        /// <inheritdoc />
        public void StopListening()
        {
        }

        /// <inheritdoc />
        public void SetMicMuted(bool mute) => IsMicMuted = mute;

        /// <inheritdoc />
        public void SetMicrophoneIndex(int index)
        {
        }

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase)
        {
            // final-user-transcription is the canonical domain event for processed text.
            // Keep this legacy phase callback for the player without publishing a second reducer input.
            if (transcriptionPhase != TranscriptionPhase.ProcessedFinal)
                _transcriptAdapter?.OnPlayerTranscriptionReceived(transcript, transcriptionPhase);

            if (_player is IConvaiPlayerEvents playerEvents)
                playerEvents.OnPlayerTranscriptionReceived(transcript, transcriptionPhase);
        }

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
            SpeakerInfo speakerInfo)
        {
            if (transcriptionPhase != TranscriptionPhase.ProcessedFinal)
                _transcriptAdapter?.OnPlayerTranscriptionReceived(transcript, transcriptionPhase, speakerInfo);

            if (_player is IConvaiPlayerEvents playerEvents)
                playerEvents.OnPlayerTranscriptionReceived(transcript, transcriptionPhase, speakerInfo);
        }

        /// <inheritdoc />
        public void OnPlayerStartedSpeaking(string sessionId)
        {
            _transcriptAdapter?.OnPlayerStartedSpeaking(sessionId);

            if (_player is IConvaiPlayerEvents playerEvents) playerEvents.OnPlayerStartedSpeaking(sessionId);
        }

        /// <inheritdoc />
        public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript)
        {
            _transcriptAdapter?.OnPlayerStoppedSpeaking(sessionId, didProduceFinalTranscript);

            if (_player is IConvaiPlayerEvents playerEvents)
                playerEvents.OnPlayerStoppedSpeaking(sessionId, didProduceFinalTranscript);
        }

        public void PublishTypedText(string transcript, string messageId, SpeakerInfo speakerInfo = default) =>
            _transcriptAdapter?.PublishTypedText(transcript, messageId, speakerInfo);

#pragma warning disable CS0067
        /// <inheritdoc />
        public event Action<string> MicrophoneStreamStarted;

        /// <inheritdoc />
        public event Action<string> MicrophoneStreamStopped;
#pragma warning restore CS0067
    }
}
