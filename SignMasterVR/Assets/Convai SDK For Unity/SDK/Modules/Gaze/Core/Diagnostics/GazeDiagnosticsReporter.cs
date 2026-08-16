using System;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Diagnostics
{
    /// <summary>
    ///     One tick's worth of numbers for the firehose dump, gathered at the call site so this
    ///     reporter never reaches back into the solvers.
    /// </summary>
    internal readonly struct GazeFirehoseSample
    {
        internal GazeFirehoseSample(
            float engagement,
            Vector2 headAngles,
            Vector2 torsoAngles,
            Vector2 leftEyeAngles,
            string eyePhaseName,
            float blinkWeight,
            float targetYawError,
            GazeTargetKind kind,
            string targetName)
        {
            Engagement = engagement;
            HeadAngles = headAngles;
            TorsoAngles = torsoAngles;
            LeftEyeAngles = leftEyeAngles;
            EyePhaseName = eyePhaseName;
            BlinkWeight = blinkWeight;
            TargetYawError = targetYawError;
            Kind = kind;
            TargetName = targetName;
        }

        internal float Engagement { get; }
        internal Vector2 HeadAngles { get; }
        internal Vector2 TorsoAngles { get; }
        internal Vector2 LeftEyeAngles { get; }
        internal string EyePhaseName { get; }
        internal float BlinkWeight { get; }
        internal float TargetYawError { get; }
        internal GazeTargetKind Kind { get; }
        internal string TargetName { get; }
    }

    /// <summary>
    ///     The gaze module's edge-triggered reporting: which target it last announced, whether the
    ///     player was last seen occluded, whether focus was last seen degraded, and how long the
    ///     eyes have been unable to reach their target.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All of this is bookkeeping about what has already been said, not state the solve
    ///         reads — which is exactly why it belongs outside the controller. Every method here
    ///         answers the same question in a different form: <em>has this changed since the last
    ///         time we mentioned it?</em> Getting that wrong produces either a silent module or one
    ///         that writes a line every frame, and both have shipped in this SDK before.
    ///     </para>
    ///     <para>
    ///         The reporter never reads the clock, the profile, or a solver. Everything it needs
    ///         arrives as an argument, so the whole of it is exercised by
    ///         <c>GazeDiagnosticsReporterTests</c> without a scene, a rig, or a frame.
    ///     </para>
    /// </remarks>
    internal sealed class GazeDiagnosticsReporter
    {
        /// <summary>Contact error (degrees) above which the target is out of the eye/head reach envelope.</summary>
        internal const float ReachLimitErrorDegrees = 10f;

        /// <summary>How long the error must stay above <see cref="ReachLimitErrorDegrees" /> before the sustained-loss line fires.</summary>
        internal const float ReachLimitHoldSeconds = 1.5f;

        /// <summary>Engagement at or above which a target counts as fully committed for the reach check.</summary>
        private const float FullCommitmentEngagement = 0.9f;

        private GazeTargetKind _lastKind = GazeTargetKind.None;
        private string _lastName = "-";
        private int _lastGeneration;
        private bool _playerOccluded;
        private bool _focusDegraded;
        private float _reachLimitTimer;
        private bool _reachLimitReported;
        private float _firehoseTimer;

        /// <summary>Forgets everything already announced, so the next tick reports from scratch.</summary>
        internal void Reset()
        {
            _lastKind = GazeTargetKind.None;
            _lastName = "-";
            _lastGeneration = 0;
            _playerOccluded = false;
            _focusDegraded = false;
            _reachLimitTimer = 0f;
            _reachLimitReported = false;
            _firehoseTimer = 0f;
        }

        /// <summary>
        ///     Reports a change of gaze target, and tells the caller whether one happened so it can
        ///     raise its own public event. A teleport that keeps the same target is reported at
        ///     Detail rather than as a transition — the character is still looking at the same
        ///     thing, it just had to re-acquire it.
        /// </summary>
        internal bool TryReportTargetTransition(
            GazeTrace trace,
            GazeTargetKind kind,
            string name,
            int generationId,
            bool teleportedThisTick,
            float time,
            out GazeTargetChange change)
        {
            bool targetChanged = kind != _lastKind || !string.Equals(name, _lastName, StringComparison.Ordinal);
            bool generationJumped = generationId != _lastGeneration && teleportedThisTick && !targetChanged;

            change = default;
            bool reported = false;

            if (targetChanged)
            {
                string reason = kind == GazeTargetKind.None ? "target released/lost" : "arbiter selection";
                change = new GazeTargetChange(_lastKind, kind, _lastName, name, reason, time);
                trace?.State($"Target {_lastKind}('{_lastName}') → {kind}('{name}') ({reason}).");
                reported = true;
            }
            else if (generationJumped && trace != null && trace.IsEnabled(GazeTraceVerbosity.Detail))
            {
                trace.Detail($"Target '{name}' jumped (teleport/camera cut) — re-acquiring, generation {generationId}.");
            }

            _lastKind = kind;
            _lastName = name;
            _lastGeneration = generationId;
            return reported;
        }

        /// <summary>
        ///     Explains <em>why</em> the player target dropped when line-of-sight gating is on — the
        ///     target transition above only reports the loss. Silent unless visibility flips.
        /// </summary>
        internal void ReportPlayerLineOfSight(GazeTrace trace, bool occluded)
        {
            if (occluded == _playerOccluded) return;

            _playerOccluded = occluded;
            trace?.State(occluded
                ? "Player occluded — line of sight lost."
                : "Player visible again — line of sight restored.");
        }

        /// <summary>Reports the focus anchor becoming unavailable, and coming back.</summary>
        internal void ReportFocusDegraded(GazeTrace trace, bool degraded)
        {
            if (degraded == _focusDegraded) return;

            _focusDegraded = degraded;
            trace?.State(degraded
                ? "Focus anchor unavailable - holding the last known point when possible."
                : "Focus anchor restored.");
        }

        /// <summary>
        ///     Reports a sustained contact-fidelity loss: when the eyes cannot close the angular
        ///     error on a fully engaged target for longer than <see cref="ReachLimitHoldSeconds" />,
        ///     emits one explanatory line — and one more when reach is restored. Never per-tick.
        /// </summary>
        internal void ReportReachLimit(
            GazeTrace trace,
            float deltaTime,
            bool hasEngagedTarget,
            float engagement,
            float contactErrorDegrees,
            string targetName)
        {
            bool outOfReach = hasEngagedTarget &&
                              engagement >= FullCommitmentEngagement &&
                              !float.IsNaN(contactErrorDegrees) &&
                              contactErrorDegrees > ReachLimitErrorDegrees;

            if (outOfReach)
            {
                _reachLimitTimer += deltaTime;
                if (!_reachLimitReported && _reachLimitTimer >= ReachLimitHoldSeconds)
                {
                    _reachLimitReported = true;
                    trace?.State(
                        $"Gaze cannot fully reach '{targetName}' — sustained {contactErrorDegrees:0}° residual " +
                        "(target outside the head/eye envelope).");
                }

                return;
            }

            _reachLimitTimer = 0f;
            if (!_reachLimitReported) return;

            _reachLimitReported = false;
            trace?.State("Gaze reach restored — target back inside the head/eye envelope.");
        }

        /// <summary>
        ///     Throttled per-tick numeric dump. Gated twice — on the profile's verbosity before the
        ///     sample is even built, and on the rate here — because this is the one trace that would
        ///     otherwise format a string every frame.
        /// </summary>
        /// <returns>
        ///     Whether a line was emitted this tick. The controller ignores it; the rate limit is
        ///     the whole point of this method, and a firehose line is logged rather than recorded,
        ///     so without this there is nothing a test could observe.
        /// </returns>
        internal bool ReportFirehose(
            GazeTrace trace,
            float deltaTime,
            GazeTraceVerbosity verbosity,
            float firehoseHz,
            in GazeFirehoseSample sample)
        {
            if (verbosity < GazeTraceVerbosity.Firehose) return false;

            _firehoseTimer += deltaTime;
            float interval = 1f / Mathf.Max(1f, firehoseHz);
            if (_firehoseTimer < interval) return false;
            _firehoseTimer = 0f;

            trace?.Firehose(
                $"engage={sample.Engagement:0.00} head=({sample.HeadAngles.x:0.0},{sample.HeadAngles.y:0.0}) " +
                $"torso=({sample.TorsoAngles.x:0.0},{sample.TorsoAngles.y:0.0}) " +
                $"eyeL=({sample.LeftEyeAngles.x:0.0},{sample.LeftEyeAngles.y:0.0}) " +
                $"eyePhase={sample.EyePhaseName} blink={sample.BlinkWeight:0.00} " +
                $"yawErr={sample.TargetYawError:0.0} kind={sample.Kind} target='{sample.TargetName}'");
            return true;
        }
    }
}
