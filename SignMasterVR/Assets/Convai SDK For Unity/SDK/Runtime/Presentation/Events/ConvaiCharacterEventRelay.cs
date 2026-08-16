using Convai.Runtime.Components;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Presentation.Events
{
    /// <summary>
    ///     Inspector-friendly relay for local <see cref="ConvaiCharacter" /> callbacks.
    /// </summary>
    /// <remarks>
    ///     Use this when designers want to react to one character's local speech, emotion, or readiness callbacks.
    ///     For room-level orchestration and typed domain events, prefer <c>ConvaiManager.Events</c>.
    /// </remarks>
    [AddComponentMenu("Convai/Events/Convai Character Event Relay")]
    [DisallowMultipleComponent]
    public sealed class ConvaiCharacterEventRelay : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip(
            "Optional explicit character reference. If omitted, the relay can use a ConvaiCharacter on the same GameObject.")]
        [SerializeField]
        private ConvaiCharacter _character;

        [Tooltip("If enabled, the relay looks for a ConvaiCharacter on the same GameObject.")] [SerializeField]
        private bool _autoResolveCharacter = true;

        [Header("Events")] [SerializeField]
        private UnityEvent<CharacterTranscriptRelayData> _onTranscriptReceived = new();

        [SerializeField] private UnityEvent _onSpeechStarted = new();

        [SerializeField] private UnityEvent _onSpeechStopped = new();

        [SerializeField] private UnityEvent<CharacterTurnCompletedRelayData> _onTurnCompleted = new();

        [SerializeField] private UnityEvent _onCharacterReady = new();

        [SerializeField] private UnityEvent<CharacterEmotionRelayData> _onEmotionChanged = new();

        private ConvaiCharacter _boundCharacter;
        private bool _loggedTargetWarning;
        private bool _subscribed;

        public ConvaiCharacter Character => _character;
        public bool AutoResolveCharacter => _autoResolveCharacter;
        public UnityEvent<CharacterTranscriptRelayData> OnTranscriptReceived => _onTranscriptReceived;
        public UnityEvent OnSpeechStarted => _onSpeechStarted;
        public UnityEvent OnSpeechStopped => _onSpeechStopped;
        public UnityEvent<CharacterTurnCompletedRelayData> OnTurnCompleted => _onTurnCompleted;
        public UnityEvent OnCharacterReady => _onCharacterReady;
        public UnityEvent<CharacterEmotionRelayData> OnEmotionChanged => _onEmotionChanged;

        private void OnEnable()
        {
            _loggedTargetWarning = false;
            TrySubscribe(true);
        }

        private void OnDisable() => Unsubscribe();

        internal void BindForTesting(ConvaiCharacter character)
        {
            Unsubscribe();
            _character = character;
            TrySubscribe(true);
        }

        internal string GetConfigurationWarning()
        {
            if (_character == null && _autoResolveCharacter && GetComponent<ConvaiCharacter>() == null)
                return "No ConvaiCharacter found on this GameObject. Assign one or disable Auto Resolve Character.";

            if (_character == null && !_autoResolveCharacter)
                return "Assign a ConvaiCharacter or enable Auto Resolve Character.";

            return string.Empty;
        }

        private void TrySubscribe(bool logFailures)
        {
            if (_subscribed) return;

            ConvaiCharacter character = _character != null ? _character :
                _autoResolveCharacter ? GetComponent<ConvaiCharacter>() : null;
            if (character == null)
            {
                if (logFailures) LogConfigurationWarning();
                return;
            }

            character.OnTranscriptReceived += HandleTranscriptReceived;
            character.OnSpeechStarted += HandleSpeechStarted;
            character.OnSpeechStopped += HandleSpeechStopped;
            character.OnTurnCompleted += HandleTurnCompleted;
            character.OnCharacterReady += HandleCharacterReady;
            character.OnEmotionChanged += HandleEmotionChanged;

            _boundCharacter = character;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _boundCharacter == null) return;

            _boundCharacter.OnTranscriptReceived -= HandleTranscriptReceived;
            _boundCharacter.OnSpeechStarted -= HandleSpeechStarted;
            _boundCharacter.OnSpeechStopped -= HandleSpeechStopped;
            _boundCharacter.OnTurnCompleted -= HandleTurnCompleted;
            _boundCharacter.OnCharacterReady -= HandleCharacterReady;
            _boundCharacter.OnEmotionChanged -= HandleEmotionChanged;

            _boundCharacter = null;
            _subscribed = false;
        }

        private void HandleTranscriptReceived(string text, bool isFinal)
        {
            _onTranscriptReceived?.Invoke(new CharacterTranscriptRelayData(
                GetCharacterId(),
                GetCharacterName(),
                text,
                isFinal));
        }

        private void HandleSpeechStarted() => _onSpeechStarted?.Invoke();

        private void HandleSpeechStopped() => _onSpeechStopped?.Invoke();

        private void HandleTurnCompleted(bool wasInterrupted) =>
            _onTurnCompleted?.Invoke(new CharacterTurnCompletedRelayData(
                GetCharacterId(),
                GetCharacterName(),
                wasInterrupted));

        private void HandleCharacterReady() => _onCharacterReady?.Invoke();

        private void HandleEmotionChanged(string emotion, int intensity) =>
            _onEmotionChanged?.Invoke(new CharacterEmotionRelayData(
                GetCharacterId(),
                GetCharacterName(),
                emotion,
                intensity));

        private string GetCharacterId() => _boundCharacter != null ? _boundCharacter.CharacterId : string.Empty;

        private string GetCharacterName()
        {
            if (_boundCharacter == null) return string.Empty;
            return string.IsNullOrWhiteSpace(_boundCharacter.CharacterName)
                ? _boundCharacter.gameObject.name
                : _boundCharacter.CharacterName;
        }

        private void LogConfigurationWarning()
        {
            if (_loggedTargetWarning) return;

            string warning = GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning)) return;

            Debug.LogWarning($"[{nameof(ConvaiCharacterEventRelay)}] {warning}", this);
            _loggedTargetWarning = true;
        }
    }
}
