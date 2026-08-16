using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     Represents a group of notifications as a ScriptableObject.
    ///     This allows for easy configuration and management of different notifications in the Unity Editor.
    /// </summary>
    [CreateAssetMenu(menuName = "Convai/Notification System/Notification Group", fileName = "New Notification Group")]
    public class SONotificationGroup : ScriptableObject
    {
        /// <summary>
        ///     Array of SONotification objects.
        ///     Each object represents a unique notification that can be triggered in the application.
        /// </summary>
        public SONotification[] soNotifications;

        private Dictionary<string, SONotification> _notificationsById;

        private void OnEnable() => InvalidateLookup();

        private void OnValidate() => InvalidateLookup();

        public static bool GetGroup(out SONotificationGroup group)
        {
            group = Resources.Load<SONotificationGroup>(nameof(SONotificationGroup));
            return group != null;
        }

        public bool TryResolve(SONotification notification, out SONotification resolvedNotification)
        {
            resolvedNotification = null;
            return notification != null && TryGetById(notification.Id, out resolvedNotification);
        }

        public bool TryGetById(string notificationId, out SONotification notification)
        {
            notification = null;
            if (string.IsNullOrWhiteSpace(notificationId)) return false;

            BuildLookupIfNeeded();
            return _notificationsById.TryGetValue(notificationId, out notification);
        }

        private void InvalidateLookup() => _notificationsById = null;

        private void BuildLookupIfNeeded()
        {
            if (_notificationsById != null) return;

            _notificationsById = new Dictionary<string, SONotification>(StringComparer.Ordinal);
            if (soNotifications == null) return;

            foreach (SONotification notification in soNotifications)
            {
                if (notification == null) continue;

                string notificationId = notification.Id;
                if (string.IsNullOrWhiteSpace(notificationId) || _notificationsById.ContainsKey(notificationId))
                    continue;

                _notificationsById.Add(notificationId, notification);
            }
        }
    }
}
