using System;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>
    ///     Montage-style action/gesture layer. Plays named <see cref="ActionEntry" /> chains
    ///     (intro → main × loop policy → outro) through a <see cref="OneShotSlot" /> with
    ///     per-entry masking (full body / upper body / custom), a curve-eased weight
    ///     envelope, hold timeouts, and interruption rules. Full-body actions suspend
    ///     locomotion (per entry) and duck the talk/pointing layers via
    ///     <see cref="FullBodyDuck01" />.
    /// </summary>
    internal sealed class ActionLayer : IAnimationLayer
    {
        public const string LayerName = "Action";

        /// <summary>
        ///     Layer weight below which an avatar-mask change is invisible. Masks cut
        ///     instantly (they never blend); replacing an action whose mask differs while
        ///     the layer is heavier than this fades out first and starts at the trough.
        /// </summary>
        private const float MaskSwapWeightEpsilon = 0.02f;

        private enum Mode
        {
            Off,
            Playing,
            FadingOut
        }

        private LayerRuntime _runtime;
        private OneShotSlot _slot;
        private AvatarMask _runtimeFullBodyMask;
        private AvatarMask _appliedMask;
        private int _port = -1;

        private Mode _mode = Mode.Off;
        private ActionEntry _activeEntry;
        private BodyAnimationActionHandle _activeHandle;
        private float _fade01;
        private float _fadeInSeconds;
        private float _fadeOutSeconds;
        private float _holdLimitSeconds;
        private float _holdTimer;
        private bool _maskWarningLogged;
        private bool _fadingOutInterrupted;

        // Duck-blend defect fix: a direct same-mask interrupt (Play() replacing one FullBody
        // entry with another while Weight stays visible) swaps _activeEntry instantly. If the
        // two entries disagree on AllowConversationOverlays, FullBodyDuck01 would otherwise snap
        // (Weight-tracked <-> 0) even though the mixer itself is crossfading the clips smoothly.
        // These fields blend the duck value itself across that same crossfade window.
        private bool _duckBlendActive;
        private float _duckBlendFromValue;
        private float _duckBlendElapsed;
        private float _duckBlendDuration;

        // A replacement whose mask differs from the visible layer's, waiting for the old
        // action's fade-out to reach the weight trough. Its handle is live from the moment
        // Play returned it; Finish() starts the entry once the trough is reached.
        private ActionEntry _pendingEntry;
        private ActionPlayOptions _pendingOptions;
        private BodyAnimationActionHandle _pendingHandle;
        private float _activeWeightMultiplier = 1f;

        /// <summary>Set by the controller; mirrors lifecycle to the public event.</summary>
        public Action<BodyAnimationActionEvent> LifecycleChanged;

        public string Name => LayerName;

        public float Weight { get; private set; }

        public string StateLabel { get; private set; } = "Off";

        public string ActiveClipName => _slot != null && _slot.IsPlaying ? _slot.ActiveClipName : "(none)";

        public float ActiveNormalizedTime => _slot?.NormalizedTime ?? 0f;

        /// <summary>Current action name, empty when idle.</summary>
        public string ActiveActionName => _activeEntry?.ActionName ?? string.Empty;

        /// <summary>
        ///     How strongly a full-body action currently owns the pose [0..1]. The controller
        ///     multiplies the talk/pointing weights by (1 − this) so masked overlays never
        ///     fight a full-body action. A full-body entry authored with
        ///     <see cref="ActionEntry.AllowConversationOverlays" /> (the "seated
        ///     conversation") never contributes to the duck: the gate lives here, at the same
        ///     source <see cref="Weight" /> is read from, so the entry's own fade-in/out curve
        ///     is what stays smooth. When a direct same-mask <see cref="Play" /> interrupt swaps
        ///     in an entry whose duck level differs from the outgoing one (Weight stays visible
        ///     the whole time, so there is no natural fade to ride), the value itself is blended
        ///     from the outgoing level to the incoming one over the same crossfade duration the
        ///     mixer uses for that interrupt — see the duck-blend fields.
        /// </summary>
        public float FullBodyDuck01
        {
            get
            {
                float current = ComputeDuckLevel(_activeEntry);
                if (!_duckBlendActive) return current;

                float t = _duckBlendDuration > 0f ? Mathf.Clamp01(_duckBlendElapsed / _duckBlendDuration) : 1f;
                return Mathf.Lerp(_duckBlendFromValue, current, t);
            }
        }

        private float ComputeDuckLevel(ActionEntry entry) =>
            entry is { MaskMode: ActionMaskMode.FullBody, AllowConversationOverlays: false } ? Weight : 0f;

        /// <summary>True while a full-body action is currently playing (any weight &gt; 0).</summary>
        public bool IsRunningFullBodyAction =>
            _mode != Mode.Off && _activeEntry is { MaskMode: ActionMaskMode.FullBody };

        /// <summary>
        ///     True while an action is playing and refuses interruption — a new
        ///     <see cref="Play" /> call for a different entry would be rejected against it.
        /// </summary>
        public bool IsBusyNonInterruptible =>
            _mode == Mode.Playing && _activeEntry != null && !_activeEntry.Interruptible;

        /// <summary>
        ///     True while any action — full body or upper body — is playing or fading out.
        ///     Used by the talk layer's beat-gesture suppression: a beat must never
        ///     fight a running action for the arms, full-body or not.
        /// </summary>
        internal bool IsActive => _mode != Mode.Off;

        /// <summary>
        ///     True while an action is playing (or fading out) AND it does not carry
        ///     <see cref="ActionEntry.AllowConversationOverlays" />. The controller's
        ///     onset-beat suppression and <see cref="Policy.ReferentialGestureDirector" />'s
        ///     peer-ownership check both use this instead of the raw <see cref="IsActive" /> so
        ///     a seated-conversation hold never blocks beats/referential gestures — it's a
        ///     conversation pose, not arm ownership.
        /// </summary>
        internal bool SuppressesConversationOverlays =>
            IsActive && !(_activeEntry?.AllowConversationOverlays ?? false);

        public void Initialize(LayerRuntime runtime, int port)
        {
            _runtime = runtime;
            _port = port;
            _slot = new OneShotSlot(runtime.Graph, runtime.Config.BlendCurve,
                msg => runtime.Trace?.Warning($"[ActionLayer] {msg}"));
            _slot.Completed += HandleSlotCompleted;

            runtime.Mixer.ConnectLayer(port, _slot.Playable, runtime.Set.UpperBodyMask);
            _appliedMask = runtime.Set.UpperBodyMask;
        }

        // ------------------------------------------------------------------ test hooks

        internal bool HasPendingReplaceForTests => _pendingEntry != null;
        internal AvatarMask AppliedMaskForTests => _appliedMask;

        public void Tick(in LayerTickContext context)
        {
            TickEnvelope(context.DeltaTime);
            TickHoldTimeout(context.DeltaTime);
            _slot.Tick(context.DeltaTime);
            TickDuckBlend(context.DeltaTime);

            if (_mode == Mode.FadingOut && _fade01 <= 0f)
                Finish();
        }

        private void TickDuckBlend(float deltaTime)
        {
            if (!_duckBlendActive) return;

            _duckBlendElapsed += deltaTime;
            if (_duckBlendElapsed >= _duckBlendDuration)
                _duckBlendActive = false;
        }

        public void Teardown()
        {
            if (_slot != null)
                _slot.Completed -= HandleSlotCompleted;

            CancelPendingReplace("teardown");
            InterruptActive("teardown");

            if (_runtimeFullBodyMask != null)
            {
                RuntimeMaskCache.Release(_runtimeFullBodyMask);
                _runtimeFullBodyMask = null;
            }

            _slot = null;
            _runtime = null;
            _activeEntry = null;
            _appliedMask = null;
            _mode = Mode.Off;
            Weight = 0f;
            _fade01 = 0f;
            StateLabel = "Off";
            _duckBlendActive = false;
            _duckBlendFromValue = 0f;
            _duckBlendElapsed = 0f;
            _duckBlendDuration = 0f;
        }

        // ------------------------------------------------------------------ public control

        /// <summary>
        ///     Starts an action. Returns null (and reports Rejected) when a non-interruptible
        ///     action is still playing. When the replacement's mask differs from the visibly
        ///     playing action's, the old action fades out first and the new one starts at the
        ///     weight trough — masks cut instantly and must never swap under a visible pose.
        /// </summary>
        public BodyAnimationActionHandle Play(ActionEntry entry, in ActionPlayOptions options)
        {
            if (entry == null || !entry.IsValid) return null;

            if (_mode == Mode.Playing && _activeEntry != null && !_activeEntry.Interruptible)
            {
                if (_runtime.Trace.IsState)
                    _runtime.Trace.State(
                        $"Action '{entry.ActionName}' rejected — '{_activeEntry.ActionName}' is not interruptible.");
                LifecycleChanged?.Invoke(
                    new BodyAnimationActionEvent(entry.ActionName, BodyAnimationActionPhase.Rejected));
                return null;
            }

            bool interrupting = _mode != Mode.Off;
            if (interrupting && Weight > MaskSwapWeightEpsilon && ResolveMaskFor(entry) != _appliedMask)
                return QueueReplace(entry, in options);

            // Direct same-mask interrupt: Weight stays visible (no trough), so a duck-level
            // change between the outgoing and incoming entry (AllowConversationOverlays differs)
            // would otherwise snap. Snapshot BEFORE InterruptActive clears _activeEntry.
            if (interrupting && Weight > MaskSwapWeightEpsilon)
                BeginDuckBlendIfNeeded(entry);
            else
                _duckBlendActive = false;

            CancelPendingReplace($"superseded by '{entry.ActionName}'");
            if (interrupting)
                InterruptActive($"replaced by '{entry.ActionName}'");

            return StartEntry(entry, in options, interrupting);
        }

        /// <summary>
        ///     Starts a duck-value blend when a direct same-mask interrupt would otherwise snap
        ///     <see cref="FullBodyDuck01" /> (see the field-group comment). No-op — and clears
        ///     any blend already in flight — when the outgoing and incoming duck levels match
        ///     (e.g. an unflagged full-body entry replacing another unflagged one, or either
        ///     entry not being an un-flagged FullBody entry at all): byte-identical to before
        ///     this fix on every such path.
        /// </summary>
        private void BeginDuckBlendIfNeeded(ActionEntry incomingEntry)
        {
            // FullBodyDuck01 (not a raw recompute) so a second rapid interrupt arriving mid-blend
            // continues from whatever is currently on screen rather than jumping to the
            // un-blended flag value.
            float outgoingDuck = FullBodyDuck01;
            float incomingDuck = ComputeDuckLevel(incomingEntry);

            if (Mathf.Approximately(outgoingDuck, incomingDuck))
            {
                _duckBlendActive = false;
                return;
            }

            _duckBlendActive = true;
            _duckBlendFromValue = outgoingDuck;
            _duckBlendElapsed = 0f;
            _duckBlendDuration = Mathf.Max(0.01f, _runtime.Config.ActionChainCrossfadeSeconds);
        }

        private BodyAnimationActionHandle QueueReplace(ActionEntry entry, in ActionPlayOptions options)
        {
            CancelPendingReplace($"superseded by '{entry.ActionName}'");

            // Dissolve the current action BEFORE queuing the replacement: Interrupt's
            // cancel-pending guard exists for user-initiated stops and would otherwise
            // swallow the entry we are about to queue.
            if (_mode == Mode.Playing)
                Interrupt();

            // The replaced action is logically dead the moment the replacement is
            // accepted — resolve its handle now (matching the same-mask path) instead of
            // making awaiters wait out the visual fade. Finish() re-resolving is a no-op.
            _activeHandle?.ResolveInterrupted();

            _pendingEntry = entry;
            _pendingOptions = options;
            _pendingHandle = CreateHandle(entry);

            if (_runtime.Trace.IsState)
                _runtime.Trace.State(
                    $"Action '{entry.ActionName}' queued behind a mask-safe fade-out of '{ActiveActionName}'.");
            return _pendingHandle;
        }

        private BodyAnimationActionHandle CreateHandle(ActionEntry entry)
        {
            var handle = new BodyAnimationActionHandle(entry.ActionName);
            handle.StopRequested = () => RequestStop();
            handle.StopImmediateRequested = blend => Interrupt(blend);
            return handle;
        }

        private BodyAnimationActionHandle StartEntry(
            ActionEntry entry, in ActionPlayOptions options, bool interrupting,
            BodyAnimationActionHandle preparedHandle = null)
        {
            ConvaiBodyAnimationConfig config = _runtime.Config;
            _fadeInSeconds = options.FadeInSeconds > 0f
                ? options.FadeInSeconds
                : ConvaiBodyAnimationConfig.ResolveOverride(entry.FadeInSecondsOverride, config.ActionFadeInSeconds);
            _fadeOutSeconds = options.FadeOutSeconds > 0f
                ? options.FadeOutSeconds
                : ConvaiBodyAnimationConfig.ResolveOverride(entry.FadeOutSecondsOverride, config.ActionFadeOutSeconds);

            ApplyMask(entry);
            SuspendLocomotionIfNeeded(entry);

            float speed = entry.Speed * (options.SpeedMultiplier > 0f ? options.SpeedMultiplier : 1f);
            var spec = new OneShotSpec
            {
                Intro = entry.IntroClip,
                Main = entry.Clip,
                Outro = entry.OutroClip,
                Loop = entry.LoopMode switch
                {
                    ActionLoopMode.LoopCount => OneShotLoop.Count,
                    ActionLoopMode.HoldUntilStopped => OneShotLoop.Hold,
                    _ => OneShotLoop.Once
                },
                LoopCount = entry.LoopCount,
                Speed = speed,
                ChainFadeSeconds = config.ActionChainCrossfadeSeconds
            };

            // The layer weight does the blend-in from Off; when interrupting, crossfade clips.
            _slot.Play(in spec, interrupting ? config.ActionChainCrossfadeSeconds : 0f);

            _activeEntry = entry;
            _activeWeightMultiplier = options.WeightMultiplier > 0f ? options.WeightMultiplier : 1f;
            _activeHandle = preparedHandle ?? CreateHandle(entry);
            _holdLimitSeconds = entry.LoopMode == ActionLoopMode.HoldUntilStopped ? options.HoldSeconds : 0f;
            _holdTimer = 0f;
            _fadingOutInterrupted = false;
            _mode = Mode.Playing;
            StateLabel = $"Playing:{entry.ActionName}";

            _runtime.ReportTransition(
                LayerName, interrupting ? "Playing" : "Off", StateLabel, entry.Clip.name,
                _fadeInSeconds,
                $"mask={entry.MaskMode}, loop={entry.LoopMode}" +
                (entry.HasIntro ? ", intro" : string.Empty) +
                (entry.HasOutro ? ", outro" : string.Empty) +
                (_holdLimitSeconds > 0f ? $", hold={_holdLimitSeconds:F1}s" : string.Empty));
            LifecycleChanged?.Invoke(
                new BodyAnimationActionEvent(entry.ActionName, BodyAnimationActionPhase.Started));

            return _activeHandle;
        }

        /// <summary>Gracefully stops the current action. Returns false when none plays.</summary>
        public bool RequestStop()
        {
            if (_pendingEntry != null)
            {
                // A stop after a queued replace cancels the replace; the old action is
                // already fading out underneath it.
                CancelPendingReplace("stop requested");
                return true;
            }

            if (_mode != Mode.Playing || _activeEntry == null) return false;

            _slot.RequestStop();
            if (_runtime.Trace.IsState)
                _runtime.Trace.State($"Action '{_activeEntry.ActionName}' stop requested.");
            LifecycleChanged?.Invoke(
                new BodyAnimationActionEvent(_activeEntry.ActionName, BodyAnimationActionPhase.Ending));
            return true;
        }

        /// <summary>Immediately stops the current action: freezes the pose and cross-dissolves the
        /// layer out over blendOutSeconds (&lt;=0 = the resolved fade-out), skipping the rest of the
        /// chain/outro. Resolves the handle as interrupted. Returns false when none plays.</summary>
        public bool Interrupt(float blendOutSeconds = -1f)
        {
            if (_pendingEntry != null)
            {
                CancelPendingReplace("immediate stop requested");
                if (blendOutSeconds > 0f) _fadeOutSeconds = blendOutSeconds;
                return true; // the old action is already frozen and fading out
            }

            if (_mode != Mode.Playing || _activeEntry == null) return false;

            _slot.Freeze();
            if (blendOutSeconds > 0f) _fadeOutSeconds = blendOutSeconds;
            _fadingOutInterrupted = true;
            _mode = Mode.FadingOut;
            StateLabel = "FadingOut";
            _runtime.ReportTransition(
                LayerName, $"Playing:{ActiveActionName}", "FadingOut",
                _slot.ActiveClipName, _fadeOutSeconds, "immediate interrupt");
            LifecycleChanged?.Invoke(
                new BodyAnimationActionEvent(_activeEntry.ActionName, BodyAnimationActionPhase.Ending));
            return true;
        }

        // ------------------------------------------------------------------ internals

        private void TickEnvelope(float deltaTime)
        {
            float target = _mode == Mode.Playing ? 1f : 0f;
            float duration = _mode == Mode.Playing
                ? Mathf.Max(0.01f, _fadeInSeconds)
                : Mathf.Max(0.01f, _fadeOutSeconds);

            _fade01 = Mathf.MoveTowards(_fade01, target, deltaTime / duration);
            float entryWeight = _activeEntry?.TargetWeight ?? 1f;
            Weight = Mathf.Clamp01(_runtime.Config.BlendCurve.Evaluate(_fade01) *
                                   _runtime.Config.ActionLayerWeight * entryWeight * _activeWeightMultiplier);
        }

        private void TickHoldTimeout(float deltaTime)
        {
            if (_mode != Mode.Playing || _holdLimitSeconds <= 0f) return;
            if (_slot.Phase != OneShotSlot.SlotPhase.Main) return;

            _holdTimer += deltaTime;
            if (_holdTimer < _holdLimitSeconds) return;

            _holdLimitSeconds = 0f;
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Action '{_activeEntry.ActionName}' hold limit reached — stopping.");
            RequestStop();
        }

        private void HandleSlotCompleted()
        {
            if (_mode != Mode.Playing) return;

            _fadingOutInterrupted = false;
            _mode = Mode.FadingOut;
            StateLabel = "FadingOut";
            _runtime.ReportTransition(
                LayerName, $"Playing:{ActiveActionName}", "FadingOut",
                _slot.ActiveClipName, _fadeOutSeconds, "chain finished");
        }

        private void Finish()
        {
            _slot.Clear();
            _mode = Mode.Off;
            StateLabel = "Off";
            _duckBlendActive = false;

            ActionEntry finished = _activeEntry;
            BodyAnimationActionHandle handle = _activeHandle;
            bool interrupted = _fadingOutInterrupted;
            _activeEntry = null;
            _activeHandle = null;
            _fadingOutInterrupted = false;

            if (interrupted) handle?.ResolveInterrupted();
            else handle?.ResolveCompleted();

            if (finished != null)
            {
                LifecycleChanged?.Invoke(new BodyAnimationActionEvent(
                    finished.ActionName,
                    interrupted ? BodyAnimationActionPhase.Interrupted : BodyAnimationActionPhase.Completed));
            }

            // The weight trough: a queued mask-changing replacement starts here.
            if (_pendingEntry != null)
            {
                ActionEntry next = _pendingEntry;
                ActionPlayOptions options = _pendingOptions;
                BodyAnimationActionHandle prepared = _pendingHandle;
                _pendingEntry = null;
                _pendingHandle = null;
                StartEntry(next, in options, interrupting: false, prepared);
            }
        }

        private void CancelPendingReplace(string reason)
        {
            if (_pendingEntry == null) return;

            ActionEntry cancelled = _pendingEntry;
            BodyAnimationActionHandle handle = _pendingHandle;
            _pendingEntry = null;
            _pendingHandle = null;

            handle?.ResolveInterrupted();
            if (_runtime?.Trace is { IsState: true })
                _runtime.Trace.State($"Queued action '{cancelled.ActionName}' cancelled ({reason}).");
            LifecycleChanged?.Invoke(
                new BodyAnimationActionEvent(cancelled.ActionName, BodyAnimationActionPhase.Interrupted));
        }

        private void InterruptActive(string reason)
        {
            if (_activeHandle == null && _activeEntry == null) return;

            ActionEntry interrupted = _activeEntry;
            BodyAnimationActionHandle handle = _activeHandle;
            _activeEntry = null;
            _activeHandle = null;

            handle?.ResolveInterrupted();
            if (interrupted != null)
            {
                if (_runtime?.Trace is { IsState: true })
                    _runtime.Trace.State($"Action '{interrupted.ActionName}' interrupted ({reason}).");
                LifecycleChanged?.Invoke(
                    new BodyAnimationActionEvent(interrupted.ActionName, BodyAnimationActionPhase.Interrupted));
            }
        }

        private AvatarMask ResolveMaskFor(ActionEntry entry) => entry.MaskMode switch
        {
            ActionMaskMode.CustomMask when entry.CustomMask != null => entry.CustomMask,
            ActionMaskMode.UpperBody => ResolveUpperBodyMask(),
            _ => FullBodyMask()
        };

        private void ApplyMask(ActionEntry entry)
        {
            AvatarMask mask = ResolveMaskFor(entry);
            _appliedMask = mask;
            if (mask != null)
                _runtime.Mixer.SetLayerMask(_port, mask);
        }

        private AvatarMask ResolveUpperBodyMask()
        {
            AvatarMask mask = _runtime.Set.UpperBodyMask;
            if (mask == null && !_maskWarningLogged)
            {
                _maskWarningLogged = true;
                _runtime.Trace.Warning(
                    "Animation set has no Upper Body Mask — upper-body actions drive the full body, " +
                    "so they will fight locomotion. Assign a mask in the set asset.");
            }

            return mask ?? FullBodyMask();
        }

        private AvatarMask FullBodyMask()
        {
            if (_runtimeFullBodyMask == null)
                _runtimeFullBodyMask = RuntimeMaskCache.AcquireFullBody();
            return _runtimeFullBodyMask;
        }

        private void SuspendLocomotionIfNeeded(ActionEntry entry)
        {
            if (entry.MaskMode != ActionMaskMode.FullBody || !entry.SuspendsLocomotion) return;

            Core.Locomotion.ILocomotionDrive locomotion = _runtime.Locomotion;
            if (locomotion == null || !locomotion.IsMoving) return;

            if (_runtime.Trace.IsState)
                _runtime.Trace.State(
                    $"Full-body action '{entry.ActionName}' suspends locomotion — stopping agent.");
            locomotion.Stop();
        }
    }
}
