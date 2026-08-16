using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Core.Policy;
using Convai.Modules.BodyAnimation.Core.Selection;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>
    ///     Masked talk overlay. Fades in while the character is Speaking and back out
    ///     afterwards, playing weighted talk variants (emotion-aware, no immediate repeats,
    ///     optional variant switch on loop for long speeches). Live speech energy scales the
    ///     layer weight so soft speech gestures less. Per-entry body coverage picks between
    ///     the set's upper-body mask and full body — full body is honored only while the
    ///     character is stationary.
    /// </summary>
    /// <remarks>
    ///     The layer owns TWO root-mixer ports. The stationary port overrides the upper
    ///     body with the authored gesture. The moving port (<see cref="LayerPorts.TalkMoving" />,
    ///     additive, arms + hands only) carries the walk-and-talk overlay: gesture deltas
    ///     layered over the gait so arm swing survives under the gesture. Stationary ↔
    ///     moving is a weight crossfade between the two pre-configured ports — no mask or
    ///     additive flag ever changes on a visible port. Entries without additive content
    ///     fall back to a softened override (see <see cref="MovingTalkMode" />).
    /// </remarks>
    internal sealed class TalkLayer : IAnimationLayer
    {
        public const string LayerName = "Talk";

        /// <summary>
        ///     Layer weight below which an avatar-mask or additive-mode change is invisible.
        ///     Masks and the additive flag cut instantly (they never blend), so any change
        ///     while the layer is heavier than this is deferred to the envelope trough.
        /// </summary>
        private const float MaskSwapWeightEpsilon = 0.02f;

        /// <summary>
        ///     Tolerance for the interrupted-hold countdown reaching zero. Repeated
        ///     float subtraction of <c>deltaTime</c> rarely lands on exactly 0 (binary
        ///     floating point cannot represent most decimal step sizes exactly), which would
        ///     otherwise strand the hold for one extra tick past its configured duration.
        /// </summary>
        private const float HoldTimerEpsilon = 1e-4f;

        /// <summary>
        ///     Which authored pool the layer is currently sourcing clips from, resolved from
        ///     <see cref="DialogueState" /> every tick. <see cref="None" /> means the
        ///     layer is released — today's non-speaking behavior.
        /// </summary>
        private enum TalkPoolKind
        {
            None = 0,
            Talk = 1,
            Listen = 2,
            Think = 3
        }

        /// <summary>
        ///     Which segment of an authored intro→loop→outro gesture bracket the stationary
        ///     mixer is currently playing. <see cref="None" /> means no bracket is in
        ///     play — either the entry has no Intro/Outro Clip, or the pool isn't Talk (brackets
        ///     never apply to Listen/Think or the moving additive overlay).
        /// </summary>
        private enum TalkBracketPhase
        {
            None = 0,
            Intro = 1,
            Loop = 2,
            Outro = 3
        }

        private LayerRuntime _runtime;
        private CrossfadeMixer _mixer;
        private CrossfadeMixer _movingMixer;    // additive walk-and-talk overlay (own port)
        private VariantScheduler _scheduler;
        private AvatarMask _runtimeFullBodyMask;
        private AvatarMask _runtimeTalkMask;    // set's upper-body mask minus the spine
        private AvatarMask _runtimeArmsMask;    // moving overlay: arms + hands only
        private int _port = -1;

        // Slot crossfade: the share of the envelope each port receives. Smoothed toward
        // their per-tier targets over MovingTalkBlendSeconds; snapped while the envelope
        // is at the trough (crossfades are free when nothing is visible).
        private float _stationaryFactor01 = 1f;
        private float _movingFactor01;
        private int _talkIndex = -1;
        private float _fade01;              // envelope position: 0 = off, 1 = on
        private float _energyScale01 = 1f;  // smoothed speech-energy weight scale

        /// <summary>
        ///     Smoothed toward the conversational-motion-budget performer's reported intensity
        ///     (<see cref="LayerTickContext.ConversationalIntensityScale" />) with a
        ///     fixed time constant — a peer module's emotion-derived intensity report visibly
        ///     scales the talk overlay without ever popping it.
        /// </summary>
        private float _reportedIntensityScale01 = 1f;

        /// <summary>Time constant (seconds) <see cref="_reportedIntensityScale01" /> smooths toward its target.</summary>
        private const float ReportedIntensityScaleTauSeconds = 0.5f;
        private float _previousClipPhase;
        private int _activeFragmentIndex = -1;
        private float _activeFragmentEnd = -1f;
        private float _releaseDelayRemaining;
        private bool _isOn;
        private bool _maskWarningLogged;
        private BodyCoverage _appliedCoverage = BodyCoverage.UpperBody;
        private bool _appliedAdditive;

        // A queued mask/additive (and optionally clip) change waiting for the envelope
        // trough. While queued, TickEnvelope drives the weight down; the swap applies at
        // ≤ MaskSwapWeightEpsilon and the envelope rises again — a brief dip, never a pop.
        private bool _pendingApplyQueued;
        private int _pendingIndex = -1;     // -1 = keep the current clip, only re-mask
        private BodyCoverage _pendingCoverage;
        private bool _pendingAdditive;

        // Conversational state → pool. Resolved every tick from DialogueState; a pool
        // switch mid-play (e.g. Listening -> Speaking) reuses the same continuation /
        // QueuePendingApply machinery as a same-pool variant switch, so it never pops.
        // _talkIndex indexes into _activePool, not a fixed list.
        private IReadOnlyList<TalkEntry> _activePool;
        private TalkPoolKind _activeKind = TalkPoolKind.None;
        private float _fadeInSeconds;   // active kind's envelope fade-in (Talk/Think: TalkFadeInSeconds, Listen: ListenFadeInSeconds)
        private float _fadeOutSeconds;  // envelope fade-out; scaled down during an interrupted fast release
        private float _thinkingTimer;   // seconds DialogueState has continuously been Thinking (ThinkingEnterDelaySeconds gate)

        // Interruption body beat. FreezeAll() holds the pose for InterruptedFreezeSeconds,
        // then a faster-than-normal fade-out (InterruptedReleaseScale) settles the layer out.
        private bool _interruptedActive;
        private float _interruptedHoldRemaining;

        // Gesture brackets. Tracks which segment of an authored intro/outro bracket the
        // stationary mixer is currently playing; drives the intro→loop handoff in
        // TickTalkBracket. None for every entry without authored intro/outro clips — the
        // default (unauthored) path never touches this field beyond leaving it at None/Loop.
        private TalkBracketPhase _talkBracketPhase = TalkBracketPhase.None;

        // Speech-rhythm beat gestures. A short additive one-shot rides on the dedicated
        // TalkBeat port, fired on rising-edge onsets in the live speech-energy signal, gated
        // to the Talk pool and refused while a peer layer owns the arms.
        private SpeechBeatDetector _beatDetector;
        private TalkBeatOverlay _beatOverlay;
        private DeterministicEmbodimentRandom _beatRandom;

        // Referential gestures ("gesture at what it says"). Shares the beat overlay/port
        // (arms-only additive one-shot) but draws from its own seeded stream so a referential
        // pick never perturbs the onset-beat sequence.
        private DeterministicEmbodimentRandom _referentialRandom;

        // Proximity-scaled expressiveness. A slow-smoothed multiplier derived from the
        // distance to the conversation partner, resolved once per tick by the controller's
        // single ConversationAnchorResolver and published on LayerTickContext — folds
        // into the talk overlay weight and the beat onset weight alike.
        private float _proximityScale01 = 1f;

        public string Name => LayerName;

        public float Weight { get; private set; }

        /// <summary>Weight the controller writes to the additive moving-talk port each tick.</summary>
        public float MovingWeight { get; private set; }

        /// <summary>Weight the controller writes to the additive beat-gesture port each tick.</summary>
        public float BeatWeight => (_beatOverlay?.Weight ?? 0f) * (_runtime?.Config.BeatLayerWeight ?? 1f);

        public string StateLabel { get; private set; } = "Off";

        public string ActiveClipName => _mixer != null && _mixer.CurrentClip != null
            ? _mixer.CurrentClip.name
            : "(none)";

        public float ActiveNormalizedTime => _mixer?.CurrentNormalizedTime ?? 0f;

        internal float ActivePlaybackSpeedForTests => _mixer?.CurrentSpeedForTests ?? 0f;

        /// <summary>
        ///     True while the talk overlay is applying full-body coverage (an entry authored
        ///     <see cref="BodyCoverage.FullBody" /> while the character is stationary). Used by
        ///     the conversational gesture performer's suppression report — a stationary
        ///     full-body talk entry takes the whole skeleton just like a full-body action does.
        ///     Includes the fade-out tail (<c>_fade01 &gt; 0</c> after <c>EndTalking</c> flips
        ///     <c>_isOn</c> off): the mask still owns the pose until the envelope reaches zero,
        ///     so dropping the report at the on/off flip would let procedural posture ramp back
        ///     in against a still-fading full-body pose.
        /// </summary>
        public bool IsRunningFullBodyCoverage =>
            (_isOn || _fade01 > 0f) && _appliedCoverage == BodyCoverage.FullBody;

        /// <summary>
        ///     True while the talk overlay is applying upper-body coverage, including the
        ///     fade-out tail (see <see cref="IsRunningFullBodyCoverage" />). Deliberately based
        ///     on the envelope rather than <see cref="Weight" />, which is energy-scaled and can
        ///     dip to zero mid-speech — the suppression report must not flap during a sentence.
        /// </summary>
        public bool IsRunningUpperBodyCoverage =>
            (_isOn || _fade01 > 0f) && _appliedCoverage == BodyCoverage.UpperBody;

        public void Initialize(LayerRuntime runtime, int port)
        {
            _runtime = runtime;
            _port = port;
            _mixer = new CrossfadeMixer(runtime.Graph, runtime.Config.BlendCurve)
            {
                OverflowReported = msg => runtime.Trace?.Warning($"[TalkLayer] {msg}")
            };
            _movingMixer = new CrossfadeMixer(runtime.Graph, runtime.Config.BlendCurve)
            {
                OverflowReported = msg => runtime.Trace?.Warning($"[TalkLayer.Moving] {msg}")
            };
            _scheduler = new VariantScheduler(runtime.RandomSeed ^ 0x7A1C);
            _energyScale01 = runtime.Config.ResolveTalkLayerWeightScale(false, 0f);
            _fadeInSeconds = runtime.Config.TalkFadeInSeconds;
            _fadeOutSeconds = runtime.Config.TalkFadeOutSeconds;

            runtime.Mixer.ConnectLayer(port, _mixer.Playable, TalkUpperBodyMask());
            // The moving overlay is configured once and never re-masked or re-moded:
            // additive gesture deltas over the gait, arms and hands only (the gait keeps
            // the torso, Convai Gaze keeps the head).
            runtime.Mixer.ConnectLayer(
                LayerPorts.TalkMoving, _movingMixer.Playable, ArmsMask(), additive: true);

            // The beat overlay shares the same arms-only mask as the moving overlay — both
            // are additive deltas over whatever else the port stack is doing.
            _beatDetector = new SpeechBeatDetector();
            _beatOverlay = new TalkBeatOverlay();
            _beatOverlay.Initialize(runtime, LayerPorts.TalkBeat, ArmsMask());
            _beatRandom = new DeterministicEmbodimentRandom(unchecked((uint)(runtime.RandomSeed ^ 0x6EA7B17)));
            _referentialRandom = new DeterministicEmbodimentRandom(unchecked((uint)(runtime.RandomSeed ^ 0x51A9F3D)));
        }

        public void Tick(in LayerTickContext context)
        {
            if (context.DialogueState == DialogueState.Thinking)
                _thinkingTimer += context.DeltaTime;
            else
                _thinkingTimer = 0f;

            ResolvePool(context.DialogueState, out TalkPoolKind kind, out IReadOnlyList<TalkEntry> pool, out bool hasContent);
            bool wantsPool = kind != TalkPoolKind.None && hasContent;

            // In Suppress mode, movement holds the whole layer off through the envelope
            // (EndTalking) instead of narrowing coverage — the smoothest exit for users
            // who want gesture-free walking. Listen/Think pools are stationary conversation
            // poses and are never subject to the moving-talk suppress switch.
            bool suppressedByMoving = kind == TalkPoolKind.Talk &&
                _runtime.Config.MovingTalk == MovingTalkMode.Suppress && context.IsMoving;
            bool shouldPlay = wantsPool && !suppressedByMoving;

            if (shouldPlay)
            {
                // A fresh want to play always wins over an in-flight interruption sequence.
                _interruptedActive = false;
                _interruptedHoldRemaining = 0f;

                if (!_isOn || kind != _activeKind)
                    BeginPlayingPool(in context, kind, pool);
                else
                {
                    CancelReleaseDelayIfNeeded(in context);
                    TickVariantSwitch(in context);
                }
            }
            else if (_isOn)
            {
                // An interruption mid-gesture freezes the pose instead of a bland
                // crossfade. Only while something is actually visible — an interruption
                // arriving while the layer is still fading in/out is a no-op here.
                if (context.DialogueState == DialogueState.Interrupted && _fade01 > MaskSwapWeightEpsilon)
                    BeginInterruptedFreeze(in context);
                else if (suppressedByMoving)
                    EndTalking(in context, "suppressed while moving");
                else if (_releaseDelayRemaining > 0f)
                    TickReleaseDelay(in context);
                else
                    BeginReleaseOrEndTalking(in context);
            }
            else if (_interruptedActive)
            {
                TickInterruptedSequence(in context);
            }

            TickEnvelope(in context);
            _mixer.Tick(context.DeltaTime);
            TickTalkBracket();
            _movingMixer.Tick(context.DeltaTime);
            TickBeats(in context);
            _beatOverlay.Tick(context.DeltaTime);
        }

        public void Teardown()
        {
            if (_runtimeFullBodyMask != null)
            {
                RuntimeMaskCache.Release(_runtimeFullBodyMask);
                _runtimeFullBodyMask = null;
            }

            if (_runtimeTalkMask != null)
            {
                RuntimeMaskCache.Release(_runtimeTalkMask);
                _runtimeTalkMask = null;
            }

            if (_runtimeArmsMask != null)
            {
                RuntimeMaskCache.Release(_runtimeArmsMask);
                _runtimeArmsMask = null;
            }

            _mixer = null;
            _movingMixer = null;
            _runtime = null;
            _scheduler = null;
            _talkIndex = -1;
            _port = -1;
            _fade01 = 0f;
            _energyScale01 = 1f;
            _releaseDelayRemaining = 0f;
            _pendingApplyQueued = false;
            _pendingIndex = -1;
            _appliedCoverage = BodyCoverage.UpperBody;
            _appliedAdditive = false;
            _stationaryFactor01 = 1f;
            _movingFactor01 = 0f;
            _reportedIntensityScale01 = 1f;
            _activePool = null;
            _activeKind = TalkPoolKind.None;
            _fadeInSeconds = 0f;
            _fadeOutSeconds = 0f;
            _thinkingTimer = 0f;
            _interruptedActive = false;
            _interruptedHoldRemaining = 0f;
            _talkBracketPhase = TalkBracketPhase.None;
            _activeFragmentIndex = -1;
            _activeFragmentEnd = -1f;

            _beatOverlay?.Teardown();
            _beatOverlay = null;
            _beatDetector = null;
            _proximityScale01 = 1f;

            Weight = 0f;
            MovingWeight = 0f;
            _isOn = false;
            StateLabel = "Off";
        }

        // ------------------------------------------------------------------ test hooks

        internal BodyCoverage AppliedCoverageForTests => _appliedCoverage;
        internal bool AppliedAdditiveForTests => _appliedAdditive;
        internal bool HasPendingApplyForTests => _pendingApplyQueued;
        internal float StationaryFactorForTests => _stationaryFactor01;
        internal float MovingFactorForTests => _movingFactor01;
        internal float ReportedIntensityScaleForTests => _reportedIntensityScale01;
        internal string MovingClipNameForTests =>
            _movingMixer != null && _movingMixer.CurrentClip != null
                ? _movingMixer.CurrentClip.name
                : "(none)";
        internal string ActivePoolKindForTests => _activeKind.ToString();
        internal float FadeInSecondsForTests => _fadeInSeconds;
        internal float FadeOutSecondsForTests => _fadeOutSeconds;
        internal bool IsInterruptedHoldForTests => _interruptedActive && _interruptedHoldRemaining > 0f;
        internal bool IsInterruptedActiveForTests => _interruptedActive;
        internal string TalkBracketPhaseForTests => _talkBracketPhase.ToString();
        internal string BeatClipNameForTests => _beatOverlay?.ActiveClipNameForTests ?? "(none)";

        // Exposes the RuntimeMaskCache-backed masks so tests can assert sharing/handoff
        // behavior without duplicating the cache's internals.
        internal AvatarMask RuntimeTalkMaskForTests => _runtimeTalkMask;
        internal AvatarMask RuntimeFullBodyMaskForTests => _runtimeFullBodyMask;
        internal AvatarMask RuntimeArmsMaskForTests => _runtimeArmsMask;

        // ------------------------------------------------------------------ state changes

        /// <summary>
        ///     Resolves which pool (Talk/Listen/Think) the given dialogue state wants, and
        ///     whether that pool has any playable content. Thinking additionally
        ///     gates on <see cref="ConvaiBodyAnimationConfig.ThinkingEnterDelaySeconds" /> via
        ///     <see cref="_thinkingTimer" /> — a brief Thinking beat never twitches a clip on.
        /// </summary>
        private void ResolvePool(
            DialogueState state,
            out TalkPoolKind kind,
            out IReadOnlyList<TalkEntry> pool,
            out bool hasContent)
        {
            switch (state)
            {
                case DialogueState.Speaking:
                    kind = TalkPoolKind.Talk;
                    pool = _runtime.Set.Talks;
                    hasContent = _runtime.Set.HasAnyTalk;
                    break;

                case DialogueState.Listening:
                case DialogueState.Attending:
                    kind = TalkPoolKind.Listen;
                    pool = _runtime.Set.Listens;
                    hasContent = _runtime.Set.HasAnyListen;
                    break;

                case DialogueState.Thinking:
                    if (_thinkingTimer >= _runtime.Config.ThinkingEnterDelaySeconds)
                    {
                        kind = TalkPoolKind.Think;
                        pool = _runtime.Set.Thinks;
                        hasContent = _runtime.Set.HasAnyThink;
                    }
                    else
                    {
                        kind = TalkPoolKind.None;
                        pool = null;
                        hasContent = false;
                    }
                    break;

                default:
                    kind = TalkPoolKind.None;
                    pool = null;
                    hasContent = false;
                    break;
            }
        }

        private void BeginPlayingPool(in LayerTickContext context, TalkPoolKind kind, IReadOnlyList<TalkEntry> pool)
        {
            if (kind != _activeKind)
                _talkIndex = -1; // an index from a different pool must never be reused

            int index = _scheduler.SelectNext(pool, _talkIndex, in context.Emotion, out float weight);
            if (index < 0) return;

            _activePool = pool;
            _activeKind = kind;
            float baseFadeIn = kind == TalkPoolKind.Listen
                ? _runtime.Config.ListenFadeInSeconds
                : _runtime.Config.TalkFadeInSeconds;
            // Calmness slightly lengthens talk/listen fades (neutral/identity at Calmness = 1).
            float calmnessFadeScale = PersonaScalars.ResolveTalkFadeInScale(_runtime.Config);
            _fadeInSeconds = baseFadeIn * calmnessFadeScale;
            _fadeOutSeconds = _runtime.Config.TalkFadeOutSeconds * calmnessFadeScale;

            TalkEntry talk = pool[index];
            BodyCoverage coverage = ResolveCoverage(talk, in context);

            if (_fade01 <= 0.01f)
            {
                // Layer is invisible: mask/additive and a zero-fade clip start are free.
                // Additive entries layer the gesture delta over the base posture instead
                // of replacing it — the authored stance (lean) never takes the body over.
                ApplyCoverage(coverage);
                ApplyAdditive(talk.Additive);

                var settings = ClipPlaySettings.Default;
                // Random phase offset so two characters never talk in visible sync.
                settings.StartNormalizedTime = SelectFragmentStart(talk);

                // A fresh enter from silence plays an authored Intro Clip first (hands
                // raise into gesture space) before handing off to the main loop in
                // TickTalkBracket. Talk pool only — Listen/Think always go straight to the
                // main clip, and every other _mixer.Play(talk.Clip, ...) site below sets the
                // phase to Loop directly (a variant switch or pool-to-pool crossfade never
                // replays the intro).
                if (kind == TalkPoolKind.Talk && talk.IntroClip != null)
                {
                    _mixer.Play(talk.IntroClip, 0f, ClipPlaySettings.Default, restartIfSame: true);
                    _talkBracketPhase = TalkBracketPhase.Intro;
                }
                else
                {
                    _mixer.Play(talk.Clip, 0f, settings, restartIfSame: true);
                    _talkBracketPhase = TalkBracketPhase.Loop;
                }

                SyncMovingSlot(talk, 0f, settings.StartNormalizedTime);
                _energyScale01 = _runtime.Config.ResolveTalkLayerWeightScale(
                    context.HasSpeechEnergy, context.SpeechEnergy);
            }
            else if (coverage == _appliedCoverage && talk.Additive == _appliedAdditive)
            {
                // Back-to-back turns (or a same-mask pool switch, e.g. Listening -> Speaking)
                // on a compatible mask/mode: crossfade at the current weight — a zero-fade
                // restart at a new random phase would pop the pose, and waiting for a full
                // release would make Speaking entries feel late. Never replays the intro: the
                // layer is already visible, so hands are already up.
                var settings = ClipPlaySettings.Default;
                settings.StartNormalizedTime = SelectFragmentStart(talk);
                _mixer.Play(talk.Clip, _runtime.Config.TalkVariantCrossfadeSeconds, settings, restartIfSame: true);
                SyncMovingSlot(talk, _runtime.Config.TalkVariantCrossfadeSeconds, settings.StartNormalizedTime);
                _talkBracketPhase = TalkBracketPhase.Loop;
            }
            else
            {
                // Mask/additive must change while the layer is still visible — masks cut,
                // they never blend. Queue everything for the envelope trough.
                QueuePendingApply(index, coverage, talk.Additive);
            }

            _talkIndex = index;
            _previousClipPhase = 0f;
            _releaseDelayRemaining = 0f;
            _isOn = true;
            StateLabel = "Talking";

            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Talk variant roll: pool={kind} index={index} weight={weight:F2} coverage={coverage} " +
                    $"emotion={context.Emotion.DominantLabel}");
            _runtime.ReportTransition(
                LayerName, "Off", "Talking", talk.Clip.name,
                _fadeInSeconds, $"dialogue={context.DialogueState}");
        }

        /// <summary>
        ///     The character was interrupted mid-gesture while the talk layer was visibly
        ///     playing. Freezes the current pose (rather than crossfading it away) so the beat
        ///     reads as a genuine interruption, held for
        ///     <see cref="ConvaiBodyAnimationConfig.InterruptedFreezeSeconds" /> before
        ///     <see cref="TickInterruptedSequence" /> starts the faster settle-out.
        /// </summary>
        private void BeginInterruptedFreeze(in LayerTickContext context)
        {
            string from = StateLabel;
            _mixer.FreezeAll();
            _movingMixer.FreezeAll();

            _interruptedActive = true;
            _interruptedHoldRemaining = _runtime.Config.InterruptedFreezeSeconds;
            _pendingApplyQueued = false; // a mid-swap mask change must not fire into a frozen pose
            _releaseDelayRemaining = 0f;
            _isOn = false;
            StateLabel = "InterruptedHold";

            _runtime.ReportTransition(
                LayerName, from, "InterruptedHold", ActiveClipName,
                0f, $"dialogue={context.DialogueState}");
        }

        /// <summary>
        ///     Ticks the post-freeze hold, then the faster interrupted release, then restores
        ///     clean state (normal fade-out duration, not interrupted) so the layer is ready
        ///     for the next Speaking entry with no stuck frozen clip speeds — the next
        ///     <see cref="BeginPlayingPool" /> always starts a brand-new clip source (never
        ///     reuses a frozen one), so recovery is automatic once this flag clears.
        /// </summary>
        private void TickInterruptedSequence(in LayerTickContext context)
        {
            if (_interruptedHoldRemaining > 0f)
            {
                _interruptedHoldRemaining = Mathf.Max(0f, _interruptedHoldRemaining - context.DeltaTime);
                if (_interruptedHoldRemaining <= HoldTimerEpsilon)
                {
                    // Clear the float dust outright: TickEnvelope's own holdingInterrupted
                    // check (and IsInterruptedHoldForTests) read this same field later in the
                    // same tick and must see the hold as fully elapsed, not a hair above zero.
                    _interruptedHoldRemaining = 0f;

                    // Hold elapsed: settle out faster than a normal fade-out.
                    _fadeOutSeconds = Mathf.Max(
                        0.01f, _runtime.Config.TalkFadeOutSeconds * _runtime.Config.InterruptedReleaseScale);
                    StateLabel = "FadingOut";
                    _runtime.ReportTransition(
                        LayerName, "InterruptedHold", "Off", ActiveClipName,
                        _fadeOutSeconds, $"interrupted fast release; dialogue={context.DialogueState}");
                }
            }
            else if (_fade01 <= MaskSwapWeightEpsilon)
            {
                // Fast release finished: clean state, ready for the next Speaking entry.
                // Same tolerance TickEnvelope itself uses for "effectively invisible" — this
                // check runs a tick behind TickEnvelope's own update (it fires before
                // TickEnvelope in the per-frame order), so an exact-zero comparison would
                // strand the flag one extra tick past when the weight actually reads as zero.
                _interruptedActive = false;
                _fadeOutSeconds = _runtime.Config.TalkFadeOutSeconds;
                _talkBracketPhase = TalkBracketPhase.None;
                StateLabel = "Off";
            }
        }

        private void BeginReleaseOrEndTalking(in LayerTickContext context)
        {
            float delay = _runtime.Config.TalkReleaseDelaySeconds;
            if (delay <= 0f)
            {
                EndTalking(in context, "dialogue ended");
                return;
            }

            // Speech has ended: do not let an arbitrary source-clip phase continue lifting
            // the hands during the release grace period. A small amount of slowed motion
            // avoids a visible stop, then EndTalking freezes the live pose for its dissolve.
            float releaseSpeed = _runtime.Config.TalkReleasePlaybackSpeed;
            _mixer.SetCurrentSpeed(releaseSpeed);
            _movingMixer.SetCurrentSpeed(releaseSpeed);
            _beatOverlay.Release();
            _releaseDelayRemaining = delay;
            StateLabel = "ReleaseHold";
            _runtime.ReportTransition(
                LayerName, "Talking", "ReleaseHold", ActiveClipName,
                delay, $"dialogue={context.DialogueState}");
        }

        private void TickReleaseDelay(in LayerTickContext context)
        {
            _releaseDelayRemaining = Mathf.Max(0f, _releaseDelayRemaining - context.DeltaTime);
            TalkEntry current = CurrentEntry;
            bool safeWindowReached = current != null && current.UseSafeReleaseWindow &&
                                     current.IsSafeReleaseTime(_mixer.CurrentNormalizedTime);
            if (safeWindowReached || _releaseDelayRemaining <= 0f)
                EndTalking(in context, safeWindowReached ? "safe release window" : "release delay elapsed");
        }

        private void CancelReleaseDelayIfNeeded(in LayerTickContext context)
        {
            if (_releaseDelayRemaining <= 0f) return;

            _releaseDelayRemaining = 0f;
            _mixer.SetCurrentSpeed(1f);
            _movingMixer.SetCurrentSpeed(1f);
            StateLabel = "Talking";
            _runtime.ReportTransition(
                LayerName, "ReleaseHold", "Talking", ActiveClipName,
                0f, $"dialogue={context.DialogueState}");
        }

        private void EndTalking(in LayerTickContext context, string reason)
        {
            string from = StateLabel;
            _releaseDelayRemaining = 0f;
            _isOn = false;
            StateLabel = "FadingOut";
            // A normal (non-interrupted) release always uses the canonical fade-out — never a
            // scaled value left over from a prior interrupted release. Calmness lengthens
            // it slightly (identity at Calmness = 1); the outro cap below is applied AFTER this
            // scaling so TalkOutroMaxSeconds remains a hard cap regardless of Calmness.
            _fadeOutSeconds = _runtime.Config.TalkFadeOutSeconds * PersonaScalars.ResolveTalkFadeInScale(_runtime.Config);

            // An authored Outro Clip plays once as talk ends — hands settle back down
            // instead of vanishing purely via weight fade. The added latency is capped: the
            // release fade-out itself is shortened to TalkOutroMaxSeconds rather than waiting
            // for the (possibly longer) authored clip to finish. Talk pool only; Listen/Think
            // releases and the moving additive overlay are unaffected.
            TalkEntry current = CurrentEntry;
            if (_activeKind == TalkPoolKind.Talk && current != null && current.OutroClip != null)
            {
                float cap = Mathf.Max(0.01f, _runtime.Config.TalkOutroMaxSeconds);
                float requiredRate = current.OutroClip.length / cap;
                float playbackRate = Mathf.Clamp(
                    requiredRate, current.OutroMinPlaybackRate, current.OutroMaxPlaybackRate);
                _fadeOutSeconds = Mathf.Min(cap, current.OutroClip.length / playbackRate);
                var settings = ClipPlaySettings.Default;
                settings.Speed = playbackRate;
                _mixer.Play(
                    current.OutroClip, _runtime.Config.ActionChainCrossfadeSeconds,
                    settings, restartIfSame: true);
                _talkBracketPhase = TalkBracketPhase.Outro;
            }
            else
            {
                // Dissolve from the exact live pose. Continuing a looping clip during the
                // fade can complete an upward hand stroke after the voice has already ended.
                _mixer.FreezeAll();
                _movingMixer.FreezeAll();
                _beatOverlay.Release();
                _talkBracketPhase = TalkBracketPhase.None;
            }

            _runtime.ReportTransition(
                LayerName, from, "Off", ActiveClipName,
                _fadeOutSeconds, $"{reason}; dialogue={context.DialogueState}");
        }

        private void TickVariantSwitch(in LayerTickContext context)
        {
            // The set asset can be live-edited in Play Mode; a shrunk pool must not throw.
            if (_activePool == null || _talkIndex < 0 || _talkIndex >= _activePool.Count)
            {
                _talkIndex = -1;
                return;
            }

            // While moving, force upper-body coverage even if the entry asked for full
            // body. The swap is deferred to the envelope trough (masks cut, not blend);
            // when the stationary port is already invisible (e.g. the moving overlay has
            // taken over), the swap is free and applies immediately.
            BodyCoverage wanted = ResolveCoverage(_activePool[_talkIndex], in context);
            if (wanted != _appliedCoverage && !_pendingApplyQueued)
            {
                if (Weight <= MaskSwapWeightEpsilon)
                    ApplyCoverage(wanted);
                else
                    QueuePendingApply(-1, wanted, _appliedAdditive);
            }

            if (!_runtime.Config.SwitchTalkVariantOnLoop) return;

            float phase = _mixer.CurrentNormalizedTime;
            if (_activeKind == TalkPoolKind.Talk && _activeFragmentEnd > 0f &&
                phase >= _activeFragmentEnd && _previousClipPhase < _activeFragmentEnd)
            {
                TalkEntry current = CurrentEntry;
                var fragmentSettings = ClipPlaySettings.Default;
                fragmentSettings.StartNormalizedTime = SelectFragmentStart(current, _activeFragmentIndex);
                float fragmentFade = _runtime.Config.TalkVariantCrossfadeSeconds;
                _mixer.Play(current.Clip, fragmentFade, fragmentSettings, restartIfSame: true);
                SyncMovingSlot(current, fragmentFade, fragmentSettings.StartNormalizedTime);
                _previousClipPhase = fragmentSettings.StartNormalizedTime;
                if (_runtime.Trace.IsDetail)
                    _runtime.Trace.Detail($"Talk motion phrase switch: fragment={_activeFragmentIndex} start={fragmentSettings.StartNormalizedTime:F2} end={_activeFragmentEnd:F2}.");
                return;
            }
            bool wrapped = phase < _previousClipPhase;
            _previousClipPhase = phase;
            if (!wrapped) return;

            // Gesture Liveliness scales how often a loop-wrap actually swaps the variant.
            // At liveliness >= 1 the probability is 1 — always switch, today's behavior — and
            // no random number is drawn, so a default-config character's variant sequence is
            // byte-identical to before this feature existed.
            float switchProbability = PersonaScalars.ResolveVariantSwitchProbability(_runtime.Config);
            if (switchProbability < 1f && _scheduler.NextInterval(0f, 1f) > switchProbability) return;

            int next = _scheduler.SelectNext(_activePool, _talkIndex, in context.Emotion, out float weight);
            if (next < 0 || next == _talkIndex) return;

            TalkEntry talk = _activePool[next];
            BodyCoverage nextCoverage = ResolveCoverage(talk, in context);
            if (nextCoverage == _appliedCoverage && talk.Additive == _appliedAdditive && !_pendingApplyQueued)
            {
                float fade = _runtime.Config.TalkVariantCrossfadeSeconds;
                var settings = ClipPlaySettings.Default;
                settings.StartNormalizedTime = SelectFragmentStart(talk);
                _mixer.Play(talk.Clip, fade, settings, restartIfSame: true);
                SyncMovingSlot(talk, fade, settings.StartNormalizedTime);
                _talkBracketPhase = TalkBracketPhase.Loop;
                _runtime.ReportTransition(
                    LayerName, "Talking", "Talking", talk.Clip.name, fade, "variant switch on loop");
            }
            else
            {
                // The next variant needs a different mask/mode — swap it at the trough.
                QueuePendingApply(next, nextCoverage, talk.Additive);
            }

            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail($"Talk variant switch on loop: index={next} weight={weight:F2}");
            _talkIndex = next;
            _previousClipPhase = 0f;
        }

        /// <summary>
        ///     Hands the stationary mixer off from an authored Intro Clip to the entry's
        ///     main loop clip once the intro finishes playing. No-op whenever no intro is in
        ///     flight (<see cref="_talkBracketPhase" /> not <see cref="TalkBracketPhase.Intro" />)
        ///     — the default, unauthored path never calls into the crossfade below. Also a
        ///     no-op while an interruption freeze/fast-release owns the mixer: a frozen intro
        ///     clip (rate 0) must stay frozen, never silently hand off to the loop mid-freeze.
        /// </summary>
        /// <remarks>
        ///     Safety net for a user-assigned LOOPING intro clip: <see cref="CrossfadeMixer.IsCurrentClipFinished" />
        ///     is unconditionally false for a looping clip (it only clears a non-looping clip
        ///     that has played out), so a looping intro would otherwise wedge the bracket in
        ///     <see cref="TalkBracketPhase.Intro" /> forever. <see cref="CrossfadeMixer.CurrentTime" />
        ///     is a raw, unwrapped running clock (unlike the wrapped normalized time), so
        ///     comparing it directly against the authored clip length — minus the upcoming
        ///     handoff crossfade — forces the handoff once the intro has visibly played through
        ///     once, regardless of its loop flag. Zero-alloc: reuses the mixer's existing clock,
        ///     no extra state.
        /// </remarks>
        private void TickTalkBracket()
        {
            if (_talkBracketPhase != TalkBracketPhase.Intro) return;
            if (_interruptedActive) return;

            TalkEntry current = CurrentEntry;
            if (current == null || current.Clip == null)
            {
                _talkBracketPhase = TalkBracketPhase.None;
                return;
            }

            float chainFade = _runtime.Config.ActionChainCrossfadeSeconds;
            AnimationClip introClip = current.IntroClip;
            bool elapsedPastIntroLength = introClip != null && introClip.length > 0f &&
                _mixer.CurrentTime >= Mathf.Max(0f, introClip.length - chainFade);

            if (!_mixer.IsCurrentClipFinished && !elapsedPastIntroLength) return;

            var settings = ClipPlaySettings.Default;
            settings.StartNormalizedTime = SelectFragmentStart(current);
            _mixer.Play(current.Clip, chainFade, settings, restartIfSame: true);
            _talkBracketPhase = TalkBracketPhase.Loop;
            _previousClipPhase = 0f;

            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail($"Talk intro finished, handing off to loop clip '{current.Clip.name}'.");
        }

        // ------------------------------------------------------------------ weight envelope

        private void TickEnvelope(in LayerTickContext context)
        {
            ConvaiBodyAnimationConfig config = _runtime.Config;

            // An interrupted freeze hold owns the envelope while it's active: the pose is
            // frozen, not fading, so the weight must not move until the hold elapses.
            bool holdingInterrupted = _interruptedActive && _interruptedHoldRemaining > 0f;

            // A queued mask/mode swap owns the envelope: dip to the trough first, apply
            // there, then the normal target (playing or not) takes over again.
            float rate;
            if (holdingInterrupted)
                rate = 0f;
            else
                rate = _isOn && !_pendingApplyQueued
                    ? context.DeltaTime / Mathf.Max(0.01f, _fadeInSeconds)
                    : -context.DeltaTime / Mathf.Max(0.01f, _fadeOutSeconds);
            _fade01 = Mathf.Clamp01(_fade01 + rate);

            if (_pendingApplyQueued && _fade01 <= MaskSwapWeightEpsilon)
                ApplyPendingAtTrough();

            float energyTarget = config.ResolveTalkLayerWeightScale(
                context.HasSpeechEnergy && _isOn, context.SpeechEnergy);

            // The energy reading is an ~80 ms RMS window and moves at syllable rate;
            // applied raw it pumps the layer weight visibly. Fast attack / slow release
            // lets gestures swell with speech instead of twitching.
            float tau = energyTarget > _energyScale01 ? 0.15f : 0.45f;
            _energyScale01 = Mathf.Lerp(
                _energyScale01, energyTarget, 1f - Mathf.Exp(-context.DeltaTime / tau));

            TickSlotFactors(in context, config);

            // Emotion-driven intensity: smoothed toward this tick's reported scale
            // so a peer module's emotion read visibly scales the talk overlay without a pop.
            _reportedIntensityScale01 = Mathf.Lerp(
                _reportedIntensityScale01, context.ConversationalIntensityScale,
                1f - Mathf.Exp(-context.DeltaTime / ReportedIntensityScaleTauSeconds));

            TickProximity(in context, config);

            // Gesture Liveliness folds in alongside the proximity multiplier, at the same
            // place, composably. Neutral (1) at liveliness = 1, so a default-config character's
            // weight is unchanged; the final clamp guards against liveliness > 1 ever pushing
            // the weight above the same 0..1 range it was always implicitly held to.
            float livelinessScale = PersonaScalars.ResolveGestureWeightScale(config);
            float envelope = Mathf.Clamp01(config.BlendCurve.Evaluate(_fade01)) * _energyScale01;
            Weight = Mathf.Clamp01(
                envelope * _stationaryFactor01 * _reportedIntensityScale01 * _proximityScale01 * livelinessScale);
            MovingWeight = Mathf.Clamp01(
                envelope * _movingFactor01 * _reportedIntensityScale01 * _proximityScale01 * livelinessScale);

            if (!_isOn && StateLabel == "FadingOut" && _fade01 <= 0f)
                StateLabel = "Off";
        }

        /// <summary>
        ///     Smooths <see cref="_proximityScale01" /> toward the two-point distance
        ///     mapping so walking toward the character never visibly pumps gesture size. Off
        ///     (or no resolvable conversation anchor) holds the multiplier at exactly 1 — zero
        ///     behavior change from before this feature existed.
        /// </summary>
        private void TickProximity(in LayerTickContext context, ConvaiBodyAnimationConfig config)
        {
            if (!config.ProximityExpressiveness)
            {
                _proximityScale01 = 1f;
                return;
            }

            float target = 1f;
            if (TryResolveConversationDistance(in context, out float distance))
            {
                target = ProximityExpressivenessSolver.ComputeTargetMultiplier(
                    distance,
                    config.ProximityNearDistance, config.ProximityNearScale,
                    config.ProximityFarDistance, config.ProximityFarScale,
                    ConvaiBodyAnimationConfig.ProximityMultiplierMin,
                    ConvaiBodyAnimationConfig.ProximityMultiplierMax);
            }

            float tau = Mathf.Max(0.05f, config.ProximitySmoothingSeconds);
            _proximityScale01 = Mathf.Lerp(
                _proximityScale01, target, 1f - Mathf.Exp(-context.DeltaTime / tau));
        }

        /// <summary>
        ///     Resolves the distance between the character root and the conversation anchor the
        ///     controller published on <see cref="LayerTickContext" /> this tick (the
        ///     controller's single <c>ConversationAnchorResolver</c> replaces this layer's own
        ///     camera lookup). Returns <c>false</c> (degrading the multiplier to neutral) when no
        ///     anchor is resolvable; the resolver itself owns the once-only degradation log.
        /// </summary>
        private bool TryResolveConversationDistance(in LayerTickContext context, out float distance)
        {
            distance = 0f;
            if (_runtime.CharacterRoot == null || !context.HasConversationAnchor) return false;

            distance = Vector3.Distance(_runtime.CharacterRoot.position, context.ConversationAnchor);
            return true;
        }

        /// <summary>
        ///     Crossfades the envelope's split between the stationary override port and the
        ///     additive moving port. Tier resolution per current entry: additive content →
        ///     the moving overlay takes over while walking; none → the override softens so
        ///     the gait's arm swing bleeds through; Suppress → the envelope itself handles
        ///     it (EndTalking), factors stay stationary.
        /// </summary>
        private void TickSlotFactors(in LayerTickContext context, ConvaiBodyAnimationConfig config)
        {
            // While releasing (speech over), the envelope owns the exit: never crossfade
            // the slot split under a fading pose — the stationary override would flash
            // back in during a walking fade-out. Reset to the stationary default once
            // nothing is visible so the next speech starts from a clean split.
            if (!_isOn)
            {
                if (_fade01 <= MaskSwapWeightEpsilon)
                {
                    _stationaryFactor01 = 1f;
                    _movingFactor01 = 0f;
                }
                return;
            }

            MovingTalkMode mode = config.MovingTalk;
            TalkEntry current = CurrentEntry;
            // Listen/Think pools are stationary conversation poses — the
            // walk-and-talk additive overlay is Speaking-only.
            bool movingSpeech = _activeKind == TalkPoolKind.Talk &&
                                 context.IsMoving && mode != MovingTalkMode.Suppress;
            bool tierA = movingSpeech &&
                         mode == MovingTalkMode.Auto &&
                         current != null &&
                         current.ResolveMovingClip() != null;

            float stationaryTarget;
            float movingTarget;
            if (tierA)
            {
                stationaryTarget = 0f;
                movingTarget = config.MovingTalkWeight;
            }
            else if (movingSpeech)
            {
                stationaryTarget = config.MovingTalkOverrideWeight;
                movingTarget = 0f;
            }
            else
            {
                stationaryTarget = 1f;
                movingTarget = 0f;
            }

            if (_fade01 <= MaskSwapWeightEpsilon)
            {
                // Nothing is visible — the slot split snaps for free (e.g. speech that
                // starts mid-walk opens directly on the moving overlay).
                _stationaryFactor01 = stationaryTarget;
                _movingFactor01 = movingTarget;
                return;
            }

            float step = context.DeltaTime / config.MovingTalkBlendSeconds;
            _stationaryFactor01 = Mathf.MoveTowards(_stationaryFactor01, stationaryTarget, step);
            _movingFactor01 = Mathf.MoveTowards(_movingFactor01, movingTarget, step);
        }

        private TalkEntry CurrentEntry =>
            _activePool != null && _talkIndex >= 0 && _talkIndex < _activePool.Count
                ? _activePool[_talkIndex]
                : null;

        /// <summary>
        ///     Keeps the moving (additive) slot playing the current entry's additive twin in
        ///     step with the stationary slot. Entries without additive content leave the old
        ///     moving clip in place — its factor is fading to zero anyway.
        /// </summary>
        private void SyncMovingSlot(TalkEntry talk, float fadeSeconds, float startNormalizedTime)
        {
            AnimationClip movingClip = talk.ResolveMovingClip();
            if (movingClip == null) return;

            var settings = ClipPlaySettings.Default;
            settings.StartNormalizedTime = startNormalizedTime;
            _movingMixer.Play(movingClip, fadeSeconds, settings, restartIfSame: true);
        }

        private void QueuePendingApply(int index, BodyCoverage coverage, bool additive)
        {
            _pendingApplyQueued = true;
            _pendingIndex = index;
            _pendingCoverage = coverage;
            _pendingAdditive = additive;
        }

        private void ApplyPendingAtTrough()
        {
            _pendingApplyQueued = false;
            ApplyCoverage(_pendingCoverage);
            ApplyAdditive(_pendingAdditive);

            if (_activePool != null && _pendingIndex >= 0 && _pendingIndex < _activePool.Count)
            {
                TalkEntry talk = _activePool[_pendingIndex];
                var settings = ClipPlaySettings.Default;
                settings.StartNormalizedTime = SelectFragmentStart(talk);
                _mixer.Play(talk.Clip, 0f, settings, restartIfSame: true);
                SyncMovingSlot(talk, 0f, settings.StartNormalizedTime);
                _previousClipPhase = 0f;
                _talkBracketPhase = TalkBracketPhase.Loop;
            }

            _pendingIndex = -1;
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Talk mask/mode swap applied at envelope trough: coverage={_appliedCoverage}, " +
                    $"additive={_appliedAdditive}.");
        }

        private void ApplyAdditive(bool additive)
        {
            _appliedAdditive = additive;
            _runtime.Mixer.SetLayerAdditive(_port, additive);
        }

        private float SelectFragmentStart(TalkEntry entry, int excludedIndex = -1)
        {
            _activeFragmentIndex = -1;
            _activeFragmentEnd = -1f;
            if (entry == null || !entry.HasFragments)
                return _scheduler.NextInterval(0f, 0.9f);

            IReadOnlyList<TalkMotionFragment> fragments = entry.Fragments;
            float total = 0f;
            int validCount = 0;
            for (int i = 0; i < fragments.Count; i++)
            {
                TalkMotionFragment fragment = fragments[i];
                if (fragment == null || !fragment.IsValid || (i == excludedIndex && fragments.Count > 1)) continue;
                total += fragment.Weight;
                validCount++;
            }
            if (validCount == 0) return _scheduler.NextInterval(0f, 0.9f);

            float roll = _scheduler.NextInterval(0f, total);
            for (int i = 0; i < fragments.Count; i++)
            {
                TalkMotionFragment fragment = fragments[i];
                if (fragment == null || !fragment.IsValid || (i == excludedIndex && fragments.Count > 1)) continue;
                roll -= fragment.Weight;
                if (roll > 0f) continue;
                _activeFragmentIndex = i;
                _activeFragmentEnd = fragment.EndNormalized;
                return fragment.StartNormalized;
            }
            return _scheduler.NextInterval(0f, 0.9f);
        }

        // ------------------------------------------------------------------ beat gestures

        /// <summary>
        ///     Drives the onset detector while the layer is eligible for beats (in the Talk
        ///     pool, actually visible, not frozen by an interruption, and not suppressed by a
        ///     peer layer owning the arms) and fires a beat one-shot on every onset. Ineligible
        ///     ticks reset the detector so a resumed speech turn always starts from a clean
        ///     baseline instead of one caught up to an unrelated stretch of energy.
        /// </summary>
        private void TickBeats(in LayerTickContext context)
        {
            ConvaiBodyAnimationConfig config = _runtime.Config;
            if (!config.EnableBeatGestures)
            {
                _beatDetector.Reset();
                return;
            }

            bool eligible = _isOn && _activeKind == TalkPoolKind.Talk &&
                            !_interruptedActive && !context.BeatSuppressedByPeers;
            if (!eligible)
            {
                _beatDetector.Reset();
                return;
            }

            float energy = context.HasSpeechEnergy ? context.SpeechEnergy : 0f;
            // Gesture Liveliness scales the beat rate inversely via the refractory window
            // (identity at liveliness = 1).
            float refractorySeconds = PersonaScalars.ResolveBeatRefractorySeconds(config, config.BeatRefractorySeconds);
            if (!_beatDetector.Tick(energy, context.DeltaTime, refractorySeconds, out float strength))
                return;

            if (!TryResolveBeatEntry(out ActionEntry entry)) return;

            float weight = Mathf.Clamp01(strength) * config.BeatWeightScale * _proximityScale01;
            _beatOverlay.Play(entry.Clip, weight);
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Beat gesture: '{entry.ActionName}' strength={strength:F2} weight={weight:F2}");
        }

        /// <summary>
        ///     Picks one action tagged <see cref="GestureCueKind.Beat" /> or
        ///     <see cref="GestureCueKind.Emphatic" /> from the set, deterministically at random
        ///     among ties (module-local seeded stream, no LINQ/alloc). Returns <c>false</c> —
        ///     silently, no log spam — when the set has no such content, which is the default
        ///     for every shipped/existing asset.
        /// </summary>
        private bool TryResolveBeatEntry(out ActionEntry entry)
        {
            entry = null;
            IReadOnlyList<ActionEntry> actions = _runtime.Set.Actions;
            if (actions == null || actions.Count == 0) return false;

            int matchCount = 0;
            int firstMatchIndex = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || !IsBeatCue(candidate.Cue)) continue;

                if (firstMatchIndex < 0) firstMatchIndex = i;
                matchCount++;
            }

            if (matchCount == 0) return false;
            if (matchCount == 1)
            {
                entry = actions[firstMatchIndex];
                return true;
            }

            int roll = Mathf.Clamp(Mathf.FloorToInt(_beatRandom.Range(0f, matchCount)), 0, matchCount - 1);
            int seen = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || !IsBeatCue(candidate.Cue)) continue;

                if (seen == roll)
                {
                    entry = candidate;
                    return true;
                }
                seen++;
            }

            return false;
        }

        private static bool IsBeatCue(GestureCueKind cue) =>
            cue is GestureCueKind.Beat or GestureCueKind.Emphatic;

        /// <summary>
        ///     Plays a beat gesture from an explicit <see cref="GestureCue" /> (Kind Beat or
        ///     Emphatic; <see cref="GestureCue.Intensity" /> maps directly to weight before the
        ///     config scale/proximity are applied) rather than the onset detector — the seam a
        ///     future backend-driven emphasis feed would call through. Not currently wired to
        ///     any public entry point; the onset detector (<see cref="TickBeats" />) is the only
        ///     caller today. Requires the same Talk-pool eligibility as an onset-fired beat, but
        ///     — being an explicit request — does not re-check peer-layer arm ownership.
        /// </summary>
        internal bool TryPlayBeat(in GestureCue cue)
        {
            if (cue.Kind != GestureCueKind.Beat && cue.Kind != GestureCueKind.Emphatic) return false;
            if (_beatOverlay == null || _runtime == null) return false;
            if (!_runtime.Config.EnableBeatGestures) return false;
            if (!_isOn || _activeKind != TalkPoolKind.Talk || _interruptedActive) return false;
            if (!_runtime.Set.TryGetActionForCue(cue.Kind, out ActionEntry entry)) return false;

            float weight = Mathf.Clamp01(cue.Intensity) * _runtime.Config.BeatWeightScale * _proximityScale01;
            _beatOverlay.Play(entry.Clip, weight);
            return true;
        }

        // ------------------------------------------------------------------ referential gestures

        /// <summary>
        ///     Plays a referential gesture ("gesture at what it says") tagged
        ///     <paramref name="kind" /> (<see cref="GestureCueKind.PalmToPlayer" />,
        ///     <see cref="GestureCueKind.HandToChest" />, <see cref="GestureCueKind.IndicateObject" />,
        ///     or <see cref="GestureCueKind.Enumerate" />) through the same additive
        ///     <see cref="TalkBeatOverlay" />/port as onset-driven beats. Requires the same
        ///     Talk-pool eligibility as an onset-fired beat (Speaking, not frozen by an
        ///     interruption); <paramref name="suppressedByPeers" /> is supplied by the caller
        ///     (an action/pointing arm-ownership check) because the referential-gesture
        ///     director fires off-tick from a transcript event and cannot read this tick's
        ///     <see cref="LayerTickContext.BeatSuppressedByPeers" /> itself. Silently refuses
        ///     (no log spam) when the feature is off or the set has no entry tagged with a
        ///     matching cue — the default for every shipped/existing asset (inert
        ///     until content exists).
        /// </summary>
        internal bool TryPlayReferentialGesture(GestureCueKind kind, bool suppressedByPeers)
        {
            if (_beatOverlay == null || _runtime == null) return false;
            if (!_runtime.Config.EnableReferentialGestures) return false;
            if (!_isOn || _activeKind != TalkPoolKind.Talk || _interruptedActive) return false;
            if (suppressedByPeers) return false;
            if (!TryResolveEntryForSingleCue(kind, ref _referentialRandom, out ActionEntry entry)) return false;

            // No Clamp01 here: the config accessor already clamps to its documented 0..1.5
            // range, and Clamp01 belongs on strength/normalized inputs only (see TickBeats'
            // `Mathf.Clamp01(strength)`) — clamping the config value itself would silently
            // discard Inspector values above 1. The overlay's own Play() applies the final
            // 0..1 safety-net clamp, exactly as it does for the beat path.
            float weight = _runtime.Config.ReferentialGestureWeight * _proximityScale01;
            _beatOverlay.Play(entry.Clip, weight);
            if (_runtime.Trace.IsDetail)
                _runtime.Trace.Detail(
                    $"Referential gesture: kind={kind} entry='{entry.ActionName}' weight={weight:F2}");
            return true;
        }

        /// <summary>
        ///     Whether the set authors any playable action tagged exactly <paramref name="kind" />.
        ///     The referential-gesture director needs this to tell two very different refusals
        ///     apart: "this set has no clip for that cue" — which should be handed to a peer
        ///     performer so the gesture still happens — versus "the layer is not in a state to
        ///     play it right now", which must not be handed anywhere.
        /// </summary>
        internal bool HasContentForCue(GestureCueKind kind)
        {
            if (_runtime?.Set == null) return false;

            IReadOnlyList<ActionEntry> actions = _runtime.Set.Actions;
            if (actions == null) return false;

            for (int i = 0; i < actions.Count; i++)
                if (actions[i] != null && actions[i].IsValid && actions[i].Cue == kind) return true;
            return false;
        }

        /// <summary>
        ///     Picks one action tagged exactly <paramref name="kind" /> from the set,
        ///     deterministically at random among ties (own seeded stream, no LINQ/alloc).
        ///     Sibling of <see cref="TryResolveBeatEntry" /> rather than a generalization of
        ///     it: beats match a two-kind union (Beat OR Emphatic) while each referential class
        ///     matches exactly one kind. Returns <c>false</c> — silently — when the set has no
        ///     such content, which is the default for every shipped/existing asset (
        ///     inert until content exists).
        /// </summary>
        private bool TryResolveEntryForSingleCue(
            GestureCueKind kind, ref DeterministicEmbodimentRandom random, out ActionEntry entry)
        {
            entry = null;
            IReadOnlyList<ActionEntry> actions = _runtime.Set.Actions;
            if (actions == null || actions.Count == 0) return false;

            int matchCount = 0;
            int firstMatchIndex = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || candidate.Cue != kind) continue;

                if (firstMatchIndex < 0) firstMatchIndex = i;
                matchCount++;
            }

            if (matchCount == 0) return false;
            if (matchCount == 1)
            {
                entry = actions[firstMatchIndex];
                return true;
            }

            int roll = Mathf.Clamp(Mathf.FloorToInt(random.Range(0f, matchCount)), 0, matchCount - 1);
            int seen = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || candidate.Cue != kind) continue;

                if (seen == roll)
                {
                    entry = candidate;
                    return true;
                }
                seen++;
            }

            return false;
        }

        // ------------------------------------------------------------------ masking

        private BodyCoverage ResolveCoverage(TalkEntry entry, in LayerTickContext context)
        {
            if (entry == null) return BodyCoverage.UpperBody;
            return context.IsMoving ? BodyCoverage.UpperBody : entry.Coverage;
        }

        private void ApplyCoverage(BodyCoverage coverage)
        {
            _appliedCoverage = coverage;

            AvatarMask mask = coverage == BodyCoverage.FullBody
                ? FullBodyMask()
                : TalkUpperBodyMask();

            if (mask != null)
                _runtime.Mixer.SetLayerMask(_port, mask);
        }

        /// <summary>
        ///     The set's upper-body mask with the spine (Body part) removed. The talk overlay
        ///     must never take the torso over: an authored stance in the mocap clip (forward
        ///     lean) would replace the idle posture wholesale. Gestures live in shoulders,
        ///     arms, hands, and head; the torso keeps the base layer's life.
        /// </summary>
        private AvatarMask TalkUpperBodyMask()
        {
            AvatarMask source = ResolveUpperBodyMask();
            if (source == null) return null;

            if (_runtimeTalkMask == null)
                _runtimeTalkMask = RuntimeMaskCache.AcquireTalkUpperBody(source);

            return _runtimeTalkMask;
        }

        private AvatarMask ResolveUpperBodyMask()
        {
            AvatarMask mask = _runtime.Set.UpperBodyMask;
            if (mask == null && !_maskWarningLogged)
            {
                _maskWarningLogged = true;
                _runtime.Trace.Warning(
                    "Animation set has no Upper Body Mask — the talk layer drives the FULL body, " +
                    "which will fight locomotion. Assign a mask in the set asset.");
            }

            return mask;
        }

        private AvatarMask FullBodyMask()
        {
            // A default-constructed AvatarMask has every humanoid part enabled.
            if (_runtimeFullBodyMask == null)
                _runtimeFullBodyMask = RuntimeMaskCache.AcquireFullBody();
            return _runtimeFullBodyMask;
        }

        /// <summary>
        ///     Arms only (shoulder → wrist), for the additive moving overlay. The gait keeps
        ///     the legs, torso, root, and FINGERS; the gaze module keeps the head. Fingers
        ///     are deliberately excluded: self-referenced additive deltas (a gesture minus
        ///     its own first frame) stack finger curl on top of the walk cycle's finger pose
        ///     and read as clenched fists — the wrists carry the gesture, the base keeps the
        ///     natural hand.
        /// </summary>
        private AvatarMask ArmsMask()
        {
            if (_runtimeArmsMask == null)
                _runtimeArmsMask = RuntimeMaskCache.AcquireArms();
            return _runtimeArmsMask;
        }
    }
}
