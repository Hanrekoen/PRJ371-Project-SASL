using System;
using Convai.Domain.Models;
using Convai.Runtime.Components;
using Convai.Runtime.Facades;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Presentation.Events
{
    /// <summary>
    ///     Inspector-friendly relay backed by ConvaiManager.Transcripts.
    /// </summary>
    [AddComponentMenu("Convai/Events/Convai Transcript Event Relay")]
    [DisallowMultipleComponent]
    public sealed class ConvaiTranscriptEventRelay : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Optional explicit manager reference. If omitted, the relay can use ConvaiManager.ActiveManager.")]
        [SerializeField]
        private ConvaiManager _manager;

        [Tooltip("If enabled, the relay uses ConvaiManager.ActiveManager when no manager is assigned.")]
        [SerializeField]
        private bool _autoResolveManager = true;

        [Header("Filters")] [Tooltip("Only forward committed transcript turns.")] [SerializeField]
        private bool _finalOnly;

        [Tooltip("Ignore pure interim/listening updates. Stable and committed turns still pass through.")] [SerializeField]
        private bool _ignoreInterimUpdates = true;

        [Tooltip("Optional character ID filter for character transcript callbacks.")] [SerializeField]
        private string _characterIdFilter;

        [Header("Events")] [SerializeField]
        private UnityEvent<TranscriptUpdateRelayData> _onTranscriptReceived = new();

        [SerializeField]
        private UnityEvent<CharacterTranscriptRelayData> _onCharacterTranscriptReceived = new();

        [SerializeField] private UnityEvent<PlayerTranscriptRelayData> _onPlayerTranscriptReceived = new();

        [SerializeField] private UnityEvent<CharacterTranscriptRelayData> _onFinalCharacterTranscriptReceived = new();

        [SerializeField] private UnityEvent<PlayerTranscriptRelayData> _onFinalPlayerTranscriptReceived = new();

        private ConvaiTranscripts _boundTranscripts;
        private bool _loggedSubscribeRetry;
        private bool _loggedTargetWarning;
        private IDisposable _subscription;
        private ConvaiTranscripts _testTranscripts;

        public ConvaiManager Manager => _manager;
        public bool AutoResolveManager => _autoResolveManager;
        public bool FinalOnly => _finalOnly;
        public bool IgnoreInterimUpdates => _ignoreInterimUpdates;
        public string CharacterIdFilter => _characterIdFilter;
        public UnityEvent<TranscriptUpdateRelayData> OnTranscriptReceived => _onTranscriptReceived;
        public UnityEvent<CharacterTranscriptRelayData> OnCharacterTranscriptReceived => _onCharacterTranscriptReceived;
        public UnityEvent<PlayerTranscriptRelayData> OnPlayerTranscriptReceived => _onPlayerTranscriptReceived;

        public UnityEvent<CharacterTranscriptRelayData> OnFinalCharacterTranscriptReceived =>
            _onFinalCharacterTranscriptReceived;

        public UnityEvent<PlayerTranscriptRelayData> OnFinalPlayerTranscriptReceived =>
            _onFinalPlayerTranscriptReceived;

        private void LateUpdate()
        {
            TrySubscribe(false);
        }

        private void OnEnable()
        {
            _loggedTargetWarning = false;
            _loggedSubscribeRetry = false;
            TrySubscribe(true);
        }

        private void OnDisable() => Unsubscribe();

        internal void BindForTesting(ConvaiTranscripts transcripts)
        {
            Unsubscribe();
            _testTranscripts = transcripts;
            TrySubscribe(true);
        }

        internal void ConfigureForTesting(bool finalOnly, bool ignoreInterimUpdates, string characterIdFilter = null)
        {
            _finalOnly = finalOnly;
            _ignoreInterimUpdates = ignoreInterimUpdates;
            _characterIdFilter = characterIdFilter;
        }

        internal string GetConfigurationWarning()
        {
            if (_testTranscripts != null) return string.Empty;

            if (_manager == null && _autoResolveManager)
                return
                    "No explicit ConvaiManager assigned. The relay will use ConvaiManager.ActiveManager at runtime; assign a manager for deterministic scene wiring.";

            if (_manager == null && !_autoResolveManager)
                return "Assign a ConvaiManager or enable Auto Resolve Manager.";

            return string.Empty;
        }

        private void TrySubscribe(bool logFailures)
        {
            if (!TryResolveTranscripts(out ConvaiTranscripts transcripts))
            {
                Unsubscribe();
                if (logFailures) LogConfigurationWarning();
                return;
            }

            if (_subscription != null && ReferenceEquals(_boundTranscripts, transcripts)) return;

            Unsubscribe();

            var options = new TranscriptSubscriptionOptions
            {
                ReplayExisting = true
            };

            _boundTranscripts = transcripts;
            _subscription = transcripts.Subscribe(HandleChange, options);
            _loggedSubscribeRetry = false;
        }

        private bool TryResolveTranscripts(out ConvaiTranscripts transcripts)
        {
            if (_testTranscripts != null)
            {
                transcripts = _testTranscripts;
                return true;
            }

            ConvaiManager manager =
                _manager != null ? _manager : _autoResolveManager ? ConvaiManager.ActiveManager : null;
            if (manager == null)
            {
                transcripts = null;
                return false;
            }

            try
            {
                if (manager.TryGetTranscripts(out transcripts)) return true;
            }
            catch (InvalidOperationException) { }

            transcripts = null;
            if (!_loggedSubscribeRetry)
            {
                Debug.LogWarning(
                    $"[{nameof(ConvaiTranscriptEventRelay)}] ConvaiManager is present but not initialized yet. The relay will retry while enabled.",
                    this);
                _loggedSubscribeRetry = true;
            }

            return false;
        }

        private void Unsubscribe()
        {
            _subscription?.Dispose();
            _subscription = null;
            _boundTranscripts = null;
        }

        private void HandleTurn(TranscriptTurn turn, bool isFinal)
        {
            if (turn == null || !turn.HasText) return;
            if (_ignoreInterimUpdates &&
                (turn.State == TranscriptTurnState.Listening || turn.State == TranscriptTurnState.Streaming))
                return;

            if (turn.Speaker?.Type == TranscriptSpeakerType.Character)
                HandleCharacterTurn(turn, isFinal);
            else if (turn.Speaker?.Type == TranscriptSpeakerType.Player)
                HandlePlayerTurn(turn, isFinal);
        }

        private void HandleChange(TranscriptChange change)
        {
            if (_boundTranscripts == null || !_boundTranscripts.IsPresentationEnabled) return;
            if (change?.Turn == null) return;
            bool isFinal = change.Kind == TranscriptChangeKind.Committed ||
                           change.Kind == TranscriptChangeKind.Interrupted ||
                           change.Kind == TranscriptChangeKind.Corrected;
            if (_finalOnly && !isFinal) return;
            HandleTurn(change.Turn, isFinal);
        }

        private void HandleCharacterTurn(TranscriptTurn turn, bool isFinal)
        {
            if (!PassesCharacterFilter(turn.Speaker.Id)) return;

            TranscriptUpdate update = ToTranscriptUpdate(turn, SpeakerType.Character);
            var data = new CharacterTranscriptRelayData(
                turn.Speaker.Id,
                turn.Speaker.DisplayName,
                turn.DisplayText,
                isFinal,
                turn.Id,
                turn.MessageId,
                turn.ResponseId);

            _onTranscriptReceived?.Invoke(new TranscriptUpdateRelayData(update));
            _onCharacterTranscriptReceived?.Invoke(data);
            if (isFinal) _onFinalCharacterTranscriptReceived?.Invoke(data);
        }

        private void HandlePlayerTurn(TranscriptTurn turn, bool isFinal)
        {
            TranscriptUpdate update = ToTranscriptUpdate(turn, SpeakerType.Player);
            var data = new PlayerTranscriptRelayData(
                turn.Speaker.Id,
                turn.Speaker.DisplayName,
                turn.Speaker.Id,
                turn.Speaker.DisplayName,
                turn.Speaker.ParticipantId,
                turn.Id,
                turn.MessageId,
                turn.DisplayText,
                isFinal);

            _onTranscriptReceived?.Invoke(new TranscriptUpdateRelayData(update));
            _onPlayerTranscriptReceived?.Invoke(data);
            if (isFinal) _onFinalPlayerTranscriptReceived?.Invoke(data);
        }

        private static TranscriptUpdate ToTranscriptUpdate(TranscriptTurn turn, SpeakerType speakerType)
        {
            return new TranscriptUpdate(
                turn.MessageId,
                turn.Id,
                turn.ResponseId,
                speakerType,
                turn.Speaker.Id,
                turn.Speaker.DisplayName,
                turn.Speaker.ParticipantId,
                turn.DisplayText,
                turn.IsCommitted
                    ? TranscriptLifecycle.Completed
                    : turn.State == TranscriptTurnState.Streaming
                        ? TranscriptLifecycle.Streaming
                        : TranscriptLifecycle.Stable,
                ToSourceKind(turn.PrimaryTextSource),
                turn.LastUpdatedAtUtc);
        }

        internal static TranscriptSegmentSourceKind ToSourceKind(TranscriptTextSource source)
        {
            return source switch
            {
                TranscriptTextSource.InterimAsr or TranscriptTextSource.AsrFinal =>
                    TranscriptSegmentSourceKind.PlayerAsr,
                TranscriptTextSource.ProcessedFinal => TranscriptSegmentSourceKind.PlayerProcessedFinal,
                TranscriptTextSource.TypedText => TranscriptSegmentSourceKind.PlayerTypedText,
                TranscriptTextSource.BotPreview => TranscriptSegmentSourceKind.BotLlmPreview,
                TranscriptTextSource.BotOutput => TranscriptSegmentSourceKind.BotOutput,
                TranscriptTextSource.LegacyBotTranscript => TranscriptSegmentSourceKind.LegacyBotTranscript,
                _ => TranscriptSegmentSourceKind.Unknown
            };
        }

        private bool PassesCharacterFilter(string characterId) =>
            string.IsNullOrWhiteSpace(_characterIdFilter) ||
            string.Equals(_characterIdFilter, characterId, StringComparison.OrdinalIgnoreCase);

        private void LogConfigurationWarning()
        {
            if (_loggedTargetWarning) return;

            string warning = GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning)) return;

            Debug.LogWarning($"[{nameof(ConvaiTranscriptEventRelay)}] {warning}", this);
            _loggedTargetWarning = true;
        }
    }
}
