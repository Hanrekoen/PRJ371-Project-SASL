using System;

namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     High-level runtime state for a vision frame source.
    /// </summary>
    public enum VisionSourceState
    {
        Idle,
        AwaitingPermission,
        Starting,
        Ready,
        Degraded,
        Stopped,
        Failed
    }

    /// <summary>
    ///     Coarse error classification for source start-up and capture failures.
    /// </summary>
    public enum VisionSourceErrorKind
    {
        None,
        Timeout,
        PermissionDenied,
        UnsupportedPlatform,
        DeviceUnavailable,
        InvalidConfiguration,
        Unknown
    }

    /// <summary>
    ///     Optional companion contract for frame sources that need to expose runtime readiness,
    ///     permission flow, or failure details beyond the minimal texture-stream interface.
    /// </summary>
    public interface IVisionFrameSourceStatusProvider
    {
        /// <summary>
        ///     Gets the current runtime state for the source.
        /// </summary>
        public VisionSourceState State { get; }

        /// <summary>
        ///     Gets the current coarse error classification.
        /// </summary>
        public VisionSourceErrorKind ErrorKind { get; }

        /// <summary>
        ///     Gets an optional human-readable status or error message.
        /// </summary>
        public string StatusMessage { get; }

        /// <summary>
        ///     Gets whether the source believes it has produced a usable frame.
        /// </summary>
        public bool HasUsableFrame { get; }

        /// <summary>
        ///     Raised when state, error kind, readiness, or status message changes.
        /// </summary>
        public event Action StatusChanged;
    }
}
