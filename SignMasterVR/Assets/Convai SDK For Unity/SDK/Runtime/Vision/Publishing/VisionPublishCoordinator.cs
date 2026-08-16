using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Sources;
using Convai.Runtime.Vision.Transport;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Vision.Publishing
{
    internal sealed class VisionPublishCoordinator : IDisposable
    {
        private enum SourceReadinessResult
        {
            Ready,
            TimedOut,
            Failed
        }

        private static readonly TimeSpan DefaultFrameReadyTimeout = TimeSpan.FromSeconds(2);
        private readonly Func<RuntimePlatform> _platformProvider;
        private readonly TimeSpan _frameReadyTimeout;
        private readonly object _syncRoot = new();

        private IConvaiRoomConnectionService _connectionService;
        private bool _disposed;
        private IEventHub _eventHub;
        private IVisionFrameSource _frameSource;
        private CancellationTokenSource _publishCts;
        private bool? _activeUsesWebGLCanvasPublishPath;
        private bool _hasExplicitPublishingEnabled;
        private bool _publishingEnabled = true;
        private int _publishVersion;
        private bool _subscribedToFrameSource;
        private bool _subscribedToRoomEvents;
        private IVideoSourceFactory _videoSourceFactory;
        private IVideoTrackManager _videoTrackManager;

        public VisionPublishCoordinator(
            Func<RuntimePlatform> platformProvider = null,
            TimeSpan? frameReadyTimeout = null)
        {
            _platformProvider = platformProvider != null ? platformProvider : () => UnityEngine.Application.platform;
            _frameReadyTimeout = frameReadyTimeout ?? DefaultFrameReadyTimeout;
        }

        public bool IsPublishing => _videoTrackManager?.IsPublishing ?? false;

        public VisionPublishPolicy PublishPolicy { get; private set; } = VisionPublishPolicy.AutoCompatible;

        public int FrameRateOverride { get; private set; }

        public int MaxBitrateOverride { get; private set; }

        public string VideoTrackName { get; private set; } = VideoPublishOptions.Default.TrackName;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CancelPendingPublish();
            UnsubscribeFromConnectionEvents();
            UnsubscribeFromFrameSource();

            try
            {
                if (_frameSource != null && _frameSource.IsCapturing)
                    _frameSource.StopCapture();
            }
            catch
            {
                // Best-effort shutdown.
            }

            try
            {
                _videoTrackManager?.Dispose();
            }
            catch
            {
                // Best-effort shutdown.
            }

            _videoTrackManager = null;
        }

        public void ConfigureDependencies(
            IEventHub eventHub,
            IVideoSourceFactory videoSourceFactory,
            IConvaiRoomConnectionService connectionService)
        {
            ThrowIfDisposed();

            _eventHub = eventHub;
            _videoSourceFactory = videoSourceFactory;

            if (ReferenceEquals(_connectionService, connectionService))
                return;

            UnsubscribeFromConnectionEvents();
            _connectionService = connectionService;
            SubscribeToConnectionEvents();
        }

        public void SetFrameSource(IVisionFrameSource frameSource)
        {
            ThrowIfDisposed();

            if (ReferenceEquals(_frameSource, frameSource))
                return;

            UnsubscribeFromFrameSource();
            _frameSource = frameSource;
            SubscribeToFrameSource();
        }

        public void ApplyConfiguration(
            VisionPublishPolicy publishPolicy,
            int frameRateOverride,
            int maxBitrateOverride,
            string videoTrackName)
        {
            ThrowIfDisposed();

            string resolvedTrackName = string.IsNullOrWhiteSpace(videoTrackName)
                ? VideoPublishOptions.Default.TrackName
                : videoTrackName.Trim();
            bool publishSettingsChanged =
                PublishPolicy != publishPolicy ||
                FrameRateOverride != frameRateOverride ||
                MaxBitrateOverride != maxBitrateOverride ||
                VideoTrackName != resolvedTrackName ||
                (_activeUsesWebGLCanvasPublishPath.HasValue &&
                 _activeUsesWebGLCanvasPublishPath.Value != UsesWebGLCanvasPublishPath());
            bool hasActiveOrPendingPublish = _videoTrackManager?.IsPublishing == true || _publishCts != null;

            PublishPolicy = publishPolicy;
            FrameRateOverride = frameRateOverride;
            MaxBitrateOverride = maxBitrateOverride;
            VideoTrackName = resolvedTrackName;

            if (!_hasExplicitPublishingEnabled)
                _publishingEnabled = publishPolicy != VisionPublishPolicy.Manual;

            if (!_publishingEnabled)
            {
                if (hasActiveOrPendingPublish)
                    _ = StopPublishingAsync();
                return;
            }

            if (hasActiveOrPendingPublish && publishSettingsChanged)
            {
                _ = RestartPublishingAsync();
                return;
            }

            SchedulePublish();
        }

        public void SetPublishPolicy(VisionPublishPolicy publishPolicy)
        {
            ThrowIfDisposed();

            ApplyConfiguration(publishPolicy, FrameRateOverride, MaxBitrateOverride, VideoTrackName);
        }

        public void SetPublishingEnabled(bool enabled)
        {
            ThrowIfDisposed();

            _hasExplicitPublishingEnabled = true;
            _publishingEnabled = enabled;
            if (enabled)
                SchedulePublish();
            else
                _ = StopPublishingAsync();
        }

        public ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            SubscribeToConnectionEvents();
            SubscribeToFrameSource();

            if (_publishingEnabled && _connectionService?.IsConnected == true)
                SchedulePublish();

            return default;
        }

        public async ValueTask StopAsync(CancellationToken ct = default)
        {
            if (_disposed)
                return;

            UnsubscribeFromConnectionEvents();
            UnsubscribeFromFrameSource();
            await StopPublishingAsync(ct);
        }

        public async ValueTask StopPublishingAsync(CancellationToken ct = default)
        {
            if (_disposed)
                return;

            CancelPendingPublish();
            if (_frameSource != null && _frameSource.IsCapturing)
                _frameSource.StopCapture();

            if (_videoTrackManager == null)
                return;

            IVideoTrackManager manager = _videoTrackManager;
            _videoTrackManager = null;

            try
            {
                await manager.UnpublishVideoAsync(ct);
            }
            finally
            {
                manager.Dispose();
                _activeUsesWebGLCanvasPublishPath = null;
            }
        }

        private void SubscribeToConnectionEvents()
        {
            if (_subscribedToRoomEvents || _connectionService == null)
                return;

            _connectionService.Connected += OnConnected;
            _connectionService.OnSessionStateChanged += OnSessionStateChanged;
            _subscribedToRoomEvents = true;
        }

        private void UnsubscribeFromConnectionEvents()
        {
            if (!_subscribedToRoomEvents || _connectionService == null)
                return;

            _connectionService.Connected -= OnConnected;
            _connectionService.OnSessionStateChanged -= OnSessionStateChanged;
            _subscribedToRoomEvents = false;
        }

        private void SubscribeToFrameSource()
        {
            if (_subscribedToFrameSource || _frameSource == null)
                return;

            _frameSource.FrameReady += OnFrameReady;
            if (_frameSource is IVisionFrameSourceStatusProvider statusProvider)
                statusProvider.StatusChanged += OnFrameSourceStatusChanged;
            _subscribedToFrameSource = true;
        }

        private void UnsubscribeFromFrameSource()
        {
            if (!_subscribedToFrameSource || _frameSource == null)
                return;

            _frameSource.FrameReady -= OnFrameReady;
            if (_frameSource is IVisionFrameSourceStatusProvider statusProvider)
                statusProvider.StatusChanged -= OnFrameSourceStatusChanged;
            _subscribedToFrameSource = false;
        }

        private void OnConnected() => SchedulePublish();

        private void OnSessionStateChanged(SessionStateChanged stateChanged)
        {
            switch (stateChanged.NewState)
            {
                case SessionState.Connected:
                    SchedulePublish();
                    return;
                case SessionState.Connecting:
                case SessionState.Reconnecting:
                    // Keep coordinator active while transport/session is progressing.
                    // Stopping here would unsubscribe before Connected is emitted.
                    return;
                default:
                    _ = StopPublishingAsync();
                    return;
            }
        }

        private void OnFrameReady() => SchedulePublish();

        private void OnFrameSourceStatusChanged() => SchedulePublish();

        private void SchedulePublish()
        {
            // Publishing is only allowed once Dynamic Vision Context resolves this room to video-capable.
            // This keeps vision components inert in audio-only rooms, tracking ConvaiRoomManager's resolved
            // vision capability (surfaced here as the connection service's ConnectionType).
            if (_disposed || !_publishingEnabled || _connectionService?.IsConnected != true ||
                _videoTrackManager?.IsPublishing == true)
                return;

            // Guard: only publish once the room resolves to video-capable (Dynamic Vision Context enabled).
            if (_connectionService?.ConnectionType != ConvaiConnectionType.Video)
            {
                ConvaiLogger.Debug(
                    "Vision publishing blocked: Dynamic Vision Context is disabled for this room. " +
                    "Set Dynamic Vision Context mode to Enabled (or Auto with Connection Type set to Video) on the ConvaiRoomManager.",
                    LogCategory.Vision);
                return;
            }

            lock (_syncRoot)
            {
                if (_publishCts != null && !_publishCts.IsCancellationRequested)
                    return;

                _publishCts = new CancellationTokenSource();
                _publishVersion++;
                _ = PublishWhenReadyAsync(_publishVersion, _publishCts);
            }
        }

        private async Task PublishWhenReadyAsync(int publishVersion, CancellationTokenSource publishCts)
        {
            CancellationToken ct = publishCts.Token;
            try
            {
                VisionPublishProfile profile = VisionPublishProfileResolver.Resolve(
                    PublishPolicy,
                    FrameRateOverride,
                    MaxBitrateOverride,
                    _platformProvider());

                bool usesWebGLCanvasPublishPath = UsesWebGLCanvasPublishPath();
                if (usesWebGLCanvasPublishPath)
                {
                    bool webGLPublished = await PublishWebGLAsync(profile, ct);
                    if (webGLPublished && !ct.IsCancellationRequested && publishVersion == _publishVersion)
                        _activeUsesWebGLCanvasPublishPath = usesWebGLCanvasPublishPath;
                    return;
                }

                if (_frameSource == null)
                {
                    ConvaiLogger.Error("Frame source is null; cannot publish video.",
                        LogCategory.Vision);
                    return;
                }

                if (!_frameSource.IsCapturing)
                    _frameSource.StartCapture();

                if (!_frameSource.IsCapturing)
                {
                    ConvaiLogger.Error(
                        "Frame source failed to start capture; aborting publish.",
                        LogCategory.Vision);
                    return;
                }

                SourceReadinessResult readinessResult = await WaitForFrameSourceReadyAsync(_frameSource, ct);
                if (readinessResult != SourceReadinessResult.Ready)
                {
                    LogSourceReadinessFailure(readinessResult, _frameSource, _frameReadyTimeout);
                    return;
                }

                if (ct.IsCancellationRequested || publishVersion != _publishVersion)
                    return;

                RenderTexture renderTexture = _frameSource.CurrentRenderTexture;
                if (renderTexture == null)
                {
                    ConvaiLogger.Error("Frame source never produced a RenderTexture.",
                        LogCategory.Vision);
                    return;
                }

                EnsureVideoTrackManager();

                VideoPublishOptions options = profile.ApplyTo(VideoPublishOptions.Default
                    .WithTrackName(VideoTrackName)
                    .WithSource(VideoTrackSource.Camera));

                bool nativePublished = await _videoTrackManager.PublishVideoAsync(renderTexture, options, ct);

                if (nativePublished && !ct.IsCancellationRequested && publishVersion == _publishVersion)
                    _activeUsesWebGLCanvasPublishPath = usesWebGLCanvasPublishPath;
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown or reconfiguration.
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to publish video: {ex.Message}",
                    LogCategory.Vision);
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_publishCts, publishCts))
                        _publishCts = null;

                    publishCts.Dispose();
                }
            }
        }

        private async Task<bool> PublishWebGLAsync(VisionPublishProfile profile, CancellationToken ct)
        {
            if (_videoSourceFactory == null)
                throw new InvalidOperationException("VideoSourceFactory not available.");

            EnsureVideoTrackManager();

            IVideoSource canvasSource = _videoSourceFactory.CreateFromCanvasCapture(VideoTrackName, profile.FrameRate);
            VideoPublishOptions options = profile.ApplyTo(VideoPublishOptions.Default
                .WithTrackName(VideoTrackName)
                .WithSource(VideoTrackSource.ScreenShare));

            return await _videoTrackManager.PublishVideoAsync(canvasSource, options, ct);
        }

        private async Task<SourceReadinessResult> WaitForFrameSourceReadyAsync(
            IVisionFrameSource frameSource,
            CancellationToken ct)
        {
            if (frameSource == null)
                return SourceReadinessResult.Failed;

            IVisionFrameSourceStatusProvider statusProvider = frameSource as IVisionFrameSourceStatusProvider;
            if (IsSourceReady(frameSource, statusProvider))
                return SourceReadinessResult.Ready;
            if (statusProvider?.State == VisionSourceState.Failed)
                return SourceReadinessResult.Failed;

            var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler() => signal.TrySetResult(true);

            frameSource.FrameReady += Handler;
            if (statusProvider != null)
                statusProvider.StatusChanged += Handler;

            TimeSpan timeoutElapsed = TimeSpan.Zero;
            DateTimeOffset? timeoutWindowStart = null;
            try
            {
                while (true)
                {
                    if (IsSourceReady(frameSource, statusProvider))
                        return SourceReadinessResult.Ready;

                    if (statusProvider?.State == VisionSourceState.Failed)
                        return SourceReadinessResult.Failed;

                    bool suspendTimeout = statusProvider?.State == VisionSourceState.AwaitingPermission;
                    if (suspendTimeout)
                    {
                        if (timeoutWindowStart.HasValue)
                        {
                            timeoutElapsed += DateTimeOffset.UtcNow - timeoutWindowStart.Value;
                            timeoutWindowStart = null;
                        }

                        using (ct.Register(() => signal.TrySetCanceled(ct)))
                            await signal.Task;
                    }
                    else
                    {
                        timeoutWindowStart ??= DateTimeOffset.UtcNow;
                        TimeSpan activeElapsed = timeoutElapsed + (DateTimeOffset.UtcNow - timeoutWindowStart.Value);
                        TimeSpan remaining = _frameReadyTimeout - activeElapsed;
                        if (remaining <= TimeSpan.Zero)
                            return SourceReadinessResult.TimedOut;

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        Task completedTask = await Task.WhenAny(signal.Task, Task.Delay(remaining, timeoutCts.Token));
                        if (completedTask != signal.Task)
                            return SourceReadinessResult.TimedOut;

                        timeoutCts.Cancel();
                        await signal.Task;
                    }

                    signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            finally
            {
                frameSource.FrameReady -= Handler;
                if (statusProvider != null)
                    statusProvider.StatusChanged -= Handler;
            }
        }

        private static bool IsSourceReady(
            IVisionFrameSource frameSource,
            IVisionFrameSourceStatusProvider statusProvider)
        {
            if (frameSource?.CurrentRenderTexture == null)
                return false;

            return frameSource.IsFrameReady || statusProvider?.HasUsableFrame == true;
        }

        private static void LogSourceReadinessFailure(
            SourceReadinessResult readinessResult,
            IVisionFrameSource frameSource,
            TimeSpan timeout)
        {
            IVisionFrameSourceStatusProvider statusProvider = frameSource as IVisionFrameSourceStatusProvider;
            string statusDetail = string.IsNullOrWhiteSpace(statusProvider?.StatusMessage)
                ? string.Empty
                : $" {statusProvider.StatusMessage}";

            switch (readinessResult)
            {
                case SourceReadinessResult.TimedOut:
                    ConvaiLogger.Error(
                        $"Frame source did not become ready within {timeout.TotalSeconds:0.#}s.{statusDetail}",
                        LogCategory.Vision);
                    return;
                case SourceReadinessResult.Failed:
                    ConvaiLogger.Error(
                        $"Frame source failed before publish. State={statusProvider?.State}, Error={statusProvider?.ErrorKind}.{statusDetail}",
                        LogCategory.Vision);
                    return;
            }
        }

        private void EnsureVideoTrackManager()
        {
            if (_videoTrackManager != null)
                return;

            if (_connectionService == null)
                throw new InvalidOperationException("Room connection service not available.");

            if (_eventHub == null)
                throw new InvalidOperationException("EventHub not available.");

            if (_videoSourceFactory == null)
                throw new InvalidOperationException("VideoSourceFactory not available.");

            _videoTrackManager = new VideoTrackManager(
                () => _connectionService.CurrentRoom,
                _eventHub,
                null,
                _videoSourceFactory);
        }

        private void CancelPendingPublish()
        {
            lock (_syncRoot)
            {
                _publishVersion++;
                _publishCts?.Cancel();
            }
        }

        private bool UsesWebGLCanvasPublishPath() =>
            _platformProvider() == RuntimePlatform.WebGLPlayer;

        private async Task RestartPublishingAsync()
        {
            await StopPublishingAsync();

            if (!_disposed && _publishingEnabled)
                SchedulePublish();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VisionPublishCoordinator));
        }
    }
}
