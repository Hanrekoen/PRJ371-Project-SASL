using System;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Domain event raised when vision capture starts.
    ///     Published via EventHub when an IVisionFrameSource begins capturing frames.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever vision capture is initiated.
    ///     Consumers can subscribe to track when visual context becomes available.
    ///     Use EventDeliveryPolicy.MainThread for UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct VisionCaptureStarted
    {
        /// <summary>
        ///     The capture width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///     The capture height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        ///     The capture frame rate in frames per second.
        /// </summary>
        public float FramesPerSecond { get; }

        /// <summary>
        ///     When the capture was started (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional source identifier for multi-camera scenarios.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        ///     Creates a new VisionCaptureStarted event.
        /// </summary>
        public VisionCaptureStarted(
            int width,
            int height,
            float framesPerSecond,
            DateTime timestamp,
            string sourceId = null)
        {
            Width = width;
            Height = height;
            FramesPerSecond = framesPerSecond;
            Timestamp = timestamp;
            SourceId = sourceId;
        }

        /// <summary>
        ///     Creates a VisionCaptureStarted event with the current UTC timestamp.
        /// </summary>
        /// <param name="width">Capture width in pixels</param>
        /// <param name="height">Capture height in pixels</param>
        /// <param name="framesPerSecond">Capture frame rate</param>
        /// <param name="sourceId">Optional source identifier</param>
        /// <returns>A new VisionCaptureStarted event</returns>
        public static VisionCaptureStarted Create(
            int width,
            int height,
            float framesPerSecond,
            string sourceId = null)
        {
            return new VisionCaptureStarted(
                width,
                height,
                framesPerSecond,
                DateTime.UtcNow,
                sourceId
            );
        }

        /// <summary>
        ///     Gets the aspect ratio of the capture.
        /// </summary>
        public float AspectRatio => Height > 0 ? (float)Width / Height : 0f;

        /// <summary>
        ///     Gets the total pixels per frame.
        /// </summary>
        public int TotalPixels => Width * Height;
    }
}
