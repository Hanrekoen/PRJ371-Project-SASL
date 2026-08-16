using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Networking.Media;
using Convai.Shared.Abstractions;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomAudioRuntimeAdapter : IRoomAudioRuntimeAdapter
    {
        private readonly Func<AudioTrackManager> _audioTrackManagerProvider;
        private readonly Func<IEventHub> _eventHubProvider;
        private readonly Func<GameObject> _gameObjectProvider;
        private readonly Func<bool> _isAudioPlaybackActiveProvider;
        private readonly ILogger _logger;
        private readonly Func<ResolvedTurnTakingOptions> _currentResolvedTurnTakingOptionsProvider;
        private readonly Func<IMicrophoneDeviceService> _microphoneDeviceServiceProvider;
        private readonly Func<IMicrophoneSourceFactory> _microphoneSourceFactoryProvider;
        private readonly Func<string> _preferredMicrophoneDeviceIdProvider;
        private readonly Func<bool> _requiresUserGestureProvider;
        private readonly Func<IConvaiRoomController> _roomControllerProvider;
        private readonly Action<IMicrophoneSourceFactory> _setMicrophoneSourceFactory;
        private readonly Func<ITransportProvider> _transportProviderProvider;
        private readonly SemaphoreSlim _microphoneRefreshGate = new(1, 1);
        private readonly object _trackedMicrophoneSourceLock = new();
        private IClientVoiceActivityDetector _clientVoiceActivityDetector;
        private IMicrophoneSource _trackedMicrophoneSource;

        public RoomAudioRuntimeAdapter(
            Func<AudioTrackManager> audioTrackManagerProvider,
            Func<IConvaiRoomController> roomControllerProvider,
            Func<bool> requiresUserGestureProvider,
            Func<bool> isAudioPlaybackActiveProvider,
            Func<IMicrophoneSourceFactory> microphoneSourceFactoryProvider,
            Action<IMicrophoneSourceFactory> setMicrophoneSourceFactory,
            Func<ITransportProvider> transportProviderProvider,
            Func<ResolvedTurnTakingOptions> currentResolvedTurnTakingOptionsProvider,
            Func<string> preferredMicrophoneDeviceIdProvider,
            Func<IMicrophoneDeviceService> microphoneDeviceServiceProvider,
            Func<IEventHub> eventHubProvider,
            Func<GameObject> gameObjectProvider,
            ILogger logger = null)
        {
            _audioTrackManagerProvider = audioTrackManagerProvider ??
                                         throw new ArgumentNullException(nameof(audioTrackManagerProvider));
            _roomControllerProvider =
                roomControllerProvider ?? throw new ArgumentNullException(nameof(roomControllerProvider));
            _requiresUserGestureProvider = requiresUserGestureProvider ??
                                           throw new ArgumentNullException(nameof(requiresUserGestureProvider));
            _isAudioPlaybackActiveProvider = isAudioPlaybackActiveProvider ??
                                             throw new ArgumentNullException(nameof(isAudioPlaybackActiveProvider));
            _microphoneSourceFactoryProvider = microphoneSourceFactoryProvider ??
                                               throw new ArgumentNullException(nameof(microphoneSourceFactoryProvider));
            _setMicrophoneSourceFactory = setMicrophoneSourceFactory ??
                                          throw new ArgumentNullException(nameof(setMicrophoneSourceFactory));
            _transportProviderProvider = transportProviderProvider ??
                                         throw new ArgumentNullException(nameof(transportProviderProvider));
            _currentResolvedTurnTakingOptionsProvider = currentResolvedTurnTakingOptionsProvider ??
                                                        throw new ArgumentNullException(
                                                            nameof(currentResolvedTurnTakingOptionsProvider));
            _preferredMicrophoneDeviceIdProvider = preferredMicrophoneDeviceIdProvider ??
                                                   throw new ArgumentNullException(nameof(preferredMicrophoneDeviceIdProvider));
            _microphoneDeviceServiceProvider = microphoneDeviceServiceProvider ??
                                              throw new ArgumentNullException(nameof(microphoneDeviceServiceProvider));
            _eventHubProvider = eventHubProvider ?? throw new ArgumentNullException(nameof(eventHubProvider));
            _gameObjectProvider = gameObjectProvider ?? throw new ArgumentNullException(nameof(gameObjectProvider));
            _logger = logger.WithTag(nameof(RoomAudioRuntimeAdapter));
        }

        public bool IsMicMuted => _audioTrackManagerProvider()?.IsMicMuted ?? false;

        public void SetMicMuted(bool muted)
        {
            AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
            if (audioTrackManager != null)
                audioTrackManager.SetMicMuted(muted);
            else
                _roomControllerProvider()?.SetMicMuted(muted);
        }

        public async Task StartListeningAsync(int microphoneIndex, CancellationToken ct)
        {
            await _microphoneRefreshGate.WaitAsync(ct);
            try
            {
                await PublishMicrophoneAsync(microphoneIndex, publishSessionErrors: true, ct).ConfigureAwait(false);
            }
            finally
            {
                _microphoneRefreshGate.Release();
            }
        }

        public async Task StopListeningAsync(CancellationToken ct)
        {
            await _microphoneRefreshGate.WaitAsync(ct);
            try
            {
                UntrackCurrentMicrophoneSource();
                AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
                if (audioTrackManager != null)
                    await audioTrackManager.UnpublishMicrophoneAsync().ConfigureAwait(false);
            }
            finally
            {
                _microphoneRefreshGate.Release();
            }
        }

        internal async Task EnsurePublishedMicrophoneAsync(bool republishIfAlreadyPublished, CancellationToken ct)
        {
            await _microphoneRefreshGate.WaitAsync(ct);
            try
            {
                AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
                if (audioTrackManager == null || !(_roomControllerProvider()?.IsConnectedToRoom ?? false))
                    return;

                IMicrophoneSource trackedSource;
                lock (_trackedMicrophoneSourceLock)
                {
                    trackedSource = _trackedMicrophoneSource;
                }

                if (trackedSource != null && !republishIfAlreadyPublished)
                    return;

                IMicrophoneSourceFactory microphoneFactory = ResolveMicrophoneFactory();
                if (microphoneFactory == null)
                    return;

                int microphoneIndex = trackedSource != null
                    ? ResolveRecoveryMicrophoneIndex(
                        trackedSource,
                        MicrophoneSessionInvalidationReason.RouteChanged,
                        microphoneFactory)
                    : ResolvePreferredMicrophoneIndex(microphoneFactory);

                await PublishMicrophoneAsync(microphoneIndex, publishSessionErrors: true, ct).ConfigureAwait(false);
            }
            finally
            {
                _microphoneRefreshGate.Release();
            }
        }

        private async Task PublishMicrophoneAsync(int microphoneIndex, bool publishSessionErrors, CancellationToken ct)
        {
            if (_requiresUserGestureProvider() && !_isAudioPlaybackActiveProvider())
            {
                _logger?.Warning(
                    "Microphone publish aborted because audio playback requires a user gesture.");
                if (publishSessionErrors)
                    _eventHubProvider()?.Publish(SessionError.Create(SessionErrorCodes.AudioMicPermissionDenied,
                        "User gesture required for microphone on this platform", null, true));
                return;
            }

            AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
            if (audioTrackManager == null || !(_roomControllerProvider()?.IsConnectedToRoom ?? false))
            {
                _logger?.Warning(
                    "Microphone publish aborted because the room audio path is not initialized.");
                return;
            }

            IMicrophoneSourceFactory microphoneFactory = ResolveMicrophoneFactory();
            if (microphoneFactory == null)
            {
                _logger?.Error(
                    "Microphone publish failed: IMicrophoneSourceFactory not registered.");
                return;
            }

            ResolvedMicrophoneTarget target = ResolveRequestedTarget(microphoneFactory, microphoneIndex);
            if (target.UsingPlatformDefaultFallback)
                _logger?.Debug(
                    "Microphone enumeration returned no devices; attempting platform default fallback.");

            IMicrophoneSource microphoneSource = null;
            try
            {
                microphoneSource = microphoneFactory.Create(target.DeviceName, target.DeviceIndex, _gameObjectProvider());
            }
            catch (Exception ex)
            {
                _logger?.Error($"Microphone publish failed while creating microphone source: {ex}");
                if (publishSessionErrors)
                    PublishMicrophonePublishFailure(target.UsingPlatformDefaultFallback);
                return;
            }

            microphoneSource.EnableAcousticEchoCancellation =
                (_currentResolvedTurnTakingOptionsProvider() ?? ResolvedTurnTakingOptions.DefaultHandsFree)
                .EnableAcousticEchoCancellation;
            UntrackCurrentMicrophoneSource();
            bool published = await audioTrackManager.RepublishMicrophoneAsync(microphoneSource,
                AudioPublishOptions.DefaultMicrophone).ConfigureAwait(false);

            if (!published)
            {
                RunOnMainThread(() => microphoneSource.Dispose());
                if (publishSessionErrors)
                    PublishMicrophonePublishFailure(target.UsingPlatformDefaultFallback);
                return;
            }

            TrackMicrophoneSource(microphoneSource);
        }

        private void PublishMicrophonePublishFailure(bool usingPlatformDefaultFallback)
        {
            if (usingPlatformDefaultFallback)
            {
                _logger?.Warning(
                    "Microphone enumeration returned no devices and platform default fallback failed.");
                _eventHubProvider()?.Publish(SessionError.Create(SessionErrorCodes.AudioMicUnavailable,
                    "No microphone devices detected", null, true));
                return;
            }

            _logger?.Error("Failed to publish microphone.");
            _eventHubProvider()?.Publish(SessionError.Create(SessionErrorCodes.AudioMicPublishFailed,
                "Failed to publish microphone audio", null, true));
        }

        private void RunOnMainThread(Action action)
        {
            if (action == null)
                return;

            if (UnityScheduler.Instance.IsMainThread())
            {
                action();
                return;
            }

            UnityScheduler.Instance.ScheduleOnMainThread(action);
        }

        private IMicrophoneSourceFactory ResolveMicrophoneFactory()
        {
            IMicrophoneSourceFactory microphoneFactory = _microphoneSourceFactoryProvider();
            if (microphoneFactory != null)
                return microphoneFactory;

            ITransportProvider transportProvider = _transportProviderProvider();
            if (transportProvider == null)
                return null;

            microphoneFactory = transportProvider.CreateMicrophoneFactory();
            _setMicrophoneSourceFactory(microphoneFactory);
            return microphoneFactory;
        }

        private ResolvedMicrophoneTarget ResolveRequestedTarget(IMicrophoneSourceFactory microphoneFactory,
            int microphoneIndex)
        {
            string[] devices = microphoneFactory.GetAvailableDevices() ?? Array.Empty<string>();
            if (devices.Length == 0)
                return new ResolvedMicrophoneTarget(null, 0, true);

            int deviceIndex = Mathf.Clamp(microphoneIndex, 0, devices.Length - 1);
            return new ResolvedMicrophoneTarget(devices[deviceIndex], deviceIndex, false);
        }

        private int ResolvePreferredMicrophoneIndex(IMicrophoneSourceFactory microphoneFactory)
        {
            string[] devices = microphoneFactory.GetAvailableDevices() ?? Array.Empty<string>();
            if (devices.Length == 0)
                return 0;

            IMicrophoneDeviceService microphoneDeviceService = _microphoneDeviceServiceProvider();
            if (microphoneDeviceService == null)
                return 0;

            int preferredIndex =
                microphoneDeviceService.ResolvePreferredDeviceIndex(_preferredMicrophoneDeviceIdProvider());
            if (preferredIndex < 0 || preferredIndex >= devices.Length)
                return 0;

            return preferredIndex;
        }

        private int ResolveRecoveryMicrophoneIndex(IMicrophoneSource source, MicrophoneSessionInvalidationReason reason,
            IMicrophoneSourceFactory microphoneFactory)
        {
            string[] devices = microphoneFactory.GetAvailableDevices() ?? Array.Empty<string>();
            if (devices.Length == 0)
                return 0;

            if (reason == MicrophoneSessionInvalidationReason.DeviceChanged)
            {
                IMicrophoneDeviceService microphoneDeviceService = _microphoneDeviceServiceProvider();
                if (microphoneDeviceService != null)
                {
                    int preferredIndex =
                        microphoneDeviceService.ResolvePreferredDeviceIndex(_preferredMicrophoneDeviceIdProvider());
                    if (preferredIndex >= 0 && preferredIndex < devices.Length)
                        return preferredIndex;
                }

                return 0;
            }

            int matchedIndex = FindDeviceIndex(devices, source?.DeviceName);
            if (matchedIndex >= 0)
                return matchedIndex;

            return Mathf.Clamp(source?.DeviceIndex ?? 0, 0, devices.Length - 1);
        }

        private static int FindDeviceIndex(string[] devices, string deviceName)
        {
            if (devices == null || devices.Length == 0 || string.IsNullOrWhiteSpace(deviceName))
                return -1;

            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i], deviceName, StringComparison.Ordinal) ||
                    string.Equals(devices[i], deviceName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private void TrackMicrophoneSource(IMicrophoneSource microphoneSource)
        {
            IMicrophoneSource previousSource;
            lock (_trackedMicrophoneSourceLock)
            {
                previousSource = _trackedMicrophoneSource;
                _trackedMicrophoneSource = microphoneSource;
            }

            if (previousSource != null)
                previousSource.SessionInvalidated -= HandleTrackedMicrophoneInvalidated;

            if (microphoneSource != null)
            {
                microphoneSource.SessionInvalidated += HandleTrackedMicrophoneInvalidated;
                StartClientVoiceActivityDetection(microphoneSource);
            }
        }

        private void UntrackCurrentMicrophoneSource()
        {
            IClientVoiceActivityDetector detector;
            IMicrophoneSource previousSource;
            lock (_trackedMicrophoneSourceLock)
            {
                detector = _clientVoiceActivityDetector;
                _clientVoiceActivityDetector = null;
                previousSource = _trackedMicrophoneSource;
                _trackedMicrophoneSource = null;
            }

            if (previousSource != null)
                previousSource.SessionInvalidated -= HandleTrackedMicrophoneInvalidated;

            if (detector != null)
                RunOnMainThread(detector.Dispose);
        }

        private void StartClientVoiceActivityDetection(IMicrophoneSource microphoneSource)
        {
            ResolvedTurnTakingOptions options =
                _currentResolvedTurnTakingOptionsProvider() ?? ResolvedTurnTakingOptions.DefaultHandsFree;
            if (options.ClientBargeInMode != Room.ClientBargeInMode.Silero)
                return;

            if (microphoneSource is not IMicrophonePcmSource pcmSource)
            {
                _logger?.Warning(
                    "Client-side Silero VAD is enabled but the active microphone transport does not expose PCM. Server interruption remains active.");
                return;
            }

            IClientVoiceActivityDetectorFactory factory = ClientVoiceActivityDetectorFactoryRegistry.Factory;
            if (factory == null)
            {
                _logger?.Warning(
                    "Client-side Silero VAD is enabled but its optional inference module is unavailable. Install com.unity.ai.inference 2.2.1 or newer; server interruption remains active.");
                return;
            }

            RunOnMainThread(() =>
            {
                lock (_trackedMicrophoneSourceLock)
                {
                    if (!ReferenceEquals(_trackedMicrophoneSource, microphoneSource) ||
                        _clientVoiceActivityDetector != null)
                        return;
                }

                IClientVoiceActivityDetector detector;
                try
                {
                    detector = factory.Create(
                        pcmSource,
                        _gameObjectProvider(),
                        () => _audioTrackManagerProvider()?.HasActiveCharacterAudioPlayback ?? false,
                        state => _eventHubProvider()?.Publish(state),
                        _logger);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        $"Client-side Silero VAD could not start ({ex.Message}). Server interruption remains active.");
                    return;
                }

                lock (_trackedMicrophoneSourceLock)
                {
                    if (ReferenceEquals(_trackedMicrophoneSource, microphoneSource) &&
                        _clientVoiceActivityDetector == null)
                    {
                        _clientVoiceActivityDetector = detector;
                        return;
                    }
                }

                detector?.Dispose();
            });
        }

        private void HandleTrackedMicrophoneInvalidated(IMicrophoneSource microphoneSource,
            MicrophoneSessionInvalidationReason reason)
        {
            _ = HandleTrackedMicrophoneInvalidatedAsync(microphoneSource, reason);
        }

        private async Task HandleTrackedMicrophoneInvalidatedAsync(IMicrophoneSource microphoneSource,
            MicrophoneSessionInvalidationReason reason)
        {
            if (microphoneSource == null)
                return;

            lock (_trackedMicrophoneSourceLock)
            {
                if (!ReferenceEquals(_trackedMicrophoneSource, microphoneSource))
                    return;
            }

            if (!await _microphoneRefreshGate.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                lock (_trackedMicrophoneSourceLock)
                {
                    if (!ReferenceEquals(_trackedMicrophoneSource, microphoneSource))
                        return;
                }

                AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
                if (audioTrackManager == null || !audioTrackManager.IsCurrentMicrophoneSource(microphoneSource))
                    return;

                IMicrophoneSourceFactory microphoneFactory = ResolveMicrophoneFactory();
                if (microphoneFactory == null)
                    return;

                int recoveryIndex = ResolveRecoveryMicrophoneIndex(microphoneSource, reason, microphoneFactory);
                _logger?.Info(
                    $"Recovering live microphone after {reason} (deviceIndex={recoveryIndex}).");
                await PublishMicrophoneAsync(recoveryIndex, publishSessionErrors: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                _microphoneRefreshGate.Release();
            }
        }

        public bool SetCharacterMuted(string characterId, bool muted)
        {
            if (string.IsNullOrEmpty(characterId))
                return false;

            AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
            if (audioTrackManager != null)
            {
                audioTrackManager.SetCharacterAudioMuted(characterId, muted);
                return true;
            }

            return _roomControllerProvider()?.SetCharacterAudioMuted(characterId, muted) ?? false;
        }

        public bool IsCharacterMuted(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return false;

            AudioTrackManager audioTrackManager = _audioTrackManagerProvider();
            if (audioTrackManager != null)
                return audioTrackManager.IsCharacterAudioMuted(characterId);

            return _roomControllerProvider()?.IsCharacterAudioMuted(characterId) ?? false;
        }

        private readonly struct ResolvedMicrophoneTarget
        {
            internal ResolvedMicrophoneTarget(string deviceName, int deviceIndex, bool usingPlatformDefaultFallback)
            {
                DeviceName = deviceName;
                DeviceIndex = deviceIndex;
                UsingPlatformDefaultFallback = usingPlatformDefaultFallback;
            }

            internal string DeviceName { get; }
            internal int DeviceIndex { get; }
            internal bool UsingPlatformDefaultFallback { get; }
        }
    }
}
