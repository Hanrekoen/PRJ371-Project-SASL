using System;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Logging;
using TranscriptionPhase = Convai.Domain.Models.TranscriptionPhase;

namespace Convai.Runtime.Services.Transcript
{
    /// <summary>
    ///     Adapter that bridges player ASR events to the domain transcript pipeline.
    /// </summary>
    public sealed class PlayerTranscriptAdapter : IConvaiPlayerEvents, IDisposable
    {
        private readonly string _defaultPlayerName;
        private readonly IEventHub _eventHub;
        private readonly Func<string> _playerNameProvider;
        private string _currentSessionId = string.Empty;
        private bool _isDisposed;

        private SpeakerInfo _lastSpeakerInfo = SpeakerInfo.Empty;

        /// <summary>
        ///     Creates a new PlayerTranscriptAdapter.
        /// </summary>
        /// <param name="eventHub">Event hub for publishing domain events. Required.</param>
        /// <param name="playerId">Unique identifier for the player. Required.</param>
        /// <param name="playerName">
        ///     Display name for the player. Falls back to <see cref="PlayerDisplayName.Default" />
        ///     if null or empty.
        /// </param>
        /// <param name="playerNameProvider">Optional dynamic player-name provider used at publish time.</param>
        /// <exception cref="ArgumentNullException">Thrown if eventHub is null.</exception>
        /// <exception cref="ArgumentException">Thrown if playerId is null or empty.</exception>
        public PlayerTranscriptAdapter(
            IEventHub eventHub,
            string playerId,
            string playerName = null,
            Func<string> playerNameProvider = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));

            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player ID cannot be null or empty.", nameof(playerId));

            PlayerId = playerId;
            _defaultPlayerName = string.IsNullOrWhiteSpace(playerName) ? PlayerDisplayName.Default : playerName;
            _playerNameProvider = playerNameProvider;
        }

        public string PlayerId { get; }

        public string PlayerName => ResolvePlayerName();

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase) =>
            OnPlayerTranscriptionReceived(transcript, transcriptionPhase, SpeakerInfo.Empty);

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
            SpeakerInfo speakerInfo)
        {
            if (_isDisposed)
                return;

            ConvaiLogger.Debug(
                $"Transcription received: phase={transcriptionPhase}, text=\"{transcript}\"",
                LogCategory.Player);

            if (speakerInfo.IsValid) _lastSpeakerInfo = speakerInfo;

            SpeakerInfo effectiveSpeakerInfo = speakerInfo.IsValid ? speakerInfo : _lastSpeakerInfo;

            string actorId = effectiveSpeakerInfo.IsValid ? effectiveSpeakerInfo.SpeakerId : PlayerId;

            string safeText = transcript ?? string.Empty;

            var domainEvent = PlayerTranscriptReceived.Create(
                actorId,
                ResolveDisplayName(effectiveSpeakerInfo),
                safeText,
                false,
                transcriptionPhase,
                speakerInfo: effectiveSpeakerInfo,
                turnId: _currentSessionId,
                messageId: _currentSessionId
            );

            _eventHub.Publish(domainEvent);

            if (transcriptionPhase == TranscriptionPhase.Completed) _lastSpeakerInfo = SpeakerInfo.Empty;
        }

        /// <inheritdoc />
        public void OnPlayerStartedSpeaking(string sessionId) => _currentSessionId = sessionId ?? string.Empty;

        /// <inheritdoc />
        public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript)
        {
            if (_currentSessionId == sessionId) _currentSessionId = string.Empty;
        }

        /// <summary>
        ///     Publishes a typed-text transcript event keyed by a stable message ID.
        /// </summary>
        public void PublishTypedText(string transcript, string messageId, SpeakerInfo speakerInfo = default)
        {
            if (_isDisposed)
                return;

            SpeakerInfo effectiveSpeakerInfo = speakerInfo.IsValid ? speakerInfo : SpeakerInfo.Empty;
            string actorId = effectiveSpeakerInfo.IsValid ? effectiveSpeakerInfo.SpeakerId : PlayerId;

            string safeText = transcript ?? string.Empty;

            _eventHub.Publish(PlayerTranscriptReceived.CreateTypedText(
                actorId,
                ResolveDisplayName(effectiveSpeakerInfo),
                safeText,
                messageId,
                effectiveSpeakerInfo));
        }

        /// <inheritdoc />
        public void Dispose() => _isDisposed = true;

        /// <summary>
        ///     The name this turn is attributed to: the configured player name when the developer
        ///     configured one, otherwise the backend's speaker name.
        /// </summary>
        /// <remarks>
        ///     A configured name always wins — the server's speaker directory does not know what
        ///     the game calls its player, and letting it overwrite the name shown in the chat UI
        ///     was a reported defect. But the untouched <see cref="PlayerDisplayName.Default" />
        ///     placeholder is not a name anyone chose, so it yields; without that, every speaker in
        ///     a multi-user room is labelled with the local player's placeholder. This is the same
        ///     rule the room transcript engine applies when it builds the turn's actor, kept in one
        ///     place so the published event and the actor it is filed under cannot disagree.
        /// </remarks>
        private string ResolveDisplayName(in SpeakerInfo speakerInfo)
        {
            string playerName = ResolvePlayerName();
            if (PlayerDisplayName.IsAuthored(playerName)) return playerName;

            return string.IsNullOrWhiteSpace(speakerInfo.SpeakerName) ? playerName : speakerInfo.SpeakerName;
        }

        private string ResolvePlayerName()
        {
            string runtimeName = _playerNameProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(runtimeName)) return _defaultPlayerName;

            return runtimeName.Trim();
        }
    }
}
