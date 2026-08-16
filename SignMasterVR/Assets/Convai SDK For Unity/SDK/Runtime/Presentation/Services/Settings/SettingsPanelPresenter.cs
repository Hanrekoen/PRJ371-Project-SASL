using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Runtime.Room;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Presentation.Services.Settings
{
    /// <summary>
    ///     Presenter for runtime settings panel view.
    /// </summary>
    public sealed class SettingsPanelPresenter : IDisposable
    {
        private readonly IMicrophoneDeviceService _microphoneDeviceService;
        private readonly IConvaiSettingsPanelController _panelController;
        private readonly IConvaiRoomConnectionService _roomConnectionService;
        private readonly Func<string> _effectivePlayerNameProvider;

        private readonly IConvaiRuntimeSettingsService _settingsService;
        private bool _isDisposed;

        private ISettingsPanelView _view;

        public SettingsPanelPresenter(
            IConvaiRuntimeSettingsService settingsService,
            IConvaiSettingsPanelController panelController,
            IMicrophoneDeviceService microphoneDeviceService,
            IConvaiRoomConnectionService roomConnectionService = null,
            Func<string> effectivePlayerNameProvider = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _panelController = panelController ?? throw new ArgumentNullException(nameof(panelController));
            _microphoneDeviceService = microphoneDeviceService ??
                                       throw new ArgumentNullException(nameof(microphoneDeviceService));
            _roomConnectionService = roomConnectionService;
            _effectivePlayerNameProvider = effectivePlayerNameProvider;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            Unbind();
        }

        public void Bind(ISettingsPanelView view)
        {
            if (_isDisposed) return;

            if (_view == view)
            {
                Render(_settingsService.Current);
                return;
            }

            Unbind();

            _view = view;
            if (_view == null) return;

            _view.SaveRequested += OnSaveRequested;
            _view.CloseRequested += OnCloseRequested;
            _settingsService.Changed += OnSettingsChanged;
            _panelController.VisibilityChanged += OnVisibilityChanged;

            if (_roomConnectionService != null)
                _roomConnectionService.ConversationInputModeChanged += OnConversationInputModeChanged;

            Render(_settingsService.Current);
        }

        public void Unbind()
        {
            if (_view != null)
            {
                _view.SaveRequested -= OnSaveRequested;
                _view.CloseRequested -= OnCloseRequested;
            }

            _settingsService.Changed -= OnSettingsChanged;
            _panelController.VisibilityChanged -= OnVisibilityChanged;

            if (_roomConnectionService != null)
                _roomConnectionService.ConversationInputModeChanged -= OnConversationInputModeChanged;

            _view = null;
        }

        private void OnSaveRequested()
        {
            if (_view == null) return;

            ConversationInputMode requestedMode = _view.SelectedConversationInputModeInput;
            if (_roomConnectionService != null && requestedMode != _roomConnectionService.ActiveConversationInputMode)
                _ = ApplyConversationModeAsync(requestedMode);

            var patch = new ConvaiRuntimeSettingsPatch
            {
                TranscriptEnabled = _view.TranscriptEnabledInput,
                NotificationsEnabled = _view.NotificationsEnabledInput,
                PreferredMicrophoneDeviceId = _view.SelectedMicrophoneDeviceId
            };

            string playerDisplayName = _view.PlayerDisplayNameInput;
            if (playerDisplayName != null)
                patch.PlayerDisplayName = playerDisplayName;

            _settingsService.Apply(patch);
            _panelController.Close();
        }

        private async Task ApplyConversationModeAsync(ConversationInputMode mode)
        {
            try
            {
                await _roomConnectionService.SetConversationInputModeAsync(mode).AsTask();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsPanelPresenter] Mode switch failed: {e.Message}");
            }
        }

        private void OnCloseRequested() => _panelController.Close();

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed) => Render(changed.Current);

        private void OnVisibilityChanged(bool isVisible)
        {
            if (isVisible)
                Render(_settingsService.Current);
        }

        private void OnConversationInputModeChanged(ConversationInputMode mode) =>
            _view?.SetConversationInputMode(mode);

        private void Render(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            if (_view == null) return;

            string effectivePlayerName = _effectivePlayerNameProvider?.Invoke();
            _view.SetPlayerDisplayName(
                string.IsNullOrWhiteSpace(effectivePlayerName)
                    ? snapshot.PlayerDisplayName
                    : effectivePlayerName);
            _view.SetTranscriptEnabled(snapshot.TranscriptEnabled);
            _view.SetNotificationsEnabled(snapshot.NotificationsEnabled);

            IReadOnlyList<ConvaiMicrophoneDevice> devices = _microphoneDeviceService.GetAvailableDevices();
            _view.SetMicrophoneOptions(devices, snapshot.PreferredMicrophoneDeviceId);

            bool modeAvailable = _roomConnectionService != null;
            _view.SetConversationInputModeVisible(modeAvailable);
            if (modeAvailable)
                _view.SetConversationInputMode(_roomConnectionService.ActiveConversationInputMode);
        }
    }
}
