using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     One ScriptableObject per notification: stable id (for cooldown / mapping), copy, and icon.
    /// </summary>
    [CreateAssetMenu(menuName = "Convai/Notification System/Notification", fileName = "New Notification")]
    public class SONotification : ScriptableObject
    {
        [Tooltip("Stable id for logs, cooldown, and session-error mapping. If empty, the asset name is used.")]
        [SerializeField]
        private string _id;

        [Tooltip("The icon to be displayed with the notification.")]
        public Sprite icon;

        [Tooltip("The notification title.")] public string notificationTitle;

        [TextArea(10, 10)] [Tooltip("The text content of the notification.")]
        public string notificationMessage;

        /// <summary>
        ///     Stable identifier (defaults to the asset name when <see cref="_id" /> is empty).
        /// </summary>
        public string Id => string.IsNullOrEmpty(_id) ? name : _id;

        public SONotification SetIcon(Sprite newIcon)
        {
            icon = newIcon;
            return this;
        }

        public SONotification SetTitle(string title)
        {
            notificationTitle = title;
            return this;
        }

        public SONotification SetMessage(string message)
        {
            notificationMessage = message;
            return this;
        }

        public override string ToString() => $"Id: {Id} | Title: {notificationTitle} | Message: {notificationMessage}";
    }
}
