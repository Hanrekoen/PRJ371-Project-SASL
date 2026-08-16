using System.Threading;
using System.Threading.Tasks;

namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Abstraction for video track unpublishing operations.
    ///     Used to stop video streams without depending on Unity-specific types.
    /// </summary>
    /// <remarks>
    ///     Typically backed by a runtime video track manager.
    /// </remarks>
    public interface IVideoTrackUnpublisher
    {
        /// <summary>
        ///     Gets a value indicating whether a video track is currently being published.
        /// </summary>
        public bool IsPublishing { get; }

        /// <summary>
        ///     Unpublishes the currently published video track.
        /// </summary>
        /// <param name="ct">Cancellation token to cancel the unpublish operation.</param>
        /// <returns>A task that completes when the track has been unpublished.</returns>
        /// <remarks>
        ///     If no track is currently published, this method returns immediately.
        /// </remarks>
        public Task UnpublishVideoAsync(CancellationToken ct = default);
    }
}
