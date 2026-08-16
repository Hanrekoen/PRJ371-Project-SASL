using System;
using System.Diagnostics;
using System.Threading;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Protocol.Messages;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Owns local player speech-cycle, transcription-session, and typed-echo protocol state.
    /// </summary>
    internal sealed class PlayerConversationInput
    {
        private const int ProcessedFinalGracePeriodMs = 200;
        private const double TypedEchoSuppressionWindowSeconds = 3d;

        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly IConvaiPlayerEvents _playerEvents;
        private readonly object _stateLock = new();
        private readonly Func<DateTime> _utcNow;

        private string _asrFinalText = string.Empty;
        private bool _completionDispatched;
        private bool _isPlayerSpeaking;
        private PendingTypedEcho _pendingTypedEcho;
        private Timer _processedFinalGraceTimer;
        private string _processedFinalText = string.Empty;
        private bool _receivedAsrFinal;
        private bool _receivedProcessedFinal;
        private bool _sessionActive;
        private string _sessionId = string.Empty;
        private bool _stopPending;
        private bool _suppressPlayerSpeechCycle;
        private DateTime _suppressPlayerSpeechCycleStartedUtc;

        public PlayerConversationInput(
            IConvaiPlayerEvents playerEvents,
            IMainThreadDispatcher dispatcher,
            IEventHub eventHub = null,
            ILogger logger = null,
            Func<DateTime> utcNow = null)
        {
            _playerEvents = playerEvents ?? throw new ArgumentNullException(nameof(playerEvents));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _eventHub = eventHub;
            _logger = logger.WithTag(nameof(PlayerConversationInput));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public SpeakerInfo CurrentSpeakerInfo { get; private set; } = SpeakerInfo.Empty;

        public string CurrentSessionId
        {
            get
            {
                lock (_stateLock) return _sessionId;
            }
        }

        public void RegisterTypedText(string messageId, string text)
        {
            string normalizedText = NormalizeTranscriptText(text);
            if (string.IsNullOrWhiteSpace(messageId) || normalizedText.Length == 0)
                return;

            lock (_stateLock)
            {
                _pendingTypedEcho = new PendingTypedEcho(messageId, normalizedText, _utcNow());
            }
        }

        public void HandleStartedSpeaking()
        {
            bool suppressTypedEcho;

            lock (_stateLock)
            {
                ExpireStaleTypedEchoSuppressionLocked();

                if (_isPlayerSpeaking)
                {
                    _logger.Debug("Ignoring duplicate user-started-speaking event.", LogCategory.Player);
                    return;
                }

                _isPlayerSpeaking = true;
                suppressTypedEcho = HasRecentPendingTypedEchoLocked();
                _suppressPlayerSpeechCycle = suppressTypedEcho;
                _suppressPlayerSpeechCycleStartedUtc = suppressTypedEcho ? _utcNow() : DateTime.MinValue;
            }

            if (suppressTypedEcho)
            {
                _logger.Debug("Suppressing typed text echo user-started-speaking event.", LogCategory.Player);
                return;
            }

            _logger.Info("Player started speaking", LogCategory.Player);
            HandleStart();
            _eventHub?.Publish(PlayerSpeakingStateChanged.StartedSpeaking(CurrentSessionId));
        }

        public void HandleStoppedSpeaking()
        {
            bool suppressTypedEcho;
            bool wasSpeaking;
            bool hasActiveSession;

            lock (_stateLock)
            {
                ExpireStaleTypedEchoSuppressionLocked();

                wasSpeaking = _isPlayerSpeaking;
                hasActiveSession = !string.IsNullOrEmpty(_sessionId);
                suppressTypedEcho = _suppressPlayerSpeechCycle;
                _isPlayerSpeaking = false;
                if (suppressTypedEcho)
                    ClearPlayerSpeechCycleSuppressionLocked();
            }

            if (suppressTypedEcho)
            {
                _logger.Debug("Suppressing typed text echo user-stopped-speaking event.", LogCategory.Player);
                return;
            }

            if (!wasSpeaking && !hasActiveSession)
            {
                _logger.Debug(
                    "Ignoring user-stopped-speaking without an active speech session.",
                    LogCategory.Player);
                return;
            }

            _logger.Info("Player stopped speaking", LogCategory.Player);
            string sessionId = CurrentSessionId;
            HandleStop();
            _eventHub?.Publish(PlayerSpeakingStateChanged.StoppedSpeaking(sessionId));
        }

        public void HandleTranscription(UserTranscriptionPayload payload)
        {
            if (payload == null) return;

            string text = payload.Text ?? string.Empty;
            string normalizedText = NormalizeTranscriptText(text);
            string transcriptionType = payload.IsFinal ? "final" : "interim";

            bool suppressTypedEcho;
            bool startRealSpeechCycle;
            bool inferSpeechStart;
            lock (_stateLock)
            {
                ExpireStaleTypedEchoSuppressionLocked();
                suppressTypedEcho = TrySuppressTypedEchoTranscriptionLocked(normalizedText, out startRealSpeechCycle);
                inferSpeechStart = !suppressTypedEcho &&
                                   !payload.IsFinal &&
                                   !_isPlayerSpeaking;
                if (inferSpeechStart)
                    _isPlayerSpeaking = true;
            }

            if (suppressTypedEcho)
            {
                _logger.Debug(
                    $"Suppressing typed text echo player transcription ({transcriptionType}): {text}",
                    LogCategory.Player);
                return;
            }

            // Some backend turn strategies emit another interim after a VAD pause without
            // emitting a matching user-started-speaking packet. An interim is positive evidence
            // that speech resumed; restore the acoustic cycle before publishing transcript data.
            if (startRealSpeechCycle || inferSpeechStart)
            {
                _logger.Info("Player started speaking", LogCategory.Player);
                HandleStart();
                _eventHub?.Publish(PlayerSpeakingStateChanged.StartedSpeaking(CurrentSessionId));
            }

            _logger.Info($"Player transcription ({transcriptionType}): {text}", LogCategory.Player);

            if (payload.IsFinal)
                HandleAsrFinal(text);
            else
                HandleInterim(text);
        }

        public void HandleProcessedFinal(FinalUserTranscriptionPayload payload)
        {
            if (payload == null) return;

            string cleanedText = payload.Text ?? string.Empty;
            var speakerInfo = new SpeakerInfo(payload.SpeakerId, payload.SpeakerName, payload.ParticipantId);
            string messageId = ResolveFinalUserTranscriptionMessageId(payload.MessageId, cleanedText);
            bool routeThroughTypedSink = !string.IsNullOrWhiteSpace(messageId) &&
                                         _playerEvents is IPlayerTypedTranscriptSink;

            _eventHub?.Publish(FinalUserTranscriptionReceived.Create(cleanedText, speakerInfo, messageId));
            AcknowledgePendingTypedText(messageId);

            if (routeThroughTypedSink)
                ((IPlayerTypedTranscriptSink)_playerEvents).PublishTypedText(cleanedText, messageId, speakerInfo);
            else
                HandleProcessedFinal(cleanedText, speakerInfo);

            string speakerDisplay = !string.IsNullOrEmpty(payload.SpeakerName)
                ? $" (speaker: {payload.SpeakerName})"
                : string.Empty;
            _logger.Debug(
                $"Received final player transcription: {cleanedText}{speakerDisplay}",
                LogCategory.Player);
        }

        public bool ShouldSuppressMirroredCharacterText(string text)
        {
            string normalizedText = NormalizeTranscriptText(text);
            if (normalizedText.Length == 0)
                return false;

            lock (_stateLock)
            {
                ExpireStaleTypedEchoSuppressionLocked();
                if (!HasRecentPendingTypedEchoLocked())
                    return false;

                if (IsMatchingPendingTypedEchoLocked(normalizedText))
                    return true;

                ClearTypedEchoSuppressionLocked();
                return false;
            }
        }

        public void HandleStart()
        {
            bool shouldCompletePrevious;
            lock (_stateLock)
            {
                if (_sessionActive && !_stopPending) return;
                shouldCompletePrevious = _stopPending && HasAnyFinalLocked();
            }

            if (shouldCompletePrevious) CompleteSession();
            StartNewSession();
        }

        public void HandleInterim(string interimText)
        {
            EnsureSession();

            string safeText = interimText ?? string.Empty;
            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(safeText, TranscriptionPhase.Interim));
        }

        public void HandleAsrFinal(string finalText)
        {
            EnsureSession();

            lock (_stateLock)
            {
                _asrFinalText = finalText ?? string.Empty;
                _receivedAsrFinal = _asrFinalText.Length > 0;
            }

            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(_asrFinalText, TranscriptionPhase.AsrFinal));
        }

        public void HandleProcessedFinal(string cleanedText, SpeakerInfo speakerInfo)
        {
            bool ignoreOrphanedProcessedFinal;

            lock (_stateLock)
            {
                ignoreOrphanedProcessedFinal = string.IsNullOrEmpty(_sessionId);
                if (!ignoreOrphanedProcessedFinal)
                {
                    _processedFinalText = cleanedText ?? string.Empty;
                    _receivedProcessedFinal = _processedFinalText.Length > 0;
                    CurrentSpeakerInfo = speakerInfo;
                    CancelGraceTimerLocked();
                }
            }

            if (ignoreOrphanedProcessedFinal)
            {
                _logger?.Debug(
                    $"Publishing late processed final without restarting a speaking session: \"{cleanedText}\"",
                    LogCategory.Player);
                string lateText = cleanedText ?? string.Empty;
                Dispatch(() =>
                    _playerEvents.OnPlayerTranscriptionReceived(
                        lateText,
                        TranscriptionPhase.ProcessedFinal,
                        speakerInfo));

                return;
            }

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(_processedFinalText, TranscriptionPhase.ProcessedFinal,
                    speakerInfo));

            bool shouldComplete;
            lock (_stateLock) shouldComplete = _stopPending;
            if (shouldComplete) CompleteSession();
        }

        public void HandleProcessedFinal(string cleanedText) => HandleProcessedFinal(cleanedText, SpeakerInfo.Empty);

        public void HandleStop()
        {
            string sessionId;
            bool completeImmediately;
            bool dispatchStopOnly;
            bool scheduleGraceTimer;

            lock (_stateLock)
            {
                if (string.IsNullOrEmpty(_sessionId)) return;

                sessionId = _sessionId;
                _stopPending = true;
                _sessionActive = false;

                completeImmediately = _receivedProcessedFinal;
                scheduleGraceTimer = !_receivedProcessedFinal && _receivedAsrFinal;
                dispatchStopOnly = !_receivedProcessedFinal && !_receivedAsrFinal;

                if (completeImmediately || dispatchStopOnly) CancelGraceTimerLocked();
                if (scheduleGraceTimer) ScheduleGraceTimerLocked(sessionId);

                if (dispatchStopOnly) ResetStateLocked();
            }

            if (dispatchStopOnly)
            {
                Dispatch(() => _playerEvents.OnPlayerStoppedSpeaking(sessionId, false));
                return;
            }

            if (completeImmediately) CompleteSession();
        }

        public void Reset()
        {
            lock (_stateLock)
            {
                CancelGraceTimerLocked();
                ResetStateLocked();
            }
        }

        private void StartNewSession()
        {
            string newSessionId;

            lock (_stateLock)
            {
                CancelGraceTimerLocked();
                ResetStateLocked();
                _sessionId = Guid.NewGuid().ToString("N");
                _sessionActive = true;
                newSessionId = _sessionId;
            }

            Dispatch(() => _playerEvents.OnPlayerStartedSpeaking(newSessionId));
            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Listening));
        }

        private void EnsureSession()
        {
            lock (_stateLock)
                if (!string.IsNullOrEmpty(_sessionId))
                    return;

            StartNewSession();
        }

        private void CompleteSession()
        {
            string sessionId;
            bool producedFinal;
            SpeakerInfo speakerInfo;

            lock (_stateLock)
            {
                if (_completionDispatched || string.IsNullOrEmpty(_sessionId))
                {
                    ResetStateLocked();
                    return;
                }

                sessionId = _sessionId;
                producedFinal = _receivedAsrFinal || _receivedProcessedFinal;
                speakerInfo = CurrentSpeakerInfo;
                _completionDispatched = true;

                CancelGraceTimerLocked();
                ResetStateLocked();
            }

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Completed, speakerInfo));
            Dispatch(() => _playerEvents.OnPlayerStoppedSpeaking(sessionId, producedFinal));
        }

        private void ScheduleGraceTimerLocked(string sessionId)
        {
            CancelGraceTimerLocked();
            _processedFinalGraceTimer = new Timer(_ => OnProcessedFinalGraceExpired(sessionId), null,
                ProcessedFinalGracePeriodMs, Timeout.Infinite);
        }

        private void OnProcessedFinalGraceExpired(string sessionId)
        {
            bool shouldComplete;

            lock (_stateLock)
            {
                shouldComplete = !_completionDispatched &&
                                 _stopPending &&
                                 _receivedAsrFinal &&
                                 !_receivedProcessedFinal &&
                                 _sessionId == sessionId;
            }

            if (shouldComplete) CompleteSession();
        }

        private void AcknowledgePendingTypedText(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
                return;

            lock (_stateLock)
            {
                if (string.Equals(_pendingTypedEcho.MessageId, messageId, StringComparison.Ordinal))
                    ClearPlayerSpeechCycleSuppressionLocked();
            }
        }

        private string ResolveFinalUserTranscriptionMessageId(string messageId, string text)
        {
            if (!string.IsNullOrWhiteSpace(messageId))
                return messageId;

            string normalizedText = NormalizeTranscriptText(text);
            if (normalizedText.Length == 0)
                return string.Empty;

            lock (_stateLock)
            {
                ExpireStaleTypedEchoSuppressionLocked();
                return IsMatchingPendingTypedEchoLocked(normalizedText)
                    ? _pendingTypedEcho.MessageId
                    : string.Empty;
            }
        }

        private static string NormalizeTranscriptText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();
        }

        private void ExpireStaleTypedEchoSuppressionLocked()
        {
            DateTime now = _utcNow();
            if (_pendingTypedEcho.RegisteredAtUtc != DateTime.MinValue &&
                (now - _pendingTypedEcho.RegisteredAtUtc).TotalSeconds > TypedEchoSuppressionWindowSeconds)
                _pendingTypedEcho = default;

            if (_suppressPlayerSpeechCycle &&
                _suppressPlayerSpeechCycleStartedUtc != DateTime.MinValue &&
                (now - _suppressPlayerSpeechCycleStartedUtc).TotalSeconds > TypedEchoSuppressionWindowSeconds)
            {
                _isPlayerSpeaking = false;
                ClearPlayerSpeechCycleSuppressionLocked();
            }
        }

        private bool HasRecentPendingTypedEchoLocked() =>
            _pendingTypedEcho.RegisteredAtUtc != DateTime.MinValue &&
            !string.IsNullOrEmpty(_pendingTypedEcho.Text);

        private bool TrySuppressTypedEchoTranscriptionLocked(string normalizedText, out bool startRealSpeechCycle)
        {
            startRealSpeechCycle = false;

            if (normalizedText.Length == 0)
                return false;

            if (_suppressPlayerSpeechCycle)
            {
                if (IsMatchingPendingTypedEchoLocked(normalizedText))
                    return true;

                ClearTypedEchoSuppressionLocked();
                startRealSpeechCycle = _isPlayerSpeaking;
                return false;
            }

            if (!IsMatchingPendingTypedEchoLocked(normalizedText))
                return false;

            _isPlayerSpeaking = true;
            _suppressPlayerSpeechCycle = true;
            _suppressPlayerSpeechCycleStartedUtc = _utcNow();
            return true;
        }

        private bool IsMatchingPendingTypedEchoLocked(string normalizedText) =>
            HasRecentPendingTypedEchoLocked() &&
            string.Equals(_pendingTypedEcho.Text, normalizedText, StringComparison.Ordinal);

        private void ClearTypedEchoSuppressionLocked()
        {
            _pendingTypedEcho = default;
            ClearPlayerSpeechCycleSuppressionLocked();
        }

        private void ClearPlayerSpeechCycleSuppressionLocked()
        {
            _suppressPlayerSpeechCycle = false;
            _suppressPlayerSpeechCycleStartedUtc = DateTime.MinValue;
        }

        private bool HasAnyFinalLocked() => _receivedAsrFinal || _receivedProcessedFinal;

        private void CancelGraceTimerLocked()
        {
            _processedFinalGraceTimer?.Dispose();
            _processedFinalGraceTimer = null;
        }

        private void ResetStateLocked()
        {
            _sessionId = string.Empty;
            _sessionActive = false;
            _receivedAsrFinal = false;
            _receivedProcessedFinal = false;
            _stopPending = false;
            _completionDispatched = false;
            _asrFinalText = string.Empty;
            _processedFinalText = string.Empty;
            CurrentSpeakerInfo = SpeakerInfo.Empty;
        }

        private void Dispatch(Action action)
        {
            void SafeInvoke()
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlayerConversationInput] Exception in dispatch callback: {ex}");
                }
            }

            if (!_dispatcher.TryDispatch(SafeInvoke))
                _logger.Warning("Failed to enqueue work on main thread dispatcher.", LogCategory.Transport);
        }

        private readonly struct PendingTypedEcho
        {
            public PendingTypedEcho(string messageId, string text, DateTime registeredAtUtc)
            {
                MessageId = messageId;
                Text = text;
                RegisteredAtUtc = registeredAtUtc;
            }

            public string MessageId { get; }
            public string Text { get; }
            public DateTime RegisteredAtUtc { get; }
        }
    }
}
