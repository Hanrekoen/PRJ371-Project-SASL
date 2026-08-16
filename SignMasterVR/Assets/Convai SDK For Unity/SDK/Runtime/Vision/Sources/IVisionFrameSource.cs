using System;
using UnityEngine;

namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     Represents any source that can provide a steady stream of video frames.
    ///     Implemented by vision capture components for video streaming and debug preview.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the minimal interface required for:
    ///         <list type="bullet">
    ///             <item>Video streaming via LiveKit (ConvaiVisionPublisher)</item>
    ///             <item>Debug preview display (VisionDebugPreview)</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Built-in Implementations:</b>
    ///         <list type="bullet">
    ///             <item><see cref="CameraVisionFrameSource" /> - Unity Camera capture</item>
    ///             <item>WebcamVisionFrameSource (Samples) - Physical webcam capture</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Custom Implementations:</b>
    ///         Developers can create custom vision sources (video files, screen capture, AR passthrough, etc.)
    ///         by implementing this interface. The SDK will automatically support them for:
    ///         <list type="bullet">
    ///             <item>Video streaming to AI characters</item>
    ///             <item>Debug preview visualization</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         Sources with asynchronous permission flow, delayed initialization, or richer failure details should also
    ///         implement <see cref="IVisionFrameSourceStatusProvider" /> so the publisher can react without relying on
    ///         log parsing or indefinite waits.
    ///     </para>
    ///     <para>
    ///         <b>RenderTexture Orientation:</b>
    ///         The <see cref="CurrentRenderTexture" /> must be in top-down orientation (Y-flipped from Unity's
    ///         default bottom-up) for correct video streaming. Use <c>Graphics.Blit</c> with
    ///         <c>scale: Vector2(1, -1)</c> and <c>offset: Vector2(0, 1)</c> to flip the Y-axis.
    ///     </para>
    /// </remarks>
    public interface IVisionFrameSource
    {
        /// <summary>
        ///     Gets a value indicating whether the source is currently capturing/providing frames.
        /// </summary>
        public bool IsCapturing { get; }

        /// <summary>
        ///     Gets the total number of frames captured since capture started.
        /// </summary>
        public long FrameCount { get; }

        /// <summary>
        ///     Gets the current frame dimensions (width, height) in pixels.
        ///     Returns (0, 0) if not yet initialized.
        /// </summary>
        public (int Width, int Height) FrameDimensions { get; }

        /// <summary>
        ///     Gets the target frame rate in frames per second.
        /// </summary>
        public float TargetFrameRate { get; }

        /// <summary>
        ///     Gets an optional source identifier for multi-source scenarios.
        ///     Can be null or empty for single-source setups.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        ///     Gets the RenderTexture containing the current video frame.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This texture must be in top-down orientation (Y-flipped) for correct video streaming.
        ///         LiveKit and standard video formats expect Y=0 at the top of the image.
        ///     </para>
        ///     <para>
        ///         The texture should have a 24-bit depth buffer for compatibility with Unity's render graph API.
        ///     </para>
        /// </remarks>
        public RenderTexture CurrentRenderTexture { get; }

        /// <summary>
        ///     Gets whether the source has produced at least one usable frame for publishing.
        /// </summary>
        public bool IsFrameReady { get; }

        /// <summary>
        ///     Raised when a usable frame is available.
        /// </summary>
        public event Action FrameReady;

        /// <summary>
        ///     Starts frame capture/generation.
        /// </summary>
        /// <remarks>
        ///     After calling this method, <see cref="CurrentRenderTexture" /> will be updated
        ///     with new frames at approximately <see cref="TargetFrameRate" /> per second.
        /// </remarks>
        public void StartCapture();

        /// <summary>
        ///     Stops frame capture/generation.
        /// </summary>
        public void StopCapture();
    }
}
