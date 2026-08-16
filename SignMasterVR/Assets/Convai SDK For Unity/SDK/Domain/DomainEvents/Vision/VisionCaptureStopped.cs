using System;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Domain event raised when vision capture stops.
    ///     Published via EventHub when an IVisionFrameSource stops capturing frames.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever vision capture is stopped.
    ///     Consumers can subscribe to track when visual context becomes unavailable.
    ///     Use EventDeliveryPolicy.MainThread for UI updates.
    /// </remarks>
    public readonly struct VisionCaptureStopped
    {
        /// <summary>
        ///     Total number of frames captured during the session.
        /// </summary>
        public long TotalFramesCaptured { get; }

        /// <summary>
        ///     When the capture was stopped (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     The reason capture was stopped.
        /// </summary>
        public VisionCaptureStopReason Reason { get; }

        /// <summary>
        ///     Optional source identifier for multi-camera scenarios.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        ///     Optional error message if stopped due to an error.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        ///     Optional structured error code for programmatic error handling.
        /// </summary>
        /// <remarks>
        ///     Error codes are defined in <see cref="Errors.SessionErrorCodes" /> (Vision* constants).
        ///     This is only set when <see cref="Reason" /> is <see cref="VisionCaptureStopReason.Error" />.
        /// </remarks>
        public string ErrorCode { get; }

        /// <summary>
        ///     Creates a new VisionCaptureStopped event.
        /// </summary>
        public VisionCaptureStopped(
            long totalFramesCaptured,
            DateTime timestamp,
            VisionCaptureStopReason reason,
            string sourceId = null,
            string errorMessage = null,
            string errorCode = null)
        {
            TotalFramesCaptured = totalFramesCaptured;
            Timestamp = timestamp;
            Reason = reason;
            SourceId = sourceId;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
        }

        /// <summary>
        ///     Creates a VisionCaptureStopped event with the current UTC timestamp.
        /// </summary>
        /// <param name="totalFramesCaptured">Total frames captured</param>
        /// <param name="reason">Reason for stopping</param>
        /// <param name="sourceId">Optional source identifier</param>
        /// <param name="errorMessage">Optional error message</param>
        /// <param name="errorCode">Optional structured error code from <see cref="Errors.SessionErrorCodes" /> (Vision* constants)</param>
        /// <returns>A new VisionCaptureStopped event</returns>
        public static VisionCaptureStopped Create(
            long totalFramesCaptured,
            VisionCaptureStopReason reason = VisionCaptureStopReason.UserRequested,
            string sourceId = null,
            string errorMessage = null,
            string errorCode = null)
        {
            return new VisionCaptureStopped(
                totalFramesCaptured,
                DateTime.UtcNow,
                reason,
                sourceId,
                errorMessage,
                errorCode
            );
        }

        /// <summary>
        ///     Whether the capture stopped due to an error.
        /// </summary>
        public bool IsError => Reason == VisionCaptureStopReason.Error;

        /// <summary>
        ///     Whether the capture stopped normally (user requested or session ended).
        /// </summary>
        public bool IsNormalStop => Reason == VisionCaptureStopReason.UserRequested ||
                                    Reason == VisionCaptureStopReason.SessionEnded;

        /// <summary>
        ///     Whether a structured error code is available.
        /// </summary>
        public bool HasErrorCode => !string.IsNullOrEmpty(ErrorCode);
    }

    /// <summary>
    ///     Reasons why vision capture may stop.
    /// </summary>
    public enum VisionCaptureStopReason
    {
        /// <summary>User or application requested stop.</summary>
        UserRequested = 0,

        /// <summary>The session or room was disconnected.</summary>
        SessionEnded = 1,

        /// <summary>Camera became unavailable or was destroyed.</summary>
        CameraLost = 2,

        /// <summary>An error occurred during capture.</summary>
        Error = 3,

        /// <summary>The component was disabled or destroyed.</summary>
        ComponentDisabled = 4
    }
}
