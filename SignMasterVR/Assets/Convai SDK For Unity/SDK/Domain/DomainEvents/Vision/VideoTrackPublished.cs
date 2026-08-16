using System;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Domain event raised when a video track is published to the room.
    ///     Published via EventHub when a video track becomes available.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a video track is successfully
    ///     published to the LiveKit room. It indicates that visual context is now being
    ///     sent to the Convai server.
    ///     Use EventDeliveryPolicy.MainThread for UI updates.
    /// </remarks>
    public readonly struct VideoTrackPublished
    {
        /// <summary>
        ///     The LiveKit track session ID (SID).
        /// </summary>
        public string TrackSid { get; }

        /// <summary>
        ///     The track name (typically "vision" or "camera").
        /// </summary>
        public string TrackName { get; }

        /// <summary>
        ///     When the track was published (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     The room session ID where the track was published.
        /// </summary>
        public string RoomSessionId { get; }

        /// <summary>
        ///     Creates a new VideoTrackPublished event.
        /// </summary>
        public VideoTrackPublished(
            string trackSid,
            string trackName,
            DateTime timestamp,
            string roomSessionId = null)
        {
            TrackSid = trackSid ?? throw new ArgumentNullException(nameof(trackSid));
            TrackName = trackName ?? "vision";
            Timestamp = timestamp;
            RoomSessionId = roomSessionId;
        }

        /// <summary>
        ///     Creates a VideoTrackPublished event with the current UTC timestamp.
        /// </summary>
        /// <param name="trackSid">The LiveKit track SID</param>
        /// <param name="trackName">The track name</param>
        /// <param name="roomSessionId">Optional room session ID</param>
        /// <returns>A new VideoTrackPublished event</returns>
        public static VideoTrackPublished Create(
            string trackSid,
            string trackName = "vision",
            string roomSessionId = null)
        {
            return new VideoTrackPublished(
                trackSid,
                trackName,
                DateTime.UtcNow,
                roomSessionId
            );
        }

        /// <summary>
        ///     Whether this is the default vision track.
        /// </summary>
        public bool IsVisionTrack => string.Equals(TrackName, "vision", StringComparison.OrdinalIgnoreCase);
    }
}
