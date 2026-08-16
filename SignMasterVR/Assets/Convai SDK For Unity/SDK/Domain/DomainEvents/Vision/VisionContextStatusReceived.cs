using System;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Backend acknowledgement of a <c>vision-status</c> query, describing the state of the
    ///     session's dynamic-vision frame buffer.
    /// </summary>
    public readonly struct VisionContextStatusReceived
    {
        private VisionContextStatusReceived(
            string status,
            string message,
            string updateId,
            string outcome,
            string activeSource,
            string activeSourceLabel,
            int lastFrameAgeMs,
            JObject rawExtras,
            DateTime timestamp)
        {
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            UpdateId = updateId ?? string.Empty;
            Outcome = outcome ?? string.Empty;
            ActiveSource = activeSource ?? string.Empty;
            ActiveSourceLabel = activeSourceLabel ?? string.Empty;
            LastFrameAgeMs = lastFrameAgeMs;
            RawExtras = rawExtras;
            Timestamp = timestamp;
        }

        /// <summary>Response status: <c>success</c>, <c>error</c>, <c>processing</c> or <c>pending</c>.</summary>
        public string Status { get; }

        /// <summary>Optional human-readable message accompanying the response.</summary>
        public string Message { get; }

        /// <summary>Echo of the request's idempotency key.</summary>
        public string UpdateId { get; }

        /// <summary>
        ///     Buffer outcome: <c>frames_available</c>, <c>buffer_empty</c>, <c>no_active_video</c> or
        ///     <c>vision_not_enabled</c>.
        /// </summary>
        public string Outcome { get; }

        /// <summary>Participant id of the video source the backend selected for this session, if any.</summary>
        public string ActiveSource { get; }

        /// <summary>Source label of the selected video publisher (e.g. webcam, canvas, screen).</summary>
        public string ActiveSourceLabel { get; }

        /// <summary>Age of the newest buffered frame in milliseconds; 0 when unknown or no frames are buffered.</summary>
        public int LastFrameAgeMs { get; }

        /// <summary>
        ///     Full extras payload, including the <c>vision_buffer</c> diagnostics object (retained
        ///     frames, PTS window, drop counters) for fields without typed accessors.
        /// </summary>
        public JObject RawExtras { get; }

        /// <summary>UTC time this event was created on the client.</summary>
        public DateTime Timestamp { get; }

        public static VisionContextStatusReceived Create(string status, string message, JObject extras)
        {
            extras ??= new JObject();
            return new VisionContextStatusReceived(
                status,
                message,
                ExtrasReader.ReadString(extras, "update_id"),
                ExtrasReader.ReadString(extras, "vision_status_outcome"),
                ExtrasReader.ReadString(extras, "active_source"),
                ExtrasReader.ReadString(extras, "active_source_label"),
                ExtrasReader.ReadInt(extras, "last_frame_age_ms"),
                extras,
                DateTime.UtcNow);
        }
    }
}
