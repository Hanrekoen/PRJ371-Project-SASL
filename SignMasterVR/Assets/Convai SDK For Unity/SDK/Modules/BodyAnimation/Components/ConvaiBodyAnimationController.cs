using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Core;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Core.Layers;
using Convai.Modules.BodyAnimation.Core.Lifecycle;
using Convai.Modules.BodyAnimation.Core.Locomotion;
using Convai.Modules.BodyAnimation.Core.Policy;
using Convai.Modules.BodyAnimation.Core.Performance;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Components
{
    /// <summary>
    ///     The Convai body animation system: a fully code-driven, layered PlayableGraph that
    ///     plays idle/talk variants, NavMesh-synced locomotion, backend-triggered actions and
    ///     gestures, and directional pointing — no Animator Controller asset required. Content
    ///     comes from a <see cref="ConvaiBodyAnimationSet" />, behavior from a
    ///     <see cref="ConvaiBodyAnimationConfig" />, both optionally routed through a
    ///     <see cref="ConvaiBodyAnimationProfile" />.
    /// </summary>
    /// <remarks>
    ///     The component is a thin composition root: it owns the graph lifetime, resolves the
    ///     humanoid <see cref="Animator" />, builds the layer stack, and forwards embodiment
    ///     ticks. All behavior lives in the internal layer classes. Diagnostics: every
    ///     transition is traced (see <see cref="ConvaiBodyAnimationConfig.TraceVerbosity" />)
    ///     and mirrored to <see cref="StateChanged" />; call <see cref="CaptureSnapshot()" />
    ///     for a full live view.
    /// </remarks>
    [EmbodimentModule(ModuleIds.BodyAnimation, "Body Animation",
        Description = "Gestures, idles and walking, played from an animation set.",
        Absence = "the character plays no gestures, idles or walk cycles of its own.",
        Order = 30)]
    [AddComponentMenu("Convai/Embodiment/Body Animation")]
    [DisallowMultipleComponent]
    public sealed partial class ConvaiBodyAnimationController :
        ConvaiCharacterModule<ConvaiBodyAnimationProfile>,
        IEmbodimentTickable,
        ICharacterReorientationHandler,
        IExertionSource,
        IAnchorMovementDrive
    {
        // No [Header] on serialized fields: ConvaiBodyAnimationControllerEditor groups these into
        // Convai sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Animation content for this character. A profile (if assigned) overrides this.")]
        private ConvaiBodyAnimationSet _animationSet;

        [SerializeField]
        [Tooltip("Runtime tuning. A profile (if assigned) overrides this; empty falls back to defaults.")]
        private ConvaiBodyAnimationConfig _config;

        [SerializeField]
        [Tooltip("Explicit Animator target. When empty, the first Animator in children is used.")]
        private Animator _animatorOverride;

        [SerializeField]
        [Tooltip("When enabled and no conversation-flow source exists, the hidden driver is " +
                 "created at runtime so Speaking states drive the talk layer.")]
        private bool _autoCreateConversationFlow = true;

        [SerializeField]
        [Tooltip("Optional custom locomotion provider. It must implement IConvaiLocomotionSource; " +
                 "advanced motion, commands, and anchored alignment are discovered as optional capabilities.")]
        private MonoBehaviour _locomotionProviderOverride;

        [SerializeField]
        [Tooltip("0 = derived automatically from this character's identity (stable across runs). " +
                 "Set a non-zero value to pin the exact sequence of idle/talk variants and ambient " +
                 "activities — useful for reproducing a reported issue.")]
        private int _randomSeed;

        private readonly List<IAnimationLayer> _layers = new(LayerPorts.Count);
        private readonly List<AnimTraceEntry> _traceScratch = new(AnimTrace.Capacity);
        private readonly List<(string slot, LocomotionClip clip)> _motionScaleScratch = new();
        private readonly List<string> _inertFeatureNamesScratch = new();
        private BodyAnimationFeatureAvailability _featureAvailability;

        private AnimationGraphHost _graphHost;
        private LayerMixerHost _mixerHost;
        private LayerRuntime _layerRuntime;
        private LocomotionLayer _locomotionLayer;
        private TalkLayer _talkLayer;
        private ActionLayer _actionLayer;
        private PointingLayer _pointingLayer;
        private ConversationalGesturePerformer _gesturePerformer;
        private ReferentialGestureDirector _referentialDirector;
        private CoSpeechPerformancePlanner _coSpeechPlanner;

        // Withdrawal tokens for contracts whose lifetime is narrower than the component's — see the
        // Contracts partial for why each one is held here rather than by the base class.
        private CharacterServiceRegistry.ServiceToken _exertionToken;
        private CharacterServiceRegistry.ServiceToken _coSpeechToken;
        private CharacterServiceRegistry.ServiceToken _gesturePerformerToken;
        private CharacterServiceRegistry.ServiceToken _motionBudgetToken;
        private readonly CoSpeechCoordinator _coSpeechCoordinator = new();
        private AmbientActivityDirector _ambientDirector;
        private ILocomotionDrive _locomotion;
        private Component _locomotionComponent;
        private IConvaiAnchorAlignment _anchorAlignment;
        private readonly ExertionModel _exertionModel = new();
        private readonly SocialSpacingRunner _socialSpacingRunner = new();
        private readonly EmotionalGaitRunner _emotionalGaitRunner = new();
        private readonly ConversationAnchorResolver _anchorResolver = new();
        private PlayActionAtRunner _activePlayActionAtRunner;
        private AnimTrace _trace;
        private Animator _animator;
        private ConvaiBodyAnimationConfig _ownedDefaultConfig;
        private ConvaiBodyAnimationSet _runtimeAnimationSetOverride;
        private bool _hasRuntimeAnimationSetOverride;
        private ConvaiBodyAnimationConfig _runtimeConfigOverride;
        private bool _hasRuntimeConfigOverride;
        private Animator _ownedAnimator;
        private bool _ownedAnimatorApplyRootMotion;
        private bool _animatorStateCaptured;
        private float _firehoseTimer;
        private bool _runtimeBuilt;
        private bool _tickRegistered;
        private bool _dependenciesHooked;
        private readonly BodyAnimationLayerArbiter _layerArbiter = new();
        /// <summary>How long a queued set swap waits before asking blocking owners to yield.</summary>
        private const float SetSwapGraceSeconds = 5f;

        /// <summary>How long a queued set swap waits before it is forced through regardless.</summary>
        private const float SetSwapForceSeconds = 10f;

        /// <summary>How long a deferred first-call request (see below) waits before it expires.</summary>
        private const float DeferredRequestTimeoutSeconds = 2f;

        private bool _setSwapPending;
        private readonly AnimationSetHandoffCoordinator _setHandoffCoordinator = new();
        private ConvaiBodyAnimationSet _builtSet;
        private ConvaiBodyAnimationConfig _builtConfig;
        private float _setSwapQueuedAt;
        private bool _setSwapGraceIssued;

        // First-call safety — a single deferred slot (never a queue) for a request made
        // before the runtime is built. Replayed on the first tick after BuildRuntime succeeds;
        // expires after DeferredRequestTimeoutSeconds with one clear message. A newer request
        // simply overwrites these fields, which is the "replace, don't queue" policy.
        //
        // the identity triplet below (kind/name/queued-at) stays here rather than moving
        // into DeferredRequestSlot — BodyAnimationLifecycleTests fakes "a request is queued"
        // without a scene by setting these three fields directly via reflection, so they must
        // keep existing, by this exact name, on the controller. DeferredRequestSlot owns the
        // payload (the other eight request-specific fields), the enum, the pure expiry check
        // and the description text.
        private DeferredRequestSlot.Kind _deferredKind;
        private string _deferredName;
        private float _deferredQueuedAt;
        private readonly DeferredRequestSlot _deferredRequest = new();

        // Referential gestures subscribe directly to the character's spoken-line feed
        // (same event GazeReferentialGlances uses), independent of the runtime build/rebuild
        // lifecycle — the handler itself is a no-op while the director hasn't been built yet.
        private readonly SpokenLineRelay _spokenLineRelay = new();

        /// <summary>
        ///     Whether the referential-gesture director claimed the spoken line currently being
        ///     consumed. See <see cref="ConsumePendingTranscript" /> for why the co-speech
        ///     planner must not also classify a claimed line.
        /// </summary>
        private bool _referentialCueClaimedThisLine;

        /// <summary>Raised for every animation transition, mirroring the trace log.</summary>
        public event Action<AnimStateChange> StateChanged;

        /// <summary>Raised for every action/gesture lifecycle stage (started, ended, …).</summary>
        public event Action<BodyAnimationActionEvent> ActionEvent;

        /// <summary>
        ///     Raised once per successful <see cref="BuildRuntime" />, after the runtime is
        ///     fully usable — the first zero-delta tick has already run and been evaluated, so a
        ///     handler that calls straight back into <see cref="PlayAction" />/<see cref="PointAt(Vector3,float)" />/
        ///     <see cref="PlayActionAt(Transform,string)" /> from inside this event succeeds
        ///     immediately. This is the documented subscribe-then-call pattern for code that
        ///     wants to guarantee a call lands rather than relying on the single-slot deferred
        ///     replay (which exists as a safety net, not a substitute for this event).
        /// </summary>
        public event Action RuntimeReady;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Expression;

        /// <summary>
        ///     The animation content this character actually plays from: the profile's set when a
        ///     profile is assigned, otherwise the set assigned on this component. <c>null</c> means
        ///     the character has no content yet and will stand still.
        /// </summary>
        public ConvaiBodyAnimationSet AnimationSet => EffectiveAnimationSet;

        /// <summary>
        ///     The tuning this character actually runs on: the profile's config when a profile is
        ///     assigned, otherwise the config assigned on this component, otherwise built-in
        ///     defaults. Never <c>null</c>.
        /// </summary>
        public ConvaiBodyAnimationConfig Config => EffectiveConfig;

        /// <summary>
        ///     Diagnostic read: which default-enabled features (beat gestures, referential
        ///     gestures, ambient activities, gesture brackets, the moving-talk additive tier,
        ///     cue-tagged actions) are actually effective on the built set, and which are enabled
        ///     but have no matching content to act on. Computed once per <see cref="BuildRuntime" />
        ///     — a default instance (every field disabled/without content) before the runtime is
        ///     ever built. Use <see cref="BodyAnimationFeatureAvailability.Compute" /> directly for
        ///     an Edit Mode read with no live runtime.
        /// </summary>
        public BodyAnimationFeatureAvailability FeatureAvailability => _featureAvailability;

        /// <summary>
        ///     True while a <see cref="SetAnimationSet" /> request is queued but has not yet
        ///     begun its crossfade handoff — e.g. waiting on an in-flight action, pointing hold
        ///     or locomotion settle. A HUD can use this to show "swap pending". The swap
        ///     escalates on its own (asks blockers to yield after
        ///     <see cref="SetSwapGraceSeconds" />, forces the handoff after
        ///     <see cref="SetSwapForceSeconds" />) — this property never needs to be polled to
        ///     make progress happen, only to report it.
        /// </summary>
        public bool IsAnimationSetSwapPending => _setSwapPending;

        /// <summary>
        ///     Whether the animation graph is built and ready to take calls. <see cref="PlayAction" />,
        ///     <see cref="PointAt(Vector3,float)" /> and <see cref="PlayActionAt(Transform,string)" />
        ///     made before this is true are held in a single deferred slot and replayed on the first
        ///     tick; subscribing to <see cref="RuntimeReady" /> is the reliable way to call in at
        ///     exactly the right moment.
        /// </summary>
        public bool IsRuntimeBuilt => _runtimeBuilt;

        /// <summary>Animator the graph outputs to (resolved at build time).</summary>
        public Animator TargetAnimator => _animator;

        /// <summary>
        ///     The Animator this character animates through: the one already resolved at build
        ///     time, otherwise the explicit override, otherwise the first Animator in children —
        ///     the same ladder <see cref="ResolveAndValidateAnimator" /> walks, but answerable
        ///     before Play Mode. The module's editor tooling (clip preview, the setup service)
        ///     resolves through this instead of repeating the search, so a character carrying more
        ///     than one Animator can never end up previewing one skeleton while the graph poses
        ///     another.
        /// </summary>
        internal Animator ResolveTargetAnimator()
        {
            if (_animator != null) return _animator;
            return _animatorOverride != null ? _animatorOverride : GetComponentInChildren<Animator>(true);
        }

        private ConvaiBodyAnimationSet EffectiveAnimationSet =>
            _hasRuntimeAnimationSetOverride
                ? _runtimeAnimationSetOverride
                : profile != null && profile.AnimationSet != null
                    ? profile.AnimationSet
                    : _animationSet;

        private ConvaiBodyAnimationConfig EffectiveConfig
        {
            get
            {
                if (_hasRuntimeConfigOverride) return _runtimeConfigOverride;
                if (profile != null && profile.Config != null) return profile.Config;
                if (_config != null) return _config;
                if (_ownedDefaultConfig == null)
                    _ownedDefaultConfig = ConvaiBodyAnimationConfig.CreateDefault();
                return _ownedDefaultConfig;
            }
        }

        /// <summary>
        ///     The config the running tick and every layer actually read. While the
        ///     runtime is built this is the build-time snapshot (<c>_builtConfig</c>) — never the
        ///     live <see cref="EffectiveConfig" />, which can diverge from it the moment the
        ///     config reference changes without a profile apply or a <see cref="SetConfig" />
        ///     call. Before the runtime is built there is no snapshot yet, so it falls back to
        ///     <see cref="EffectiveConfig" />.
        /// </summary>
        private ConvaiBodyAnimationConfig ActiveConfig =>
            _runtimeBuilt && _builtConfig != null ? _builtConfig : EffectiveConfig;

        protected override string ProfileModuleId => ModuleIds.BodyAnimation;

        protected override Func<ConvaiBodyAnimationProfile> DefaultProfileFactory =>
            ConvaiBodyAnimationProfile.CreateDefault;

        /// <summary>
        ///     Swaps the animation content at runtime (e.g. a different character archetype).
        ///     A running controller defers the graph handoff until a safe idle boundary.
        /// </summary>
        public void SetAnimationSet(ConvaiBodyAnimationSet set)
        {
            _runtimeAnimationSetOverride = set;
            _hasRuntimeAnimationSetOverride = true;
            if (!_runtimeBuilt) return;

            _setSwapPending = true;
            _setSwapQueuedAt = Time.unscaledTime;
            _setSwapGraceIssued = false;
            _trace?.State($"Animation set swap queued for '{(set != null ? set.DisplayName : "(null)")}'.");
            TryBeginSetHandoff();

            // The queued handoff owns the transition; do not destroy the live graph.
        }

        /// <summary>
        ///     Swaps runtime tuning (behavior config) at runtime — e.g. a calm ↔ energetic
        ///     persona switch mid-conversation. Unlike <see cref="SetAnimationSet" />, no
        ///     set-swap handoff is needed: while the runtime is built this routes through
        ///     <see cref="ApplyConfigInPlace" />, which already preserves active clips and
        ///     handles, so an in-flight gesture is never cut. Before the runtime is built, the
        ///     override is simply recorded and adopted by the next <see cref="BuildRuntime" />.
        ///     A null <paramref name="config" /> is refused (warned, no-op) — there is always an
        ///     effective config, never a stateless one.
        /// </summary>
        public void SetConfig(ConvaiBodyAnimationConfig config)
        {
            if (config == null)
            {
                ConvaiLoggerWarning("SetConfig ignored — config is null.");
                return;
            }

            _runtimeConfigOverride = config;
            _hasRuntimeConfigOverride = true;

            if (_runtimeBuilt) ApplyConfigInPlace(config);
            // else: the next BuildRuntime call adopts it via EffectiveConfig.
        }

        /// <summary>
        ///     Overrides the conversation anchor used by social spacing, proximity
        ///     expressiveness and ambient suppression. This is the VR / split-screen /
        ///     multiplayer answer: the default resolution ladder ends at <c>Camera.main</c>,
        ///     which is the wrong anchor for an XR rig, a second local player, or a cutscene
        ///     camera that doesn't carry the MainCamera tag. Pass the transform that represents
        ///     "the person this character is talking to" — an XR Origin camera, a specific
        ///     player's head, an NPC conversation partner.
        /// </summary>
        public void SetConversationAnchor(Transform anchor) => _anchorResolver.SetExplicitAnchor(anchor);

        /// <summary>
        ///     Clears an anchor set via <see cref="SetConversationAnchor" />; resolution falls
        ///     back to the default ladder (<c>Camera.main</c>, then the first enabled Game-view
        ///     camera).
        /// </summary>
        public void ClearConversationAnchor() => _anchorResolver.Clear();

        /// <summary>
        ///     Captures a live snapshot into a caller-owned instance, reusing its lists. An
        ///     on-demand diagnostic (layer entries and clip-name reads allocate) — call it
        ///     from HUD refreshes, not per-frame gameplay code.
        /// </summary>
        public void CaptureSnapshot(BodyAnimationSnapshot snapshot)
        {
            if (snapshot == null) return;
            snapshot.Clear();

            snapshot.Owner = name;
            ConvaiBodyAnimationSet animationSet = EffectiveAnimationSet;
            snapshot.SetName = animationSet != null ? animationSet.DisplayName : "(none)";
            snapshot.DialogueState = CurrentDialogueState;
            snapshot.SpeechEnergy = CurrentSpeechEnergy;
            CoSpeechPerformanceReading coSpeechSnapshot = _coSpeechPlanner?.Current ?? CoSpeechPerformanceReading.None;
            snapshot.CoSpeechQuality = coSpeechSnapshot.QualityTier;
            snapshot.CoSpeechPhase = coSpeechSnapshot.PhrasePhase;
            snapshot.CoSpeechGeneration = coSpeechSnapshot.GenerationId;
            snapshot.CoSpeechGestureSequence = coSpeechSnapshot.GestureSequence;
            snapshot.CoSpeechGesture = coSpeechSnapshot.HasGesture
                ? coSpeechSnapshot.Gesture.Kind.ToString()
                : string.Empty;
            snapshot.AgentSpeed = _locomotion != null ? _locomotion.Speed : 0f;
            snapshot.AnimationSpeed = _locomotionLayer?.AnimationSpeed ?? 0f;
            snapshot.LocomotionState = _locomotionLayer?.StateLabel ?? string.Empty;
            snapshot.DesiredSpeed = _locomotion?.DesiredSpeed ?? 0f;
            snapshot.RemainingDistance = _locomotion?.RemainingDistance ?? 0f;
            snapshot.MotionPreviousNormalizedTime = _locomotionLayer?.PreviousNormalizedTime ?? 0f;
            snapshot.MotionCurrentNormalizedTime = _locomotionLayer?.CurrentNormalizedTime ?? 0f;
            snapshot.RateWarp = _locomotionLayer?.RateWarp ?? 1f;
            snapshot.SharedGaitPhase = _locomotionLayer?.SharedGaitPhase ?? 0f;
            snapshot.GraphPlayableCount = _graphHost?.PlayableCount ?? 0;
            snapshot.AppliedTurnYaw = _locomotionLayer?.AppliedTurnYaw ?? 0f;
            snapshot.ExpectedTurnYaw = _locomotionLayer?.ExpectedTurnYaw ?? 0f;
            snapshot.HandoffMarker = _locomotionLayer?.HandoffMarker ?? 0f;
            snapshot.StopDistanceError = _locomotionLayer?.StopDistanceError ?? 0f;

            for (int port = 0; port < LayerPorts.Count; port++)
            {
                IAnimationLayer layer = port < _layers.Count ? _layers[port] : _talkLayer;
                float envelope = port switch
                {
                    LayerPorts.TalkMoving => _talkLayer?.MovingWeight ?? 0f,
                    LayerPorts.TalkBeat => _talkLayer?.BeatWeight ?? 0f,
                    _ => layer?.Weight ?? 0f
                };
                string portName = port switch
                {
                    LayerPorts.TalkMoving => "Moving Talk",
                    LayerPorts.TalkBeat => "Talk Beat",
                    _ => layer?.Name ?? $"Port {port}"
                };
                AvatarMask mask = _mixerHost?.GetLayerMask(port);
                snapshot.Layers.Add(new BodyAnimationLayerSnapshot
                {
                    Name = portName,
                    State = layer?.StateLabel ?? string.Empty,
                    Clip = layer?.ActiveClipName ?? "(none)",
                    Weight = _mixerHost?.GetLayerWeight(port) ?? 0f,
                    DesiredWeight = _layerArbiter.GetDesiredWeight(port),
                    EnvelopeWeight = envelope,
                    ArbiterTargetWeight = _layerArbiter.GetFinalWeight(port),
                    FinalWeight = _mixerHost?.GetLayerWeight(port) ?? 0f,
                    Owner = _layerArbiter.GetOwner(port),
                    Mask = mask != null ? mask.name : "Full Body",
                    Additive = _mixerHost?.IsLayerAdditive(port) ?? false,
                    NormalizedTime = layer?.ActiveNormalizedTime ?? 0f
                });
            }

            if (_trace != null)
            {
                _trace.CopyRecentEntries(_traceScratch);
                snapshot.RecentTrace.AddRange(_traceScratch);
            }
        }

        /// <summary>Convenience allocating variant of <see cref="CaptureSnapshot(BodyAnimationSnapshot)" />.</summary>
        public BodyAnimationSnapshot CaptureSnapshot()
        {
            var snapshot = new BodyAnimationSnapshot();
            CaptureSnapshot(snapshot);
            return snapshot;
        }

        // ------------------------------------------------------------------ actions & pointing

        /// <summary>
        ///     Plays a named action/gesture from the animation set (name or alias,
        ///     case/separator-insensitive). Never returns null: on failure (runtime not built,
        ///     the action is unknown, or a non-interruptible action is still playing) an
        ///     already-completed, already-failed handle is returned instead —
        ///     check <see cref="BodyAnimationActionHandle.Failed" /> /
        ///     <see cref="BodyAnimationActionHandle.FailureReason" /> to detect it; the reason
        ///     is also logged. Calling this before the runtime is built (e.g. from
        ///     <c>Start()</c>/<c>Awake()</c>) records the request in a single deferred slot and
        ///     replays it automatically once <see cref="RuntimeReady" /> — subscribe to that
        ///     event, or check <see cref="IsRuntimeBuilt" />, for a call that is guaranteed to
        ///     land rather than relying on the deferred safety net.
        /// </summary>
        public BodyAnimationActionHandle PlayAction(string nameOrAlias, ActionPlayOptions options = default)
        {
            if (!_runtimeBuilt || _actionLayer == null)
            {
                QueueDeferredRequest(DeferredRequestSlot.Kind.PlayAction, nameOrAlias);
                _deferredRequest.ActionOptions = options;
                ConvaiLoggerWarning(
                    $"PlayAction('{nameOrAlias}') requested before the animation graph was ready — " +
                    $"it will be replayed automatically once the graph builds, or expire after " +
                    $"{DeferredRequestTimeoutSeconds:F0}s.");
                return BodyAnimationActionHandle.CreateFailed(nameOrAlias, "runtime not built");
            }

            ConvaiBodyAnimationSet animationSet = EffectiveAnimationSet;
            if (animationSet == null || !animationSet.TryGetAction(nameOrAlias, out Data.ActionEntry entry))
            {
                _trace.Warning(
                    $"PlayAction('{nameOrAlias}') — no matching action in set '{animationSet?.DisplayName ?? "(none)"}'.");
                return BodyAnimationActionHandle.CreateFailed(nameOrAlias, "unknown action");
            }

            // ActionLayer.Play can itself return null (e.g. a non-interruptible action is still
            // playing) — normalized to a failed handle here so PlayAction's own contract holds.
            return _actionLayer.Play(entry, in options)
                   ?? BodyAnimationActionHandle.CreateFailed(nameOrAlias, "an active non-interruptible action is still playing");
        }

        /// <summary>Gracefully stops the current action (outro plays when authored).</summary>
        public bool StopAction() => _runtimeBuilt && _actionLayer != null && _actionLayer.RequestStop();

        /// <summary>Immediately stops the current action, cross-dissolving it out over blendOutSeconds
        /// (&lt;=0 = the action's fade-out), skipping the remaining chain/outro.</summary>
        public bool StopActionImmediate(float blendOutSeconds = -1f) =>
            _runtimeBuilt && _actionLayer != null && _actionLayer.Interrupt(blendOutSeconds);

        /// <summary>Name of the action currently playing, empty when none.</summary>
        public string CurrentActionName => _actionLayer?.ActiveActionName ?? string.Empty;

        /// <summary>
        ///     Walks the character to <paramref name="anchor" />, root-aligns precisely to its
        ///     pose (position + yaw, lerped over the entry's authored
        ///     <see cref="Data.ActionAnchorOptions" />), then plays
        ///     <paramref name="actionNameOrAlias" /> — the "sit on the bench" / pick-up /
        ///     use-prop flow. When the character can't get close enough to align (blocked
        ///     path, short leg), it degrades to playing the action unaligned instead of
        ///     retrying. The anchor's height is ignored for alignment — only its XZ position
        ///     and yaw matter, the character's own Y stays grounded throughout — so place
        ///     anchors at the character's intended stand point, not at seat/prop height.
        ///     Never returns null: on failure (runtime not built, no
        ///     <see cref="ConvaiNavMeshLocomotion" />, the anchor is null, or the action is
        ///     unknown) an already-completed, already-failed handle is returned instead — check
        ///     <see cref="PlayActionAtHandle.Failed" />/<see cref="PlayActionAtHandle.FailureReason" />.
        ///     Calling this before the runtime is built records the request in the same
        ///     single-slot deferred replay <see cref="PlayAction" /> uses.
        /// </summary>
        public PlayActionAtHandle PlayActionAt(Transform anchor, string actionNameOrAlias) =>
            PlayActionAtInternal(anchor, actionNameOrAlias, null, default);

        /// <summary>
        ///     <see cref="PlayActionAt(Transform,string)" /> overload with explicit anchor
        ///     alignment options (overrides the action entry's authored defaults) and action
        ///     playback tweaks.
        /// </summary>
        public PlayActionAtHandle PlayActionAt(
            Transform anchor,
            string actionNameOrAlias,
            Data.ActionAnchorOptions anchorOptions,
            ActionPlayOptions playOptions = default) =>
            PlayActionAtInternal(anchor, actionNameOrAlias, anchorOptions, playOptions);

        private PlayActionAtHandle PlayActionAtInternal(
            Transform anchor,
            string actionNameOrAlias,
            Data.ActionAnchorOptions anchorOptionsOverride,
            in ActionPlayOptions playOptions)
        {
            if (anchor == null)
            {
                ConvaiLoggerWarning($"PlayActionAt('{actionNameOrAlias}') ignored — anchor is null.");
                return PlayActionAtHandle.CreateFailed(actionNameOrAlias, "anchor is null");
            }

            if (!_runtimeBuilt || _actionLayer == null || _locomotionLayer == null)
            {
                QueueDeferredRequest(DeferredRequestSlot.Kind.PlayActionAt, actionNameOrAlias);
                _deferredRequest.Anchor = anchor;
                _deferredRequest.AnchorOptions = anchorOptionsOverride;
                _deferredRequest.PlayOptions = playOptions;
                ConvaiLoggerWarning(
                    $"PlayActionAt('{actionNameOrAlias}') requested before the animation graph was ready — " +
                    $"it will be replayed automatically once the graph builds, or expire after " +
                    $"{DeferredRequestTimeoutSeconds:F0}s.");
                return PlayActionAtHandle.CreateFailed(actionNameOrAlias, "runtime not built");
            }

            if (_locomotion == null)
            {
                ConvaiLoggerWarning(
                    $"PlayActionAt('{actionNameOrAlias}') ignored — no ConvaiNavMeshLocomotion found.");
                return PlayActionAtHandle.CreateFailed(actionNameOrAlias, "no locomotion");
            }

            ConvaiBodyAnimationSet animationSet = EffectiveAnimationSet;
            if (animationSet == null || !animationSet.TryGetAction(actionNameOrAlias, out Data.ActionEntry entry))
            {
                _trace.Warning(
                    $"PlayActionAt('{actionNameOrAlias}') — no matching action in set '{animationSet?.DisplayName ?? "(none)"}'.");
                return PlayActionAtHandle.CreateFailed(actionNameOrAlias, "unknown action");
            }

            // A new explicit anchored request supersedes one already in flight — same
            // "explicit command owns the character" policy PlayAction uses when replacing an
            // already-running action.
            _activePlayActionAtRunner?.Cancel();

            Data.ActionAnchorOptions options = anchorOptionsOverride ?? entry.AnchorOptions;
            var anchorPose = new AnchorPose(anchor.position, anchor.eulerAngles.y);

            var runner = new PlayActionAtRunner(
                entry, in playOptions, options, in anchorPose, this,
                (playEntry, options2) => _actionLayer.Play(playEntry, in options2),
                reason => _trace.State($"PlayActionAt('{actionNameOrAlias}') degraded: {reason}."));

            _activePlayActionAtRunner = runner;
            runner.Start();
            return runner.Handle;
        }

        /// <summary>
        ///     Points at a world position. The arm raises, holds at the apex for
        ///     <paramref name="holdSeconds" /> (&lt;0 = until <see cref="StopPointing" />),
        ///     then lowers. Never returns null: on failure (runtime not built, or the set has
        ///     no pointing clips) an already-completed, already-failed handle is returned
        ///     instead — check <see cref="BodyAnimationPointingHandle.Failed" />/
        ///     <see cref="BodyAnimationPointingHandle.FailureReason" />. Calling this before
        ///     the runtime is built records the request in the same single-slot deferred
        ///     replay <see cref="PlayAction" /> uses.
        /// </summary>
        public BodyAnimationPointingHandle PointAt(Vector3 worldPosition, float holdSeconds = -1f)
        {
            RequestPointGlance(worldPosition);
            if (!_runtimeBuilt || _pointingLayer == null)
            {
                QueueDeferredRequest(DeferredRequestSlot.Kind.PointAtPosition, null);
                _deferredRequest.Position = worldPosition;
                _deferredRequest.HoldSeconds = holdSeconds;
                ConvaiLoggerWarning(
                    "PointAt requested before the animation graph was ready — it will be replayed " +
                    $"automatically once the graph builds, or expire after {DeferredRequestTimeoutSeconds:F0}s.");
                return BodyAnimationPointingHandle.CreateFailed("runtime not built");
            }

            return _pointingLayer.Point(worldPosition, null, holdSeconds, 1f, -1f, -1f, false)
                   ?? BodyAnimationPointingHandle.CreateFailed("set has no pointing clips");
        }

        /// <summary>
        ///     Points at a (moving) transform, re-aiming while the hold lasts. Never returns
        ///     null — see the <see cref="PointAt(Vector3,float)" /> remarks for the failure
        ///     contract.
        /// </summary>
        public BodyAnimationPointingHandle PointAt(Transform target, float holdSeconds = -1f)
        {
            if (target == null) return BodyAnimationPointingHandle.CreateFailed("target is null");
            RequestPointGlance(target.position);
            if (!_runtimeBuilt || _pointingLayer == null)
            {
                QueueDeferredRequest(DeferredRequestSlot.Kind.PointAtTarget, null);
                _deferredRequest.Target = target;
                _deferredRequest.HoldSeconds = holdSeconds;
                ConvaiLoggerWarning(
                    "PointAt requested before the animation graph was ready — it will be replayed " +
                    $"automatically once the graph builds, or expire after {DeferredRequestTimeoutSeconds:F0}s.");
                return BodyAnimationPointingHandle.CreateFailed("runtime not built");
            }

            return _pointingLayer.Point(target.position, target, holdSeconds, 1f, -1f, -1f, false)
                   ?? BodyAnimationPointingHandle.CreateFailed("set has no pointing clips");
        }

        /// <summary>
        ///     Points at a (moving) transform with playback tweaks (speed, blend-in/out
        ///     durations, and the auto-release style). Re-aims while the hold lasts. Never
        ///     returns null — see the <see cref="PointAt(Vector3,float)" /> remarks for the
        ///     failure contract.
        /// </summary>
        public BodyAnimationPointingHandle PointAt(Transform target, in PointingPlayOptions options)
        {
            if (target == null) return BodyAnimationPointingHandle.CreateFailed("target is null");
            RequestPointGlance(target.position);
            if (!_runtimeBuilt || _pointingLayer == null)
            {
                QueueDeferredRequest(DeferredRequestSlot.Kind.PointAtTargetOptions, null);
                _deferredRequest.Target = target;
                _deferredRequest.PointingOptions = options;
                ConvaiLoggerWarning(
                    "PointAt requested before the animation graph was ready — it will be replayed " +
                    $"automatically once the graph builds, or expire after {DeferredRequestTimeoutSeconds:F0}s.");
                return BodyAnimationPointingHandle.CreateFailed("runtime not built");
            }

            // A default-constructed options struct has HoldSeconds == 0; treat <=0 as
            // "hold until released" so the zero-value default is a safe indefinite hold.
            // (The float overloads keep their 0 == immediate-release meaning.)
            float holdSeconds = options.HoldSeconds <= 0f ? -1f : options.HoldSeconds;
            return _pointingLayer.Point(
                       target.position, target, holdSeconds, options.Speed,
                       options.BlendInSeconds, options.BlendOutSeconds,
                       options.ReleaseStyle == PointingReleaseStyle.Blend,
                       options.WeightMultiplier)
                   ?? BodyAnimationPointingHandle.CreateFailed("set has no pointing clips");
        }

        /// <summary>
        ///     Point-glance coordination: when a point is raised, also glance at the target
        ///     briefly through the cross-module <see cref="IGazeGlanceHandler" /> seam (the
        ///     Convai Gaze module, when present) — eyes visit the target instead of staying
        ///     locked on the player while the arm points. A no-op when the feature is off or no
        ///     handler is registered.
        /// </summary>
        private void RequestPointGlance(Vector3 worldPosition)
        {
            ConvaiBodyAnimationConfig config = ActiveConfig;
            if (!config.EnablePointGlance) return;
            Context?.GlanceHandler?.RequestGlance(worldPosition, config.PointGlanceSeconds);
        }

        /// <summary>Releases the current pointing hold (the lower-arm tail still plays).</summary>
        public void StopPointing() => _pointingLayer?.Release();

        /// <summary>Stops the current pointing hold now and cross-dissolves the pose out, skipping the lower-arm tail.</summary>
        public void StopPointingImmediate(float blendOutSeconds = -1f) => _pointingLayer?.ReleaseImmediate(blendOutSeconds);

        // ------------------------------------------------------------------ reorientation

        /// <summary>Whether an animated facing turn (turn-in-place) is currently playing.</summary>
        public bool IsReorienting => _runtimeBuilt && _locomotionLayer != null && _locomotionLayer.IsTurningInPlace;

        /// <summary>
        ///     Rotates the character to face <paramref name="worldDirection" /> with the
        ///     animated turn-in-place family — no NavMeshAgent required. This is the
        ///     <see cref="ICharacterReorientationHandler" /> entry the gaze module uses for
        ///     body turns. Returns <c>false</c> when the request cannot be honored (feature
        ///     disabled, clips missing, locomotion busy) so callers can fall back.
        /// </summary>
        public bool FaceTowards(Vector3 worldDirection, string reason = "FaceTowards")
        {
            if (!_runtimeBuilt || _locomotionLayer == null) return false;

            // The angle is measured against the rig the player actually sees (the
            // animator's transform), not the character root: the root can sit off-axis
            // relative to the model (authoring offsets, an external system rotating a
            // child), and an angle measured on the wrong forward turns the character
            // AWAY from the target. The turn itself rotates the character root, which
            // carries the rig rigidly, so the measured delta lands exactly on the rig.
            Transform facing = _animator != null ? _animator.transform
                : Context?.CharacterRoot != null ? Context.CharacterRoot : transform;
            Vector3 flatDirection = worldDirection;
            flatDirection.y = 0f;
            Vector3 flatForward = facing.forward;
            flatForward.y = 0f;
            if (flatDirection.sqrMagnitude < 1e-6f || flatForward.sqrMagnitude < 1e-6f) return false;

            float signedAngle = Vector3.SignedAngle(
                flatForward.normalized, flatDirection.normalized, Vector3.up);
            return _locomotionLayer.RequestFacingTurn(signedAngle, reason);
        }

        bool ICharacterReorientationHandler.TryReorient(Vector3 worldDirection, string reason) =>
            FaceTowards(worldDirection, reason);

        void ICharacterReorientationHandler.CancelReorientation(string reason) =>
            _locomotionLayer?.CancelFacingTurn(reason);

        // ------------------------------------------------------------------ IAnchorMovementDrive (PlayActionAt)
        // The PlayActionAtRunner's root-write authority: MoveTo/Stop delegate to the
        // NavMeshAgent wrapper, IsSettled reads the locomotion layer's own state machine
        // (the planted-stop guarantee), and BeginAlignment/EndAlignment reuse the exact
        // freeze + RotationDrivenExternally coordination turn-in-place already uses so the
        // agent never fights the direct root writes.

        bool IAnchorMovementDrive.MoveTo(Vector3 worldPosition) =>
            _locomotion is IConvaiLocomotionCommands commands && commands.MoveTo(worldPosition);

        void IAnchorMovementDrive.Stop() => _locomotion?.Stop();

        event Action<bool> IAnchorMovementDrive.MoveEnded
        {
            add { if (_locomotion != null) _locomotion.MoveEnded += value; }
            remove { if (_locomotion != null) _locomotion.MoveEnded -= value; }
        }

        bool IAnchorMovementDrive.IsSettled => _locomotionLayer != null && _locomotionLayer.IsSettled;

        Vector3 IAnchorMovementDrive.RootPosition =>
            _layerRuntime?.CharacterRoot != null ? _layerRuntime.CharacterRoot.position : transform.position;

        float IAnchorMovementDrive.RootYawDegrees =>
            _layerRuntime?.CharacterRoot != null ? _layerRuntime.CharacterRoot.eulerAngles.y : transform.eulerAngles.y;

        void IAnchorMovementDrive.BeginAlignment()
        {
            _locomotion?.FreezeAgent(true);
            if (_locomotion != null) _locomotion.RotationDrivenExternally = true;
            // Position has no path-follow equivalent to RotationDrivenExternally — the agent's
            // own updatePosition sync would otherwise snap/correct the root mid-lerp.
            _anchorAlignment?.BeginRootAlignment();
        }

        void IAnchorMovementDrive.SetAlignmentPose(Vector3 position, float yawDegrees)
        {
            if (_layerRuntime?.CharacterRoot == null) return;
            _layerRuntime.CharacterRoot.position = position;
            _layerRuntime.CharacterRoot.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        void IAnchorMovementDrive.EndAlignment()
        {
            Vector3 rootPosition = _layerRuntime?.CharacterRoot != null
                ? _layerRuntime.CharacterRoot.position
                : transform.position;
            // Re-sync the agent's internal simulation position to wherever alignment (or a
            // mid-alignment cancel) actually left the root before restoring updatePosition.
            _anchorAlignment?.EndRootAlignment(rootPosition);
            _locomotion?.FreezeAgent(false);
            if (_locomotion != null) _locomotion.RotationDrivenExternally = false;
        }

        // ------------------------------------------------------------------ exertion

        /// <summary>
        ///     <see cref="IExertionSource" /> entry point: normalized locomotion effort, 0
        ///     (rested) .. 1 (full sustained run effort). Stays 0 when the runtime isn't built
        ///     or <see cref="ConvaiBodyAnimationConfig.PublishExertion" /> is off.
        /// </summary>
        float IExertionSource.Exertion01 => _exertionModel.Value01;

        protected override void OnProfileApplied(ConvaiBodyAnimationProfile newProfile)
        {
            if (!UnityEngine.Application.isPlaying || Context == null || !isActiveAndEnabled) return;
            if (!_runtimeBuilt)
            {
                BuildRuntime();
                return;
            }

            if (EffectiveAnimationSet != _builtSet)
            {
                _setSwapPending = true;
                _setSwapQueuedAt = Time.unscaledTime;
                _setSwapGraceIssued = false;
                TryBeginSetHandoff();
            }
            else if (EffectiveConfig != _builtConfig)
            {
                ApplyConfigInPlace(EffectiveConfig);
            }
        }

        private void ApplyConfigInPlace(ConvaiBodyAnimationConfig config)
        {
            if (config == null || _layerRuntime == null) return;
            if (config != _builtConfig) ReportConfigCorrections(config);
            bool wasPublishingExertion = _builtConfig != null && _builtConfig.PublishExertion;
            _builtConfig = config;
            _layerRuntime.Config = config;
            _trace.Verbosity = config.TraceVerbosity;
            _locomotion?.ConfigureSpeeds(config.WalkSpeed, config.JogSpeed);

            if (wasPublishingExertion != config.PublishExertion)
            {
                if (config.PublishExertion) ProvideExertionSource();
                else ReleaseExertionSource();
            }

            _referentialDirector = new ReferentialGestureDirector(config, _talkLayer, _actionLayer, _pointingLayer);
            _referentialDirector.GestureResolved += HandleReferentialGestureResolved;
            RebuildCoSpeechPlanner(config);
            _ambientDirector = new AmbientActivityDirector(
                config, _actionLayer, _builtSet, _layerRuntime.CharacterRoot, _trace,
                unchecked((uint)(_layerRuntime.RandomSeed ^ 0x416D6269)));
            _socialSpacingRunner.Rebuild(config.ComfortRadius, config.ComfortHoldSeconds, config.MaxRepositionsPerMinute);
            _trace?.State("Body Animation config applied in place; active clips and handles were preserved.");
        }

        private void RebuildCoSpeechPlanner(ConvaiBodyAnimationConfig config)
        {
            if (_coSpeechPlanner != null)
            {
                _coSpeechToken.Release();
                _coSpeechToken = default;
                _coSpeechPlanner.Reset();
            }

            _coSpeechPlanner = _layerRuntime == null
                ? null
                : new CoSpeechPerformancePlanner(config, _layerRuntime.RandomSeed);
            if (_coSpeechPlanner != null && Context != null)
                _coSpeechToken = Context.Provide<ICoSpeechPerformanceSource>(_coSpeechPlanner);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;

            HookContextEvents();
            ProvideService<ICharacterReorientationHandler>(this);

            // Configured once (not per tick, — the module holds a zero steady-state allocation budget): the
            // delegate is a closure over this instance's own _locomotion field, which is
            // re-resolved only on a first build (never on a set-swap handoff), so one configure
            // call stays correct for the component's whole enabled lifetime.
            _emotionalGaitRunner.Configure(ApplyGaitSpeedMultiplier);
            if (EffectiveConfig.PublishExertion)
                ProvideExertionSource();

            // Referential gestures — same GetComponentInParent<ConvaiCharacter> lookup
            // GazeReferentialGlances uses. Resolved BEFORE BuildRuntime (it used to run
            // after), because the seed resolution BuildRuntime performs prefers the
            // character's CharacterId when present — resolving it after the build meant the
            // seed was always derived from the fallback (hierarchy path) instead.
            _spokenLineRelay.SetCharacter(GetComponentInParent<ConvaiCharacter>(true));

            BuildRuntime();
            RegisterGesturePerformer();

            // Event subscription happens after BuildRuntime/RegisterGesturePerformer (unlike
            // SetCharacter above) — this is the previous ordering preserved exactly: only the
            // seed resolution needs the character early, the actual subscriptions do not.
            _spokenLineRelay.Attach(Context?.EventHub);
        }

        protected override void OnDisable()
        {
            _spokenLineRelay.Detach();

            UnregisterGesturePerformer();
            TeardownRuntime();
            // The reorientation handler rides the base class's token release in base.OnDisable().
            ReleaseExertionSource();
            UnhookContextEvents();
            base.OnDisable();
        }

        /// <summary>
        ///     Routes one final spoken line to the two things that read it, without letting both
        ///     act on it. The referential-gesture director gets first refusal: it understands
        ///     more of the line (it also matches registered scene-object names) and it owns the
        ///     referential refractory windows. Only a line it did not claim reaches the
        ///     co-speech planner's own coarser classifier — otherwise a single sentence could
        ///     produce an authored referential gesture and a second, unrelated procedural one
        ///     from the same words.
        /// </summary>
        private void ConsumePendingTranscript()
        {
            if (!_spokenLineRelay.TryConsumePending(out string text)) return;

            _referentialCueClaimedThisLine = false;
            _referentialDirector?.NotifyUtterance(text);
            if (!_referentialCueClaimedThisLine) _coSpeechPlanner?.NotifyTranscript(text, true);
        }

        /// <summary>
        ///     The director resolved a referential cue for this line. When it could not play the
        ///     cue itself (the set authors no clip tagged with it), the cue is handed to the
        ///     co-speech planner, which publishes it for any registered peer performer — so the
        ///     gesture the character meant still happens, procedurally, instead of silently
        ///     evaporating.
        /// </summary>
        private void HandleReferentialGestureResolved(GestureCueKind kind, bool authoredPlayed)
        {
            _referentialCueClaimedThisLine = true;
            if (!authoredPlayed) _coSpeechPlanner?.NotifyGestureCue(kind);
        }

        /// <summary>The one place <see cref="_emotionalGaitRunner" /> is allowed to touch the locomotion component.</summary>
        private void ApplyGaitSpeedMultiplier(float multiplier) =>
            (_locomotion as ConvaiNavMeshLocomotion)?.SetGaitSpeedMultiplier(multiplier);

        /// <summary>Tears down and rebuilds the runtime, re-registering the gesture performer.</summary>
        private void RebuildRuntime()
        {
            UnregisterGesturePerformer();
            TeardownRuntime();
            BuildRuntime();
            RegisterGesturePerformer();
        }

        private void RegisterGesturePerformer()
        {
            if (_gesturePerformer == null || Context == null) return;
            _gesturePerformerToken = Context.Provide<IConversationalGesturePerformer>(_gesturePerformer);
            // Same instance, second contract: the performer also owns the conversational motion
            // budget — same lifecycle as the performer registration.
            _motionBudgetToken = Context.Provide<IConversationalMotionBudget>(_gesturePerformer);
        }

        private void UnregisterGesturePerformer()
        {
            if (_gesturePerformer == null) return;
            _gesturePerformerToken.Release();
            _gesturePerformerToken = default;
            _motionBudgetToken.Release();
            _motionBudgetToken = default;
            // Settles a still-pending cue as Cancelled before TeardownRuntime interrupts the
            // layer underneath it — consumers always get exactly one terminal result per cue.
            _gesturePerformer.Detach();
        }

        protected override void OnDestroy()
        {
            if (_ownedDefaultConfig != null)
            {
                if (UnityEngine.Application.isPlaying) Destroy(_ownedDefaultConfig);
                else DestroyImmediate(_ownedDefaultConfig);
                _ownedDefaultConfig = null;
            }
            base.OnDestroy();
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (!_runtimeBuilt) return;

            // Replay (or expire) a single-slot deferred first-call request on the first
            // tick the runtime is usable — including the synchronous zero-delta tick BuildRuntime
            // itself runs, so a call made in Start()/Awake() lands before the scene's first real
            // frame renders.
            ReplayDeferredRequestIfAny();

            // Resolved once, read by every consumer below and by every layer through
            // the context — the tick and the layers can never disagree about which config is
            // live, even if EffectiveConfig's reference changed since the last build/apply.
            ConvaiBodyAnimationConfig config = ActiveConfig;

            // Resolved once, published on the context so the layers never each read
            // Camera.main independently, and shared below with the ambient director and social
            // spacing (which are ticked outside the layer loop).
            bool hasConversationAnchor = _anchorResolver.TryResolve(_trace, out Vector3 conversationAnchor);

            ISpeechEnergyProvider energyProvider = Context?.SpeechEnergyProvider;
            ConsumePendingTranscript();
            GazeReading gaze = Context?.GazeSource?.Current ?? GazeReading.None;
            _coSpeechPlanner?.Tick(
                CurrentDialogueState,
                energyProvider?.Current ?? 0f,
                deltaTime,
                // The path ahead is a heading, not a thing — a referential gesture aimed at it would
                // have the character gesturing at empty road while it walks and talks. Every other
                // kind still qualifies; this is not a wider change to what counts as a referent.
                gaze.TargetKind is not (GazeTargetKind.None or GazeTargetKind.TravelPath),
                gaze.WorldPoint);
            CoSpeechPerformanceReading coSpeech = _coSpeechPlanner?.Current ?? CoSpeechPerformanceReading.None;
            _coSpeechCoordinator.Dispatch(in coSpeech, Context?.GlanceHandler, Context?.BrowCueSink);
            // a beat gesture must never fight a running action or an active pointing hold
            // for the arms — one-tick-stale, same as IsMoving below (both peer layers' state
            // as of the end of the previous tick). A seated-conversation hold
            // (AllowConversationOverlays) never counts as owning the arms for this purpose.
            bool beatSuppressedByPeers = _actionLayer.SuppressesConversationOverlays || _pointingLayer.IsActive;
            var context = new LayerTickContext(
                deltaTime,
                CurrentDialogueState,
                CurrentEmotion,
                energyProvider?.Current ?? 0f,
                energyProvider != null,
                IsMoving,
                _gesturePerformer?.ReportedIntensityScale ?? 1f,
                beatSuppressedByPeers,
                hasConversationAnchor,
                conversationAnchor);

            for (int i = 0; i < _layers.Count; i++)
            {
                IAnimationLayer layer = _layers[i];
                layer.Tick(in context);
            }

            _layerArbiter.Resolve(_locomotionLayer, _talkLayer, _actionLayer, _pointingLayer);
            for (int port = 0; port < LayerPorts.Count; port++)
                _mixerHost.SetLayerWeight(port, _layerArbiter.GetFinalWeight(port));

            if (config.PublishExertion)
            {
                float speed = _locomotion != null ? _locomotion.Speed : 0f;
                _exertionModel.Tick(
                    deltaTime, speed, config.WalkSpeed, config.JogSpeed,
                    config.ExertionRiseSeconds, config.ExertionRecoverySeconds);
            }

            // Emotion-driven gait speed. Only touches the locomotion component at all while
            // the feature is on (or on the single tick it turns off) — off by default leaves the
            // commanded speed path untouched.
            _emotionalGaitRunner.Tick(config.EnableEmotionalGait, in context.Emotion, config.EmotionGaitRange, deltaTime);

            if (_activePlayActionAtRunner != null)
            {
                _activePlayActionAtRunner.Tick(deltaTime);
                if (_activePlayActionAtRunner.Handle.IsDone)
                    _activePlayActionAtRunner = null;
            }

            // Ambient idle life — after the layer/duck/exertion/runner state for this tick
            // is settled, so the director sees this tick's final busy/moving/runner state.
            _ambientDirector?.Tick(
                context.DialogueState, deltaTime, IsMoving, _activePlayActionAtRunner != null,
                hasConversationAnchor, conversationAnchor);

            // Social stepping — same busy signal shape as the ambient director, evaluated
            // after it so a fresh ambient wind-down this tick is reflected in IsMoving/busy.
            if (config.EnableSocialSpacing)
            {
                bool socialSpacingBusy = _actionLayer.IsActive || IsMoving || _activePlayActionAtRunner != null;
                _socialSpacingRunner.Tick(
                    _layerRuntime?.CharacterRoot, _locomotion, _trace,
                    context.DialogueState, deltaTime, socialSpacingBusy, hasConversationAnchor, conversationAnchor);
            }

            TickFirehose(deltaTime, config);
            TickSetHandoff(deltaTime);
        }

        private void TickSetHandoff(float deltaTime)
        {
            if (_graphHost != null && _graphHost.TickRootHandoff(deltaTime))
            {
                Playable retiringRoot = _graphHost.TakeRetiringRoot();
                // No SetAnimationStartGate(true) here: that was a half-fix for an earlier retiring-layer bug (the
                // retiring layer's Teardown, called just below via TeardownRetiringLayers(),
                // used to always re-close the gate on completion). Now that Teardown only
                // touches the drive when its runtime still OwnsLocomotionDrive (false for the
                // retiring runtime since TryBeginSetHandoff), the live layer's own gate state is
                // never clobbered and this re-open is redundant.
                _setHandoffCoordinator.TeardownRetiringLayers();
                _graphHost.DestroyRetiredSubgraph(retiringRoot);
                _trace?.State("Animation set root handoff completed.");
            }

            // A Hold-looping action or an indefinite pointing hold can block the polite
            // handoff guard forever. Escalate rather than wedge silently: at the grace mark, ask
            // the owning layers to yield; at the force mark, perform the handoff anyway (still
            // through the normal crossfade — never a hard cut). Only evaluated while the swap
            // hasn't begun yet (3d: cannot fire against a swap already in flight).
            if (_setSwapPending && _graphHost != null && !_graphHost.IsRootHandoffActive)
            {
                float elapsed = Time.unscaledTime - _setSwapQueuedAt;
                EscalationAction escalation = AnimationSetHandoffCoordinator.EvaluateEscalation(
                    elapsed, _setSwapGraceIssued, SetSwapGraceSeconds, SetSwapForceSeconds);
                if (escalation.IssueGrace)
                {
                    _setSwapGraceIssued = true;
                    RequestSetSwapBlockersYield();
                }

                TryBeginSetHandoff(force: escalation.Force);
            }
        }

        /// <summary>
        ///     After <see cref="SetSwapGraceSeconds" /> of a queued swap not being able to
        ///     begin, asks each currently-blocking owner to yield gracefully (their own outro/
        ///     release, not a hard cut). Runs once per queued swap (gated by
        ///     <see cref="_setSwapGraceIssued" />) — never every tick.
        /// </summary>
        private void RequestSetSwapBlockersYield()
        {
            var yielding = new System.Text.StringBuilder();
            void Append(string label)
            {
                if (yielding.Length > 0) yielding.Append(", ");
                yielding.Append(label);
            }

            if (_actionLayer != null && _actionLayer.IsActive)
            {
                StopAction();
                Append("action");
            }

            if (_pointingLayer != null && _pointingLayer.IsActive)
            {
                StopPointing();
                Append("pointing hold");
            }

            if (_activePlayActionAtRunner != null)
            {
                _activePlayActionAtRunner.Cancel();
                Append("PlayActionAt runner");
            }

            _trace?.State(yielding.Length > 0
                ? $"Animation set swap grace period elapsed ({SetSwapGraceSeconds:F0}s) — asked blocking owner(s) [{yielding}] to yield."
                : $"Animation set swap grace period elapsed ({SetSwapGraceSeconds:F0}s) — locomotion has not settled; no owner to ask, waiting for it to settle or the force mark.");
        }

        /// <summary>
        ///     Begins the queued set handoff. Politely (<paramref name="force" /> false)
        ///     this still requires action idle, pointing idle, locomotion settled and no
        ///     <see cref="PlayActionAtRunner" /> — the original safe-boundary guard. After
        ///     <see cref="SetSwapForceSeconds" /> the coordinator calls this with
        ///     <paramref name="force" /> true, which skips that ownership guard and performs the
        ///     handoff regardless: still the normal crossfade, never a hard cut, because an
        ///     animation set the user asked for ten seconds ago that never arrives is worse than
        ///     a blended transition.
        /// </summary>
        private void TryBeginSetHandoff(bool force = false)
        {
            if (!_setSwapPending || !_runtimeBuilt || _graphHost == null || _graphHost.IsRootHandoffActive)
                return;
            if (!force &&
                (_activePlayActionAtRunner != null || _actionLayer.IsActive || _pointingLayer.IsActive ||
                 !_locomotionLayer.IsSettled))
                return;

            ConvaiBodyAnimationSet set = EffectiveAnimationSet;
            if (set == null)
            {
                _setSwapPending = false;
                _setSwapGraceIssued = false;
                _trace?.Warning("Animation set swap refused because the effective Set is null; the current stack remains active.");
                return;
            }

            if (force)
            {
                // The blockers were already asked to yield at the grace mark; force means they
                // did not finish in time — cancel/retire them outright rather than let their
                // stale runner state survive into the new layer stack.
                _activePlayActionAtRunner?.Cancel();
                _activePlayActionAtRunner = null;
                _trace?.State(
                    $"Animation set swap forced after {SetSwapForceSeconds:F0}s — blocking owner(s) did not " +
                    "yield in time; performing the handoff now with the normal crossfade.");
            }

            _setSwapPending = false;
            _setSwapGraceIssued = false;
            UnregisterGesturePerformer();
            _setHandoffCoordinator.BeginRetiring(_layers);

            // Disarm the retiring runtime's authority over the shared locomotion drive
            // before the new runtime (and its live LocomotionLayer) is created below. The
            // retiring layers hold a reference to this exact runtime object, so flipping the
            // flag here is what stops their eventual Teardown() from touching a drive the new
            // layer is already commanding.
            if (_layerRuntime != null)
                _layerRuntime.OwnsLocomotionDrive = false;

            ConvaiBodyAnimationConfig config = EffectiveConfig;
            _builtSet = set;
            _builtConfig = config;

            // The incoming set can have been analyzed on a differently sized rig, so the scale
            // must be re-resolved AND re-pushed to the locomotion component — the agent's jog
            // distance gates live there, not in the layer runtime, and would otherwise keep the
            // outgoing set's calibration. (Agent dimensions are not re-derived: the rig has not
            // changed, only the content.)
            float motionScale = ResolveMotionScale(set, _animator);
            if (_locomotionComponent is ConvaiNavMeshLocomotion swapNavMeshLocomotion)
                swapNavMeshLocomotion.SetMotionScale(motionScale);
            // The handoff path used to skip this call entirely (unlike BuildRuntime,
            // which configures speeds right after resolving locomotion), leaving the drive
            // configured for the outgoing set's speeds until the next full BuildRuntime. Not part
            // of LayerStackBuilder — locomotion is not re-resolved on a handoff, only
            // reconfigured, so it belongs at this call site rather than inside "build the stack".
            _locomotion?.ConfigureSpeeds(config.WalkSpeed, config.JogSpeed);

            Transform characterRoot = _locomotionComponent != null ? _locomotionComponent.transform : transform;
            var args = new LayerStackBuilder.Args(
                _graphHost.Graph, set, config, _trace, ResolveRandomSeed(), motionScale,
                characterRoot, _animator, _locomotion,
                change => StateChanged?.Invoke(change),
                e => ActionEvent?.Invoke(e),
                HandleReferentialGestureResolved,
                _socialSpacingRunner);
            LayerStackBuilder.Result stack = LayerStackBuilder.Build(in args, _layers);

            _mixerHost = stack.Mixer;
            _layerRuntime = stack.LayerRuntime;
            _locomotionLayer = stack.LocomotionLayer;
            _talkLayer = stack.TalkLayer;
            _actionLayer = stack.ActionLayer;
            _pointingLayer = stack.PointingLayer;
            _gesturePerformer = stack.GesturePerformer;
            _referentialDirector = stack.ReferentialDirector;
            _ambientDirector = stack.AmbientDirector;
            BindBrakingDistanceProvider();

            // Same call as a first build: the incoming set brings a new layer runtime, so the
            // planner is rebuilt against its seed. Going through the shared helper also withdraws
            // the outgoing planner's registration — the inline version that used to live here
            // overwrote the token instead, leaving the previous planner registered.
            RebuildCoSpeechPlanner(config);
            RegisterGesturePerformer();

            bool hasConversationAnchor = _anchorResolver.TryResolve(_trace, out Vector3 conversationAnchor);
            var initialContext = new LayerTickContext(
                0f, CurrentDialogueState, CurrentEmotion, CurrentSpeechEnergy,
                Context?.SpeechEnergyProvider != null, false, 1f, false,
                hasConversationAnchor, conversationAnchor);
            for (int i = 0; i < _layers.Count; i++)
                _layers[i].Tick(in initialContext);
            _layerArbiter.Resolve(_locomotionLayer, _talkLayer, _actionLayer, _pointingLayer);
            for (int port = 0; port < LayerPorts.Count; port++)
                _mixerHost.SetLayerWeight(port, _layerArbiter.GetFinalWeight(port));

            _graphHost.BeginRootHandoff(_mixerHost.Mixer, config.IdleCrossfadeSeconds);
            _graphHost.Evaluate();
            _trace?.State($"Animation set root handoff started: '{set.DisplayName}'.");
        }

        // ------------------------------------------------------------------ deferred first-call safety

        /// <summary>Records a request made before the runtime is built into the single deferred slot, replacing any older one.</summary>
        private void QueueDeferredRequest(DeferredRequestSlot.Kind kind, string name)
        {
            _deferredKind = kind;
            _deferredName = name;
            _deferredQueuedAt = UnityEngine.Time.unscaledTime;
        }

        private void ClearDeferredRequest()
        {
            _deferredKind = DeferredRequestSlot.Kind.None;
            _deferredName = null;
            _deferredRequest.Clear();
        }

        /// <summary>
        ///     Called at the top of every tick while the runtime is built. Replays the
        ///     pending deferred request (if any and not expired) or logs the one clear expiry
        ///     message and drops it. The slot is cleared before replay so a handler that itself
        ///     fails and re-queues (unlikely, but not impossible) never loops.
        /// </summary>
        private void ReplayDeferredRequestIfAny()
        {
            if (_deferredKind == DeferredRequestSlot.Kind.None) return;

            if (DeferredRequestSlot.HasExpired(_deferredQueuedAt, UnityEngine.Time.unscaledTime, DeferredRequestTimeoutSeconds))
            {
                ConvaiLoggerWarning(
                    $"{DeferredRequestSlot.Describe(_deferredKind, _deferredName)} was requested before the animation graph was ready and expired " +
                    $"after {DeferredRequestTimeoutSeconds:F0}s. Call it after the RuntimeReady event, or check " +
                    "IsRuntimeBuilt first.");
                ClearDeferredRequest();
                return;
            }

            DeferredRequestSlot.Kind kind = _deferredKind;
            string name = _deferredName;
            Vector3 position = _deferredRequest.Position;
            Transform target = _deferredRequest.Target;
            float holdSeconds = _deferredRequest.HoldSeconds;
            PointingPlayOptions pointingOptions = _deferredRequest.PointingOptions;
            Transform anchor = _deferredRequest.Anchor;
            Data.ActionAnchorOptions anchorOptions = _deferredRequest.AnchorOptions;
            ActionPlayOptions actionOptions = _deferredRequest.ActionOptions;
            ActionPlayOptions playOptions = _deferredRequest.PlayOptions;
            ClearDeferredRequest();

            switch (kind)
            {
                case DeferredRequestSlot.Kind.PlayAction:
                    PlayAction(name, actionOptions);
                    break;
                case DeferredRequestSlot.Kind.PointAtPosition:
                    PointAt(position, holdSeconds);
                    break;
                case DeferredRequestSlot.Kind.PointAtTarget:
                    if (target != null) PointAt(target, holdSeconds);
                    break;
                case DeferredRequestSlot.Kind.PointAtTargetOptions:
                    if (target != null) PointAt(target, in pointingOptions);
                    break;
                case DeferredRequestSlot.Kind.PlayActionAt:
                    if (anchor != null) PlayActionAtInternal(anchor, name, anchorOptions, in playOptions);
                    break;
            }
        }

        // ------------------------------------------------------------------ build/teardown

        private void BuildRuntime()
        {
            if (!UnityEngine.Application.isPlaying || _runtimeBuilt) return;

            EnsureConversationFlowSource();

            if (!ResolveAndValidateAnimator())
                return;

            ConvaiBodyAnimationSet animationSet = EffectiveAnimationSet;
            if (animationSet == null)
            {
                ConvaiLoggerWarning(
                    "No ConvaiBodyAnimationSet assigned (directly or via profile) — body animation stays inactive.");
                RestoreAnimatorState();
                return;
            }

            ConvaiBodyAnimationConfig config = EffectiveConfig;
            ReportConfigCorrections(config);
            _builtSet = animationSet;
            _builtConfig = config;

            _trace = new AnimTrace(name) { Verbosity = config.TraceVerbosity };
            _graphHost = new AnimationGraphHost(_animator, name, _trace);
            // _mixerHost is created inside LayerStackBuilder.Build below — creating one here too
            // would leave an orphan AnimationLayerMixerPlayable in the graph, never connected,
            // never destroyed.

            _locomotion = ResolveLocomotion();
            float motionScale = ResolveMotionScale(animationSet, _animator);
            if (_locomotion != null)
            {
                _locomotion.ConfigureSpeeds(config.WalkSpeed, config.JogSpeed);
                if (_locomotionComponent is ConvaiNavMeshLocomotion navMeshLocomotion)
                {
                    navMeshLocomotion.SetMotionScale(motionScale);
                    navMeshLocomotion.ConfigureAgentDimensionsFromRig(_animator);
                }
            }

            // Single rotation authority: turn/start yaw must rotate the same transform the
            // locomotion component's path-follow rotation drives, or the two fight when the
            // components sit on different GameObjects.
            Transform characterRoot = transform;
            if (_locomotionComponent != null && _locomotionComponent.transform != transform)
            {
                characterRoot = _locomotionComponent.transform;
                _trace.State(
                    $"Character root authority set to locomotion transform '{_locomotionComponent.name}' " +
                    "(the controller sits on a different GameObject).");
            }

            var args = new LayerStackBuilder.Args(
                _graphHost.Graph, animationSet, config, _trace, ResolveRandomSeed(), motionScale,
                characterRoot, _animator, _locomotion,
                change => StateChanged?.Invoke(change),
                actionEvent => ActionEvent?.Invoke(actionEvent),
                HandleReferentialGestureResolved,
                _socialSpacingRunner);
            LayerStackBuilder.Result stack = LayerStackBuilder.Build(in args, _layers);

            _mixerHost = stack.Mixer;
            _layerRuntime = stack.LayerRuntime;
            _locomotionLayer = stack.LocomotionLayer;
            _talkLayer = stack.TalkLayer;
            _actionLayer = stack.ActionLayer;
            _pointingLayer = stack.PointingLayer;
            _gesturePerformer = stack.GesturePerformer;
            _referentialDirector = stack.ReferentialDirector;
            _ambientDirector = stack.AmbientDirector;
            BindBrakingDistanceProvider();

            // Co-speech performance is part of the runtime, so it starts with the runtime. It used
            // to be created only by a live set swap or config swap, which meant a character that
            // was simply built and left alone — every character, on every first Play — never
            // published ICoSpeechPerformanceSource, and Body Language read an empty co-speech
            // performance for the whole session. Built here, before the priming tick below, the
            // planner is live from the first frame.
            RebuildCoSpeechPlanner(config);

            _graphHost.SetRoot(_mixerHost.Mixer);
            _graphHost.Play();

            EmbodimentTickScheduler scheduler = Context?.EnsureTickScheduler();
            if (scheduler != null)
            {
                scheduler.Register(this);
                _tickRegistered = true;
            }

            _runtimeBuilt = true;

            var findings = new List<BodyAnimationFinding>();
            animationSet.CollectFindings(findings);
            _featureAvailability = BodyAnimationFeatureAvailability.Compute(animationSet, config);
            _trace.State(
                $"Built: set='{animationSet.DisplayName}' idles={animationSet.Idles.Count} " +
                $"talks={animationSet.Talks.Count} actions={animationSet.Actions.Count} " +
                $"pointing={animationSet.Pointing.Entries.Count} issues={findings.Count} " +
                $"seed={_layerRuntime.RandomSeed} | {config.DescribeFeatures()}");
            for (int i = 0; i < findings.Count; i++)
                _trace.Warning($"Set issue: {findings[i].Message}");

            // Name only the features that are genuinely stuck — a healthy set stays silent, so
            // this never adds noise to the common path. Both directions are reported: a switch
            // that is on with nothing to play, and content that is authored with the switch that
            // would play it turned off.
            //
            // Warning, not a trace line: these two are the difference between "this character is
            // not set up" and "this character is set up but has no clips for that", and they fire
            // once per build. Routed through the trace gate they would be silent on the shipped
            // config, whose verbosity is Off — a user who tags a clip and sees nothing happen
            // would again have nothing anywhere telling them why.
            _inertFeatureNamesScratch.Clear();
            _featureAvailability.CollectInertFeatureNames(_inertFeatureNamesScratch);
            if (_inertFeatureNamesScratch.Count > 0)
                _trace.Warning(
                    $"Turned on but with nothing to play (no matching content in '{animationSet.DisplayName}'): " +
                    string.Join(", ", _inertFeatureNamesScratch) +
                    ". Either author and tag the clips, or turn the setting off.");

            _inertFeatureNamesScratch.Clear();
            _featureAvailability.CollectDormantContentNames(_inertFeatureNamesScratch);
            if (_inertFeatureNamesScratch.Count > 0)
                _trace.Warning(
                    $"'{animationSet.DisplayName}' authors content for " +
                    string.Join(", ", _inertFeatureNamesScratch) +
                    ", but the setting that would play it is off — turn it on in the Body Animation config.");

            // Pose the skeleton this very frame: until the first scheduled embodiment tick
            // the animator renders the avatar's default pose (open jaw on CC rigs) — a
            // visible blink at Play. One zero-dt tick starts the initial idle, the
            // immediate evaluation applies it.
            ((IEmbodimentTickable)this).EmbodimentTick(0f);
            _graphHost.Evaluate();

            // Raised after the runtime is fully usable — the zero-delta tick above already
            // ran and was evaluated — so a handler that calls straight back into PlayAction/
            // PointAt/PlayActionAt from inside this event succeeds immediately.
            RuntimeReady?.Invoke();
        }

        private void TeardownRuntime()
        {
            // A deferred first-call request must not survive a teardown — replaying it
            // against a runtime that no longer exists (or a rebuilt one with different content)
            // would be worse than dropping it silently.
            ClearDeferredRequest();

            if (_coSpeechPlanner != null)
            {
                _coSpeechToken.Release();
                _coSpeechToken = default;
                _coSpeechPlanner.Reset();
                _coSpeechPlanner = null;
                _coSpeechCoordinator.Reset();
            }
            _activePlayActionAtRunner?.Cancel();
            _activePlayActionAtRunner = null;

            if (_tickRegistered)
            {
                Context?.TickScheduler?.Unregister(this);
                _tickRegistered = false;
            }

            // Withdrawn before the layers go: the locomotion component outlives this runtime, and
            // a provider left pointing at a torn-down layer would answer the next graceful stop
            // out of a dead runtime.
            BindBrakingDistanceProvider(withdraw: true);

            for (int i = 0; i < _layers.Count; i++)
                _layers[i].Teardown();
            _layers.Clear();
            _setHandoffCoordinator.TeardownRetiringLayers();
            _setSwapPending = false;
            _setSwapGraceIssued = false;

            _graphHost?.Dispose();
            _graphHost = null;
            _mixerHost = null;
            _layerRuntime = null;
            _locomotionLayer = null;
            _talkLayer = null;
            _actionLayer = null;
            _pointingLayer = null;
            _gesturePerformer = null;
            _referentialDirector = null;
            _ambientDirector = null;
            _socialSpacingRunner.Clear();
            _emotionalGaitRunner.Reset();
            _anchorResolver.Reset(); // re-arms the degradation log-once latch for the next build.
            _locomotion = null;
            _locomotionComponent = null;
            _anchorAlignment = null;
            _exertionModel.Reset();
            _runtimeBuilt = false;
            _builtSet = null;
            _builtConfig = null;
            RestoreAnimatorState();
        }

        /// <summary>
        ///     Hands the NavMesh locomotion component a way to ask the live locomotion layer how
        ///     much room a stop needs, so <see cref="ConvaiNavMeshLocomotion.StopGracefully" />
        ///     brakes over the distance this character's own stop clip travels rather than over a
        ///     generic physics estimate. A character with no NavMesh locomotion, or with the layer
        ///     absent, simply keeps the physics fallback.
        /// </summary>
        /// <param name="withdraw">True to clear the provider (runtime teardown).</param>
        private void BindBrakingDistanceProvider(bool withdraw = false)
        {
            if (_locomotionComponent is not ConvaiNavMeshLocomotion navMeshLocomotion) return;

            navMeshLocomotion.SetBrakingDistanceProvider(
                withdraw || _locomotionLayer == null ? null : _locomotionLayer.SuggestBrakingDistance);
        }

        /// <summary>
        ///     Resolves the seed the variant scheduler, ambient director and co-speech
        ///     planner all derive their own streams from. <c>0</c> (the default) derives a
        ///     stable identity from the character — <see cref="ConvaiCharacter.CharacterId" />
        ///     when a <see cref="ConvaiCharacter" /> is present, otherwise this transform's full
        ///     hierarchy path — hashed with <see cref="StableHash" /> so the sequence is
        ///     reproducible across sessions and scene loads (the old expression used
        ///     <see cref="object.GetHashCode()" />, the instance ID, which is neither). A
        ///     non-zero <see cref="_randomSeed" /> is used verbatim, letting a user pin an exact
        ///     sequence to reproduce a reported issue.
        /// </summary>
        private int ResolveRandomSeed()
        {
            if (_randomSeed != 0) return _randomSeed;

            ConvaiCharacter character = _spokenLineRelay.Character;
            if (character != null && !string.IsNullOrEmpty(character.CharacterId))
                return StableHash.Of(character.CharacterId);

            return StableHash.Of(BuildHierarchyPath(transform));
        }

        /// <summary>
        ///     Resolves the single factor every clip-measured distance/speed the locomotion
        ///     layer reads is multiplied by, so a character built at a different scale than the
        ///     sample rig the content was analyzed on still lands its stops and covers the right
        ///     ground. Computed from the walk clip's <see cref="ClipMotionMetadata.AuthoredMotionScale" />
        ///     — the set's canonical cycle — via the shared <see cref="MotionScaleResolver" />, and
        ///     called from both <see cref="BuildRuntime" /> and <see cref="TryBeginSetHandoff" />
        ///     (the <see cref="ResolveRandomSeed" /> precedent) so the handoff path never diverges.
        /// </summary>
        private float ResolveMotionScale(ConvaiBodyAnimationSet set, Animator animator)
        {
            if (set == null || animator == null) return 1f;

            ClipMotionMetadata walkMeta = set.Locomotion.Walk.Metadata;
            float authoredWalkScale = walkMeta != null && walkMeta.HasAuthoredMotionScale
                ? walkMeta.AuthoredMotionScale
                : 0f;

            float scale = MotionScaleResolver.Resolve(
                animator.humanScale, animator.transform.lossyScale, authoredWalkScale);

            if (scale != 1f)
            {
                _trace?.State(
                    $"Rig motion scale {scale:F2} (this rig measures {scale:F2}x the animation " +
                    "content's reference scale) — walk/jog speeds and stop distances calibrated automatically.");
            }

            WarnOnMotionScaleMismatch(set, authoredWalkScale > 0f ? authoredWalkScale : MotionScaleResolver.DefaultAuthoredMotionScale);
            return scale;
        }

        /// <summary>
        ///     Set-consistency check: names, in one warning, any assigned locomotion clip
        ///     whose analyzed motion scale disagrees with the walk clip's by more than
        ///     <see cref="MotionScaleResolver.ClipMismatchThreshold" />. Clips with no recorded
        ///     scale (unanalyzed or metadata written before clip motion was measured) are never counted as disagreeing.
        /// </summary>
        private void WarnOnMotionScaleMismatch(ConvaiBodyAnimationSet set, float walkAuthoredScale)
        {
            set.Locomotion.CollectAssigned(_motionScaleScratch);
            string outliers = MotionScaleResolver.FindMismatchedClips(_motionScaleScratch, walkAuthoredScale);
            if (outliers != null)
            {
                ConvaiLoggerWarning(
                    $"Locomotion clip(s) [{outliers}] were measured at a different rig scale than the " +
                    "Walk clip and will not calibrate consistently with it — re-run the Clip Motion " +
                    "Analyzer over the whole set so every clip shares one reference rig.");
            }
        }

        /// <summary>Full "Root/Child/.../This" hierarchy path — runs once per build, not per frame.</summary>
        private static string BuildHierarchyPath(Transform t)
        {
            var builder = new System.Text.StringBuilder(t.name);
            Transform current = t.parent;
            while (current != null)
            {
                builder.Insert(0, "/").Insert(0, current.name);
                current = current.parent;
            }
            return builder.ToString();
        }

        private bool ResolveAndValidateAnimator()
        {
            _animator = _animatorOverride != null
                ? _animatorOverride
                : GetComponentInChildren<Animator>(true);

            if (_animator == null)
            {
                ConvaiLoggerWarning("No Animator found in children — body animation stays inactive.");
                return false;
            }

            if (_animator.avatar == null || !_animator.avatar.isValid || !_animator.avatar.isHuman)
            {
                ConvaiLoggerWarning(
                    $"Animator '{_animator.name}' needs a valid Humanoid avatar " +
                    "(Rig → Animation Type → Humanoid). Body animation stays inactive.");
                return false;
            }

            CaptureAnimatorState(_animator);

            if (_animator.runtimeAnimatorController != null)
            {
                ConvaiLoggerWarning(
                    $"Animator '{_animator.name}' has an Animator Controller assigned; the body " +
                    "animation PlayableGraph replaces its output while active.");
            }

            if (_animator.applyRootMotion)
            {
                // The module is the root authority: displacement comes from the
                // NavMeshAgent and turn/start yaw from the analyzed motion curves.
                // Applied root motion would move the character a second time on top of
                // the scripted drive.
                _animator.applyRootMotion = false;
                Convai.Runtime.Logging.ConvaiLogger.Info(
                    $"[ConvaiBodyAnimationController] '{name}' disabled Apply Root Motion on " +
                    $"Animator '{_animator.name}' — the body animation module drives root " +
                    "displacement and rotation itself.",
                    Convai.Domain.Logging.LogCategory.Animation);
            }

            return true;
        }

        private void CaptureAnimatorState(Animator animator)
        {
            if (_animatorStateCaptured && _ownedAnimator == animator) return;
            RestoreAnimatorState();
            _ownedAnimator = animator;
            _ownedAnimatorApplyRootMotion = animator.applyRootMotion;
            _animatorStateCaptured = true;
        }

        private void RestoreAnimatorState()
        {
            if (!_animatorStateCaptured) return;
            if (_ownedAnimator != null)
                _ownedAnimator.applyRootMotion = _ownedAnimatorApplyRootMotion;
            _ownedAnimator = null;
            _animatorStateCaptured = false;
        }

        private void EnsureConversationFlowSource()
        {
            bool autoCreate = profile != null ? profile.AutoCreateConversationFlow : _autoCreateConversationFlow;
            if (!autoCreate) return;
            if (Context == null || Context.ConversationFlowSource != null) return;
            if (!UnityEngine.Application.isPlaying) return;

            Context.MarkConversationFlowDriverDemanded();
            Context.TryEnsureConversationFlowSource();
        }

        // ------------------------------------------------------------------ inputs

        private DialogueState CurrentDialogueState =>
            Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;

        private EmotionReading CurrentEmotion =>
            Context?.EmotionStateSource?.Current ?? EmotionReading.Neutral;

        private float CurrentSpeechEnergy
        {
            get
            {
                ISpeechEnergyProvider provider = Context?.SpeechEnergyProvider;
                return provider?.Current ?? 0f;
            }
        }

        /// <summary>True while the locomotion layer is displacing the character.</summary>
        private bool IsMoving => _locomotionLayer?.IsMoving ?? false;

        private ILocomotionDrive ResolveLocomotion()
        {
            if (_locomotionProviderOverride != null)
            {
                if (_locomotionProviderOverride is not IConvaiLocomotionSource source)
                {
                    ConvaiLoggerWarning(
                        $"Locomotion provider '{_locomotionProviderOverride.name}' does not implement " +
                        $"{nameof(IConvaiLocomotionSource)}; locomotion animation stays disabled.");
                    return null;
                }

                _locomotionComponent = _locomotionProviderOverride;
                _anchorAlignment = source as IConvaiAnchorAlignment;
                return source as ILocomotionDrive ?? new LocomotionProviderAdapter(source);
            }

            ConvaiNavMeshLocomotion locomotion = GetComponentInParent<ConvaiNavMeshLocomotion>(true);
            if (locomotion == null)
                locomotion = GetComponentInChildren<ConvaiNavMeshLocomotion>(true);
            _locomotionComponent = locomotion;
            _anchorAlignment = locomotion;
            return locomotion;
        }

        // ------------------------------------------------------------------ context hooks

        private void HookContextEvents()
        {
            if (Context == null || _dependenciesHooked) return;
            Context.DependenciesPopulated += HandleDependenciesPopulated;
            _dependenciesHooked = true;
        }

        private void UnhookContextEvents()
        {
            if (Context == null || !_dependenciesHooked) return;
            Context.DependenciesPopulated -= HandleDependenciesPopulated;
            _dependenciesHooked = false;
        }

        private void HandleDependenciesPopulated()
        {
            if (_runtimeBuilt)
            {
                if (_tickRegistered) return;
                EmbodimentTickScheduler scheduler = Context?.EnsureTickScheduler();
                if (scheduler == null) return;
                scheduler.Register(this);
                _tickRegistered = true;
                return;
            }

            BuildRuntime();
            RegisterGesturePerformer();
        }

        // ------------------------------------------------------------------ diagnostics

        private void TickFirehose(float deltaTime, ConvaiBodyAnimationConfig config)
        {
            if (_trace.Verbosity < AnimTraceVerbosity.Firehose) return;

            _firehoseTimer += deltaTime;
            if (_firehoseTimer < config.FirehoseIntervalSeconds) return;
            _firehoseTimer = 0f;

            for (int i = 0; i < _layers.Count; i++)
            {
                IAnimationLayer layer = _layers[i];
                _trace.Firehose(
                    $"layer[{i}:{layer.Name}] w={layer.Weight:F3} state={layer.StateLabel} " +
                    $"clip={layer.ActiveClipName} t={layer.ActiveNormalizedTime:F3}");
            }
        }

        /// <summary>
        ///     Surfaces anything the config asset carries that is out of range. <c>[Min]</c>,
        ///     <c>[Range]</c> and <c>OnValidate</c> are editor-only and never run in a build, so a
        ///     config authored by script, shipped from an older schema, or hand-edited in YAML can
        ///     reach the runtime with, say, a jog slower than its walk — or an empty blend curve,
        ///     which used to hold every layer's weight at zero with no diagnostic whatsoever. The
        ///     getters clamp regardless; this exists so the user is told rather than left to
        ///     wonder why their tuning did not take effect.
        /// </summary>
        private void ReportConfigCorrections(ConvaiBodyAnimationConfig config)
        {
            if (config == null) return;

            Data.BodyAnimationConfigCorrections corrections = config.ValidateForRuntime();
            if (!corrections.HasCorrections) return;

            var builder = new System.Text.StringBuilder();
            builder.Append("body animation config '").Append(config.name)
                .Append("' has ").Append(corrections.Descriptions.Count)
                .Append(corrections.Descriptions.Count == 1 ? " setting" : " settings")
                .Append(" outside the supported range. The runtime corrected them for this session; " +
                        "fix the asset to make it permanent:");
            for (int i = 0; i < corrections.Descriptions.Count; i++)
                builder.Append("\n  • ").Append(corrections.Descriptions[i]);

            ConvaiLoggerWarning(builder.ToString());
        }

        private void ConvaiLoggerWarning(string message)
        {
            Convai.Runtime.Logging.ConvaiLogger.Warning(
                $"[ConvaiBodyAnimationController] '{name}' {message}",
                Convai.Domain.Logging.LogCategory.Animation);
        }
    }
}
