using System;

namespace Convai.Infrastructure.Networking.Audio
{
    /// <summary>
    ///     Information about an audio track being routed.
    /// </summary>
    internal readonly struct AudioRouteInfo
    {
        /// <summary>Participant ID that owns the audio track.</summary>
        public string ParticipantId { get; }

        /// <summary>Track SID from the transport layer.</summary>
        public string TrackSid { get; }

        /// <summary>Current routing state.</summary>
        public AudioRoutingState State { get; }

        /// <summary>Volume level (0.0 to 1.0).</summary>
        public float Volume { get; }

        /// <summary>Whether the audio is muted.</summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     Creates a new AudioRouteInfo.
        /// </summary>
        public AudioRouteInfo(
            string participantId,
            string trackSid,
            AudioRoutingState state,
            float volume = 1f,
            bool isMuted = false)
        {
            ParticipantId = participantId ?? throw new ArgumentNullException(nameof(participantId));
            TrackSid = trackSid;
            State = state;
            Volume = Math.Clamp(volume, 0f, 1f);
            IsMuted = isMuted;
        }

        /// <summary>Creates an info instance for active routing.</summary>
        public static AudioRouteInfo Active(string participantId, string trackSid, float volume = 1f)
            => new(participantId, trackSid, AudioRoutingState.Active, volume);

        /// <summary>Creates an info instance for stopped routing.</summary>
        public static AudioRouteInfo Stopped(string participantId)
            => new(participantId, null, AudioRoutingState.Stopped, 0f);
    }
}
