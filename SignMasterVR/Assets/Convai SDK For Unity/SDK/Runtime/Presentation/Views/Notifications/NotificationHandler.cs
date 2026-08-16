using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Abstractions;
using Convai.Shared.Interfaces;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     Handles the notification system's behavior and interactions.
    /// </summary>
    public class NotificationHandler : MonoBehaviour
    {
        [SerializeField] private SONotificationGroup notificationGroup;
        [SerializeField] private UINotificationController notificationControllerPrefab;
        private bool _eventsSubscribed;
        private IConvaiNotificationService _notificationService;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;

        private UINotificationController _spawnedController;

        private void Awake()
        {
            ResolveNotificationGroup();

            // Try to find existing controller in children first
            _spawnedController = GetComponentInChildren<UINotificationController>(true);

            // Only instantiate if no existing controller and prefab is set
            if (_spawnedController == null && notificationControllerPrefab != null)
                _spawnedController = Instantiate(notificationControllerPrefab, transform);
            else if (_spawnedController == null)
            {
                ConvaiLogger.Warning("No UINotificationController found and no prefab set.",
                    LogCategory.UI);
            }
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            if (!TryInitializeNotificationService())
            {
                ConvaiLogger.Warning(
                    "Notification service not available; notifications will be deferred until services initialize.",
                    LogCategory.UI);
            }
        }

        private void OnDisable()
        {
            if (_notificationService != null)
            {
                _notificationService.OnNotificationRequested -= NotificationRequest;
                _notificationService.OnNotificationDismissed -= OnNotificationDismissed;
                _eventsSubscribed = false;
            }
        }

        public void Inject(
            IConvaiNotificationService notificationService = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null)
        {
            _notificationService = notificationService;
            _runtimeSettingsService = runtimeSettingsService;

            if (isActiveAndEnabled)
                TryInitializeNotificationService();
        }

        private void TryResolveDependencies()
        {
            if (_notificationService != null && _runtimeSettingsService != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetNotificationService(out IConvaiNotificationService notificationService);
            manager.TryGetRuntimeSettingsService(out IConvaiRuntimeSettingsService runtimeSettingsService);

            if (notificationService != null && runtimeSettingsService != null)
                Inject(notificationService, runtimeSettingsService);
        }

        private void NotificationRequest(SONotification notification)
        {
            if (notification == null) return;

            if (_runtimeSettingsService == null || !_runtimeSettingsService.Current.NotificationsEnabled)
            {
                ConvaiLogger.Info("Cannot send notification because notifications are disabled in runtime settings.",
                    LogCategory.UI);
                return;
            }

            if (!TryResolveNotification(notification, out SONotification resolvedNotification))
            {
                ConvaiLogger.Warning(
                    $"No notification registered in the notification group for id: {notification.Id}",
                    LogCategory.UI);
                return;
            }

            if (_spawnedController != null)
                _spawnedController.Notify(resolvedNotification);
            else
            {
                ConvaiLogger.Error(
                    "UINotificationController is null, cannot display notification.",
                    LogCategory.UI);
            }
        }

        private void ResolveNotificationGroup()
        {
            if (notificationGroup != null) return;

            if (!SONotificationGroup.GetGroup(out notificationGroup))
            {
                ConvaiLogger.Error("SONotificationGroup asset could not be resolved.",
                    LogCategory.UI);
            }
        }

        private bool TryResolveNotification(SONotification notification, out SONotification resolvedNotification)
        {
            resolvedNotification = null;
            ResolveNotificationGroup();
            return notificationGroup != null && notificationGroup.TryResolve(notification, out resolvedNotification);
        }

        private void OnNotificationDismissed()
        {
            if (_spawnedController != null) _spawnedController.ClearAll();
        }

        private bool TryInitializeNotificationService()
        {
            if (_notificationService == null || _runtimeSettingsService == null) return false;

            if (!_eventsSubscribed)
            {
                _notificationService.OnNotificationRequested += NotificationRequest;
                _notificationService.OnNotificationDismissed += OnNotificationDismissed;
                _eventsSubscribed = true;
            }

            return true;
        }
    }
}
