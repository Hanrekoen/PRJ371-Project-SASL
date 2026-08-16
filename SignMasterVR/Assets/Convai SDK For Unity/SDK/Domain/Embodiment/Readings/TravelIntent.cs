using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    ///     Where the character is going, as the rest of the embodiment stack needs to see it:
    ///     a smoothed direction of travel, how fast, how much is left, and what the journey is about.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a <em>reading</em>, not a command — the publisher states what is happening and
    ///         consumers decide what to do about it. Gaze is the first consumer (eyes on the path,
    ///         periodic check-ins on the subject); nothing here is gaze-specific.
    ///     </para>
    ///     <para>
    ///         <b><see cref="Direction" /> is already smoothed by the publisher, on purpose.</b> The
    ///         raw steering direction of a path-following agent steps discontinuously every time a
    ///         corner is passed. Handed to gaze unsmoothed, the derived look point would jump far
    ///         enough to trip the target-teleport test and fire a re-acquisition saccade and blink on
    ///         a target that never actually changed. The seam publishes intent, never agent internals.
    ///     </para>
    /// </remarks>
    internal readonly struct TravelIntent
    {
        /// <summary>Not going anywhere. The identity value every consumer degrades to.</summary>
        public static readonly TravelIntent None = default;

        /// <summary>
        ///     Whether the character is deliberately going somewhere. False when idle, while merely
        ///     settling at the end of a move, and during a turn in place.
        /// </summary>
        public bool IsTraveling { get; }

        /// <summary>
        ///     Smoothed, ground-projected unit vector of travel in world space.
        ///     <see cref="Vector3.zero" /> when not traveling.
        /// </summary>
        public Vector3 Direction { get; }

        /// <summary>
        ///     Live speed normalized against the character's own jog speed, clamped to 0..1. Drives
        ///     how far ahead a traveler looks and how often it checks in.
        /// </summary>
        public float Speed01 { get; }

        /// <summary>
        ///     Metres left to travel, or <see cref="float.PositiveInfinity" /> when the journey has
        ///     no known end (following someone, patrolling, wandering).
        /// </summary>
        public float RemainingDistance { get; }

        /// <summary>World-space point of what the journey is about. Only meaningful with <see cref="HasSubject" />.</summary>
        public Vector3 SubjectPosition { get; }

        /// <summary>What kind of thing <see cref="SubjectPosition" /> is, if anything.</summary>
        public TravelSubjectKind SubjectKind { get; }

        /// <summary>Whether a subject was declared for this journey.</summary>
        public bool HasSubject => SubjectKind != TravelSubjectKind.None;

        public TravelIntent(
            bool isTraveling,
            Vector3 direction,
            float speed01,
            float remainingDistance,
            Vector3 subjectPosition,
            TravelSubjectKind subjectKind)
        {
            IsTraveling = isTraveling;
            Direction = direction;
            Speed01 = speed01 < 0f ? 0f : speed01 > 1f ? 1f : speed01;
            RemainingDistance = remainingDistance;
            SubjectPosition = subjectPosition;
            SubjectKind = subjectKind;
        }
    }
}
