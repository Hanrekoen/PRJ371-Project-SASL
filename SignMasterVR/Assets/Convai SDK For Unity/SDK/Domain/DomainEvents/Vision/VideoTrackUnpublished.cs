using System;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Domain event raised when a video track is unpublished from the room.
    ///     Published via EventHub when a video track is removed.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a video track is unpublished
    ///     from the LiveKit room. It indicates that visual context is no longer being
    ///     sent to the Convai server.
    ///     Use EventDeliveryPolicy.MainThread for UI updates.
    /// </remarks>
    public readonly struct VideoTrackUnpublished
    {
        /// <summary>
        ///     The LiveKit track session ID (SID) that was unpublished.
        /// </summary>
        public string TrackSid { get; }

        /// <summary>
        ///     The track name (typically "vision" or "camera").
        /// </summary>
        public string TrackName { get; }

        /// <summary>
        ///     When the track was unpublished (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     The reason the track was unpublished.
        /// </summary>
        public VideoTrackUnpublishReason Reason { get; }

        /// <summary>
        ///     The room session ID where the track was unpublished.
        /// </summary>
        public string RoomSessionId { get; }

        /// <summary>
        ///     Creates a new VideoTrackUnpublished event.
        /// </summary>
        public VideoTrackUnpublished(
            string trackSid,
            string trackName,
            DateTime timestamp,
            VideoTrackUnpublishReason reason,
            string roomSessionId = null)
        {
            TrackSid = trackSid ?? throw new ArgumentNullException(nameof(trackSid));
            TrackName = trackName ?? "vision";
            Timestamp = timestamp;
            Reason = reason;
            RoomSessionId = roomSessionId;
        }

        /// <summary>
        ///     Creates a VideoTrackUnpublished event with the current UTC timestamp.
        /// </summary>
        /// <param name="trackSid">The LiveKit track SID</param>
        /// <param name="trackName">The track name</param>
        /// <param name="reason">The unpublish reason</param>
        /// <param name="roomSessionId">Optional room session ID</param>
        /// <returns>A new VideoTrackUnpublished event</returns>
        public static VideoTrackUnpublished Create(
            string trackSid,
            string trackName = "vision",
            VideoTrackUnpublishReason reason = VideoTrackUnpublishReason.UserRequested,
            string roomSessionId = null)
        {
            return new VideoTrackUnpublished(
                trackSid,
                trackName,
                DateTime.UtcNow,
                reason,
                roomSessionId
            );
        }

        /// <summary>
        ///     Whether this is the default vision track.
        /// </summary>
        public bool IsVisionTrack => string.Equals(TrackName, "vision", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Whether the track was unpublished normally.
        /// </summary>
        public bool IsNormalUnpublish => Reason == VideoTrackUnpublishReason.UserRequested ||
                                         Reason == VideoTrackUnpublishReason.SessionEnded;
    }

    /// <summary>
    ///     Reasons why a video track may be unpublished.
    /// </summary>
    public enum VideoTrackUnpublishReason
    {
        /// <summary>User or application requested unpublish.</summary>
        UserRequested = 0,

        /// <summary>The session or room was disconnected.</summary>
        SessionEnded = 1,

        /// <summary>The track source became unavailable.</summary>
        SourceLost = 2,

        /// <summary>An error occurred with the track.</summary>
        Error = 3,

        /// <summary>The component was disabled or destroyed.</summary>
        ComponentDisabled = 4
    }
}
