using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Providers;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     How a walking character looks: eyes down the road, a glance at what the journey is about
    ///     every few seconds, and a settle onto it on arrival.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two outputs per tick, and nothing else. <see cref="TryBuildPathCandidate" /> offers the
    ///         path ahead as an ordinary gaze candidate, so the arbiter's acquisition ramps, interest
    ///         budget and point smoothing all apply to it unchanged. <see cref="TickCheckIn" />
    ///         returns whether a glance at the subject is due, which the controller turns into a
    ///         glance-tier scripted request — the same mechanism the curiosity and character-glance
    ///         directors already use, so an explicit <c>GazeAt</c> and an eye-contact lock both stay
    ///         sovereign over it with no new special-casing.
    ///     </para>
    ///     <para>
    ///         Arrival is not a special case: the path candidate's relevance fades to zero across the
    ///         arrival window and normal arbitration lands on whatever is actually there — the
    ///         destination object, the action's target, the player. The settle beat falls out of
    ///         hand-off rather than being scripted.
    ///     </para>
    /// </remarks>
    internal sealed class TravelGazeDirector
    {
        /// <summary>
        ///     Stable identity for the path candidate.
        /// </summary>
        /// <remarks>
        ///     <b>Load-bearing.</b> The path is a point with no transform, and the arbiter keys such
        ///     candidates by rounded world position when they have no name
        ///     (<c>GazeTargetArbiter.CandidateKey</c>). The point moves with the character every
        ///     frame, so a position-keyed path candidate would look like a brand-new target on every
        ///     tick — bumping the generation id and firing a re-acquisition saccade and blink, every
        ///     frame, forever. A constant name makes the key constant.
        /// </remarks>
        private const string PathCandidateName = "Path ahead";

        /// <summary>Relevance ramp-out floor below which the candidate is withdrawn entirely.</summary>
        private const float MinimumUsefulRelevance = 0.02f;

        private float _engageRamp;
        private float _checkInCountdown;
        private bool _armed;

        /// <summary>Whether travel gaze produced a path candidate on the last tick (diagnostics).</summary>
        public bool IsActive { get; private set; }

        /// <summary>Seconds until the next check-in glance, for the inspector's live row.</summary>
        public float SecondsToNextCheckIn => _checkInCountdown;

        public void Reset()
        {
            _engageRamp = 0f;
            _checkInCountdown = 0f;
            _armed = false;
            IsActive = false;
        }

        /// <summary>
        ///     Advances the engagement ramp and produces this tick's path candidate.
        /// </summary>
        /// <param name="intent">Current travel reading; not travelling withdraws the candidate.</param>
        /// <param name="characterRoot">Character root — the point is measured from here.</param>
        /// <param name="eyeHeight">Height above the root the look point sits at.</param>
        /// <param name="profile">Timing and tuning source.</param>
        /// <param name="deltaTime">Tick delta.</param>
        /// <param name="candidate">The produced candidate when this returns <c>true</c>.</param>
        public bool TryBuildPathCandidate(
            in TravelIntent intent,
            Transform characterRoot,
            float eyeHeight,
            ConvaiGazeProfile profile,
            float deltaTime,
            out GazeTargetCandidate candidate)
        {
            candidate = default;

            bool wants = profile != null &&
                         profile.EnableTravelGaze &&
                         intent.IsTraveling &&
                         characterRoot != null &&
                         intent.Direction.sqrMagnitude > 1e-6f;

            float engageSeconds = profile != null ? Mathf.Max(0.01f, profile.TravelEngageSeconds) : 0.35f;
            _engageRamp = Mathf.MoveTowards(_engageRamp, wants ? 1f : 0f, deltaTime / engageSeconds);

            if (!wants && _engageRamp <= MinimumUsefulRelevance)
            {
                IsActive = false;
                return false;
            }

            // Arrival: fade out across the last stretch so the hand-off to whatever is actually at
            // the destination is a decay, never a snap or a frame with no candidate at all.
            float arrivalScale = wants ? ResolveArrivalScale(intent, profile) : 0f;
            float relevance = _engageRamp * arrivalScale;
            if (relevance <= MinimumUsefulRelevance)
            {
                IsActive = false;
                return false;
            }

            float lookAhead = Mathf.Lerp(
                profile.PathLookAheadMinMeters, profile.PathLookAheadMaxMeters, intent.Speed01);

            Vector3 point = characterRoot.position + intent.Direction * lookAhead;
            point.y = characterRoot.position.y + eyeHeight;

            candidate = new GazeTargetCandidate(
                GazeTargetKind.TravelPath,
                profile.TravelPathPriority,
                relevance,
                null,
                point,
                PathCandidateName);

            IsActive = true;
            return true;
        }

        /// <summary>
        ///     Counts down to the next check-in glance and reports whether one is due this tick.
        ///     Returns <c>false</c> whenever there is nothing to check on, which is the correct
        ///     behavior for a journey nobody declared a subject for.
        /// </summary>
        /// <param name="intent">Current travel reading.</param>
        /// <param name="state">Dialogue state — a character talking to you looks at you more.</param>
        /// <param name="profile">Timing and tuning source.</param>
        /// <param name="random">Seeded stream, so a character's cadence is reproducible.</param>
        /// <param name="deltaTime">Tick delta.</param>
        public bool TickCheckIn(
            in TravelIntent intent,
            DialogueState state,
            ConvaiGazeProfile profile,
            ref DeterministicEmbodimentRandom random,
            float deltaTime)
        {
            // The destination glance is opt-in and sits behind its own setting: watching the road
            // is the behaviour worth having on by default, and this one is not. Its timing comes
            // from a countdown rather than from anything the character noticed, so it reads as an
            // unexplained look away from the path — most visibly near arrival, where the
            // destination is close enough that the glance is a large movement.
            if (profile == null || !profile.EnableTravelGaze || !profile.EnableDestinationGlances ||
                !intent.IsTraveling || !intent.HasSubject)
            {
                _armed = false;
                return false;
            }

            // Arrived: there is nothing left to check on. The path candidate is already
            // withdrawn at this distance for the same reason; the check-in cadence used to do
            // the opposite and TIGHTEN on approach, which is right for a door you are walking
            // toward and wrong once you are standing in it — the glances kept firing at a
            // subject now underfoot. A journey with no known end (following someone) never
            // arrives, so a companion is checked on for as long as the walk lasts.
            if (intent.RemainingDistance <= Mathf.Max(0f, profile.ArrivalReleaseMeters))
            {
                _armed = false;
                return false;
            }

            if (!_armed)
            {
                _armed = true;
                _checkInCountdown = SampleInterval(intent, state, profile, ref random);
                return false;
            }

            _checkInCountdown -= deltaTime;
            if (_checkInCountdown > 0f) return false;

            _checkInCountdown = SampleInterval(intent, state, profile, ref random);
            return true;
        }

        /// <summary>
        ///     How much of the path candidate survives this frame: full while there is road left,
        ///     fading to nothing across the arrival window. A journey with no known end
        ///     (following someone) never arrives, so it never fades.
        /// </summary>
        private static float ResolveArrivalScale(in TravelIntent intent, ConvaiGazeProfile profile)
        {
            float remaining = intent.RemainingDistance;
            if (float.IsPositiveInfinity(remaining)) return 1f;

            float release = Mathf.Max(0f, profile.ArrivalReleaseMeters);
            float approach = Mathf.Max(release + 0.01f, profile.ArrivalApproachMeters);

            if (remaining <= release) return 0f;
            if (remaining >= approach) return 1f;

            return Mathf.InverseLerp(release, approach, remaining);
        }

        /// <summary>
        ///     Time until the next glance. Shorter for a companion than a destination, shorter again
        ///     while in conversation, and shorter still as the destination comes into reach — the
        ///     way people look at a door more the closer they get to it.
        /// </summary>
        private static float SampleInterval(
            in TravelIntent intent,
            DialogueState state,
            ConvaiGazeProfile profile,
            ref DeterministicEmbodimentRandom random)
        {
            bool companion = intent.SubjectKind == TravelSubjectKind.Companion;

            float min = companion ? profile.CompanionGlanceIntervalMin : profile.TravelGlanceIntervalMin;
            float max = companion ? profile.CompanionGlanceIntervalMax : profile.TravelGlanceIntervalMax;
            float interval = random.Range(min, Mathf.Max(min, max));

            if (state is DialogueState.Speaking or DialogueState.Listening)
                interval *= Mathf.Clamp(profile.TravelGlanceConversationScale, 0.05f, 1f);

            interval *= ResolveApproachUrgency(intent, profile);

            return Mathf.Max(0.2f, interval);
        }

        /// <summary>
        ///     Multiplier that tightens the check-in cadence over the final approach, down to half
        ///     the authored interval at the moment of arrival. 1 while the destination is far, or
        ///     unknown.
        /// </summary>
        private static float ResolveApproachUrgency(in TravelIntent intent, ConvaiGazeProfile profile)
        {
            float remaining = intent.RemainingDistance;
            if (float.IsPositiveInfinity(remaining)) return 1f;

            float approach = Mathf.Max(0.01f, profile.ArrivalApproachMeters);
            if (remaining >= approach) return 1f;

            return Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(remaining / approach));
        }
    }
}
