using Convai.Domain.Embodiment.Readings;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Core.Locomotion;
using Convai.Modules.BodyAnimation.Core.Policy;
using Convai.Modules.BodyAnimation.Core.Selection;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>
    ///     Full-body base layer: the idle variant pool plus the NavMesh-synchronized
    ///     locomotion state machine.
    /// </summary>
    /// <remarks>
    ///     States: <c>Idle → (TurnInPlace) → (Start) → Move ⇄ SpeedChange → Stop → Idle</c>.
    ///     The NavMeshAgent is the position authority; during one-shots (starts, stops,
    ///     speed changes) the agent's speed is slaved to the clip's analyzed distance curve
    ///     (distance matching). Scripted yaw uses trusted analyzed curves when available;
    ///     turn-in-place can also drive from its 90/180 slot intent when imported clips have
    ///     no usable yaw metadata. During turn-in-place the agent is frozen entirely.
    ///     Every advanced feature is config-gated and degrades to plain crossfade blending
    ///     only when its required clips or unsafe movement metadata are missing, logging the
    ///     degradation once.
    /// </remarks>
    internal sealed class LocomotionLayer : IAnimationLayer
    {
        public const string LayerName = "Locomotion";

        private enum LocoState
        {
            Idle,
            TurnInPlace,
            Start,
            Move,
            SpeedChange,
            Stop
        }

        private LayerRuntime _runtime;
        private CrossfadeMixer _mixer;
        private VariantScheduler _scheduler;
        private Blend1D _moveBlend;
        private LocoState _state = LocoState.Idle;

        // Idle pool
        private int _idleIndex = -1;
        private float _idleTimer;
        private float _idleSwapAt;

        // Move blend
        private float _speedParam;
        private float _speedVelocity;
        private float _walkThreshold;
        private float _jogThreshold;
        private bool _wasJogRegime;
        private float _cruiseDistance; // meters covered at cruise speed since leaving idle
        private bool _lowSpeedStopSkipTraced;

        // Active one-shot (turn/start/stop/speed change)
        private MotionChoice _activeMotion;
        private float _prevMotionNorm;
        private float _yawScale;
        private float _turnAppliedYaw;
        private float _activeTurnAuthoredYaw;
        private bool _activeTurnUsesNominalYaw;
        private bool _stopIsManaged;
        private float _stopRate = 1f; // stop clip playback rate matched to entry speed
        private Vector3 _stopDestination;

        // Last emotion seen by Tick — EnterIdle is reached from event callbacks without a
        // tick context, and the arrival idle must still respect emotion affinities.
        private EmotionReading _lastEmotion = EmotionReading.Neutral;

        private bool _started;
        private bool _noMovementClipsWarned;
        private bool _turnDegradationLogged;
        private bool _turnNominalYawLogged;
        private bool _startDegradationLogged;
        private bool _stopDegradationLogged;
        private float _previousNormalizedTime;
        private float _currentNormalizedTime;

        public string Name => LayerName;

        // The base layer always contributes the full pose.
        public float Weight => 1f;

        public string StateLabel { get; private set; } = "Empty";

        /// <summary>True while the layer is in a displacing state (talk coverage, HUD).</summary>
        public bool IsMoving => _state is LocoState.Move or LocoState.Start or LocoState.SpeedChange;

        /// <summary>True while an animated turn-in-place is playing.</summary>
        public bool IsTurningInPlace => _state == LocoState.TurnInPlace;

        /// <summary>
        ///     True once the layer has fully settled in Idle — the planted-stop guarantee
        ///     <c>PlayActionAt</c> waits on after a move ends before root-aligning: a stop
        ///     clip's settle tail can still be playing for a beat after
        ///     <c>ConvaiNavMeshLocomotion.MoveEnded</c> fires.
        /// </summary>
        internal bool IsSettled => _state == LocoState.Idle;

        /// <summary>Effective animation ground speed (m/s) after rate warping; 0 while idle.</summary>
        public float AnimationSpeed { get; private set; }
        internal float RateWarp { get; private set; } = 1f;
        internal float SharedGaitPhase => _moveBlend?.Phase ?? 0f;
        internal float AppliedTurnYaw => _turnAppliedYaw;
        internal float ExpectedTurnYaw => _activeTurnAuthoredYaw * _yawScale;
        internal float HandoffMarker => ResolveHandoffTime(_activeMotion.Clip?.Metadata);
        internal float StopDistanceError
        {
            get
            {
                if (_state != LocoState.Stop || _runtime?.Locomotion == null) return 0f;
                ClipMotionMetadata metadata = _activeMotion.Clip?.Metadata;
                float authoredRemaining = metadata != null && metadata.HasDistance
                    ? Mathf.Max(0f, ScaledAuthoredDistance(metadata) - ScaledDistanceAt(metadata, _currentNormalizedTime))
                    : 0f;
                return _runtime.Locomotion.RemainingDistance - authoredRemaining;
            }
        }
        internal float PreviousNormalizedTime => _previousNormalizedTime;
        internal float CurrentNormalizedTime => _currentNormalizedTime;

        public string ActiveClipName
        {
            get
            {
                if (_state == LocoState.Move && _moveBlend != null)
                    return _moveBlend.DominantClipName;
                return _mixer != null && _mixer.CurrentClip != null ? _mixer.CurrentClip.name : "(none)";
            }
        }

        public float ActiveNormalizedTime =>
            _state == LocoState.Move && _moveBlend != null
                ? _moveBlend.Phase
                : _currentNormalizedTime;

        public void Initialize(LayerRuntime runtime, int port)
        {
            _runtime = runtime;
            _mixer = new CrossfadeMixer(runtime.Graph, runtime.Config.BlendCurve)
            {
                OverflowReported = msg => runtime.Trace?.Warning($"[LocomotionLayer] {msg}")
            };
            _scheduler = new VariantScheduler(runtime.RandomSeed ^ 0x1D1E);
            runtime.Mixer.ConnectLayer(port, _mixer.Playable);

            BuildMoveBlend();

            if (_runtime.Locomotion != null)
            {
                _runtime.Locomotion.SetAnimationStartGate(true);
                _runtime.Locomotion.MoveEnded += HandleMoveEnded;
            }
        }

        public void Tick(in LayerTickContext context)
        {
            _previousNormalizedTime = _mixer?.CurrentNormalizedTime ?? 0f;
            _mixer?.Tick(context.DeltaTime);
            _currentNormalizedTime = _mixer?.CurrentNormalizedTime ?? 0f;
            _lastEmotion = context.Emotion;

            if (!_started)
                StartInitialIdle(in context);

            switch (_state)
            {
                case LocoState.Idle:
                    TickIdleVariants(in context);
                    TryLeaveIdle();
                    break;

                case LocoState.TurnInPlace:
                    TickTurn(in context);
                    break;

                case LocoState.Start:
                    TickStart(context.DeltaTime);
                    break;

                case LocoState.Move:
                    TickMove(in context);
                    break;

                case LocoState.SpeedChange:
                    TickSpeedChange(context.DeltaTime);
                    break;

                case LocoState.Stop:
                    TickStop(in context);
                    break;
            }

        }

        public void Teardown()
        {
            if (_runtime?.Locomotion != null)
            {
                // The event unsubscribe always runs: a retiring layer must stop receiving
                // MoveEnded regardless of drive ownership, or it will react to a move that the
                // live layer (already swapped in on the same drive) is handling.
                _runtime.Locomotion.MoveEnded -= HandleMoveEnded;

                // The remaining four calls mutate the shared drive itself (gate, managed motion,
                // freeze, rotation authority). During a set-swap handoff this runtime no longer
                // owns the drive — the new, live LocomotionLayer does — so touching it here would
                // unfreeze/stop a turn the live layer is mid-way through.
                if (_runtime.OwnsLocomotionDrive)
                {
                    _runtime.Locomotion.SetAnimationStartGate(false);
                    _runtime.Locomotion.EndManagedMotion();
                    _runtime.Locomotion.FreezeAgent(false);
                    _runtime.Locomotion.RotationDrivenExternally = false;
                }
            }

            _mixer = null;
            _moveBlend = null;
            _runtime = null;
            _scheduler = null;
            _idleIndex = -1;
            _started = false;
            _state = LocoState.Idle;
            AnimationSpeed = 0f;
            _lastEmotion = EmotionReading.Neutral;
            ResetTurnDrive();
            StateLabel = "Empty";
        }

        private void ResetTurnDrive()
        {
            _activeTurnAuthoredYaw = 0f;
            _activeTurnUsesNominalYaw = false;
        }

        // ------------------------------------------------------------------ construction

        private void BuildMoveBlend()
        {
            LocomotionSection locomotion = _runtime.Set.Locomotion;
            if (!locomotion.HasMovement)
                return;

            _walkThreshold = ResolveAuthoredSpeed(locomotion.Walk, _runtime.Config.WalkSpeed, "walk");
            _jogThreshold = locomotion.HasJog
                ? ResolveAuthoredSpeed(locomotion.Jog, _runtime.Config.JogSpeed, "jog")
                : _walkThreshold;

            var samples = locomotion.HasJog
                ? new[]
                {
                    new Blend1D.Sample(locomotion.Walk.Clip, _walkThreshold),
                    new Blend1D.Sample(locomotion.Jog.Clip, _jogThreshold)
                }
                : new[] { new Blend1D.Sample(locomotion.Walk.Clip, _walkThreshold) };

            _moveBlend = new Blend1D(_runtime.Graph, samples, _runtime.Config.EnableFootIK);

            // Command the agent at the clips' MEASURED speeds so the blend parameter sits
            // exactly on a sample (pure cycle, rate = 1). With config speeds instead, a jog
            // authored at ~3.7 m/s commanded at 2.6 m/s lives forever in the mushy middle of
            // the walk↔jog blend.
            if (_runtime.Locomotion != null)
            {
                _runtime.Locomotion.ConfigureSpeeds(_walkThreshold, _jogThreshold);
                if (_runtime.Trace.IsState)
                    _runtime.Trace.State(
                        $"Agent speeds synced to measured clip speeds: walk {_walkThreshold:F2} m/s, " +
                        $"jog {_jogThreshold:F2} m/s.");
            }
        }

        private float ResolveAuthoredSpeed(LocomotionClip clip, float fallback, string label)
        {
            if (clip.Metadata.HasSpeed)
                return ScaledAuthoredSpeed(clip.Metadata);

            // NOT scaled: Config.WalkSpeed/JogSpeed are world speeds the user typed, not a
            // measurement of the content — multiplying them by the rig's motion scale would
            // silently override an explicit user setting instead of correcting a measurement.
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"'{clip.ClipName}' ({label}) has no analyzed authored speed — using config " +
                    $"fallback {fallback:F2} m/s. Run the Clip Motion Analyzer for exact foot sync.");
            return fallback;
        }

        // ------------------------------------------------------------------ rig scale calibration

        /// <summary>Authored ground speed (m/s), scaled for this character's rig.</summary>
        private float ScaledAuthoredSpeed(ClipMotionMetadata meta) =>
            meta != null ? meta.AuthoredSpeed * _runtime.MotionScale : 0f;

        /// <summary>Total authored travel distance (m), scaled for this character's rig.</summary>
        private float ScaledAuthoredDistance(ClipMotionMetadata meta) =>
            meta != null ? meta.AuthoredDistance * _runtime.MotionScale : 0f;

        /// <summary>Distance (m) covered at <paramref name="normalizedTime" />, scaled for this character's rig.</summary>
        private float ScaledDistanceAt(ClipMotionMetadata meta, float normalizedTime) =>
            meta != null ? meta.EvaluateDistance(normalizedTime) * _runtime.MotionScale : 0f;

        // ------------------------------------------------------------------ idle

        private void StartInitialIdle(in LayerTickContext context)
        {
            _started = true;

            int index = _scheduler.SelectNext(_runtime.Set.Idles, -1, in context.Emotion, out _);
            if (index < 0)
            {
                _runtime.Trace.Warning(
                    "No valid idle entry in the animation set — base layer stays empty (T-pose risk). " +
                    "Assign at least one looping idle clip.");
                StateLabel = "Empty";
                return;
            }

            IdleEntry idle = _runtime.Set.Idles[index];
            _mixer.Play(idle.Clip, 0f, FootIkSettings());

            _idleIndex = index;
            ScheduleNextIdleSwap();
            StateLabel = "Idle";
            _runtime.ReportTransition(LayerName, "Empty", "Idle", idle.Clip.name, 0f, "initial idle");
        }

        private void TickIdleVariants(in LayerTickContext context)
        {
            if (_idleIndex < 0) return;

            _idleTimer += context.DeltaTime;
            if (_idleTimer < _idleSwapAt) return;

            int next = _scheduler.SelectNext(
                _runtime.Set.Idles, _idleIndex, in context.Emotion, out float weight);
            ScheduleNextIdleSwap();

            if (next < 0 || next == _idleIndex) return;

            IdleEntry idle = _runtime.Set.Idles[next];
            float fade = _runtime.Config.IdleCrossfadeSeconds;
            _mixer.Play(idle.Clip, fade, FootIkSettings());

            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Idle variant roll: index={next} weight={weight:F2} emotion={context.Emotion.DominantLabel}");
            _runtime.ReportTransition(LayerName, "Idle", "Idle", idle.Clip.name, fade, "idle variant swap");
            _idleIndex = next;
        }

        private void ScheduleNextIdleSwap()
        {
            _idleTimer = 0f;
            // Calmness stretches idle variant intervals (identity at Calmness = 1).
            float scale = PersonaScalars.ResolveIdleIntervalScale(_runtime.Config);
            _idleSwapAt = _scheduler.NextInterval(
                _runtime.Config.IdleVariantIntervalMin * scale,
                _runtime.Config.IdleVariantIntervalMax * scale);
        }

        private void EnterIdle(string from, string reason, float fade)
        {
            AnimationSpeed = 0f;
            _cruiseDistance = 0f; // the next leg must earn its planted stop again
            ResetTurnDrive();
            _state = LocoState.Idle;

            int index = _scheduler.SelectNext(_runtime.Set.Idles, -1, in _lastEmotion, out _);
            if (index < 0)
            {
                StateLabel = "Empty";
                return;
            }

            IdleEntry idle = _runtime.Set.Idles[index];
            _mixer.Play(idle.Clip, fade, FootIkSettings(), restartIfSame: true);

            _idleIndex = index;
            ScheduleNextIdleSwap();
            StateLabel = "Idle";
            _runtime.ReportTransition(LayerName, from, "Idle", idle.Clip.name, fade, reason);
        }

        // ------------------------------------------------------------------ leaving idle

        private void TryLeaveIdle()
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            if (locomotion == null || !locomotion.IsMoving) return;

            if (_moveBlend == null)
            {
                locomotion.ReleaseAnimationStartGate();
                if (!_noMovementClipsWarned)
                {
                    _noMovementClipsWarned = true;
                    _runtime.Trace.Warning(
                        "Agent is moving but the set has no walk clip — the character will slide in idle. " +
                        "Assign Locomotion → Walk in the animation set.");
                }
                return;
            }

            // The steering angle is unknown until the async path is computed — leaving idle
            // now would always read 0° and skip turns (walking off before facing the target).
            if (locomotion.PathPending) return;

            float angle = locomotion.SignedAngleToSteering;

            // Moderate angles: the directional start rotates during the first steps while
            // translating toward the target — responsive and natural. Near-reversals read
            // wrong mid-start (translating toward a target far off the facing), so a crisp
            // turn-in-place fires first and the move-off start follows, wind-up trimmed.
            // Turn-in-place is also the fallback when no start fits (short leg, missing
            // clips, starts disabled).
            if (Mathf.Abs(angle) >= _runtime.Config.Turn180MinAngle &&
                TryEnterTurn(locomotion, angle)) return;
            if (TryEnterFittingStart(locomotion, angle, "leaving idle")) return;
            if (TryEnterTurn(locomotion, angle)) return;
            EnterMove("leaving idle → plain blend", _moveBlend.Phase);
        }

        /// <summary>
        ///     Requests an animated turn-in-place toward a facing direction while the
        ///     character is stationary (no NavMeshAgent required) — the gaze module's body
        ///     reorientation entry point. Returns <c>false</c> when the layer is busy, the
        ///     feature is disabled, the angle is below the turn threshold, or turn clips are
        ///     missing. Called again while a turn is already in flight, it re-aims the
        ///     remaining rotation at the new angle (moving targets), so the turn
        ///     lands on the live target instead of where the target stood when it fired.
        /// </summary>
        public bool RequestFacingTurn(float signedAngleDegrees, string reason)
        {
            if (_state == LocoState.TurnInPlace) return ReaimFacingTurn(signedAngleDegrees);
            if (_state != LocoState.Idle) return false;

            ConvaiBodyAnimationConfig config = _runtime.Config;
            if (!config.EnableTurnInPlace || Mathf.Abs(signedAngleDegrees) < config.TurnInPlaceMinAngle)
                return false;

            MotionChoice choice = LocomotionSelectors.SelectTurn(
                _runtime.Set.Locomotion, signedAngleDegrees, config.Turn180MinAngle);
            if (!TryPrepareTurnDrive(choice, signedAngleDegrees, out float authoredYaw, out bool useNominalYaw))
                return false;

            _yawScale = MotionDrive.YawScale(signedAngleDegrees, authoredYaw);
            if (_yawScale == 0f) return false;

            ILocomotionDrive locomotion = _runtime.Locomotion;
            if (locomotion != null)
            {
                locomotion.FreezeAgent(true);
                locomotion.RotationDrivenExternally = true;
            }

            _activeMotion = choice;
            _prevMotionNorm = 0f;
            _turnAppliedYaw = 0f;
            _activeTurnAuthoredYaw = authoredYaw;
            _activeTurnUsesNominalYaw = useNominalYaw;
            _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                FootIkSettings(), restartIfSame: true);

            _state = LocoState.TurnInPlace;
            StateLabel = choice.Label;
            _runtime.ReportTransition(
                LayerName, "Idle", choice.Label, choice.Clip.ClipName,
                _runtime.Config.LocomotionCrossfadeSeconds,
                $"facing request: {reason} (yaw error {signedAngleDegrees:F0}°, authored {authoredYaw:F0}°, " +
                $"scale {_yawScale:F2}{(useNominalYaw ? ", nominal yaw drive" : "")})");
            return true;
        }

        /// <summary>
        ///     Steers an in-flight facing turn so the rotation still to come lands on the
        ///     new remaining angle. The clip keeps playing untouched; only the yaw scale is
        ///     re-solved against the unplayed part of the active turn drive. A target that
        ///     crossed to the other side (or was reached early) clamps the scale to zero —
        ///     the animation finishes cleanly and the director corrects afterwards.
        /// </summary>
        private bool ReaimFacingTurn(float remainingAngleDegrees)
        {
            if (Mathf.Abs(_activeTurnAuthoredYaw) < 1f) return true;

            float norm = _currentNormalizedTime;
            float authoredRemaining = _activeTurnAuthoredYaw - EvaluateActiveTurnYaw(norm);
            if (Mathf.Abs(authoredRemaining) < 10f) return true; // too little clip left to steer with

            // The cap matches the fire-time clamp: late in the clip the unplayed curve is
            // short, and letting the ratio run higher spins the root visibly faster than
            // the clip's step — a whip right at the landing. A residual the capped scale
            // cannot serve is closed by the caller's post-turn settle instead.
            float newScale = Mathf.Clamp(remainingAngleDegrees / authoredRemaining, 0f, 1.4f);
            if (Mathf.Abs(newScale - _yawScale) > 0.1f && _runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Facing turn re-aimed: remaining {remainingAngleDegrees:F0}°, " +
                    $"scale {_yawScale:F2} → {newScale:F2}.");
            _yawScale = newScale;
            return true;
        }

        /// <summary>
        ///     Cancels an in-flight facing turn, unfreezing the agent and easing back to
        ///     idle. Safe to call when nothing is turning.
        /// </summary>
        public void CancelFacingTurn(string reason)
        {
            if (_state != LocoState.TurnInPlace) return;

            ILocomotionDrive locomotion = _runtime.Locomotion;
            locomotion?.FreezeAgent(false);
            if (locomotion != null)
                locomotion.RotationDrivenExternally = false;

            EnterIdle(StateLabel, $"facing turn cancelled: {reason}", _runtime.Config.LocomotionCrossfadeSeconds);
        }

        private bool TryEnterTurn(ILocomotionDrive locomotion, float angle)
        {
            ConvaiBodyAnimationConfig config = _runtime.Config;
            if (!config.EnableTurnInPlace || Mathf.Abs(angle) < config.TurnInPlaceMinAngle)
                return false;

            MotionChoice choice = LocomotionSelectors.SelectTurn(
                _runtime.Set.Locomotion, angle, config.Turn180MinAngle);
            if (!TryPrepareTurnDrive(choice, angle, out float authoredYaw, out bool useNominalYaw))
                return false;

            // Do not restart the same turn clip while it is already in flight. A moving
            // steering target (for example, gaze/body orientation being released as an
            // action starts) can keep the residual above the re-selection threshold for
            // several ticks. Restarting the same clip resets both normalized time and
            // applied yaw, leaving the agent frozen in TurnInPlace indefinitely. Keep the
            // current performance and steer its remaining authored yaw toward the latest
            // path heading instead.
            if (_state == LocoState.TurnInPlace && _activeMotion.IsValid)
            {
                if (_activeMotion.Clip.Clip == choice.Clip.Clip)
                    return ReaimFacingTurn(angle);

                // A moving steering target can shrink below the 180° bucket while a
                // Turn:180 clip is already committed. Crossfading down to Turn:90 resets
                // applied yaw and plays a second turn — re-aim the larger clip instead.
                if (Mathf.Abs(choice.AuthoredYaw) < Mathf.Abs(_activeMotion.AuthoredYaw) &&
                    Mathf.Sign(choice.AuthoredYaw) == Mathf.Sign(_activeMotion.AuthoredYaw))
                    return ReaimFacingTurn(angle);
            }

            _yawScale = MotionDrive.YawScale(angle, authoredYaw);
            if (_yawScale == 0f) return false;

            locomotion.FreezeAgent(true);
            locomotion.RotationDrivenExternally = true;

            _activeMotion = choice;
            _prevMotionNorm = 0f;
            _turnAppliedYaw = 0f;
            _activeTurnAuthoredYaw = authoredYaw;
            _activeTurnUsesNominalYaw = useNominalYaw;
            _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                FootIkSettings(), restartIfSame: true);

            string from = StateLabel;
            _state = LocoState.TurnInPlace;
            StateLabel = choice.Label;
            _runtime.ReportTransition(
                LayerName, from, choice.Label, choice.Clip.ClipName,
                _runtime.Config.LocomotionCrossfadeSeconds,
                $"yaw error {angle:F0}°, authored {authoredYaw:F0}°, scale {_yawScale:F2}, " +
                $"agent frozen{(useNominalYaw ? ", nominal yaw drive" : "")}");
            return true;
        }

        private bool TryPrepareTurnDrive(
            in MotionChoice choice,
            float requiredYaw,
            out float authoredYaw,
            out bool useNominalYaw)
        {
            authoredYaw = 0f;
            useNominalYaw = false;

            if (!choice.IsValid)
            {
                LogTurnUnavailable(
                    "Turn-in-place degraded: no turn clip is assigned for the requested direction.");
                return false;
            }

            float nominalYaw = choice.AuthoredYaw;
            if (Mathf.Abs(nominalYaw) < 45f || Mathf.Sign(nominalYaw) != Mathf.Sign(requiredYaw))
            {
                LogTurnUnavailable(_runtime.Trace.IsState
                    ? $"Turn-in-place degraded: selected turn '{choice.Label}' cannot serve yaw {requiredYaw:F0}°."
                    : null);
                return false;
            }

            ClipMotionMetadata meta = choice.Clip.Metadata;
            float measuredYaw = meta != null && meta.HasYaw ? meta.AuthoredYawDegrees : 0f;
            if (TurnYawMatchesNominal(measuredYaw, nominalYaw))
            {
                authoredYaw = measuredYaw;
                return true;
            }

            authoredYaw = nominalYaw;
            useNominalYaw = true;
            if (!_turnNominalYawLogged)
            {
                _turnNominalYawLogged = true;
                _runtime.Trace.State(
                    "Turn-in-place using nominal yaw drive: analyzed turn yaw is missing or " +
                    "does not match the clip slot, so the assigned turn clip still plays while " +
                    "root rotation follows its 90/180 degree authoring intent.");
            }
            else if (_runtime.Trace.IsDetail)
            {
                _runtime.Trace.Detail(
                    $"Turn '{choice.Label}' using nominal yaw drive: measured {measuredYaw:F0}°, " +
                    $"slot {nominalYaw:F0}°.");
            }
            return true;
        }

        private void LogTurnUnavailable(string message)
        {
            if (_turnDegradationLogged) return;
            _turnDegradationLogged = true;
            _runtime.Trace.State(message);
        }

        private static bool TurnYawMatchesNominal(float measuredYaw, float nominalYaw)
        {
            float nominalMagnitude = Mathf.Abs(nominalYaw);
            float measuredMagnitude = Mathf.Abs(measuredYaw);
            return Mathf.Sign(measuredYaw) == Mathf.Sign(nominalYaw) &&
                   measuredMagnitude >= nominalMagnitude * 0.5f &&
                   measuredMagnitude <= nominalMagnitude * 1.5f;
        }

        private float EvaluateActiveTurnYaw(float normalizedTime)
        {
            if (_activeTurnUsesNominalYaw)
            {
                return MotionDrive.NominalTurnYaw(
                    _activeTurnAuthoredYaw,
                    normalizedTime,
                    ResolveHandoffTime(_activeMotion.Clip?.Metadata));
            }

            ClipMotionMetadata meta = _activeMotion.Clip?.Metadata;
            return meta != null && meta.HasYaw ? meta.EvaluateYaw(normalizedTime) : 0f;
        }

        private void TickTurn(in LayerTickContext context)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float norm = _currentNormalizedTime;

            float yawDelta = ActiveTurnYawDelta(_prevMotionNorm, norm);
            if (yawDelta != 0f && _runtime.CharacterRoot != null)
            {
                _runtime.CharacterRoot.Rotate(0f, yawDelta, 0f, Space.World);
                _turnAppliedYaw += yawDelta;
            }
            _prevMotionNorm = norm;

            bool done = norm >= ResolveHandoffTime(_activeMotion.Clip?.Metadata) ||
                        _mixer.IsCurrentClipFinished;

            // A mid-turn destination change can invalidate the committed yaw. Re-aim the
            // committed clip toward the live steering target first; only crossfade to a
            // different turn when re-aiming cannot close the predicted residual (for example
            // the target crossed to the other side or needs a larger clip).
            if (!done && locomotion != null && locomotion.IsMoving &&
                !locomotion.PathPending)
            {
                float steering = locomotion.SignedAngleToSteering;
                ReaimFacingTurn(steering);

                float remainingYaw = _activeTurnAuthoredYaw * _yawScale - _turnAppliedYaw;
                float predictedResidual = Mathf.DeltaAngle(0f, steering - remainingYaw);
                float redirectThreshold = Mathf.Max(_runtime.Config.TurnInPlaceMinAngle, 45f);

                if (Mathf.Abs(predictedResidual) > redirectThreshold)
                {
                    if (TryEnterTurn(locomotion, steering)) return;
                    done = true; // no clip can serve the new heading — stop turning,
                                 // path-follow rotation corrects from Move
                }
            }

            if (!done) return;

            locomotion?.FreezeAgent(false);
            if (locomotion != null)
                locomotion.RotationDrivenExternally = false;

            if (locomotion != null && locomotion.IsMoving)
            {
                EnterStartOrMove(locomotion, locomotion.SignedAngleToSteering,
                    $"turn finished (applied {_turnAppliedYaw:F0}°, residual {locomotion.SignedAngleToSteering:F0}°)",
                    trimWindup: true);
            }
            else
            {
                // The handoff cuts the clip's settle tail — give the turn→idle blend the
                // same extra room planted stops get so the weight shift reads out instead
                // of snapping into the idle pose.
                EnterIdle(StateLabel,
                    $"turn finished, applied {_turnAppliedYaw:F0}°, no active move",
                    Mathf.Max(_runtime.Config.LocomotionCrossfadeSeconds, 0.4f));
            }
        }

        private float ActiveTurnYawDelta(float previousNorm, float norm)
        {
            if (_activeTurnUsesNominalYaw)
            {
                return MotionDrive.NominalTurnYawDelta(
                    _activeTurnAuthoredYaw,
                    previousNorm,
                    norm,
                    _yawScale,
                    ResolveHandoffTime(_activeMotion.Clip?.Metadata));
            }

            return MotionDrive.YawDelta(_activeMotion.Clip.Metadata, previousNorm, norm, _yawScale);
        }

        // ------------------------------------------------------------------ start

        private void EnterStartOrMove(
            ILocomotionDrive locomotion, float angle, string reason, bool trimWindup = false)
        {
            if (TryEnterFittingStart(locomotion, angle, reason, trimWindup)) return;
            EnterMove($"{reason} → plain blend", _moveBlend.Phase);
        }

        private bool TryEnterFittingStart(
            ILocomotionDrive locomotion, float angle, string reason, bool trimWindup = false)
        {
            ConvaiBodyAnimationConfig config = _runtime.Config;
            if (!config.EnableDirectionalStarts) return false;

            // A start clip must fit inside the leg — otherwise the agent arrives
            // mid-start and pops back to idle. Jog starts are long (~6.8m authored), so
            // short jog legs fall back to the walk start family before giving up.
            float remaining = locomotion.RemainingDistance;
            bool jog = IsJogCommanded(locomotion);

            MotionChoice choice = SelectFittingStart(jog, angle, remaining, config);
            if (choice.IsValid)
            {
                EnterStart(locomotion, choice, angle, reason, trimWindup);
                return true;
            }

            MotionChoice anyStart = LocomotionSelectors.SelectStart(
                _runtime.Set.Locomotion, jog, angle, config.Turn180MinAngle);
            if (anyStart.IsValid && anyStart.Clip.Metadata.HasDistance)
            {
                if (_runtime.Trace.IsDetail)
                    _runtime.Trace.Detail(
                        $"Start '{anyStart.Label}' skipped: leg {remaining:F2}m shorter than " +
                        $"authored {ScaledAuthoredDistance(anyStart.Clip.Metadata):F2}m.");
            }
            else if (!_startDegradationLogged)
            {
                _startDegradationLogged = true;
                _runtime.Trace.State(
                    "Directional starts degraded to a plain blend: start clips or their distance " +
                    "metadata are missing (run the Clip Motion Analyzer).");
            }

            return false;
        }

        private MotionChoice SelectFittingStart(
            bool jog, float angle, float remaining, ConvaiBodyAnimationConfig config)
        {
            MotionChoice choice = LocomotionSelectors.SelectStart(
                _runtime.Set.Locomotion, jog, angle, config.Turn180MinAngle);
            if (Fits(choice)) return choice;

            if (jog)
            {
                // Leg too short for the jog start — the walk start still reads far better
                // than a raw blend; the agent accelerates to jog after the handoff.
                choice = LocomotionSelectors.SelectStart(
                    _runtime.Set.Locomotion, false, angle, config.Turn180MinAngle);
                if (Fits(choice)) return choice;
            }

            return default;

            bool Fits(in MotionChoice candidate) =>
                candidate.IsValid && candidate.Clip.Metadata.HasDistance &&
                remaining >= ScaledAuthoredDistance(candidate.Clip.Metadata) * 0.9f &&
                ScriptedYawTrustworthy(candidate);
        }

        /// <summary>
        ///     A directional (90/180) start is only usable when the analyzer actually
        ///     measured a matching rotation in the clip. Some packs author "directional"
        ///     starts without any skeleton rotation (the direction lives in the discarded
        ///     translation only) — scripted yaw driven by such metadata walks the character
        ///     off facing the wrong way. Those fall back to turn-in-place / plain blend.
        /// </summary>
        private bool ScriptedYawTrustworthy(in MotionChoice candidate)
        {
            if (Mathf.Abs(candidate.AuthoredYaw) < 45f) return true; // forward: path-follow rotates

            float measured = candidate.Clip.Metadata.HasYaw
                ? candidate.Clip.Metadata.AuthoredYawDegrees
                : 0f;
            bool trustworthy =
                Mathf.Sign(measured) == Mathf.Sign(candidate.AuthoredYaw) &&
                Mathf.Abs(measured) >= Mathf.Abs(candidate.AuthoredYaw) * 0.5f;

            if (!trustworthy && _runtime.Trace.IsDetail)
            {
                _runtime.Trace.Detail(
                    $"Directional start '{candidate.Label}' rejected: measured yaw {measured:F0}° " +
                    $"doesn't back its nominal {candidate.AuthoredYaw:F0}° (clip carries no usable " +
                    "rotation) — falling back to turn-in-place / plain blend.");
            }

            return trustworthy;
        }

        private void EnterStart(
            ILocomotionDrive locomotion, in MotionChoice choice, float angle, string reason,
            bool trimWindup = false)
        {
            locomotion.BeginManagedMotion();

            ClipMotionMetadata meta = choice.Clip.Metadata;
            _yawScale = meta.HasYaw && Mathf.Abs(choice.AuthoredYaw) >= 45f
                ? MotionDrive.YawScale(angle, meta.AuthoredYawDegrees)
                : 0f;

            // Forward starts carry no authored yaw — leave rotation to path following so a
            // residual heading error (< turn threshold) is corrected while starting instead
            // of being walked straight through and fixed late.
            if (_yawScale == 0f)
                locomotion.RotationDrivenExternally = false;

            // Right after a turn the body has already anticipated — skip the start clip's
            // stationary wind-up so the first step lands immediately instead of pausing.
            float startNorm = trimWindup
                ? MotionDrive.NormalizedTimeAtDistance(meta, 0.05f, _runtime.MotionScale)
                : 0f;
            ClipPlaySettings settings = FootIkSettings();
            settings.StartNormalizedTime = startNorm;

            _activeMotion = choice;
            _prevMotionNorm = startNorm;
            _turnAppliedYaw = 0f;
            ResetTurnDrive();
            _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                settings, restartIfSame: true);

            string from = StateLabel;
            _state = LocoState.Start;
            StateLabel = choice.Label;
            _runtime.ReportTransition(
                LayerName, from, choice.Label, choice.Clip.ClipName,
                _runtime.Config.LocomotionCrossfadeSeconds,
                $"{reason}; angle {angle:F0}°, authored dist {ScaledAuthoredDistance(meta):F2}m, " +
                $"start at {startNorm:F2}");
        }

        private void TickStart(float deltaTime)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float norm = _currentNormalizedTime;
            ClipMotionMetadata meta = _activeMotion.Clip.Metadata;

            float speed = MotionDrive.SpeedAt(meta, norm, _activeMotion.Clip.Length, _runtime.MotionScale);
            locomotion?.SetManagedSpeed(speed);
            AnimationSpeed = speed;
            AccumulateCruise(speed, deltaTime);

            float yawDelta = MotionDrive.YawDelta(meta, _prevMotionNorm, norm, _yawScale);
            if (yawDelta != 0f && _runtime.CharacterRoot != null)
            {
                _runtime.CharacterRoot.Rotate(0f, yawDelta, 0f, Space.World);
                _turnAppliedYaw += yawDelta;
            }
            _prevMotionNorm = norm;

            if (locomotion != null && !locomotion.IsMoving)
            {
                locomotion.EndManagedMotion();
                EnterIdle(StateLabel, "move ended during start", _runtime.Config.LocomotionCrossfadeSeconds);
                return;
            }

            // A hard mid-start redirect would keep walking a stale heading with rotation
            // clip-owned — hand off to Move so path following re-steers immediately.
            if (locomotion != null && !locomotion.PathPending)
            {
                float remainingYaw = meta.AuthoredYawDegrees * _yawScale - _turnAppliedYaw;
                float predictedResidual = Mathf.DeltaAngle(
                    0f, locomotion.SignedAngleToSteering - remainingYaw);
                if (Mathf.Abs(predictedResidual) > 100f)
                {
                    locomotion.EndManagedMotion();
                    float redirectPhase = FootPhaseUtil.HandoffPhase(
                        meta, _runtime.Set.Locomotion.Walk.Metadata, _moveBlend.Phase);
                    EnterMove($"start aborted — redirect ({predictedResidual:F0}° off)", redirectPhase);
                    return;
                }
            }

            bool handoff = norm >= ResolveHandoffTime(meta) ||
                           _mixer.IsCurrentClipFinished;
            if (!handoff) return;

            locomotion?.EndManagedMotion();

            float phase = FootPhaseUtil.HandoffPhase(
                meta, _runtime.Set.Locomotion.Walk.Metadata, _moveBlend.Phase);
            EnterMove($"start handoff at {norm:F2}", phase);
        }

        // ------------------------------------------------------------------ move

        private void EnterMove(string reason, float startPhase)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float fade = _runtime.Config.LocomotionCrossfadeSeconds;

            locomotion?.ReleaseAnimationStartGate();
            ResetTurnDrive();
            _speedParam = locomotion != null ? locomotion.Speed : 0f;
            _speedVelocity = 0f;
            _moveBlend.SetPhase(startPhase);
            _moveBlend.SetParameter(_speedParam);
            _mixer.PlayExternal(_moveBlend.Playable, fade);

            _wasJogRegime = IsJogCommanded(locomotion);
            _lowSpeedStopSkipTraced = false;
            string from = StateLabel;
            _state = LocoState.Move;
            StateLabel = "Move";
            _runtime.ReportTransition(
                LayerName, from, "Move", _moveBlend.DominantClipName, fade,
                $"{reason}; speed={_speedParam:F2} m/s, phase={startPhase:F2}");
        }

        private void TickMove(in LayerTickContext context)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float agentSpeed = locomotion != null ? locomotion.Speed : 0f;

            _speedParam = Mathf.SmoothDamp(
                _speedParam, agentSpeed, ref _speedVelocity,
                _runtime.Config.SpeedDampingSeconds, float.MaxValue, context.DeltaTime);
            _moveBlend.SetParameter(_speedParam);

            float rate = 1f;
            if (_runtime.Config.EnableSpeedWarping && agentSpeed > 0.01f)
            {
                rate = Mathf.Clamp(
                    agentSpeed / Mathf.Max(0.01f, _moveBlend.BlendedThreshold),
                    _runtime.Config.RateWarpMin,
                    _runtime.Config.RateWarpMax);
            }

            _moveBlend.RateScale = rate;
            RateWarp = rate;
            _moveBlend.Tick(context.DeltaTime);
            AnimationSpeed = _moveBlend.BlendedThreshold * rate;

            AccumulateCruise(agentSpeed, context.DeltaTime);

            if (locomotion == null || !locomotion.IsMoving)
            {
                // No stop clip played (short leg or degraded) — give the walk→idle
                // settle more room than a state crossfade so the last step reads out.
                EnterIdle("Move", locomotion != null ? "movement ended" : "locomotion component lost",
                    Mathf.Max(_runtime.Config.LocomotionCrossfadeSeconds, 0.4f));
                return;
            }

            if (TryEnterStop(locomotion, agentSpeed)) return;
            TryEnterSpeedChange(locomotion);
        }

        private bool IsJogCommanded(ILocomotionDrive locomotion)
        {
            if (locomotion == null || _jogThreshold <= _walkThreshold) return false;
            return locomotion.DesiredSpeed > (_walkThreshold + _jogThreshold) * 0.5f;
        }

        /// <summary>
        ///     Tracks meters covered at cruise speed since the character left idle — planted
        ///     stops are gated on it so a two-step reposition never earns a full-momentum plant.
        /// </summary>
        private void AccumulateCruise(float speed, float deltaTime)
        {
            if (speed >= _walkThreshold * 0.85f)
                _cruiseDistance += speed * deltaTime;
        }

        private ClipMotionMetadata DominantCycleMetadata()
        {
            LocomotionSection section = _runtime.Set.Locomotion;
            bool jogDominant = section.HasJog &&
                               _moveBlend.Parameter > (_walkThreshold + _jogThreshold) * 0.5f;
            return jogDominant ? section.Jog.Metadata : section.Walk.Metadata;
        }

        // ------------------------------------------------------------------ speed change

        private void TryEnterSpeedChange(ILocomotionDrive locomotion)
        {
            bool jogNow = IsJogCommanded(locomotion);
            if (jogNow == _wasJogRegime) return;

            bool toJog = jogNow;
            _wasJogRegime = jogNow;

            if (!_runtime.Config.EnableSpeedChangeClips) return;

            FootSide upcoming = FootPhaseUtil.NextPlantFoot(DominantCycleMetadata(), _moveBlend.Phase);
            MotionChoice choice = LocomotionSelectors.SelectSpeedChange(
                _runtime.Set.Locomotion, toJog, upcoming);
            if (!choice.IsValid || !choice.Clip.Metadata.HasDistance) return;

            // The transition clip travels ~4m — with less path left, let the blend handle it.
            if (locomotion.RemainingDistance < ScaledAuthoredDistance(choice.Clip.Metadata) * 1.1f)
            {
                if (_runtime.Trace.IsDetail)
                    _runtime.Trace.Detail(
                        $"Speed change '{choice.Label}' skipped: leg shorter than authored " +
                        $"{ScaledAuthoredDistance(choice.Clip.Metadata):F2}m.");
                return;
            }

            locomotion.BeginManagedMotion();
            // Speed-change clips travel straight — keep path-follow rotation live so a
            // mid-clip destination change steers the character instead of freezing the
            // facing until the handoff.
            locomotion.RotationDrivenExternally = false;

            _activeMotion = choice;
            _prevMotionNorm = 0f;
            _yawScale = 0f;
            ResetTurnDrive();
            _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                FootIkSettings(), restartIfSame: true);

            _state = LocoState.SpeedChange;
            StateLabel = choice.Label;
            _runtime.ReportTransition(
                LayerName, "Move", choice.Label, choice.Clip.ClipName,
                _runtime.Config.LocomotionCrossfadeSeconds,
                $"speed regime → {(toJog ? "jog" : "walk")}, plant={upcoming}");
        }

        private void TickSpeedChange(float deltaTime)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float norm = _currentNormalizedTime;
            ClipMotionMetadata meta = _activeMotion.Clip.Metadata;

            float speed = MotionDrive.SpeedAt(meta, norm, _activeMotion.Clip.Length, _runtime.MotionScale);
            locomotion?.SetManagedSpeed(speed);
            AnimationSpeed = speed;
            AccumulateCruise(speed, deltaTime);

            if (locomotion != null && !locomotion.IsMoving)
            {
                locomotion.EndManagedMotion();
                EnterIdle(StateLabel, "move ended during speed change", _runtime.Config.LocomotionCrossfadeSeconds);
                return;
            }

            bool handoff = norm >= ResolveHandoffTime(meta) ||
                           _mixer.IsCurrentClipFinished;
            if (!handoff) return;

            locomotion?.EndManagedMotion();

            float phase = FootPhaseUtil.HandoffPhase(
                meta, DominantCycleMetadata(), _moveBlend.Phase);
            EnterMove($"speed change handoff at {norm:F2}", phase);
        }

        private float ResolveHandoffTime(ClipMotionMetadata metadata)
        {
            if (metadata != null && metadata.SchemaVersion >= ClipMotionMetadata.CurrentSchemaVersion)
                return metadata.RecommendedHandoffNormalizedTime;
            return _runtime.Config.MotionHandoffNormalizedTime;
        }

        // ------------------------------------------------------------------ stop

        /// <summary>
        ///     How much path a stop from <paramref name="speed" /> needs to read as a stop rather
        ///     than a cut: the distance the stop clip this speed would select actually travels.
        ///     <see cref="ConvaiNavMeshLocomotion.StopGracefully" /> brakes over exactly this, so
        ///     the run-out lands inside <see cref="TryEnterStop" />'s acceptance window and the
        ///     planted stop is earned instead of missed by a few centimetres.
        /// </summary>
        /// <returns>
        ///     The distance in metres, or <c>0</c> when this character has nothing better to offer
        ///     than the agent's own physics — no movement content, planted stops disabled, or no
        ///     stop clip with measured distance for the gait in question.
        /// </returns>
        public float SuggestBrakingDistance(float speed)
        {
            if (_moveBlend == null || _runtime?.Config == null) return 0f;
            if (!_runtime.Config.EnablePlantedStops) return 0f;

            // The same gait test TryEnterStop makes, asked one braking distance earlier. Asking
            // it here of the commanded speed rather than of the blend parameter is deliberate:
            // the run-out has not started yet, so the body is still at cruise.
            bool jogging = speed > (_walkThreshold + _jogThreshold) * 0.5f;
            if (!jogging && !_runtime.Config.PlantedStopsWhileWalking) return 0f;

            FootSide upcoming = FootPhaseUtil.NextPlantFoot(DominantCycleMetadata(), _moveBlend.Phase);
            MotionChoice choice = LocomotionSelectors.SelectStop(
                _runtime.Set.Locomotion, jogging, abrupt: false, lowSpeed: false, upcoming);
            if (!choice.IsValid || !choice.Clip.Metadata.HasDistance) return 0f;

            return ScaledAuthoredDistance(choice.Clip.Metadata);
        }

        private bool TryEnterStop(ILocomotionDrive locomotion, float agentSpeed)
        {
            if (!_runtime.Config.EnablePlantedStops || locomotion.InManagedMotion) return false;
            if (locomotion.PathPending) return false;

            // Stop selection follows the gait the body is actually in, not the commanded
            // speed — a short leg where jog was commanded but never reached must not slam
            // a jog stop authored for full momentum.
            bool jogging = _moveBlend.Parameter > (_walkThreshold + _jogThreshold) * 0.5f;

            // Planted stops are a momentum feature: at jog speed the plant reads athletic
            // and necessary; at walking pace it reads theatrical, so walking arrivals
            // settle via agent braking + idle blend unless explicitly enabled.
            if (!jogging && !_runtime.Config.PlantedStopsWhileWalking) return false;

            bool lowSpeed = agentSpeed < _walkThreshold * _runtime.Config.LowSpeedStopFraction;
            FootSide upcoming = FootPhaseUtil.NextPlantFoot(DominantCycleMetadata(), _moveBlend.Phase);

            MotionChoice choice = LocomotionSelectors.SelectStop(
                _runtime.Set.Locomotion, jogging, abrupt: false, lowSpeed, upcoming);
            if (!choice.IsValid || !choice.Clip.Metadata.HasDistance)
            {
                if (!_stopDegradationLogged)
                {
                    _stopDegradationLogged = true;
                    _runtime.Trace.State(
                        "Planted stops degraded to agent auto-braking: stop clips or their distance " +
                        "metadata are missing (run the Clip Motion Analyzer).");
                }
                return false;
            }

            // At crawl speeds only the low-speed settle clip reads right; when the set
            // has none, braking into a plain idle blend beats a full-speed plant.
            if (lowSpeed && choice.Clip != _runtime.Set.Locomotion.WalkStopLowSpeed)
            {
                if (!_lowSpeedStopSkipTraced)
                {
                    _lowSpeedStopSkipTraced = true;
                    if (_runtime.Trace.IsDetail)
                        _runtime.Trace.Detail(
                            $"Planted stop '{choice.Label}' skipped at crawl speed {agentSpeed:F2} m/s " +
                            "(no low-speed stop clip authored) — settling via plain idle blend.");
                }
                return false;
            }

            float stopDistance = ScaledAuthoredDistance(choice.Clip.Metadata);
            float remaining = locomotion.RemainingDistance;
            if (remaining > stopDistance * 1.05f + 0.02f) return false;

            // A planted stop is a full-momentum performance and must be earned: a leg
            // that never genuinely cruised (a short reposition) settles with agent
            // braking and a plain idle blend instead of stop-clip theater.
            // PlantedStopMinTravel is authored in metres against the reference rig (like the
            // clip metadata) — scaled here at the point of use, never written back to the asset.
            float scaledMinTravel = _runtime.Config.PlantedStopMinTravel * _runtime.MotionScale;
            if (_cruiseDistance < scaledMinTravel)
            {
                if (!_lowSpeedStopSkipTraced)
                {
                    _lowSpeedStopSkipTraced = true;
                    if (_runtime.Trace.IsDetail)
                        _runtime.Trace.Detail(
                            $"Stop '{choice.Label}' skipped: leg cruised {_cruiseDistance:F2}m < " +
                            $"{scaledMinTravel:F2}m — settling via braking + idle blend.");
                }
                return false;
            }

            // The clip must also fit the leg: distance matching absorbs ~±25%, but with
            // meaningfully less path than the clip travels the agent parks mid-clip and
            // the character steps in place. Brake into a plain idle blend instead.
            if (remaining < stopDistance * 0.75f)
            {
                if (!_lowSpeedStopSkipTraced)
                {
                    _lowSpeedStopSkipTraced = true;
                    if (_runtime.Trace.IsDetail)
                        _runtime.Trace.Detail(
                            $"Stop '{choice.Label}' skipped: remaining {remaining:F2}m can't fit " +
                            $"the clip's {stopDistance:F2}m travel — settling via plain idle blend.");
                }
                return false;
            }

            locomotion.BeginManagedMotion();

            // Match the clip's playback to the actual approach: a plant authored at
            // 1.28 m/s entered while moving slower plays proportionally slower, so the
            // feet never outrun the body's real momentum.
            float clipEntrySpeed = MotionDrive.SpeedAt(
                choice.Clip.Metadata, 0.05f, choice.Clip.Length, _runtime.MotionScale);
            _stopRate = clipEntrySpeed > 0.2f
                ? Mathf.Clamp(agentSpeed / clipEntrySpeed, 0.85f, 1.15f)
                : 1f;

            _activeMotion = choice;
            _prevMotionNorm = 0f;
            _stopIsManaged = true;
            _stopDestination = locomotion.Destination;
            ResetTurnDrive();
            _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                FootIkSettings(), restartIfSame: true);
            _mixer.SetCurrentSpeed(_stopRate);

            _state = LocoState.Stop;
            StateLabel = choice.Label;
            _runtime.ReportTransition(
                LayerName, "Move", choice.Label, choice.Clip.ClipName,
                _runtime.Config.LocomotionCrossfadeSeconds,
                $"distance match: remaining {remaining:F2}m ≤ authored {stopDistance:F2}m, " +
                $"plant={upcoming}, lowSpeed={lowSpeed}, rate={_stopRate:F2}");
            return true;
        }

        private void TickStop(in LayerTickContext context)
        {
            ILocomotionDrive locomotion = _runtime.Locomotion;
            float norm = _currentNormalizedTime;
            ClipMotionMetadata meta = _activeMotion.Clip.Metadata;

            if (!_stopIsManaged && TryResumeMoveDuringAbruptStop(locomotion))
                return;

            if (_stopIsManaged && locomotion != null)
            {
                // Abort only when the destination actually changed (a new MoveTo mid-stop).
                // Distance readings are NOT used for this: multi-corner paths intermittently
                // report unknown remaining distance, which previously caused stop→move churn.
                Vector3 destinationDelta = locomotion.Destination - _stopDestination;
                if (destinationDelta.sqrMagnitude > 0.04f)
                {
                    // A destination nudged along the same heading that still lands inside
                    // the clip's remaining travel band is absorbed by distance matching —
                    // aborting to Move for a sub-step correction causes visible
                    // Stop→Move→Stop churn when destinations update near the stop envelope.
                    float expectedLeft = Mathf.Max(
                        0f, ScaledAuthoredDistance(meta) - ScaledDistanceAt(meta, norm));
                    float newRemaining = locomotion.RemainingDistance;
                    bool headingHolds = Mathf.Abs(locomotion.SignedAngleToSteering) < 60f;

                    if (headingHolds &&
                        newRemaining >= expectedLeft * 0.75f &&
                        newRemaining <= expectedLeft * 1.35f + 0.1f)
                    {
                        _stopDestination = locomotion.Destination;
                        if (_runtime.Trace.IsDetail)
                            _runtime.Trace.Detail(
                                $"Stop retargeted in place: destination moved " +
                                $"{destinationDelta.magnitude:F2}m, remaining {newRemaining:F2}m " +
                                $"fits the clip's {expectedLeft:F2}m left.");
                    }
                    else
                    {
                        locomotion.EndManagedMotion();
                        _stopIsManaged = false;
                        EnterMove("stop aborted — new destination", _moveBlend.Phase);
                        return;
                    }
                }

                // Two-sided distance matching: the agent speed leans toward the clip's plan
                // AND the clip rate leans toward the real path. One-sided matching only
                // corrected the agent — when the capsule still arrived early the clip kept
                // walking against a parked body (in-place marching for the rest of the clip).
                float expectedRemaining = Mathf.Max(
                    0f, ScaledAuthoredDistance(meta) - ScaledDistanceAt(meta, norm));
                float actualRemaining = locomotion.RemainingDistance;
                float speed = MotionDrive.SpeedAt(
                    meta, norm, _activeMotion.Clip.Length, _runtime.MotionScale) * _stopRate;
                if (expectedRemaining > 0.05f)
                {
                    float ratio = Mathf.Clamp(actualRemaining / expectedRemaining, 0.8f, 1.3f);
                    speed *= ratio;
                    _mixer.SetCurrentSpeed(_stopRate * Mathf.Clamp(1f / ratio, 0.9f, 1.15f));
                }
                locomotion.SetManagedSpeed(speed);
                AnimationSpeed = speed;
            }
            else
            {
                AnimationSpeed = 0f;
            }

            // Some stop clips carry multi-second settle tails (JogStop_LF is 5.5s for 1.26m
            // of travel). Once the authored distance is fully covered and the agent stands,
            // hand off to idle instead of waiting the tail out.
            bool clipDone = _mixer.IsCurrentClipFinished || norm >= 0.98f;
            bool travelDone = _stopIsManaged &&
                              norm >= 0.55f &&
                              ScaledDistanceAt(meta, norm) >= ScaledAuthoredDistance(meta) - 0.02f &&
                              (locomotion == null || locomotion.Speed < 0.05f);
            // The entry gate admits legs down to 75% of the clip's authored travel, so the
            // capsule can legitimately arrive before the clip covers its distance. Once the
            // agent is parked, keeping the clip walking is in-place marching — settle now.
            bool arrivedEarly = _stopIsManaged &&
                                norm >= 0.35f &&
                                locomotion != null &&
                                locomotion.RemainingDistance <= 0.03f &&
                                locomotion.Speed < 0.05f;
            if (!clipDone && !travelDone && !arrivedEarly)
                return;

            if (_stopIsManaged && locomotion != null)
            {
                locomotion.EndManagedMotion();
                float residual = locomotion.RemainingDistance;
                if (residual > 0.2f && locomotion.IsMoving)
                {
                    if (_runtime.Trace.IsDetail)
                        _runtime.Trace.Detail($"Stop undershot destination by {residual:F2}m — resuming Move.");
                    _stopIsManaged = false;
                    EnterMove("stop undershoot correction", _moveBlend.Phase);
                    return;
                }

                locomotion.CompleteMoveFromAnimation();
            }

            _stopIsManaged = false;
            // Settle tails are often cut early (travelDone) — ease into idle a touch
            // slower than a state crossfade so the weight shift reads out.
            EnterIdle(StateLabel, "stop finished",
                Mathf.Max(_runtime.Config.LocomotionCrossfadeSeconds, 0.4f));
        }

        private bool TryResumeMoveDuringAbruptStop(ILocomotionDrive locomotion)
        {
            if (locomotion == null || !locomotion.IsMoving) return false;
            if (_moveBlend == null) return false;
            if (locomotion.PathPending) return false;

            _cruiseDistance = 0f;
            float angle = locomotion.SignedAngleToSteering;
            if (Mathf.Abs(angle) >= _runtime.Config.Turn180MinAngle &&
                TryEnterTurn(locomotion, angle)) return true;

            EnterStartOrMove(
                locomotion,
                angle,
                "abrupt stop interrupted by new move",
                trimWindup: true);
            return true;
        }

        // ------------------------------------------------------------------ abrupt stop

        private void HandleMoveEnded(bool reachedDestination)
        {
            if (reachedDestination) return;
            if (_state is not (LocoState.Move or LocoState.Start or LocoState.SpeedChange)) return;

            // Forced cancel (Stop() call): the agent halts instantly — velocity zeroed, not
            // merely un-pathed — so the abrupt clip has no residual glide to fight. Play it
            // only from jog-level momentum; walk-speed cancels read better as a short settle
            // into idle, since the abrupt clips are authored as hard brakes. A character that
            // chose to stop (StopGracefully) never reaches here in Move: it runs its stride
            // out and earns a planted stop through TryEnterStop instead.
            ILocomotionDrive locomotion = _runtime.Locomotion;
            locomotion?.EndManagedMotion();

            bool hasAbruptStopMomentum = HasAbruptStopMomentum(out float momentumSpeed);
            // PlantedStopMinTravel is authored in metres against the reference rig — scaled
            // at the point of use, never written back to the asset.
            if (_runtime.Config.EnablePlantedStops &&
                hasAbruptStopMomentum &&
                _cruiseDistance >= _runtime.Config.PlantedStopMinTravel * _runtime.MotionScale)
            {
                MotionChoice choice = new(
                    _runtime.Set.Locomotion.JogStopAbrupt, "JogStop:Abrupt");

                if (choice.IsValid)
                {
                    _activeMotion = choice;
                    _prevMotionNorm = 0f;
                    _stopIsManaged = false; // agent already halted
                    _stopRate = 1f;
                    ResetTurnDrive();
                    _mixer.Play(choice.Clip.Clip, _runtime.Config.LocomotionCrossfadeSeconds,
                        FootIkSettings(), restartIfSame: true);

                    string from = StateLabel;
                    _state = LocoState.Stop;
                    StateLabel = choice.Label;
                    _runtime.ReportTransition(
                        LayerName, from, choice.Label, choice.Clip.ClipName,
                        _runtime.Config.LocomotionCrossfadeSeconds,
                        $"move canceled — abrupt jog stop (speed={momentumSpeed:F2} m/s)");
                    return;
                }
            }

            EnterIdle(StateLabel, "move canceled", _runtime.Config.LocomotionCrossfadeSeconds);
        }

        private bool HasAbruptStopMomentum(out float momentumSpeed)
        {
            momentumSpeed = _state == LocoState.Move && _moveBlend != null
                ? Mathf.Max(AnimationSpeed, _moveBlend.Parameter)
                : AnimationSpeed;

            if (_jogThreshold <= _walkThreshold) return false;

            float jogEntrySpeed = (_walkThreshold + _jogThreshold) * 0.5f;
            return momentumSpeed >= jogEntrySpeed;
        }

        private ClipPlaySettings FootIkSettings()
        {
            var settings = ClipPlaySettings.Default;
            settings.ApplyFootIK = _runtime.Config.EnableFootIK;
            return settings;
        }
    }
}
