using System;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Domain event raised when a vision frame is captured.
    ///     Published via EventHub when a frame has been captured and encoded.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a vision frame is successfully captured.
    ///     Note: This event does NOT contain the frame data itself to avoid memory overhead.
    ///     The actual frame data is passed directly to the video track publisher.
    ///     Use EventDeliveryPolicy.Immediate to avoid blocking the capture pipeline.
    /// </remarks>
    public readonly struct VisionFrameCaptured
    {
        /// <summary>
        ///     The frame width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///     The frame height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        ///     The sequential frame index since capture started.
        /// </summary>
        public long FrameIndex { get; }

        /// <summary>
        ///     The encoded frame size in bytes (e.g., JPEG size).
        /// </summary>
        public int SizeBytes { get; }

        /// <summary>
        ///     When the frame was captured (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional source identifier for multi-camera scenarios.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        ///     Creates a new VisionFrameCaptured event.
        /// </summary>
        public VisionFrameCaptured(
            int width,
            int height,
            long frameIndex,
            int sizeBytes,
            DateTime timestamp,
            string sourceId = null)
        {
            Width = width;
            Height = height;
            FrameIndex = frameIndex;
            SizeBytes = sizeBytes;
            Timestamp = timestamp;
            SourceId = sourceId;
        }

        /// <summary>
        ///     Creates a VisionFrameCaptured event with the current UTC timestamp.
        /// </summary>
        /// <param name="width">Frame width in pixels</param>
        /// <param name="height">Frame height in pixels</param>
        /// <param name="frameIndex">Sequential frame index</param>
        /// <param name="sizeBytes">Encoded frame size in bytes</param>
        /// <param name="sourceId">Optional source identifier</param>
        /// <returns>A new VisionFrameCaptured event</returns>
        public static VisionFrameCaptured Create(
            int width,
            int height,
            long frameIndex,
            int sizeBytes,
            string sourceId = null)
        {
            return new VisionFrameCaptured(
                width,
                height,
                frameIndex,
                sizeBytes,
                DateTime.UtcNow,
                sourceId
            );
        }

        /// <summary>
        ///     Gets the compression ratio if original size is known.
        /// </summary>
        /// <param name="originalBytes">Original uncompressed size in bytes</param>
        /// <returns>Compression ratio (0.0 to 1.0)</returns>
        public float GetCompressionRatio(int originalBytes) =>
            originalBytes > 0 ? (float)SizeBytes / originalBytes : 1f;
    }
}
