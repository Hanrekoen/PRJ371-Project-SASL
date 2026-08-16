using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Vision;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Vision.Transport
{
    /// <summary>
    ///     Manages video track operations for Convai room connections.
    ///     Handles video track publishing and publishes domain events.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    ///     Implements IVideoTrackManager for dependency injection and mocking.
    /// </summary>
    internal class VideoTrackManager : IVideoTrackManager
    {
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly Func<IRoomFacade> _roomFacadeProvider;
        private readonly object _syncRoot = new();
        private readonly IVideoSourceFactory _videoSourceFactory;
        private ILocalVideoTrack _currentVideoTrack;
        private bool _disposed;

        private IVideoSource _videoSource;

        /// <summary>
        ///     Initializes a new instance of the <see cref="VideoTrackManager" /> class.
        /// </summary>
        /// <param name="roomFacadeProvider">Provider function that returns the current room facade instance.</param>
        /// <param name="eventHub">Event hub for publishing domain events.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="videoSourceFactory">Factory for creating video sources (optional, can be null for testing).</param>
        public VideoTrackManager(
            Func<IRoomFacade> roomFacadeProvider,
            IEventHub eventHub,
            ILogger logger,
            IVideoSourceFactory videoSourceFactory = null)
        {
            _roomFacadeProvider = roomFacadeProvider ?? throw new ArgumentNullException(nameof(roomFacadeProvider));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger.WithTag(nameof(VideoTrackManager));
            _videoSourceFactory = videoSourceFactory;
        }

        /// <summary>
        ///     Gets the current room facade instance from the provider.
        /// </summary>
        private IRoomFacade RoomFacade => _roomFacadeProvider();

        /// <inheritdoc />
        public bool IsPublishing => _currentVideoTrack != null;

        /// <inheritdoc />
        public string CurrentTrackName { get; private set; }

        /// <inheritdoc />
        public string CurrentTrackSid { get; private set; }

        /// <inheritdoc />
        public event Action<string, string> OnTrackPublished;

        /// <inheritdoc />
        public event Action<string> OnTrackUnpublished;

        /// <inheritdoc />
        public IConvaiOperation<bool> PublishVideoAsync(
            RenderTexture source,
            VideoPublishOptions options,
            CancellationToken ct = default) =>
            ConvaiOperation<bool>.FromTask(PublishVideoFromRenderTextureAsyncCore(source, options, ct));

        /// <inheritdoc />
        public IConvaiOperation<bool> PublishVideoAsync(
            IVideoSource source,
            VideoPublishOptions options,
            CancellationToken ct = default) =>
            ConvaiOperation<bool>.FromTask(PublishVideoAsyncCore(source, options, ct));

        /// <inheritdoc />
        public IConvaiOperation<Unit> UnpublishVideoAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(UnpublishVideoAsyncCore(ct));

        /// <summary>
        ///     Disposes the video track manager and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            IVideoSource sourceToStop;
            string trackSid;
            string trackName;

            lock (_syncRoot)
            {
                sourceToStop = _videoSource;
                trackSid = CurrentTrackSid;
                trackName = CurrentTrackName;

                _videoSource = null;
                _currentVideoTrack = null;
                CurrentTrackSid = null;
                CurrentTrackName = null;
            }

            sourceToStop?.StopCapture();
            sourceToStop?.Dispose();

            if (trackSid != null)
                PublishTrackUnpublishedEvent(trackSid, trackName, VideoTrackUnpublishReason.ComponentDisabled);

            _logger?.Info("Disposed");
        }

        private async Task<bool> PublishVideoFromRenderTextureAsyncCore(
            RenderTexture source,
            VideoPublishOptions options,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (source == null) throw new ArgumentNullException(nameof(source));

            if (_videoSourceFactory == null)
            {
                _logger?.Error("PublishVideoAsync aborted: VideoSourceFactory is null");
                ConvaiLogger.Error("Cannot publish video: VideoSourceFactory is not configured.",
                    LogCategory.Vision);
                return false;
            }

            IVideoSource videoSource = _videoSourceFactory.CreateFromRenderTexture(source, options.TrackName);
            if (videoSource is ITextureVideoSource textureVideoSource)
                textureVideoSource.TargetFrameRate = options.MaxFrameRate;
            return await PublishVideoAsync(videoSource, options, ct);
        }

        private async Task<bool> PublishVideoAsyncCore(
            IVideoSource source,
            VideoPublishOptions options,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (source == null) throw new ArgumentNullException(nameof(source));

            IRoomFacade room = RoomFacade;
            if (room?.LocalParticipant == null)
            {
                _logger?.Error("PublishVideoAsync aborted: LocalParticipant is null");
                ConvaiLogger.Error(
                    "Cannot publish video: LocalParticipant is null. Ensure the room is fully connected before publishing.",
                    LogCategory.Vision);
                return false;
            }

            bool sourceOwnedByManager = false;
            try
            {
                await UnpublishVideoAsync(ct);
                ct.ThrowIfCancellationRequested();

                if (!source.IsCapturing) source.StartCapture();

                ILocalVideoTrack track = await room.LocalParticipant.PublishVideoTrackAsync(
                    source,
                    options,
                    ct);

                if (track == null)
                {
                    _logger?.Error(
                        "Failed to publish video track - PublishVideoTrackAsync returned null");
                    ConvaiLogger.Error(
                        "Failed to publish video track. Check server logs for details.",
                        LogCategory.Vision);
                    source.StopCapture();
                    source.Dispose();
                    return false;
                }

                lock (_syncRoot)
                {
                    _videoSource = source;
                    _currentVideoTrack = track;
                    CurrentTrackName = options.TrackName;
                    CurrentTrackSid = track.Sid;
                    sourceOwnedByManager = true;
                }

                _logger?.Info(
                    $"Video track '{options.TrackName}' published successfully (SID: {track.Sid})");

                PublishTrackPublishedEvent(track.Sid, options.TrackName);

                OnTrackPublished?.Invoke(options.TrackName, track.Sid);

                return true;
            }
            catch (OperationCanceledException)
            {
                if (!sourceOwnedByManager)
                    CleanupSourceAfterFailedPublish(source, "cancelled publish");

                _logger?.Info("PublishVideoAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                if (!sourceOwnedByManager)
                    CleanupSourceAfterFailedPublish(source, "failed publish");

                _logger?.Error($"Exception in PublishVideoAsync: {ex}");
                ConvaiLogger.Error(
                    $"Exception while publishing video track: {ex.Message}\n{ex.StackTrace}",
                    LogCategory.Vision);
                return false;
            }
        }

        private async Task<Unit> UnpublishVideoAsyncCore(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            ILocalVideoTrack trackToUnpublish;
            IVideoSource sourceToStop;
            string trackSid;
            string trackName;

            lock (_syncRoot)
            {
                if (_currentVideoTrack == null) return Unit.Value;

                trackToUnpublish = _currentVideoTrack;
                sourceToStop = _videoSource;
                trackSid = CurrentTrackSid;
                trackName = CurrentTrackName;

                _currentVideoTrack = null;
                _videoSource = null;
                CurrentTrackSid = null;
                CurrentTrackName = null;
            }

            try
            {
                sourceToStop?.StopCapture();
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    $"Failed to stop video source before unpublish: {ex.Message}");
            }

            try
            {
                IRoomFacade room = RoomFacade;
                if (room?.LocalParticipant != null)
                    await room.LocalParticipant.UnpublishTrackAsync(trackToUnpublish, ct);
            }
            catch (OperationCanceledException)
            {
                _logger?.Info("UnpublishVideoAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Exception in UnpublishVideoAsync: {ex}");
            }
            finally
            {
                try
                {
                    sourceToStop?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        $"Failed to dispose video source during unpublish: {ex.Message}");
                }
            }

            _logger?.Info($"Video track '{trackName}' unpublished (SID: {trackSid})");

            PublishTrackUnpublishedEvent(trackSid, trackName, VideoTrackUnpublishReason.UserRequested);

            OnTrackUnpublished?.Invoke(trackSid);
            return Unit.Value;
        }

        private void PublishTrackPublishedEvent(string trackSid, string trackName)
        {
            try
            {
                var domainEvent = VideoTrackPublished.Create(trackSid, trackName);
                _eventHub.Publish(domainEvent);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to publish VideoTrackPublished event: {ex}");
            }
        }

        private void PublishTrackUnpublishedEvent(string trackSid, string trackName, VideoTrackUnpublishReason reason)
        {
            try
            {
                var domainEvent = VideoTrackUnpublished.Create(trackSid, trackName, reason);
                _eventHub.Publish(domainEvent);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to publish VideoTrackUnpublished event: {ex}");
            }
        }

        private void CleanupSourceAfterFailedPublish(IVideoSource source, string context)
        {
            try
            {
                source.StopCapture();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to stop video source after {context}: {ex.Message}");
            }

            try
            {
                source.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to dispose video source after {context}: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VideoTrackManager));
        }
    }
}
