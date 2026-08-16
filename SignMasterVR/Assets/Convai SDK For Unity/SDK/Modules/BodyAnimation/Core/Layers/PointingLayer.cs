using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>
    ///     Directional pointing overlay on the upper-body mask. Picks the clip whose authored
    ///     direction is angularly closest to the target, raises the arm, freezes playback at
    ///     the apex while the hold lasts (re-aiming with a crossfade when the target crosses
    ///     into another direction bucket), then resumes so the lower-arm tail plays before
    ///     the layer fades out.
    /// </summary>
    internal sealed class PointingLayer : IAnimationLayer
    {
        public const string LayerName = "Pointing";

        /// <summary>Fallback pointing origin height when the rig has no resolvable chest bone.</summary>
        private const float DefaultChestHeight = 1.35f;

        private enum Mode
        {
            Off,
            Raising,
            Holding,
            Releasing,
            FadingOut
        }

        private LayerRuntime _runtime;
        private CrossfadeMixer _mixer;
        private Mode _mode = Mode.Off;
        private float _chestHeight = DefaultChestHeight;

        private PointingEntry _activeEntry;
        private BodyAnimationPointingHandle _handle;
        private Transform _targetTransform;
        private Vector3 _targetPosition;
        private float _holdSeconds = -1f;
        private float _holdTimer;
        private float _fade01;
        private float _speed = 1f;
        private float _blendInSeconds = -1f;
        private float _blendOutSeconds = -1f;
        private bool _blendOnAutoRelease;
        private float _weightMultiplier = 1f;

        public string Name => LayerName;

        public float Weight { get; private set; }

        public string StateLabel { get; private set; } = "Off";

        public string ActiveClipName => _mixer != null && _mixer.CurrentClip != null
            ? _mixer.CurrentClip.name
            : "(none)";

        public float ActiveNormalizedTime => _mixer?.CurrentNormalizedTime ?? 0f;

        // ------------------------------------------------------------------ test hooks

        /// <summary>True while the pose mixer is mid-crossfade (regression: re-pointing a
        /// live pose must always crossfade, never zero-fade swap).</summary>
        internal bool MixerTransitioningForTests => _mixer is { IsTransitioning: true };

        internal string ModeLabelForTests => _mode.ToString();

        /// <summary>
        ///     True while a pointing gesture is raising, holding, releasing, or fading out.
        ///     Used by the talk layer's beat-gesture suppression: a beat must never
        ///     fight an active pointing hold for the arms.
        /// </summary>
        internal bool IsActive => _mode != Mode.Off;

        public void Initialize(LayerRuntime runtime, int port)
        {
            _runtime = runtime;
            _mixer = new CrossfadeMixer(runtime.Graph, runtime.Config.BlendCurve)
            {
                OverflowReported = msg => runtime.Trace?.Warning($"[PointingLayer] {msg}")
            };
            runtime.Mixer.ConnectLayer(port, _mixer.Playable, runtime.Set.UpperBodyMask);
            _chestHeight = ResolveChestHeight(runtime);
        }

        /// <summary>
        ///     Pointing directions are measured from chest height. Derive it from the actual
        ///     rig (child and giant characters alike) instead of assuming an adult human;
        ///     rigs without a resolvable chest bone use the adult-human fallback.
        /// </summary>
        private static float ResolveChestHeight(LayerRuntime runtime)
        {
            Animator animator = runtime.Animator;
            if (animator == null || runtime.CharacterRoot == null) return DefaultChestHeight;

            // GetBoneTransform throws (it does not return null) when the Animator carries no valid
            // humanoid avatar. BuildRuntime validates the avatar before it ever builds a layer
            // stack, but a set-swap handoff rebuilds the stack without re-validating, so an avatar
            // cleared at runtime would take the whole handoff down with an exception rather than
            // degrading. Fall back to the adult-human default, exactly as an unmapped chest does.
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                return DefaultChestHeight;

            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) return DefaultChestHeight;

            float height = runtime.CharacterRoot.InverseTransformPoint(chest.position).y;
            return height > 0.1f ? height : DefaultChestHeight;
        }

        public void Tick(in LayerTickContext context)
        {
            switch (_mode)
            {
                case Mode.Raising:
                    TickAim();
                    if (_mixer.CurrentNormalizedTime >= _runtime.Set.Pointing.HoldNormalizedTime)
                        BeginHold();
                    break;

                case Mode.Holding:
                    TickAim();
                    TickHoldTimer(context.DeltaTime);
                    break;

                case Mode.Releasing:
                    if (_mixer.IsCurrentClipFinished)
                    {
                        _mode = Mode.FadingOut;
                        StateLabel = "FadingOut";
                    }
                    break;
            }

            TickEnvelope(context.DeltaTime);
            _mixer.Tick(context.DeltaTime);

            if (_mode == Mode.FadingOut && _fade01 <= 0f)
                Finish();
        }

        public void Teardown()
        {
            _handle?.Resolve();
            _handle = null;
            _mixer = null;
            _runtime = null;
            _activeEntry = null;
            _targetTransform = null;
            _mode = Mode.Off;
            Weight = 0f;
            _fade01 = 0f;
            StateLabel = "Off";
        }

        // ------------------------------------------------------------------ public control

        /// <summary>
        ///     Points at a world position (or a moving transform when provided).
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Authored-content invariant.</b> This layer never improvises: it
        ///         only ever plays a clip the Animation Set actually authored for the requested
        ///         direction. When the set has no pointing entries at all it fails cleanly — returns
        ///         <c>null</c>, plays nothing, <see cref="ModeLabelForTests" /> stays <c>"Off"</c> —
        ///         rather than silently degrading to a procedural pose. There is consequently no
        ///         code path inside this layer that could ever let a procedural fallback and an
        ///         authored clip fight over the same request.
        ///     </para>
        /// </remarks>
        public BodyAnimationPointingHandle Point(
            Vector3 worldPosition, Transform target, float holdSeconds, float speed,
            float blendInSeconds, float blendOutSeconds, bool blendOnAutoRelease,
            float weightMultiplier = 1f)
        {
            if (!_runtime.Set.Pointing.HasAny)
            {
                _runtime.Trace.Warning(
                    "PointAt was requested, but this character's animation set has no pointing clips, " +
                    "so nothing will play. Add pointing directions to the set in the Body Animation editor.");
                return null;
            }

            _targetTransform = target;
            _targetPosition = target != null ? target.position : worldPosition;

            (float yaw, float pitch) = DirectionToTarget(_targetPosition);
            PointingEntry entry = _runtime.Set.Pointing.FindClosest(yaw, pitch);
            if (entry == null) return null;

            bool retargeting = _mode is Mode.Raising or Mode.Holding;
            // Any lingering pose — a releasing tail or a fade-out in flight — must be
            // crossfaded away, never zero-fade swapped: a zero fade while the layer still
            // has weight teleports the arm to the new clip's first frame.
            bool blendFromLivePose = _mode != Mode.Off || _fade01 > 0f;
            _handle?.Resolve(); // a previous in-flight point is superseded
            _handle = new BodyAnimationPointingHandle
            {
                ReleaseRequested = Release,
                ReleaseImmediateRequested = ReleaseImmediate,
                SpeedChangeRequested = SetSpeedLive
            };

            _activeEntry = entry;
            _holdSeconds = holdSeconds;
            _holdTimer = 0f;
            _speed = speed <= 0f ? 1f : speed;
            _blendInSeconds = blendInSeconds;
            _blendOutSeconds = blendOutSeconds;
            _blendOnAutoRelease = blendOnAutoRelease;
            _weightMultiplier = weightMultiplier > 0f ? weightMultiplier : 1f;

            var settings = ClipPlaySettings.Default;
            settings.Speed = _speed;
            _mixer.Play(
                entry.Clip,
                blendFromLivePose ? _runtime.Config.PointingReaimCrossfadeSeconds : 0f,
                settings, restartIfSame: true);

            _mode = Mode.Raising;
            StateLabel = "Raising";
            _runtime.ReportTransition(
                LayerName, retargeting ? "Holding" : "Off", "Raising", entry.Clip.name,
                _runtime.Config.PointingFadeSeconds,
                $"target yaw {yaw:F0}deg pitch {pitch:F0}deg -> clip dir ({entry.YawDegrees:F0}, {entry.PitchDegrees:F0}), " +
                $"hold={(holdSeconds < 0f ? "inf" : $"{holdSeconds:F1}s")}");

            return _handle;
        }

        /// <summary>Ends the hold; the lower-arm tail plays, then the layer fades out.</summary>
        public void Release()
        {
            if (_mode is not (Mode.Raising or Mode.Holding)) return;

            _mixer.SetCurrentSpeed(_speed);
            _mode = Mode.Releasing;
            StateLabel = "Releasing";
            _runtime.ReportTransition(
                LayerName, "Holding", "Releasing", ActiveClipName,
                _runtime.Config.PointingFadeSeconds, "hold released");
        }

        /// <summary>Stops now and cross-dissolves the current pose out, skipping the lower tail.</summary>
        public void ReleaseImmediate(float blendOutSeconds = -1f)
        {
            if (_mode is Mode.Off or Mode.FadingOut) return;
            if (blendOutSeconds > 0f) _blendOutSeconds = blendOutSeconds;
            _mixer.FreezeAll();                      // freeze current + fading sources for a static dissolve
            string from = StateLabel;
            _mode = Mode.FadingOut;
            StateLabel = "FadingOut";
            _runtime.ReportTransition(
                LayerName, from, "FadingOut", ActiveClipName,
                EffectiveBlendOut(), "immediate release");
        }

        // ------------------------------------------------------------------ internals

        private void BeginHold()
        {
            _mixer.SetCurrentSpeed(0f);
            _mode = Mode.Holding;
            StateLabel = "Holding";
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail($"Pointing hold at apex (clip '{ActiveClipName}').");
        }

        private void TickHoldTimer(float deltaTime)
        {
            if (_holdSeconds < 0f) return;

            _holdTimer += deltaTime;
            if (_holdTimer >= _holdSeconds)
            {
                if (_blendOnAutoRelease) ReleaseImmediate();
                else Release();
            }
        }

        private void TickAim()
        {
            if (_targetTransform != null)
                _targetPosition = _targetTransform.position;

            (float yaw, float pitch) = DirectionToTarget(_targetPosition);
            PointingEntry closest = _runtime.Set.Pointing.FindClosest(yaw, pitch);
            if (closest == null || closest == _activeEntry) return;

            bool wasHolding = _mode == Mode.Holding;
            var settings = ClipPlaySettings.Default;
            settings.Speed = _speed;
            settings.StartNormalizedTime = wasHolding
                ? _runtime.Set.Pointing.HoldNormalizedTime
                : _mixer.CurrentNormalizedTime;

            _mixer.Play(closest.Clip, _runtime.Config.PointingReaimCrossfadeSeconds, settings, restartIfSame: true);
            if (wasHolding)
                _mixer.SetCurrentSpeed(0f);

            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Pointing re-aim: ({yaw:F0}, {pitch:F0}) -> clip '{closest.Clip.name}'.");
            _activeEntry = closest;
        }

        private void TickEnvelope(float deltaTime)
        {
            bool on = _mode is Mode.Raising or Mode.Holding or Mode.Releasing;
            float duration = Mathf.Max(0.01f, on ? EffectiveBlendIn() : EffectiveBlendOut());
            _fade01 = Mathf.MoveTowards(_fade01, on ? 1f : 0f, deltaTime / duration);
            Weight = Mathf.Clamp01(_runtime.Config.BlendCurve.Evaluate(_fade01) *
                                   _runtime.Config.PointingLayerWeight * _weightMultiplier);
        }

        private void SetSpeedLive(float speed)
        {
            _speed = speed <= 0f ? 1f : speed;
            if (_mode is Mode.Raising or Mode.Releasing)
                _mixer.SetCurrentSpeed(_speed);
        }

        private float EffectiveBlendIn() => _blendInSeconds > 0f ? _blendInSeconds : _runtime.Config.PointingFadeSeconds;
        private float EffectiveBlendOut() => _blendOutSeconds > 0f ? _blendOutSeconds : _runtime.Config.PointingFadeSeconds;

        private void Finish()
        {
            _mixer.Clear();
            _mode = Mode.Off;
            StateLabel = "Off";
            _activeEntry = null;
            _targetTransform = null;

            BodyAnimationPointingHandle handle = _handle;
            _handle = null;
            handle?.Resolve();

            _runtime.ReportTransition(
                LayerName, "FadingOut", "Off", "(none)",
                _runtime.Config.PointingFadeSeconds, "pointing finished");
        }

        private (float yaw, float pitch) DirectionToTarget(Vector3 worldPosition)
        {
            Transform root = _runtime.CharacterRoot;
            if (root == null) return (0f, 0f);

            Vector3 origin = root.position + Vector3.up * _chestHeight;
            Vector3 local = root.InverseTransformDirection(worldPosition - origin);
            if (local.sqrMagnitude < 1e-4f) return (0f, 0f);

            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float horizontal = new Vector2(local.x, local.z).magnitude;
            float pitch = Mathf.Atan2(local.y, horizontal) * Mathf.Rad2Deg;
            return (yaw, pitch);
        }
    }
}
