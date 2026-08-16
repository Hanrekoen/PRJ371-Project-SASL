using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Eyebrow-gaze coordination: decides when the gaze system should raise a one-shot
    ///     <see cref="IBrowCueSink" /> cue from three inputs sampled once per tick — the current
    ///     eye pitch (upward saccade/fixation), a backchannel nod starting, and an interruption
    ///     startle re-acquisition firing. Pure POCO, scene-free, zero allocation; the controller
    ///     ticks it once per frame and forwards any pending cue to
    ///     <see cref="Convai.Runtime.Embodiment.EmbodimentContext.BrowCueSink" /> when one is
    ///     registered (a single null check otherwise — no per-frame cost when absent).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Priority.</b> At most one cue is emitted per tick. An interruption firing this
    ///         tick always wins (<see cref="BrowCueKind.SurpriseFlash" />); otherwise a nod
    ///         starting wins over an upward saccade (<see cref="BrowCueKind.Flash" /> over
    ///         <see cref="BrowCueKind.SubtleRaise" />).
    ///     </para>
    ///     <para>
    ///         <b>Rate limiting.</b> <see cref="BrowCueKind.Flash" /> and
    ///         <see cref="BrowCueKind.SubtleRaise" /> share a cooldown
    ///         (<see cref="RateLimitSeconds" />) so the brows never flicker every frame while the
    ///         eyes hover near the pitch threshold or the character nods repeatedly.
    ///         <see cref="BrowCueKind.SurpriseFlash" /> bypasses that shared cooldown entirely
    ///         (an interruption startle should always read, even mid-cooldown from an unrelated
    ///         nod) but has its own independent refractory
    ///         (<see cref="SurpriseRefractorySeconds" />) so a bouncing interruption signal cannot
    ///         spam it either.
    ///     </para>
    /// </remarks>
    internal sealed class BrowCueCoordinator
    {
        /// <summary>Eye pitch (degrees, positive upward) above which an upward saccade starts biasing the brows.</summary>
        private const float UpwardPitchThresholdDegrees = 10f;

        /// <summary>Eye pitch (degrees) at which <see cref="BrowCueKind.SubtleRaise" /> intensity saturates to 1.</summary>
        private const float UpwardPitchFullDegrees = 25f;

        /// <summary>Minimum spacing between <see cref="BrowCueKind.SubtleRaise" />/<see cref="BrowCueKind.Flash" /> cues.</summary>
        private const float RateLimitSeconds = 1.5f;

        /// <summary>Minimum spacing between <see cref="BrowCueKind.SurpriseFlash" /> cues, independent of <see cref="RateLimitSeconds" />.</summary>
        private const float SurpriseRefractorySeconds = 1.5f;

        private float _cooldownRemaining;
        private float _surpriseRefractoryRemaining;

        private bool _hasPendingCue;
        private BrowCueKind _pendingKind;
        private float _pendingIntensity;

        /// <summary>Whether the most recent <see cref="Tick" /> produced a cue to consume this frame.</summary>
        public bool HasPendingCue => _hasPendingCue;

        /// <summary>The pending cue's kind. Only meaningful when <see cref="HasPendingCue" /> is true.</summary>
        public BrowCueKind PendingKind => _pendingKind;

        /// <summary>The pending cue's intensity (0..1). Only meaningful when <see cref="HasPendingCue" /> is true.</summary>
        public float PendingIntensity => _pendingIntensity;

        /// <summary>Clears all internal state (disable/rebind).</summary>
        public void Reset()
        {
            _cooldownRemaining = 0f;
            _surpriseRefractoryRemaining = 0f;
            _hasPendingCue = false;
            _pendingKind = BrowCueKind.SubtleRaise;
            _pendingIntensity = 0f;
        }

        /// <param name="eyePitchDegrees">Current eye pitch, degrees, positive upward (see <see cref="UpwardPitchThresholdDegrees" />).</param>
        /// <param name="nodStarted">True only on the tick a backchannel/acknowledgment nod begins.</param>
        /// <param name="interruptionFired">True only on the tick an interruption startle re-acquisition fires.</param>
        /// <param name="deltaTime">Elapsed seconds. Non-positive/NaN/infinite values only clear the pending cue (no timer advance).</param>
        public void Tick(float eyePitchDegrees, bool nodStarted, bool interruptionFired, float deltaTime)
        {
            _hasPendingCue = false;

            if (!(deltaTime > 0f) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;

            if (_cooldownRemaining > 0f) _cooldownRemaining -= deltaTime;
            if (_surpriseRefractoryRemaining > 0f) _surpriseRefractoryRemaining -= deltaTime;

            if (interruptionFired && _surpriseRefractoryRemaining <= 0f)
            {
                Emit(BrowCueKind.SurpriseFlash, 1f);
                _surpriseRefractoryRemaining = SurpriseRefractorySeconds;
                return;
            }

            if (_cooldownRemaining > 0f)
                return;

            if (nodStarted)
            {
                Emit(BrowCueKind.Flash, 1f);
                _cooldownRemaining = RateLimitSeconds;
                return;
            }

            if (float.IsNaN(eyePitchDegrees) || float.IsInfinity(eyePitchDegrees))
                return;

            if (eyePitchDegrees > UpwardPitchThresholdDegrees)
            {
                float intensity = Mathf.Clamp01(
                    (eyePitchDegrees - UpwardPitchThresholdDegrees) /
                    (UpwardPitchFullDegrees - UpwardPitchThresholdDegrees));
                Emit(BrowCueKind.SubtleRaise, intensity);
                _cooldownRemaining = RateLimitSeconds;
            }
        }

        private void Emit(BrowCueKind kind, float intensity01)
        {
            _hasPendingCue = true;
            _pendingKind = kind;
            _pendingIntensity = intensity01;
        }
    }
}
