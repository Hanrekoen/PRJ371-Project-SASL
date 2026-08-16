using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Views.Notifications;
using Convai.Shared.Interfaces;
using UnityEngine.Scripting;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Default SDK implementation of IConvaiNotificationService.
    ///     Thread-safe: marshals event invocations to the main thread via IUnityScheduler.
    /// </summary>
    /// <remarks>
    ///     This service is always registered by the ConvaiManager-managed bootstrap pipeline. It remains inert unless:
    ///     1. A UI handler subscribes to OnNotificationRequested/OnNotificationDismissed
    ///     2. Runtime settings enable notifications
    ///     The service itself does not check runtime settings.
    ///     Callers should gate requests based on IConvaiRuntimeSettingsService.
    /// </remarks>
    [Preserve]
    public sealed class ConvaiNotificationService : IConvaiNotificationService
    {
        private readonly object _eventLock = new();
        private readonly IUnityScheduler _scheduler;
        private Action _onNotificationDismissed;

        private Action<SONotification> _onNotificationRequested;

        /// <summary>
        ///     Creates a new ConvaiNotificationService.
        /// </summary>
        /// <param name="scheduler">Unity scheduler for thread marshaling. If null, events fire on the calling thread.</param>
        public ConvaiNotificationService(IUnityScheduler scheduler = null)
        {
            _scheduler = scheduler;
        }

        /// <inheritdoc />
        public event Action<SONotification> OnNotificationRequested
        {
            add
            {
                lock (_eventLock) _onNotificationRequested += value;
            }
            remove
            {
                lock (_eventLock) _onNotificationRequested -= value;
            }
        }

        /// <inheritdoc />
        public event Action OnNotificationDismissed
        {
            add
            {
                lock (_eventLock) _onNotificationDismissed += value;
            }
            remove
            {
                lock (_eventLock) _onNotificationDismissed -= value;
            }
        }

        /// <inheritdoc />
        public void RequestNotification(SONotification notification)
        {
            if (notification == null) return;

            Action<SONotification> handler;
            lock (_eventLock) handler = _onNotificationRequested;

            if (handler == null) return;

            void InvokeHandler()
            {
                try
                {
                    handler.Invoke(notification);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error invoking OnNotificationRequested: {ex.Message}",
                        LogCategory.UI);
                }
            }

            if (_scheduler != null && !_scheduler.IsMainThread())
                _scheduler.ScheduleOnMainThread(InvokeHandler);
            else
                InvokeHandler();
        }

        /// <inheritdoc />
        public void DismissNotification()
        {
            Action handler;
            lock (_eventLock) handler = _onNotificationDismissed;

            if (handler == null) return;

            void InvokeHandler()
            {
                try
                {
                    handler.Invoke();
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error invoking OnNotificationDismissed: {ex.Message}",
                        LogCategory.UI);
                }
            }

            if (_scheduler != null && !_scheduler.IsMainThread())
                _scheduler.ScheduleOnMainThread(InvokeHandler);
            else
                InvokeHandler();
        }
    }
}
