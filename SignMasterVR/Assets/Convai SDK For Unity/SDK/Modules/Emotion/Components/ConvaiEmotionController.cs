using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Emotion;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Modules.Emotion.Core;
using Convai.Modules.Emotion.Compilation;
using Convai.Modules.Emotion.Direction;
using Convai.Modules.Emotion.Integrations;
using Convai.Modules.Emotion.Outputs;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Components;
using Convai.Runtime.Emotion;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Emotion.Components
{
    /// <summary>
    ///     MonoBehaviour front-end for the Emotion module. Consumes server emotion events,
    ///     smooths scores through an <see cref="EmotionScoreAccumulator" />, and dispatches the
    ///     composed reading to the character's face and to any output bindings authored on the
    ///     <see cref="ConvaiEmotionProfile" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Registers itself as the authoritative <see cref="IEmotionStateSource" /> for
    ///         the character, and when a blendshape binding is active, as the
    ///         <see cref="IEmotionMouthWeightProvider" /> for LipSync handoff.
    ///     </para>
    /// </remarks>
    [EmbodimentModule(ModuleIds.Emotion, "Emotion",
        Description = "Facial expression and mood, driven by what the character feels.",
        Absence = "the face stays neutral — no expression, and no mood behind what is said.",
        Order = 20)]
    [AddComponentMenu("Convai/Embodiment/Emotion")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MoodCommandHandlerAdapter))]
    public sealed class ConvaiEmotionController : ConvaiCharacterModule<ConvaiEmotionProfile>,
        IEmotionStateSource,
        IEmotionStateFrameSource,
        IEmotionMouthWeightProvider,
        IEmbodimentTickable,
        IEmotionDetectionModeSource,
        IBrowCueSink
    {
        [Header("Detection")]
        [SerializeField]
        // The wording is deliberate: the two providers are named here the way the dropdown names
        // them, never by the vendor words the enum members carry. A user choosing between them
        // should be choosing between what they do, not between two acronyms.
        [Tooltip("How this character's feelings are worked out. Off asks for nothing, so it never " +
                 "receives anything to feel — the same as removing this component. Responsive updates " +
                 "while the reply is being spoken; Accurate reads the whole reply once and holds up in " +
                 "any language. This setting decides it, overriding any emotion setting on the backend.")]
        private EmotionDetectionMode detectionMode = EmotionDetectionMode.Nrclex;

        [Header("Overrides")]
        [SerializeField]
        [Tooltip("When enabled, the pipeline ignores server events and holds the locked emotion.")]
        private bool lockEmotion;

        [SerializeField, ConvaiEmotionLabel]
        [Tooltip("The emotion to hold while this is on.")]
        private string lockedEmotionLabel = "neutral";

        [SerializeField, Range(0f, 1f)]
        private float lockedIntensity = 1f;

        [SerializeField, ConvaiEmotionLabel("Use the personality's resting mood")]
        [Tooltip("Per-character resting-mood override applied when the pipeline builds. Precedence: " +
                 "SetMood() (runtime) > Initial Mood (this field) > profile Persona Baseline. Empty = use the " +
                 "profile's Persona Baseline. A label that resolves to the taxonomy's neutral entry forces a " +
                 "truly neutral rest, suppressing the profile baseline instead of falling through to it.")]
        private string initialMoodLabel = "";

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Intensity for Initial Mood Label. Only used when the label is non-empty and resolves to a " +
                 "non-neutral taxonomy entry; ignored when Initial Mood is empty or forces neutral rest.")]
        // Defaults to a usable resting strength rather than 0: with 0, picking an Initial Mood
        // silently did nothing until the user also found and raised this second field. Shared with
        // the inspector, troubleshooter and MCP seeds so all four agree on what "usable" means.
        private float initialMoodIntensity = Profiles.EmotionPersonalityTable.DefaultRestingMoodIntensity;

        [Header("Action Performance Reactions")]
        [Tooltip("A brief mood nudge after an action step succeeds or fails. Only runs when this " +
                 "character has an Action Runner and its Performance toggle is on; inert otherwise.")]
        // These shipped as "satisfied" and "frustrated" — two words no emotion vocabulary in the
        // SDK defines, so the feature resolved nothing and silently did nothing on every character
        // that enabled it. Defaults now name emotions that actually exist.
        [SerializeField, ConvaiEmotionLabel("No reaction after a success")]
        private string actionSuccessMoodLabel = "joy";

        [SerializeField, ConvaiEmotionLabel("No reaction after a failure")]
        private string actionFailureMoodLabel = "sadness";

        [SerializeField, Range(0f, 1f)]
        private float actionMoodIntensity = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("How long the outcome mood beat holds before reverting to the authored baseline.")]
        private float actionMoodHoldSeconds = 2.5f;

        [SerializeField, Min(0f)]
        private float actionMoodTransitionSeconds = 1f;

        private ActionPerformanceMoodReactor _actionPerformanceReactor;

        private ConvaiCharacter _character;
        private ConvaiEmotionProfile _effectiveProfile;
        private EmotionTaxonomyAsset _effectiveTaxonomy;
        private bool _createdSyntheticTaxonomy;

        private EmotionScoreAccumulator _accumulator;

        private readonly List<IEmotionOutputBinding> _activeBindings = new(1);
        private CompiledEmotionModel _compiledExpressionModel;
        private EmotionExpressionPlanner _expressionPlanner;
        private SemanticBlendshapeEmotionOutput _semanticExpressionOutput;

        // ── Micro-expression life layer (built on demand, ticked every frame; opt-in via profile) ──
        private MicroExpressionDirector _microDirector;
        private MicroExpressionBinding _microBinding;

        // Last-seen dialogue beat, used to detect Reacting/Interrupted TRANSITIONS (one-shot
        // triggers fire only on change, never every tick the state persists). Reset on teardown.
        private DialogueState _lastDialogueBeat = DialogueState.Idle;

        private readonly Dictionary<string, float> _currentScoresSnapshot =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, float> _expressivenessGains =
            new(StringComparer.OrdinalIgnoreCase);

        private SubscriptionToken _emotionToken;
        private SubscriptionToken _speechToken;
        private SubscriptionToken _sessionToken;
        private IEventHub _subscribedEventHub;
        private bool _dependenciesChangedHandlerRegistered;
        private bool _rigBindingChangedHandlerRegistered;
        private bool _warnedAboutUnscopedEmotionEvent;
        private bool _warnedAboutMissingCharacter;
        private bool _warnedAboutNoAuthoredSlots;
        private readonly HashSet<string> _warnedUnknownLabels = new(StringComparer.OrdinalIgnoreCase);

        // Cached public snapshot. The runtime pipeline keeps its mutable current-frame state in
        // the scalar fields and preallocated score table below; a defensive EmotionReading is
        // captured only when a caller actually asks for Current, so a retained snapshot stays
        // safe without allocating on every embodiment tick.
        private EmotionReading _cachedReading = EmotionReading.Neutral;
        private int _stateVersion;
        private int _cachedReadingVersion = -1;
        private string _currentDominantLabel = EmotionReading.NeutralLabel;
        private float _currentDominantScore;
        private float _currentMouthInfluence;
        private string _currentMoodLabel = EmotionReading.NeutralLabel;
        private float _currentMoodScore;
        private string[] _frameLabels = Array.Empty<string>();
        private float[] _frameScores = Array.Empty<float>();

        // Per-label taxonomy data that never changes once the pipeline is built, cached
        // index-for-index with _frameLabels. The state frame and the mouth-influence read
        // used to resolve these through the taxonomy's dictionary every tick, for every
        // label - repeated work for values that cannot move until the profile is swapped,
        // which rebuilds the pipeline anyway.
        private EmotionDimensions[] _frameDimensions = Array.Empty<EmotionDimensions>();
        private float[] _frameMouthInfluence = Array.Empty<float>();
        private int _dominantFrameIndex = -1;
        private EmotionStateFrame _currentFrame = EmotionStateFrame.Neutral;
        private string _lastDominantLabel;
        private float _dominantHoldSeconds;
        private bool _isCharacterSpeaking;

        // Compatibility gameplay override. Unlike the serialized preview lock, this channel is
        // not rewritten every tick; it owns the accumulator target until explicitly cleared and
        // rejects backend transients while active, matching the public API contract.
        private bool _emotionOverrideActive;
        private string _emotionOverrideLabel;
        private float _emotionOverrideScore;

        // Older backends do not send a sequence number. The event timestamp is still a useful
        // monotonic guard against queued/out-of-order delivery within the current session.
        private DateTime _lastAcceptedEmotionTimestamp = DateTime.MinValue;
        private long _lastAcceptedEmotionSequence = -1;

        // ── Voice-energy coupled expression ────────────────────────────────────────────
        /// <summary>
        ///     Exponential rate/s (see <see cref="TickProsodyGain" />) the smoothed
        ///     <see cref="_prosodyGain" /> eases toward <see cref="ComputeProsodyGainTarget" />'s
        ///     target. Not authorable — a stable, fast-but-not-twitchy default.
        /// </summary>
        private const float ProsodyGainSmoothingRate = 8f;

        /// <summary>
        ///     Below this delta from the target, <see cref="TickProsodyGain" /> snaps instead of
        ///     continuing to ease, so the gain settles exactly at 1 (or the target) instead of
        ///     asymptotically approaching it forever.
        /// </summary>
        private const float ProsodyGainSnapEpsilon = 0.001f;

        /// <summary>
        ///     Smoothed global intensity multiplier applied to every output binding's composed
        ///     intensity (see <see cref="IEmotionOutputBinding.Apply" />). <c>1</c> = no change;
        ///     always exactly <c>1</c> when <see cref="ConvaiEmotionProfile.ProsodyCoupling" /> is
        ///     <c>0</c>. Reset to 1 on
        ///     pipeline teardown.
        /// </summary>
        private float _prosodyGain = 1f;

        // ── Resolved emotion/mood gameplay events bookkeeping ──────────────────────────
        // Dominant transitions reuse _lastDominantLabel (see EmbodimentTick) rather than a
        // dedicated field. Mood has no equivalent existing tracker, so it gets one field. Both
        // are null-sentinel ("not yet observed") and reset to null in TeardownPipeline so a
        // rebuild never fires a spurious "changed" event from its first composed reading.
        private string _lastNotifiedMoodLabel;

        // ── Blending: a primary emotion plus its complements, with anti-flicker guards ──
        /// <summary>
        ///     Below this primary score, the controller is considered "at rest" (no active
        ///     primary emotion), so hysteresis (dwell/margin) is bypassed and the next incoming
        ///     emotion is accepted immediately regardless of its strength. Hysteresis exists to
        ///     prevent flicker between two ACTIVE emotions — it must never block the first one.
        /// </summary>
        private const float RestPrimaryThreshold = 0.001f;

        private float _emotionClock;
        private string _primaryLabel;
        private float _primaryScore;
        private float _lastSwitchTime;
        private string[] _blendLabels;
        private float[] _blendScores;

        // ── Mood pickup — a low-intensity, capped facial echo of a nearby OTHER
        // character's strong dominant emotion. Registration is unconditional (every emotion-
        // bearing character is witnessable); the throttled scan below only actually runs when
        // this character opts in via ConvaiEmotionProfile.ContagionEnabled.
        private const float ContagionScanInterval = 0.25f;
        private const float ContagionActivationThreshold = 0.35f;

        private EmotionContagionRegistry.Entry _contagionRegistryEntry;

        /// <summary>
        ///     Withdrawal token for the brow-cue sink contract. Held separately because that
        ///     registration is scoped to the micro-expression layer, which can be torn down and
        ///     rebuilt while the component stays enabled.
        /// </summary>
        private CharacterServiceRegistry.ServiceToken _browCueSinkToken;
        private float _contagionScanPhaseOffset;
        private float _nextContagionScanTime;

        /// <inheritdoc />
        public EmotionReading Current
        {
            get
            {
                if (_cachedReadingVersion == _stateVersion) return _cachedReading;

                _cachedReading = new EmotionReading(
                    _currentDominantLabel,
                    _currentDominantScore,
                    _currentScoresSnapshot,
                    _currentMouthInfluence,
                    _dominantHoldSeconds,
                    _currentMoodLabel,
                    _currentMoodScore);
                _cachedReadingVersion = _stateVersion;
                return _cachedReading;
            }
        }

        /// <inheritdoc />
        public EmotionStateFrame CurrentFrame => _currentFrame;

        /// <summary>
        ///     Emotion detection mode requested for this character. Resolved at connect time to
        ///     decide whether the backend runs detection and which provider it uses.
        /// </summary>
        public EmotionDetectionMode EmotionDetectionMode => detectionMode;

        /// <summary>Canonical emotion label after taxonomy resolution, smoothing, and profile composition.</summary>
        public string CurrentResolvedEmotion => _currentDominantLabel;

        /// <summary>Composed normalized intensity [0, 1] for <see cref="CurrentResolvedEmotion" />.</summary>
        public float CurrentNormalizedIntensity => _currentDominantScore;

        /// <summary>
        ///     Canonical label of the character's persona/temperament resting mood. This is
        ///     explicitly NOT the transient dominant emotion — see <see cref="CurrentResolvedEmotion" />.
        /// </summary>
        public string CurrentMoodLabel => _currentMoodLabel;

        /// <summary>Normalized <c>[0, 1]</c> intensity for <see cref="CurrentMoodLabel" />.</summary>
        public float CurrentMoodScore => _currentMoodScore;

        /// <summary>
        ///     Raised when the SMOOTHED dominant (transient) emotion label changes — i.e. the same
        ///     label transition <see cref="CurrentResolvedEmotion" /> observes, hysteresis and all.
        ///     Fires once per transition (label-change only, not on score-only movement while the
        ///     label persists), with the new label and its current <see cref="CurrentNormalizedIntensity" />.
        ///     Never raised before the pipeline builds or while it is torn down; a subscriber
        ///     exception is caught and logged, never propagated into the tick.
        /// </summary>
        public event Action<string, float> DominantEmotionChanged;

        /// <summary>
        ///     Raised when the resolved resting-mood label (<see cref="CurrentMoodLabel" />)
        ///     changes — covers the baseline first taking effect, <see cref="SetMood" />, mood
        ///     drift taking over, <see cref="ClearMood" />, and a session-reset revert. Fires once
        ///     per transition (label-change only), with the new label and its current
        ///     <see cref="CurrentMoodScore" />. Never raised before the pipeline builds or while
        ///     it is torn down; a subscriber exception is caught and logged, never propagated into
        ///     the tick.
        /// </summary>
        public event Action<string, float> MoodChanged;

        /// <inheritdoc />
        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <summary>
        ///     The personality asset assigned in the Inspector, or <c>null</c> when this character
        ///     runs on the SDK defaults.
        /// </summary>
        /// <remarks>
        ///     For the editor surfaces only, which need to answer "is a profile assigned, and which
        ///     one" on every repaint. Reading it through a <see cref="UnityEditor.SerializedObject" />
        ///     — the only other way to reach a <c>protected</c> field — allocated one per call, from
        ///     five call sites, on an inspector that repaints every frame in Play Mode. This is
        ///     deliberately <em>not</em> the effective profile: a surface that offers to create one
        ///     must be able to tell "none assigned" from "the runtime default".
        /// </remarks>
        internal ConvaiEmotionProfile AssignedProfile => profile;

        /// <inheritdoc />
        protected override string ProfileModuleId => ModuleIds.Emotion;

        /// <inheritdoc />
        protected override System.Func<ConvaiEmotionProfile> DefaultProfileFactory => ConvaiEmotionProfile.CreateDefault;

        /// <inheritdoc />
        protected override void OnProfileApplied(ConvaiEmotionProfile newProfile)
        {
            _effectiveProfile = null;
            _warnedAboutNoAuthoredSlots = false; // re-evaluate the "no facial slots" diagnostic for the new profile

            if (!isActiveAndEnabled) return;
            RebuildPipeline();
        }

        /// <inheritdoc />
        public bool TryGetMouthWeight(BlendshapeTargetKey key, out float weight)
        {
            if (_semanticExpressionOutput != null)
                return _semanticExpressionOutput.TryGetMouthWeight(key, out weight);
            weight = 0f;
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            _character = GetComponentInParent<ConvaiCharacter>(true);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;

            BuildPipeline();
            Context.DependenciesPopulated += HandleDependenciesPopulated;
            _dependenciesChangedHandlerRegistered = true;
            Context.RigBindingChanged += HandleRigBindingChanged;
            _rigBindingChangedHandlerRegistered = true;
            SubscribeToEventHub();

            ProvideService<IEmotionStateSource>(this);
            // A first-class contract rather than something consumers find by downcasting
            // IEmotionStateSource: the per-frame view is a separate capability, so it is a separate
            // registration and a consumer that needs it asks for it by name.
            ProvideService<IEmotionStateFrameSource>(this);
            // Always register; TryGetMouthWeight returns false when no blendshape binding is
            // active so the controller is observable but contributes nothing.
            ProvideService<IEmotionMouthWeightProvider>(this);

            // Every emotion-bearing character registers as a witness/source
            // unconditionally — opting IN to actually reacting is a per-character profile setting
            // consulted only in the throttled scan (TickContagionScan), never here.
            _contagionRegistryEntry = new EmotionContagionRegistry.Entry
            {
                Context = Context,
                Root = Context.CharacterRoot,
                Controller = this
            };
            EmotionContagionRegistry.Register(_contagionRegistryEntry);

            Context.EnsureTickScheduler()?.Register(this);

            // Registers unconditionally (mirrors every other seam
            // registration above); the dispatcher's own Performance toggle decides whether it is
            // ever notified, so a disabled toggle is a true no-op, not a missing registration.
            _actionPerformanceReactor ??= new ActionPerformanceMoodReactor(this);
            ContributeService<IActionPerformanceReactor>(_actionPerformanceReactor);
        }

        protected override void OnDisable()
        {
            EmotionContagionRegistry.Unregister(_contagionRegistryEntry);
            _contagionRegistryEntry = null;

            if (_dependenciesChangedHandlerRegistered && Context != null)
            {
                Context.DependenciesPopulated -= HandleDependenciesPopulated;
                _dependenciesChangedHandlerRegistered = false;
            }

            if (_rigBindingChangedHandlerRegistered && Context != null)
            {
                Context.RigBindingChanged -= HandleRigBindingChanged;
                _rigBindingChangedHandlerRegistered = false;
            }

            UnsubscribeFromEventHub();
            // The emotion state, frame and mouth-weight contracts plus the action reactor were
            // published through the base class, which releases their tokens in base.OnDisable().
            Context?.TickScheduler?.Unregister(this);

            TeardownPipeline();

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            ReleaseSyntheticAssets();
            base.OnDestroy();
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (_accumulator == null) return;

            if (deltaTime > 0f) _emotionClock += deltaTime;

            if (lockEmotion)
                _accumulator.SetImmediateEmotion(lockedEmotionLabel, lockedIntensity);
            else
                _accumulator.Tick(deltaTime);

            TickContagionScan();

            // Snapshot _lastDominantLabel BEFORE UpdateDominantHold overwrites it, so the
            // transition it detects can be compared against the reading composed at the end of
            // this method. null only immediately after a (re)build (see TeardownPipeline) — that
            // is what suppresses the spurious "changed" fire on the pipeline's first reading.
            string previousDominantLabel = _lastDominantLabel;
            _accumulator.GetDominant(out string dominantLabel, out float dominantScore);
            UpdateDominantHold(dominantLabel, deltaTime);
            _accumulator.GetMood(out string moodLabel, out float moodScore);

            CopyOutputScoresSnapshot();

            // Read the shared speech-energy signal ONCE per tick (Current only — never
            // Sample(), which LipSync owns) and reuse it for both the prosody gain below and
            // TickMicroExpressions, instead of each reading it independently.
            float speechEnergy = Context?.SpeechEnergyProvider?.Current ?? 0f;
            if (float.IsNaN(speechEnergy) || speechEnergy < 0f) speechEnergy = 0f;

            float prosodyGainTarget = ComputeProsodyGainTarget(
                _isCharacterSpeaking, speechEnergy, _effectiveProfile != null ? _effectiveProfile.ProsodyCoupling : 0f);
            TickProsodyGain(prosodyGainTarget, deltaTime);

            if (_expressionPlanner != null)
            {
                // The profile's Prosody Coupling reaches the face through the planner's own
                // per-channel prosody term. Passing the smoothed _prosodyGain only to the output
                // bindings below (as this used to) meant the knob moved animator parameters and
                // shader properties but never the expression itself.
                _expressionPlanner.Tick(
                    _frameScores, _isCharacterSpeaking, speechEnergy, deltaTime, _prosodyGain);
                _semanticExpressionOutput?.Apply(_expressionPlanner.Weights);
            }

            for (int i = 0; i < _activeBindings.Count; i++)
                _activeBindings[i].Apply(_currentScoresSnapshot, _prosodyGain);

            // Micro-life rides on top of the bindings' Apply above (compositor layer order is
            // additive, not sequencing-sensitive, but ticking last keeps it reading the same
            // dominant label/score the rest of this frame just settled on).
            TickMicroExpressions(deltaTime, dominantLabel, dominantScore, moodLabel, moodScore, speechEnergy);

            _dominantFrameIndex = FrameIndexOf(dominantLabel);
            float mouthInfluence = ResolveMouthInfluence(dominantScore);
            _currentDominantLabel = dominantLabel;
            _currentDominantScore = dominantScore;
            _currentMouthInfluence = mouthInfluence;
            _currentMoodLabel = moodLabel;
            _currentMoodScore = moodScore;
            unchecked { _stateVersion++; }
            UpdateBorrowedFrame();

            // Resolved emotion/mood gameplay events, fired only on label TRANSITIONS, AFTER
            // _currentReading is composed so subscribers observe a consistent Current. Zero
            // allocation on the (overwhelmingly common) no-transition path; RaiseResolvedEmotionEvent
            // only allocates on the rare transition fire (see its remarks).
            if (previousDominantLabel != null &&
                !string.Equals(previousDominantLabel, _currentDominantLabel, StringComparison.OrdinalIgnoreCase))
            {
                RaiseResolvedEmotionEvent(DominantEmotionChanged, nameof(DominantEmotionChanged),
                    _currentDominantLabel, _currentDominantScore);
            }

            if (_lastNotifiedMoodLabel == null)
            {
                // First-ever composed reading: seed the bookkeeping without firing, so building
                // the pipeline is not itself reported as a mood change.
                _lastNotifiedMoodLabel = _currentMoodLabel;
            }
            else if (!string.Equals(_lastNotifiedMoodLabel, _currentMoodLabel, StringComparison.OrdinalIgnoreCase))
            {
                _lastNotifiedMoodLabel = _currentMoodLabel;
                RaiseResolvedEmotionEvent(MoodChanged, nameof(MoodChanged),
                    _currentMoodLabel, _currentMoodScore);
            }
        }

        /// <summary>
        ///     Guarded dispatch for <see cref="DominantEmotionChanged" />/<see cref="MoodChanged" />:
        ///     mirrors <c>Convai.Runtime.Utilities.SafeEventInvoker</c> (internal to
        ///     <c>Convai.Runtime</c> and not <c>InternalsVisibleTo</c> this module's asmdef, so it
        ///     cannot be reused directly). Each subscriber is invoked individually via
        ///     <see cref="Delegate.GetInvocationList" /> so a throwing subscriber cannot break the
        ///     tick or block subsequent subscribers in the chain. This allocates
        ///     (<c>GetInvocationList</c> + closures are not used, but the array itself is), which
        ///     would violate the module's zero-steady-state-allocation rule on a per-frame path —
        ///     but these events fire only on rare label transitions, never every tick, so it is not
        ///     a per-frame path and the allocation is acceptable and intentional.
        /// </summary>
        private void RaiseResolvedEmotionEvent(Action<string, float> handlers, string eventName, string label, float score)
        {
            if (handlers == null) return;

            foreach (Delegate rawHandler in handlers.GetInvocationList())
            {
                var handler = (Action<string, float>)rawHandler;
                try
                {
                    handler(label, score);
                }
                catch (Exception ex)
                {
                    Context?.Logger?.Error(
                        $"[ConvaiEmotionController] {eventName} subscriber threw: {ex}");
                }
            }
        }

        /// <summary>
        ///     Sets an explicit override emotion, bypassing server events until
        ///     <see cref="ClearEmotionOverride" /> is called.
        /// </summary>
        public void SetEmotionOverride(string label, float score)
        {
            if (_accumulator == null) return;

            if (_effectiveTaxonomy == null || !_effectiveTaxonomy.TryResolve(label, out EmotionDescriptor descriptor))
            {
                if (!string.IsNullOrWhiteSpace(label) && _warnedUnknownLabels.Add(label))
                    Context?.Logger?.Warning(
                        $"[ConvaiEmotionController] SetEmotionOverride was given '{label}', which this character's "
                        + "emotion vocabulary does not define, so the face stays neutral. Pass a label the "
                        + "vocabulary defines, or add it to that emotion's other words on the vocabulary asset.");
                descriptor = _effectiveTaxonomy?.Neutral;
            }

            _emotionOverrideActive = true;
            _emotionOverrideLabel = descriptor?.Label ?? EmotionReading.NeutralLabel;
            _emotionOverrideScore = descriptor != null && descriptor.IsNeutral ? 0f : Mathf.Clamp01(score);
            _accumulator.SetTargetEmotion(_emotionOverrideLabel, _emotionOverrideScore);
        }

        /// <summary>Clears any override and restores neutral state until the next server event.</summary>
        public void ClearEmotionOverride()
        {
            if (_accumulator == null) return;
            _emotionOverrideActive = false;
            _emotionOverrideLabel = null;
            _emotionOverrideScore = 0f;
            _accumulator.SetTargetEmotion(_effectiveTaxonomy?.Neutral?.Label ?? EmotionReading.NeutralLabel, 0f);
        }

        /// <summary>Locks the character to a specific emotion until <see cref="UnlockEmotion" /> is called.</summary>
        public void LockEmotion(string label, float intensity = 1f)
        {
            lockEmotion = true;
            lockedEmotionLabel = label;
            lockedIntensity = Mathf.Clamp01(intensity);
            _accumulator?.SetImmediateEmotion(lockedEmotionLabel, lockedIntensity);
        }

        /// <summary>
        ///     Releases a previous <see cref="LockEmotion" /> call.
        /// </summary>
        /// <remarks>
        ///     Clearing the flag is not enough on its own: <see cref="LockEmotion" /> goes through
        ///     <see cref="EmotionScoreAccumulator.SetImmediateEmotion" />, which writes the locked
        ///     value into the target scores as well as the current ones. Without resetting the
        ///     target here, the tick keeps smoothing toward the locked emotion and the face holds
        ///     that expression until the next backend event — indefinitely on a quiet connection.
        ///     An active gameplay override (<see cref="SetEmotionOverride" />) owns the target
        ///     instead, and is restored rather than cleared, mirroring
        ///     <see cref="OnSessionStateChanged" />'s ordering.
        /// </remarks>
        public void UnlockEmotion()
        {
            lockEmotion = false;

            if (_accumulator == null) return;

            if (_emotionOverrideActive)
                _accumulator.SetTargetEmotion(_emotionOverrideLabel, _emotionOverrideScore);
            else
                _accumulator.SetTargetEmotion(
                    _effectiveTaxonomy?.Neutral?.Label ?? EmotionReading.NeutralLabel, 0f);
        }

        /// <summary>
        ///     Sets a runtime resting-mood override, smoothly transitioning the persona baseline
        ///     to <paramref name="label" />/<paramref name="intensity" /> over
        ///     <paramref name="transitionSeconds" />. This is independent of the transient
        ///     server-driven emotion (<see cref="CurrentResolvedEmotion" />) and of
        ///     <see cref="LockEmotion" />; it does not persist across a session reset — see
        ///     <see cref="ClearMood" />. An empty/neutral label, an unknown label (warns once), or
        ///     a non-positive intensity all transition to "no mood" rather than the authored
        ///     baseline. Safe no-op before the pipeline is built.
        /// </summary>
        public void SetMood(string label, float intensity, float transitionSeconds = 1.5f)
        {
            if (_accumulator == null) return;

            string resolvedLabel = null;
            float resolvedIntensity = 0f;

            if (!string.IsNullOrWhiteSpace(label))
            {
                if (_effectiveTaxonomy != null && _effectiveTaxonomy.TryResolve(label, out EmotionDescriptor descriptor))
                {
                    if (!descriptor.IsNeutral)
                    {
                        resolvedLabel = descriptor.Label;
                        resolvedIntensity = Mathf.Clamp01(intensity);
                    }
                }
                else if (_warnedUnknownLabels.Add(label))
                {
                    Context?.Logger?.Warning(
                        $"[ConvaiEmotionController] SetMood was given '{label}', which this character's emotion "
                        + "vocabulary does not define, so the character rests at no mood. Pass a label the "
                        + "vocabulary defines, or add it to that emotion's other words on the vocabulary asset.");
                }
            }

            _accumulator.SetPersonaBaselineTarget(
                resolvedLabel ?? _effectiveTaxonomy?.Neutral.Label ?? EmotionReading.NeutralLabel,
                resolvedIntensity,
                transitionSeconds);
        }

        /// <summary>
        ///     Clears any active runtime mood set via <see cref="SetMood" />, smoothly
        ///     transitioning back to the AUTHORED baseline (this character's <c>Initial Mood</c>
        ///     override when set, otherwise the profile's Persona Baseline — which may itself be
        ///     "no mood") over <paramref name="transitionSeconds" />. Safe no-op before the
        ///     pipeline is built.
        /// </summary>
        public void ClearMood(float transitionSeconds = 1.5f)
        {
            if (_accumulator == null) return;

            ResolveAuthoredBaseline(out string label, out float intensity);
            _accumulator.SetPersonaBaselineTarget(label, intensity, transitionSeconds);
        }

        /// <summary>
        ///     Attempts to resolve <paramref name="label" /> against this character's active
        ///     emotion taxonomy (canonical labels and aliases), returning the canonical,
        ///     non-neutral label. The mood and reaction Action Behaviors call this to validate a
        ///     label BEFORE calling
        ///     <see cref="SetMood" />/<see cref="SetEmotionOverride" />, which otherwise silently
        ///     degrade an unknown label to neutral rather than failing — this lets a caller fail
        ///     actionably instead. Returns <c>false</c> for an empty/whitespace label, a label the
        ///     taxonomy cannot resolve, a label that resolves to the taxonomy's neutral entry (not
        ///     a valid mood/reaction), or when the pipeline has not built yet (no taxonomy resolved).
        /// </summary>
        public bool TryResolveEmotionLabel(string label, out string canonicalLabel)
        {
            canonicalLabel = null;
            if (_effectiveTaxonomy == null || string.IsNullOrWhiteSpace(label)) return false;
            if (!_effectiveTaxonomy.TryResolve(label, out EmotionDescriptor descriptor)) return false;
            if (descriptor.IsNeutral) return false;

            canonicalLabel = descriptor.Label;
            return true;
        }

        /// <summary>
        ///     Non-neutral canonical labels recognized by this character's active emotion
        ///     taxonomy, in taxonomy authoring order. Used by action executors to compose an
        ///     actionable "unknown label" failure message via <see cref="TryResolveEmotionLabel" />.
        ///     Empty before the pipeline builds. Allocates a fresh list per call — only intended
        ///     for the (infrequent) failure-reporting path, never a per-frame one.
        /// </summary>
        public IReadOnlyList<string> KnownEmotionLabels
        {
            get
            {
                if (_effectiveTaxonomy == null) return Array.Empty<string>();

                IReadOnlyList<EmotionDescriptor> emotions = _effectiveTaxonomy.Emotions;
                var labels = new List<string>(emotions.Count);
                for (int i = 0; i < emotions.Count; i++)
                {
                    if (!emotions[i].IsNeutral) labels.Add(emotions[i].Label);
                }
                return labels;
            }
        }

        /// <summary>
        ///     Brief outcome mood beat: nudges the character briefly toward
        ///     <see cref="actionSuccessMoodLabel" />/<see cref="actionFailureMoodLabel" /> after an
        ///     action step's outcome, then lets it lift off on its own. Called only by
        ///     <see cref="ActionPerformanceMoodReactor" />; safe no-op before the pipeline is built
        ///     or when the resolved label is empty/unknown.
        /// </summary>
        /// <remarks>
        ///     Runs on the accumulator's own short-lived beat channel rather than through
        ///     <see cref="SetMood" />/<see cref="ClearMood" />. The old implementation set a mood
        ///     and then cleared it, and clearing means "return to the AUTHORED baseline" — so a
        ///     two-second reaction silently discarded any gameplay <see cref="SetMood" /> and any
        ///     accumulated mood drift, breaking this module's own documented precedence. The beat
        ///     now rides on top and leaves whatever is underneath it intact.
        /// </remarks>
        internal void ReactToActionOutcome(bool success)
        {
            if (_accumulator == null) return;

            string label = success ? actionSuccessMoodLabel : actionFailureMoodLabel;
            if (string.IsNullOrWhiteSpace(label)) return;

            if (!TryResolveEmotionLabel(label, out string canonicalLabel))
            {
                if (_warnedUnknownLabels.Add(label))
                    Context?.Logger?.Warning(
                        $"[ConvaiEmotionController] Mood After Actions uses '{label}', which this character's " +
                        "emotion vocabulary does not recognize, so the outcome reaction will not play. " +
                        "Pick a label the vocabulary defines, or add it as an alias.");
                return;
            }

            _accumulator.SetOutcomeBeat(
                canonicalLabel, actionMoodIntensity, actionMoodHoldSeconds, actionMoodTransitionSeconds);
        }

        /// <summary>
        ///     Resolves the AUTHORED resting-mood pair (label + intensity) that
        ///     <see cref="BuildPipeline" />, <see cref="ClearMood" />, and the session-reset
        ///     handler all agree on, following the precedence chain
        ///     <c>Initial Mood</c> override &gt; profile Persona Baseline (SetMood is a separate,
        ///     higher-priority runtime layer that is cleared by <see cref="ClearMood" /> before
        ///     this method runs). Three cases: (1) <c>initialMoodLabel</c> is non-empty and
        ///     resolves to a non-neutral taxonomy entry — the override wins, using
        ///     <c>initialMoodIntensity</c>; (2) it is non-empty and resolves to the taxonomy's
        ///     neutral entry — this FORCES a truly neutral rest (taxonomy neutral label, 0
        ///     intensity), suppressing the profile's Persona Baseline rather than falling through
        ///     to it; (3) it is empty/whitespace or does not resolve at all — the profile's
        ///     Persona Baseline is used. Returns the taxonomy's neutral label and 0 when nothing
        ///     is configured.
        /// </summary>
        private void ResolveAuthoredBaseline(out string label, out float intensity)
        {
            label = _effectiveTaxonomy?.Neutral.Label ?? EmotionReading.NeutralLabel;
            intensity = 0f;

            if (_effectiveTaxonomy == null) return;

            if (!string.IsNullOrWhiteSpace(initialMoodLabel) &&
                _effectiveTaxonomy.TryResolve(initialMoodLabel, out EmotionDescriptor overrideDescriptor))
            {
                if (overrideDescriptor.IsNeutral)
                {
                    // Force a truly neutral rest — do not fall through to the profile's Persona
                    // Baseline. Writing a neutral label into Initial Mood is how a user says "this
                    // character rests at nothing", so it has to suppress the profile baseline
                    // rather than be treated as an empty field and ignored.
                    label = _effectiveTaxonomy.Neutral.Label;
                    intensity = 0f;
                    return;
                }

                label = overrideDescriptor.Label;
                intensity = Mathf.Clamp01(initialMoodIntensity);
                return;
            }

            if (_effectiveProfile != null &&
                !string.IsNullOrWhiteSpace(_effectiveProfile.BaselineEmotionLabel) &&
                _effectiveTaxonomy.TryResolve(_effectiveProfile.BaselineEmotionLabel, out EmotionDescriptor baselineDescriptor) &&
                !baselineDescriptor.IsNeutral)
            {
                label = baselineDescriptor.Label;
                intensity = _effectiveProfile.BaselineIntensity;
            }
        }

        private void BuildPipeline()
        {
            ReleaseSyntheticTaxonomyIfOwned();
            _effectiveProfile = ResolveProfile();
            if (_effectiveProfile == null) return; // Awake not called yet (ExecuteAlways race)
            _effectiveTaxonomy = _effectiveProfile.ResolveTaxonomyOrDefault(out _createdSyntheticTaxonomy);

            _accumulator = new EmotionScoreAccumulator(_effectiveTaxonomy,
                _effectiveProfile.LerpSpeed, _effectiveProfile.DecaySpeed);
            _accumulator.SetPerEmotionDynamics(_effectiveProfile.EmotionDynamics);
            _accumulator.ConfigureMicroBurst(
                _effectiveProfile.MicroBurstEnabled,
                _effectiveProfile.MicroBurstDuration,
                _effectiveProfile.MicroBurstOvershoot,
                _effectiveProfile.MicroBurstThreshold);
            _accumulator.ConfigureMoodDrift(
                _effectiveProfile.MoodDriftEnabled,
                _effectiveProfile.MoodDriftRate,
                _effectiveProfile.MoodRecoveryRate,
                _effectiveProfile.MoodDriftMaxIntensity);
            _accumulator.ConfigureContagion(_effectiveProfile.ContagionEnabled);

            // Deterministic per-character phase offset in [0, ContagionScanInterval) so many
            // characters' witness scans don't all land on the same frame.
            _contagionScanPhaseOffset = DeterministicEmbodimentRandom.UnitDrawFromLcgSeed(
                DeterministicEmbodimentRandom.CreateSeed(this, 0xC0471A61u)) * ContagionScanInterval;
            _nextContagionScanTime = _contagionScanPhaseOffset;

            ResolveAuthoredBaseline(out string resolvedBaselineLabel, out float resolvedBaselineIntensity);
            _accumulator.SetPersonaBaseline(resolvedBaselineLabel, resolvedBaselineIntensity);

            BuildExpressivenessGainLookup();

            int taxonomyCount = _effectiveTaxonomy.Emotions.Count;
            _blendLabels = new string[taxonomyCount];
            _blendScores = new float[taxonomyCount];
            _frameLabels = new string[taxonomyCount];
            _frameScores = new float[taxonomyCount];
            _frameDimensions = new EmotionDimensions[taxonomyCount];
            _frameMouthInfluence = new float[taxonomyCount];
            for (int i = 0; i < taxonomyCount; i++)
            {
                EmotionDescriptor descriptor = _effectiveTaxonomy.Emotions[i];
                _frameLabels[i] = descriptor.Label;
                _frameDimensions[i] = descriptor.Dimensions;
                _frameMouthInfluence[i] = descriptor.DefaultMouthInfluence;
            }
            _dominantFrameIndex = -1;
            _primaryLabel = _effectiveTaxonomy.Neutral.Label;
            _primaryScore = 0f;
            _lastSwitchTime = 0f;
            _emotionClock = 0f;

            RebuildOutputBindings();
        }

        /// <summary>
        ///     (Re)builds the expression pipeline: the rig-independent semantic face output, plus
        ///     any optional extra output bindings authored on the profile.
        /// </summary>
        /// <remarks>
        ///     There is exactly one facial path. The module previously carried a second,
        ///     slot-list-based one whose data was silently discarded whenever semantic expressions
        ///     were on — which was every shipped profile — so its authored slots, its "build slots
        ///     for rig" tooling and the neutral alternator it fed were all dead weight presented as
        ///     live configuration.
        /// </remarks>
        private void RebuildOutputBindings()
        {
            for (int i = 0; i < _activeBindings.Count; i++)
                _activeBindings[i].Unbind(this);

            _activeBindings.Clear();

            _semanticExpressionOutput?.Unbind();
            _compiledExpressionModel = EmotionProfileCompiler.Compile(
                _effectiveTaxonomy, _effectiveProfile.ExpressionRecipes);
            _expressionPlanner = new EmotionExpressionPlanner(_compiledExpressionModel);
            _semanticExpressionOutput = new SemanticBlendshapeEmotionOutput();
            _semanticExpressionOutput.Bind(this, Context?.EnsureRigBinding(), Context?.EnsureCompositor());

            MaterialPropertyEmotionBinding material = _effectiveProfile.CreateMaterialRuntimeBinding();
            if (material != null && HasAnyAuthoredMaterialSlot(material.Slots))
            {
                IStandardRigBinding materialRigBinding = Context?.EnsureRigBinding();
                // Through the interface: Bind is an explicit implementation because the contract
                // and the rig binding it takes are SDK-internal infrastructure.
                ((IEmotionOutputBinding)material).Bind(this, _effectiveTaxonomy, materialRigBinding);
                _activeBindings.Add(material);
            }

            WarnIfNoFaceResolved();

            RebuildMicroExpressions();
        }

        /// <summary>
        ///     Emits a one-time, actionable warning when nothing on this character's face could be
        ///     resolved, so emotion state will update while the face never moves.
        /// </summary>
        /// <remarks>
        ///     Expression recipes name what should move in semantic terms and the runtime maps
        ///     those onto whichever blendshapes the character's mesh actually has. Zero resolved
        ///     channels therefore means the rig itself is the problem — no facial mesh is bound, or
        ///     its blendshape names match no supported convention — not that the profile is
        ///     under-authored. The message says so, because the previous one told users to press a
        ///     "build slots for rig" button that no longer exists.
        /// </remarks>
        private void WarnIfNoFaceResolved()
        {
            if (_warnedAboutNoAuthoredSlots) return;
            if ((_semanticExpressionOutput?.ResolvedSemanticCount ?? 0) > 0) return;
            if (_activeBindings.Count > 0) return; // e.g. a shader-only blush/sweat setup still drives visible output

            _warnedAboutNoAuthoredSlots = true;
            Context?.Logger?.Warning(
                $"[ConvaiEmotionController] No facial blendshapes could be resolved on '{name}', so emotion state " +
                "will update but the face will not move. Check that the character has a skinned facial mesh with " +
                "blendshapes, and that its blendshape names follow a supported convention (ARKit, Reallusion CC3/CC4, " +
                "or MetaHuman). For a rig using none of those, assign a Custom Rig Convention Map.");
        }

        /// <summary>
        ///     Creates and binds the micro-expression director/source only when
        ///     <c>ConvaiEmotionProfile.MicroExpressionsEnabled</c> is true. When disabled (the
        ///     default), neither object is created, so nothing is ever submitted to the
        ///     compositor's <c>EmotionMicro</c> layer at all.
        /// </summary>
        private void RebuildMicroExpressions()
        {
            TeardownMicroExpressions();

            if (_effectiveProfile == null || !_effectiveProfile.MicroExpressionsEnabled) return;

            IStandardRigBinding rigBinding = Context?.EnsureRigBinding();
            FacialBlendshapeCompositorHost compositor = Context?.EnsureCompositor();
            if (compositor == null) return;

            _microDirector = new MicroExpressionDirector();
            _microDirector.Seed(this);

            _microBinding = new MicroExpressionBinding();
            _microBinding.Bind(this, rigBinding, this, compositor);

            // Only register as the character's brow-cue sink while the micro-expression layer
            // actually owns the brow channels it would compose a cue into — with the feature off,
            // the sink is never registered and Gaze's
            // RaiseBrowCue publish costs a single null check, same as any other absent seam.
            // Scoped to the micro-expression layer's lifetime, not the component's, so this holds
            // its own token instead of riding the base class's OnDisable release.
            if (Context != null) _browCueSinkToken = Context.Provide<IBrowCueSink>(this);
        }

        private void TeardownMicroExpressions()
        {
            _browCueSinkToken.Release();
            _browCueSinkToken = default;

            _microBinding?.Unbind();
            _microBinding = null;
            _microDirector?.Reset();
            _microDirector = null;
            _lastDialogueBeat = DialogueState.Idle;
        }

        /// <summary>
        ///     Brow-cue sink: forwards a cue raised by Gaze to the micro-expression director's
        ///     brow envelope, so the eyebrows move with where the character looks. No-op when
        ///     micro-expressions are disabled (the
        ///     controller is never registered as the sink in that case, so this is unreachable
        ///     from Gaze, but stays a safe no-op if called directly).
        /// </summary>
        void IBrowCueSink.RaiseBrowCue(BrowCueKind kind, float intensity01) =>
            _microDirector?.TriggerBrowCue(kind, intensity01);

        /// <summary>
        ///     Advances the micro-expression director (idle drift + speech accent) and
        ///     submits its weights to the compositor. <paramref name="speechEnergy" /> is read by
        ///     the caller (<see cref="EmbodimentTick" />) from
        ///     <see cref="EmbodimentContext.SpeechEnergyProvider" />'s <c>Current</c> ONLY —
        ///     never <c>Sample()</c>, since the LipSync adapter owns sampling and double-advancing
        ///     shared state would be a subtle cross-module bug — and shared with the voice-energy
        ///     gain so the provider is read exactly once per tick. No-op when the feature is
        ///     disabled or the binding found no shapes.
        /// </summary>
        /// <remarks>
        ///     The bias fed to <see cref="MicroExpressionDirector.SetEmotionBias" /> is the
        ///     STRONGER of the dominant transient and the current mood (see
        ///     <see cref="SelectMicroEmotionBias" />), so a settled resting mood/drift can tint
        ///     idle micro-life even when there is no active transient. With mood inactive
        ///     (<paramref name="moodScore" /> == 0) the choice is always the dominant transient,
        ///     nothing at all.
        ///     <para>
        ///         Also resolves the dialogue-phase state from
        ///         <see cref="Convai.Runtime.Embodiment.EmbodimentContext.ConversationFlowSource" />
        ///         (the same Domain interface Gaze consumes) and feeds
        ///         <see cref="MicroExpressionDirector.SetListeningState" /> so brow/squint gain a
        ///         sustained attentive lift + sparse bursts while the player speaks. No
        ///         ConversationFlow module registered degrades to "never listening" with no log
        ///         and no throw. Gated by <see cref="ConvaiEmotionProfile.ListeningReactionStrength" />
        ///         (0 = off, the default).
        ///     </para>
        ///     <para>
        ///         Also drives a sustained Thinking concentration look (
        ///         <see cref="MicroExpressionDirector.SetThinkingState" />, gated by
        ///         <see cref="ConvaiEmotionProfile.ThinkingReactionStrength" />) and fires one-shot
        ///         beat accents (<see cref="MicroExpressionDirector.TriggerBeatAccent" />) only on
        ///         the TRANSITION into <c>Reacting</c>/<c>Interrupted</c> (tracked via a private
        ///         last-beat field), gated by
        ///         <see cref="ConvaiEmotionProfile.ReactingAccentStrength" />/
        ///         <see cref="ConvaiEmotionProfile.InterruptedFlinchStrength" />.
        ///     </para>
        /// </remarks>
        private void TickMicroExpressions(float deltaTime, string dominantLabel, float dominantScore,
            string moodLabel, float moodScore, float speechEnergy)
        {
            if (_microDirector == null || _microBinding == null) return;

            SelectMicroEmotionBias(dominantLabel, dominantScore, moodLabel, moodScore,
                out string biasLabel, out float biasScore);

            _microDirector.SetEmotionBias(biasLabel, biasScore);

            // Dialogue-phase signal comes from the same Domain interface Gaze consumes —
            // never a bespoke state machine here. Absent ConversationFlow degrades to Idle (never
            // listening), matching the interface's documented fallback contract.
            DialogueState state = Context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            _microDirector.SetListeningState(state == DialogueState.Listening, _effectiveProfile.ListeningReactionStrength);

            // Sustained concentration look for the Thinking beat, same shape as listening.
            _microDirector.SetThinkingState(state == DialogueState.Thinking, _effectiveProfile.ThinkingReactionStrength);

            // One-shot beat accents fire only on the TRANSITION into Reacting/Interrupted,
            // never every tick the state persists.
            if (state != _lastDialogueBeat)
            {
                if (state == DialogueState.Reacting)
                    _microDirector.TriggerBeatAccent(MicroExpressionDirector.BeatAccent.Reacting,
                        _effectiveProfile.ReactingAccentStrength);
                else if (state == DialogueState.Interrupted)
                    _microDirector.TriggerBeatAccent(MicroExpressionDirector.BeatAccent.Interrupted,
                        _effectiveProfile.InterruptedFlinchStrength);

                _lastDialogueBeat = state;
            }

            _microDirector.Tick(
                deltaTime,
                _effectiveProfile.MicroExpressionAmplitude,
                _effectiveProfile.MicroExpressionStillness,
                _effectiveProfile.SpeechAccentStrength,
                speechEnergy);

            _microBinding.Apply(_microDirector);
        }

        /// <summary>
        ///     Pure computation of the voice-energy gain target — the value <see cref="_prosodyGain" />
        ///     eases toward each tick. <c>1</c> (no change) unless the character is currently
        ///     speaking AND <paramref name="coupling" /> is above <c>0</c>; otherwise interpolates
        ///     between <c>1</c> and <c>[0.85, 1.15]</c> (energy 0..1) by <paramref name="coupling" />.
        ///     No allocation.
        /// </summary>
        internal static float ComputeProsodyGainTarget(bool speaking, float energy, float coupling)
        {
            if (!speaking || coupling <= 0f) return 1f;
            return Mathf.Lerp(1f, 0.85f + 0.3f * Mathf.Clamp01(energy), Mathf.Clamp01(coupling));
        }

        /// <summary>
        ///     Eases <see cref="_prosodyGain" /> toward <paramref name="target" /> at
        ///     <see cref="ProsodyGainSmoothingRate" />/s, snapping once within
        ///     <see cref="ProsodyGainSnapEpsilon" /> so it settles exactly rather than
        ///     asymptotically approaching forever. With <paramref name="target" /> always 1
        ///     (coupling 0 or not speaking), this converges to and stays at exactly 1.
        /// </summary>
        private void TickProsodyGain(float target, float deltaTime)
        {
            float delta = target - _prosodyGain;
            if (Mathf.Abs(delta) < ProsodyGainSnapEpsilon)
            {
                _prosodyGain = target;
                return;
            }

            float alpha = 1f - Mathf.Exp(-ProsodyGainSmoothingRate * Mathf.Max(0f, deltaTime));
            _prosodyGain = Mathf.Lerp(_prosodyGain, target, alpha);
        }

        /// <summary>
        ///     Runs the throttled mood-pickup witness scan when
        ///     <see cref="ConvaiEmotionProfile.ContagionEnabled" /> is on and the pipeline is
        ///     built, at most every <see cref="ContagionScanInterval" /> of <see cref="_emotionClock" />
        ///     (staggered per character via <see cref="_contagionScanPhaseOffset" />). Between
        ///     scans, the last scan's result stays in effect on the accumulator's echo channel —
        ///     this method only decides WHEN to rescan, never per-frame.
        /// </summary>
        private void TickContagionScan()
        {
            if (_effectiveProfile == null || !_effectiveProfile.ContagionEnabled) return;
            if (_emotionClock < _nextContagionScanTime) return;

            _nextContagionScanTime = _emotionClock + ContagionScanInterval;
            ScanForContagion();
        }

        /// <summary>
        ///     Iterates <see cref="EmotionContagionRegistry.All" /> once (no allocation,
        ///     never <c>FindObjectsOfType</c>), skipping this character's own entry and any
        ///     destroyed/unresolvable entry, and picks the single strongest nearby OTHER
        ///     character's dominant transient as the contagion candidate. Reads only the other
        ///     character's LAST-LATCHED <see cref="IEmotionStateSource.Current" /> — never triggers
        ///     that character's own cognition tick. No candidate (nothing in range, nothing above
        ///     the activation threshold) resolves to <c>(null, 0)</c>, which
        ///     <see cref="EmotionScoreAccumulator.SetContagionTarget" /> treats as "clear the echo
        ///     target" so a lone character's residual echo naturally decays to zero.
        /// </summary>
        private void ScanForContagion()
        {
            if (_accumulator == null || Context == null) return;

            Transform witnessRoot = Context.CharacterRoot;
            if (witnessRoot == null) return;

            float radius = _effectiveProfile.ContagionRadius;
            float strength = _effectiveProfile.ContagionStrength;
            float maxIntensity = _effectiveProfile.ContagionMaxIntensity;

            string bestLabel = null;
            float bestIntensity = 0f;

            IReadOnlyList<EmotionContagionRegistry.Entry> entries = EmotionContagionRegistry.All;
            for (int i = 0; i < entries.Count; i++)
            {
                EmotionContagionRegistry.Entry entry = entries[i];
                if (entry == null) continue;
                // entry.Controller/Root/Context are all concrete UnityEngine.Object-derived
                // fields, so a plain "== null" already correctly detects a destroyed-but-not-
                // yet-collected object (no interface-typed "fake null" gotcha here).
                if (entry.Controller == null) continue;
                if (ReferenceEquals(entry.Controller, this)) continue; // self-exclusion
                if (entry.Root == null) continue;
                if (entry.Context == null) continue;

                float distance = Vector3.Distance(witnessRoot.position, entry.Root.position);
                if (distance > radius) continue;

                IEmotionStateSource source = entry.Context.EmotionStateSource;
                if (source == null) continue;

                EmotionReading reading = source.Current;
                if (reading.IsNeutral) continue;
                if (reading.DominantScore < ContagionActivationThreshold) continue;

                float falloff = Mathf.Clamp01(1f - distance / radius);
                float candidateIntensity = Mathf.Min(reading.DominantScore * falloff * strength, maxIntensity);

                if (candidateIntensity > bestIntensity)
                {
                    bestIntensity = candidateIntensity;
                    bestLabel = reading.DominantLabel;
                }
            }

            _accumulator.SetContagionTarget(bestLabel, bestIntensity);
        }

        private void TeardownPipeline()
        {
            _semanticExpressionOutput?.Unbind();
            _semanticExpressionOutput = null;
            _expressionPlanner?.Reset();
            _expressionPlanner = null;
            _compiledExpressionModel = null;

            for (int i = 0; i < _activeBindings.Count; i++)
                _activeBindings[i].Unbind(this);

            _activeBindings.Clear();
            TeardownMicroExpressions();
            _accumulator?.Reset();
            _accumulator = null;
            _prosodyGain = 1f;
            _cachedReading = EmotionReading.Neutral;
            _currentDominantLabel = EmotionReading.NeutralLabel;
            _currentDominantScore = 0f;
            _currentMouthInfluence = 0f;
            _currentMoodLabel = EmotionReading.NeutralLabel;
            _currentMoodScore = 0f;
            unchecked { _stateVersion++; }
            _cachedReadingVersion = _stateVersion;
            _frameLabels = Array.Empty<string>();
            _frameScores = Array.Empty<float>();
            _frameDimensions = Array.Empty<EmotionDimensions>();
            _frameMouthInfluence = Array.Empty<float>();
            _dominantFrameIndex = -1;
            _currentFrame = EmotionStateFrame.Neutral;
            _currentScoresSnapshot.Clear();
            _expressivenessGains.Clear();
            _dominantHoldSeconds = 0f;
            _lastDominantLabel = null;
            _lastNotifiedMoodLabel = null;
            _nextContagionScanTime = 0f;
            ResetBlendHysteresisState();
            _blendLabels = null;
            _blendScores = null;
        }

        /// <summary>
        ///     Resets the blending scalar state (primary label tracking, switch
        ///     clock) without discarding the preallocated blend buffers, so it is safe to call from
        ///     both pipeline teardown and a live session reset (reconnect), leaving no stale
        ///     hysteresis behind in either case.
        /// </summary>
        private void ResetBlendHysteresisState()
        {
            _emotionClock = 0f;
            _lastSwitchTime = 0f;
            _primaryScore = 0f;
            _primaryLabel = _effectiveTaxonomy != null ? _effectiveTaxonomy.Neutral.Label : null;
        }

        /// <summary>
        ///     Builds the per-label expressiveness-gain lookup once at build time (not per tick or
        ///     per event) from the authored profile entries.
        /// </summary>
        private void BuildExpressivenessGainLookup()
        {
            _expressivenessGains.Clear();
            IReadOnlyList<Profiles.EmotionExpressivenessEntry> entries = _effectiveProfile.Expressiveness;
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                Profiles.EmotionExpressivenessEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Label)) continue;
                _expressivenessGains[entry.Label] = entry.Gain;
            }
        }

        /// <summary>
        ///     Returns the expressiveness gain for <paramref name="canonicalLabel" /> from the
        ///     pre-built lookup, or <c>1</c> (unchanged) when absent. O(1), no allocation.
        /// </summary>
        private float GainFor(string canonicalLabel)
        {
            if (string.IsNullOrEmpty(canonicalLabel)) return 1f;
            return _expressivenessGains.TryGetValue(canonicalLabel, out float gain) ? gain : 1f;
        }

        private void SubscribeToEventHub()
        {
            IEventHub hub = Context?.EventHub;
            if (ReferenceEquals(_subscribedEventHub, hub)) return;

            UnsubscribeFromEventHub();
            if (hub == null) return;

            _emotionToken = hub.Subscribe<CharacterEmotionChanged>(OnEmotionChanged);
            _speechToken = hub.Subscribe<CharacterSpeechStateChanged>(OnSpeechStateChanged);
            _sessionToken = hub.Subscribe<SessionStateChanged>(OnSessionStateChanged);
            _subscribedEventHub = hub;
        }

        private void UnsubscribeFromEventHub()
        {
            IEventHub hub = _subscribedEventHub;
            if (hub == null) return;

            if (_emotionToken != default) hub.Unsubscribe(_emotionToken);
            if (_speechToken != default) hub.Unsubscribe(_speechToken);
            if (_sessionToken != default) hub.Unsubscribe(_sessionToken);

            _emotionToken = default;
            _speechToken = default;
            _sessionToken = default;
            _subscribedEventHub = null;
        }

        private void OnEmotionChanged(CharacterEmotionChanged evt)
        {
            if (_accumulator == null || _effectiveTaxonomy == null) return;
            if (!MatchesCharacter(evt.CharacterId)) return;
            if (lockEmotion || _emotionOverrideActive) return;
            if (evt.Sequence >= 0 && evt.Sequence <= _lastAcceptedEmotionSequence) return;
            if (evt.Timestamp < _lastAcceptedEmotionTimestamp) return;
            if (evt.Sequence >= 0) _lastAcceptedEmotionSequence = evt.Sequence;
            _lastAcceptedEmotionTimestamp = evt.Timestamp;

            if (!_effectiveTaxonomy.TryResolve(evt.Emotion, out EmotionDescriptor descriptor))
            {
                if (_warnedUnknownLabels.Add(evt.Emotion))
                {
                    Context?.Logger?.Warning(
                        $"[ConvaiEmotionController] The backend sent the emotion '{evt.Emotion}', which this "
                        + "character's emotion vocabulary does not define, so the face stays neutral. Add it "
                        + "to that emotion's other words on the vocabulary asset if it is expected.");
                }
                descriptor = _effectiveTaxonomy.Neutral;
            }

            float normalized = Mathf.Clamp01(
                (evt.NormalizedIntensity + (_effectiveProfile != null ? _effectiveProfile.IntensityOffset : 0f)) *
                Mathf.Lerp(0.35f, 1f, evt.Confidence));

            bool blendingEnabled = _effectiveProfile != null && _effectiveProfile.EnableEmotionBlending;

            if (descriptor.IsNeutral)
            {
                // Neutral/unknown incoming label: clear to 0 exactly as today, blending or not.
                _accumulator.SetTargetEmotion(descriptor.Label, 0f);
                _primaryLabel = descriptor.Label;
                _primaryScore = 0f;
                _lastSwitchTime = _emotionClock;
                return;
            }

            normalized = Mathf.Clamp01(normalized * GainFor(descriptor.Label));

            if (!blendingEnabled)
            {
                // Winner-takes-all path: one non-neutral emotion at a time. The anti-flicker state
                // (primary label/score/clock) is kept coherent — but never CONSULTED here — so
                // toggling the flag on at runtime starts from a sensible dwell baseline instead of
                // a stale one.
                _accumulator.SetTargetEmotion(descriptor.Label, normalized);
                _primaryLabel = descriptor.Label;
                _primaryScore = normalized;
                _lastSwitchTime = _emotionClock;
                return;
            }

            ApplyBlendedEmotionChange(descriptor, normalized);
        }

        /// <summary>
        ///     Blending path: decides via the anti-flicker guards whether <paramref name="descriptor" />
        ///     supplants the current primary emotion, and if accepted, writes the primary plus its
        ///     taxonomy complements into the reused blend buffers via the zero-alloc
        ///     <see cref="EmotionScoreAccumulator.SetTargetEmotions(string[], float[], int)" />
        ///     overload. Rejections are anti-flicker no-ops: the current targets are left untouched.
        /// </summary>
        private void ApplyBlendedEmotionChange(EmotionDescriptor descriptor, float score)
        {
            bool accept =
                _primaryScore <= RestPrimaryThreshold ||
                string.Equals(descriptor.Label, _primaryLabel, StringComparison.OrdinalIgnoreCase) ||
                (_emotionClock - _lastSwitchTime) >= _effectiveProfile.EmotionSwitchDwell ||
                score >= _primaryScore + _effectiveProfile.EmotionSwitchMargin;

            if (!accept) return; // Anti-flicker: ignore the event, keep current targets.

            _primaryLabel = descriptor.Label;
            _primaryScore = score;
            _lastSwitchTime = _emotionClock;

            int count = 0;
            _blendLabels[count] = descriptor.Label;
            _blendScores[count] = score;
            count++;

            int maxContributors = Mathf.Clamp(_effectiveProfile.MaxSimultaneousEmotions, 1, _blendLabels.Length);
            IReadOnlyList<string> complements = descriptor.Complements;
            if (complements != null)
            {
                for (int i = 0; i < complements.Count && count < maxContributors; i++)
                {
                    string complementLabel = complements[i];
                    if (string.IsNullOrWhiteSpace(complementLabel)) continue;
                    if (!_effectiveTaxonomy.TryResolve(complementLabel, out EmotionDescriptor complementDescriptor)) continue;
                    if (complementDescriptor.IsNeutral) continue;
                    if (string.Equals(complementDescriptor.Label, descriptor.Label, StringComparison.OrdinalIgnoreCase)) continue;

                    bool alreadyAdded = false;
                    for (int j = 0; j < count; j++)
                    {
                        if (string.Equals(_blendLabels[j], complementDescriptor.Label, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }
                    if (alreadyAdded) continue;

                    _blendLabels[count] = complementDescriptor.Label;
                    _blendScores[count] = Mathf.Clamp01(score * _effectiveProfile.ComplementBlendScale);
                    count++;
                }
            }

            _accumulator.SetTargetEmotions(_blendLabels, _blendScores, count);
        }

        private void OnSpeechStateChanged(CharacterSpeechStateChanged evt)
        {
            if (!MatchesCharacter(evt.CharacterId)) return;
            _isCharacterSpeaking = evt.IsSpeaking;
        }

        private void OnSessionStateChanged(SessionStateChanged evt)
        {
            if (evt.NewState != SessionState.Disconnected && evt.NewState != SessionState.Error) return;

            _lastAcceptedEmotionTimestamp = DateTime.MinValue;
            _lastAcceptedEmotionSequence = -1;

            // Hysteresis (primary label/score/switch clock) must never survive across a
            // session boundary, even when locked — otherwise the next session's first event
            // is judged against stale state. Locked emotion persistence only applies to the
            // accumulator's target scores, so that reset stays behind the lock guard.
            ResetBlendHysteresisState();

            // Runtime-API mood (SetMood) must never survive a session boundary — snap the
            // accumulator's baseline back to the AUTHORED pair regardless of lockEmotion, since
            // mood/baseline is independent of the locked-emotion target-score override below.
            if (_accumulator != null && _effectiveTaxonomy != null)
            {
                ResolveAuthoredBaseline(out string label, out float intensity);
                _accumulator.SetPersonaBaseline(label, intensity);
            }

            // An in-flight outcome beat must not survive a session boundary either, and for the
            // same reason the mood reset above sits outside the lock guard: the beat is
            // independent of the locked-emotion target-score override.
            _accumulator?.ClearOutcomeBeat();

            if (lockEmotion) return;

            _accumulator?.Reset();
            if (_emotionOverrideActive)
                _accumulator?.SetTargetEmotion(_emotionOverrideLabel, _emotionOverrideScore);
        }

        private bool MatchesCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                WarnUnscopedEmotionEventOnce();
                return false;
            }

            if (_character == null)
            {
                WarnMissingCharacterOnce();
                return false;
            }

            if (string.IsNullOrWhiteSpace(_character.CharacterId)) return false;
            return string.Equals(_character.CharacterId, characterId, StringComparison.OrdinalIgnoreCase);
        }

        private void WarnUnscopedEmotionEventOnce()
        {
            if (_warnedAboutUnscopedEmotionEvent) return;
            _warnedAboutUnscopedEmotionEvent = true;
            Context?.Logger.WithTag(nameof(ConvaiEmotionController))?.Warning(
                "Ignored an emotion event without a character id. " +
                "Character-scoped emotion events must include a character id to avoid cross-character expression bleed.");
        }

        /// <summary>
        ///     Warns once when this controller has no <see cref="ConvaiCharacter" /> ancestor, which
        ///     makes <see cref="MatchesCharacter" /> reject every incoming emotion event. Without
        ///     this the component looks fully configured and simply never reacts, with nothing in
        ///     the console to explain why — the single most confusing way this module can fail.
        /// </summary>
        private void WarnMissingCharacterOnce()
        {
            if (_warnedAboutMissingCharacter) return;
            _warnedAboutMissingCharacter = true;
            Context?.Logger.WithTag(nameof(ConvaiEmotionController))?.Warning(
                $"'{name}' has no ConvaiCharacter on itself or any parent, so it can never match an " +
                "incoming emotion event and will stay neutral forever. Move this component onto the " +
                "Convai character GameObject (or one of its children).");
        }

        private void HandleDependenciesPopulated()
        {
            SubscribeToEventHub();
            Context?.EnsureTickScheduler()?.Register(this);
            RebuildPipeline();
        }

        private void HandleRigBindingChanged(IStandardRigBinding rigBinding)
        {
            if (_effectiveProfile == null || _effectiveTaxonomy == null) return;

            // Mouth provider registration is permanent for the controller's lifetime; the
            // current TryGetMouthWeight result tracks the semantic output directly.
            RebuildOutputBindings();
        }

        private void RebuildPipeline()
        {
            TeardownPipeline();
            BuildPipeline();
        }

        private ConvaiEmotionProfile ResolveProfile()
        {
            _effectiveProfile = EffectiveProfile;
            return _effectiveProfile;
        }

        private void ReleaseSyntheticAssets()
        {
            ReleaseSyntheticTaxonomyIfOwned();
        }

        private void ReleaseSyntheticTaxonomyIfOwned()
        {
            if (!_createdSyntheticTaxonomy || _effectiveTaxonomy == null) return;

            // Destroy() is forbidden in EditMode (e.g. EditMode tests with [ExecuteAlways]).
            if (UnityEngine.Application.isPlaying)
                Destroy(_effectiveTaxonomy);
            else
                DestroyImmediate(_effectiveTaxonomy);

            _effectiveTaxonomy = null;
            _createdSyntheticTaxonomy = false;
        }



        /// <summary>
        ///     Copies the accumulator's per-label output into both the retainable string-keyed
        ///     snapshot (<see cref="EmotionReading.AllScores" />) and the indexed frame buffer the
        ///     expression planner consumes, in one pass.
        /// </summary>
        /// <remarks>
        ///     Deliberately iterates the pre-resolved <see cref="_frameLabels" /> array rather than
        ///     enumerating the accumulator's dictionary. <see cref="EmotionScoreAccumulator.OutputScores" />
        ///     is typed as <see cref="IReadOnlyDictionary{TKey,TValue}" />, so a <c>foreach</c> over
        ///     it boxes <see cref="Dictionary{TKey,TValue}" />'s struct enumerator — one heap
        ///     allocation per tick per character, which the module's zero-steady-state-allocation
        ///     rule forbids on a per-frame path. The label array holds exactly the taxonomy labels
        ///     the accumulator is keyed by, so this is equivalent, and it folds the previously
        ///     separate indexed-score copy into the same loop.
        /// </remarks>
        private void CopyOutputScoresSnapshot()
        {
            IReadOnlyDictionary<string, float> source = _accumulator.OutputScores;
            int count = Mathf.Min(_frameLabels.Length, _frameScores.Length);

            for (int i = 0; i < count; i++)
            {
                string label = _frameLabels[i];
                source.TryGetValue(label, out float score);

                // Same key set every tick, so assigning rather than Clear()-then-add keeps the
                // dictionary's buckets stable and allocation-free after the first frame.
                _currentScoresSnapshot[label] = score;
                _frameScores[i] = score;
            }
        }

        /// <summary>
        ///     Index of <paramref name="label" /> in the pre-resolved frame arrays, or
        ///     <c>-1</c>. A linear walk over a handful of labels, run once per tick for the
        ///     single dominant label - cheaper than the dictionary lookup it replaces, and
        ///     allocation-free.
        /// </summary>
        private int FrameIndexOf(string label)
        {
            if (string.IsNullOrEmpty(label)) return -1;
            for (int i = 0; i < _frameLabels.Length; i++)
                if (string.Equals(_frameLabels[i], label, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private void UpdateBorrowedFrame()
        {
            float valence = 0f;
            float arousal = 0f;
            float agency = 0f;
            float approach = 0f;
            int count = Mathf.Min(_frameLabels.Length, _frameScores.Length);

            for (int i = 0; i < count; i++)
            {
                float score = _frameScores[i];
                if (score <= 0f) continue;

                EmotionDimensions d = _frameDimensions[i];
                valence += d.Valence * score;
                arousal += d.Arousal * score;
                agency += d.Agency * score;
                approach += d.Approach * score;
            }

            var dimensions = new EmotionDimensions(valence, arousal, agency, approach);
            _currentFrame = new EmotionStateFrame(
                _stateVersion,
                _currentDominantLabel,
                _currentDominantScore,
                _frameLabels,
                _frameScores,
                _currentMoodLabel,
                _currentMoodScore,
                dimensions,
                _currentMouthInfluence,
                _dominantHoldSeconds);
        }

        private void UpdateDominantHold(string dominantLabel, float deltaTime)
        {
            if (!string.Equals(dominantLabel, _lastDominantLabel, StringComparison.OrdinalIgnoreCase))
            {
                _lastDominantLabel = dominantLabel;
                _dominantHoldSeconds = 0f;
            }
            else
            {
                _dominantHoldSeconds += deltaTime;
            }
        }

        private float ResolveMouthInfluence(float dominantScore)
        {
            if (_dominantFrameIndex < 0 || _dominantFrameIndex >= _frameMouthInfluence.Length) return 0f;
            return Mathf.Clamp01(_frameMouthInfluence[_dominantFrameIndex] * dominantScore);
        }

        /// <summary>
        ///     Pure selection of the micro-expression bias source — the STRONGER of the
        ///     dominant transient and the current mood (ties favor the dominant transient).
        ///     With <paramref name="moodScore" /> == 0 (mood inactive), the choice is always the
        ///     dominant transient. No allocation.
        /// </summary>
        internal static void SelectMicroEmotionBias(string dominantLabel, float dominantScore,
            string moodLabel, float moodScore, out string label, out float score)
        {
            if (dominantScore >= moodScore)
            {
                label = dominantLabel;
                score = dominantScore;
            }
            else
            {
                label = moodLabel;
                score = moodScore;
            }
        }

        /// <summary>
        ///     Returns <c>true</c> when at least one material-property slot has both a non-empty
        ///     emotion label and a non-empty shader property name authored (a "payload" slot), so
        ///     the binding is worth creating at all.
        /// </summary>
        private static bool HasAnyAuthoredMaterialSlot(IReadOnlyList<MaterialPropertyEmotionSlot> slots)
        {
            if (slots == null) return false;
            for (int i = 0; i < slots.Count; i++)
            {
                MaterialPropertyEmotionSlot slot = slots[i];
                if (slot == null) continue;
                if (string.IsNullOrWhiteSpace(slot.EmotionLabel)) continue;
                if (string.IsNullOrWhiteSpace(slot.PropertyName)) continue;
                return true;
            }
            return false;
        }
    }
}
