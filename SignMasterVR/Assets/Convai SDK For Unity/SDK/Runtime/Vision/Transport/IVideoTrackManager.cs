using System;
using System.Threading;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using UnityEngine;

namespace Convai.Runtime.Vision.Transport
{
    /// <summary>
    ///     Manages video track operations for Convai room connections.
    ///     Handles video track publishing and unpublishing to LiveKit rooms.
    ///     Mirrors AudioTrackManager pattern for consistency.
    /// </summary>
    /// <remarks>
    ///     Implementations should:
    ///     - Handle LiveKit room connection state
    ///     - Publish domain events via EventHub on track state changes
    ///     - Support async cancellation
    ///     - Be thread-safe for concurrent access
    /// </remarks>
    public interface IVideoTrackManager : IDisposable
    {
        /// <summary>
        ///     Gets a value indicating whether a video track is currently being published.
        /// </summary>
        public bool IsPublishing { get; }

        /// <summary>
        ///     Gets the name of the currently published video track, or null if not publishing.
        /// </summary>
        public string CurrentTrackName { get; }

        /// <summary>
        ///     Gets the session ID (SID) of the currently published track, or null if not publishing.
        /// </summary>
        public string CurrentTrackSid { get; }

        /// <summary>
        ///     Publishes a video track to the LiveKit room using the specified Unity <see cref="RenderTexture" />.
        ///     This is the native-platform convenience overload.
        /// </summary>
        /// <param name="source">The RenderTexture to capture and publish as video.</param>
        /// <param name="options">Options controlling the video encoding and track configuration.</param>
        /// <param name="ct">Cancellation token to cancel the publish operation.</param>
        /// <returns>
        ///     A task that completes with <c>true</c> if the track was published successfully;
        ///     <c>false</c> if publishing failed (e.g., no room connection, already publishing).
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="source" /> is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
        public IConvaiOperation<bool> PublishVideoAsync(
            RenderTexture source,
            VideoPublishOptions options,
            CancellationToken ct = default);

        /// <summary>
        ///     Publishes a video track to the LiveKit room using a platform-specific video source.
        ///     Use this overload for WebGL canvas capture and other non-RenderTexture publishing paths.
        /// </summary>
        /// <param name="source">The platform-specific video source to publish.</param>
        /// <param name="options">Options controlling the video encoding and track configuration.</param>
        /// <param name="ct">Cancellation token to cancel the publish operation.</param>
        /// <returns>
        ///     A task that completes with <c>true</c> if the track was published successfully;
        ///     <c>false</c> if publishing failed.
        /// </returns>
        public IConvaiOperation<bool> PublishVideoAsync(
            IVideoSource source,
            VideoPublishOptions options,
            CancellationToken ct = default);

        /// <summary>
        ///     Unpublishes the currently published video track from the LiveKit room.
        /// </summary>
        /// <param name="ct">Cancellation token to cancel the unpublish operation.</param>
        /// <returns>A task that completes when the track has been unpublished.</returns>
        /// <remarks>
        ///     If no track is currently published, this method returns immediately.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
        public IConvaiOperation<Unit> UnpublishVideoAsync(CancellationToken ct = default);

        /// <summary>
        ///     Raised when a video track is successfully published.
        /// </summary>
        public event Action<string, string> OnTrackPublished;

        /// <summary>
        ///     Raised when a video track is unpublished.
        /// </summary>
        public event Action<string> OnTrackUnpublished;
    }
}
