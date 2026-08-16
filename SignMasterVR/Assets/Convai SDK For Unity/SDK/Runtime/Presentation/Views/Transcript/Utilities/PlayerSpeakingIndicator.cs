using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Controls the visual appearance of a microphone indicator based on player speaking state.
    ///     Subscribes to PlayerSpeakingStateChanged events via EventHub for decoupled communication.
    /// </summary>
    /// <remarks>
    ///     This component follows the SDK's event-driven architecture pattern.
    ///     When the player speaks, the indicator shows full alpha; when silent, alpha is reduced.
    ///     Usage:
    ///     1. Add this component to a GameObject with an Image component
    ///     2. Assign the microphoneIndicator reference (or it will auto-detect)
    ///     3. Optionally customize speakingAlpha and silentAlpha values
    ///     The component will automatically:
    ///     - Subscribe to PlayerSpeakingStateChanged events on start
    ///     - Update the indicator alpha based on speaking state
    ///     - Unsubscribe on disable to prevent memory leaks
    /// </remarks>
    public class PlayerSpeakingIndicator : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField]
        [Tooltip(
            "The Image component that indicates microphone/speaking state. If not assigned, will try to find on this GameObject.")]
        private Image microphoneIndicator;

        [Header("Alpha Settings")]
        [SerializeField]
        [Tooltip("Alpha value when player is speaking (0-1)")]
        [Range(0f, 1f)]
        private float speakingAlpha = 1f;

        [SerializeField] [Tooltip("Alpha value when player is silent (0-1)")] [Range(0f, 1f)]
        private float silentAlpha = 0.5f;

        [Header("Animation")] [SerializeField] [Tooltip("Enable smooth transition between alpha values")]
        private bool enableSmoothTransition = true;

        [SerializeField] [Tooltip("Speed of the alpha transition")]
        private float transitionSpeed = 8f;

        private Color _baseColor;

        private IEventHub _eventHub;
        private bool _isSubscribed;
        private SubscriptionToken _subscriptionToken;
        private float _targetAlpha;

        /// <summary>
        ///     Gets whether the player is currently speaking.
        /// </summary>
        public bool IsSpeaking { get; private set; }

        private void Awake()
        {
            if (microphoneIndicator == null) microphoneIndicator = GetComponent<Image>();

            if (microphoneIndicator != null)
            {
                _baseColor = microphoneIndicator.color;
                _targetAlpha = silentAlpha;
            }
        }

        private void Start()
        {
            TryResolveDependencies();
            SubscribeToEvents();
        }

        private void Update()
        {
            if (microphoneIndicator == null) return;

            if (enableSmoothTransition)
            {
                float currentAlpha = microphoneIndicator.color.a;
                float newAlpha = Mathf.Lerp(currentAlpha, _targetAlpha, Time.deltaTime * transitionSpeed);
                SetAlpha(newAlpha);
            }
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            if (_eventHub != null && !_isSubscribed) SubscribeToEvents();
        }

        private void OnDisable() => UnsubscribeFromEvents();

        private void OnDestroy() => UnsubscribeFromEvents();

        public void Inject(IEventHub eventHub)
        {
            if (_eventHub == eventHub)
                return;

            UnsubscribeFromEvents();
            _eventHub = eventHub;

            if (isActiveAndEnabled)
                SubscribeToEvents();
        }

        private void TryResolveDependencies()
        {
            if (_eventHub != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            if (manager.TryGetEventHub(out IEventHub eventHub))
                Inject(eventHub);
        }

        /// <summary>
        ///     Subscribes to PlayerSpeakingStateChanged events.
        /// </summary>
        private void SubscribeToEvents()
        {
            if (_isSubscribed) return;

            if (_eventHub == null)
            {
                ConvaiLogger.Warning(
                    "IEventHub not available. " +
                    "Ensure ConvaiManager has initialized before this component.", LogCategory.UI);
                return;
            }

            _subscriptionToken = _eventHub.Subscribe<PlayerSpeakingStateChanged>(OnPlayerSpeakingStateChanged);
            _isSubscribed = true;

            UpdateIndicator(false);
        }

        /// <summary>
        ///     Unsubscribes from events to prevent memory leaks.
        /// </summary>
        private void UnsubscribeFromEvents()
        {
            if (!_isSubscribed || _eventHub == null) return;

            _eventHub.Unsubscribe(_subscriptionToken);
            _subscriptionToken = default;
            _isSubscribed = false;
        }

        /// <summary>
        ///     Handles PlayerSpeakingStateChanged events from the EventHub.
        /// </summary>
        /// <param name="eventData">The player speaking state change event data.</param>
        private void OnPlayerSpeakingStateChanged(PlayerSpeakingStateChanged eventData)
        {
            IsSpeaking = eventData.IsSpeaking;
            UpdateIndicator(IsSpeaking);
        }

        /// <summary>
        ///     Updates the indicator based on speaking state.
        /// </summary>
        /// <param name="isSpeaking">Whether the player is currently speaking.</param>
        private void UpdateIndicator(bool isSpeaking)
        {
            _targetAlpha = isSpeaking ? speakingAlpha : silentAlpha;

            if (!enableSmoothTransition && microphoneIndicator != null) SetAlpha(_targetAlpha);
        }

        /// <summary>
        ///     Sets the alpha value of the indicator.
        /// </summary>
        /// <param name="alpha">The alpha value to set (0-1).</param>
        private void SetAlpha(float alpha)
        {
            if (microphoneIndicator == null) return;

            Color color = microphoneIndicator.color;
            microphoneIndicator.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        ///     Manually sets the speaking state (useful for testing).
        /// </summary>
        /// <param name="isSpeaking">Whether the player is speaking.</param>
        public void SetSpeakingState(bool isSpeaking)
        {
            IsSpeaking = isSpeaking;
            UpdateIndicator(isSpeaking);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (microphoneIndicator != null && !UnityEngine.Application.isPlaying) SetAlpha(silentAlpha);
        }

        [ContextMenu("Test - Simulate Speaking")]
        private void TestSimulateSpeaking() => SetSpeakingState(true);

        [ContextMenu("Test - Simulate Silent")]
        private void TestSimulateSilent() => SetSpeakingState(false);
#endif
    }
}
