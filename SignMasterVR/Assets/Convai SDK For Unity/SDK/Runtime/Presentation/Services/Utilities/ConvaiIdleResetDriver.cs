using System;
using System.Reflection;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.Presentation.Services.Utilities
{
    /// <summary>
    ///     Optional component that sends reset-idle-timer only after an idle warning and only on UI-only activity.
    /// </summary>
    [AddComponentMenu("Convai/Convai Idle Reset Driver")]
    public sealed class ConvaiIdleResetDriver : MonoBehaviour
    {
        [SerializeField]
        [Min(0f)]
        [Tooltip("Minimum seconds between reset-idle-timer retries when the send path is unavailable.")]
        private float _retryCooldownSeconds = 3f;

        [SerializeField] [Tooltip("Whether keyboard input should count as UI-only activity.")]
        private bool _monitorKeyboardInput = true;

        [SerializeField] [Tooltip("Whether mouse clicks should count as UI-only activity.")]
        private bool _monitorMouseInput = true;

        [SerializeField] [Tooltip("Whether touch taps should count as UI-only activity.")]
        private bool _monitorTouchInput = true;

        [SerializeField] [Tooltip("Log idle reset driver decisions for debugging.")]
        private bool _debugLogging;

        private IConvaiRoomConnectionService _connectionService;
        private IEventHub _eventHub;
        private bool _isSubscribed;
        private bool _loggedMissingDependencies;
        private SubscriptionToken _playerSpeakingToken;
        private IdleResetPolicy _policy;
        private SubscriptionToken _rtviOutboundToken;
        private SubscriptionToken _sessionStateToken;
        private SubscriptionToken _userIdleWarningToken;

        private void Awake() => RebuildPolicy();

        private void Update()
        {
            if (!_isSubscribed)
                SubscribeToEvents();

            if (!_isSubscribed || _policy == null || !_policy.ShouldMonitorUi)
                return;

            if (WasUiActivityDetected())
                TryHandleUiActivity("unity-input");
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            SubscribeToEvents();
        }

        private void OnDisable() => UnsubscribeFromEvents();

        private void OnDestroy() => UnsubscribeFromEvents();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!UnityEngine.Application.isPlaying)
                return;

            RebuildPolicy();
        }
#endif

        /// <summary>
        ///     Manually reports UI activity, useful for wiring explicit UI callbacks.
        /// </summary>
        public void ReportUiActivity() => TryHandleUiActivity("manual");

        public void Inject(IEventHub eventHub, IConvaiRoomConnectionService connectionService)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _loggedMissingDependencies = false;
            RebuildPolicy();
        }

        private void RebuildPolicy()
        {
            _policy = new IdleResetPolicy(TimeSpan.FromSeconds(Mathf.Max(0f, _retryCooldownSeconds)));
            if (_connectionService != null)
                _policy.HandleSessionStateChanged(_connectionService.CurrentState);
        }

        private void TryResolveDependencies()
        {
            if (_eventHub != null && _connectionService != null)
                return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null)
                return;

            manager.TryGetEventHub(out IEventHub eventHub);
            manager.TryGetRoomConnectionService(out IConvaiRoomConnectionService connectionService);

            if (eventHub != null && connectionService != null)
                Inject(eventHub, connectionService);
        }

        private void SubscribeToEvents()
        {
            if (_isSubscribed)
                return;

            TryResolveDependencies();
            if (_eventHub == null || _connectionService == null)
            {
                if (_loggedMissingDependencies)
                    return;

                ConvaiLogger.Warning(
                    "EventHub or room connection service is unavailable. Ensure ConvaiManager is initialized.",
                    LogCategory.SDK);
                _loggedMissingDependencies = true;
                return;
            }

            _userIdleWarningToken = _eventHub.Subscribe<UserIdleWarningReceived>(HandleUserIdleWarningReceived);
            _playerSpeakingToken = _eventHub.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChanged);
            _rtviOutboundToken = _eventHub.Subscribe<OutboundRtviMessageSent>(HandleOutboundRtviMessageSent);
            _sessionStateToken = _eventHub.Subscribe<SessionStateChanged>(HandleSessionStateChanged);
            _policy.HandleSessionStateChanged(_connectionService.CurrentState);
            _isSubscribed = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!_isSubscribed || _eventHub == null)
                return;

            _eventHub.Unsubscribe(_userIdleWarningToken);
            _eventHub.Unsubscribe(_playerSpeakingToken);
            _eventHub.Unsubscribe(_rtviOutboundToken);
            _eventHub.Unsubscribe(_sessionStateToken);
            _userIdleWarningToken = default;
            _playerSpeakingToken = default;
            _rtviOutboundToken = default;
            _sessionStateToken = default;
            _isSubscribed = false;
        }

        private void HandleUserIdleWarningReceived(UserIdleWarningReceived e)
        {
            _policy?.HandleIdleWarning();

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"Armed after user-idle-warning. remainingSeconds={e.RemainingSeconds}",
                    LogCategory.SDK);
            }
        }

        private void HandlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged e)
        {
            _policy?.HandlePlayerSpeakingStateChanged(e.IsSpeaking);

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"Player speaking state changed. isSpeaking={e.IsSpeaking}, warningActive={_policy?.IsWarningActive}",
                    LogCategory.SDK);
            }
        }

        private void HandleOutboundRtviMessageSent(OutboundRtviMessageSent e)
        {
            _policy?.HandleOutboundRtviMessage(e.MessageType);

            if (_debugLogging && IdleResetPolicy.IsServerVisibleActivity(e.MessageType))
            {
                ConvaiLogger.Debug(
                    $"Cleared armed warning because outbound RTVI activity was observed. type={e.MessageType}",
                    LogCategory.SDK);
            }
        }

        private void HandleSessionStateChanged(SessionStateChanged e)
        {
            _policy?.HandleSessionStateChanged(e.NewState);

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"Session state changed. newState={e.NewState}, warningActive={_policy?.IsWarningActive}",
                    LogCategory.SDK);
            }
        }

        private void TryHandleUiActivity(string source)
        {
            if (_policy == null || _connectionService == null)
                return;

            if (!_policy.TryBeginResetAttempt(DateTime.UtcNow))
                return;

            bool sent = _connectionService.ResetIdleTimer();
            _policy.HandleResetResult(sent);

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"UI activity handled. source={source}, sentReset={sent}, warningActive={_policy.IsWarningActive}",
                    LogCategory.SDK);
            }
        }

        private bool WasUiActivityDetected()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (_monitorMouseInput &&
                (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
                return true;

            if (_monitorTouchInput)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.GetTouch(i).phase == TouchPhase.Began)
                        return true;
                }
            }

            if (_monitorKeyboardInput && Input.anyKeyDown)
                return true;
#endif

#if ENABLE_INPUT_SYSTEM
            if (_monitorMouseInput &&
                (WasPressedThisFrame("UnityEngine.InputSystem.Mouse, Unity.InputSystem", "leftButton") ||
                 WasPressedThisFrame("UnityEngine.InputSystem.Mouse, Unity.InputSystem", "rightButton") ||
                 WasPressedThisFrame("UnityEngine.InputSystem.Mouse, Unity.InputSystem", "middleButton")))
                return true;

            if (_monitorTouchInput &&
                WasPressedThisFrame("UnityEngine.InputSystem.Touchscreen, Unity.InputSystem", "primaryTouch", "press"))
                return true;

            if (_monitorKeyboardInput &&
                WasPressedThisFrame("UnityEngine.InputSystem.Keyboard, Unity.InputSystem", "anyKey"))
                return true;
#endif

            return false;
        }

        private static bool WasPressedThisFrame(string typeName, params string[] memberPath)
        {
            var type = Type.GetType(typeName);
            if (type == null) return false;

            object device = type.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (device == null) return false;

            object current = device;
            for (int i = 0; i < memberPath.Length; i++)
            {
                current = current?.GetType().GetProperty(memberPath[i], BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(current);
                if (current == null) return false;
            }

            return current.GetType().GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance)
                       ?.GetValue(current) is bool wasPressed
                   && wasPressed;
        }
    }
}
