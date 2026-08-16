using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.EventSystem;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Core.Behaviors;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Core.Policy;
using Convai.Modules.Gaze.Core.Reorientation;
using Convai.Modules.Gaze.Core.Shift;
using Convai.Modules.Gaze.Core.Solvers;
using Convai.Modules.Gaze.Core.Targeting;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Integrations;
using Convai.Modules.Gaze.Providers;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Components
{
    /// <summary>
    ///     The Convai Gaze system: a single, fully code-driven controller that decides what
    ///     the character looks at (targeting), how strongly each dialogue state commits to it
    ///     (policy), and articulates the look anatomically across torso, neck/head, eyes, and
    ///     eyelids (solvers) — including full-body turns toward off-axis targets. Behavior is
    ///     authored through one <see cref="ConvaiGazeProfile" /> asset.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The component is a thin composition root: targeting and policy run in the
    ///         embodiment <see cref="EmbodimentTickPhase.Cognition" /> tick, the solver chain
    ///         runs in <c>LateUpdate</c> (execution order <see cref="EmbodimentExecutionOrders.Gaze" />)
    ///         after the Animator has posed the skeleton and before the facial compositor
    ///         flushes. All behavior lives in the internal <c>Core</c> classes.
    ///     </para>
    ///     <para>
    ///         The current decision is published as a <see cref="GazeReading" /> through
    ///         <see cref="IGazeSource" /> on the character's embodiment context; every target
    ///         transition is traced (see <see cref="ConvaiGazeProfile.TraceVerbosity" />) and
    ///         mirrored to <see cref="TargetChanged" />. Call <see cref="CaptureSnapshot()" />
    ///         for a full live view.
    ///     </para>
    /// </remarks>
    [EmbodimentModule(ModuleIds.Gaze, "Gaze",
        Description = "Where the character looks — eye contact, glances, and attention.",
        Absence = "the eyes and head stay wherever the animation puts them, so the character never " +
                  "makes eye contact.",
        Order = 10)]
    [AddComponentMenu("Convai/Embodiment/Gaze")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EmbodimentExecutionOrders.Gaze)]
    [RequireComponent(typeof(GazeAttentionRequests))]
    public sealed class ConvaiGazeController :
        ConvaiCharacterModule<ConvaiGazeProfile>,
        IGazeSource,
        IGazeGlanceHandler,
        IEmbodimentTickable,
        IFacialBlendshapeSource
    {
        [SerializeField]
        [Tooltip("When enabled, a Gaze Player Anchor provider is created at runtime if the character has no target provider.")]
        private bool autoCreatePlayerAnchor = true;

        [SerializeField]
        [Tooltip("Optional: the transform this character treats as 'the player'. Empty = the main " +
                 "camera (XR rigs included). Set for split-screen, multiplayer, or cutscene rigs.")]
        private Transform playerAnchorOverride;

        [SerializeField]
        [Tooltip("How eye contact is governed. Natural: the profile's per-state policy table. " +
                 "Speaking Focus: player focus only while the character is producing speech. " +
                 "Conversation Lock: full commitment to the player anchor in every conversational " +
                 "(non-Idle) state, ambient idle life preserved. Always Lock: full commitment in " +
                 "every state including Idle. Scripted GazeAt() preempts Social focus; Exact " +
                 "rejects it unless Allow Scripted Overrides is enabled.")]
        private GazeEyeContactMode eyeContactMode = GazeEyeContactMode.Natural;

        [SerializeField]
        [Tooltip("Social keeps subtle fixation life while focused. Exact suppresses intentional look-aways and fixation offsets.")]
        private GazeFocusFidelity focusFidelity = GazeFocusFidelity.Social;

        [SerializeField]
        [Tooltip("Where on the player the character aims. Auto picks the camera when the anchor is a camera and the object's own origin otherwise, which is what almost every scene wants.")]
        private GazeAnchorAimMode playerAnchorAimMode = GazeAnchorAimMode.Auto;

        [SerializeField]
        [Tooltip("Anchor-local aim point used when Player Anchor Aim Mode is Local Offset.")]
        private Vector3 playerAnchorAimOffset;

        [SerializeField]
        [Tooltip("How the character turns its body to look at something behind it. Stepping Turn " +
                 "plays the body's own turn animation, which reads as a person turning but needs " +
                 "those clips and takes as long as they take. Smooth Rotation turns the character " +
                 "directly, which is instant to set up, never fights an animation, and is what many " +
                 "first-person and stylised games use. Only affects turns the gaze system asks for.")]
        private GazeBodyTurnStyle bodyTurnStyle = GazeBodyTurnStyle.SteppingTurn;

        [SerializeField]
        [Tooltip("Allow explicit GazeAt requests to preempt an active Exact focus. Off is recommended for kiosk and presenter use cases.")]
        private bool allowScriptedOverridesDuringExactFocus;

        [SerializeField]
        [Tooltip("While an eye-contact lock is in force, absorb glance-tier scripted requests " +
                 "(GlanceAt, referential glances) so nothing briefly pulls gaze off the player " +
                 "anchor. Explicit GazeAt() preempts Social focus; Exact follows its Allow " +
                 "Scripted Overrides setting.")]
        private bool lockBlocksGlances = true;

        private readonly List<IGazeTargetProvider> _providers = new(4);
        private readonly List<IGazeTargetProvider> _providerScratch = new(8);
        private readonly List<IGazeTargetProvider> _runtimeProviders = new(2);
        private readonly List<GazeTargetCandidate> _candidates = new(4);
        private readonly List<GazeTargetCandidate> _focusCandidates = new(1);

        /// <summary>
        ///     Normalized speech energy above which the character counts as "producing speech"
        ///     — the gate that hard-suppresses listening backchannel nods so the character
        ///     never nods over its own words.
        /// </summary>
        private const float CharacterSpeakingEnergyThreshold = 0.1f;

        /// <summary>
        ///     Priority of a <see cref="GlanceAt(Transform, float)" /> request. Strictly below
        ///     the default <see cref="GazeOptions.Priority" /> (0) so any explicit
        ///     <see cref="GazeAt(Transform, GazeOptions)" /> outranks a glance, yet above the
        ///     internal curiosity-glance tier (-100) so a scripted glance still wins over
        ///     ambient curiosity.
        /// </summary>
        private const int GlancePriority = -5;

        /// <summary>Minimum engagement floor while a target-loss search substitutes for the lost player target, so the head/eye stages still commit to the search fixations.</summary>
        private const float SearchEngagementFloor = 0.6f;

        private readonly GazeTargetArbiter _arbiter = new();
        private readonly GazePolicyEngine _policy = new();
        private readonly GazeFocusScopeEvaluator _focusScope = new();
        private readonly GazeScriptedRequests _scripted = new();
        private readonly List<string> _absorbedScratch = new(2);
        private readonly GazeChainCalibration _chain = new();
        private readonly HeadTorsoSolver _headTorso = new();
        private readonly AmbientExplorationDirector _ambient = new();
        private readonly EyeSolver _eyes = new();
        private readonly BlinkDirector _blink = new();
        private readonly FixationMicroMotion _micro = new();
        private readonly FaceScanDirector _faceScan = new();
        private readonly EyeBlendshapeWriter _eyeWriter = new();
        private readonly ReorientationDirector _reorientation = new();

        /// <summary>Owns the gaze shift as one event: its clock, and its division across the ladder.</summary>
        private readonly GazeShiftDirector _shiftDirector = new();

        /// <summary>The eyes' short drop-and-lift as the character comes to rest after a walk.</summary>
        private readonly ArrivalSettleDirector _arrivalSettle = new();

        /// <summary>This frame's shift requirement, measured once and shared by every stage.</summary>
        private GazeShiftMeasurement _shiftMeasurement;

        /// <summary>
        ///     What was engaged last frame. Only used to recognise the travel-path hand-off, so
        ///     arriving somewhere reads as one continuous movement rather than two.
        /// </summary>
        private GazeTargetKind _previousTargetKind = GazeTargetKind.None;
        private readonly AversionDirector _aversion = new();
        private readonly SearchDirector _search = new();
        private readonly BackchannelDirector _backchannel = new();
        private readonly InterruptionReactionDirector _interruptionReaction = new();
        private readonly TurnTakingDirector _turnTaking = new();
        private readonly HeadGestureArbiter _headGestureArbiter = new();
        private readonly EmotionGazeModulator _emotionModulator = new();
        private readonly PupilArousalModel _pupilArousal = new();
        private readonly BrowCueCoordinator _browCueCoordinator = new();
        private readonly ProxemicRegulator _proxemics = new();
        private readonly CuriosityGlanceDirector _curiosity = new();
        private readonly CharacterGlanceDirector _characterGlance = new();
        private readonly TravelGazeDirector _travel = new();
        private readonly GazeLodGovernor _lodGovernor = new();

        /// <summary>This tick's travel reading. <c>None</c> whenever nothing publishes travel.</summary>
        private TravelIntent _travelIntent = TravelIntent.None;

        /// <summary>Parent-local root position from the previous tick, for the provisioning probe.</summary>
        private Vector3 _lastLocalRootPosition;
        private bool _hasLastLocalRootPosition;
        private bool _travelIntentProvisioned;
        private bool _wasTraveling;

        private SkinnedMeshRenderer[] _renderers;
        private bool _lodSkipExpression;
        private DeterministicEmbodimentRandom _random;
        private DeterministicEmbodimentRandom _turnTakingRandom;
        private bool _useEyeBones;
        private bool _useLookShapes;
        private GazeTrace _trace;
        private GazeDirective _directive = GazeDirective.Disengaged;
        private PlayerAnchorTargetProvider _ownedPlayerAnchor;
        private CharacterGazeTargetProvider _characterGaze;
        private PlayerAttentionSensor _attentionSensor;
        private ConvaiCharacter _character;
        private bool _finalTranscriptPending;
        private int _finalTranscriptWordCount;
        private bool _finalTranscriptBlinkPending;

        /// <summary>
        ///     Delay (seconds) after the player's binary VAD falls (stops speaking) before the
        ///     blink-cluster cue opens — the boundary is felt a beat after the silence
        ///     starts, not on the raw edge itself.
        /// </summary>
        private const float PlayerPauseClusterDelaySeconds = 0.3f;

        // Blink clustering (Domain-event seam): cached delegate assigned once in the
        // constructor (never per OnEnable) so repeated enable/disable cycles never allocate a
        // fresh delegate — mirrors ConvaiBodyLanguageController's own Domain-event wiring.
        private readonly Action<PlayerSpeakingStateChanged> _handlePlayerSpeakingStateChanged;
        private IEventHub _subscribedEventHub;
        private SubscriptionToken _playerSpeakingToken;

        /// <summary>Last dialogue state observed by the Speaking-exit edge check (own small cache — see class remarks).</summary>
        private DialogueState _lastBlinkClusterState = DialogueState.Idle;
        private bool _hasBlinkClusterState;

        /// <summary>
        ///     Raw (unsmoothed) player-speaking flag from <see cref="PlayerSpeakingStateChanged" />,
        ///     reused by the listener mouth-bias face scan (FaceScanDirector does its own
        ///     ~0.5s smoothing of this flag).
        /// </summary>
        private bool _playerSpeaking;

        private bool _rigHandlerRegistered;

        // Latch for ValidateRig's log-once contract across rebinds (see ValidateRig).
        private IStandardRigBinding _rigWarningBinding;
        private bool _rigWarningReported;
        private IHeadGestureChannel _registeredHeadGestureChannel;
        private readonly GazeDiagnosticsReporter _diagnostics = new();
        private bool _tickRegistered;
        private bool _runtimeInitialized;
        private bool _focusActive;
        private Vector3 _lastFocusPoint;
        private bool _hasLastFocusPoint;
        private bool _focusDegraded;
        private bool _ownedPlayerAnchorFocusOnly;

        // Look-where-you-act: a glance at the target while a targeted action step
        // executes. See ActionPerformanceGazeReactor remarks.
        private ActionPerformanceGazeReactor _actionPerformanceReactor;

        /// <summary>Raised for every gaze target transition, mirroring the trace log.</summary>
        public event Action<GazeTargetChange> TargetChanged;

        /// <inheritdoc />
        public GazeReading Current { get; private set; } = GazeReading.None;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        Component IFacialBlendshapeSource.SourceComponent => this;

        string IFacialBlendshapeSource.SourceName => nameof(ConvaiGazeController);

        /// <inheritdoc />
        protected override string ProfileModuleId => ModuleIds.Gaze;

        /// <inheritdoc />
        protected override Func<ConvaiGazeProfile> DefaultProfileFactory => ConvaiGazeProfile.CreateDefault;

        /// <summary>Calibrated bone chain (internal seam for editor gizmos and tests).</summary>
        internal GazeChainCalibration Chain => _chain;

        /// <summary>
        ///     The profile this character is actually running on — the assigned one, or the
        ///     built-in defaults when none is assigned. Internal seam for the integrations that
        ///     live outside the component but answer to its tuning.
        /// </summary>
        internal ConvaiGazeProfile ActiveProfile => EffectiveProfile;

        internal GazeTrace Trace => _trace;

        internal GazeTargetStack ScriptedStack => _scripted.Stack;

        /// <summary>Whether the resolved eye backend drives eye bones (internal seam for the editor troubleshooter).</summary>
        internal bool EyeBackendUsesBones => _useEyeBones;

        /// <summary>Whether the resolved eye backend drives EyeLook* blendshapes (internal seam for the editor troubleshooter).</summary>
        internal bool EyeBackendUsesLookShapes => _useLookShapes;

        /// <summary>
        ///     Whether the head-gesture arbiter currently reports an external program active
        ///     (or still draining its post-completion refractory) — i.e. whether the backchannel
        ///     is being suppressed as this frame's no-double-nod mechanism (internal seam for
        ///     tests/diagnostics).
        /// </summary>
        internal bool HeadGestureExternalActive => _headGestureArbiter.ExternalActive;

        public ConvaiGazeController() =>
            _handlePlayerSpeakingStateChanged = OnPlayerSpeakingStateChanged;

        /// <inheritdoc />
        protected override void OnProfileApplied(ConvaiGazeProfile newProfile)
        {
            ConvaiGazeProfile effective = EffectiveProfile;
            if (_trace != null && effective != null)
                _trace.Verbosity = effective.TraceVerbosity;
            ConfigureOwnedPlayerAnchor();
            _trace?.State($"Profile applied: '{(newProfile != null ? newProfile.name : "(runtime default)")}'.");
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;

            ProvideService<IGazeSource>(this);
            ProvideService<IGazeGlanceHandler>(this);
            Context.EnsureTickScheduler()?.Register(this);
            _tickRegistered = true;

            // Registers unconditionally, like the seams above; the
            // dispatcher's own Performance toggle decides whether it is ever notified.
            _actionPerformanceReactor ??= new ActionPerformanceGazeReactor(this);
            ContributeService<IActionPerformanceReactor>(_actionPerformanceReactor);

            if (!UnityEngine.Application.isPlaying) return;

            _random = DeterministicEmbodimentRandom.Create(this);
            _turnTakingRandom = DeterministicEmbodimentRandom.Create(this, 0x475A5455u);
            Context.RigBindingChanged += HandleRigBindingChanged;
            _rigHandlerRegistered = true;

            Context.AddServiceChangedHandler<IHeadGestureChannel>(HandleHeadGestureChannelChanged);
            RegisterHeadGestureConsumer(Context.HeadGestureChannel);

            RefreshProviders();
            EnsurePlayerAnchorIfNeeded();
            ApplyPlayerAnchorOverride(clearWhenNull: false);
            ApplyPlayerAnchorAim();
            EnsureRuntimeInitialized();
            SubscribeToEventHub();
        }

        /// <summary>
        ///     Blink clustering: the player-VAD falling-edge trigger. A missing EventHub
        ///     (Context never populated with one — e.g. a bare test rig) leaves the subscription
        ///     unset, which degrades byte-identically to no clustering cue from this trigger —
        ///     the Speaking-exit and isFinal triggers are unaffected. Mirrors
        ///     <c>ConvaiBodyLanguageController.SubscribeToEventHub</c>.
        /// </summary>
        private void SubscribeToEventHub()
        {
            IEventHub hub = Context?.EventHub;
            if (hub == null) return;

            _playerSpeakingToken = hub.Subscribe<PlayerSpeakingStateChanged>(_handlePlayerSpeakingStateChanged);
            _subscribedEventHub = hub;
        }

        private void UnsubscribeFromEventHub()
        {
            IEventHub hub = _subscribedEventHub;
            if (hub == null) return;

            hub.Unsubscribe(_playerSpeakingToken);
            _playerSpeakingToken = default;
            _subscribedEventHub = null;
        }

        /// <summary>
        ///     Blink clustering, trigger (c): a falling edge (player just stopped speaking)
        ///     schedules the cluster cue ~300 ms later — the boundary is felt a beat after the
        ///     silence starts, not on the raw edge.
        /// </summary>
        private void OnPlayerSpeakingStateChanged(PlayerSpeakingStateChanged evt)
        {
            // Listener mouth-bias face scan: cache the raw flag for FaceScanDirector's own
            // smoothing (read at the FaceScan tick call site in SolveEyes).
            _playerSpeaking = evt.IsSpeaking;

            if (evt.IsSpeaking) return;
            _blink.NotifyDelayedClusterCue(PlayerPauseClusterDelaySeconds);
        }

        protected override void OnDisable()
        {
            UnsubscribeFromEventHub();
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

            if (Context != null)
                Context.RemoveServiceChangedHandler<IHeadGestureChannel>(HandleHeadGestureChannelChanged);
            RegisterHeadGestureConsumer(null);
            RegisterTranscriptSource(null);
            _finalTranscriptPending = false;
            _finalTranscriptWordCount = 0;
            _finalTranscriptBlinkPending = false;
            _hasBlinkClusterState = false;
            _lastBlinkClusterState = DialogueState.Idle;

            // Gaze source, glance handler and the action reactor were published through the base
            // class, which releases every token in base.OnDisable() below.
            _actionPerformanceReactor?.ReleaseHeldGaze();
            DestroyOwnedPlayerAnchor();
            _arbiter.Reset();
            _policy.Reset();
            _scripted.Reset();
            _chain.RestoreEyeRest();
            Context?.EnsureCompositor()?.ClearLayer(this, FacialBlendshapeLayers.Eyes);
            _chain.Clear();
            _headTorso.Reset();
            _ambient.Reset();
            _eyes.Reset();
            _faceScan.Reset();
            _eyeWriter.Clear();
            _reorientation.Reset();
            _shiftDirector.Reset();
            _arrivalSettle.Reset();
            _previousTargetKind = GazeTargetKind.None;
            _aversion.Reset();
            _search.Reset();
            _backchannel.Reset();
            _interruptionReaction.Reset();
            _turnTaking.Reset();
            _headGestureArbiter.Reset();
            _emotionModulator.Reset();
            _pupilArousal.Reset();
            _browCueCoordinator.Reset();
            _proxemics.Reset();
            _curiosity.Reset();
            _characterGlance.Reset();
            _travel.Reset();
            _travelIntent = TravelIntent.None;
            _hasLastLocalRootPosition = false;
            _travelIntentProvisioned = false;
            _wasTraveling = false;
            _lodGovernor.Reset();
            _lodSkipExpression = false;
            _directive = GazeDirective.Disengaged;
            Current = GazeReading.None;
            _diagnostics.Reset();
            _runtimeInitialized = false;
            _rigWarningBinding = null;
            _rigWarningReported = false;
            _focusActive = false;
            _focusScope.Reset();

            base.OnDisable();
        }

        /// <summary>
        ///     Claims/releases the head-gesture channel slot as its registered instance changes
        ///     (including going null on disable, or when Body Language enables/disables at
        ///     runtime). Idempotent by construction: registering the same instance twice, or
        ///     unregistering when nothing is registered, are both safe no-ops on the channel
        ///     side (see <see cref="IHeadGestureChannel" />), so this never double-registers or
        ///     throws regardless of call order.
        /// </summary>
        private void RegisterHeadGestureConsumer(IHeadGestureChannel channel)
        {
            if (_registeredHeadGestureChannel == channel) return;

            _registeredHeadGestureChannel?.UnregisterConsumer(this);
            _registeredHeadGestureChannel = channel;
            _registeredHeadGestureChannel?.RegisterConsumer(this);
        }

        private void HandleHeadGestureChannelChanged(IHeadGestureChannel channel) =>
            RegisterHeadGestureConsumer(channel);

        /// <summary>
        ///     Subscribes to the character's transcript stream for the turn-taking floor-yield
        ///     cue, re-resolving idempotently exactly like <see cref="RegisterHeadGestureConsumer" />
        ///     — a rescan finding the same instance (or no character at all) is a safe no-op.
        /// </summary>
        private void RegisterTranscriptSource(ConvaiCharacter character)
        {
            if (_character == character) return;

            if (_character != null) _character.OnTranscriptReceived -= HandleTranscriptReceived;
            _character = character;
            if (_character != null) _character.OnTranscriptReceived += HandleTranscriptReceived;
        }

        /// <summary>
        ///     Latches a final-transcript pulse for <see cref="TurnTakingDirector" /> to consume
        ///     on its next tick; interim (non-final) transcripts are ignored, matching
        ///     <see cref="Providers.GazeReferentialGlances" />'s own final-only handling.
        /// </summary>
        private void HandleTranscriptReceived(string text, bool isFinal)
        {
            if (!isFinal) return;
            _finalTranscriptBlinkPending = true;
            DialogueState state = Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            if (!ShouldLatchFinalTranscriptForTurn(state)) return;
            _finalTranscriptPending = true;
            _finalTranscriptWordCount = CountTranscriptWords(text);
        }

        internal static bool ShouldLatchFinalTranscriptForTurn(DialogueState state) =>
            state == DialogueState.Thinking || state == DialogueState.Speaking;

        internal static bool ShouldClearPendingTurnTranscript(DialogueState state) =>
            state != DialogueState.Thinking && state != DialogueState.Speaking;

        internal static int CountTranscriptWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            bool insideWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                bool apostropheInsideWord = (current == '\'' || current == '’') && insideWord &&
                                            i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]);
                bool wordCharacter = char.IsLetterOrDigit(current) || apostropheInsideWord;
                if (wordCharacter && !insideWord) count++;
                insideWord = wordCharacter;
            }
            return count;
        }

        /// <summary>
        ///     Fills <paramref name="results" /> with every optional gaze capability and whether
        ///     this character currently has it — the supported way to answer "what is this
        ///     character missing?" without reflecting over component types.
        /// </summary>
        /// <remarks>
        ///     Adding <see cref="ConvaiGazeController" /> already gives a character eyes, a head,
        ///     idle life, blinking, body turns and conversational rhythm. The capabilities reported
        ///     here are the further ones that each live behind their own small component; none is
        ///     created automatically. See <see cref="GazeCapabilities" /> for why.
        /// </remarks>
        public void CaptureCapabilities(List<GazeCapabilityInfo> results) =>
            GazeCapabilities.Evaluate(Context != null ? Context.CharacterRoot : transform.root, results);

        /// <summary>Rescans the character hierarchy for target providers.</summary>
        public void RefreshProviders()
        {
            _providers.Clear();
            Transform root = Context != null ? Context.CharacterRoot : transform.root;
            if (root == null) return;

            _providerScratch.Clear();
            root.GetComponentsInChildren(true, _providerScratch);
            for (int i = 0; i < _providerScratch.Count; i++)
                _providers.Add(_providerScratch[i]);
            _providerScratch.Clear();

            // The character-gaze provider is polled specially (it yields one candidate per
            // OTHER registered character, not a single self-candidate), so it is cached here
            // rather than added to the IGazeTargetProvider list.
            _characterGaze = root.GetComponentInChildren<CharacterGazeTargetProvider>(true);

            // The attention sensor is not a target provider — it feeds curiosity-glance
            // reciprocation (E8) and the live HUD, so it is cached the same way.
            _attentionSensor = root.GetComponentInChildren<PlayerAttentionSensor>(true);

            RefreshRendererCache(root);

            // The character's transcript stream feeds the turn-taking floor-yield cue —
            // only subscribed at runtime, mirroring the other runtime-only event handlers below.
            if (UnityEngine.Application.isPlaying)
                RegisterTranscriptSource(root.GetComponentInChildren<ConvaiCharacter>(true));
        }

        /// <summary>
        ///     Re-caches the character's skinned renderers for the crowd-LOD off-screen check.
        ///     Called from <see cref="RefreshProviders" /> and on every rig rebind, because a mesh
        ///     swap (outfit change, LOD mesh, addressable skin) destroys every cached renderer and
        ///     a stale cache would otherwise read as "off-screen" forever.
        /// </summary>
        private void RefreshRendererCache(Transform root)
        {
            if (root == null) return;
            _renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        /// <summary>
        ///     Re-resolves the eye output backend (eye bones vs. <c>EyeLook*</c> blendshapes)
        ///     from the profile's <see cref="ConvaiGazeProfile.EyeActuationMode" />. Call this
        ///     after changing that mode at runtime.
        /// </summary>
        public void RefreshEyeBackend() => ResolveEyeBackend(EffectiveProfile);

        // ------------------------------------------------------------------ scripted gaze

        /// <summary>
        ///     Directs gaze at a (moving) transform. Scripted requests outrank all automatic
        ///     targets and work in any dialogue state when
        ///     <see cref="GazeOptions.Engagement" /> is set explicitly. Await
        ///     <see cref="GazeHandle.Settled" /> to gate follow-up work (e.g. a pick-up
        ///     action) on the character visibly looking first.
        /// </summary>
        public GazeHandle GazeAt(Transform target, GazeOptions options = default)
        {
            if (target == null) return null;
            return GazeAtInternal(target, target.position, hasTransform: true, target.name, options);
        }

        /// <summary>Directs gaze at a world-space point. See <see cref="GazeAt(Transform, GazeOptions)" />.</summary>
        public GazeHandle GazeAt(Vector3 worldPoint, GazeOptions options = default) =>
            GazeAtInternal(null, worldPoint, hasTransform: false, "point", options);

        /// <summary>
        ///     Glances at a (moving) transform briefly, then returns to whatever the policy
        ///     dictates — the one-line "look there for a moment". A glance is a committed but
        ///     low-priority scripted request: any explicit
        ///     <see cref="GazeAt(Transform, GazeOptions)" /> outranks it, it never turns the
        ///     body, and the policy target resumes automatically when the hold ends. While an
        ///     eye-contact lock is in force (<see cref="EyeContactMode" />) with
        ///     <see cref="LockBlocksGlances" /> on, the glance is absorbed: the returned handle
        ///     is already completed (unsettled) and gaze never leaves the player anchor.
        /// </summary>
        /// <param name="target">Transform to glance at (a <c>null</c> target is a no-op).</param>
        /// <param name="durationSeconds">Hold duration, clamped to at least 0.2 s so the eyes can visibly land it.</param>
        public GazeHandle GlanceAt(Transform target, float durationSeconds = 1.2f)
        {
            if (target == null) return null;
            if (TryAbsorbGlance(target.name, out GazeHandle absorbed)) return absorbed;
            return GazeAt(target, GlanceOptions(durationSeconds));
        }

        /// <summary>Glances at a world-space point briefly. See <see cref="GlanceAt(Transform, float)" />.</summary>
        public GazeHandle GlanceAt(Vector3 worldPoint, float durationSeconds = 1.2f)
        {
            if (TryAbsorbGlance("point", out GazeHandle absorbed)) return absorbed;
            return GazeAt(worldPoint, GlanceOptions(durationSeconds));
        }

        /// <summary>
        ///     <see cref="IGazeGlanceHandler" /> entry point: a cross-module glance
        ///     request (e.g. Body Animation, when the character starts pointing at something)
        ///     routes through the same <see cref="GlanceAt(Vector3, float)" /> path scripted
        ///     callers use.
        /// </summary>
        void IGazeGlanceHandler.RequestGlance(Vector3 worldPosition, float durationSeconds) =>
            GlanceAt(worldPosition, durationSeconds);

        /// <summary>
        ///     Absorbs a glance at the door while the eye-contact lock is in force and
        ///     <see cref="LockBlocksGlances" /> is on: the stack is never touched and the
        ///     caller receives an already-completed (unsettled) handle, so composed code that
        ///     awaits <see cref="GazeHandle.Completion" /> proceeds immediately.
        /// </summary>
        private bool TryAbsorbGlance(string name, out GazeHandle absorbed)
        {
            absorbed = null;
            if (!lockBlocksGlances) return false;

            DialogueState state = Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            if (!_focusActive && !IsLockActive(eyeContactMode, state)) return false;

            absorbed = new GazeHandle(this, entryId: 0, name) { Outcome = GazeOutcome.HeldEyeContactInstead };
            absorbed.MarkCompleted();
            _trace?.State($"Glance '{name}' absorbed by the eye-contact lock.");
            return true;
        }

        private static GazeOptions GlanceOptions(float durationSeconds) => new()
        {
            Priority = GlancePriority,
            HoldSeconds = Mathf.Max(0.2f, durationSeconds),
            Engagement = 1f,          // glances are committed — the brevity is the modifier
            AllowBodyTurn = false     // a glance never turns the body
        };

        /// <summary>Releases every scripted gaze request.</summary>
        public void ReleaseAllScriptedGaze()
        {
            _scripted.ReleaseAll();
            _trace?.State("All scripted gaze requests released.");
        }

        internal void ReleaseGaze(GazeHandle handle)
        {
            if (handle == null) return;

            bool removed = _scripted.Release(handle);
            if (removed)
                _trace?.State($"Scripted gaze '{handle.TargetName}' released.");
        }

        private GazeHandle GazeAtInternal(
            Transform target,
            Vector3 point,
            bool hasTransform,
            string name,
            GazeOptions options)
        {
            if (_focusActive && focusFidelity == GazeFocusFidelity.Exact &&
                !allowScriptedOverridesDuringExactFocus)
            {
                var rejected = new GazeHandle(this, entryId: 0, name);
                rejected.MarkCompleted();
                _trace?.State($"GazeAt '{name}' rejected by Exact focus.");
                return rejected;
            }

            float deadline = options.HoldSeconds > 0f
                ? Time.time + options.HoldSeconds
                : float.PositiveInfinity;
            float engagementOverride = options.Engagement > 0f ? Mathf.Clamp01(options.Engagement) : -1f;

            GazeHandle handle = _scripted.Push(
                this, target, point, hasTransform, options.Priority,
                engagementOverride, options.AllowBodyTurn, deadline, name);

            _trace?.State(
                $"GazeAt '{name}' (priority {options.Priority}, hold " +
                $"{(options.HoldSeconds > 0f ? options.HoldSeconds.ToString("0.0") + "s" : "until released")}, " +
                $"engagement {(engagementOverride > 0f ? engagementOverride.ToString("0.00") : "policy")}, " +
                $"bodyTurn {options.AllowBodyTurn}).");
            return handle;
        }

        /// <summary>
        ///     Latches this tick's scripted winner and expires handles whose stack entry is
        ///     gone (hold elapsed, or the target transform died). Settlement itself is decided
        ///     later, against the solved contact error — see
        ///     <see cref="ProcessScriptedSettlement(float)" />. Internal seam so tests can
        ///     drive it without play mode.
        /// </summary>
        internal void ProcessScriptedHandles(in GazeTargetDecision decision)
        {
            _scripted.ProcessDecision(in decision);
        }

        private void ProcessScriptedSettlement()
        {
            float error = _eyes.ContactErrorDegrees;
            if (float.IsNaN(error) && _directive.HasEngagedTarget)
                error = ComputeHeadFacingError(
                    _chain.CurrentEyeRestForward,
                    _directive.WorldPoint - _chain.HeadPivotPosition);
            ProcessScriptedSettlement(error);
        }

        internal static float ComputeHeadFacingError(Vector3 forward, Vector3 toTarget)
        {
            if (forward.sqrMagnitude <= 1e-8f || toTarget.sqrMagnitude <= 1e-8f)
                return float.NaN;
            return Vector3.Angle(forward, toTarget);
        }

        internal void ProcessScriptedSettlement(float contactErrorDegrees)
        {
            _scripted.ProcessSettlement(contactErrorDegrees);
        }

        // ------------------------------------------------------------------ providers

        /// <summary>Registers a non-component provider (systems, netcode, tests).</summary>
        public void RegisterTargetProvider(IGazeTargetProvider provider)
        {
            if (provider == null || _runtimeProviders.Contains(provider)) return;
            _runtimeProviders.Add(provider);
        }

        /// <summary>Unregisters a provider added via <see cref="RegisterTargetProvider" />.</summary>
        public void UnregisterTargetProvider(IGazeTargetProvider provider) =>
            _runtimeProviders.Remove(provider);

        /// <summary>
        ///     Cognition tick: targeting and policy. Solvers run later in
        ///     <see cref="LateUpdate" /> against the freshly animated pose.
        /// </summary>
        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (!UnityEngine.Application.isPlaying || Context == null) return;

            EnsureRuntimeInitialized();
            ConvaiGazeProfile profile = EffectiveProfile;
            if (profile == null) return;

            DialogueState state = Context.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            bool characterSpeaking = _character != null && _character.IsSpeaking;
            ISpeechEnergyProvider speech = Context.SpeechEnergyProvider;
            characterSpeaking |= speech != null && speech.Current > CharacterSpeakingEnergyThreshold;
            _focusActive = _focusScope.Evaluate(eyeContactMode, state, characterSpeaking, deltaTime);

            // E10 crowd LOD: focused characters remain full-rate so camera/HMD motion never
            // turns a product-level focus promise into visibly stepped tracking.
            // solver stage. Skipped cognition ticks accumulate their dt so the executed tick
            // advances springs/ramps by the full elapsed time.
            if (profile.EnableGazeLod && !_focusActive)
            {
                bool anyVisible = AnyRendererVisible();
                bool runCognition = _lodGovernor.TickCognition(
                    profile, ResolvePlayerDistance(), anyVisible, deltaTime,
                    out float lodDeltaTime, out bool skipExpression);
                _lodSkipExpression = skipExpression;
                if (!runCognition) return;
                deltaTime = lodDeltaTime;
            }
            else
            {
                _lodSkipExpression = false;
            }

            bool locked = _focusActive;
            GazeStatePolicy statePolicy = locked
                ? GazeStatePolicy.LockedToPlayer(state)
                : profile.GetStatePolicy(state);

            IEmotionStateFrameSource frameSource = Context.EmotionStateFrameSource;
            if (frameSource != null)
            {
                EmotionStateFrame frame = frameSource.CurrentFrame;
                _emotionModulator.Tick(profile, in frame);
            }
            else
            {
                EmotionReading emotion = Context.EmotionStateSource?.Current ?? EmotionReading.Neutral;
                _emotionModulator.Tick(profile, in emotion);
            }
            // The lock promises full commitment no matter what — an authored emotion
            // modifier must not silently scale it back down. Blink-rate modulation stays
            // (blinks are life, not contact), and aversion is already zero in the locked
            // policy so its modifier is inert.
            _policy.EngagementModifier = locked ? 1f : _emotionModulator.EngagementScale;
            _policy.AversionModifier = _emotionModulator.AversionScale;

            // Proxemic intimacy regulation: ticked unconditionally (even while locked) so the
            // smoothed closeness factor stays continuous and never jumps the instant a lock
            // releases — the lock bypass is enforced at each consumption point instead (aversion
            // floor below in LateUpdate, face-scan scale in SolveEyes, blink scale right here).
            bool hasPlayerDistance = TryResolvePlayerAnchor(out _);
            _proxemics.Tick(
                profile.EnableProxemicRegulation, hasPlayerDistance,
                hasPlayerDistance ? ResolvePlayerDistance() : 0f,
                profile.ProxemicCloseDistanceMeters, profile.ProxemicIntensity, deltaTime);

            _blink.RateScale = _emotionModulator.BlinkRateScale * (locked ? 1f : _proxemics.BlinkRateScale);

            // Travel is read before anything competes for attention, so this tick's candidates and
            // policy both see the same answer to "are we going somewhere?".
            EnsureTravelIntentIfMoving(profile);
            _travelIntent = Context.TravelIntentSource?.Current ?? TravelIntent.None;
            bool traveling = profile.EnableTravelGaze && _travelIntent.IsTraveling;

            // The eyes' settle beat is driven off the raw travel reading, not off `traveling`:
            // it must still play for a character whose travel gaze is switched off, because
            // coming to rest is a thing bodies do, not a gaze feature.
            _arrivalSettle.Tick(
                _travelIntent.IsTraveling,
                profile.ArrivalSettleEyeDropDegrees,
                profile.ArrivalSettleSeconds,
                deltaTime);

            // Rising edge only: a step that started as "look at this" becomes "go there" the moment
            // the character sets off, and the held stare has to be handed over exactly once.
            if (traveling && !_wasTraveling)
                _actionPerformanceReactor?.OnTravelStarted();
            _wasTraveling = traveling;

            if (traveling && !locked)
            {
                // The movement system owns the character's facing while it walks. A gaze-driven body
                // turn on top of that is two systems writing yaw at once, so the reorientation
                // director is stood down for the duration rather than left to fight the path.
                statePolicy.AllowBodyTurn = false;
                statePolicy.HeadContribution *= profile.TravelHeadContributionScale;
            }

            GatherCandidates(profile, deltaTime);
            TickTravelCheckIn(profile, state, deltaTime);
            TickCuriosityGlance(profile, statePolicy, deltaTime);
            TickCharacterGlance(profile, statePolicy, deltaTime);
            if (locked && lockBlocksGlances)
                SuppressGlanceTierRequests();
            GazeTargetStack.Entry scripted = _scripted.ResolveActive(Time.time);
            if (locked && focusFidelity == GazeFocusFidelity.Exact &&
                !allowScriptedOverridesDuringExactFocus)
            {
                RejectScriptedRequestsForExactFocus();
                scripted = null;
            }

            IReadOnlyList<GazeTargetCandidate> arbitrationCandidates = _candidates;
            if (ShouldUseFocusedPlayerCandidates(locked, scripted != null))
            {
                _focusCandidates.Clear();
                PlayerAnchorTargetProvider anchor = FindActivePlayerAnchorProvider();
                if (anchor == null && ShouldProvisionPlayerAnchor(
                        autoCreatePlayerAnchor, FindPlayerAnchorProvider() != null, _providers.Count,
                        _runtimeProviders.Count, focusActive: true))
                {
                    _ownedPlayerAnchorFocusOnly = _providers.Count > 0 || _runtimeProviders.Count > 0;
                    anchor = CreateOwnedPlayerAnchor("Focus mode provisioned a dedicated player anchor.");
                }
                if (anchor != null && anchor.TryGetFocusCandidate(out GazeTargetCandidate focusCandidate))
                {
                    _focusCandidates.Add(focusCandidate);
                    _lastFocusPoint = focusCandidate.WorldPoint;
                    _hasLastFocusPoint = true;
                    _focusDegraded = false;
                }
                else if (_hasLastFocusPoint)
                {
                    _focusCandidates.Add(new GazeTargetCandidate(
                        GazeTargetKind.Player, int.MaxValue, 1f, null, _lastFocusPoint,
                        "Last known player focus"));
                    _focusDegraded = true;
                }
                else
                {
                    _focusDegraded = true;
                }

                // The arbiter intentionally holds a lost target for Natural gaze. A focus
                // contract must never inherit that unrelated ownership when no player point
                // has ever been resolved, so clear its state before ticking the empty list.
                if (ShouldResetArbiterForMissingFocus(locked, _focusCandidates.Count, _hasLastFocusPoint))
                    _arbiter.Reset();

                _diagnostics.ReportFocusDegraded(_trace, _focusDegraded);
                arbitrationCandidates = _focusCandidates;
            }
            else if (!locked)
            {
                _focusDegraded = false;
            }

            GazeTargetDecision decision = _arbiter.Tick(
                arbitrationCandidates, scripted, statePolicy.AllowPlayerTarget, profile, deltaTime);

            _directive = _policy.Tick(in statePolicy, in decision, profile, deltaTime);

            if (locked)
                _search.Abort();
            else
                TickTargetLossSearch(profile, in decision, state, deltaTime);

            ProcessScriptedHandles(in decision);
            PublishReading(profile, in decision);
            TraceTargetTransitions(in decision);
            TracePlayerLineOfSight();
        }

        /// <summary>
        ///     Whether the eye-contact lock is in force for <paramref name="state" /> under
        ///     <paramref name="mode" />. Pure — tests pin the full mode × state matrix.
        /// </summary>
        internal static bool IsLockActive(GazeEyeContactMode mode, DialogueState state) => mode switch
        {
            GazeEyeContactMode.AlwaysLock => true,
            GazeEyeContactMode.ConversationLock => state != DialogueState.Idle,
            GazeEyeContactMode.SpeakingFocus => state == DialogueState.Speaking,
            _ => false
        };

        /// <summary>
        ///     Drops every glance-tier scripted entry (priority below the explicit
        ///     <see cref="GazeAt(Transform, GazeOptions)" /> default of 0 — glances, curiosity,
        ///     character glances) while the eye-contact lock is in force, completing their
        ///     handles unsettled. Explicit requests are untouched: a direct <c>GazeAt()</c> is
        ///     deliberate developer intent and stays sovereign over the lock. This is the
        ///     runtime-flip complement of the door check in <see cref="GlanceAt(Transform, float)" /> —
        ///     it purges requests that were already held when the lock engaged.
        /// </summary>
        private void SuppressGlanceTierRequests()
        {
            if (!_scripted.SuppressGlanceTier(_absorbedScratch)) return;

            for (int i = 0; i < _absorbedScratch.Count; i++)
                _trace?.State($"Glance '{_absorbedScratch[i]}' absorbed by the eye-contact lock.");
        }

        /// <summary>
        ///     Edge-triggered occlusion trace: explains WHY the player target dropped when the
        ///     line-of-sight check is on (the arbiter's own transition trace only reports the
        ///     loss). Never logs per-tick — only on the occluded/visible transitions.
        /// </summary>
        private void TracePlayerLineOfSight()
        {
            PlayerAnchorTargetProvider anchor = FindActivePlayerAnchorProvider();
            _diagnostics.ReportPlayerLineOfSight(_trace, anchor != null && anchor.LineOfSightOccluded);
        }

        /// <remarks>
        ///     Solver chain entry point. Runs after the Animator/PlayableGraph has posed the
        ///     skeleton this frame (execution order <see cref="EmbodimentExecutionOrders.Gaze" />)
        ///     so bone writes survive into rendering, and before the facial compositor flush
        ///     (order 20000) so eyelid/blink weights land in the same frame.
        /// </remarks>
        private void LateUpdate()
        {
            if (!UnityEngine.Application.isPlaying || Context == null) return;
            if (!_runtimeInitialized) return;

            ConvaiGazeProfile profile = EffectiveProfile;
            if (profile == null || !_chain.IsBound || !_chain.HasHeadChain) return;

            // E10: while off-screen, skip the whole solver stage (no solves, no bone/blendshape
            // writes). With an Animator present the pose is overwritten anyway; without one the
            // last write persists off-screen, which is invisible by definition.
            if (_lodSkipExpression) return;

            float deltaTime = Time.deltaTime;
            ResampleLiveTargetPoint();
            bool ambientActive = !_directive.HasEngagedTarget && profile.EnableAmbientExploration;
            _ambient.Tick(profile, deltaTime, ambientActive, ref _random);

            // Is there still an idle fixation to hand over? The boolean above flips a whole frame
            // before the ladder has any share to give — the head's onset has not elapsed — so on
            // its own it drops the fixation and the head starts the wrong way before turning out.
            // While the look is not fully taken up (acquiring, or being released) the head goes
            // on holding the fixation until it joins the look. Only the head stage reads this:
            // the eyes are ballistic by design and must keep jumping.
            //
            // HasResumableFixation is the second half of the question, and it is not optional:
            // the director clears its angles to zero once the resume window has expired, and a
            // cleared zero is not a fixation to return to — it is an instruction to face front.
            // Handing the head back to one mid-conversation reads as the character briefly
            // looking away from you and snapping back.
            bool ambientHandover = profile.EnableAmbientExploration && !ambientActive &&
                                   _ambient.HasResumableFixation &&
                                   _directive.TargetCommitment < 0.9999f;

            DialogueState dialogueState = Context.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            ISpeechEnergyProvider speech = Context.SpeechEnergyProvider;
            bool characterSpeaking = speech != null && speech.Current > CharacterSpeakingEnergyThreshold;
            bool hasSpeechActivitySignal = _character != null;
            bool speechActive = hasSpeechActivitySignal && _character.IsSpeaking;
            characterSpeaking |= speechActive;

            // Blink clustering, trigger (a): the Speaking-exit edge, from this director's own
            // small last-state cache (not TurnTakingDirector's — that one is a separate,
            // guaranteed forced blink for the floor-yield beat, not a probability spike).
            if (_hasBlinkClusterState && _lastBlinkClusterState == DialogueState.Speaking && dialogueState != DialogueState.Speaking)
                _blink.NotifyClusterCue();
            _lastBlinkClusterState = dialogueState;
            _hasBlinkClusterState = true;

            // Turn-taking gaze choreography: planning break / floor-yield bookkeeping,
            // ticked before the aversion director so this tick's break decision can drive it
            // (see TurnTakingDirector remarks). The lock check reuses the same predicate the
            // Cognition tick used to build this frame's statePolicy.
            bool eyeContactLocked = _focusActive;
            bool exactFocus = eyeContactLocked && focusFidelity == GazeFocusFidelity.Exact;
            bool finalTranscriptForTurn = _finalTranscriptPending && dialogueState == DialogueState.Speaking;
            if (_finalTranscriptPending && ShouldClearPendingTurnTranscript(dialogueState))
            {
                _finalTranscriptPending = false;
                _finalTranscriptWordCount = 0;
            }
            _turnTaking.Tick(
                dialogueState, profile, eyeContactLocked, finalTranscriptForTurn,
                _finalTranscriptWordCount, hasSpeechActivitySignal, speechActive,
                speech != null ? speech.Current : 0f, deltaTime, ref _turnTakingRandom);
            if (eyeContactLocked)
                _turnTaking.CancelPlanningBreak();

            // Blink clustering, trigger (b): reuses the same final-transcript pulse
            // TurnTakingDirector just consumed above — no second transcript subscription.
            if (_finalTranscriptBlinkPending)
                _blink.NotifyClusterCue();
            _finalTranscriptBlinkPending = false;
            if (finalTranscriptForTurn)
            {
                _finalTranscriptPending = false;
                _finalTranscriptWordCount = 0;
            }

            if (_turnTaking.PlanningBreakStarted && !exactFocus)
                _aversion.ForceBeat(
                    _turnTaking.StartedAversionMode,
                    _turnTaking.PlanningBreakDurationSeconds,
                    _turnTaking.StartedAversionStrength,
                    ref _turnTakingRandom);

            // While a turn-taking break is active it drives the aversion director with its
            // authored kind (opening cognitive vs. mid-turn natural); otherwise TurnTakingDirector
            // suppresses ordinary Speaking aversion so there is exactly one cadence owner.
            GazeAversionMode aversionMode = _directive.AversionMode;
            float aversionStrength = _directive.AversionStrength;
            // Emotional gaze signature: the active emotion's beat-direction bias, unless a
            // turn-taking is forcing a beat: its opening/mid-turn shape is intentional and
            // remains independent of the dominant emotion.
            GazeAversionBias aversionBias = _emotionModulator.AversionBias;
            if (_turnTaking.PlanningBreakActive)
            {
                aversionMode = _turnTaking.StartedAversionMode;
                aversionStrength = _turnTaking.StartedAversionStrength;
                aversionBias = GazeAversionBias.CognitiveDefault;
            }
            else
            {
                // Proxemic intimacy regulation: a close player raises the aversion floor
                // (max, never lowers an already-higher authored/state strength) so contact
                // softens instead of staring harder — bypassed entirely while the eye-contact
                // lock is in force (a kiosk keeps staring; LockedToPlayer's own strength is
                // already 0 regardless).
                bool turnTakingOwnsSpeaking = dialogueState == DialogueState.Speaking &&
                                              profile.EnableTurnTakingGaze;
                aversionStrength = ComposeNaturalSpeakingAversion(
                    aversionStrength,
                    _turnTaking.AversionSuppressionFactor,
                    _proxemics.AversionFloor,
                    applyProxemicFloor: !eyeContactLocked,
                    turnTakingOwnsSpeaking);
            }

            _aversion.Tick(aversionMode, aversionStrength, aversionBias, _directive.HasEngagedTarget, deltaTime, ref _random);

            // Floor-yield engagement pin: hold engagement at 1 for the pin's duration, exactly
            // like the target-loss search's engagement floor (Cognition tick) — mutating the
            // frame-local directive only affects this frame's expression output.
            if (_turnTaking.YieldEngagementPinActive)
            {
                _directive.Engagement = Mathf.Max(_directive.Engagement, 1f);
                // Pinned on both, or the pin would raise the eye/gate value while the ladder —
                // which divides the shift by the settled value — kept dividing the old one.
                _directive.SettledEngagement = Mathf.Max(_directive.SettledEngagement, 1f);
            }

            // Interruption startle beat: one-shot on the Speaking → Interrupted edge.
            // Ticked here (not Cognition) so its pulses are consumed the same frame by the
            // head-tilt and eye/blink stages below.
            _interruptionReaction.Tick(dialogueState, profile, deltaTime, ref _random);

            // Sense the external head-gesture channel BEFORE ticking the backchannel: the
            // arbiter's no-double-nod mechanism needs this tick's freshest external-active
            // state folded into the backchannel's own suppression input below, not last
            // frame's (see HeadGestureArbiter.SenseExternal remarks).
            _headGestureArbiter.SenseExternal(Context.HeadGestureChannel, _aversion.IsAverting, deltaTime);

            // Suppress (pause without re-arm) while the character produces speech — it must
            // never nod over its own words — while there is no engaged target: nodding at
            // nobody (player out of range or line-of-sight lost) reads as a glitch — or while
            // an external head-gesture program is active (or in its post-completion
            // refractory): this is the arbiter's no-double-nod mechanism, reusing the
            // director's own shipped cancel-fade path rather than any new cancellation logic.
            bool nodSuppressed = characterSpeaking || !_directive.HasEngagedTarget || _headGestureArbiter.ExternalActive;
            _backchannel.Tick(
                profile, dialogueState == DialogueState.Listening, nodSuppressed, deltaTime, ref _random);

            _headGestureArbiter.Compose(_backchannel.GestureOffset);

            // ---- The gaze shift, measured once and divided once. ----
            //
            // Order is load-bearing: measure what the shift requires from the rig, hand that
            // one number to the actuator ladder, then let each actuator execute the share it
            // was given. Every stage of this chain used to measure and decide for itself, and
            // the three answers were free to disagree — see GazeActuatorLadder.
            bool hasShift = _directive.HasEngagedTarget &&
                            _chain.TryMeasureShift(_directive.WorldPoint, out _shiftMeasurement);
            if (!hasShift) _shiftMeasurement = default;

            GazeShiftPlan shiftPlan = hasShift
                ? _shiftDirector.Plan(
                    in _shiftMeasurement,
                    profile,
                    // The settled strength, NOT the acquire/release ramp. The ladder's share is
                    // proportional to what it is handed, so handing it the ramp made the head's
                    // goal ramp too — and a ramped goal is tracked, not shaped: the head followed
                    // it at the ramp's speed, skipping the duration law entirely. The actuator
                    // needs to see where the look is going in order to decide how long getting
                    // there should take. See GazeDirective.SettledEngagement.
                    _directive.SettledEngagement,
                    _directive.HeadContribution,
                    _chain.HasTorso,
                    _directive.AllowBodyTurn,
                    _directive.GenerationId,
                    deltaTime,
                    // Last frame's achieved pose. Comfort is about what the character is
                    // holding, which only the previous frame can report.
                    (_eyes.LeftEyeAngles + _eyes.RightEyeAngles).magnitude * 0.5f,
                    _headTorso.HeadAngles.x,
                    // Arriving is one movement. Handing the path a walking character was
                    // watching over to whatever is at the end of it must not restart the
                    // cascade — that put a second onset freeze right at the moment the
                    // character reaches you and turns.
                    _previousTargetKind == GazeTargetKind.TravelPath)
                : GazeShiftPlan.Idle;
            _previousTargetKind = _directive.HasEngagedTarget ? _directive.Kind : GazeTargetKind.None;

            // Ticked BEFORE the actuators so this frame's relief reflects this frame's turn.
            // Read after them, the relief was a frame stale at both ends of every turn: the
            // neck stayed extended into the first frame of a turn and snapped back a frame
            // after it ended.
            _reorientation.Tick(
                bodyTurnStyle == GazeBodyTurnStyle.SteppingTurn ? Context.ReorientationHandler : null,
                profile,
                in _directive,
                _shiftMeasurement.RequiredYaw,
                shiftPlan.WantsFeet,
                _chain.Root != null ? _chain.Root : transform,
                Context.CharacterRoot != null ? Context.CharacterRoot : transform,
                deltaTime,
                _trace);

            var input = new HeadTorsoSolveInput
            {
                Chain = _chain,
                Profile = profile,
                DeltaTime = deltaTime,
                TargetPoint = _directive.WorldPoint,
                HasTarget = hasShift,
                Measurement = _shiftMeasurement,
                Plan = shiftPlan,
                Engagement = _directive.Engagement,
                AmbientAngles = _ambient.CurrentAngles,
                AmbientActive = ambientActive,
                AmbientHandover = ambientHandover,
                // The scale is applied unconditionally: the director holds it at 1 whenever no
                // planning break is running, including outside Speaking, so there is nothing left
                // for a state test to decide. Gating it on the state instead made the term step
                // from the cancelled break's scale straight back to 1 on the Speaking-exit edge,
                // with the beat's residue still on the offset — a gain step on a channel composed
                // downstream of the actuator, which is a pose step by another name.
                AversionOffset = ResolveFocusAversionOffset(
                    _aversion.Offset * _turnTaking.HeadParticipationScale, eyeContactLocked),
                // The floor-yield head dip is composed on top of the arbiter's own output — like
                // the interruption tilt below — so it plays even while an external Body Language
                // head-gesture program owns Compose()'s backchannel-vs-external decision.
                GestureOffset = exactFocus
                    ? Vector2.zero
                    : _headGestureArbiter.Offset + _turnTaking.YieldHeadDipOffset,
                GestureRollDegrees = exactFocus
                    ? 0f
                    : _headGestureArbiter.RollDegrees + _interruptionReaction.TiltDegrees,
                BodyTurnActive = _reorientation.IsReorienting,
                // What kind of movement this is, which sets how long it takes. Only genuine
                // reflexes are urgent: a startle beat, and re-acquiring after a cut or teleport.
                // Looking at the player is Neutral like any other act of attention — a person
                // walking up is not an emergency, and a character that whips round to face its
                // own user reads as alarmed by them.
                Urgency = ResolveMovementUrgency(ambientActive),
                PoseSink = Context?.ProceduralPoseCompositor
            };
            _headTorso.Solve(in input);

            SolveEyes(profile, deltaTime, ambientActive, eyeContactLocked, exactFocus);
            ProcessScriptedSettlement();
            TraceReachLimit(deltaTime);

            TraceFirehose(profile, deltaTime);

            // Pupil response: emotion intensity + gaze engagement arousal, smoothed ~1s,
            // published to an optional eye-appearance driver (e.g. a shader-property pupil
            // dilation binding). A no-op single null check when no driver is registered.
            IEmotionStateFrameSource pupilFrameSource = Context.EmotionStateFrameSource;
            float pupilEmotionScore = pupilFrameSource != null
                ? pupilFrameSource.CurrentFrame.DominantScore
                : Context.EmotionStateSource?.Current.DominantScore ?? 0f;
            _pupilArousal.Tick(pupilEmotionScore, _directive.Engagement, deltaTime);
            Context.EyeAppearanceDriver?.SetPupilDilation(_pupilArousal.Dilation);

            // Eyebrow-gaze coordination: current eye pitch (post-solve, positive upward),
            // this tick's backchannel-nod-start pulse, and the interruption startle
            // re-acquisition pulse decide whether a one-shot brow cue fires this frame. A no-op
            // single null check when no brow-cue sink is registered.
            float eyePitchDegrees = (_eyes.LeftEyeAngles.y + _eyes.RightEyeAngles.y) * 0.5f;
            _browCueCoordinator.Tick(
                eyePitchDegrees, _backchannel.NodStartedThisTick, _interruptionReaction.WantsReacquisition, deltaTime);
            if (_browCueCoordinator.HasPendingCue)
                Context.BrowCueSink?.RaiseBrowCue(_browCueCoordinator.PendingKind, _browCueCoordinator.PendingIntensity);
        }

        /// <summary>
        ///     Classifies this frame's head/torso movement so the actuator can pick a duration.
        /// </summary>
        /// <remarks>
        ///     Only reflexes are urgent. The startle beat and a post-cut re-acquisition are things
        ///     that happen TO the character; everything else — including acquiring the player — is
        ///     something it chose, and choices are made at ordinary speed. Idle exploration is
        ///     slower still, which is most of what separates an idle character from an alert one.
        /// </remarks>
        private GazeMovementUrgency ResolveMovementUrgency(bool ambientActive)
        {
            // WasCut, not TeleportedThisTick. The latter is also raised by every ordinary
            // re-target — it is what tells the eye stage to saccade rather than glide — so
            // reading it here classified every decision to look at something as a reflex and
            // executed it at reflex speed. An idle curiosity glance, the most voluntary movement
            // this module makes, was the most visible case: it fired at 0.75x duration out of
            // idle drift running at 1.35x.
            if (_interruptionReaction.WantsReacquisition || _directive.WasCut)
                return GazeMovementUrgency.Urgent;

            return ambientActive || !_directive.HasEngagedTarget
                ? GazeMovementUrgency.Relaxed
                : GazeMovementUrgency.Neutral;
        }

        private void SolveEyes(
            ConvaiGazeProfile profile,
            float deltaTime,
            bool ambientActive,
            bool eyeContactLocked,
            bool exactFocus)
        {
            bool faceScanActive = _directive.HasEngagedTarget &&
                                  _directive.Kind == GazeTargetKind.Player;
            // Emotional gaze signature: SaccadeTempoScale paces the micro-saccade dwell too
            // (quicker tempo = livelier fixation, slower tempo = more settled).
            _micro.Tick(profile, deltaTime, _emotionModulator.SaccadeTempoScale, ref _random);
            // Listener mouth-bias: FaceScanDirector smooths this raw flag itself (~0.5s).
            _faceScan.Tick(profile, deltaTime, faceScanActive && !exactFocus, _playerSpeaking, ref _random);

            // Emotional gaze signature: FixationLivelinessScale multiplies the state's own
            // liveliness before the speech-energy modulation, so an emotion's stillness/energy
            // and speech's own boost compose rather than one overriding the other.
            float liveliness = _directive.FixationLiveliness * _emotionModulator.FixationLivelinessScale;
            ISpeechEnergyProvider speechEnergy = Context.SpeechEnergyProvider;
            if (speechEnergy != null && _directive.HasEngagedTarget)
                liveliness *= Mathf.Lerp(0.85f, 1.2f, Mathf.Clamp01(speechEnergy.Current));

            var eyeInput = new EyeSolveInput
            {
                Chain = _chain,
                Profile = profile,
                DeltaTime = deltaTime,
                TargetPoint = _directive.WorldPoint,
                HasTarget = _directive.HasEngagedTarget,
                Engagement = _directive.Engagement,
                AmbientAngles = _ambient.CurrentAngles,
                AmbientActive = ambientActive,
                // Proxemic intimacy regulation: scales the face-scan landmark offset by the
                // same factor FaceScanDirector.Offset's own radius would be scaled by (it's a
                // pure multiplier on Landmarks * radius, so scaling the offset here is
                // equivalent to scaling FaceScanRadiusDegrees at the source) — bypassed entirely
                // while the eye-contact lock is in force.
                MicroOffset = ResolveMicroOffset(eyeContactLocked, exactFocus),
                AversionOffset = ResolveFocusAversionOffset(_aversion.EyeOffset, eyeContactLocked),
                GenerationId = _directive.GenerationId,
                // The interruption startle re-acquisition pulse forces a fresh saccade toward
                // the current target on this tick only, exactly like a real teleport/camera cut
                // — reuses the eye solver's existing fresh-target mechanism rather than a new
                // channel.
                Teleported = _directive.TeleportedThisTick || _interruptionReaction.WantsReacquisition,
                FixationLiveliness = liveliness,
                // Emotional gaze signature: scales saccade reaction latency in the eye stage.
                SaccadeTempoScale = _emotionModulator.SaccadeTempoScale,
                ApplyToBones = _useEyeBones,
                LookShapesActive = _useLookShapes
            };
            _eyes.Solve(in eyeInput);

            if (_interruptionReaction.WantsBlink || _turnTaking.WantsYieldBlink)
                _blink.TryTriggerForcedBlink(profile);

            float saccadeAmplitude = _eyes.SaccadeStartedAmplitude;
            if (saccadeAmplitude > 0f)
            {
                bool shiftBlink = _blink.TryTriggerShiftBlink(profile, saccadeAmplitude, ref _random);

                // Saccades fire a few times a second, so the gate is tested before the message
                // is built — at Off verbosity this path allocates nothing at all.
                if (_trace != null && _trace.IsEnabled(GazeTraceVerbosity.Detail))
                {
                    if (shiftBlink)
                        _trace.Detail($"Gaze-shift blink on {saccadeAmplitude:0.0}° saccade.");
                    else if (saccadeAmplitude > 8f)
                        _trace.Detail($"Saccade {saccadeAmplitude:0.0}° toward '{_directive.TargetName}'.");
                }
            }

            _blink.Tick(profile, deltaTime, ref _random);

            FacialBlendshapeCompositorHost compositor = Context?.EnsureCompositor();
            // Lid aperture is expression, not contact, so it rides through even under an
            // eye-contact lock — an angry locked stare should still look angry.
            _eyeWriter.Submit(
                compositor, this, profile, _blink.Weight, _emotionModulator.LidApertureScale,
                _eyes.LeftEyeAngles, _eyes.RightEyeAngles,
                driveLookShapes: _useLookShapes, deltaTime);
        }

        private Vector2 ResolveMicroOffset(bool eyeContactLocked, bool exactFocus)
        {
            if (exactFocus) return Vector2.zero;
            Vector2 offset = _micro.Offset +
                             _faceScan.Offset * (eyeContactLocked ? 1f : _proxemics.FaceScanRadiusScale);

            // The arrival settle rides this channel specifically because the head never reads
            // it: a settle that moved the neck would be a head bow, not a settle.
            offset.y += _arrivalSettle.PitchOffsetDegrees;

            return ConstrainMicroOffset(offset, eyeContactLocked);
        }

        internal static Vector2 ConstrainMicroOffset(Vector2 offset, bool socialFocusActive) =>
            socialFocusActive ? Vector2.ClampMagnitude(offset, 0.75f) : offset;

        internal static Vector2 ResolveFocusAversionOffset(Vector2 offset, bool focusActive) =>
            focusActive ? Vector2.zero : offset;

        internal static float ComposeNaturalSpeakingAversion(
            float authoredStrength,
            float suppressionFactor,
            float proxemicFloor,
            bool applyProxemicFloor,
            bool turnTakingOwnsSpeaking)
        {
            float strength = Mathf.Clamp01(authoredStrength * suppressionFactor);
            if (applyProxemicFloor && !turnTakingOwnsSpeaking)
                strength = Mathf.Max(strength, Mathf.Clamp01(proxemicFloor));
            return strength;
        }

        private void TraceReachLimit(float deltaTime) =>
            _diagnostics.ReportReachLimit(
                _trace, deltaTime, _directive.HasEngagedTarget, _directive.Engagement,
                _eyes.ContactErrorDegrees, _directive.TargetName);

        private void TraceFirehose(ConvaiGazeProfile profile, float deltaTime)
        {
            // Gated here as well as inside the reporter so the sample is not even gathered at the
            // verbosities every shipping character actually runs at.
            if (profile.TraceVerbosity < GazeTraceVerbosity.Firehose) return;

            var sample = new GazeFirehoseSample(
                _directive.Engagement, _headTorso.HeadAngles, _headTorso.TorsoAngles,
                _eyes.LeftEyeAngles, _eyes.PhaseName, _blink.Weight,
                _headTorso.TargetYawError, _directive.Kind, _directive.TargetName);

            _diagnostics.ReportFirehose(
                _trace, deltaTime, profile.TraceVerbosity, profile.FirehoseHz, in sample);
        }

        /// <summary>Fills <paramref name="snapshot" /> with the live gaze state.</summary>
        public void CaptureSnapshot(GazeSnapshot snapshot)
        {
            if (snapshot == null) return;

            snapshot.Clear();
            snapshot.Reading = Current;
            snapshot.TargetKind = _directive.HasEngagedTarget ? _directive.Kind : GazeTargetKind.None;
            snapshot.TargetName = _directive.HasEngagedTarget ? _directive.TargetName : "-";
            snapshot.DialogueState = Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            snapshot.PolicyEngagement = _policy.SmoothedEngagement;
            snapshot.HeadAngles = _headTorso.HeadAngles;
            snapshot.HeadRollDegrees = _headTorso.HeadRollDegrees;
            snapshot.TorsoAngles = _headTorso.TorsoAngles;
            snapshot.TargetErrorAngles = _directive.HasEngagedTarget
                ? new Vector2(_shiftMeasurement.RequiredYaw, _shiftMeasurement.RequiredPitch)
                : Vector2.zero;
            snapshot.LeftEyeAngles = _eyes.LeftEyeAngles;
            snapshot.RightEyeAngles = _eyes.RightEyeAngles;
            snapshot.EyePhase = _eyes.PhaseName;
            snapshot.ContactErrorDegrees = _directive.HasEngagedTarget ? _eyes.ContactErrorDegrees : float.NaN;
            snapshot.FocusActive = _focusActive;
            snapshot.FocusFidelity = focusFidelity;
            snapshot.FocusDegraded = _focusDegraded;
            snapshot.ContactUsesBoneBackend = _useEyeBones;
            snapshot.BlinkWeight = _blink.Weight;
            snapshot.IsReorienting = _reorientation.IsReorienting;
            snapshot.IsNodding = _backchannel.IsNodding;
            bool sensorLive = _attentionSensor != null && _attentionSensor.isActiveAndEnabled;
            snapshot.PlayerAttention = sensorLive ? _attentionSensor.PlayerAttention : -1f;
            snapshot.PlayerLooking = sensorLive && _attentionSensor.IsPlayerLooking;
            ConvaiGazeProfile lodProfile = EffectiveProfile;
            snapshot.LodEnabled = lodProfile != null && lodProfile.EnableGazeLod;
            snapshot.LodFar = snapshot.LodEnabled && _lodGovernor.IsFar;
            snapshot.LodExpressionSkipped = _lodSkipExpression;
            _trace?.CopyRecentEntries(snapshot.RecentTrace);
        }

        /// <summary>Allocating convenience overload of <see cref="CaptureSnapshot(GazeSnapshot)" />.</summary>
        public GazeSnapshot CaptureSnapshot()
        {
            var snapshot = new GazeSnapshot();
            CaptureSnapshot(snapshot);
            return snapshot;
        }

        /// <summary>
        ///     While idle (player suppressed by policy), occasionally schedules a soft,
        ///     short glance at the player through the scripted stack so the character still
        ///     feels aware of them. Low priority — any real scripted request outranks it.
        /// </summary>
        /// <summary>
        ///     Priority of a travel check-in glance. Glance tier (below the explicit
        ///     <see cref="GazeAt(Transform, GazeOptions)" /> default of 0) so an authored gaze action
        ///     always wins and an eye-contact lock absorbs it through the existing suppression path —
        ///     above the idle curiosity glance, because checking on where you are going while walking
        ///     is a more purposeful beat than idle curiosity.
        /// </summary>
        private const int TravelCheckInPriority = -50;

        /// <summary>
        ///     Fires the periodic look at what the journey is about. The subject is whatever declared
        ///     it — the destination of a walk, the person being followed, or the target of an action
        ///     step — so a customer's own executor gets this without writing any gaze code.
        /// </summary>
        private void TickTravelCheckIn(ConvaiGazeProfile profile, DialogueState state, float deltaTime)
        {
            if (!_travel.TickCheckIn(in _travelIntent, state, profile, ref _random, deltaTime)) return;

            _scripted.PushUnowned(
                null, ResolveTravelCheckInPoint(), hasTransform: false,
                priority: TravelCheckInPriority, engagementOverride: 0.9f, allowBodyTurn: false,
                deadline: Time.time + profile.TravelGlanceHoldSeconds, name: "travel check-in");

            if (_trace != null && _trace.IsEnabled(GazeTraceVerbosity.Detail))
                _trace.Detail($"Travel check-in glance for {profile.TravelGlanceHoldSeconds:0.00}s.");
        }

        /// <summary>
        ///     Height above the character root that a look point sits at. Uses the head bone when the
        ///     rig offers one, and the same 1.6 m standing eye line as <see cref="ResolveRestPoint" />
        ///     otherwise — one height convention for the module, not two.
        /// </summary>
        /// <summary>
        ///     Where a travel check-in glance actually aims: the journey's subject, raised to the
        ///     character's own eye line when it sits below it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The travel reading reports a subject's <em>transform position</em>, which for a
        ///         companion is the player's root — their feet, not their eyes — and for a
        ///         destination is a point on the floor. Aimed at raw, a check-in glance therefore
        ///         looks at the ground, and the closer the character gets the worse it is: the
        ///         angle steepens (about 57° down at a metre) while the check-in cadence
        ///         simultaneously tightens on approach. The character arrives somewhere and
        ///         repeatedly ducks its head at its own feet.
        ///     </para>
        ///     <para>
        ///         Raising it is the same correction <c>PlayerAnchorTargetProvider</c> already
        ///         applies to a non-camera anchor, for the same reason — gaze should land on the
        ///         eye line rather than the feet. The rule here is one-sided on purpose: a
        ///         subject ABOVE the character's eye line is left alone, so it still looks up at
        ///         a high shelf. What it will not do is crane its neck downward at something it
        ///         is walking toward.
        ///     </para>
        /// </remarks>
        private Vector3 ResolveTravelCheckInPoint()
        {
            Transform root = Context != null ? Context.CharacterRoot : transform;
            if (root == null) return _travelIntent.SubjectPosition;

            return LiftToEyeLine(
                _travelIntent.SubjectPosition, root.position.y + ResolveEyeHeight(root));
        }

        /// <summary>
        ///     Raises a look point to <paramref name="observerEyeLineY" /> when it sits below it,
        ///     and leaves it alone when it does not.
        /// </summary>
        internal static Vector3 LiftToEyeLine(Vector3 point, float observerEyeLineY)
        {
            point.y = Mathf.Max(point.y, observerEyeLineY);
            return point;
        }

        /// <summary>
        ///     Height of this character's eye line above its root, for the look points that are
        ///     defined relative to it — the path a traveller watches, and the lift applied to a
        ///     travel subject.
        /// </summary>
        /// <remarks>
        ///     Measured from the eye bones when the rig has them, and only from the head bone
        ///     otherwise. The head bone sits below the eyes — about 7 cm on a CC4 rig — and both
        ///     callers mean the eye line specifically, so reading the head bone put a standing
        ///     downward bias of a degree or so on everything a walking character looked at.
        ///     Harmless in isolation and in exactly the wrong direction next to the other
        ///     head-down defects, so it is measured properly rather than left to cancel.
        /// </remarks>
        private float ResolveEyeHeight(Transform root)
        {
            if (root == null || Context?.RigBinding == null) return DefaultEyeHeight;

            IStandardRigBinding binding = Context.RigBinding;
            binding.TryGetBone(StandardBone.LeftEye, out Transform leftEye);
            binding.TryGetBone(StandardBone.RightEye, out Transform rightEye);
            if (leftEye != null && rightEye != null)
                return (leftEye.position.y + rightEye.position.y) * 0.5f - root.position.y;

            if (binding.TryGetBone(StandardBone.Head, out Transform head) && head != null)
                return head.position.y - root.position.y;

            return DefaultEyeHeight;
        }

        /// <summary>Adult eye height (metres) used when the rig offers nothing to measure.</summary>
        private const float DefaultEyeHeight = 1.6f;

        /// <summary>
        ///     Brings the character's travel intent into being the first time it is seen to move.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A deliberately looser probe than the real detection: this only answers "should this
        ///         character have a travel intent at all", and the component itself then applies the
        ///         proper speed and sustain gates. Measured parent-locally for the same reason it is
        ///         there — a character riding a moving platform is not walking.
        ///     </para>
        ///     <para>
        ///         Convai locomotion provisions it at the start of a move instead, so this path only
        ///         matters for characters moved by something else entirely.
        ///     </para>
        /// </remarks>
        private void EnsureTravelIntentIfMoving(ConvaiGazeProfile profile)
        {
            if (_travelIntentProvisioned || !profile.EnableTravelGaze) return;

            if (Context?.TravelIntentSource != null)
            {
                _travelIntentProvisioned = true;
                return;
            }

            // The character root, not this component's own transform: gaze is not required to sit on
            // the root, and a controller on a child object holds a constant local position while the
            // character walks — the probe would never fire, and the component would be provisioned
            // onto the child. Same idiom the owned player anchor already uses.
            Transform root = Context.CharacterRoot != null ? Context.CharacterRoot : transform;

            Vector3 local = root.localPosition;
            if (!_hasLastLocalRootPosition)
            {
                _lastLocalRootPosition = local;
                _hasLastLocalRootPosition = true;
                return;
            }

            Vector3 delta = local - _lastLocalRootPosition;
            _lastLocalRootPosition = local;
            delta.y = 0f;

            // One frame of unmistakable movement is enough to justify the component; being wrong
            // costs an inert component, while being late costs the first stride of the walk.
            if (delta.sqrMagnitude < ProvisioningMoveEpsilonSquared) return;

            _travelIntentProvisioned = ConvaiTravelIntent.EnsureOn(root.gameObject) != null;
        }

        /// <summary>
        ///     Squared per-tick parent-local displacement that justifies provisioning a travel intent
        ///     (2 cm — about 1.2 m/s at 60 Hz, unambiguously locomotion rather than settle or drift).
        /// </summary>
        private const float ProvisioningMoveEpsilonSquared = 0.02f * 0.02f;

        private void TickCuriosityGlance(ConvaiGazeProfile profile, in GazeStatePolicy statePolicy, float deltaTime)
        {
            bool idleActive = !statePolicy.AllowPlayerTarget &&
                              _scripted.Count == 0 &&
                              profile.EnableAmbientExploration;

            // E8 reciprocation: when a player attention sensor reports the player is watching,
            // shrink the wait so the idle character glances back sooner (down to ~40% at full
            // attention). No sensor / feature off → unchanged authored cadence.
            float glanceIntervalScale = 1f;
            if (profile.CuriosityRespondsToAttention && _attentionSensor != null && _attentionSensor.isActiveAndEnabled)
                glanceIntervalScale = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(_attentionSensor.PlayerAttention));

            if (!_curiosity.Tick(profile, deltaTime, idleActive, ref _random, glanceIntervalScale)) return;

            Transform curiosityRoot = Context != null && Context.CharacterRoot != null
                ? Context.CharacterRoot
                : transform;

            for (int i = 0; i < _candidates.Count; i++)
            {
                GazeTargetCandidate candidate = _candidates[i];
                if (candidate.Kind != GazeTargetKind.Player) continue;
                // Same reachability rule the character glance uses, and for the same reason: a
                // glance never turns the body, so a player standing behind the shoulder is not
                // glanced at — it would pin the head and eyes at their limits for the whole hold
                // and then unwind, which is a lurch, not a glance. Skipping re-arms the timer, so
                // the character tries again once the player is somewhere it can actually look.
                if (!IsWithinGlanceReach(curiosityRoot, candidate.WorldPoint)) continue;

                _scripted.PushUnowned(
                    candidate.Target, candidate.WorldPoint, candidate.Target != null,
                    // Engagement 1, like every other glance in this module: a glance is
                    // committed and its brevity is what makes it a glance (see GlanceOptions).
                    // It shipped at 0.5, which was a second damper on top of the state policy's
                    // own head contribution — see GlanceHeadContribution.
                    priority: -100, engagementOverride: 1f, allowBodyTurn: false,
                    deadline: Time.time + profile.CuriosityGlanceDuration, name: "curiosity glance",
                    headContributionOverride: GlanceHeadContribution,
                    localAimOffset: LocalAimOffsetOf(in candidate));
                if (_trace != null && _trace.IsEnabled(GazeTraceVerbosity.Detail))
                    _trace.Detail($"Curiosity glance at '{candidate.DebugName}' for {profile.CuriosityGlanceDuration:0.0}s.");
                return;
            }
        }

        /// <summary>
        ///     Horizontal angle from the character's facing beyond which an idle glance is not
        ///     attempted: a glance never turns the body, so a target behind the shoulder would
        ///     just pin the eyes and head at their limits for the whole hold. Shared by both idle
        ///     glance beats — the reason is the rig's reach, which does not care whether the
        ///     character is glancing at the player or at another character.
        /// </summary>
        private const float GlanceMaxYawDegrees = 100f;

        /// <summary>
        ///     How much of an idle glance the head takes. A glance at a person is an act of
        ///     attention: the head does most of it and the eyes finish, which is how people look
        ///     at each other.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Stated by the glance rather than inherited from the dialogue state, because the
        ///         state in question is Idle, whose head contribution describes a character
        ///         drifting around a room (0.4 in the shipped sample profile) — a different
        ///         movement that happens to share a row in the table. Inherited, the glance's own
        ///         strength multiplied it: 0.5 × 0.4 = 0.2, a number nobody authored, and the
        ///         result was a character who barely turned her head and held the look at the
        ///         corner of her eyes. Past a 44° shift the eyes even ran out of travel
        ///         (<c>Eye Max Yaw</c> 35°) and the gaze landed short of the player outright —
        ///         "she turned, but she is not looking at me".
        ///     </para>
        ///     <para>
        ///         0.75 keeps the eyes inside their comfort range for anything the glance is
        ///         allowed to attempt: at the widest reachable shift the head takes about three
        ///         quarters and the eyes are left with the rest, instead of the reverse.
        ///     </para>
        /// </remarks>
        private const float GlanceHeadContribution = 0.75f;

        /// <summary>
        ///     A candidate's aim point expressed in its own transform's space, so a glance built
        ///     from it follows the target and still aims where the provider said.
        /// </summary>
        /// <remarks>
        ///     A scripted request that carries a transform resolves its aim from that transform's
        ///     position, which is right for a developer's <c>GazeAt(prop)</c> and wrong for a
        ///     provider candidate: the player anchor aims at an eye line above the rig's root, and
        ///     re-pushing it as a glance without this dropped that offset — the character followed
        ///     the player perfectly and looked at their feet. Zero whenever the point already is
        ///     the transform's origin, which is the case for a camera anchor, so the common desktop
        ///     setup is bit-identical.
        /// </remarks>
        private static Vector3 LocalAimOffsetOf(in GazeTargetCandidate candidate) =>
            candidate.Target != null
                ? candidate.Target.InverseTransformPoint(candidate.WorldPoint)
                : Vector3.zero;

        /// <summary>
        ///     Whether <paramref name="worldPoint" /> is inside <see cref="GlanceMaxYawDegrees" />
        ///     of the character's facing, measured on the horizontal plane. A degenerate facing or
        ///     a point directly overhead counts as reachable — there is no yaw to be beyond.
        /// </summary>
        private static bool IsWithinGlanceReach(Transform root, Vector3 worldPoint)
        {
            if (root == null) return true;

            Vector3 forward = root.forward;
            forward.y = 0f;
            Vector3 toTarget = worldPoint - root.position;
            toTarget.y = 0f;
            if (forward.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f) return true;

            return Vector3.Angle(forward, toTarget) <= GlanceMaxYawDegrees;
        }

        /// <summary>
        ///     While idle, occasionally schedules a short soft glance at a nearby character —
        ///     the mutual-glance beat between idle NPCs. Idle policy engagement is 0, so the
        ///     glance carries its own commitment through the scripted stack; a real scripted
        ///     request or an engaged player still outranks it. The target is picked
        ///     relevance-weighted (speakers attract more glances) among reachable candidates.
        /// </summary>
        private void TickCharacterGlance(ConvaiGazeProfile profile, in GazeStatePolicy statePolicy, float deltaTime)
        {
            if (_characterGaze == null || !_characterGaze.isActiveAndEnabled || !_characterGaze.EnableIdleGlances)
            {
                _characterGlance.Reset();
                return;
            }

            bool idleActive = !statePolicy.AllowPlayerTarget &&
                              _scripted.Count == 0 &&
                              profile.EnableAmbientExploration;
            bool fire = _characterGlance.Tick(
                _characterGaze.EnableIdleGlances,
                _characterGaze.IdleGlanceIntervalMin,
                _characterGaze.IdleGlanceIntervalMax,
                deltaTime, idleActive, ref _random);
            if (!fire) return;

            Transform root = Context != null && Context.CharacterRoot != null
                ? Context.CharacterRoot
                : transform;

            // Relevance-weighted reservoir pick: each candidate replaces the current pick
            // with probability weight/runningTotal, so glances distribute naturally instead
            // of always fixating the same nearest character.
            float totalWeight = 0f;
            int pick = -1;
            for (int i = 0; i < _candidates.Count; i++)
            {
                GazeTargetCandidate candidate = _candidates[i];
                if (candidate.Kind != GazeTargetKind.Character || candidate.Relevance <= 0f) continue;
                if (!IsWithinGlanceReach(root, candidate.WorldPoint)) continue;

                totalWeight += candidate.Relevance;
                if (_random.Range(0f, totalWeight) <= candidate.Relevance)
                    pick = i;
            }

            if (pick < 0) return;

            GazeTargetCandidate target = _candidates[pick];
            _scripted.PushUnowned(
                target.Target, target.WorldPoint, target.Target != null,
                priority: -100, engagementOverride: _characterGaze.IdleGlanceEngagement, allowBodyTurn: false,
                deadline: Time.time + _characterGaze.IdleGlanceDuration, name: "character glance",
                headContributionOverride: GlanceHeadContribution,
                localAimOffset: LocalAimOffsetOf(in target));
            if (_trace != null && _trace.IsEnabled(GazeTraceVerbosity.Detail))
                _trace.Detail($"Character glance at '{target.DebugName}' for {_characterGaze.IdleGlanceDuration:0.0}s.");
        }

        /// <summary>
        ///     Target-loss search: when the player candidate drops out (LOS occlusion or
        ///     range exit) after at least 2 s of continuous engagement, the last known point is
        ///     held and a short burst of searching saccades substitutes for the lost target
        ///     until the search director releases (completion, reacquisition, or a state exit
        ///     to Idle). Substitutes <see cref="_directive" />'s target point/engagement/head
        ///     contribution in place — the same channel every other target already flows
        ///     through — so no new solver seam is needed.
        /// </summary>
        private void TickTargetLossSearch(ConvaiGazeProfile profile, in GazeTargetDecision decision, DialogueState state, float deltaTime)
        {
            // A scripted/glance-tier target (GlanceAt, curiosity, character glance, referential
            // glances, ...) always outranks every provider tier (see GazeTargetArbiter), which
            // means the character's attention has deliberately moved on. Abort outright rather
            // than silently overriding it for the rest of the search — resuming a stale search
            // afterwards would look robotic. decision.IsScripted is the arbiter's own
            // discriminator for "this tick's winner came off the scripted stack".
            if (decision.IsScripted)
            {
                _search.Abort();
                return;
            }

            bool playerValid = TryGetPlayerCandidate(out Vector3 playerPoint);
            bool engagedWithPlayer = _directive.Kind == GazeTargetKind.Player && _directive.HasEngagedTarget;
            bool wasSearching = _search.SearchActive;

            // Same gaze-origin pivot the solver stage uses (ResolvePlayerDistance uses the same
            // fallback) — gives the director a character-relative lateral basis instead of a
            // world axis, so the search reads as sideways regardless of facing direction.
            Vector3 observerPosition = _chain.IsBound ? _chain.HeadPivotPosition : transform.position;

            bool searching = _search.Tick(
                playerValid, engagedWithPlayer, playerPoint, observerPosition, state,
                profile.EnableTargetLossSearch, profile.TargetLossSearchMaxSeconds, deltaTime, ref _random);

            if (!searching) return;

            if (!wasSearching)
                _trace?.State("Player lost — searching last known direction.");

            // Kind is forced (not just left as decision.Kind) because the search can still be
            // active after the arbiter's own loss-hold/commitment decay has fully released the
            // target to None. Target/Name are left as whatever the decision already carries
            // (usually still the player's, mid-decay) so this substitution never reads as a
            // target change to TraceTargetTransitions/TargetChanged — it is the same commitment,
            // just aimed at a searched point instead of the live target position.
            _directive.Kind = GazeTargetKind.Player;
            _directive.WorldPoint = _search.SearchPoint;
            _directive.Engagement = Mathf.Max(_directive.Engagement, SearchEngagementFloor);
            _directive.SettledEngagement = Mathf.Max(_directive.SettledEngagement, SearchEngagementFloor);
            _directive.HeadContribution = _search.HeadContribution;
            _directive.TeleportedThisTick = _directive.TeleportedThisTick || _search.FixationChangedThisTick;
        }

        /// <summary>Finds the player candidate in this tick's gathered list, if any (LOS/range-valid).</summary>
        private bool TryGetPlayerCandidate(out Vector3 point)
        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                GazeTargetCandidate candidate = _candidates[i];
                if (candidate.Kind == GazeTargetKind.Player && candidate.Relevance > 0f)
                {
                    point = candidate.WorldPoint;
                    return true;
                }
            }

            point = Vector3.zero;
            return false;
        }

        private void GatherCandidates(ConvaiGazeProfile profile, float deltaTime)
        {
            _candidates.Clear();
            Transform root = Context != null ? Context.CharacterRoot : transform;

            // The path ahead, offered like any other candidate so the arbiter's acquisition ramp,
            // interest budget and point smoothing all apply to it unchanged.
            if (_travel.TryBuildPathCandidate(
                    in _travelIntent, root, ResolveEyeHeight(root), profile, deltaTime,
                    out GazeTargetCandidate pathCandidate))
            {
                _candidates.Add(pathCandidate);
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                IGazeTargetProvider provider = _providers[i];
                if (provider == null) continue;
                if (!_focusActive && _ownedPlayerAnchorFocusOnly &&
                    ReferenceEquals(provider, _ownedPlayerAnchor)) continue;
                // provider is interface-typed, so "== null" above is plain reference equality
                // and does not catch a destroyed-but-not-yet-collected Behaviour (Unity's
                // "fake null"); behaviour's static type is UnityEngine.Object-derived, so its
                // own "== null" correctly detects that case before touching isActiveAndEnabled.
                if (provider is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled)) continue;
                if (provider.TryGetCandidate(root, out GazeTargetCandidate candidate))
                    _candidates.Add(candidate);
            }

            for (int i = 0; i < _runtimeProviders.Count; i++)
            {
                IGazeTargetProvider provider = _runtimeProviders[i];
                if (provider == null) continue;
                if (provider.TryGetCandidate(root, out GazeTargetCandidate candidate))
                    _candidates.Add(candidate);
            }

            IReadOnlyList<WorldObjectGazeTargetProvider> worldObjects =
                WorldObjectGazeTargetProvider.ActiveProviders;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                WorldObjectGazeTargetProvider provider = worldObjects[i];
                if (provider == null) continue;
                if (provider.TryGetCandidate(root, out GazeTargetCandidate candidate))
                    _candidates.Add(candidate);
            }

            // Declarative drag-drop gaze targets (no scene metadata required).
            IReadOnlyList<ConvaiGazeTarget> gazeTargets = ConvaiGazeTarget.ActiveTargets;
            for (int i = 0; i < gazeTargets.Count; i++)
            {
                ConvaiGazeTarget target = gazeTargets[i];
                if (target == null) continue;
                if (target.TryGetCandidate(root, out GazeTargetCandidate candidate))
                    _candidates.Add(candidate);
            }

            // Character-to-character mutual gaze: one candidate per other registered
            // character. Speakers are fully relevant (listeners turn to them); idle
            // characters are low-relevance and cycle through the arbiter's interest budget.
            if (_characterGaze != null && _characterGaze.isActiveAndEnabled && _characterGaze.LookAtOthers)
            {
                IReadOnlyList<ConvaiCharacterGazeRegistry.Entry> others = ConvaiCharacterGazeRegistry.All;
                for (int i = 0; i < others.Count; i++)
                {
                    if (_characterGaze.TryBuildCandidate(root, others[i], out GazeTargetCandidate candidate))
                        _candidates.Add(candidate);
                }
            }
        }

        private void PublishReading(ConvaiGazeProfile profile, in GazeTargetDecision decision)
        {
            if (_directive.HasEngagedTarget)
            {
                Current = new GazeReading(
                    _directive.Kind,
                    _directive.Target,
                    _directive.WorldPoint,
                    _directive.Engagement,
                    _aversion.IsAverting,
                    _directive.GenerationId);
                return;
            }

            if (profile.EnableAmbientExploration)
            {
                Current = new GazeReading(
                    GazeTargetKind.Ambient,
                    null,
                    ResolveRestPoint(),
                    0f,
                    isAverting: false,
                    decision.GenerationId);
                return;
            }

            Current = GazeReading.None;
        }

        private void TraceTargetTransitions(in GazeTargetDecision decision)
        {
            GazeTargetKind kind = _directive.HasEngagedTarget ? _directive.Kind : GazeTargetKind.None;
            string name = _directive.HasEngagedTarget ? _directive.TargetName : "-";

            if (_diagnostics.TryReportTargetTransition(
                    _trace, kind, name, decision.GenerationId, decision.TeleportedThisTick,
                    Time.time, out GazeTargetChange change))
                TargetChanged?.Invoke(change);
        }

        private Vector3 ResolveRestPoint()
        {
            Transform reference = Context?.RigBinding?.Root != null ? Context.RigBinding.Root : transform;
            Vector3 forward = reference.forward.sqrMagnitude > 1e-6f ? reference.forward.normalized : Vector3.forward;

            if (Context?.RigBinding != null &&
                Context.RigBinding.TryGetBone(StandardBone.Head, out Transform head) &&
                head != null)
            {
                return head.position + forward * 2f;
            }

            return reference.position + Vector3.up * 1.6f + forward * 2f;
        }

        /// <summary>Whether any cached renderer is currently visible to a camera (E10 LOD gate).</summary>
        private bool AnyRendererVisible() => EvaluateRendererVisibility(_renderers);

        /// <summary>
        ///     Whether the crowd-LOD governor should treat this character as on-screen, given its
        ///     cached renderer array. Pure and static so the stale-cache cases are unit-testable
        ///     without a camera or a rig.
        /// </summary>
        /// <remarks>
        ///     Both "nothing cached yet" (the rig is still being set up) and "everything cached was
        ///     destroyed" (a mesh swap the rebind hook did not see) report <c>true</c>. They are the
        ///     same situation from the governor's point of view — the cache cannot answer the
        ///     question — and answering <c>false</c> instead would silently pin the character to the
        ///     off-screen tier forever, which reads as gaze half-dying for no visible reason.
        /// </remarks>
        internal static bool EvaluateRendererVisibility(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return true;

            bool anyAlive = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                anyAlive = true;
                if (renderers[i].isVisible) return true;
            }

            return !anyAlive;
        }

        /// <summary>Distance from the character's head pivot to the player proxy (main camera) for LOD.</summary>
        private float ResolvePlayerDistance()
        {
            Vector3 point;
            PlayerAnchorTargetProvider provider = FindActivePlayerAnchorProvider();
            if (provider != null && provider.TryResolveFocusPoint(out point))
            {
                Vector3 providerHead = _chain.IsBound ? _chain.HeadPivotPosition : transform.position;
                return Vector3.Distance(providerHead, point);
            }

            Transform anchor = playerAnchorOverride != null
                ? playerAnchorOverride
                : Camera.main != null ? Camera.main.transform : null;
            if (anchor == null) return 0f;

            Vector3 head = _chain.IsBound ? _chain.HeadPivotPosition : transform.position;
            return Vector3.Distance(head, anchor.position);
        }

        /// <summary>
        ///     The transform this character treats as the player's eyes, for anything that needs to
        ///     aim at, measure to, or turn toward the player the same way the gaze system does.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Exists because aiming at "the player" and aiming at the player's <em>GameObject</em>
        ///         are not the same thing: a first-person rig's root sits on the floor, so a request
        ///         built from it makes the character stare at the player's feet. This resolves the same
        ///         anchor the eye-contact path uses — <see cref="PlayerAnchorOverride" />, then the
        ///         active <see cref="PlayerAnchorTargetProvider" /> (which itself prefers
        ///         <c>Camera.main</c> and skips render-texture and utility cameras), then
        ///         <c>Camera.main</c> — so scripted requests and conversational eye contact agree on
        ///         where a person is.
        ///     </para>
        ///     <para>
        ///         Public so game code can agree with it too. A behavior that measures how near the
        ///         player is, or turns to face them, and resolves the player its own way will disagree
        ///         with this character's gaze the moment a project uses split-screen, a multiplayer
        ///         rig, or a cutscene camera — the eyes follow the assigned anchor while the rest of
        ///         the logic follows something else.
        ///     </para>
        /// </remarks>
        /// <param name="anchor">The player's eye-line transform, when this scene has one.</param>
        /// <returns><c>false</c> when there is no anchor, no provider and no camera to fall back on.</returns>
        public bool TryGetPlayerAnchor(out Transform anchor) => TryResolvePlayerAnchor(out anchor);

        private bool TryResolvePlayerAnchor(out Transform anchor)
        {
            anchor = playerAnchorOverride;
            if (anchor != null) return true;

            PlayerAnchorTargetProvider provider = FindActivePlayerAnchorProvider();
            if (provider != null && provider.TryGetFocusCandidate(out GazeTargetCandidate candidate))
            {
                anchor = candidate.Target;
                return anchor != null;
            }

            if (Camera.main != null)
            {
                anchor = Camera.main.transform;
                return true;
            }
            return false;
        }

        private void RejectScriptedRequestsForExactFocus()
        {
            if (!_scripted.RejectAllForExactFocus()) return;
            _trace?.State("Scripted gaze rejected by Exact focus.");
        }

        private void ResampleLiveTargetPoint()
        {
            if (!ShouldResampleFocusedPlayer(
                    _focusActive, _directive.HasEngagedTarget, _directive.Kind)) return;

            PlayerAnchorTargetProvider anchor = FindActivePlayerAnchorProvider();
            if (anchor != null && anchor.TryResolveFocusPoint(out Vector3 focusPoint))
            {
                _directive.WorldPoint = focusPoint;
                _lastFocusPoint = focusPoint;
                _hasLastFocusPoint = true;
            }
        }

        /// <summary>
        ///     The transform this character treats as "the player". <c>null</c> (default)
        ///     resolves to <c>Camera.main</c> (XR rigs included), then any enabled camera.
        ///     Assign for split-screen, multiplayer, or cutscene rigs — engagement policies,
        ///     body turns, and the dynamic-context bridge all follow the new anchor. Applies
        ///     immediately at runtime; setting it back to <c>null</c> returns to the camera.
        /// </summary>
        public Transform PlayerAnchorOverride
        {
            get => playerAnchorOverride;
            set
            {
                playerAnchorOverride = value;
                ApplyPlayerAnchorOverride(clearWhenNull: true);
            }
        }

        /// <summary>
        ///     How this character's eye contact is governed. <see cref="GazeEyeContactMode.Natural" />
        ///     follows the profile's per-state policy table; <see cref="GazeEyeContactMode.ConversationLock" />
        ///     fully commits to the player anchor (<see cref="PlayerAnchorOverride" /> if set,
        ///     otherwise the main camera) in every conversational (non-Idle) state while Idle
        ///     keeps its authored ambient life; <see cref="GazeEyeContactMode.AlwaysLock" />
        ///     commits in every state including Idle. While a lock is in force the table
        ///     (including any authored aversion) and emotion engagement scaling are bypassed.
        ///     Settable at runtime; takes effect on the next tick, ramping in smoothly like any
        ///     other policy change — no snap. A scripted
        ///     <see cref="GazeAt(Transform,GazeOptions)" /> request outranks Social focus. Exact
        ///     focus rejects it unless <see cref="AllowScriptedOverridesDuringExactFocus" /> is
        ///     enabled; glance-tier requests are absorbed while
        ///     <see cref="LockBlocksGlances" /> is on.
        /// </summary>
        public GazeEyeContactMode EyeContactMode
        {
            get => eyeContactMode;
            set => eyeContactMode = value;
        }

        /// <summary>
        ///     Precision used while <see cref="EyeContactMode" /> is active. Social preserves
        ///     subtle fixation life; Exact suppresses intentional offsets without freezing blinks,
        ///     eyelids, pupils, vergence, or anatomical body turns.
        /// </summary>
        public GazeFocusFidelity FocusFidelity
        {
            get => focusFidelity;
            set => focusFidelity = value;
        }

        /// <summary>How the player anchor's conversational aim point is derived.</summary>
        public GazeAnchorAimMode PlayerAnchorAimMode
        {
            get => playerAnchorAimMode;
            set
            {
                playerAnchorAimMode = value;
                ApplyPlayerAnchorAim(authoredNow: true);
            }
        }

        /// <summary>Anchor-local aim offset used by <see cref="GazeAnchorAimMode.LocalOffset" />.</summary>
        public Vector3 PlayerAnchorAimOffset
        {
            get => playerAnchorAimOffset;
            set
            {
                playerAnchorAimOffset = value;
                ApplyPlayerAnchorAim(authoredNow: true);
            }
        }

        /// <summary>
        ///     Whether explicit <see cref="GazeAt(Transform,GazeOptions)" /> requests may preempt
        ///     an active Exact focus. Disabled by default; Social focus always permits them.
        /// </summary>
        public bool AllowScriptedOverridesDuringExactFocus
        {
            get => allowScriptedOverridesDuringExactFocus;
            set => allowScriptedOverridesDuringExactFocus = value;
        }

        /// <summary>
        ///     While an eye-contact lock is in force (see <see cref="EyeContactMode" />),
        ///     absorbs glance-tier scripted requests — <see cref="GlanceAt(Transform, float)" />
        ///     and everything built on it, such as referential glances — so nothing briefly
        ///     pulls gaze off the player anchor. Absorbed handles complete immediately without
        ///     settling. An explicit <see cref="GazeAt(Transform,GazeOptions)" /> preempts Social
        ///     focus; Exact follows <see cref="AllowScriptedOverridesDuringExactFocus" />. On by
        ///     default; turn off to let glances play through the
        ///     lock. Has no effect in <see cref="GazeEyeContactMode.Natural" /> mode.
        /// </summary>
        public bool LockBlocksGlances
        {
            get => lockBlocksGlances;
            set => lockBlocksGlances = value;
        }

        private void EnsurePlayerAnchorIfNeeded()
        {
            if (!ShouldProvisionPlayerAnchor(
                    autoCreatePlayerAnchor,
                    FindActivePlayerAnchorProvider() != null,
                    _providers.Count,
                    _runtimeProviders.Count,
                    focusActive: false)) return;
            if (Context == null || !UnityEngine.Application.isPlaying) return;

            CreateOwnedPlayerAnchor("No gaze target provider found — auto-provisioned a Gaze Player Anchor.");
        }

        /// <summary>
        ///     Pushes <see cref="playerAnchorOverride" /> into the character's player-anchor
        ///     provider, provisioning one when the override needs a carrier. With
        ///     <paramref name="clearWhenNull" /> false (enable-time sync) a null override
        ///     leaves a user-added provider's own Explicit Anchor untouched.
        /// </summary>
        private void ApplyPlayerAnchorOverride(bool clearWhenNull)
        {
            if (playerAnchorOverride == null && !clearWhenNull) return;

            PlayerAnchorTargetProvider provider = FindPlayerAnchorProvider();
            if (provider == null && playerAnchorOverride != null &&
                Context != null && UnityEngine.Application.isPlaying && isActiveAndEnabled)
            {
                provider = CreateOwnedPlayerAnchor(
                    "Player anchor override set — provisioned a Gaze Player Anchor to carry it.");
            }

            if (provider != null)
            {
                provider.ExplicitAnchor = playerAnchorOverride;
                ApplyPlayerAnchorAim(provider);
            }
        }

        internal static bool ShouldProvisionPlayerAnchor(
            bool autoCreate,
            bool hasPlayerProvider,
            int providerCount,
            int runtimeProviderCount,
            bool focusActive)
        {
            if (!autoCreate || hasPlayerProvider) return false;
            return focusActive || (providerCount <= 0 && runtimeProviderCount <= 0);
        }

        internal static bool ShouldUseFocusedPlayerCandidates(bool focusActive, bool hasScriptedWinner) =>
            focusActive && !hasScriptedWinner;

        internal static bool ShouldResampleFocusedPlayer(
            bool focusActive,
            bool hasEngagedTarget,
            GazeTargetKind kind) =>
            focusActive && hasEngagedTarget && kind == GazeTargetKind.Player;

        internal static bool ShouldResetArbiterForMissingFocus(
            bool focusActive,
            int focusCandidateCount,
            bool hasLastFocusPoint) =>
            focusActive && focusCandidateCount == 0 && !hasLastFocusPoint;

        private void ApplyPlayerAnchorAim(bool authoredNow = false) =>
            ApplyPlayerAnchorAim(FindPlayerAnchorProvider(), authoredNow);

        /// <summary>
        ///     Pushes this controller's aim settings onto <paramref name="provider" />.
        /// </summary>
        /// <param name="provider">The anchor to configure. Null is a no-op.</param>
        /// <param name="authoredNow">
        ///     Whether the caller is an act of authoring — someone assigning
        ///     <see cref="PlayerAnchorAimMode" /> or <see cref="PlayerAnchorAimOffset" /> — as
        ///     opposed to the enable-time pass that only replays serialized values.
        /// </param>
        /// <remarks>
        ///     An anchor you placed yourself owns its own aim settings, so the enable-time pass
        ///     leaves it alone unless this controller carries a non-default aim of its own. Without
        ///     that rule, adding a Gaze component next to a hand-configured anchor would silently
        ///     revert it to Auto. Assigning the property is different: choosing Auto there is a
        ///     decision, not an absent one, so it pushes through and the anchor follows.
        /// </remarks>
        private void ApplyPlayerAnchorAim(PlayerAnchorTargetProvider provider, bool authoredNow = false)
        {
            if (provider == null) return;

            bool carriesOwnAim = playerAnchorAimMode != GazeAnchorAimMode.Auto ||
                                 playerAnchorAimOffset != Vector3.zero;
            if (provider != _ownedPlayerAnchor && !authoredNow && !carriesOwnAim)
                return;

            provider.AimMode = playerAnchorAimMode;
            provider.LocalAimOffset = playerAnchorAimOffset;
        }

        private PlayerAnchorTargetProvider FindPlayerAnchorProvider()
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is PlayerAnchorTargetProvider provider)
                    return provider;
            }

            return _ownedPlayerAnchor;
        }

        private PlayerAnchorTargetProvider FindActivePlayerAnchorProvider()
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is PlayerAnchorTargetProvider provider &&
                    IsUsableFocusProvider(provider))
                    return provider;
            }

            return IsUsableFocusProvider(_ownedPlayerAnchor)
                ? _ownedPlayerAnchor
                : null;
        }

        internal static bool IsUsableFocusProvider(PlayerAnchorTargetProvider provider) =>
            provider != null && provider.isActiveAndEnabled;

        private PlayerAnchorTargetProvider CreateOwnedPlayerAnchor(string traceMessage)
        {
            GameObject owner = Context.CharacterRoot != null ? Context.CharacterRoot.gameObject : gameObject;
            _ownedPlayerAnchor = owner.AddComponent<PlayerAnchorTargetProvider>();
            ConfigureOwnedPlayerAnchor();
            ApplyPlayerAnchorAim(_ownedPlayerAnchor);
            RefreshProviders();
            _trace?.Detail(traceMessage);
            return _ownedPlayerAnchor;
        }

        private void ConfigureOwnedPlayerAnchor()
        {
            if (_ownedPlayerAnchor == null) return;

            ConvaiGazeProfile p = EffectiveProfile;
            if (p == null) return;
            _ownedPlayerAnchor.Configure(
                p.PlayerMaxDistance, p.PlayerFullRelevanceDistance,
                p.PlayerLineOfSight, p.PlayerObstructionMask);
        }

        private void DestroyOwnedPlayerAnchor()
        {
            if (_ownedPlayerAnchor == null) return;

            PlayerAnchorTargetProvider provider = _ownedPlayerAnchor;
            _ownedPlayerAnchor = null;
            _ownedPlayerAnchorFocusOnly = false;
            _providers.Remove(provider);

            if (UnityEngine.Application.isPlaying)
                Destroy(provider);
            else
                DestroyImmediate(provider);
        }

        private void HandleRigBindingChanged(IStandardRigBinding rigBinding)
        {
            _chain.RestoreEyeRest();
            Context?.EnsureCompositor()?.ClearLayer(this, FacialBlendshapeLayers.Eyes);
            _headTorso.Reset();
            _eyes.Reset();
            _chain.Bind(Context, transform);
            _eyeWriter.Bind(Context?.EnsureRigBinding());
            ResolveEyeBackend(EffectiveProfile);

            // A mesh swap that comes with a rig rebind destroys every cached renderer, so the
            // crowd-LOD visibility check must re-resolve here or it reads a dead array.
            RefreshRendererCache(Context != null ? Context.CharacterRoot : transform.root);

            // The one-clear-error contract has to survive a rebind: a new binding without a Head
            // mapping stops gaze just as dead as a missing one at startup did.
            ValidateRig();

            _trace?.State("Rig binding changed — gaze chain recalibrated.");
        }

        private void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized) return;

            ConvaiGazeProfile p = EffectiveProfile;
            if (p == null) return;

            _trace ??= new GazeTrace(name);
            _trace.Verbosity = p.TraceVerbosity;

            _chain.Bind(Context, transform);
            _eyeWriter.Bind(Context?.EnsureRigBinding());
            ResolveEyeBackend(p);
            _blink.Reset(p, ref _random);
            _micro.Reset(ref _random);
            _faceScan.Reset();
            ValidateRig();

            _trace.State(
                $"Gaze runtime initialized. ambient={p.EnableAmbientExploration} faceScan={p.EnableFaceScan} " +
                $"vergence={p.EnableVergence} blink={p.EnableBlink} bodyTurn={p.EnableBodyTurn} " +
                $"emotionModulation={p.EnableEmotionModulation} eyes={p.EyeActuationMode} " +
                $"eyeBackend={(_useEyeBones ? "bones" : _useLookShapes ? "blendshapes" : "disabled")} " +
                // The optional capabilities join the existing init trace rather than adding a
                // second line, so one support log answers "what did this character actually have?"
                $"extras=[{GazeCapabilities.DescribeActive(Context != null ? Context.CharacterRoot : transform.root)}]");

            _runtimeInitialized = true;
        }

        private void ResolveEyeBackend(ConvaiGazeProfile p)
        {
            _useEyeBones = false;
            _useLookShapes = false;
            if (p == null) return;

            switch (p.EyeActuationMode)
            {
                case GazeEyeActuationMode.Auto:
                    _useEyeBones = _chain.HasEyeBones;
                    _useLookShapes = !_useEyeBones && _eyeWriter.HasLookShapes;
                    if (!_useEyeBones && !_useLookShapes)
                        _trace?.Warning(
                            "No eye bones and no EyeLook* blendshapes were resolved — the eye stage is " +
                            "disabled. Head/torso gaze still runs. Check the rig convention mapping.");
                    break;

                case GazeEyeActuationMode.Bones:
                    _useEyeBones = _chain.HasEyeBones;
                    if (!_useEyeBones)
                        _trace?.Warning("Eye backend forced to Bones but no LeftEye/RightEye bone pair was resolved.");
                    break;

                case GazeEyeActuationMode.Blendshapes:
                    _useLookShapes = _eyeWriter.HasLookShapes;
                    if (!_useLookShapes)
                        _trace?.Warning("Eye backend forced to Blendshapes but no EyeLook* shapes were resolved.");
                    break;

                case GazeEyeActuationMode.Disabled:
                    break;
            }
        }

        /// <summary>
        ///     Reports an unusable rig exactly once per distinct binding. Called on first
        ///     initialization and on every rig rebind, because a runtime rebind can break a rig
        ///     that was fine at startup — but a rebind loop must not turn one clear error into a
        ///     per-frame console flood, so the last-reported binding is latched.
        /// </summary>
        private void ValidateRig()
        {
            if (_trace == null) return;

            IStandardRigBinding rigBinding = Context?.EnsureRigBinding();
            bool usable = rigBinding != null &&
                          rigBinding.TryGetBone(StandardBone.Head, out Transform head) && head != null;

            if (!ShouldReportRigWarning(usable, rigBinding, _rigWarningBinding, _rigWarningReported))
            {
                // A usable rig also clears the latch, so a rig that breaks again later still reports.
                if (usable)
                {
                    _rigWarningBinding = null;
                    _rigWarningReported = false;
                }
                return;
            }

            _rigWarningBinding = rigBinding;
            _rigWarningReported = true;

            _trace.Warning(rigBinding == null
                ? "No semantic rig binding could be resolved. Add StandardRigBinding to the character " +
                  "root and map Head (plus optional Neck/Eyes); gaze stays inert until a binding exists."
                : "Rig binding has no semantic Head mapping. Assign Head in StandardRigBinding or use " +
                  "a recognized bone name; head/eye gaze stays inert until Head resolves.");
        }

        /// <summary>
        ///     The log-once decision for <see cref="ValidateRig" />: warn when the rig is unusable
        ///     and this exact binding has not already been reported. Pure and static so the
        ///     rebind-loop cases are unit-testable without a rig.
        /// </summary>
        /// <remarks>
        ///     A rebind to a <em>different</em> broken binding warns again — it is genuinely new
        ///     information. A rebind to the same one does not, which is what keeps a rebind loop
        ///     from turning the one clear error into a per-frame console flood.
        /// </remarks>
        internal static bool ShouldReportRigWarning(
            bool usable, IStandardRigBinding current, IStandardRigBinding lastReported, bool alreadyReported)
        {
            if (usable) return false;
            return !alreadyReported || !ReferenceEquals(lastReported, current);
        }
    }
}
