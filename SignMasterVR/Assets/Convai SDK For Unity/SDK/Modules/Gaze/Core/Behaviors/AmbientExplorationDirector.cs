using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Ambient idle life: while no target is engaged, produces synthetic fixation angles
    ///     around rest-forward at randomized intervals with a configurable re-centering bias.
    ///     The eyes follow the angles fully; the head follows the profile's
    ///     <c>AmbientHeadFollow</c> fraction (wired in the solver).
    /// </summary>
    internal sealed class AmbientExplorationDirector
    {
        /// <summary>
        ///     How long idle life may be interrupted and still be resumed where it left off.
        /// </summary>
        /// <remarks>
        ///     Long enough to cover a glance and its release (the longest curiosity glance the
        ///     profile allows is 4 s, plus the commitment ramp-out), short enough that a real
        ///     conversation is never resumed into — after one, the fixation the character was
        ///     holding beforehand is stale and a fresh one is the right answer.
        ///     <para>
        ///         Resuming matters for two reasons. A person who glances at you and looks back
        ///         returns to what they were looking at, not to a new random point; and while the
        ///         glance is running the head solver is crossfading out of this fixation, so
        ///         clearing it mid-glance would be exactly the frame-one drop to centre the
        ///         crossfade exists to remove.
        ///     </para>
        /// </remarks>
        private const float ResumeWindowSeconds = 5f;

        private Vector2 _targetAngles;
        private float _elapsed;
        private float _interval;
        private float _inactiveSeconds;
        private bool _wasActive;
        private bool _hasFixation;

        /// <summary>
        ///     Current ambient fixation target (yaw/pitch degrees from rest-forward). Survives a
        ///     short interruption so the head can be handed back to it — see
        ///     <see cref="ResumeWindowSeconds" />.
        /// </summary>
        public Vector2 CurrentAngles => _targetAngles;

        /// <summary>
        ///     Whether <see cref="CurrentAngles" /> is a fixation idle life is still holding, as
        ///     opposed to the cleared value left behind once the resume window has expired.
        /// </summary>
        /// <remarks>
        ///     The distinction matters because the cleared value is <see cref="Vector2.zero" />,
        ///     which is indistinguishable from a perfectly legitimate "was looking straight
        ///     ahead" fixation — and a caller handing the head back to a fixation that no longer
        ///     exists is not resuming idle life, it is commanding the head to face front. Only
        ///     the director knows which of the two a zero means.
        /// </remarks>
        public bool HasResumableFixation => _hasFixation;

        public void Reset()
        {
            _targetAngles = Vector2.zero;
            _elapsed = 0f;
            _interval = 0f;
            _inactiveSeconds = 0f;
            _wasActive = false;
            _hasFixation = false;
        }

        public void Tick(
            ConvaiGazeProfile profile,
            float deltaTime,
            bool active,
            ref DeterministicEmbodimentRandom random)
        {
            if (!active)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    _inactiveSeconds = 0f;
                }

                _inactiveSeconds += deltaTime;

                // Held for a glance-length interruption, cleared once the character has plainly
                // been doing something else. Clearing is what stops idle life from freezing on
                // an angle chosen minutes ago; holding is what lets a glance be handed back.
                if (_inactiveSeconds > ResumeWindowSeconds)
                {
                    _targetAngles = Vector2.zero;
                    _elapsed = 0f;
                    _interval = 0f;
                    _hasFixation = false;
                }

                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;

                // Re-entering after a long absence: pick a fresh fixation immediately rather
                // than resuming one that no longer means anything. After a short one, the
                // countdown continues where it stopped, so a glance does not also cost the
                // character its place in its own idle rhythm.
                if (_inactiveSeconds > ResumeWindowSeconds)
                {
                    _elapsed = 0f;
                    _interval = 0f;
                }

                _inactiveSeconds = 0f;
            }

            // Idle life is running, so whatever angles it is holding — including the zero it
            // starts on, and the zero the recentre bias samples — are a real fixation.
            _hasFixation = true;

            _elapsed += deltaTime;
            if (_elapsed < _interval) return;

            _elapsed = 0f;
            _interval = random.Range(profile.AmbientIntervalMin, profile.AmbientIntervalMax);
            _targetAngles = SampleTarget(profile, ref random);
        }

        private static Vector2 SampleTarget(ConvaiGazeProfile profile, ref DeterministicEmbodimentRandom random)
        {
            if (random.Value < profile.AmbientRecenterBias)
                return Vector2.zero;

            float yaw = random.Range(-profile.AmbientYawRangeDegrees, profile.AmbientYawRangeDegrees);
            float pitch = random.Range(-profile.AmbientPitchDownDegrees, profile.AmbientPitchUpDegrees);
            return new Vector2(yaw, pitch);
        }
    }
}
