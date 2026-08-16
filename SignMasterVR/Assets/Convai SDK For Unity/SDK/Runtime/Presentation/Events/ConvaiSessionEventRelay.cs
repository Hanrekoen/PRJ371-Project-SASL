using System;
using Convai.Domain.DomainEvents.Session;
using Convai.Runtime.Components;
using Convai.Runtime.Facades;
using Convai.Runtime.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Presentation.Events
{
    /// <summary>
    ///     Inspector-friendly relay for high-value session and lifecycle events.
    /// </summary>
    /// <remarks>
    ///     Use this as the designer-friendly, no-code layer over <c>ConvaiManager.Events</c>.
    ///     For code-driven integrations, subscribe to <c>ConvaiManager.Events</c> directly.
    /// </remarks>
    [AddComponentMenu("Convai/Events/Convai Session Event Relay")]
    [DisallowMultipleComponent]
    public sealed class ConvaiSessionEventRelay : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Optional explicit manager reference. If omitted, the relay can use ConvaiManager.ActiveManager.")]
        [SerializeField]
        private ConvaiManager _manager;

        [Tooltip("If enabled, the relay uses ConvaiManager.ActiveManager when no manager is assigned.")]
        [SerializeField]
        private bool _autoResolveManager = true;

        [Header("Events")] [SerializeField] private UnityEvent _onConnected = new();

        [SerializeField] private UnityEvent _onDisconnected = new();

        [SerializeField] private UnityEvent _onReconnecting = new();

        [SerializeField] private UnityEvent _onReconnected = new();

        [SerializeField] private UnityEvent _onUsageLimitReached = new();

        [SerializeField] private UnityEvent<UserIdleWarningRelayData> _onUserIdleWarning = new();

        [SerializeField] private UnityEvent<UserIdleTimeoutRelayData> _onUserIdleTimeout = new();

        [SerializeField] private UnityEvent<RuntimeBackgroundStateRelayData> _onRuntimeBackgroundStateChanged = new();

        [SerializeField] private UnityEvent<SessionStateChangedRelayData> _onSessionStateChanged = new();

        [SerializeField] private UnityEvent<SessionErrorRelayData> _onSessionError = new();

        private ConvaiEvents _boundEvents;
        private bool _loggedSubscribeRetry;
        private bool _loggedTargetWarning;
        private bool _subscribed;
        private ConvaiEvents _testEvents;

        public ConvaiManager Manager => _manager;
        public bool AutoResolveManager => _autoResolveManager;
        public UnityEvent OnConnected => _onConnected;
        public UnityEvent OnDisconnected => _onDisconnected;
        public UnityEvent OnReconnecting => _onReconnecting;
        public UnityEvent OnReconnected => _onReconnected;
        public UnityEvent OnUsageLimitReached => _onUsageLimitReached;
        public UnityEvent<UserIdleWarningRelayData> OnUserIdleWarning => _onUserIdleWarning;
        public UnityEvent<UserIdleTimeoutRelayData> OnUserIdleTimeout => _onUserIdleTimeout;
        public UnityEvent<RuntimeBackgroundStateRelayData> OnRuntimeBackgroundStateChanged =>
            _onRuntimeBackgroundStateChanged;
        public UnityEvent<SessionStateChangedRelayData> OnSessionStateChanged => _onSessionStateChanged;
        public UnityEvent<SessionErrorRelayData> OnSessionError => _onSessionError;

        private void LateUpdate()
        {
            if (!_subscribed) TrySubscribe(false);
        }

        private void OnEnable()
        {
            _loggedTargetWarning = false;
            _loggedSubscribeRetry = false;
            TrySubscribe(true);
        }

        private void OnDisable() => Unsubscribe();

        internal void BindForTesting(ConvaiEvents events)
        {
            Unsubscribe();
            _testEvents = events;
            TrySubscribe(true);
        }

        internal string GetConfigurationWarning()
        {
            if (_testEvents != null) return string.Empty;

            if (_manager == null && _autoResolveManager)
                return
                    "No explicit ConvaiManager assigned. The relay will use ConvaiManager.ActiveManager at runtime; assign a manager for deterministic scene wiring.";

            if (_manager == null && !_autoResolveManager)
                return "Assign a ConvaiManager or enable Auto Resolve Manager.";

            return string.Empty;
        }

        private void TrySubscribe(bool logFailures)
        {
            if (_subscribed) return;

            if (!TryResolveEvents(out ConvaiEvents events))
            {
                if (logFailures) LogConfigurationWarning();
                return;
            }

            events.OnSessionStateChanged += HandleSessionStateChanged;
            events.OnSessionError += HandleSessionError;
            events.OnUsageLimitReached += HandleUsageLimitReached;
            events.OnUserIdleWarningReceived += HandleUserIdleWarningReceived;
            events.OnUserIdleTimeoutElapsed += HandleUserIdleTimeoutElapsed;
            events.OnRuntimeBackgroundStateChanged += HandleRuntimeBackgroundStateChanged;

            _boundEvents = events;
            _subscribed = true;
            _loggedSubscribeRetry = false;
        }

        private bool TryResolveEvents(out ConvaiEvents events)
        {
            if (_testEvents != null)
            {
                events = _testEvents;
                return true;
            }

            ConvaiManager manager =
                _manager != null ? _manager : _autoResolveManager ? ConvaiManager.ActiveManager : null;
            if (manager == null)
            {
                events = null;
                return false;
            }

            try
            {
                events = manager.Events;
                return true;
            }
            catch (InvalidOperationException)
            {
                events = null;
                if (!_loggedSubscribeRetry)
                {
                    Debug.LogWarning(
                        $"[{nameof(ConvaiSessionEventRelay)}] ConvaiManager is present but not initialized yet. The relay will retry while enabled.",
                        this);
                    _loggedSubscribeRetry = true;
                }

                return false;
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _boundEvents == null) return;

            _boundEvents.OnSessionStateChanged -= HandleSessionStateChanged;
            _boundEvents.OnSessionError -= HandleSessionError;
            _boundEvents.OnUsageLimitReached -= HandleUsageLimitReached;
            _boundEvents.OnUserIdleWarningReceived -= HandleUserIdleWarningReceived;
            _boundEvents.OnUserIdleTimeoutElapsed -= HandleUserIdleTimeoutElapsed;
            _boundEvents.OnRuntimeBackgroundStateChanged -= HandleRuntimeBackgroundStateChanged;

            _boundEvents = null;
            _subscribed = false;
        }

        private void HandleSessionStateChanged(SessionStateChanged e)
        {
            _onSessionStateChanged?.Invoke(new SessionStateChangedRelayData(e));

            if (e.OldState == SessionState.Connected && e.NewState == SessionState.Reconnecting)
                _onReconnecting?.Invoke();

            if (e.OldState == SessionState.Reconnecting && e.NewState == SessionState.Connected)
                _onReconnected?.Invoke();
            else if (e.OldState == SessionState.Connecting && e.NewState == SessionState.Connected)
                _onConnected?.Invoke();

            if (e.NewState == SessionState.Disconnected) _onDisconnected?.Invoke();
        }

        private void HandleSessionError(SessionError e) => _onSessionError?.Invoke(new SessionErrorRelayData(e));

        private void HandleUsageLimitReached(UsageLimitReached _) =>
            _onUsageLimitReached?.Invoke();

        private void HandleUserIdleWarningReceived(UserIdleWarningReceived e) =>
            _onUserIdleWarning?.Invoke(new UserIdleWarningRelayData(e));

        private void HandleUserIdleTimeoutElapsed(UserIdleTimeoutElapsed e) =>
            _onUserIdleTimeout?.Invoke(new UserIdleTimeoutRelayData(e));

        private void HandleRuntimeBackgroundStateChanged(RuntimeBackgroundStateChanged e) =>
            _onRuntimeBackgroundStateChanged?.Invoke(new RuntimeBackgroundStateRelayData(e));

        private void LogConfigurationWarning()
        {
            if (_loggedTargetWarning) return;

            string warning = GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning)) return;

            Debug.LogWarning($"[{nameof(ConvaiSessionEventRelay)}] {warning}", this);
            _loggedTargetWarning = true;
        }
    }
}
