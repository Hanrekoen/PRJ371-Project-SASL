using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Runtime.Vision.Transport;

namespace Convai.Runtime.Adapters.Vision
{
    /// <summary>
    ///     Runtime adapter that bridges the Unity-free <see cref="IVideoTrackUnpublisher" /> port
    ///     to the Unity-specific <see cref="IVideoTrackManager" /> implementation.
    /// </summary>
    /// <remarks>
    ///     Bridges <see cref="IVideoTrackUnpublisher" /> to <see cref="IVideoTrackManager" />.
    /// </remarks>
    internal class VideoTrackUnpublisherAdapter : IVideoTrackUnpublisher
    {
        private readonly IVideoTrackManager _videoTrackManager;

        public VideoTrackUnpublisherAdapter(IVideoTrackManager videoTrackManager)
        {
            _videoTrackManager = videoTrackManager;
        }

        /// <inheritdoc />
        public bool IsPublishing => _videoTrackManager?.IsPublishing ?? false;

        /// <inheritdoc />
        public Task UnpublishVideoAsync(CancellationToken ct = default)
        {
            if (_videoTrackManager == null) return Task.CompletedTask;

            return _videoTrackManager.UnpublishVideoAsync(ct).AsTask();
        }
    }
}
