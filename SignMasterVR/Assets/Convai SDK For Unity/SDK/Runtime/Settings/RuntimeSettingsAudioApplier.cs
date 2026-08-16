using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies microphone-related runtime settings and performs live in-room mic republish.
    /// </summary>
    internal sealed class RuntimeSettingsAudioApplier : IDisposable
    {
        private readonly SemaphoreSlim _applySemaphore = new(1, 1);
        private readonly IConvaiRoomAudioService _audioService;
        private readonly IConvaiRoomConnectionService _connectionService;
        private readonly IMicrophoneDeviceService _microphoneDeviceService;
        private readonly object _queueLock = new();
        private readonly IUnityScheduler _scheduler;
        private readonly IConvaiRuntimeSettingsService _settings;
        private bool _disposed;
        private bool _hasQueued;
        private bool _isProcessing;

        private string _queuedDeviceId;

        public RuntimeSettingsAudioApplier(
            IConvaiRuntimeSettingsService settings,
            IMicrophoneDeviceService microphoneDeviceService,
            IConvaiRoomConnectionService connectionService = null,
            IConvaiRoomAudioService audioService = null,
            IUnityScheduler scheduler = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _microphoneDeviceService = microphoneDeviceService ??
                                       throw new ArgumentNullException(nameof(microphoneDeviceService));
            _connectionService = connectionService;
            _audioService = audioService;
            _scheduler = scheduler;

            _settings.Changed += OnSettingsChanged;
        }

        public void Dispose()
        {
            _disposed = true;
            _settings.Changed -= OnSettingsChanged;
            _applySemaphore.Dispose();
        }

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask & ConvaiRuntimeSettingsChangeMask.PreferredMicrophoneDeviceId) == 0) return;

            QueueSwitch(changed.Current.PreferredMicrophoneDeviceId);
        }

        private void QueueSwitch(string preferredDeviceId)
        {
            lock (_queueLock)
            {
                _queuedDeviceId = preferredDeviceId;
                _hasQueued = true;

                if (_isProcessing) return;

                _isProcessing = true;
            }

            RunOnMainThread(() => _ = ProcessQueueAsync());
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                while (true)
                {
                    if (_disposed) return;

                    string deviceId;
                    lock (_queueLock)
                    {
                        if (!_hasQueued)
                        {
                            _isProcessing = false;
                            return;
                        }

                        deviceId = _queuedDeviceId;
                        _hasQueued = false;
                    }

                    await ApplyMicrophoneSwitchAsync(deviceId);
                }
            }
            catch (Exception ex)
            {
                lock (_queueLock) _isProcessing = false;

                ConvaiLogger.Warning($"Microphone apply loop failed: {ex.Message}",
                    LogCategory.Audio);
            }
        }

        private async Task ApplyMicrophoneSwitchAsync(string preferredDeviceId)
        {
            if (!TryResolveRoomServices(out IConvaiRoomConnectionService connectionService,
                    out IConvaiRoomAudioService audioService)) return;

            if (!connectionService.IsConnected) return;

            int index = _microphoneDeviceService.ResolvePreferredDeviceIndex(preferredDeviceId);
            if (index < 0)
            {
                ConvaiLogger.Warning("No valid microphone device found for switch.",
                    LogCategory.Audio);
                return;
            }

            await _applySemaphore.WaitAsync();
            try
            {
                await audioService.StartListeningAsync(index);
                ConvaiLogger.Debug($"Switched live microphone to index {index}",
                    LogCategory.Audio);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Warning($"Failed to switch microphone: {ex.Message}",
                    LogCategory.Audio);
            }
            finally
            {
                _applySemaphore.Release();
            }
        }

        private void RunOnMainThread(Action action)
        {
            if (action == null) return;

            if (_scheduler != null && !_scheduler.IsMainThread())
            {
                _scheduler.ScheduleOnMainThread(action);
                return;
            }

            action.Invoke();
        }

        private bool TryResolveRoomServices(
            out IConvaiRoomConnectionService connectionService,
            out IConvaiRoomAudioService audioService)
        {
            connectionService = _connectionService;
            audioService = _audioService;
            return connectionService != null && audioService != null;
        }
    }
}
