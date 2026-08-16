using System;
using Convai.Infrastructure.Networking.Transport;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Information about a track publication.
    /// </summary>
    public readonly struct TrackPublicationInfo : IEquatable<TrackPublicationInfo>
    {
        /// <summary>
        ///     Unique identifier for the track.
        /// </summary>
        public string TrackSid { get; }

        /// <summary>
        ///     Name of the track.
        /// </summary>
        public string TrackName { get; }

        /// <summary>
        ///     Kind of track (audio, video, etc).
        /// </summary>
        public TrackKind Kind { get; }

        /// <summary>
        ///     Whether the track is muted.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     Whether the track is subscribed (for remote tracks).
        /// </summary>
        public bool IsSubscribed { get; }

        /// <summary>
        ///     Creates a new track publication info.
        /// </summary>
        public TrackPublicationInfo(string trackSid, string trackName, TrackKind kind, bool isMuted = false,
            bool isSubscribed = false)
        {
            TrackSid = trackSid ?? string.Empty;
            TrackName = trackName ?? string.Empty;
            Kind = kind;
            IsMuted = isMuted;
            IsSubscribed = isSubscribed;
        }

        public bool Equals(TrackPublicationInfo other) => TrackSid == other.TrackSid;

        public override bool Equals(object obj) => obj is TrackPublicationInfo other && Equals(other);

        public override int GetHashCode() => TrackSid?.GetHashCode() ?? 0;
    }
}
