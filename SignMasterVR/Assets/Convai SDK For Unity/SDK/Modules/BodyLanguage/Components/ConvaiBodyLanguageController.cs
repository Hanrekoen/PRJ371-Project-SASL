using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.EventSystem;
using Convai.Modules.BodyLanguage.Core.Behaviors;
using Convai.Modules.BodyLanguage.Core.Diagnostics;
using Convai.Modules.BodyLanguage.Core.Gestures;
using Convai.Modules.BodyLanguage.Core.Policy;
using Convai.Modules.BodyLanguage.Core.Pose;
using Convai.Modules.BodyLanguage.Core.Signals;
using Convai.Modules.BodyLanguage.Data;
using Convai.Modules.BodyLanguage.Integrations;
using Convai.Runtime.Animation;
using Convai.Runtime.Animation.ProceduralPose;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Components
{
    /// <summary>
    ///     The Convai Body Language system: conversational nonverbal direction for the
    ///     character's body. Body Animation moves the body; Body Language makes it speak —
    ///     when to gesture, how to hold the spine, how to breathe, and how the body
    ///     participates in listening. Behavior is authored through one
    ///     <see cref="ConvaiBodyLanguageProfile" /> asset.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The component runs its decision layer in the embodiment
    ///         <see cref="EmbodimentTickPhase.Cognition" /> tick: it reads the dialogue state and
    ///         emotion reading, resolves the profile's per-state policy, smooths policy transitions
    ///         so a state change never snaps, and lets the directors — posture, breathing,
    ///         gesticulation, head gestures, listening posture, fidgets, stance, postural sway,
    ///         idle macro-cycles and one-shot reactions — compute this tick's targets. Actuation
    ///         runs in <c>LateUpdate</c> (execution order
    ///         <see cref="EmbodimentExecutionOrders.BodyPose" />, after the Animator/PlayableGraph
    ///         has posed the skeleton and before Gaze): the solvers spring toward those targets and
    ///         every channel is accumulated onto one shared
    ///         <see cref="ProceduralPoseCompositor" />, which performs exactly one guarded,
    ///         swing-only write per bone per frame.
    ///     </para>
    ///     <para>
    ///         The rig is validated on enable and on every rig rebind: a missing Spine bone
    ///         makes the module inert with a single logged error — it never throws or logs per
    ///         frame. On enable (and on a successful rebind) the posture/breath master weight
    ///         ramps in from zero rather than snapping; on disable the solvers instantly restore
    ///         whatever pose the animator wrote (never a residual delta left on a bone). Call
    ///         <see cref="CaptureSnapshot()" /> for a full live view.
    ///     </para>
    /// </remarks>
    [EmbodimentModule(ModuleIds.BodyLanguage, "Body Language",
        Description = "Posture, breathing and small movements that keep the body alive.",
        Absence = "the body holds a still pose between animations, without breathing or shifting " +
                  "its weight.",
        Order = 40)]
    [AddComponentMenu("Convai/Embodiment/Body Language")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EmbodimentExecutionOrders.BodyPose)]
    public sealed class ConvaiBodyLanguageController :
        ConvaiCharacterModule<ConvaiBodyLanguageProfile>,
        IEmbodimentTickable,
        IBodyLanguageSource
    {
        private readonly BodyLanguagePolicyEngine _policy = new();
        // Single writer for the shared spine/shoulder/head-gesture chain: one guard, one restore
        // per frame, one write per bone per apply. Also registered on EmbodimentContext as the
        // BodyPose slot's compositor so Gaze's torso-aim entry routes through this same guard
        // rather than holding a second one (OnEnable/OnDisable below).
        private readonly ProceduralPoseCompositor _poseCompositor = new();
        private readonly PostureSolver _postureSolver = new();
        private readonly BreathSolver _breathSolver = new();
        // Adaptive-layering estimator: watches the animated pose's own torso rotation for baked
        // idle-clip breathing so the procedural breath depth can duck itself underneath it
        // instead of beating against it.
        private readonly AnimatedBreathMotionEstimator _breathMotionEstimator = new();
        private readonly PostureDirector _postureDirector = new();
        private readonly BreathingDirector _breathingDirector = new();
        private readonly EmotionBodyModulator _emotionModulator = new();
        // One-shot bodily reactions: autonomous emotion-spike triggers plus the
        // scripted TriggerReaction API. Ticked right after the emotion modulator so it reads the
        // SAME EmotionReading the modulator just blended from.
        private readonly ReactionDirector _reactionDirector = new();
        private readonly HeadGestureDirector _headGestureDirector = new();
        private readonly HeadGestureChannel _headGestureChannel = new();
        private readonly GesticulationDirector _gesticulationDirector = new();
        private readonly FidgetDirector _fidgetDirector = new();
        private readonly ListeningPostureDirector _listeningPostureDirector = new();
        // Stance + postural sway: pelvis weight-shift/yaw schedule
        // and the continuous band-limited spine sway, composed onto the shared compositor's
        // pelvis and spine-chain channels alongside posture/breath in LateUpdate.
        private readonly StanceDirector _stanceDirector = new();
        private readonly PosturalSwayDirector _swayDirector = new();
        // Idle macro-cycle (idle presence): a very slow, multi-minute seeded
        // drift that nudges breath depth, sway amplitude, and fidget cadence together so a long
        // idle never settles into a perceptibly looping baseline. Ticked right after the sway
        // director every Cognition tick; consumed by the controller only (breath/sway/fidget
        // producers never see this director directly) — see the Cognition tick and LateUpdate.
        private readonly MacroCycleDirector _macroCycleDirector = new();
        // Idle hand/wrist micro-life: owns its own guard (fingers/wrists are
        // BL-exclusive bones, outside the shared compositor's spine/shoulder/head/pelvis/leg
        // scope) — see HandMicroSolver's remarks.
        private readonly HandMicroSolver _handMicroSolver = new();
        private readonly ProceduralArmGestureSolver _coSpeechArmSolver = new();
        private int _lastCoSpeechGestureSequence;

        private SpeechPulseAnalyzer _speechPulseAnalyzer;
        private Animator _handMicroAnimator;

        // Scripted-API bookkeeping: live handles this controller has issued via
        // Nod(), completed and removed as soon as their outcome is known. At most one active +
        // one pending head gesture is ever in flight (HeadGestureDirector's own single-slot
        // queue — see its remarks), so a small List is a zero-fuss fit, the same shape as
        // Gaze's own _handles list. PulseGesture's dispatch is synchronous (see its remarks), so
        // GestureCueHandle never needs an outstanding-handle list of its own.
        private readonly List<HeadGestureHandle> _headGestureHandles = new(2);
        private int _nextGestureCueRequestId;
        private BodyLanguageReading _currentReading = BodyLanguageReading.None;

        private BodyLanguageTrace _trace;
        private bool _tickRegistered;
        private bool _rigHandlerRegistered;

        // The acknowledgment nod on action-batch start. See
        // ActionPerformanceNodReactor remarks; registered/unregistered alongside the other seams.
        private ActionPerformanceNodReactor _actionPerformanceReactor;

        /// <summary>Nod intensity requested for the action acknowledgment beat (0..1).</summary>
        private const float ActionAcknowledgmentNodIntensity = 0.4f;
        private bool _runtimeInitialized;
        private bool _inert;
        private bool _hasSpine;
        private bool _hasChest;
        private bool _hasUpperChest;
        private bool _hasLeftShoulder;
        private bool _hasRightShoulder;
        private DialogueState _lastState;
        private bool _hasLastState;
        private bool _headGestureFallbackLogged;
        private bool _headGestureFallbackActiveThisFrame;
        private bool _legCompensationActiveThisFrame;
        private GestureSuppression _lastLoggedSuppression = GestureSuppression.None;
        private bool _hasLoggedSuppression;

        // ── Conversational intelligence ──────────────────────────────────────
        // The anticipatory inhale and the listening backchannel both subscribe
        // to Domain events — cached delegate fields (assigned once in the constructor, not per
        // OnEnable) so repeated enable/disable cycles never allocate a fresh delegate. Both
        // subscriptions are Domain-only (CharacterAudioPlaybackStateChanged/PlayerSpeakingStateChanged
        // live in Convai.Domain.DomainEvents.Runtime), so this never creates a reference to the
        // LipSync/ConversationFlow module assemblies — module isolation intact.
        private readonly Action<CharacterAudioPlaybackStateChanged> _handleAudioPlaybackStateChanged;
        private readonly Action<PlayerSpeakingStateChanged> _handlePlayerSpeakingStateChanged;
        private ConvaiCharacter _character;
        private IEventHub _subscribedEventHub;
        private SubscriptionToken _audioPlaybackToken;
        private SubscriptionToken _playerSpeakingToken;

        /// <summary>
        ///     <c>Time.time</c> the anticipatory pre-speech inhale last fired, or
        ///     <see cref="float.NegativeInfinity" /> if it never has — see
        ///     <see cref="AnticipatoryInhaleSuppressionWindowSeconds" />.
        /// </summary>
        private float _anticipatoryInhaleAt = float.NegativeInfinity;

        /// <summary>
        ///     This tick's blended physiological arousal, 0..1 —
        ///     captured once in the Cognition tick right after <see cref="_emotionModulator" />
        ///     ticks, consumed by both the fidget-gap feed later in the SAME Cognition tick and
        ///     the sway amplitude in <c>LateUpdate</c>.
        /// </summary>
        private float _arousalLevel = 0.5f;

        /// <summary>Cached <c>Camera.main</c> lookup — re-probed at most every <see cref="CameraReProbeIntervalSeconds" />, never read per frame.</summary>
        private Camera _mainCamera;

        private float _cameraProbeTimer;

        /// <summary>This tick's applied camera-distance amplitude LOD scale, slewed — 1 = no-op.</summary>
        private float _cameraLodScale = 1f;

        /// <summary>Current posture/breath master weight, 0..1 — ramps in on enable/rebind, never snaps.</summary>
        private float _masterWeight;

        /// <summary>
        ///     Posture-only suppression factor, 0..1 — under <see cref="GestureSuppression.UpperBody" />
        ///     it ramps to the profile's reduced weight while breath keeps the full master weight
        ///     ("posture at reduced weight, breath stays"). Multiplied into the
        ///     posture solve's weight in <c>LateUpdate</c>, never the breath solve's.
        /// </summary>
        private float _postureSuppressionWeight = 1f;

        /// <summary>
        ///     Runtime expressiveness override, set via the <see cref="Expressiveness" />
        ///     public property. <c>null</c> means "use the profile's own resolved value" —
        ///     cleared on a profile hot-swap (<see cref="OnProfileApplied" />) and on disable.
        /// </summary>
        private float? _expressivenessOverride;

        /// <summary>This tick's effective expressiveness 0..1 (override, else the profile's resolved value) — recomputed once per Cognition tick.</summary>
        private float _effectiveExpressiveness = 0.5f;

        /// <summary>
        ///     This tick's amplitude/frequency/richness gains derived from
        ///     <see cref="_effectiveExpressiveness" /> via <see cref="Core.Policy.ExpressivenessCurves" />
        ///     — recomputed once per Cognition tick, read by every amplitude/
        ///     interval/optional-behavior feed in Cognition and <c>LateUpdate</c>. All default to
        ///     1 (Natural, no-op) so an un-ticked controller composes bit-identically to a plain
        ///     multiply-by-one.
        /// </summary>
        private float _amplitudeGain = 1f;

        /// <summary>See <see cref="_amplitudeGain" />. Schedulers DIVIDE their interval by this gain.</summary>
        private float _frequencyGain = 1f;

        /// <summary>See <see cref="_amplitudeGain" />. Gates/scales optional-behavior repertoire (shrugs, hand micro-life, settle steps).</summary>
        private float _richnessGain = 1f;

        /// <summary>
        ///     This tick's posture-pulse contribution (Gesticulation), read by
        ///     <c>LateUpdate</c> and folded additively into <see cref="PostureSolveInput.TransientLeanTarget" />
        ///     BEFORE spring integration — the spring's own smoothing still applies, so a beat
        ///     never pops the posture. Transient (fully ducks under suppression) — see
        ///     <see cref="PostureSolveInput.SustainedLeanTarget" /> for the state+emotion source
        ///     that survives suppression at a floor instead.
        /// </summary>
        private float _posturePulseValue;

        /// <summary>
        ///     This tick's combined lateral weight-shift target (fidget program + a small static
        ///     Thinking asymmetry bias, clamped), read by <c>LateUpdate</c> and assembled into
        ///     <see cref="PostureSolveInput.LateralShiftTarget" />. Producer directors never touch
        ///     this — only the controller composes it (mirrors <see cref="_posturePulseValue" />).
        /// </summary>
        private float _lateralShiftValue;

        /// <summary>
        ///     Continuous smoothing of the registered <see cref="IConversationalMotionBudget" />'s
        ///     <c>UpperBodyOccupancy01</c> (0 when no budget is registered), slewed over the same
        ///     <see cref="ConvaiBodyLanguageProfile.PostureFadeSeconds" /> step as
        ///     <see cref="_postureSuppressionWeight" />. Feeds the hand-micro weight gate
        ///     so the overlay's own occupancy dips/rises never pop the idle hand motion.
        /// </summary>
        private float _occupancySmoothed;

        /// <summary>This tick's slewed idle hand/wrist micro-motion weight, read by <c>LateUpdate</c>.</summary>
        private float _handMicroWeight;

        /// <summary>Seconds^-1 the hand-micro weight may change per second — gating never pops.</summary>
        private const float HandMicroWeightSlewPerSecond = 2f;

        /// <summary>
        ///     Static lateral asymmetry bias while Thinking ("Thinking has a
        ///     body"), composed additively with the fidget director's own weight-shift so a
        ///     thinking pose reads as subtly asymmetric even between fidget cycles.
        /// </summary>
        private const float ThinkingAsymmetryBias = 0.2f;

        /// <summary>
        ///     Sustained-posture-source floors (sustained/transient posture-source separation):
        ///     under UpperBody suppression the sustained silhouette (state-policy + emotion bias,
        ///     from <see cref="PostureDirector" />) is floored at these effective weights instead
        ///     of ducking with the fast transient channels (posture pulses, listening lean-in,
        ///     fidget weight-shift) — a character keeps its openness/lean/tension "shape" while
        ///     talking with its hands, even though the beat-driven motion still visibly ducks.
        ///     Conservative, feel-tunable; NOT public profile knobs (minimize surface) — the
        ///     UpperBody suppression weight itself (<see cref="ConvaiBodyLanguageProfile.UpperBodySuppressionPostureWeight" />)
        ///     remains the single author-facing suppression knob.
        /// </summary>
        private const float OpennessSustainFloor = 0.85f;

        private const float TensionSustainFloor = 0.80f;
        private const float LeanSustainFloor = 0.75f;

        /// <summary>
        ///     Seconds the posture/breath scalar policy freezes on entering
        ///     <see cref="DialogueState.Interrupted" /> — the "freeze 0.3s → re-engage" beat.
        ///     Internal const, no profile surface.
        /// </summary>
        private const float InterruptedFreezeSeconds = 0.3f;

        /// <summary>
        ///     Intensity of the Reacting "sharp inhale", reusing the catch-breath
        ///     breath event at reduced strength so a reaction draws a smaller breath than a
        ///     full startled catch-breath on interruption.
        /// </summary>
        private const float ReactingInhaleIntensity = 0.6f;

        /// <summary>Minimum seconds between Firehose dumps so a diagnostics session stays readable.</summary>
        private const float FirehoseIntervalSeconds = 0.1f;

        /// <summary>
        ///     Lateral pelvis-offset cap (centimeters) used when leg compensation is unavailable
        ///     (toggled off, or the leg chain does not resolve) — small enough to stay
        ///     skinning-safe without visible foot slide even though nothing re-pins the feet.
        /// </summary>
        private const float LegFreePelvisOffsetCapCentimeters = 1.2f;

        /// <summary>
        ///     Seconds after an anticipatory pre-speech inhale fires during which
        ///     <see cref="HandleStateEntry" />'s own on-entry <c>InhaleBeforeSpeaking</c> trigger
        ///     is skipped as a duplicate — the state-entry trigger remains the degradation path
        ///     when the anticipatory signal never fires (no event, or it arrived too late — see
        ///     <see cref="OnAudioPlaybackStateChanged" />).
        /// </summary>
        private const float AnticipatoryInhaleSuppressionWindowSeconds = 1.5f;

        /// <summary>Minimum seconds between two <c>Camera.main</c> lookups — a lookup, never a per-frame call.</summary>
        private const float CameraReProbeIntervalSeconds = 1f;

        private float _firehoseTimer;

        public ConvaiBodyLanguageController()
        {
            _headGestureChannel.BindDirector(_headGestureDirector);
            _handleAudioPlaybackStateChanged = OnAudioPlaybackStateChanged;
            _handlePlayerSpeakingStateChanged = OnPlayerSpeakingStateChanged;
        }

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <inheritdoc />
        protected override string ProfileModuleId => ModuleIds.BodyLanguage;

        /// <inheritdoc />
        protected override Func<ConvaiBodyLanguageProfile> DefaultProfileFactory =>
            ConvaiBodyLanguageProfile.CreateDefault;

        /// <summary>Whether the module is inert (unusable rig; one error logged, no per-tick work).</summary>
        internal bool IsInert => _inert;

        internal BodyLanguageTrace Trace => _trace;

        /// <summary>
        ///     Number of consumers currently registered on the head-gesture channel (diagnostics/tests).
        /// </summary>
        internal int HeadGestureConsumerCount => _headGestureChannel.ConsumerCount;

        /// <summary>
        ///     The breathing director's currently active one-shot breath event — test/diagnostic
        ///     seam only (PlayMode coverage of the anticipatory-inhale
        ///     Domain-event subscription).
        /// </summary>
        internal BreathEventKind ActiveBreathEvent => _breathingDirector.ActiveEvent;

        /// <summary>
        ///     Requests a scripted one-shot head gesture (Nod/Shake/Tilt) at the given intensity
        ///     (0..1, scales amplitude). Returns <c>false</c> when the director's single pending
        ///     slot is already occupied — see <see cref="HeadGestureDirector" />. Handle-free
        ///     request surface used by the tests; <see cref="Nod" /> is the public API.
        /// </summary>
        internal bool RequestHeadGesture(HeadGestureKind kind, float intensity = 1f) =>
            _headGestureDirector.TryRequest(kind, intensity);

        /// <summary>Latest published body-language reading (also exposed via <see cref="IBodyLanguageSource" />).</summary>
        public BodyLanguageReading Current => _currentReading;

        /// <summary>
        ///     Runtime expressiveness override, 0..1. Setting it wins over the
        ///     profile until the next profile hot-swap (<see cref="OnProfileApplied" /> clears
        ///     it back to "use the profile"). The getter returns the effective value — the
        ///     override when set, otherwise the profile's own resolved
        ///     <c>ConvaiBodyLanguageProfile.ResolveExpressiveness()</c> — as of the most recent
        ///     Cognition tick.
        /// </summary>
        public float Expressiveness
        {
            get => _effectiveExpressiveness;
            set => _expressivenessOverride = Mathf.Clamp01(value);
        }

        /// <summary>
        ///     Whether the controller is in a state where its tick will actually advance and
        ///     drain scripted head-gesture handles. This is exactly the readiness gate the
        ///     Cognition tick and <c>LateUpdate</c> apply before touching the directors: the
        ///     component enabled and playing, the context and runtime present, the rig usable
        ///     (not inert), and an effective profile resolved. When this is <c>false</c> a
        ///     scripted request must NOT be enqueued — nothing would ever complete its handle —
        ///     so the scripted API instead degrades to an already-completed handle.
        /// </summary>
        private bool CanProcessScriptedHandles =>
            isActiveAndEnabled &&
            UnityEngine.Application.isPlaying &&
            Context != null &&
            _runtimeInitialized &&
            !_inert &&
            EffectiveProfile != null;

        /// <summary>
        ///     Requests a scripted one-shot head gesture (Nod/Shake/Tilt) at
        ///     <paramref name="intensity" /> (0..1, scales amplitude). Scripted requests share the
        ///     director's single active/pending slot with automatic programs (co-speech beats,
        ///     listening tilt-holds), so a scripted request can still be refused when that slot is
        ///     already occupied twice over.
        /// </summary>
        /// <returns>
        ///     A handle whose <see cref="HeadGestureHandle.Completion" /> resolves when the
        ///     program ends (naturally, superseded, or cleared via
        ///     <see cref="ClearScriptedOverrides" />). A request that is refused, or made on a
        ///     controller that cannot tick (disabled, not playing, inert from a missing rig, or
        ///     without an effective profile — see <see cref="CanProcessScriptedHandles" />),
        ///     returns an already-completed handle with <see cref="HeadGestureHandle.IsActive" />
        ///     <c>false</c> — never <c>null</c>, never throws, never hangs an <c>await</c>.
        /// </returns>
        public HeadGestureHandle Nod(HeadGestureKind kind, float intensity = 1f)
        {
            // Degrade gracefully: if the controller will not tick, nothing would
            // ever drain the handle, so never enqueue into the director — return an
            // already-completed handle instead of one that hangs forever.
            if (!CanProcessScriptedHandles)
                return CompletedHeadGestureHandle(kind, HeadGestureRefusal.Unavailable);

            bool accepted = _headGestureDirector.TryRequest(kind, intensity, out int requestId);
            if (!accepted)
                return CompletedHeadGestureHandle(kind, HeadGestureRefusal.Busy);

            var handle = new HeadGestureHandle(this, requestId, kind);
            _headGestureHandles.Add(handle);
            return handle;
        }

        private static HeadGestureHandle CompletedHeadGestureHandle(
            HeadGestureKind kind,
            HeadGestureRefusal refusal)
        {
            var handle = new HeadGestureHandle(null, 0, kind, refusal);
            handle.MarkCompleted();
            return handle;
        }

        /// <summary>
        ///     Requests a scripted semantic gesture cue, routed through the registered
        ///     <see cref="IConversationalGesturePerformer" /> with scripted priority over
        ///     automatic gesticulation.
        /// </summary>
        /// <remarks>
        ///     <b>Completion contract:</b> <see cref="GestureCueHandle.Completion" /> resolves as
        ///     soon as the cue's DISPATCH OUTCOME is known — accepted for performance by the
        ///     conversational gesture performer, or refused/substituted (see
        ///     <see cref="ConvaiBodyLanguageController.TryEmitGestureCue" />). It does NOT track
        ///     the resulting clip through to its visual end: that would require the performer to
        ///     report back per clip, which the contract deliberately does not ask of it. A
        ///     refused or substituted cue returns an already-completed handle.
        /// </remarks>
        /// <returns>A handle for the request. Never <c>null</c>, never throws.</returns>
        public GestureCueHandle PulseGesture(GestureCue cue)
        {
            int requestId = ++_nextGestureCueRequestId;
            var handle = new GestureCueHandle(this, requestId, cue.Kind);

            // Dispatch is synchronous when the controller can act (TryEmitGestureCue's return
            // value IS the dispatch outcome the contract promises); when it cannot (disabled,
            // not playing, inert, no profile — TryEmitGestureCue would short-circuit to a refusal
            // anyway) the handle still completes right here, so it never hangs an await.
            if (CanProcessScriptedHandles)
                TryEmitGestureCue(in cue);
            handle.MarkCompleted();
            return handle;
        }

        /// <summary>
        ///     Fire-and-forget one-shot bodily reaction. <see cref="ReactionKind.CatchBreath" />/
        ///     <see cref="ReactionKind.Sigh" /> route to the breathing system's own breath events;
        ///     <see cref="ReactionKind.SurpriseFlinch" />/<see cref="ReactionKind.AmusementBounce" />
        ///     route to the reaction system. Safe when the controller cannot tick (no-op — see
        ///     <see cref="CanProcessScriptedHandles" />). No handle: every envelope is sub-2-second
        ///     and not worth an awaitable contract. Respects the profile's own gates
        ///     (<see cref="ConvaiBodyLanguageProfile.EnableCatchBreath" />,
        ///     <see cref="ConvaiBodyLanguageProfile.EnableSigh" />,
        ///     <see cref="ConvaiBodyLanguageProfile.EnableReactions" />) — a disabled category is
        ///     silently dropped, same as the automatic on-entry beats.
        /// </summary>
        /// <param name="kind">The reaction to trigger. <see cref="ReactionKind.None" /> is a no-op.</param>
        /// <param name="intensity">Relative intensity, 0..1.</param>
        public void TriggerReaction(ReactionKind kind, float intensity = 1f)
        {
            if (!CanProcessScriptedHandles) return;

            ConvaiBodyLanguageProfile profile = EffectiveProfile;

            switch (kind)
            {
                case ReactionKind.CatchBreath:
                    if (profile.EnableCatchBreath)
                        _breathingDirector.TriggerEvent(BreathEventKind.CatchBreath, intensity);
                    break;
                case ReactionKind.Sigh:
                    if (profile.EnableSigh)
                        _breathingDirector.TriggerEvent(BreathEventKind.Sigh, intensity);
                    break;
                default:
                    if (profile.EnableReactions)
                        _reactionDirector.TryTrigger(kind, intensity, bypassRefractory: true);
                    break;
            }
        }

        /// <summary>
        ///     Cancels and completes every outstanding handle issued by <see cref="Nod" /> and
        ///     <see cref="PulseGesture" /> so any awaiting caller unblocks, and hands the
        ///     head-gesture channel back to the automatic directors. Idempotent — safe to call
        ///     when nothing is active.
        /// </summary>
        /// <remarks>
        ///     Only the controller's own tracked scripted requests are cancelled (via
        ///     <see cref="HeadGestureDirector.CancelRequest" />, matched by id); an autonomous
        ///     program the directors are running — a co-speech beat or a listening tilt-hold —
        ///     is left untouched and keeps playing. This is why it does NOT call
        ///     <see cref="HeadGestureDirector.Reset" />, which would wipe autonomous state too
        ///     ("directors resume").
        /// </remarks>
        public void ClearScriptedOverrides() => CompleteAllScriptedHandles();

        private void CompleteAllScriptedHandles()
        {
            for (int i = _headGestureHandles.Count - 1; i >= 0; i--)
            {
                HeadGestureHandle handle = _headGestureHandles[i];
                _headGestureDirector.CancelRequest(handle.RequestId);
                handle.MarkCompleted();
            }
            _headGestureHandles.Clear();
        }

        internal void ReleaseHeadGestureHandle(HeadGestureHandle handle)
        {
            if (handle == null) return;
            handle.MarkCompleted();
            _headGestureHandles.Remove(handle);
        }

        /// <summary>
        ///     <see cref="GestureCueHandle.Release" />'s owner callback. <see cref="PulseGesture" />
        ///     completes its handle synchronously before returning it, so by the time a caller
        ///     can call <see cref="GestureCueHandle.Release" /> the handle is already completed —
        ///     <see cref="GestureCueHandle.MarkCompleted" />'s <c>TrySetResult</c> makes this a
        ///     safe no-op rather than requiring any bookkeeping here.
        /// </summary>
        internal void ReleaseGestureCueHandle(GestureCueHandle handle) => handle?.MarkCompleted();

        /// <summary>
        ///     Settles outstanding <see cref="Nod" /> handles against this tick's director state
        ///     — called once per Cognition tick, right after the head-gesture director advances
        ///     (mirrors Gaze's <c>ProcessScriptedHandles</c> seam). A handle completes the instant its
        ///     correlation id is no longer the director's active or pending request (program
        ///     ended naturally, or was superseded/cleared).
        /// </summary>
        private void ProcessHeadGestureHandles()
        {
            for (int i = _headGestureHandles.Count - 1; i >= 0; i--)
            {
                HeadGestureHandle handle = _headGestureHandles[i];
                if (!_headGestureDirector.HasRequestEnded(handle.RequestId)) continue;

                handle.MarkCompleted();
                _headGestureHandles.RemoveAt(i);
            }
        }

        /// <inheritdoc />
        protected override void OnProfileApplied(ConvaiBodyLanguageProfile newProfile)
        {
            // A profile hot-swap re-resolves expressiveness from the new profile:
            // a runtime override only wins "until the next profile hot-swap" per the public
            // contract on the Expressiveness property.
            _expressivenessOverride = null;

            ConvaiBodyLanguageProfile effective = EffectiveProfile;
            if (_trace != null && effective != null)
                _trace.Verbosity = effective.TraceVerbosity;
            // A profile hot-swap re-applies the Signals section too: the analyzer's config is
            // immutable, so it is rebuilt from the new profile (event-cadence allocation, not a
            // per-tick cost). Pulse/baseline state restarts clean — matching the "profile
            // hot-swap re-resolves" behavior; a mid-utterance swap misses at most one onset.
            if (_runtimeInitialized && effective != null)
                _speechPulseAnalyzer = new SpeechPulseAnalyzer(effective.BuildSignalConfig());
            if (_trace != null && _trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                _trace.State($"Profile applied: '{(newProfile != null ? newProfile.name : "(runtime default)")}'.");
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;

            Context.EnsureTickScheduler()?.Register(this);
            _tickRegistered = true;

            if (!UnityEngine.Application.isPlaying) return;

            Context.RigBindingChanged += HandleRigBindingChanged;
            _rigHandlerRegistered = true;

            EnsureRuntimeInitialized();

            // Registered after runtime init so the channel only becomes discoverable once the
            // controller itself is ready to serve TryGetOffset. Every service claimed here is
            // released again by base.OnDisable().
            ProvideService<IHeadGestureChannel>(_headGestureChannel);
            ProvideService<IBodyLanguageSource>(this);

            // Claim the BodyPose slot's compositor so Gaze's torso-aim entry can find it
            // through the context (single owner per character — see the field's remarks).
            ProvideService<ProceduralPoseCompositor>(_poseCompositor);

            // Resolve this character's identity (mirrors
            // ConvaiEmotionController's own GetComponentInParent lookup) and subscribe to the
            // Domain-event seams. A missing EventHub (Context never populated with one — e.g. a
            // bare test rig) leaves both subscriptions unset, which simply means neither the
            // anticipatory inhale nor the listening backchannel fires — see
            // OnAudioPlaybackStateChanged/OnPlayerSpeakingStateChanged's own
            // CanProcessScriptedHandles guard for the same degradation on a live but
            // not-yet-ready controller.
            _character = GetComponentInParent<ConvaiCharacter>(true);
            SubscribeToEventHub();

            // The action acknowledgment reactor registers unconditionally (mirrors every other seam
            // registration above); the dispatcher's own Performance toggle decides whether it is
            // ever notified, so a disabled toggle is a true no-op.
            _actionPerformanceReactor ??= new ActionPerformanceNodReactor(this, ActionAcknowledgmentNodIntensity);
            ContributeService<IActionPerformanceReactor>(_actionPerformanceReactor);
        }

        /// <summary>Paired with <see cref="UnsubscribeFromEventHub" /> — see the fields' remarks.</summary>
        private void SubscribeToEventHub()
        {
            IEventHub hub = Context?.EventHub;
            if (hub == null) return;

            _audioPlaybackToken = hub.Subscribe<CharacterAudioPlaybackStateChanged>(_handleAudioPlaybackStateChanged);
            _playerSpeakingToken = hub.Subscribe<PlayerSpeakingStateChanged>(_handlePlayerSpeakingStateChanged);
            _subscribedEventHub = hub;
        }

        private void UnsubscribeFromEventHub()
        {
            IEventHub hub = _subscribedEventHub;
            if (hub == null) return;

            hub.Unsubscribe(_audioPlaybackToken);
            hub.Unsubscribe(_playerSpeakingToken);
            _audioPlaybackToken = default;
            _playerSpeakingToken = default;
            _subscribedEventHub = null;
        }

        /// <summary>
        ///     The anticipatory inhale fires on the earliest reliable pre-playback
        ///     signal available without a cross-module assembly reference —
        ///     <see cref="CharacterAudioPlaybackStateChanged" />.<c>Started()</c>. Verified
        ///     against the LipSync bridge (<c>ConvaiLipSyncBridge.OnAudioPlaybackStateChanged</c>):
        ///     this event is what OPENS the playback gate that starts the LipSync engine
        ///     producing frames, whose animator blend factor must then ramp up over further
        ///     frames before <c>CompositorDialoguePhaseAdapter.IsSpeechActive</c> (blend &gt;
        ///     0.01) can flip <see cref="DialogueState" /> to <c>Speaking</c> — so on that path
        ///     this signal is guaranteed-earlier by construction, not just usually-earlier.
        ///     A separate server-pushed <c>CharacterSpeechStateChanged</c> event can ALSO flip
        ///     Speaking directly (via <c>ConversationFlowStateMachine.Derive</c>'s
        ///     <c>IsCharacterSpeaking || IsLipSyncSpeaking</c> OR); its ordering relative to this
        ///     signal is not statically provable (platform/network dependent — the LipSync
        ///     bridge's own remarks note server speech state can lag WebGL audio playback, i.e.
        ///     arrive AFTER this signal there, though nothing guarantees the reverse never
        ///     happens on other platforms). The "state is not already Speaking" guard below
        ///     makes a late arrival on that path a safe, silent no-op rather than a
        ///     double-trigger, so the feature degrades gracefully per-utterance (falls back to
        ///     <see cref="HandleStateEntry" />'s on-entry trigger) instead of failing outright
        ///     when the race is lost.
        /// </summary>
        private void OnAudioPlaybackStateChanged(CharacterAudioPlaybackStateChanged evt)
        {
            if (!evt.IsPlaying) return;
            if (!MatchesCharacter(evt.CharacterId)) return;
            if (!CanProcessScriptedHandles) return;

            ConvaiBodyLanguageProfile profile = EffectiveProfile;
            if (profile == null || !profile.EnableInhaleBeforeSpeaking) return;

            DialogueState state = Context.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            if (state == DialogueState.Speaking) return;

            _breathingDirector.TriggerEvent(BreathEventKind.InhaleBeforeSpeaking);
            _anticipatoryInhaleAt = Time.time;
        }

        /// <summary>
        ///     The listening backchannel: a falling edge (the user just stopped
        ///     speaking) is a pause boundary — no energy/VAD threshold needed, the binary event
        ///     itself is the signal. Forwarded to <see cref="ListeningPostureDirector.NotifyUserPause" />
        ///     only while actually Listening — the director itself is otherwise
        ///     inert outside Listening, but the state check here also avoids probing
        ///     <see cref="CanProcessScriptedHandles" /> needlessly on every rising edge).
        /// </summary>
        private void OnPlayerSpeakingStateChanged(PlayerSpeakingStateChanged evt)
        {
            if (evt.IsSpeaking) return;
            if (!CanProcessScriptedHandles) return;

            DialogueState state = Context.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            if (state != DialogueState.Listening) return;

            _listeningPostureDirector.NotifyUserPause();
        }

        /// <summary>Character-identity filter for the anticipatory inhale, mirroring how LipSync and Emotion scope their own Domain-event subscriptions.</summary>
        private bool MatchesCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return false;
            if (_character == null || string.IsNullOrWhiteSpace(_character.CharacterId)) return false;
            return string.Equals(_character.CharacterId, characterId, StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnDisable()
        {
            UnsubscribeFromEventHub();
            _character = null;
            if (_tickRegistered)
            {
                Context?.TickScheduler?.Unregister(this);
                _tickRegistered = false;
            }

            if (_rigHandlerRegistered && Context != null)
            {
                Context.RigBindingChanged -= HandleRigBindingChanged;
                _rigHandlerRegistered = false;
            }

            // The head-gesture channel, the body-language source, the pose compositor and the
            // action-performance reactor are all released by base.OnDisable() below, which owns
            // every service this component provided.

            // Neutral report on disable: the budget owner holds the last
            // reported value indefinitely, so a disabling consumer must explicitly hand back 1f
            // rather than leave a stale non-neutral scale in effect.
            Context?.ConversationalMotionBudget?.ReportConversationalIntensity(1f);

            CompleteAllScriptedHandles();

            _policy.Reset();
            _postureDirector.Reset();
            _breathingDirector.Reset();
            _emotionModulator.Reset();
            _reactionDirector.Reset();
            _headGestureDirector.Reset();
            _gesticulationDirector.Reset();
            _fidgetDirector.Reset();
            _listeningPostureDirector.Reset();
            _stanceDirector.Reset();
            _swayDirector.Reset();
            _macroCycleDirector.Reset();
            _speechPulseAnalyzer?.Reset();
            // Instant restore, not a timed fade: LateUpdate stops ticking the instant the
            // component disables, so the only way to guarantee zero residual delta is to
            // unwind whatever was written this frame right now (mirrors the gaze solver
            // chain's own disable/rebind reset). The shared guard's cached post-write value
            // matches this frame's final composite, so restoring it BEFORE either solver
            // resets its own state correctly unwinds the whole chain in one shot — restoring
            // after either Reset() would find the guard already emptied.
            _poseCompositor.RestoreStaleWrites();
            _postureSolver.Reset();
            _breathSolver.Reset();
            _breathMotionEstimator.Reset();
            _poseCompositor.Clear();
            _handMicroSolver.Reset();
            _coSpeechArmSolver.Reset();
            // Clearing the last-seen co-speech sequence with the solver it gates: a stale value
            // surviving a disable/enable cycle would make the next request whose sequence happens
            // to match it look like one already played, and swallow it.
            _lastCoSpeechGestureSequence = 0;
            _handMicroAnimator = null;
            _masterWeight = 0f;
            _postureSuppressionWeight = 1f;
            _occupancySmoothed = 0f;
            _handMicroWeight = 0f;
            _inert = false;
            _hasSpine = false;
            _hasChest = false;
            _hasUpperChest = false;
            _hasLeftShoulder = false;
            _hasRightShoulder = false;
            _lastState = DialogueState.Idle;
            _hasLastState = false;
            _headGestureFallbackLogged = false;
            _headGestureFallbackActiveThisFrame = false;
            _legCompensationActiveThisFrame = false;
            _lastLoggedSuppression = GestureSuppression.None;
            _hasLoggedSuppression = false;
            _posturePulseValue = 0f;
            _lateralShiftValue = 0f;
            _expressivenessOverride = null;
            _effectiveExpressiveness = 0.5f;
            _amplitudeGain = 1f;
            _frequencyGain = 1f;
            _richnessGain = 1f;
            _currentReading = BodyLanguageReading.None;
            _runtimeInitialized = false;
            _anticipatoryInhaleAt = float.NegativeInfinity;
            _arousalLevel = 0.5f;
            _mainCamera = null;
            _cameraProbeTimer = 0f;
            _cameraLodScale = 1f;

            base.OnDisable();
        }

        /// <summary>
        ///     Cognition tick: dialogue state → resolved per-state policy → smoothed policy →
        ///     emotion modulation → posture/breathing director targets for this tick's
        ///     <c>LateUpdate</c> solve.
        /// </summary>
        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (!UnityEngine.Application.isPlaying || Context == null) return;

            EnsureRuntimeInitialized();
            if (_inert) return;

            ConvaiBodyLanguageProfile profile = EffectiveProfile;
            if (profile == null) return;

            // Expressiveness dial: resolved once per tick, before any director
            // runs, so every amplitude/interval/optional-behavior feed below reads this SAME
            // tick's gains.
            UpdateExpressivenessGains(profile);

            DialogueState state = Context.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;

            // Arm on-entry beats for a genuine state transition BEFORE the policy and directors
            // tick this frame: the Interrupted freeze must hold this very frame's pre-interrupt
            // posture, and a breath event should start advancing in this frame's breath tick.
            // _lastState is updated (with the trace) at the end of the tick, so this compares
            // against the previous frame's state.
            if (_hasLastState && state != _lastState)
                HandleStateEntry(state, profile);

            BodyLanguageStatePolicy statePolicy = profile.GetPolicy(state);
            _policy.Tick(in statePolicy, profile.PolicyTransitionSeconds, deltaTime);

            IEmotionStateFrameSource frameSource = Context.EmotionStateFrameSource;
            EmotionReading emotion = EmotionReading.Neutral;
            if (frameSource != null)
            {
                EmotionStateFrame frame = frameSource.CurrentFrame;
                _emotionModulator.Tick(profile, in frame);
            }
            else
            {
                emotion = Context.EmotionStateSource?.Current ?? EmotionReading.Neutral;
                _emotionModulator.Tick(profile, in emotion);
            }
            // Physiological coherence: captured once here, right
            // after the modulator ticks, so both the fidget-gap feed later in THIS tick and the
            // sway amplitude in LateUpdate read the same tick's arousal. Breath rate is already
            // emotion-scaled via BreathRateScale inside BreathingDirector.Tick below — arousal is
            // NOT double-applied there.
            _arousalLevel = _emotionModulator.Arousal01;
            // Reactions: reads the SAME EmotionReading the modulator just blended
            // from, so an autonomous flinch/bounce spike detection sees this tick's fresh scores.
            if (frameSource != null)
            {
                EmotionStateFrame frame = frameSource.CurrentFrame;
                _reactionDirector.Tick(in frame, deltaTime);
            }
            else
            {
                _reactionDirector.Tick(in emotion, deltaTime);
            }

            BodyLanguageStatePolicy smoothed = _policy.Current;
            _postureDirector.Tick(in smoothed, _emotionModulator, profile.PostureTargetSlewSeconds, deltaTime);

            // Ensure (not just read): auto-provisions the LipSync speech-energy adapter on first
            // demand when a LipSync component is present on this character (mirrors
            // TryEnsureConversationFlowSource's on-demand pattern). Once registered this is a
            // cheap null-check every subsequent tick; when no LipSync component exists it stays
            // null and gesticulation degrades to the statistical-cadence fallback. Moved above
            // the breathing director's own tick so its speech-coupled exhale sees
            // this SAME tick's fresh energy value instead of last tick's; the SpeechPulse
            // analyzer step itself stays below, unchanged, using this same `energy`.
            ISpeechEnergyProvider speechEnergyProvider = Context.EnsureSpeechEnergyProvider();
            bool hasSpeechEnergyProvider = speechEnergyProvider != null;
            float energy = speechEnergyProvider?.Current ?? 0f;

            // Idle macro-cycle depth scale: reads whatever value the macro-cycle
            // director currently holds — this tick's own _macroCycleDirector.Tick call happens
            // later below (right after the sway director), so this is last tick's settled value.
            // An accepted, imperceptible one-tick defer at this director's multi-minute timescale
            // (same idiom as the speech-gap-inhale arm-this-tick/apply-next-tick defer above).
            float macroDepthScale = 1f + 0.12f * _macroCycleDirector.Energy01;
            // Exertion → breath: folds locomotion effort from the optional
            // IExertionSource (Body Animation) into the breathing rate/depth multipliers. No
            // source registered (module absent, or the producer's PublishExertion is off)
            // degrades to exactly today's behavior — both multipliers stay at the identity 1.
            float exertion01 = Mathf.Clamp01(Context.ExertionSource?.Exertion01 ?? 0f);
            float exertionRateMultiplier = 1f + profile.ExertionRateBoost * exertion01;
            float exertionDepthMultiplier = 1f + profile.ExertionDepthBoost * exertion01;
            _breathingDirector.Tick(
                in smoothed, _emotionModulator, profile.PostureTargetSlewSeconds, deltaTime,
                energy, state == DialogueState.Speaking, macroDepthScale,
                exertionRateMultiplier, exertionDepthMultiplier);
            _headGestureDirector.Tick(
                deltaTime,
                profile.HeadGestureNodMaxPitchDegrees,
                profile.HeadGestureShakeMaxYawDegrees,
                profile.HeadGestureTiltMaxRollDegrees,
                profile.HeadGestureRefractorySeconds,
                profile.HeadGestureRefractoryVarianceSeconds);
            if (_headGestureHandles.Count > 0)
                ProcessHeadGestureHandles();

            IConversationalGesturePerformer performer = Context.ConversationalGesturePerformer;
            IConversationalMotionBudget budget = Context.ConversationalMotionBudget;
            GestureSuppression suppression = ResolveSuppression(performer, budget);
            // Standing report, not a one-shot event (interface contract): re-reported every
            // cognition tick; a neutral disable-time report lives in OnDisable.
            budget?.ReportConversationalIntensity(_emotionModulator.GestureIntensityScale);

            SpeechPulse pulse = default;
            _speechPulseAnalyzer?.Step(energy, deltaTime, out pulse);

            _gesticulationDirector.Tick(
                state,
                smoothed.GesticulationEnabled,
                smoothed.GesticulationIntensity,
                in pulse,
                hasSpeechEnergyProvider,
                suppression,
                _emotionModulator.GestureIntensityScale,
                _emotionModulator.GestureRateScale,
                deltaTime,
                DivideByGain(profile.BeatMinIntervalSeconds, _frequencyGain),
                DivideByGain(profile.BeatIntervalVarianceSeconds, _frequencyGain),
                ScaleByGain(profile.BeatHeadIntensity, _amplitudeGain),
                ScaleByGain(profile.PosturePulseAmplitude, _amplitudeGain),
                profile.PosturePulseAttackSeconds,
                profile.PosturePulseDecaySeconds,
                profile.EnergyToIntensityGain,
                DivideByGain(profile.StatisticalCadenceIntervalSeconds, _frequencyGain),
                DivideByGain(profile.StatisticalCadenceVarianceSeconds, _frequencyGain),
                profile.UpperBodySuppressionPostureWeight,
                _trace);

            if (_gesticulationDirector.WantsHeadBeat)
                _headGestureDirector.TryRequestBeat(HeadGestureKind.Nod, _gesticulationDirector.HeadBeatIntensity);
            // Phrase-end nod: a distinct request from the ordinary beat above —
            // both go through the same fire-now-or-drop TryRequestBeat slot, so at most one of
            // the two actually starts a program this tick (whichever the director set first is
            // irrelevant; only one of WantsHeadBeat/WantsPhraseEndNod is ever true per tick, since
            // GesticulationDirector.Tick returns immediately after arming either one).
            if (_gesticulationDirector.WantsPhraseEndNod)
                _headGestureDirector.TryRequestBeat(
                    HeadGestureKind.Nod, _gesticulationDirector.PhraseEndNodIntensity, GesticulationDirector.PhraseEndNodDurationSeconds);

            _posturePulseValue = _gesticulationDirector.PosturePulseValue;

            // Speech-coupled breathing: a confident Release pulse (a phrase
            // gap) while Speaking arms a gentle top-up inhale — breath already escapes gesture
            // suppression (see the breath-input carve-out below), so this is the cheapest way to
            // make the body read as breathing DURING speech, not just between utterances. Placed
            // here (after the gesticulation director's own Tick, above) rather than immediately
            // after `pulse` is computed so `IsStatisticalCadenceActive` is this SAME tick's fresh
            // value, not last tick's — no existing line is reordered to get this. This tick's
            // `BreathingDirector.Tick()` already ran earlier (right after `_postureDirector.Tick`,
            // before `pulse` existed), so arming the event now still only takes visible effect
            // starting next tick's envelope advance — a one-tick defer that is unavoidable
            // wherever within this tick the call is placed, and imperceptible for a breath cue.
            // SpeechPulse
            // is a struct (allocation-free); BreathingDirector owns all anti-pumping (refractory,
            // confidence floor, envelope lockout, conservative fallback mode).
            if (state == DialogueState.Speaking)
                _breathingDirector.TryTriggerSpeechGapInhale(
                    pulse.Kind, pulse.Strength, _gesticulationDirector.IsStatisticalCadenceActive);

            // Gaze-aversion gate: read-only consume of the Gaze module's
            // reading — a missing/absent Gaze source degrades to "never averting" (tilt-holds
            // still schedule normally), never a throw. Covers both Listening's own tilt-hold and
            // a Thinking look-away that bleeds into a later Listening beat.
            bool gazeIsAverting = (Context.GazeSource?.Current ?? GazeReading.None).IsAverting;

            _listeningPostureDirector.Tick(
                state,
                smoothed.ListeningPostureEnabled,
                smoothed.ListeningLeanIn,
                gazeIsAverting,
                deltaTime,
                DivideByGain(profile.ListeningTiltCadenceSeconds, _frequencyGain),
                ScaleByGain(profile.ListeningTiltIntensity, _amplitudeGain));

            if (_listeningPostureDirector.WantsTiltHold && !gazeIsAverting)
                _headGestureDirector.TryRequest(HeadGestureKind.Tilt, _listeningPostureDirector.TiltHoldIntensity);

            // Idle macro-cycle fidget cadence: same one-tick-defer accepted
            // above for breath — higher macro energy makes fidgets slightly busier (a shorter
            // gap between cycles). Composes with the arousal-driven gap scale:
            // 1.15 - 0.3*arousal — at neutral arousal (0.5) this is exactly 1, so a character
            // with no active emotion is left entirely unscaled.
            float macroFidgetGapScale = 1f - 0.15f * _macroCycleDirector.Energy01;
            float arousalFidgetGapScale = 1.15f - 0.3f * _arousalLevel;
            _fidgetDirector.Tick(
                state,
                smoothed.FidgetsEnabled,
                smoothed.FidgetRate,
                suppression,
                _listeningPostureDirector.StillnessFactor,
                deltaTime,
                DivideByGain(profile.FidgetGapSeconds, _frequencyGain) * macroFidgetGapScale * arousalFidgetGapScale,
                profile.FidgetEaseSeconds,
                profile.FidgetHoldSeconds);

            // Stance: the periodic pelvis weight-shift/yaw schedule.
            _stanceDirector.Tick(
                state,
                profile.EnableWeightShifts,
                suppression,
                DivideByGain(profile.WeightShiftIntervalSeconds, _frequencyGain),
                DivideByGain(profile.WeightShiftIntervalVarianceSeconds, _frequencyGain),
                profile.WeightShiftTransferSeconds,
                deltaTime,
                _richnessGain);

            // Postural sway: this director is the sole consumer of the smoothed policy's
            // per-state AmbientDrift knob.
            _swayDirector.Tick(profile.EnableAmbientSway, smoothed.AmbientDrift, deltaTime);

            // Idle macro-cycle (idle presence): ticked right after sway so this
            // frame's fresh Energy01 is available to the sway amplitude consumer in LateUpdate
            // below (breath/fidget above already consumed the PREVIOUS tick's value — see their
            // own comments for why that one-tick defer is unavoidable and imperceptible here).
            _macroCycleDirector.Tick(profile.EnableIdleMacroCycles, deltaTime);

            // Thinking asymmetry ("Thinking has a body"): a small static bias
            // composes additively with the fidget program's own weight-shift so a thinking pose
            // reads as subtly asymmetric even between fidget cycles, not just during them.
            float thinkingAsymmetry = state == DialogueState.Thinking ? ThinkingAsymmetryBias : 0f;
            _lateralShiftValue = Mathf.Clamp(_fidgetDirector.WeightShiftValue + thinkingAsymmetry, -1f, 1f);

            // Suppression drives TWO weights: the shared master weight — FullBody
            // fades posture AND breath fully out (the active full-body motion owns the
            // skeleton), anything else ramps to 1, fully active while the rig is usable — and a
            // posture-only factor for UpperBody (locomotion, upper-body talk), where the rule is
            // "posture at reduced weight, BREATH STAYS":
            // the factor multiplies only the posture solve's weight in LateUpdate, never the
            // breath solve's. Both ramp through the SAME PostureFadeSeconds knob already used
            // for enable/disable fades — never a second fade constant, never a snap between
            // suppression levels.
            float fadeStep = profile.PostureFadeSeconds > 0f ? deltaTime / profile.PostureFadeSeconds : 1f;
            MotionRegionWeights regions = MotionRegionArbitrator.Resolve(
                suppression, budget != null, budget?.UpperBodyOccupancy01 ?? 0f,
                profile.UpperBodySuppressionPostureWeight);
            _masterWeight = Mathf.MoveTowards(_masterWeight, regions.Master, fadeStep);

            // Continuous posture duck: when a motion budget is registered, the
            // binary UpperBody ramp is replaced by a target proportional to the overlay's own
            // live occupancy — posture reduces smoothly as the talk overlay swells and recovers
            // in its speech-pause dips, instead of snapping between two suppression levels. No
            // budget ⇒ the binary behavior, byte-for-byte (degradation path).
            _postureSuppressionWeight = Mathf.MoveTowards(_postureSuppressionWeight, regions.Posture, fadeStep);

            // Hand-micro's occupancy gate — same fade step as the posture duck
            // above so both settle on the same cadence.
            _occupancySmoothed = Mathf.MoveTowards(_occupancySmoothed, 1f - regions.HandMicro, fadeStep);

            CoSpeechPerformanceReading coSpeech = Context.CoSpeechPerformanceSource?.Current ?? CoSpeechPerformanceReading.None;
            if (coSpeech.PhrasePhase is CoSpeechPhrasePhase.Interrupted or
                CoSpeechPhrasePhase.Releasing or CoSpeechPhrasePhase.None)
                _coSpeechArmSolver.Cancel();
            if (coSpeech.HasGesture && coSpeech.GestureSequence != _lastCoSpeechGestureSequence)
            {
                _lastCoSpeechGestureSequence = coSpeech.GestureSequence;
                CoSpeechGestureRequest gesture = coSpeech.Gesture;
                bool requestedRegionFree = gesture.Handedness switch
                {
                    CoSpeechHandedness.Left => regions.LeftArm > 0f,
                    CoSpeechHandedness.Right => regions.RightArm > 0f,
                    CoSpeechHandedness.Bilateral => regions.LeftArm > 0f && regions.RightArm > 0f,
                    _ => regions.Arms > 0f
                };
                if (requestedRegionFree && profile.EnableProceduralGestureFallback)
                    _coSpeechArmSolver.TryStart(in gesture);
            }

            if (!_hasLoggedSuppression || suppression != _lastLoggedSuppression)
            {
                if (_hasLoggedSuppression && _trace != null && _trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                    _trace.State($"Gesture suppression {_lastLoggedSuppression} → {suppression}.");
                _lastLoggedSuppression = suppression;
                _hasLoggedSuppression = true;
            }

            if (!_hasLastState || state != _lastState)
            {
                if (_hasLastState && _trace != null && _trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                    _trace.State($"Dialogue state {_lastState} → {state} — policy re-targeted.");
                _lastState = state;
                _hasLastState = true;
            }
        }

        /// <summary>
        ///     Resolves this tick's effective expressiveness (<see cref="_expressivenessOverride" />
        ///     wins, else <c>profile.ResolveExpressiveness()</c>) and derives the three gains via
        ///     <see cref="Core.Policy.ExpressivenessCurves" />. Called once per
        ///     Cognition tick, before any director runs.
        /// </summary>
        private void UpdateExpressivenessGains(ConvaiBodyLanguageProfile profile)
        {
            _effectiveExpressiveness = Mathf.Clamp01(_expressivenessOverride ?? profile.ResolveExpressiveness());
            _amplitudeGain = ExpressivenessCurves.AmplitudeGain(_effectiveExpressiveness);
            _frequencyGain = ExpressivenessCurves.FrequencyGain(_effectiveExpressiveness);
            _richnessGain = ExpressivenessCurves.RichnessGain(_effectiveExpressiveness);
        }

        /// <summary>
        ///     Scales <paramref name="baseValue" /> by <paramref name="gain" /> — the expressiveness
        ///     ×AmplitudeGain/×RichnessGain fast path: at the Natural
        ///     default every gain resolves to exactly 1, so this returns <paramref name="baseValue" />
        ///     unmodified instead of paying for a needless multiply.
        /// </summary>
        private static float ScaleByGain(float baseValue, float gain) => gain == 1f ? baseValue : baseValue * gain;

        /// <summary>
        ///     Divides <paramref name="intervalSeconds" /> by <paramref name="frequencyGain" /> — a
        ///     higher gain yields a shorter, more frequent interval. Same ==1 fast path as
        ///     <see cref="ScaleByGain" />.
        /// </summary>
        private static float DivideByGain(float intervalSeconds, float frequencyGain) =>
            frequencyGain == 1f ? intervalSeconds : intervalSeconds / frequencyGain;

        /// <summary>
        ///     On-entry beats for a genuine dialogue-state transition,
        ///     armed before this frame's policy and director ticks: the Interrupted freeze plus a
        ///     posture-pulse clear (a hard pause that holds the pre-interrupt pose for
        ///     <see cref="InterruptedFreezeSeconds" />), and the state-specific breath events —
        ///     catch-breath on interruption and, at <see cref="ReactingInhaleIntensity" />, on a
        ///     sharp reaction; a sigh on settling; an inhale as speech starts. Each breath event
        ///     is gated by its profile toggle (<see cref="ConvaiBodyLanguageProfile.EnableCatchBreath" />,
        ///     <see cref="ConvaiBodyLanguageProfile.EnableSigh" />,
        ///     <see cref="ConvaiBodyLanguageProfile.EnableInhaleBeforeSpeaking" />) — off ⇒ no event.
        ///     Entering Reacting also attempts a mild startle flinch via
        ///     <see cref="ReactionDirector.TryTrigger" /> — a non-bypassing autonomous-style
        ///     request, so it silently no-ops if the reaction system is already in its own
        ///     refractory window. The Speaking entry additionally skips its own inhale when the
        ///     anticipatory pre-speech inhale (<see cref="OnAudioPlaybackStateChanged" />) already fired within
        ///     <see cref="AnticipatoryInhaleSuppressionWindowSeconds" /> — this on-entry trigger
        ///     remains the degradation path when no anticipatory signal ever fires.
        /// </summary>
        private void HandleStateEntry(DialogueState state, ConvaiBodyLanguageProfile profile)
        {
            switch (state)
            {
                case DialogueState.Interrupted:
                    _policy.BeginHold(InterruptedFreezeSeconds);
                    _gesticulationDirector.ClearPosturePulse();
                    _posturePulseValue = 0f;
                    if (profile.EnableCatchBreath)
                        _breathingDirector.TriggerEvent(BreathEventKind.CatchBreath);
                    break;
                case DialogueState.Reacting:
                    if (profile.EnableCatchBreath)
                        _breathingDirector.TriggerEvent(BreathEventKind.CatchBreath, ReactingInhaleIntensity);
                    _reactionDirector.TryTrigger(ReactionKind.SurpriseFlinch, 0.5f, bypassRefractory: false);
                    break;
                case DialogueState.Settling:
                    if (profile.EnableSigh)
                        _breathingDirector.TriggerEvent(BreathEventKind.Sigh);
                    break;
                case DialogueState.Speaking:
                    bool alreadyDrewAnticipatoryBreath =
                        (Time.time - _anticipatoryInhaleAt) < AnticipatoryInhaleSuppressionWindowSeconds;
                    if (profile.EnableInhaleBeforeSpeaking && !alreadyDrewAnticipatoryBreath)
                        _breathingDirector.TriggerEvent(BreathEventKind.InhaleBeforeSpeaking);
                    break;
            }
        }

        /// <summary>
        ///     Semantic-channel entry point: routes an explicit
        ///     <see cref="GestureCue" /> through the registered
        ///     <see cref="IConversationalGesturePerformer" />, respecting suppression and the
        ///     profile's semantic-cue refractory. Returns the dispatch outcome directly;
        ///     <see cref="PulseGesture" /> is the public, handle-returning wrapper around it.
        /// </summary>
        internal bool TryEmitGestureCue(in GestureCue cue)
        {
            if (Context == null) return false;

            ConvaiBodyLanguageProfile profile = EffectiveProfile;
            if (profile == null) return false;

            IConversationalGesturePerformer performer = Context.ConversationalGesturePerformer;
            IConversationalMotionBudget budget = Context.ConversationalMotionBudget;
            GestureSuppression suppression = ResolveSuppression(performer, budget);

            bool accepted = _gesticulationDirector.TryEmitCue(
                in cue, performer, suppression,
                _emotionModulator.GestureIntensityScale,
                profile.SemanticCueRefractorySeconds,
                ScaleByGain(profile.BeatHeadIntensity, _amplitudeGain),
                ScaleByGain(profile.PosturePulseAmplitude, _amplitudeGain),
                _trace);

            if (_gesticulationDirector.WantsHeadBeat)
                _headGestureDirector.TryRequestBeat(HeadGestureKind.Nod, _gesticulationDirector.HeadBeatIntensity);
            _posturePulseValue = _gesticulationDirector.PosturePulseValue;

            MotionRegionWeights cueRegions = MotionRegionArbitrator.Resolve(
                suppression, budget != null, budget?.UpperBodyOccupancy01 ?? 0f,
                profile.UpperBodySuppressionPostureWeight);
            if (!accepted && _gesticulationDirector.ProceduralFallbackRequested && cueRegions.Arms > 0f &&
                profile.EnableProceduralGestureFallback)
                _handMicroSolver.TryTriggerGesture(cue.Kind, cue.Intensity * _emotionModulator.GestureIntensityScale);

            return accepted;
        }

        /// <summary>
        ///     Actuation: posture spring update + breath phase update, applied as swing-only
        ///     deltas over the frame's animated pose. Runs after the Animator/PlayableGraph has
        ///     posed the skeleton (execution order <see cref="EmbodimentExecutionOrders.BodyPose" />)
        ///     and before Gaze (<see cref="EmbodimentExecutionOrders.Gaze" />) re-solves the
        ///     head/torso chain, so both layers compose cleanly with one writer per bone.
        /// </summary>
        /// <summary>
        ///     How much a dialogue state trusts the animated-pose breath-motion sample this tick:
        ///     Idle/Listening (calm, no active talking) trust it fully;
        ///     Thinking/Settling are half-trusted transitional states; Speaking/Reacting/
        ///     Interrupted actively pollute the sample with unrelated motion and are excluded
        ///     entirely. Attending (a brief orienting beat, itself calm) is treated like Idle.
        /// </summary>
        private static float StateDuckWeight(DialogueState state) => state switch
        {
            DialogueState.Idle => 1f,
            DialogueState.Attending => 1f,
            DialogueState.Listening => 1f,
            DialogueState.Thinking => 0.5f,
            DialogueState.Settling => 0.5f,
            DialogueState.Speaking => 0f,
            DialogueState.Reacting => 0f,
            DialogueState.Interrupted => 0f,
            _ => 1f
        };

        /// <summary>
        ///     Resolves the effective suppression consumed by the Cognition-tick duck logic:
        ///     a registered budget's <see cref="IConversationalMotionBudget.HardSuppression" />
        ///     takes over — deliberately hard-only, since upper-body-talk-alone is covered at
        ///     finer granularity by the budget's own occupancy negotiation — while a missing
        ///     budget degrades byte-for-byte to the older <see cref="IConversationalGesturePerformer.CurrentSuppression" />
        ///     read.
        /// </summary>
        private static GestureSuppression ResolveSuppression(
            IConversationalGesturePerformer performer, IConversationalMotionBudget budget) =>
            budget != null ? budget.HardSuppression : (performer?.CurrentSuppression ?? GestureSuppression.None);

        /// <summary>
        ///     Idle hand-micro's per-state weight multiplier: fully alive while
        ///     Idle/Listening/Thinking; a small residual (0.35) while Speaking so authored talk
        ///     clips own the hands mostly, with just enough life left in the pauses; silent
        ///     everywhere else (Reacting/Interrupted/Settling/Attending — busy or transitional).
        /// </summary>
        private static float StateHandMicroWeight(DialogueState state) => state switch
        {
            DialogueState.Idle => 1f,
            DialogueState.Listening => 1f,
            DialogueState.Thinking => 1f,
            DialogueState.Speaking => 0.35f,
            _ => 0f
        };

        /// <summary>
        ///     Camera-distance amplitude LOD: resolves and slews
        ///     <see cref="_cameraLodScale" />. <c>Camera.main</c> is only ever read here, at most
        ///     once every <see cref="CameraReProbeIntervalSeconds" /> — never per frame. Toggled
        ///     off ⇒ the scale snaps to exactly 1, leaving no residual slew behind; a
        ///     resolved-but-currently-null
        ///     camera (e.g. a transient scene-load gap) instead feeds a target of 1 through the
        ///     SAME slew as a real distance change, so it composes with "camera cuts never pop
        ///     amplitude" rather than being a special-cased snap.
        /// </summary>
        private void UpdateCameraDistanceLod(ConvaiBodyLanguageProfile profile, float deltaTime)
        {
            if (!profile.EnableCameraDistanceLod)
            {
                _cameraLodScale = 1f;
                return;
            }

            _cameraProbeTimer -= deltaTime;
            if (_cameraProbeTimer <= 0f)
            {
                _mainCamera = Camera.main;
                _cameraProbeTimer = CameraReProbeIntervalSeconds;
            }

            float target = _mainCamera != null
                ? CameraDistanceLod.ScaleForDistance(Vector3.Distance(_mainCamera.transform.position, transform.position))
                : 1f;

            _cameraLodScale = Mathf.MoveTowards(_cameraLodScale, target, CameraDistanceLod.MaxScaleChangePerSecond * deltaTime);
        }

        private void LateUpdate()
        {
            if (!UnityEngine.Application.isPlaying || Context == null) return;
            if (!_runtimeInitialized || _inert) return;

            ConvaiBodyLanguageProfile profile = EffectiveProfile;
            if (profile == null || !_poseCompositor.IsBound) return;

            float deltaTime = Time.deltaTime;

            // Starts this frame's write protocol: unwinds last frame's writes ONCE, for the
            // whole chain, before either solver's output is accumulated — see
            // ProceduralPoseCompositor.BeginFrame. Neither solver touches a Transform anymore;
            // the compositor is now the single writer.
            _poseCompositor.BeginFrame();

            // Camera-distance amplitude LOD: resolved before the
            // amplitudes it scales (sway below, hand-micro weight further down) are computed.
            UpdateCameraDistanceLod(profile, deltaTime);

            // Adaptive-layering estimate: reads this frame's
            // just-restored animated/static base pose off the compositor — a talking/reacting
            // body (or a masterWeight-suppressed one) is excluded via StateDuckWeight/the
            // suppression carve-out below so it never pollutes the estimate.
            float breathDuckWeight = StateDuckWeight(_lastState) * (_postureSuppressionWeight < 1f ? 0f : 1f);
            _breathMotionEstimator.Tick(
                _poseCompositor.AnimatedTorsoLocalRotation, _poseCompositor.HasAnimatedPoseSample,
                breathDuckWeight, deltaTime);

            // Sustained posture silhouette gets HALF-STRENGTH amplitude coupling:
            // a Subtle character still holds its openness/lean/tension "shape" — only the
            // TRANSIENT motion (posture pulses, listening lean-in, lateral weight-shift, all
            // folded in below) gets the full gain.
            float sustainedAmplitudeGain = Mathf.Lerp(1f, _amplitudeGain, 0.5f);

            var postureInput = new PostureSolveInput
            {
                DeltaTime = deltaTime,
                // Sustained/transient posture-source separation: openness and tension
                // are 100% sustained (state-policy + emotion bias, from PostureDirector) — the
                // solver floors their effective weight under suppression instead of ducking them
                // to zero with the transient channels.
                OpennessTarget = _postureDirector.OpennessTarget,
                // Lean is split at the source: the sustained state+emotion bias survives
                // suppression at LeanSustainFloor, while the posture-pulse envelope (Gesticulation)
                // and the listening lean-in bias are fully
                // transient and ducks with _postureSuppressionWeight like today. The solver
                // combines and clamps both together (double-lean guard) before scaling —
                // PostureSolver's own spring still smooths the combined target, so neither a beat
                // nor a listening engage/decay ever pops the posture (no separate bone writes, no
                // second guard).
                SustainedLeanTarget = _postureDirector.LeanTarget,
                TransientLeanTarget = _posturePulseValue + _listeningPostureDirector.LeanInBias,
                TensionTarget = _postureDirector.TensionTarget,
                // Lateral weight-shift (fidget program + Thinking asymmetry) is entirely
                // transient — it ducks fully under suppression, no sustain floor.
                LateralShiftTarget = _lateralShiftValue,
                // MasterWeight is fade/enable only (enable ramp, disable, FullBody suppression
                // fade) — it NEVER carries UpperBody suppression anymore; SuppressionWeight below
                // is the transient-only duck that the sustain floors exempt the sustained
                // channels from. This is the "posture at reduced weight, breath
                // stays" now further refined so posture's SUSTAINED silhouette also stays,
                // reduced only to its floor, while its TRANSIENT motion still ducks fully.
                MasterWeight = _masterWeight,
                SuppressionWeight = _postureSuppressionWeight,
                OpennessSustainFloor = OpennessSustainFloor,
                LeanSustainFloor = LeanSustainFloor,
                TensionSustainFloor = TensionSustainFloor,
                MaxOpennessDegrees = ScaleByGain(profile.MaxOpennessDegrees, sustainedAmplitudeGain),
                MaxLeanDegrees = ScaleByGain(profile.MaxLeanDegrees, sustainedAmplitudeGain),
                MaxTensionDegrees = ScaleByGain(profile.MaxTensionDegrees, sustainedAmplitudeGain),
                // Fidget/thinking weight-shift is transient MOTION, not sustained silhouette —
                // full amplitude gain, unlike the half-strength sustained channels above.
                MaxLateralShiftDegrees = ScaleByGain(profile.MaxLateralShiftDegrees, _amplitudeGain),
                SpringSharpness = profile.PostureSpringSharpness,
                MaxAngularSpeedDegreesPerSecond = profile.PostureMaxAngularSpeed
            };
            _postureSolver.Solve(in postureInput);

            // Adaptive layering: duck the procedural breath depth against a
            // baked idle-clip breathing estimate — off ⇒ the multiplier is exactly 1f, leaving
            // the director's own depth untouched.
            float breathDepth = _breathingDirector.Depth *
                (profile.EnableBreathAdaptiveLayering ? _breathMotionEstimator.DuckFactor : 1f);

            var breathInput = new BreathSolveInput
            {
                DeltaTime = deltaTime,
                // Breath RATE is NEVER scaled by expressiveness: it is physiology,
                // not performance.
                RateCpm = _breathingDirector.RateCpm,
                Depth = breathDepth,
                Irregularity = _breathingDirector.Irregularity,
                EventKind = _breathingDirector.ActiveEvent,
                MasterWeight = _masterWeight,
                // Breath chest/shoulder/lateral degrees all derive from these two max fields
                // (ChestLateralDegrees is a share of MaxChestExpansionDegrees) — gaining both here
                // propagates the amplitude gain to all three shaped outputs.
                MaxChestExpansionDegrees = ScaleByGain(profile.MaxBreathChestExpansionDegrees, _amplitudeGain),
                MaxShoulderLiftDegrees = ScaleByGain(profile.MaxBreathShoulderLiftDegrees, _amplitudeGain)
            };
            _breathSolver.Solve(in breathInput);

            // Stance: pelvis lateral offset + obliquity + yaw, spine counter-curve,
            // feet pinned by the compositor's own leg pass (TwoBoneLegSolver) when the leg chain
            // resolves and compensation is enabled. When compensation is unavailable the fed
            // offset is clamped to a small skinning-safe travel instead — the compositor applies
            // whatever it is given (single responsibility). Also requires
            // the leg chain NOT be near full extension this frame — a T-pose/straight-leg rig's
            // bend plane is numerically meaningless, so compensation is unavailable exactly as if
            // the chain had not resolved at all, capping the pelvis offset below and never
            // running the solver (the compositor enforces this same gate independently too — see
            // ProceduralPoseCompositor.ApplyPelvis).
            bool legCompensationActive = profile.EnableLegCompensation && _poseCompositor.HasLegChain &&
                !_poseCompositor.LegChainNearFullExtension;
            float pelvisOffsetCm = ScaleByGain(profile.MaxPelvisOffsetCentimeters, _amplitudeGain);
            if (!legCompensationActive) pelvisOffsetCm = Mathf.Min(pelvisOffsetCm, LegFreePelvisOffsetCapCentimeters);
            _poseCompositor.LegCompensationEnabled = legCompensationActive;
            _legCompensationActiveThisFrame = legCompensationActive;

            float maxPelvisObliquityDegreesGained = ScaleByGain(profile.MaxPelvisObliquityDegrees, _amplitudeGain);

            if (profile.EnableWeightShifts)
            {
                // Stance pre-load (anticipation): obliquity leads the lateral
                // travel by the pre-load window, so the pelvis obliquity argument is fed from
                // PelvisObliquity01 (leading), not PelvisLateral01 (which the pelvis lateral
                // translation and yaw arguments still correctly use).
                _poseCompositor.AddPelvis(
                    _stanceDirector.PelvisLateral01 * pelvisOffsetCm * 0.01f * _masterWeight,
                    _stanceDirector.PelvisObliquity01 * maxPelvisObliquityDegreesGained * _masterWeight,
                    _stanceDirector.PelvisYaw01 * ScaleByGain(profile.MaxPelvisYawDegrees, _amplitudeGain) * _masterWeight);
            }

            // Sway + stance counter-curve fold into the one spine-chain call below.
            // Idle macro-cycle: this tick's FRESH Energy01 (the Cognition tick's
            // _macroCycleDirector.Tick call above already ran this frame, unlike breath/fidget's
            // one-tick-deferred reads) nudges sway amplitude ±15%. Composes with the arousal
            // coherence multiplier (0.85 + 0.3*arousal — exactly 1 at
            // neutral arousal) and the camera-distance LOD scale (exactly 1 when off/no camera),
            // so a character with neither active is left entirely unscaled.
            float maxSwayDegreesGained =
                ScaleByGain(profile.MaxSwayDegrees, _amplitudeGain) *
                (1f + 0.15f * _macroCycleDirector.Energy01) *
                (0.85f + 0.3f * _arousalLevel) *
                _cameraLodScale;
            float swaySagittal = profile.EnableAmbientSway
                ? _swayDirector.SwaySagittal01 * maxSwayDegreesGained * _masterWeight
                : 0f;
            float swayLateral = profile.EnableAmbientSway
                ? _swayDirector.SwayLateral01 * maxSwayDegreesGained * _masterWeight
                : 0f;
            float stanceCounterLateral = profile.EnableWeightShifts
                ? _stanceDirector.SpineCounterLateral01 * maxPelvisObliquityDegreesGained * 1.2f * _masterWeight
                : 0f;

            // Reactions: a startle flinch straightens the spine (negative
            // sagittal) and jumps the shoulders; an amused bounce adds a light positive chest
            // bounce. Routed through the compositor's BALLISTIC spine lane —
            // still the same one guarded write per bone, but filtered under the fast gestural
            // caps so the tonic lane's postural limits never blunt a flinch's attack. Only one
            // of Flinch/Bounce is ever non-zero at a time (ReactionDirector plays at most one
            // reaction), and both already scale by their own envelope's intensity; the master
            // weight is applied here since, unlike the posture and breath solvers, this
            // director has no MasterWeight input of its own.
            float reactionFlinchDegrees = profile.EnableReactions
                ? _reactionDirector.FlinchValue * ScaleByGain(profile.MaxFlinchDegrees, _amplitudeGain) * _masterWeight
                : 0f;
            float reactionBounceDegrees = profile.EnableReactions
                ? _reactionDirector.BounceValue * ScaleByGain(profile.MaxAmusementBounceDegrees, _amplitudeGain) * _masterWeight
                : 0f;

            // Tonic lane: slow postural motion (posture, breath, sway, stance counter-curve).
            _poseCompositor.AddSpineChainSwing(
                _breathSolver.ChestSagittalDegrees + swaySagittal,
                _postureSolver.SpineLateralDegrees + _breathSolver.ChestLateralDegrees + swayLateral + stanceCounterLateral);
            _poseCompositor.AddPostureSilhouette(_postureSolver.OpennessDegrees, _postureSolver.LeanDegrees);
            // Ballistic lane: reaction flinch/bounce transients.
            _poseCompositor.AddSpineChainSwingBallistic(-reactionFlinchDegrees + reactionBounceDegrees, 0f);
            _poseCompositor.AddShoulderLift(_breathSolver.ShoulderLiftDegrees);
            // Shrug: a one-shot procedural shoulder lift, additive to the breath
            // lift above — ballistic lane (fast gestural transient), summed with the tonic
            // breath lift by the compositor after filtering. Richness-gated TOO
            // ("richness gates repertoire") — at Subtle (richness 0) the shrug vanishes entirely.
            _poseCompositor.AddShoulderLiftBallistic(
                _gesticulationDirector.ShrugValue * ScaleByGain(profile.MaxShrugDegrees, _amplitudeGain) * _richnessGain * _masterWeight);
            // Startle shoulder jump: shoulders jump with the flinch, half its
            // spine amplitude — reactionFlinchDegrees already carries the master weight and gain.
            _poseCompositor.AddShoulderLiftBallistic(reactionFlinchDegrees * 0.5f);
            _poseCompositor.AddShoulderTension(_postureSolver.ShoulderTensionDegrees);
            // Head stabilization against breath: counter-pitches the
            // head/neck against the breath's sagittal chest swing so the head reads level while
            // the ribcage moves. Not independently gained — it already tracks the (gained)
            // ChestSagittalDegrees it stabilizes against. Self-actuates ONLY when nothing has
            // registered on the head-gesture channel — mirrors ApplyHeadGestureFallback's own
            // gate below ("applied by Gaze when present, by the compositor fallback
            // when not"). A registered consumer (Gaze) already counters animated head/torso
            // deviation — including breath — as part of its own head tracking; the compositor
            // must never double-write Head/Neck once a consumer owns them.
            // Gesture-ducked: a counter-pitch composed under an
            // active head gesture (e.g. a nod) reads as the stabilization fighting the gesture, so
            // it fades out to the extent a gesture is currently playing (full weight while no
            // gesture, none while one is at full weight). TryGetOffset is a pure read of the
            // director's current state — this peek never itself actuates anything.
            if (_headGestureChannel.ConsumerCount == 0)
            {
                float gestureDuck = 1f;
                if (_headGestureChannel.TryGetOffset(out HeadGestureOffset gestureOffset))
                    gestureDuck = 1f - Mathf.Clamp01(gestureOffset.Weight);
                _poseCompositor.AddBreathHeadStabilization(
                    _breathSolver.ChestSagittalDegrees, profile.BreathHeadStabilization * gestureDuck);
            }

            ApplyHeadGestureFallback(profile);
            _poseCompositor.ApplyAccumulated(deltaTime);

            // Idle hand/wrist micro-life: own independent guard, own weight gate
            // — state-scaled, master-weight-scaled, and ducked by the same occupancy the
            // continuous posture duck above uses, so authored talk gestures never fight it.
            // Richness-gated ("richness gates repertoire") — at Subtle (richness 0)
            // hand micro-life is fully absent; clamped to 0..1 since this is a gate/weight,
            // not an amplitude (its own degree
            // amplitude is gained separately below via MaxFingerCurlDegrees/MaxWristMicroDegrees).
            // Camera-distance LOD multiplies this weight target too —
            // HandMicroSolver.Tick re-clamps its own weight01 to 0..1, so the LOD's up-to-1.3×
            // far-camera boost never needs a second clamp here.
            float handMicroTarget = profile.EnableHandMicro
                ? _masterWeight * (1f - _occupancySmoothed) * StateHandMicroWeight(_lastState) * Mathf.Clamp01(_richnessGain) * _cameraLodScale
                : 0f;
            _handMicroWeight = Mathf.MoveTowards(
                _handMicroWeight, handMicroTarget, HandMicroWeightSlewPerSecond * deltaTime);
            _handMicroSolver.MaxFingerCurlDegrees = ScaleByGain(profile.MaxFingerCurlDegrees, _amplitudeGain);
            _handMicroSolver.MaxWristMicroDegrees = ScaleByGain(profile.MaxWristMicroDegrees, _amplitudeGain);
            _handMicroSolver.GestureAmplitudeScale = profile.ProceduralGestureAmplitude * _amplitudeGain;
            _coSpeechArmSolver.AmplitudeScale = profile.ProceduralGestureAmplitude * _amplitudeGain;
            GestureSuppression handSuppression = ResolveSuppression(
                Context.ConversationalGesturePerformer, Context.ConversationalMotionBudget);
            float handActuationWeight = _handMicroSolver.IsGestureActive && handSuppression == GestureSuppression.None
                ? _masterWeight
                : _coSpeechArmSolver.IsActive ? 0f : _handMicroWeight;
            _handMicroSolver.Tick(Time.time, handActuationWeight, deltaTime);
            _coSpeechArmSolver.Tick(deltaTime,
                handSuppression == GestureSuppression.None ? _masterWeight : 0f);

            RefreshCurrentReading();

            TraceFirehose(deltaTime);
        }

        /// <summary>
        ///     Republishes <see cref="Current" /> (and, through it,
        ///     <see cref="EmbodimentContext.BodyLanguageSource" />) from this frame's
        ///     already-computed director/solver state — no new per-tick allocation (a value-type
        ///     struct assignment only; every field is a primitive/enum already held in a
        ///     controller field or cheap director property).
        /// </summary>
        private void RefreshCurrentReading()
        {
            // BreathSolver.Phase is radians in [0, 2π); BodyLanguageReading documents breath
            // phase as normalized [0, 1) (Domain stays UnityEngine-Mathf-free), so it is
            // rescaled here rather than exposing the radian convention outside the module.
            float normalizedBreathPhase = _breathSolver.Phase / (2f * Mathf.PI);

            _currentReading = new BodyLanguageReading(
                _lastState,
                _postureSolver.Openness,
                _postureSolver.Lean,
                _postureSolver.Tension,
                normalizedBreathPhase,
                Context?.ConversationalGesturePerformer?.CurrentSuppression ?? GestureSuppression.None,
                _headGestureDirector.IsPlaying,
                _headGestureDirector.ActiveKind,
                _gesticulationDirector.LastCueKind,
                _stanceDirector.PelvisLateral01,
                _effectiveExpressiveness,
                _reactionDirector.ActiveReaction);
        }

        /// <summary>
        ///     No-consumer head-gesture fallback: when nothing has registered on the
        ///     head-gesture channel, Body Language self-actuates the active program's offset by
        ///     accumulating it onto the shared <see cref="_poseCompositor" />'s head-gesture
        ///     channel instead of merely publishing it for a consumer to compose. This is still
        ///     the <see cref="EmbodimentExecutionOrders.BodyPose" /> slot's single
        ///     restore-once-per-frame protocol — the compositor writes Neck/Head once, together
        ///     with every other channel, never a second guard instance. Conservative hardcoded
        ///     limits (independent of the profile's own amplitude maxima, which already produce
        ///     small angles, but the fallback path additionally caps pitch/yaw to ±12° and roll
        ///     to ±8° since nothing downstream re-clamps a self-actuated head: a registered
        ///     consumer (e.g. Gaze) limit-compresses the channel's
        ///     offset itself, so this second cap only matters when self-actuating. Also feeds the
        ///     director's neck-lead offset onto the compositor's explicit neck
        ///     channel — proximal-to-distal sequencing (neck initiates, head follows) even in the
        ///     no-consumer fallback path.
        /// </summary>
        private void ApplyHeadGestureFallback(ConvaiBodyLanguageProfile profile)
        {
            const float fallbackMaxPitchYawDegrees = 12f;
            const float fallbackMaxRollDegrees = 8f;

            _headGestureFallbackActiveThisFrame = false;

            if (_headGestureChannel.ConsumerCount > 0 || !_poseCompositor.HasHeadChain)
            {
                _headGestureFallbackLogged = false;
                return;
            }

            if (!_headGestureChannel.TryGetOffset(out HeadGestureOffset offset) || offset.Weight <= 0f)
                return;

            _headGestureFallbackActiveThisFrame = true;

            if (!_headGestureFallbackLogged)
            {
                _headGestureFallbackLogged = true;
                if (_trace != null && _trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                    _trace.State("No head-gesture consumer — self-actuating conservatively.");
            }

            float weight = Mathf.Clamp01(offset.Weight) * Mathf.Clamp01(_masterWeight);
            float pitch = Mathf.Clamp(offset.PitchDegrees, -fallbackMaxPitchYawDegrees, fallbackMaxPitchYawDegrees) * weight;
            float yaw = Mathf.Clamp(offset.YawDegrees, -fallbackMaxPitchYawDegrees, fallbackMaxPitchYawDegrees) * weight;
            float roll = Mathf.Clamp(offset.RollDegrees, -fallbackMaxRollDegrees, fallbackMaxRollDegrees) * weight;

            if (Mathf.Abs(pitch) >= 1e-5f || Mathf.Abs(yaw) >= 1e-5f || Mathf.Abs(roll) >= 1e-5f)
                _poseCompositor.AddHeadGesture(pitch, yaw, roll);

            // Neck-lead: the channel wraps the director, so this stays a
            // channel read (TryGetNeckLeadOffset mirrors TryGetOffset) rather than a direct
            // director reference. Clamped/weighted EXACTLY like the head offset above (same
            // fallback caps, same master weight), then explicitly shared onto the compositor's
            // own neck channel via AddNeckGesture — NeckGestureShare is the single constant the
            // compositor already owns (made internal for this one reference rather than mirrored
            // here a second time, the lower-churn option since the module already has
            // InternalsVisibleTo access to the Runtime assembly).
            if (_headGestureChannel.TryGetNeckLeadOffset(out HeadGestureOffset neckLead) && neckLead.Weight > 0f)
            {
                float neckWeight = Mathf.Clamp01(neckLead.Weight) * Mathf.Clamp01(_masterWeight);
                float neckPitch = Mathf.Clamp(neckLead.PitchDegrees, -fallbackMaxPitchYawDegrees, fallbackMaxPitchYawDegrees) * neckWeight;
                float neckYaw = Mathf.Clamp(neckLead.YawDegrees, -fallbackMaxPitchYawDegrees, fallbackMaxPitchYawDegrees) * neckWeight;
                float neckRoll = Mathf.Clamp(neckLead.RollDegrees, -fallbackMaxRollDegrees, fallbackMaxRollDegrees) * neckWeight;

                if (Mathf.Abs(neckPitch) >= 1e-5f || Mathf.Abs(neckYaw) >= 1e-5f || Mathf.Abs(neckRoll) >= 1e-5f)
                    _poseCompositor.AddNeckGesture(
                        neckPitch * ProceduralPoseCompositor.NeckGestureShare,
                        neckYaw * ProceduralPoseCompositor.NeckGestureShare,
                        neckRoll * ProceduralPoseCompositor.NeckGestureShare);
            }
        }

        /// <summary>
        ///     Per-tick numeric dump, throttled to a readable cadence. The verbosity gate runs
        ///     BEFORE the interpolated message is built, so below Firehose this method costs a
        ///     branch and allocates nothing (mirrors the gaze controller's TraceFirehose).
        /// </summary>
        private void TraceFirehose(float deltaTime)
        {
            if (_trace == null || _trace.Verbosity < BodyLanguageTraceVerbosity.Firehose) return;

            _firehoseTimer += deltaTime;
            if (_firehoseTimer < FirehoseIntervalSeconds) return;
            _firehoseTimer = 0f;

            _trace.Firehose(
                $"Posture openness={_postureSolver.Openness:0.00} lean={_postureSolver.Lean:0.00} " +
                $"tension={_postureSolver.Tension:0.00} lateral={_postureSolver.LateralShift:0.00} " +
                $"weight={_masterWeight:0.00} breathPhase={_breathSolver.Phase:0.00} breathWave={_breathSolver.Waveform:0.00}");
        }

        /// <summary>Fills <paramref name="snapshot" /> with the live body language state.</summary>
        public void CaptureSnapshot(BodyLanguageSnapshot snapshot)
        {
            if (snapshot == null) return;

            snapshot.Clear();
            snapshot.DialogueState = Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            snapshot.ActivePolicy = _policy.Current;
            snapshot.TargetPolicy = _policy.Target;
            snapshot.IsInert = _inert;
            snapshot.HasSpine = _hasSpine;
            snapshot.HasChest = _hasChest;
            snapshot.HasUpperChest = _hasUpperChest;
            snapshot.HasShoulders = _hasLeftShoulder && _hasRightShoulder;
            snapshot.HasProceduralArmChain = _handMicroSolver.HasArmChain;
            snapshot.HasProceduralFingerChain = _handMicroSolver.HasFingers;
            ConvaiBodyLanguageProfile profile = EffectiveProfile;
            snapshot.ProfileName = profile != null ? profile.name : "-";
            snapshot.PostureOpennessTarget = _postureDirector.OpennessTarget;
            snapshot.PostureLeanTarget = _postureDirector.LeanTarget;
            snapshot.PostureTensionTarget = _postureDirector.TensionTarget;
            snapshot.PostureOpennessCurrent = _postureSolver.Openness;
            snapshot.PostureLeanCurrent = _postureSolver.Lean;
            snapshot.PostureTensionCurrent = _postureSolver.Tension;
            snapshot.MasterWeight = _masterWeight;
            snapshot.BreathPhase = _breathSolver.Phase;
            snapshot.BreathWaveform = _breathSolver.Waveform;
            snapshot.BreathRateCpm = _breathingDirector.RateCpm;
            snapshot.BreathDepth = _breathingDirector.Depth;
            snapshot.BreathBakedAmplitudeDegrees = _breathMotionEstimator.BakedAmplitudeDegrees;
            snapshot.BreathDuckFactor = _breathMotionEstimator.DuckFactor;
            snapshot.HeadGestureIsPlaying = _headGestureDirector.IsPlaying;
            snapshot.HeadGestureActiveKind = _headGestureDirector.ActiveKind;
            snapshot.HeadGestureProgress = _headGestureDirector.ActiveProgress;
            snapshot.HeadGestureConsumerCount = _headGestureChannel.ConsumerCount;
            snapshot.HeadGestureFallbackActive = _headGestureFallbackActiveThisFrame;
            snapshot.PostureSuppressionWeight = _postureSuppressionWeight;
            snapshot.GesticulationSuppression = Context?.ConversationalGesturePerformer?.CurrentSuppression ?? GestureSuppression.None;
            snapshot.LastGestureCueKind = _gesticulationDirector.LastCueKind;
            snapshot.LastGestureCueAccepted = _gesticulationDirector.LastCueAccepted;
            snapshot.ProceduralGestureFallbackActive = _handMicroSolver.IsGestureActive;
            snapshot.GesticulationStatisticalCadenceActive = _gesticulationDirector.IsStatisticalCadenceActive;
            snapshot.GesticulationPosturePulseValue = _gesticulationDirector.PosturePulseValue;
            snapshot.PostureLateralShiftTarget = _lateralShiftValue;
            snapshot.PostureLateralShiftCurrent = _postureSolver.LateralShift;
            snapshot.FidgetWeightShift = _fidgetDirector.WeightShiftValue;
            snapshot.ListeningLeanIn = _listeningPostureDirector.LeanInBias;
            snapshot.ListeningStillnessFactor = _listeningPostureDirector.StillnessFactor;
            snapshot.ListeningWantsTiltHold = _listeningPostureDirector.WantsTiltHold;
            snapshot.StanceLateral = _stanceDirector.PelvisLateral01;
            snapshot.StanceIsShifting = _stanceDirector.IsShifting;
            snapshot.SwaySagittal = _swayDirector.SwaySagittal01;
            snapshot.SwayLateral = _swayDirector.SwayLateral01;
            snapshot.MacroCycleEnergy = _macroCycleDirector.Energy01;
            snapshot.ArousalLevel = _arousalLevel;
            snapshot.CameraLodScale = _cameraLodScale;
            snapshot.LegCompensationActive = _legCompensationActiveThisFrame;
            IConversationalMotionBudget budget = Context?.ConversationalMotionBudget;
            snapshot.UsingMotionBudget = budget != null;
            snapshot.UpperBodyOccupancy = budget?.UpperBodyOccupancy01 ?? 0f;
            snapshot.ActiveReactionKind = _reactionDirector.ActiveReaction;
            snapshot.ReactionFlinch = _reactionDirector.FlinchValue;
            snapshot.ReactionBounce = _reactionDirector.BounceValue;
            snapshot.Expressiveness = _effectiveExpressiveness;
            snapshot.AmplitudeGain = _amplitudeGain;
            snapshot.FrequencyGain = _frequencyGain;
            snapshot.RichnessGain = _richnessGain;

            // Motion meter: the compositor's post-cap applied spine swing, the
            // breath solver's own pre-composition sagittal output, and this tick's stance
            // pelvis travel — mirrors the LateUpdate math without duplicating the master-weight
            // gate logic (0 when the corresponding channel is disabled/unbound).
            snapshot.SternumLeverMeters = _poseCompositor.SternumLeverMeters;
            snapshot.AppliedSpineSagittalDegrees = _poseCompositor.AppliedSpineSagittalDegrees;
            snapshot.AppliedSpineLateralDegrees = _poseCompositor.AppliedSpineLateralDegrees;
            snapshot.BreathAppliedSagittalDegrees = _breathSolver.ChestSagittalDegrees;
            if (profile != null && profile.EnableWeightShifts)
            {
                float pelvisOffsetCmForSnapshot = ScaleByGain(profile.MaxPelvisOffsetCentimeters, _amplitudeGain);
                float pelvisObliquityForSnapshot = ScaleByGain(profile.MaxPelvisObliquityDegrees, _amplitudeGain);
                snapshot.StanceLateralCentimeters = _stanceDirector.PelvisLateral01 * pelvisOffsetCmForSnapshot * _masterWeight;
                snapshot.StanceObliquityDegrees = _stanceDirector.PelvisObliquity01 * pelvisObliquityForSnapshot * _masterWeight;
            }

            _trace?.CopyRecentEntries(snapshot.RecentTrace);
        }

        /// <summary>Allocating convenience overload of <see cref="CaptureSnapshot(BodyLanguageSnapshot)" />.</summary>
        public BodyLanguageSnapshot CaptureSnapshot()
        {
            var snapshot = new BodyLanguageSnapshot();
            CaptureSnapshot(snapshot);
            return snapshot;
        }

        private void HandleRigBindingChanged(IStandardRigBinding rigBinding)
        {
            // Rebind support: recalibrate weights and reset solver state so the first tick
            // after a rebind produces no residual delta from the old rig. As in OnDisable, the
            // shared guard must be restored BEFORE the compositor rebinds or either solver
            // resets its own state — the guard's cached post-write value only matches this
            // frame's final composite before Bind/Reset() clears it.
            _poseCompositor.RestoreStaleWrites();
            _poseCompositor.Bind(Context?.EnsureRigBinding());
            ValidateRig();
            _handMicroAnimator = GetComponentInChildren<Animator>(true);
            _handMicroSolver.Reset();
            _handMicroSolver.Bind(_handMicroAnimator);
            _coSpeechArmSolver.Reset();
            _coSpeechArmSolver.Bind(_handMicroAnimator);
            _lastCoSpeechGestureSequence = 0;
            _postureSolver.Reset();
            _breathSolver.Reset();
            _breathMotionEstimator.Reset();
            _masterWeight = 0f;
            _trace?.State("Rig binding changed — body language bones re-probed and recalibrated.");
        }

        private void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized) return;

            ConvaiBodyLanguageProfile p = EffectiveProfile;
            if (p == null) return;

            _trace ??= new BodyLanguageTrace(name);
            _trace.Verbosity = p.TraceVerbosity;

            _poseCompositor.Bind(Context?.EnsureRigBinding());
            ValidateRig();
            // Hand micro-life needs the Animator directly (HumanBodyBones wrist/finger lookup);
            // IStandardRigBinding has no Animator accessor, so resolve it the same way
            // AnimatorConductor and ConvaiBodyAnimationController do.
            _handMicroAnimator = GetComponentInChildren<Animator>(true);
            _handMicroSolver.Bind(_handMicroAnimator);
            _coSpeechArmSolver.Bind(_handMicroAnimator);
            _breathSolver.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0xB0DE1A17u));
            // Fresh, deliberate salt distinct from breath's 0xB0DE1A17u — "GEST1CULATE" read as
            // hex-safe digits, kept in the same house style of a memorable constant per director.
            _gesticulationDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0x6E57C01Au));
            // Two more fresh, distinct salts (house style: a memorable hex-safe constant per
            // director) — "F1DGET" for the fidget weight-shift program and "LISTEN" for the
            // listening tilt-hold cadence.
            _fidgetDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0xF1D6E7DEu));
            _listeningPostureDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0x715E7E57u));
            // Two more fresh, distinct salts: the stance director's own
            // fallback constant, and a memorable "SWAY" digit-safe constant for the sway director.
            _stanceDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0x57A2CEDAu));
            _swayDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0x5A755AEDu));
            // Fresh, distinct salt (idle presence — "CYCLES", hex-safe digits)
            // for the idle macro-cycle director.
            _macroCycleDirector.Seed(DeterministicEmbodimentRandom.CreateSeed(this, 0xC1C1E5EEu));
            _speechPulseAnalyzer = new SpeechPulseAnalyzer(p.BuildSignalConfig());
            _masterWeight = 0f;

            if (_trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                _trace.State(
                    $"Body language runtime initialized. inert={_inert} spine={_hasSpine} chest={_hasChest} " +
                    $"upperChest={_hasUpperChest} shoulders={_hasLeftShoulder && _hasRightShoulder} " +
                    $"transition={p.PolicyTransitionSeconds:0.00}s emotionModulation={p.EnableEmotionModulation}");

            _runtimeInitialized = true;
        }

        /// <summary>
        ///     Reads this enable/rebind's inertness and rig-shape flags off the already-bound
        ///     <see cref="_poseCompositor" /> (the caller binds it first — see
        ///     <see cref="EnsureRuntimeInitialized" />/<see cref="HandleRigBindingChanged" />):
        ///     Spine is required (missing ⇒ inert with a single error); Chest/UpperChest/
        ///     Shoulders are optional. Also logs the spine-chain redistribution and shoulder-channel
        ///     notices at bind time, because the compositor itself never logs. Never logs per frame.
        /// </summary>
        private void ValidateRig()
        {
            bool wasInert = _inert;

            _hasSpine = _poseCompositor.IsBound;
            _hasChest = _poseCompositor.HasChest;
            _hasUpperChest = _poseCompositor.HasUpperChest;
            _hasLeftShoulder = _poseCompositor.LeftShoulder != null;
            _hasRightShoulder = _poseCompositor.RightShoulder != null;

            if (Context?.EnsureRigBinding() == null)
            {
                _inert = true;
                if (!wasInert)
                    _trace?.Error(
                        "[ConvaiBodyLanguageController] No rig binding could be resolved. Body language " +
                        "needs a Humanoid character with a StandardRigBinding (added automatically for " +
                        "Humanoid Animators). The module stays inert.");
                return;
            }

            if (!_hasSpine)
            {
                _inert = true;
                if (!wasInert)
                    _trace?.Error(
                        "[ConvaiBodyLanguageController] Rig binding has no Spine bone. Check that the " +
                        "Animator avatar is Humanoid and the spine chain is mapped; the module stays " +
                        "inert until a Spine bone exists.");
                return;
            }

            _inert = false;

            if (_trace != null && _trace.Verbosity >= BodyLanguageTraceVerbosity.State)
            {
                if (!_hasChest || !_hasUpperChest)
                    _trace.State(
                        $"Posture/breath redistribution — chest={_hasChest} upperChest={_hasUpperChest}: " +
                        $"spine={_poseCompositor.SpineWeight:0.00} chest={_poseCompositor.ChestWeight:0.00} " +
                        $"upperChest={_poseCompositor.UpperChestWeight:0.00}.");
                if (!(_hasLeftShoulder && _hasRightShoulder))
                    _trace.State("Shoulder tension channel disabled — one or both shoulder bones are missing.");
            }
        }
    }
}
