using System;
using Convai.Runtime.Presentation.Views.Notifications;

namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Shared interface for the notification service.
    /// </summary>
    /// <remarks>
    ///     Requests carry a <see cref="SONotification" /> asset (id, title, message, icon).
    /// </remarks>
    public interface IConvaiNotificationService
    {
        /// <summary>
        ///     Event raised when a notification should be displayed.
        /// </summary>
        public event Action<SONotification> OnNotificationRequested;

        /// <summary>
        ///     Event raised when a notification should be hidden.
        /// </summary>
        public event Action OnNotificationDismissed;

        /// <summary>
        ///     Requests a notification to be displayed.
        /// </summary>
        /// <param name="notification">The notification asset to display.</param>
        public void RequestNotification(SONotification notification);

        /// <summary>
        ///     Dismisses the current notification.
        /// </summary>
        public void DismissNotification();
    }
}
