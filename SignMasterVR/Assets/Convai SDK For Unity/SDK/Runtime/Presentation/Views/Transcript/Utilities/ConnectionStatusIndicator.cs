using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Displays the current Convai session connection status in the UI.
    ///     Subscribes to SessionStateChanged events via EventHub for decoupled communication.
    /// </summary>
    /// <remarks>
    ///     This component follows the SDK's event-driven architecture pattern.
    ///     It can be attached to any UI panel that needs to show connection status.
    ///     Usage:
    ///     1. Add this component to a GameObject with UI elements
    ///     2. Assign the statusIndicator (Image) and statusText (TMP_Text) references
    ///     3. Optionally customize colors for each state
    ///     The component will automatically:
    ///     - Query current connection state on start
    ///     - Subscribe to SessionStateChanged events
    ///     - Update the UI based on connection state
    ///     - Unsubscribe on disable to prevent memory leaks
    /// </remarks>
    public class ConnectionStatusIndicator : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField]
        [Tooltip("The circular indicator image that shows connection status color")]
        private Image statusIndicator;

        [SerializeField] [Tooltip("The text component that displays the connection status message")]
        private TMP_Text statusText;

        [Header("Status Colors")] [SerializeField] [Tooltip("Color when disconnected from the server")]
        private Color disconnectedColor = new(0.5f, 0.5f, 0.5f, 1f);

        [SerializeField] [Tooltip("Color when attempting to connect")]
        private Color connectingColor = new(1f, 0.6f, 0f, 1f);

        [SerializeField] [Tooltip("Color when successfully connected")]
        private Color connectedColor = new(0.2f, 0.8f, 0.2f, 1f);

        [SerializeField] [Tooltip("Color when reconnecting after connection loss")]
        private Color reconnectingColor = new(1f, 0.8f, 0f, 1f);

        [SerializeField] [Tooltip("Color when an error occurred")]
        private Color errorColor = new(0.9f, 0.2f, 0.2f, 1f);

        [Header("Status Messages")] [SerializeField]
        private string disconnectedMessage = "Disconnected";

        [SerializeField] private string connectingMessage = "Connecting";
        [SerializeField] private string connectedMessage = "Connected";
        [SerializeField] private string reconnectingMessage = "Reconnecting";
        [SerializeField] private string disconnectingMessage = "Disconnecting";
        [SerializeField] private string errorMessage = "Connection Error";

        [Header("Animation")]
        [SerializeField]
        [Tooltip("Enable pulsing animation for transitional states (Connecting, Reconnecting)")]
        private bool enablePulseAnimation = true;

        [SerializeField] [Tooltip("Speed of the pulse animation")]
        private float pulseSpeed = 2f;

        [SerializeField] [Tooltip("Minimum alpha value during pulse")]
        private float pulseMinAlpha = 0.4f;

        private IConvaiRoomConnectionService _connectionService;
        private SessionState _currentState = SessionState.Disconnected;

        private IEventHub _eventHub;
        private bool _isPulsing;
        private bool _isSubscribed;
        private SubscriptionToken _subscriptionToken;

        private void Start()
        {
            TryResolveDependencies();
            SubscribeToEvents();
        }

        private void Update()
        {
            if (!_isPulsing || statusIndicator == null) return;

            float alpha = Mathf.Lerp(pulseMinAlpha, 1f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Color currentColor = statusIndicator.color;
            statusIndicator.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha);
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            if (_eventHub != null && !_isSubscribed) SubscribeToEvents();
        }

        private void OnDisable() => UnsubscribeFromEvents();

        private void OnDestroy() => UnsubscribeFromEvents();

        public void Inject(IEventHub eventHub, IConvaiRoomConnectionService connectionService)
        {
            if (_eventHub != eventHub)
                UnsubscribeFromEvents();

            _eventHub = eventHub;
            _connectionService = connectionService;

            if (isActiveAndEnabled)
                SubscribeToEvents();
        }

        private void TryResolveDependencies()
        {
            if (_eventHub != null && _connectionService != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetEventHub(out IEventHub eventHub);
            manager.TryGetRoomConnectionService(out IConvaiRoomConnectionService connectionService);
            Inject(eventHub, connectionService);
        }

        /// <summary>
        ///     Subscribes to SessionStateChanged events and queries initial state.
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

            _subscriptionToken = _eventHub.Subscribe<SessionStateChanged>(OnSessionStateChanged);
            _isSubscribed = true;

            QueryInitialState();
        }

        /// <summary>
        ///     Queries the initial connection state from the room connection service.
        /// </summary>
        private void QueryInitialState()
        {
            if (_connectionService != null)
            {
                // Prefer the session state machine over a raw transport connectivity flag.
                // On WebGL, the transport can be connected before the bot is ready, so IsConnected alone
                // can prematurely show "Connected".
                SessionState initialState = _connectionService.CurrentState;

                // Fallback: if session state isn't initialized yet but transport is connected.
                if (initialState == SessionState.Disconnected && _connectionService.IsConnected)
                    initialState = SessionState.Connected;

                _currentState = initialState;
                UpdateUI(_currentState);
            }
            else
            {
                _currentState = SessionState.Disconnected;
                UpdateUI(_currentState);
            }
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
        ///     Handles SessionStateChanged events from the EventHub.
        /// </summary>
        /// <param name="eventData">The session state change event data.</param>
        private void OnSessionStateChanged(SessionStateChanged eventData)
        {
            _currentState = eventData.NewState;
            UpdateUI(_currentState);
        }

        /// <summary>
        ///     Updates the UI elements based on the current session state.
        /// </summary>
        /// <param name="state">The current session state.</param>
        private void UpdateUI(SessionState state)
        {
            Color targetColor = GetColorForState(state);
            string message = GetMessageForState(state);
            bool shouldPulse = state.IsTransitioning() && enablePulseAnimation;

            if (statusIndicator != null)
                statusIndicator.color = targetColor;
            else
                ConvaiLogger.Warning("statusIndicator is not assigned!", LogCategory.UI);

            if (statusText != null)
                statusText.text = message;
            else
                ConvaiLogger.Warning("statusText is not assigned!", LogCategory.UI);

            _isPulsing = shouldPulse;

            if (!_isPulsing && statusIndicator != null)
            {
                Color color = statusIndicator.color;
                statusIndicator.color = new Color(color.r, color.g, color.b, 1f);
            }
        }

        private Color GetColorForState(SessionState state)
        {
            return state switch
            {
                SessionState.Disconnected => disconnectedColor,
                SessionState.Connecting => connectingColor,
                SessionState.Connected => connectedColor,
                SessionState.Reconnecting => reconnectingColor,
                SessionState.Disconnecting => disconnectedColor,
                SessionState.Error => errorColor,
                _ => disconnectedColor
            };
        }

        private string GetMessageForState(SessionState state)
        {
            return state switch
            {
                SessionState.Disconnected => disconnectedMessage,
                SessionState.Connecting => connectingMessage,
                SessionState.Connected => connectedMessage,
                SessionState.Reconnecting => reconnectingMessage,
                SessionState.Disconnecting => disconnectingMessage,
                SessionState.Error => errorMessage,
                _ => disconnectedMessage
            };
        }

        /// <summary>
        ///     Manually sets the connection state (useful for testing or override scenarios).
        /// </summary>
        /// <param name="state">The state to display.</param>
        public void SetState(SessionState state)
        {
            _currentState = state;
            UpdateUI(state);
        }

        /// <summary>
        ///     Forces a refresh of the connection state from the service.
        /// </summary>
        public void RefreshState() => QueryInitialState();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (statusIndicator != null && !UnityEngine.Application.isPlaying) UpdateUI(_currentState);
        }

        [ContextMenu("Test - Show Connecting")]
        private void TestShowConnecting() => SetState(SessionState.Connecting);

        [ContextMenu("Test - Show Connected")]
        private void TestShowConnected() => SetState(SessionState.Connected);

        [ContextMenu("Test - Show Disconnected")]
        private void TestShowDisconnected() => SetState(SessionState.Disconnected);

        [ContextMenu("Test - Show Error")]
        private void TestShowError() => SetState(SessionState.Error);
#endif
    }
}
