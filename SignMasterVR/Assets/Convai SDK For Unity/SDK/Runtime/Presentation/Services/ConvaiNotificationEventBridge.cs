using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Views.Notifications;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Scripting;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Bridges domain events (SessionError, etc.) to notification requests.
    ///     Eagerly initialized by bootstrap to subscribe to domain events.
    /// </summary>
    /// <remarks>
    ///     This service:
    ///     - Subscribes to SessionError events from IEventHub
    ///     - Resolves notifications from session errors via <see cref="SONotificationErrorMap" />
    ///     - Checks runtime notification enablement before requesting notifications
    ///     - Implements cooldown/deduplication (keyed by <see cref="SONotification.Id" />)
    /// </remarks>
    [Preserve]
    public sealed class ConvaiNotificationEventBridge : IDisposable
    {
        private readonly object _cooldownLock = new();
        private readonly SONotificationErrorMap _errorMap;
        private readonly IEventHub _eventHub;
        private readonly Func<bool> _isNotificationEnabled;
        private readonly Dictionary<string, float> _lastShownTimeByNotificationId = new();
        private readonly Func<IConvaiNotificationService> _notificationServiceAccessor;

        private SubscriptionToken _sessionErrorToken;

        /// <summary>
        ///     Creates a new ConvaiNotificationEventBridge.
        /// </summary>
        /// <param name="notificationService">The notification service to send requests to.</param>
        /// <param name="eventHub">The event hub to subscribe to domain events.</param>
        /// <param name="isNotificationEnabled">Function that returns true if notifications are enabled in settings.</param>
        /// <param name="errorMap">
        ///     Maps session errors to notifications. If null, loads <see cref="SONotificationErrorMap.LoadDefault" />.
        /// </param>
        public ConvaiNotificationEventBridge(
            IConvaiNotificationService notificationService,
            IEventHub eventHub,
            Func<bool> isNotificationEnabled,
            SONotificationErrorMap errorMap = null)
        {
            if (notificationService == null) throw new ArgumentNullException(nameof(notificationService));

            _notificationServiceAccessor = () => notificationService;
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _isNotificationEnabled = isNotificationEnabled ?? (() => false);
            _errorMap = errorMap ?? SONotificationErrorMap.LoadDefault();

            SubscribeToEvents();
        }

        /// <summary>
        ///     Creates a new ConvaiNotificationEventBridge with late-bound notification service resolution.
        /// </summary>
        /// <param name="notificationServiceAccessor">
        ///     Accessor that returns the current notification service instance (or null if
        ///     unavailable).
        /// </param>
        /// <param name="eventHub">The event hub to subscribe to domain events.</param>
        /// <param name="isNotificationEnabled">Function that returns true if notifications are enabled in settings.</param>
        /// <param name="errorMap">
        ///     Maps session errors to notifications. If null, loads <see cref="SONotificationErrorMap.LoadDefault" />.
        /// </param>
        public ConvaiNotificationEventBridge(
            Func<IConvaiNotificationService> notificationServiceAccessor,
            IEventHub eventHub,
            Func<bool> isNotificationEnabled,
            SONotificationErrorMap errorMap = null)
        {
            _notificationServiceAccessor = notificationServiceAccessor ?? (() => null);
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _isNotificationEnabled = isNotificationEnabled ?? (() => false);
            _errorMap = errorMap;

            SubscribeToEvents();
        }

        /// <summary>
        ///     Cooldown period in seconds before the same notification id can be shown again.
        /// </summary>
        public float CooldownSeconds { get; set; } = 10f;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_sessionErrorToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_sessionErrorToken);
                _sessionErrorToken = default;
            }

            ConvaiLogger.Debug("Disposed", LogCategory.SDK);
        }

        private void SubscribeToEvents()
        {
            _sessionErrorToken = _eventHub.Subscribe<SessionError>(
                HandleSessionError);

            ConvaiLogger.Debug("Subscribed to SessionError events", LogCategory.SDK);
        }

        private void HandleSessionError(SessionError error)
        {
            if (!_isNotificationEnabled()) return;

            IConvaiNotificationService notificationService = _notificationServiceAccessor?.Invoke();
            if (notificationService == null) return;

            SONotification notification = MapErrorCodeToNotification(error.ErrorCode);
            if (notification == null)
            {
                ConvaiLogger.Debug(
                    $"No notification mapping for error code: {error.ErrorCode}",
                    LogCategory.SDK);
                return;
            }

            if (!TryPassCooldown(notification))
            {
                ConvaiLogger.Debug(
                    $"Notification {notification.Id} suppressed by cooldown",
                    LogCategory.SDK);
                return;
            }

            ConvaiLogger.Debug(
                $"Requesting notification: {notification.Id} for error: {error.ErrorCode}",
                LogCategory.SDK);
            notificationService.RequestNotification(notification);
        }

        private SONotification MapErrorCodeToNotification(string errorCode)
        {
            SONotificationErrorMap map = _errorMap ?? SONotificationErrorMap.LoadDefault();
            if (map == null || !map.TryResolve(errorCode, out SONotification notification)) return null;
            return notification;
        }

        private bool TryPassCooldown(SONotification notification)
        {
            if (notification == null) return false;

            string id = notification.Id;
            if (string.IsNullOrEmpty(id)) return true;

            float now = Time.realtimeSinceStartup;

            lock (_cooldownLock)
            {
                if (_lastShownTimeByNotificationId.TryGetValue(id, out float lastTime))
                {
                    if (now - lastTime < CooldownSeconds)
                        return false;
                }

                _lastShownTimeByNotificationId[id] = now;
                return true;
            }
        }

        /// <summary>
        ///     Manually requests a notification (for pre-flight checks in ConvaiRoomManager).
        ///     Respects settings and cooldown.
        /// </summary>
        public void RequestNotificationIfEnabled(SONotification notification)
        {
            if (notification == null) return;
            if (!_isNotificationEnabled()) return;

            IConvaiNotificationService notificationService = _notificationServiceAccessor?.Invoke();
            if (notificationService == null) return;

            if (!TryPassCooldown(notification)) return;

            notificationService.RequestNotification(notification);
        }

        /// <summary>
        ///     Clears cooldown state (useful for testing or scene changes).
        /// </summary>
        public void ClearCooldowns()
        {
            lock (_cooldownLock) _lastShownTimeByNotificationId.Clear();
        }
    }
}
