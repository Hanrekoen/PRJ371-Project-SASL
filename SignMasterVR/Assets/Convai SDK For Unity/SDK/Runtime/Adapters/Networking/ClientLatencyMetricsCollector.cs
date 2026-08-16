#nullable enable
using System;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Logging;
using Convai.Runtime.Networking.Media;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Collects client-side latency timestamps for the conversation pipeline when debug mode is enabled.
    ///     Subscribes to EventHub and records: player stop → final transcript → UI; character first transcript,
    ///     TTS started, first lip sync, audio playing. Logs segment deltas (ms) at end of each character turn.
    /// </summary>
    /// <remarks>
    ///     See Documentation~/CLIENT_LATENCY_METRICS_DESIGN.md for segment definitions and test usage.
    /// </remarks>
    internal sealed class ClientLatencyMetricsCollector : IDisposable
    {
        private readonly IDebugMetricsFileWriter? _fileWriter;
        private readonly Func<bool> _isEnabled;
        private readonly object _lock = new();
        private readonly ILogger? _logger;

        private SubscriptionToken? _audioPlaybackToken;
        private DateTime? _characterAudioPlayingAtUtc;
        private DateTime? _characterFirstLipSyncAtUtc;
        private DateTime? _characterFirstTranscriptAtUtc;
        private SubscriptionToken? _characterSpeechToken;
        private SubscriptionToken? _characterTranscriptToken;
        private DateTime? _characterTtsStartedAtUtc;
        private bool _disposed;
        private IEventHub? _eventHub;
        private SubscriptionToken? _lipSyncToken;
        private DateTime? _playerFinalTranscriptAtUtc;
        private SubscriptionToken? _playerSpeakingToken;
        private DateTime? _playerStoppedSpeakingAtUtc;
        private SubscriptionToken? _playerTranscriptToken;
        private long? _clientSpeechCandidateTicks;

        public ClientLatencyMetricsCollector(
            IEventHub eventHub,
            Func<bool> isEnabled,
            ILogger? logger = null,
            IDebugMetricsFileWriter? fileWriter = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            _logger = logger.WithTag(nameof(ClientLatencyMetricsCollector));
            _fileWriter = fileWriter;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unbind();
            _eventHub = null;
        }

        /// <summary>
        ///     Subscribes to transcript, speech, lip sync, and audio playback events. Call when room is composed.
        /// </summary>
        public void Bind()
        {
            if (_eventHub == null || _disposed) return;

            _playerSpeakingToken =
                _eventHub.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChangedSafe);
            _playerTranscriptToken = _eventHub.Subscribe<PlayerTranscriptReceived>(HandlePlayerTranscriptReceivedSafe);
            _characterTranscriptToken =
                _eventHub.Subscribe<CharacterTranscriptReceived>(HandleCharacterTranscriptReceivedSafe);
            _characterSpeechToken =
                _eventHub.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechStateChangedSafe);
            _lipSyncToken = _eventHub.Subscribe<LipSyncPackedDataReceived>(HandleLipSyncPackedDataReceivedSafe);
            _audioPlaybackToken = _eventHub.Subscribe<CharacterAudioPlaybackStateChanged>(
                HandleCharacterAudioPlaybackStateChangedSafe);
        }

        /// <summary>
        ///     Unsubscribes from all events. Call when room composition is disposed.
        /// </summary>
        public void Unbind()
        {
            if (_eventHub == null) return;

            if (_playerSpeakingToken.HasValue)
            {
                _eventHub.Unsubscribe(_playerSpeakingToken.Value);
                _playerSpeakingToken = null;
            }

            if (_playerTranscriptToken.HasValue)
            {
                _eventHub.Unsubscribe(_playerTranscriptToken.Value);
                _playerTranscriptToken = null;
            }

            if (_characterTranscriptToken.HasValue)
            {
                _eventHub.Unsubscribe(_characterTranscriptToken.Value);
                _characterTranscriptToken = null;
            }

            if (_characterSpeechToken.HasValue)
            {
                _eventHub.Unsubscribe(_characterSpeechToken.Value);
                _characterSpeechToken = null;
            }

            if (_lipSyncToken.HasValue)
            {
                _eventHub.Unsubscribe(_lipSyncToken.Value);
                _lipSyncToken = null;
            }

            if (_audioPlaybackToken.HasValue)
            {
                _eventHub.Unsubscribe(_audioPlaybackToken.Value);
                _audioPlaybackToken = null;
            }
        }

        internal void RecordBargeInMarker(BargeInMarker marker)
        {
            if (!_isEnabled()) return;

            lock (_lock)
            {
                if (marker.Stage == BargeInMarkerStage.ClientSpeechCandidate)
                    _clientSpeechCandidateTicks = marker.TimestampTicks;

                double? sinceCandidateMs = _clientSpeechCandidateTicks.HasValue
                    ? (marker.TimestampTicks - _clientSpeechCandidateTicks.Value) * 1000d /
                      System.Diagnostics.Stopwatch.Frequency
                    : null;
                string payload =
                    $"stage={marker.Stage} trigger={marker.Trigger} affectedStreams={marker.AffectedStreams}" +
                    (sinceCandidateMs.HasValue ? $" sinceCandidate_ms={sinceCandidateMs.Value:F1}" : string.Empty);
                _fileWriter?.WriteLine("client_barge_in", payload);
                _logger?.Debug($"[ClientBargeIn] {payload}", LogCategory.Audio);

                if (marker.Stage is BargeInMarkerStage.PlaybackRestored or BargeInMarkerStage.DuckCancelled)
                    _clientSpeechCandidateTicks = null;
            }
        }

        private void HandlePlayerSpeakingStateChangedSafe(PlayerSpeakingStateChanged e)
        {
            try
            {
                OnPlayerSpeakingStateChanged(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling PlayerSpeakingStateChanged: isSpeaking={e.IsSpeaking}. {ex}",
                    LogCategory.Events);
            }
        }

        private void HandlePlayerTranscriptReceivedSafe(PlayerTranscriptReceived e)
        {
            try
            {
                OnPlayerTranscriptReceived(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling PlayerTranscriptReceived: phase={e.Phase}, turnId={e.TurnId}. {ex}",
                    LogCategory.Events);
            }
        }

        private void HandleCharacterTranscriptReceivedSafe(CharacterTranscriptReceived e)
        {
            try
            {
                OnCharacterTranscriptReceived(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling CharacterTranscriptReceived: characterId='{e.CharacterId}', participantId='{e.Message.ParticipantId}'. {ex}",
                    LogCategory.Events);
            }
        }

        private void HandleCharacterSpeechStateChangedSafe(CharacterSpeechStateChanged e)
        {
            try
            {
                OnCharacterSpeechStateChanged(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling CharacterSpeechStateChanged: characterId='{e.CharacterId}', isSpeaking={e.IsSpeaking}. {ex}",
                    LogCategory.Events);
            }
        }

        private void HandleLipSyncPackedDataReceivedSafe(LipSyncPackedDataReceived e)
        {
            try
            {
                OnLipSyncPackedDataReceived(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling LipSyncPackedDataReceived: characterId='{e.CharacterId}', valid={e.IsValid}. {ex}",
                    LogCategory.Events);
            }
        }

        private void HandleCharacterAudioPlaybackStateChangedSafe(CharacterAudioPlaybackStateChanged e)
        {
            try
            {
                OnCharacterAudioPlaybackStateChanged(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Failed handling CharacterAudioPlaybackStateChanged: characterId='{e.CharacterId}', isPlaying={e.IsPlaying}. {ex}",
                    LogCategory.Events);
            }
        }

        private void OnPlayerSpeakingStateChanged(PlayerSpeakingStateChanged e)
        {
            if (!_isEnabled()) return;
            if (!e.IsEndOfSpeech) return;

            lock (_lock)
            {
                _playerStoppedSpeakingAtUtc = DateTime.UtcNow;
                _playerFinalTranscriptAtUtc = null;
                _characterFirstTranscriptAtUtc = null;
                _characterTtsStartedAtUtc = null;
                _characterFirstLipSyncAtUtc = null;
                _characterAudioPlayingAtUtc = null;
                _fileWriter?.WriteLine("client_player_stopped", "{}");
            }
        }

        private void OnPlayerTranscriptReceived(PlayerTranscriptReceived e)
        {
            if (!_isEnabled()) return;
            if (e.Phase != TranscriptionPhase.ProcessedFinal) return;

            lock (_lock)
            {
                _playerFinalTranscriptAtUtc = DateTime.UtcNow;
                _fileWriter?.WriteLine("client_player_final_transcript", "{}");
            }
        }

        private void OnCharacterTranscriptReceived(CharacterTranscriptReceived e)
        {
            if (!_isEnabled()) return;

            lock (_lock)
            {
                if (_characterFirstTranscriptAtUtc.HasValue) return;
                _characterFirstTranscriptAtUtc = DateTime.UtcNow;
                _fileWriter?.WriteLine("client_character_first_transcript", "{}");
            }
        }

        private void OnCharacterSpeechStateChanged(CharacterSpeechStateChanged e)
        {
            if (!_isEnabled()) return;

            lock (_lock)
            {
                if (e.IsSpeaking)
                {
                    if (!_characterTtsStartedAtUtc.HasValue)
                    {
                        _characterTtsStartedAtUtc = DateTime.UtcNow;
                        _fileWriter?.WriteLine("client_character_tts_started", "{}");
                    }
                }
                else
                {
                    LogTurnSummary();
                    ResetCharacterTimestamps();
                    _fileWriter?.WriteLine("client_character_stopped", "{}");
                }
            }
        }

        private void OnLipSyncPackedDataReceived(LipSyncPackedDataReceived e)
        {
            if (!_isEnabled() || !e.IsValid) return;

            lock (_lock)
            {
                if (_characterFirstLipSyncAtUtc.HasValue) return;
                _characterFirstLipSyncAtUtc = DateTime.UtcNow;
                _fileWriter?.WriteLine("client_character_first_lipsync", "{}");
            }
        }

        private void OnCharacterAudioPlaybackStateChanged(CharacterAudioPlaybackStateChanged e)
        {
            if (!_isEnabled() || !e.IsPlaying) return;

            lock (_lock)
            {
                if (_characterAudioPlayingAtUtc.HasValue) return;
                _characterAudioPlayingAtUtc = DateTime.UtcNow;
                _fileWriter?.WriteLine("client_character_audio_playing", "{}");
            }
        }

        private void LogTurnSummary()
        {
            DateTime? t0 = _playerStoppedSpeakingAtUtc;
            if (!t0.HasValue) return;

            double? stopToFinalPlayerMs = _playerFinalTranscriptAtUtc.HasValue
                ? (_playerFinalTranscriptAtUtc.Value - t0.Value).TotalMilliseconds
                : null;
            double? stopToFirstCharTranscriptMs = _characterFirstTranscriptAtUtc.HasValue
                ? (_characterFirstTranscriptAtUtc.Value - t0.Value).TotalMilliseconds
                : null;
            double? stopToTtsStartedMs = _characterTtsStartedAtUtc.HasValue
                ? (_characterTtsStartedAtUtc.Value - t0.Value).TotalMilliseconds
                : null;
            double? stopToFirstLipSyncMs = _characterFirstLipSyncAtUtc.HasValue
                ? (_characterFirstLipSyncAtUtc.Value - t0.Value).TotalMilliseconds
                : null;
            double? stopToAudioPlayingMs = _characterAudioPlayingAtUtc.HasValue
                ? (_characterAudioPlayingAtUtc.Value - t0.Value).TotalMilliseconds
                : null;
            double? audioHoldMs = null;
            if (_characterTtsStartedAtUtc.HasValue && _characterAudioPlayingAtUtc.HasValue)
                audioHoldMs = (_characterAudioPlayingAtUtc.Value - _characterTtsStartedAtUtc.Value).TotalMilliseconds;

            string playerPart = stopToFinalPlayerMs.HasValue
                ? $"Player: stop→finalTranscript={stopToFinalPlayerMs.Value:F0}ms"
                : "Player: (no final transcript this turn)";
            string charPart = "Character: ";
            if (stopToFirstCharTranscriptMs.HasValue)
                charPart += $"stop→firstTranscript={stopToFirstCharTranscriptMs.Value:F0}ms ";
            if (stopToTtsStartedMs.HasValue) charPart += $"stop→ttsStarted={stopToTtsStartedMs.Value:F0}ms ";
            if (stopToFirstLipSyncMs.HasValue) charPart += $"stop→firstLipSync={stopToFirstLipSyncMs.Value:F0}ms ";
            if (stopToAudioPlayingMs.HasValue) charPart += $"stop→audioPlaying={stopToAudioPlayingMs.Value:F0}ms ";
            if (audioHoldMs.HasValue) charPart += $"(audioHoldForLipSync={audioHoldMs.Value:F0}ms)";

            string summaryLine = $"{playerPart} | {charPart.TrimEnd()}";
            _logger?.Info($"[ClientLatency] {summaryLine}");
            ConvaiLogger.Info($"[ClientLatency] {summaryLine}", LogCategory.SDK);

            string payload = string.Join(" ",
                stopToFinalPlayerMs.HasValue ? $"stopToFinalPlayer_ms={stopToFinalPlayerMs.Value:F0}" : "",
                stopToFirstCharTranscriptMs.HasValue
                    ? $"stopToFirstCharTranscript_ms={stopToFirstCharTranscriptMs.Value:F0}"
                    : "",
                stopToTtsStartedMs.HasValue ? $"stopToTtsStarted_ms={stopToTtsStartedMs.Value:F0}" : "",
                stopToFirstLipSyncMs.HasValue ? $"stopToFirstLipSync_ms={stopToFirstLipSyncMs.Value:F0}" : "",
                stopToAudioPlayingMs.HasValue ? $"stopToAudioPlaying_ms={stopToAudioPlayingMs.Value:F0}" : "",
                audioHoldMs.HasValue ? $"audioHoldForLipSync_ms={audioHoldMs.Value:F0}" : "").Trim();
            _fileWriter?.WriteLine("client_latency_summary", payload);
        }

        private void ResetCharacterTimestamps()
        {
            _characterFirstTranscriptAtUtc = null;
            _characterTtsStartedAtUtc = null;
            _characterFirstLipSyncAtUtc = null;
            _characterAudioPlayingAtUtc = null;
        }
    }
}
