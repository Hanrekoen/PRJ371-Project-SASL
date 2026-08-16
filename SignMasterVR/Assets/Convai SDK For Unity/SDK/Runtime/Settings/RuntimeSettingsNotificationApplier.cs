using System;
using Convai.Shared.Abstractions;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies notification-related runtime settings and exposes enabled state.
    /// </summary>
    public sealed class RuntimeSettingsNotificationApplier : IDisposable
    {
        private readonly IConvaiNotificationService _notificationService;
        private readonly IConvaiRuntimeSettingsService _settings;

        public RuntimeSettingsNotificationApplier(
            IConvaiRuntimeSettingsService settings,
            IConvaiNotificationService notificationService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            _settings.Changed += OnSettingsChanged;
        }

        public bool AreNotificationsEnabled => _settings.Current.NotificationsEnabled;

        public void Dispose() => _settings.Changed -= OnSettingsChanged;

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask & ConvaiRuntimeSettingsChangeMask.NotificationsEnabled) == 0) return;

            if (!changed.Current.NotificationsEnabled) _notificationService.DismissNotification();
        }
    }
}
