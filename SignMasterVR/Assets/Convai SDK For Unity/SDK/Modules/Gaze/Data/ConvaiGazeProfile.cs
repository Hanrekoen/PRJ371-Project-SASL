using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Gaze.Data
{
    /// <summary>How strongly and in which pattern a dialogue state breaks eye contact.</summary>
    public enum GazeAversionMode
    {
        /// <summary>No deliberate aversion — gaze holds contact for the whole state.</summary>
        None = 0,

        /// <summary>
        ///     Cognitive aversion: up/side look-away beats used while the character is
        ///     thinking or recalling (classic Thinking behavior).
        /// </summary>
        Cognitive = 1,

        /// <summary>
        ///     Natural conversational aversion: occasional brief contact breaks that keep a
        ///     long mutual gaze from reading as a stare.
        /// </summary>
        Natural = 2
    }

    /// <summary>
    ///     Aversion-beat direction bias while an authored emotion is dominant (emotional
    ///     gaze signature). Overrides the aversion director's default direction pick for beats
    ///     scheduled while the bias is active; a beat forced by the turn-taking planning break
    ///     is never biased — a planning break is cognitive, not emotional.
    /// </summary>
    public enum GazeAversionBias
    {
        /// <summary>
        ///     No emotional override — beats keep the state's own shape (Cognitive: up/side;
        ///     Natural: mostly down). This is value 0, so a modifier row that never sets a bias
        ///     reads as "leave the direction alone" rather than as a direction of its own.
        /// </summary>
        CognitiveDefault = 0,

        /// <summary>Straight up, little side drift — a distancing "can't look at you" beat.</summary>
        Up = 1,

        /// <summary>Level sideways glance, away from the target.</summary>
        Side = 2,

        /// <summary>Straight down — a withdrawn, low-energy beat (sadness).</summary>
        Down = 3,

        /// <summary>Down and to the side — an averted, self-conscious beat (shame/embarrassment).</summary>
        DownSide = 4
    }

    /// <summary>How the eye stage writes its result to the character.</summary>
    public enum GazeEyeActuationMode
    {
        /// <summary>Prefer eye bones; fall back to EyeLook* blendshapes; else disable eyes.</summary>
        Auto = 0,

        /// <summary>Rotate the LeftEye/RightEye bones only.</summary>
        Bones = 1,

        /// <summary>Drive EyeLook* blendshapes through the facial compositor only.</summary>
        Blendshapes = 2,

        /// <summary>Eye stage disabled (head/torso gaze still runs).</summary>
        Disabled = 3
    }

    /// <summary>Verbosity gate for the gaze diagnostics channel.</summary>
    public enum GazeTraceVerbosity
    {
        /// <summary>No trace output. Warnings and errors are still logged. The shipping default.</summary>
        Off = 0,

        /// <summary>Target changes, policy switches, reorientation lifecycle. The first step up while debugging.</summary>
        State = 1,

        /// <summary>Adds arbiter scores, saccade decisions, limit clamps, aversion beats.</summary>
        Detail = 2,

        /// <summary>Adds throttled per-tick angle/weight dumps. Logged, never recorded.</summary>
        Firehose = 3
    }

    /// <summary>
    ///     Per-<see cref="DialogueState" /> gaze policy: how strongly the character commits
    ///     to its focus target during that conversational beat.
    /// </summary>
    [Serializable]
    public struct GazeStatePolicy
    {
        /// <summary>Dialogue state this entry applies to.</summary>
        [Tooltip("Dialogue state this entry applies to. Unlisted states fall back to the Idle entry.")]
        public DialogueState State;

        /// <summary>Target engagement 0–1.</summary>
        [Range(0f, 1f)]
        [Tooltip("Target engagement 0–1. 0 releases gaze to ambient life; 1 is a full commit to the target.")]
        public float Engagement;

        /// <summary>When disabled, the player anchor is suppressed in this state (ambient life takes over).</summary>
        [Tooltip("When disabled, the player anchor is suppressed in this state (ambient life takes over).")]
        public bool AllowPlayerTarget;

        /// <summary>How much the head/neck participate relative to the eyes (0 = eyes only, 1 = full head commit).</summary>
        [Range(0f, 1f)]
        [Tooltip("How much the head/neck participate relative to the eyes (0 = eyes only, 1 = full head commit).")]
        public float HeadContribution;

        /// <summary>Whether sustained off-axis gaze may trigger a full-body turn in this state.</summary>
        [Tooltip("Whether sustained off-axis gaze may trigger a full-body turn in this state.")]
        public bool AllowBodyTurn;

        /// <summary>Aversion pattern for this state (None keeps unbroken contact).</summary>
        [Tooltip("Aversion pattern for this state (None keeps unbroken contact).")]
        public GazeAversionMode AversionMode;

        /// <summary>Aversion intensity: scales how often and how far contact-break beats go.</summary>
        [Range(0f, 1f)]
        [Tooltip("Aversion intensity: scales how often and how far contact-break beats go.")]
        public float AversionStrength;

        /// <summary>Scale on fixation micro-behavior (micro-saccades, face scanning) for this state.</summary>
        [Range(0f, 2f)]
        [Tooltip("Scale on fixation micro-behavior (micro-saccades, face scanning) for this state.")]
        public float FixationLiveliness;

        /// <summary>
        ///     A synthetic policy that fully commits to the player anchor no matter the
        ///     dialogue state: full engagement, full head/body participation, body turns
        ///     allowed, no deliberate aversion. Used by
        ///     <c>ConvaiGazeController.EyeContactMode</c> to override the authored per-state
        ///     table with a hard "always look at me" guarantee — e.g. for a static kiosk
        ///     character or a demo that never wants the eye contact to break, even in Idle.
        ///     Micro-life (blinks, drift, face-scan) is left at its normal scale so the lock
        ///     still reads as alive, not a frozen stare.
        /// </summary>
        public static GazeStatePolicy LockedToPlayer(DialogueState state) => new()
        {
            State = state,
            Engagement = 1f,
            AllowPlayerTarget = true,
            HeadContribution = 1f,
            AllowBodyTurn = true,
            AversionMode = GazeAversionMode.None,
            AversionStrength = 0f,
            FixationLiveliness = 1f
        };
    }

    /// <summary>Opt-in per-emotion modulation of the gaze behavior.</summary>
    [Serializable]
    public struct EmotionGazeModifier
    {
        /// <summary>Emotion label as reported by the emotion module (case-insensitive).</summary>
        [ConvaiEmotionLabel]
        [Tooltip("The emotion this modifier reacts to.")]
        public string EmotionLabel;

        /// <summary>Multiplier on state engagement while this emotion is dominant.</summary>
        [Range(0f, 1.5f)]
        [Tooltip("Multiplier on state engagement while this emotion is dominant.")]
        public float EngagementScale;

        /// <summary>Multiplier on aversion strength while this emotion is dominant.</summary>
        [Range(0f, 2f)]
        [Tooltip("Multiplier on aversion strength while this emotion is dominant.")]
        public float AversionScale;

        /// <summary>Multiplier on the statistical blink rate while this emotion is dominant.</summary>
        [Range(0.25f, 2f)]
        [Tooltip("Multiplier on the statistical blink rate while this emotion is dominant.")]
        public float BlinkRateScale;

        /// <summary>Eyelid aperture while this emotion is dominant: &lt;1 narrows the lids (a stare), &gt;1 widens them (1 = unchanged).</summary>
        [Range(0.5f, 1.5f)]
        [Tooltip("Eyelid aperture while this emotion is dominant: <1 narrows the lids (a stare), >1 widens them (1 = unchanged).")]
        public float LidApertureScale;

        /// <summary>Aversion-beat direction while this emotion is dominant.</summary>
        [Tooltip("Aversion-beat direction while this emotion is dominant. CognitiveDefault keeps the state's own up/side (Cognitive) or mostly-down (Natural) shape.")]
        public GazeAversionBias AversionBias;

        /// <summary>
        ///     Multiplier on saccade reaction latency and fixation dwell while this emotion is
        ///     dominant: &gt;1 quickens reactions, &lt;1 slows them. A freshly added entry starts at
        ///     0, which is read as "leave the tempo alone".
        /// </summary>
        [Range(0.7f, 1.3f)]
        [Tooltip("Multiplier on saccade reaction latency and fixation dwell while this emotion is dominant: >1 quickens reactions, <1 slows them (0 on a freshly added entry = unmodified).")]
        public float SaccadeTempoScale;

        /// <summary>Multiplier on fixation liveliness (micro-saccades, face scanning) while this emotion is dominant (0 on a freshly added entry = unmodified).</summary>
        [Range(0.25f, 2f)]
        [Tooltip("Multiplier on fixation liveliness (micro-saccades, face scanning) while this emotion is dominant (0 on a freshly added entry = unmodified).")]
        public float FixationLivelinessScale;
    }

    /// <summary>
    ///     The single authoring asset for the Convai Gaze system: targeting, per-state
    ///     policies, head/torso solving, the oculomotor eye model, blinking, body turns,
    ///     idle life, emotion modulation, and diagnostics — one asset per character archetype.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Settings live in nested blocks, one per authoring concern.</b> The asset used to
    ///         be one flat run of 111 fields separated only by <c>[Header]</c> attributes, which
    ///         meant the C# file, the YAML, and any code walking the type all presented the same
    ///         undifferentiated list. The blocks below give that list a structure that survives
    ///         outside the inspector.
    ///     </para>
    ///     <para>
    ///         <b>The public accessors did not move and did not change.</b> Every property below is
    ///         the same name and the same type it has always been, now reading through its block.
    ///         Nesting the public surface would have been 111 breaking API changes in exchange for
    ///         nothing a customer can see.
    ///     </para>
    ///     <para>
    ///         <b>The asset carries its settings once, and nothing else.</b> Grouping changes a
    ///         field's serialized path — <c>playerMaxDistance</c> is now
    ///         <c>targeting.playerMaxDistance</c> — and Unity cannot carry a value across a nesting
    ///         level, so a profile authored against the previous layout reads as defaults. That is a
    ///         one-line note in the release notes rather than a hidden second copy of all 111
    ///         settings kept alive forever. Do not add one back:
    ///         <c>GazeProfileAssetTests.TheProfile_CarriesNoCompatibilityLayer</c> fails the build
    ///         if a hidden mirror of these settings reappears.
    ///     </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ConvaiGazeProfile",
        menuName = "Convai/Embodiment/Gaze Profile",
        order = 120)]
    public sealed class ConvaiGazeProfile : ScriptableObject
    {
        // ── Targeting ────────────────────────────────────────────────────────

        /// <summary>Who or what is worth looking at, and how long the character stays interested.</summary>
        [Serializable]
        private sealed class TargetingSettings
        {
            [SerializeField, Min(0f)]
            [Tooltip("Distance (meters) beyond which the player anchor loses relevance entirely.")]
            internal float playerMaxDistance = 8f;

            [SerializeField, Min(0f)]
            [Tooltip("Distance (meters) below which the player anchor is fully relevant.")]
            internal float playerFullRelevanceDistance = 4f;

            [SerializeField]
            [Tooltip("Auto-created player anchor only: require an unobstructed line of sight to the " +
                     "player (a wall breaks contact, reappearing re-acquires). Off by default.")]
            internal bool playerLineOfSight;

            [SerializeField]
            [Tooltip("Auto-created player anchor only: layers treated as vision obstructions for the line-of-sight test.")]
            internal LayerMask playerObstructionMask = Physics.DefaultRaycastLayers;

            [SerializeField, Min(0.05f)]
            [Tooltip("Target displacement (meters/frame) treated as a camera cut/teleport: gaze re-acquires with a saccade instead of dragging.")]
            internal float targetTeleportThreshold = 1.25f;

            [SerializeField, Min(0.01f)]
            [Tooltip("Seconds for engagement to ramp in after a target is acquired.")]
            internal float commitmentAcquireSeconds = 0.35f;

            [SerializeField, Min(0.01f)]
            [Tooltip("Seconds for engagement to ramp out after a target is lost or released.")]
            internal float commitmentReleaseSeconds = 0.9f;

            [SerializeField, Min(0f)]
            [Tooltip("Seconds the last target point is held after target loss before decaying to ambient.")]
            internal float targetLossHoldSeconds = 0.6f;

            [SerializeField, Min(0f)]
            [Tooltip("Interest drained per second from the currently held target (glance cycling between equal candidates).")]
            internal float interestDecayPerSecond = 0.05f;

            [SerializeField, Min(0f)]
            [Tooltip("Interest restored per second to non-selected candidates.")]
            internal float interestRecoveryPerSecond = 0.1f;

            [SerializeField, Min(1f)]
            [Tooltip("Hard cap (seconds) on continuously holding one candidate when alternatives exist.")]
            internal float maxContinuousHoldSeconds = 14f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Interest level below which the arbiter forces a break to another candidate.")]
            internal float interestBreakThreshold = 0.15f;

            [SerializeField]
            [Tooltip("When the player target is lost mid-conversation (line-of-sight occlusion or range exit) after " +
                     "being continuously engaged for at least 2 seconds, hold the last known point and perform a " +
                     "short burst of searching saccades around it — biased toward the direction the player was last " +
                     "moving — before releasing to the normal decay-to-ambient path. Never applies during Idle.")]
            internal bool enableTargetLossSearch = true;

            [SerializeField, Range(1f, 5f)]
            [Tooltip("Hard cap (seconds) on a target-loss search before it releases to the normal decay path.")]
            internal float targetLossSearchMaxSeconds = 3f;

            // Off by default, which is a judgement about what reads well rather than a safety
            // default. A character that turns to look at whatever its current action step is
            // about sounds right described in a sentence, and in practice it reads as the
            // character being distracted by its own hands: the glance is decided by the step
            // boundary rather than by anything the character noticed, so it lands at moments a
            // person would not have looked. Walking somewhere is unaffected — the character still
            // watches the road and still checks on where it is going, which is what the travel
            // section governs.
            [SerializeField]
            [Tooltip("Glance at whatever the current action step is about, for as long as the step runs. Off by default: it tends to read as the character watching its own hands rather than as intent. Does not affect what a walking character looks at.")]
            internal bool enableLookAtActionTargets;
        }

        [SerializeField]
        private TargetingSettings targeting = new();

        // ── Conversation states ──────────────────────────────────────────────

        /// <summary>How committed the gaze is in each conversational beat.</summary>
        [Serializable]
        private sealed class ConversationStateSettings
        {
            [SerializeField]
            [Tooltip("Per-dialogue-state gaze policy. Unlisted states fall back to the Idle entry.")]
            internal List<GazeStatePolicy> statePolicies = BuildDefaultStatePolicies();

            [SerializeField, Range(0f, 20f)]
            [Tooltip("Exponential smoothing speed applied when the active policy changes.")]
            internal float policyBlendSpeed = 5f;
        }

        [SerializeField]
        private ConversationStateSettings conversationStates = new();

        // ── Head & torso ─────────────────────────────────────────────────────

        /// <summary>How much of the body a look recruits beyond the eyes.</summary>
        [Serializable]
        private sealed class HeadAndTorsoSettings
        {
            [SerializeField, Range(0f, 10f)]
            [Tooltip("Angular dead-band (degrees): target motion inside this band is absorbed by the eyes while the head holds its aim — prevents the head from micro-chasing every small camera move.")]
            internal float headStabilityDegrees = 2.5f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("How much the head cancels the animation's own head movement while the character is looking at something. At 1 the head stays level on the target and the eyes stay centred; lower values let the animation's head bob show through, at the cost of the eyes having to look up or sideways to compensate.")]
            internal float headStabilization = 1f;

            [SerializeField, Range(30f, 720f)]
            [Tooltip("Safety limit on head angular speed in degrees/second. This is a ceiling, not a speed setting — how fast the head actually turns is set by Head Turn Time under Gaze Shift. A correctly tuned character never reaches this.")]
            internal float maxHeadAngularSpeed = 240f;

            [SerializeField, Range(30f, 480f)]
            [Tooltip("Safety limit on chest angular speed in degrees/second. Like the head limit, a ceiling rather than a speed setting.")]
            internal float maxTorsoAngularSpeed = 180f;

            [SerializeField, Range(0f, 60f)]
            [Tooltip("Maximum yaw (degrees) contributed by the neck+head chain.")]
            internal float maxHeadYawDegrees = 55f;

            [SerializeField, Range(0f, 45f)]
            [Tooltip("Maximum pitch (degrees) contributed by the neck+head chain.")]
            internal float maxHeadPitchDegrees = 32f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Share of the head chain rotation carried by the neck bone (rest goes to the head bone).")]
            internal float neckShare = 0.35f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("How much a turn travels up the body instead of the whole head chain rotating as one piece: the neck leads, the head arrives a moment later and settles. At 0 the chain turns rigidly, which is the single most recognisable tell of procedural head motion.")]
            internal float chainFollowThrough = 1f;

            [SerializeField]
            [Tooltip("Recruit chest/upper-chest for gaze amplitudes beyond the head's comfortable range.")]
            internal bool enableTorsoRecruitment = true;

            [SerializeField, Range(0f, 40f)]
            [Tooltip("Maximum yaw (degrees) contributed by chest+upper-chest together.")]
            internal float maxTorsoYawDegrees = 22f;

            [SerializeField, Range(0f, 20f)]
            [Tooltip("Maximum pitch (degrees) contributed by chest+upper-chest together.")]
            internal float maxTorsoPitchDegrees = 6f;

        }

        [SerializeField]
        private HeadAndTorsoSettings headAndTorso = new();

        // ── Gaze shift ladder ────────────────────────────────────────────────

        /// <summary>
        ///     How a look is shared out across the body, and in what order the parts join in.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One settings block for the whole cascade, because the parts are not
        ///         independent: how far the head turns decides whether the chest needs to join,
        ///         which decides whether the feet do. Previously these lived as five unrelated
        ///         fields across two blocks, each read by a different stage of the solver, and
        ///         nothing kept their answers consistent — three of them were waits, and they
        ///         could stack into a visible freeze on arrival.
        ///     </para>
        ///     <para>
        ///         Entry angles are how big a look has to be before that part of the body joins.
        ///         Onsets are how long after the eyes it starts to move — the natural
        ///         eyes-then-head-then-body cascade, measured from one clock so they cannot add up.
        ///     </para>
        /// </remarks>
        [Serializable]
        private sealed class GazeShiftLadderSettings
        {
            [SerializeField, Range(0f, 45f)]
            [Tooltip("How far off-axis a look has to be before the head joins in. Below this the eyes handle it alone.")]
            internal float headEntryDegrees = 12f;

            [SerializeField, Range(0f, 90f)]
            [Tooltip("How far off-axis a look has to be before the chest joins in.")]
            internal float torsoEntryDegrees = 35f;

            [SerializeField, Range(10f, 170f)]
            [Tooltip("How much of a look the head and chest can still not reach before the character turns its feet. Measured on what is left over, not on the raw angle — so a character whose head comfortably covers the look does not turn, and one already at its limit turns sooner.")]
            internal float feetEntryDegrees = 25f;

            [SerializeField, Range(0f, 0.5f)]
            [Tooltip("How long after the eyes the head starts to move. The eyes always arrive first; this is the gap before the head follows, and it is most of what makes a look read as one movement rather than two.")]
            internal float headOnsetSeconds = 0.12f;

            [SerializeField, Range(0f, 0.8f)]
            [Tooltip("How long after the eyes the chest starts to move.")]
            internal float torsoOnsetSeconds = 0.15f;

            [SerializeField, Range(0f, 1.5f)]
            [Tooltip("How long after the eyes the feet may start to turn.")]
            internal float feetOnsetSeconds = 0.25f;

            [SerializeField, Range(0.05f, 0.9f)]
            [Tooltip("How long a small head turn takes, in seconds. This and Added Time Per Degree are the whole speed law: a look of any size takes this plus its size times that. Lower reads alert, higher reads calm.")]
            internal float headTurnBaseSeconds = 0.45f;

            [SerializeField, Range(0f, 0.03f)]
            [Tooltip("Seconds added to a head turn for every degree it covers. At the default a 20° look takes about seven tenths of a second and a 40° look about a second — conversational pace, not the sprint people manage when told to look at something as fast as they can.")]
            internal float headTurnSecondsPerDegree = 0.0125f;

            [SerializeField, Range(0.05f, 1.4f)]
            [Tooltip("How long a small chest turn takes, in seconds. The chest is heavier than the head and moves more deliberately, which is why it has its own timing.")]
            internal float torsoTurnBaseSeconds = 0.55f;

            [SerializeField, Range(0f, 0.05f)]
            [Tooltip("Seconds added to a chest turn for every degree it covers.")]
            internal float torsoTurnSecondsPerDegree = 0.018f;

            [SerializeField, Range(0f, 0.5f)]
            [Tooltip("How front-loaded a movement is. At 0 it speeds up and slows down symmetrically; higher values get going faster and spend longer easing in, which is what real head movements do. Above about 0.3 it starts to read as a flinch.")]
            internal float movementSkew = 0.18f;

            [SerializeField, Range(0.5f, 10f)]
            [Tooltip("How far the aim has to jump before it counts as a new movement rather than the current one being adjusted. Below this the head just follows; above it, the character commits to a fresh look.")]
            internal float shiftTriggerDegrees = 2f;

            [SerializeField, Range(0.5f, 3f)]
            [Tooltip("How much slower idle looking-around is than purposeful looking. Above 1 the character drifts lazily when nothing has its attention, which is the difference between idle and alert.")]
            internal float idleDriftTempoScale = 1.35f;

            [SerializeField, Range(0f, 40f)]
            [Tooltip("How far the eyes can sit from centre before the character starts turning its head further to bring them back. Holding a look out of the corner of the eye is work, and nobody does it for long.")]
            internal float eyeComfortDegrees = 14f;

            [SerializeField, Range(0f, 60f)]
            [Tooltip("How far the head can stay turned before the character wants to turn its feet, even when it can already see what it is looking at. This is why people turn to face someone they are already looking at.")]
            internal float headComfortYawDegrees = 35f;
        }

        [SerializeField]
        private GazeShiftLadderSettings gazeShiftLadder = new();

        // ── Eyes ─────────────────────────────────────────────────────────────

        /// <summary>The oculomotor model: range, pursuit, saccades, micro-life, face scanning.</summary>
        [Serializable]
        private sealed class EyeSettings
        {
            [SerializeField]
            [Tooltip("Eye output backend. Auto prefers bones and falls back to EyeLook* blendshapes.")]
            internal GazeEyeActuationMode eyeActuationMode = GazeEyeActuationMode.Auto;

            [SerializeField, Range(10f, 55f)]
            [Tooltip("Oculomotor range: maximum eye yaw (degrees) from rest.")]
            internal float eyeMaxYawDegrees = 35f;

            [SerializeField, Range(5f, 40f)]
            [Tooltip("Oculomotor range: maximum upward eye pitch (degrees).")]
            internal float eyeMaxPitchUpDegrees = 22f;

            [SerializeField, Range(5f, 45f)]
            [Tooltip("Oculomotor range: maximum downward eye pitch (degrees).")]
            internal float eyeMaxPitchDownDegrees = 28f;

            [SerializeField, Range(0.5f, 1f)]
            [Tooltip("Fraction of the oculomotor range where soft-limit compression begins.")]
            internal float eyeSoftLimitFraction = 0.8f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("How strongly the eyes re-center in the orbit as the head catches up (0 = stay off-center).")]
            internal float orbitRecenteringStrength = 0.6f;

            [SerializeField, Range(5f, 90f)]
            [Tooltip("Eye tracking sharpness during smooth pursuit (higher = tighter tracking).")]
            internal float eyeTrackingSharpness = 40f;

            [SerializeField, Range(0.01f, 0.08f)]
            [Tooltip("Minimum saccade duration (seconds) — the main-sequence intercept.")]
            internal float saccadeMinDurationSeconds = 0.03f;

            [SerializeField, Range(0.0005f, 0.01f)]
            [Tooltip("Added saccade duration per degree of amplitude (seconds/degree) — the main-sequence slope.")]
            internal float saccadeDurationPerDegree = 0.0022f;

            [SerializeField, Range(0.1f, 5f)]
            [Tooltip("Gaze error (degrees) below which no corrective saccade is issued.")]
            internal float saccadeDeadzoneDegrees = 0.75f;

            [SerializeField, Range(0f, 0.4f)]
            [Tooltip("Saccadic reaction latency (seconds): the pause before the eyes launch toward a new or displaced target. Real eyes take ~0.1–0.25 s to respond; 0 jumps instantly.")]
            internal float saccadeReactionSeconds = 0.12f;

            [SerializeField, Range(1f, 20f)]
            [Tooltip("Pursuit error (degrees) above which a catch-up saccade fires.")]
            internal float catchUpErrorDegrees = 5f;

            [SerializeField, Range(0f, 0.25f)]
            [Tooltip("Predictive pursuit lead (seconds): during smooth pursuit the eyes aim slightly " +
                     "ahead of a moving target along its measured velocity, cancelling the constant " +
                     "trailing error that would otherwise be closed by catch-up saccades. 0 disables " +
                     "prediction entirely (no velocity-tracking cost). Saccades stay ballistic to the " +
                     "true point; static targets are unaffected. Best kept near 1/eyeTrackingSharpness " +
                     "(≈0.025 s at the default) and below 2/eyeTrackingSharpness, past which the eyes " +
                     "over-lead and track worse.")]
            internal float pursuitLeadSeconds = 0.04f;

            [SerializeField]
            [Tooltip("Converge the eyes on near targets (per-eye yaw differs — VR lean-ins).")]
            internal bool enableVergence = true;

            [SerializeField, Range(0.05f, 1f)]
            [Tooltip("Closest supported convergence distance (meters); nearer targets clamp here.")]
            internal float vergenceMinDistance = 0.14f;

            [SerializeField, Range(2f, 30f)]
            [Tooltip("Maximum inward convergence angle per eye (degrees) — the cross-eye clamp.")]
            internal float maxConvergenceDegrees = 16f;

            [SerializeField, Range(0.05f, 0.08f)]
            [Tooltip("Interpupillary distance (meters) used only when the rig has NO eye bones: sets " +
                     "how far apart the synthesized eyes sit so the blendshape backend still converges.")]
            internal float syntheticInterpupillaryDistance = 0.063f;

            [SerializeField, Range(0f, 2f)]
            [Tooltip("Fixation drift amplitude (degrees) — slow wander while fixating.")]
            internal float fixationDriftDegrees = 0.35f;

            [SerializeField, Range(0.05f, 3f)]
            [Tooltip("Fixation drift frequency (Hz).")]
            internal float fixationDriftFrequency = 0.5f;

            [SerializeField, Range(0.2f, 6f)]
            [Tooltip("Mean seconds between fixation micro-saccades.")]
            internal float microSaccadeIntervalMean = 1.5f;

            [SerializeField, Range(0f, 4f)]
            [Tooltip("Uniform jitter (± seconds) applied to the micro-saccade interval.")]
            internal float microSaccadeIntervalJitter = 0.9f;

            [SerializeField, Range(0f, 2f)]
            [Tooltip("Micro-saccade amplitude (degrees).")]
            internal float microSaccadeAmplitudeDegrees = 0.5f;

            [SerializeField]
            [Tooltip("Scan between implied face landmarks (eyes/mouth) when gazing at the player or a face.")]
            internal bool enableFaceScan = true;

            [SerializeField, Range(0.5f, 6f)]
            [Tooltip("Mean seconds between face-scan fixation shifts.")]
            internal float faceScanIntervalMean = 2.1f;

            [SerializeField, Range(0f, 4f)]
            [Tooltip("Uniform jitter (± seconds) applied to the face-scan interval.")]
            internal float faceScanIntervalJitter = 1.2f;

            [SerializeField, Range(0.5f, 6f)]
            [Tooltip("Angular radius (degrees) of the implied face-landmark triangle.")]
            internal float faceScanRadiusDegrees = 2.2f;

            [SerializeField]
            [Tooltip("While the player is speaking, bias face-scan fixations toward the mouth landmark (speech comprehension behavior). Blends smoothly in/out over ~0.5s.")]
            internal bool enableListenerMouthBias = true;

            [SerializeField, Range(1f, 4f)]
            [Tooltip("Multiplier on the mouth landmark's selection weight at full listener mouth-bias (player speaking).")]
            internal float listenerMouthBiasStrength = 2f;
        }

        [SerializeField]
        private EyeSettings eyes = new();

        // ── Blink & lids ─────────────────────────────────────────────────────

        /// <summary>Statistical blinking and eyelid behavior.</summary>
        [Serializable]
        private sealed class BlinkSettings
        {
            [SerializeField]
            [Tooltip("Statistical blinking through the facial compositor.")]
            internal bool enableBlink = true;

            [SerializeField, Range(1f, 12f)]
            [Tooltip("Mean seconds between spontaneous blinks.")]
            internal float blinkIntervalMean = 4.2f;

            [SerializeField, Range(0f, 8f)]
            [Tooltip("Uniform jitter (± seconds) applied to the blink interval.")]
            internal float blinkIntervalJitter = 2.2f;

            [SerializeField, Range(0.02f, 0.2f)]
            [Tooltip("Lid close time (seconds).")]
            internal float blinkCloseSeconds = 0.07f;

            [SerializeField, Range(0.04f, 0.4f)]
            [Tooltip("Lid open time (seconds).")]
            internal float blinkOpenSeconds = 0.16f;

            [SerializeField, Range(0.1f, 2f)]
            [Tooltip("Refractory window (seconds) during which no new blink can start.")]
            internal float blinkRefractorySeconds = 0.6f;

            [SerializeField, Range(0f, 90f)]
            [Tooltip("Gaze shift amplitude (degrees) above which a blink may accompany the shift (0 disables).")]
            internal float gazeShiftBlinkThresholdDegrees = 18f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Probability that a large gaze shift triggers a blink.")]
            internal float gazeShiftBlinkProbability = 0.55f;

            [SerializeField]
            [Tooltip("Eyelids follow vertical eye rotation (look down lowers the lids).")]
            internal bool enableEyelidFollow = true;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Strength of the eyelid pitch-follow.")]
            internal float eyelidFollowStrength = 0.6f;

            [SerializeField]
            [Tooltip("Elevate blink likelihood for a short window after a cognitive boundary — the " +
                     "end of an utterance, a final transcript, or the player pausing — so blinks read " +
                     "as beats rather than random ticks. Never guarantees a blink, only makes one more " +
                     "likely.")]
            internal bool enableBlinkClustering = true;

            [SerializeField, Range(1f, 6f)]
            [Tooltip("Blink-rate multiplier applied for ~0.7 s after a clustering cue.")]
            internal float blinkClusterRateMultiplier = 3f;
        }

        [SerializeField]
        private BlinkSettings blinkAndLids = new();

        // ── Body turn ────────────────────────────────────────────────────────

        /// <summary>When a look becomes a full-body reorientation.</summary>
        [Serializable]
        private sealed class BodyTurnSettings
        {
            [SerializeField]
            [Tooltip("Allow full-body reorientation toward the gaze target (state policy still gates it).")]
            internal bool enableBodyTurn = true;

            [SerializeField, Range(1f, 30f)]
            [Tooltip("Yaw error (degrees) below which the turn is considered complete.")]
            internal float bodyTurnCompletionToleranceDegrees = 8f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("While a body turn is in flight, head/torso gaze offsets are scaled to this fraction: the neck visibly relaxes and rides the turn instead of staying pinned at its limit (the eyes stay on target).")]
            internal float bodyTurnHeadRelief = 0.4f;

            [SerializeField, Range(45f, 540f)]
            [Tooltip("Peak speed (degrees/second) of the procedural fallback turn used when no animated handler is available. A person turning to face someone in conversation peaks well below this ceiling; the setting is what the turn is allowed to reach at its fastest moment, not its average.")]
            internal float proceduralTurnSpeed = 140f;
        }

        [SerializeField]
        private BodyTurnSettings bodyTurn = new();

        // ── Idle life ────────────────────────────────────────────────────────

        /// <summary>What the character does with its eyes when nothing has its attention.</summary>
        [Serializable]
        private sealed class IdleLifeSettings
        {
            [SerializeField]
            [Tooltip("Ambient eye/head exploration while no target is engaged.")]
            internal bool enableAmbientExploration = true;

            [SerializeField, Range(0f, 60f)]
            [Tooltip("Ambient exploration yaw range (± degrees).")]
            internal float ambientYawRangeDegrees = 26f;

            [SerializeField, Range(0f, 30f)]
            [Tooltip("Ambient exploration upward pitch range (degrees).")]
            internal float ambientPitchUpDegrees = 8f;

            [SerializeField, Range(0f, 30f)]
            [Tooltip("Ambient exploration downward pitch range (degrees).")]
            internal float ambientPitchDownDegrees = 12f;

            [SerializeField, Range(0.4f, 10f)]
            [Tooltip("Minimum seconds between ambient fixation changes.")]
            internal float ambientIntervalMin = 1.7f;

            [SerializeField, Range(1f, 20f)]
            [Tooltip("Maximum seconds between ambient fixation changes.")]
            internal float ambientIntervalMax = 4.6f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Fraction of the ambient look carried by the head (rest is eyes only).")]
            internal float ambientHeadFollow = 0.35f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Bias toward re-centering instead of picking a new off-center point.")]
            internal float ambientRecenterBias = 0.35f;

            [SerializeField]
            [Tooltip("Occasional short eye-led glances at the player while otherwise idle.")]
            // On by default: an idle character glancing at the player occasionally is the cheapest
            // "it's alive" cue the module has, and it is bounded by the interval fields below. It
            // shipped off, which meant nobody ever saw it.
            internal bool enableCuriosityGlances = true;

            [SerializeField, Range(2f, 30f)]
            [Tooltip("Minimum seconds between curiosity glances.")]
            internal float curiosityGlanceIntervalMin = 7f;

            [SerializeField, Range(4f, 60f)]
            [Tooltip("Maximum seconds between curiosity glances.")]
            internal float curiosityGlanceIntervalMax = 16f;

            [SerializeField, Range(0.3f, 4f)]
            [Tooltip("Duration (seconds) of a curiosity glance.")]
            internal float curiosityGlanceDuration = 1.2f;

            [SerializeField]
            [Tooltip("While idle, glance back at the player sooner when a Player Attention Sensor reports " +
                     "the player is looking at this character (needs curiosity glances enabled and a " +
                     "PlayerAttentionSensor on the character). Off by default.")]
            internal bool curiosityRespondsToAttention;
        }

        [SerializeField]
        private IdleLifeSettings idleLife = new();

        // ── Travel ───────────────────────────────────────────────────────────

        /// <summary>Where the character looks while it is walking somewhere.</summary>
        [Serializable]
        private sealed class TravelSettings
        {
            [SerializeField]
            [Tooltip("While walking, watch the path ahead and check on the destination now and then, " +
                     "instead of either staring at it the whole way or ignoring it.")]
            // On by default. Shipping this off would mean shipping the broken behavior as the
            // default and calling the fix an opt-in — a character that stares at a doorway for the
            // eight seconds it takes to walk there is a defect, not a preference.
            internal bool enableTravelGaze = true;

            // Off by default, and separate from the setting above on purpose: watching the road
            // and glancing at the destination are two behaviours, and only one of them reads
            // well. The glance is periodic rather than motivated — its timing comes from a
            // countdown, not from anything the character noticed — so it lands as an unexplained
            // look away from the road, and it is at its most conspicuous near arrival, where the
            // destination is close enough that the look is a large one. Watching the path ahead
            // is unaffected and stays on.
            [SerializeField]
            [Tooltip("While walking, glance at the destination (or whoever it is following) every few seconds. Off by default: the timing is a countdown rather than something the character noticed, so it reads as an unexplained look away from the road. Watching the path ahead is a separate setting and stays on.")]
            internal bool enableDestinationGlances;

            [SerializeField, Min(1)]
            [Tooltip("How strongly the path competes with other things to look at. Above the player " +
                     "anchor's 10 on purpose: below it, a character following the player would walk " +
                     "the whole way staring at them.")]
            internal int travelPathPriority = 15;

            [SerializeField, Range(0.5f, 20f)]
            [Tooltip("How far ahead the character looks at walking pace, in metres.")]
            internal float pathLookAheadMinMeters = 3f;

            [SerializeField, Range(1f, 40f)]
            [Tooltip("How far ahead the character looks at full pace, in metres. Faster travel looks " +
                     "further down the road.")]
            internal float pathLookAheadMaxMeters = 8f;

            [SerializeField, Range(0.05f, 2f)]
            [Tooltip("How long the switch to travel gaze takes, in seconds. This is a fade, not a snap.")]
            internal float travelEngageSeconds = 0.35f;

            [SerializeField, Range(0.5f, 20f)]
            [Tooltip("Minimum seconds between glances at the place being walked to.")]
            internal float travelGlanceIntervalMin = 2.5f;

            [SerializeField, Range(1f, 30f)]
            [Tooltip("Maximum seconds between glances at the place being walked to.")]
            internal float travelGlanceIntervalMax = 5f;

            [SerializeField, Range(0.3f, 20f)]
            [Tooltip("Minimum seconds between glances at a person being followed. Shorter than for a " +
                     "place — walking with someone and never looking at them reads as escort duty.")]
            internal float companionGlanceIntervalMin = 1.6f;

            [SerializeField, Range(0.5f, 30f)]
            [Tooltip("Maximum seconds between glances at a person being followed.")]
            internal float companionGlanceIntervalMax = 3.2f;

            [SerializeField, Range(0.15f, 3f)]
            [Tooltip("How long each glance lasts, in seconds. Long enough to read as a look, short " +
                     "enough not to read as a stare.")]
            internal float travelGlanceHoldSeconds = 0.55f;

            [SerializeField, Range(0.05f, 1f)]
            [Tooltip("How much sooner the glances come while the character is talking with someone. " +
                     "0.5 means twice as often.")]
            internal float travelGlanceConversationScale = 0.5f;

            [SerializeField, Range(0f, 12f)]
            [Tooltip("How far the eyes drop for a moment as the character comes to rest at the " +
                     "end of a walk, before lifting back. Eyes only — the head never follows " +
                     "this. Zero switches it off.")]
            internal float arrivalSettleEyeDropDegrees = 4f;

            [SerializeField, Range(0f, 2f)]
            [Tooltip("How long the settle takes, from the eyes dropping to their coming back up.")]
            internal float arrivalSettleSeconds = 0.7f;

            [SerializeField, Range(0.5f, 15f)]
            [Tooltip("How close to the destination the character starts settling, in metres. Inside " +
                     "this the glances come closer together and the road starts to matter less.")]
            internal float arrivalApproachMeters = 3f;

            [SerializeField, Range(0.1f, 10f)]
            [Tooltip("How close to the destination the character stops watching the road entirely, " +
                     "in metres, and simply looks at what it came for.")]
            internal float arrivalReleaseMeters = 1.2f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("How much the head follows the road compared with how much it would follow a " +
                     "face. Walking gaze is more eyes-led than conversation.")]
            internal float travelHeadContributionScale = 0.8f;
        }

        [SerializeField]
        private TravelSettings travel = new();

        // ── Conversational gestures ──────────────────────────────────────────

        /// <summary>Nods and the interruption startle.</summary>
        [Serializable]
        private sealed class GestureSettings
        {
            [SerializeField]
            [Tooltip("Small acknowledgment nods while the character is listening (subtle, on by default). Never nods while it speaks.")]
            internal bool enableListeningNods = true;

            [SerializeField, Range(1f, 10f)]
            [Tooltip("Peak downward pitch (degrees) of a listening nod.")]
            internal float nodPitchDegrees = 4f;

            [SerializeField, Range(0.3f, 2f)]
            [Tooltip("Duration (seconds) of one nod's double-bob envelope.")]
            internal float nodDurationSeconds = 0.7f;

            [SerializeField, Range(1f, 20f)]
            [Tooltip("Minimum seconds between listening nods.")]
            internal float listeningNodIntervalMin = 3.5f;

            [SerializeField, Range(2f, 30f)]
            [Tooltip("Maximum seconds between listening nods.")]
            internal float listeningNodIntervalMax = 8f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Probability of a nod right when Listening begins (\"I'm with you\").")]
            internal float acknowledgeNodProbability = 0.7f;

            [SerializeField]
            [Tooltip("Plays a one-shot ~1 s startle micro-reaction (re-acquisition saccade, blink, " +
                     "and small head tilt) when the character is interrupted mid-sentence (Speaking → " +
                     "Interrupted). Non-repeating until the character speaks again.")]
            internal bool enableInterruptionReaction = true;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Magnitude of the interruption startle reaction (mainly the head tilt, ~2–4°).")]
            internal float interruptionReactionIntensity = 0.7f;
        }

        [SerializeField]
        private GestureSettings conversationalGestures = new();

        // ── Conversation rhythm ──────────────────────────────────────────────

        /// <summary>Turn-taking choreography while the character holds the floor.</summary>
        [Serializable]
        private sealed class RhythmSettings
        {
            [SerializeField]
            [Tooltip("Turn-taking gaze choreography during Speaking (Natural eye-contact mode): direct " +
                     "contact for short/reactive replies, sparse bounded breaks for genuinely planned or " +
                     "extended answers, and a floor-yield cue after speech ends.")]
            internal bool enableTurnTakingGaze = true;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Propensity for eligible planning breaks. Short/reactive replies remain break-free; " +
                     "extended answers are capped and separated by long contact recovery.")]
            internal float planningBreakProbability = 0.7f;

            [SerializeField]
            [Tooltip("Plays a deliberate blink as part of the floor-yield cue near the end of an utterance.")]
            internal bool enableYieldBlink = true;

            [SerializeField]
            [Tooltip("Adds a small downward head dip to the floor-yield cue after speech ends. Disabled by default to avoid an acknowledgement-like nod.")]
            internal bool enableYieldHeadDip;
        }

        [SerializeField]
        private RhythmSettings conversationRhythm = new();

        // ── Emotion modulation ───────────────────────────────────────────────

        /// <summary>How the dominant emotion colours the gaze.</summary>
        [Serializable]
        private sealed class EmotionModulationSettings
        {
            [SerializeField]
            [Tooltip("Scale gaze behavior by the dominant emotion (opt-in).")]
            // On by default: with no Emotion module present the modulator reads a neutral emotion and
            // produces unit scales, so this costs nothing on a character that has no emotions — and on
            // one that does, it is the only thing that makes an angry character's gaze read as angry.
            internal bool enableEmotionModulation = true;

            [SerializeField]
            [Tooltip("Per-emotion modifiers applied while that emotion is dominant.")]
            internal List<EmotionGazeModifier> emotionModifiers = BuildDefaultEmotionModifiers();
        }

        [SerializeField]
        private EmotionModulationSettings emotionModulation = new();

        // ── Proxemics ────────────────────────────────────────────────────────

        /// <summary>How eye contact softens as the player closes the distance.</summary>
        [Serializable]
        private sealed class ProxemicSettings
        {
            [SerializeField]
            [Tooltip("Soften eye contact instead of holding a fixed stare as the player leans in " +
                     "close (VR) — the aversion floor rises, face-scan radius widens, and blink rate " +
                     "quickens the closer they get, and backs off smoothly as they step back. Bypassed " +
                     "entirely while an eye-contact lock (Conversation Lock, Always Lock) is in force — " +
                     "a kiosk keeps staring.")]
            internal bool enableProxemicRegulation = true;

            [SerializeField, Range(0.2f, 1.5f)]
            [Tooltip("Distance (meters) at which the player starts to read as 'close' — proxemic softening ramps in below this distance.")]
            internal float proxemicCloseDistanceMeters = 0.6f;

            [SerializeField, Range(0f, 1f)]
            [Tooltip("Overall strength of the proxemic softening effect (0 disables the effect while leaving the toggle on, 1 = full authored effect).")]
            internal float proxemicIntensity = 1f;
        }

        [SerializeField]
        private ProxemicSettings proxemics = new();

        // ── Performance ──────────────────────────────────────────────────────

        /// <summary>Level-of-detail governors for crowded scenes.</summary>
        [Serializable]
        private sealed class PerformanceSettings
        {
            [SerializeField]
            [Tooltip("Level-of-detail governor for crowds: far characters think less often and " +
                     "off-screen characters skip the solver stage entirely. Off by default (opt-in).")]
            internal bool enableGazeLod;

            [SerializeField, Range(2f, 60f)]
            [Tooltip("Distance (meters) beyond which the cognition tick drops to the far rate (with 1 m hysteresis).")]
            internal float lodFarDistance = 12f;

            [SerializeField, Range(1f, 30f)]
            [Tooltip("Cognition rate (Hz) for characters beyond the far distance.")]
            internal float lodFarCognitionHz = 10f;

            [SerializeField]
            [Tooltip("Skip the LateUpdate solver stage while none of the character's renderers is visible to a camera.")]
            internal bool skipWhenInvisible = true;
        }

        [SerializeField]
        private PerformanceSettings performance = new();

        // ── Diagnostics ──────────────────────────────────────────────────────

        /// <summary>What this character's gaze writes to the console.</summary>
        [Serializable]
        private sealed class DiagnosticsSettings
        {
            [SerializeField]
            [Tooltip("Diagnostics verbosity for this character's gaze trace. Off is the shipping " +
                     "default — warnings and errors are always logged regardless. Raise to State " +
                     "while debugging to see target changes, policy switches and body turns.")]
            // Off, not State. State routes every target transition through ConvaiLogger.Info, and
            // the SDK's own default global log level is Info — so shipping State meant a fresh
            // install printed a multi-line init block per character plus a console line every time
            // a curiosity glance fired (every 7-16 s, on by default). Diagnostics a user did not
            // ask for are noise; the ones they cannot ignore (rig unusable, no eye backend) bypass
            // this gate entirely and still reach the console.
            internal GazeTraceVerbosity traceVerbosity = GazeTraceVerbosity.Off;

            [SerializeField, Range(1f, 60f)]
            [Tooltip("Maximum firehose dump rate (Hz).")]
            internal float firehoseHz = 10f;
        }

        [SerializeField]
        private DiagnosticsSettings diagnostics = new();

        // ── Public accessors ─────────────────────────────────────────────────
        //
        // Unchanged in name, type and meaning — only the storage behind them moved. Anything a
        // customer wrote against this asset still compiles and still returns the same value.

        /// <summary>Distance (meters) beyond which the player anchor loses relevance entirely.</summary>
        public float PlayerMaxDistance => targeting.playerMaxDistance;
        /// <summary>Distance (meters) below which the player anchor is fully relevant.</summary>
        public float PlayerFullRelevanceDistance => targeting.playerFullRelevanceDistance;
        /// <summary>Auto-created player anchor only: require an unobstructed line of sight to the player (a wall breaks contact, reappearing re-acquires).</summary>
        public bool PlayerLineOfSight => targeting.playerLineOfSight;
        /// <summary>Auto-created player anchor only: layers treated as vision obstructions for the line-of-sight test.</summary>
        public int PlayerObstructionMask => targeting.playerObstructionMask;
        /// <summary>Target displacement (meters/frame) treated as a camera cut/teleport: gaze re-acquires with a saccade instead of dragging.</summary>
        public float TargetTeleportThreshold => targeting.targetTeleportThreshold;
        /// <summary>Seconds for engagement to ramp in after a target is acquired.</summary>
        public float CommitmentAcquireSeconds => targeting.commitmentAcquireSeconds;
        /// <summary>Seconds for engagement to ramp out after a target is lost or released.</summary>
        public float CommitmentReleaseSeconds => targeting.commitmentReleaseSeconds;
        /// <summary>Seconds the last target point is held after target loss before decaying to ambient.</summary>
        public float TargetLossHoldSeconds => targeting.targetLossHoldSeconds;
        /// <summary>Interest drained per second from the currently held target (glance cycling between equal candidates).</summary>
        public float InterestDecayPerSecond => targeting.interestDecayPerSecond;
        /// <summary>Interest restored per second to non-selected candidates.</summary>
        public float InterestRecoveryPerSecond => targeting.interestRecoveryPerSecond;
        /// <summary>Hard cap (seconds) on continuously holding one candidate when alternatives exist.</summary>
        public float MaxContinuousHoldSeconds => targeting.maxContinuousHoldSeconds;
        /// <summary>Interest level below which the arbiter forces a break to another candidate.</summary>
        public float InterestBreakThreshold => targeting.interestBreakThreshold;
        /// <summary>
        ///     When the player target is lost mid-conversation — line-of-sight occlusion or range
        ///     exit — after at least 2 seconds of continuous engagement, hold the last known point
        ///     and play a short burst of searching saccades around it before releasing to the normal
        ///     decay-to-ambient path. Never applies during Idle.
        /// </summary>
        public bool EnableTargetLossSearch => targeting.enableTargetLossSearch;
        /// <summary>Hard cap (seconds) on a target-loss search before it releases to the normal decay path.</summary>
        public float TargetLossSearchMaxSeconds => targeting.targetLossSearchMaxSeconds;
        /// <summary>
        ///     Whether a targeted action step's subject becomes something the character looks at
        ///     while the step runs. Off by default — see the field's remarks. A walking character
        ///     is unaffected either way: watching the road and checking on the destination are
        ///     governed by the travel settings.
        /// </summary>
        public bool EnableLookAtActionTargets => targeting.enableLookAtActionTargets;

        /// <summary>Per-dialogue-state gaze policy.</summary>
        public IReadOnlyList<GazeStatePolicy> StatePolicies => conversationStates.statePolicies;
        /// <summary>Exponential smoothing speed applied when the active policy changes.</summary>
        public float PolicyBlendSpeed => conversationStates.policyBlendSpeed;

        /// <summary>
        ///     Angular dead-band (degrees): target motion inside this band is absorbed by the eyes
        ///     while the head holds its aim, which stops the head micro-chasing every small camera move.
        /// </summary>
        public float HeadStabilityDegrees => headAndTorso.headStabilityDegrees;
        /// <summary>
        ///     How much of the animation's own head deviation the head chain cancels while
        ///     engaged, 0–1. This is a stabilization reflex, not part of the voluntary gaze
        ///     shift: at 1 the head holds level on the target and the eyes stay centred in
        ///     their orbits; below 1 the animated head bow shows through and the eyes must
        ///     counter-rotate to keep contact. Scaled by engagement, so an idle character
        ///     keeps its animated head personality either way.
        /// </summary>
        public float HeadStabilization => headAndTorso.headStabilization;
        /// <summary>
        ///     Safety ceiling on head angular speed in degrees/second. Not a speed setting: how
        ///     fast the head turns comes from <see cref="HeadTurnBaseSeconds" /> and
        ///     <see cref="HeadTurnSecondsPerDegree" />, and a correctly tuned character never
        ///     reaches this.
        /// </summary>
        public float MaxHeadAngularSpeed => headAndTorso.maxHeadAngularSpeed;
        /// <summary>Safety ceiling on chest angular speed in degrees/second.</summary>
        public float MaxTorsoAngularSpeed => headAndTorso.maxTorsoAngularSpeed;
        /// <summary>Maximum yaw (degrees) contributed by the neck+head chain.</summary>
        public float MaxHeadYawDegrees => headAndTorso.maxHeadYawDegrees;
        /// <summary>Maximum pitch (degrees) contributed by the neck+head chain.</summary>
        public float MaxHeadPitchDegrees => headAndTorso.maxHeadPitchDegrees;
        /// <summary>Share of the head chain rotation carried by the neck bone (rest goes to the head bone).</summary>
        public float NeckShare => headAndTorso.neckShare;
        /// <summary>
        ///     0–1 amount of overlapping action in the head chain: how much the neck leads a turn
        ///     and the head trails it, then settles. 0 rotates the chain rigidly.
        /// </summary>
        public float ChainFollowThrough => headAndTorso.chainFollowThrough;
        /// <summary>Recruit chest/upper-chest for gaze amplitudes beyond the head's comfortable range.</summary>
        public bool EnableTorsoRecruitment => headAndTorso.enableTorsoRecruitment;

        /// <summary>How far off-axis a look must be before the head joins the eyes.</summary>
        public float HeadEntryDegrees => gazeShiftLadder.headEntryDegrees;
        /// <summary>How far off-axis a look must be before the chest joins in.</summary>
        public float TorsoEntryDegrees => gazeShiftLadder.torsoEntryDegrees;
        /// <summary>
        ///     How much of a look may remain unmet by the head and chest before the feet turn.
        ///     Measured on the residual, not the raw error, so the decision reflects whether the
        ///     neck is actually out of room rather than tripping at a fixed angle.
        /// </summary>
        public float FeetEntryDegrees => gazeShiftLadder.feetEntryDegrees;
        /// <summary>Seconds after the eyes at which the head begins to move.</summary>
        public float HeadOnsetSeconds => gazeShiftLadder.headOnsetSeconds;
        /// <summary>Seconds after the eyes at which the chest begins to move.</summary>
        public float TorsoOnsetSeconds => gazeShiftLadder.torsoOnsetSeconds;
        /// <summary>Seconds after the eyes at which the feet may begin to turn.</summary>
        public float FeetOnsetSeconds => gazeShiftLadder.feetOnsetSeconds;
        /// <summary>
        ///     Seconds a small head turn takes. With <see cref="HeadTurnSecondsPerDegree" /> this
        ///     is the head's whole duration law — the movement's shape comes from minimum jerk,
        ///     and this decides how long that shape is stretched over.
        /// </summary>
        public float HeadTurnBaseSeconds => gazeShiftLadder.headTurnBaseSeconds;
        /// <summary>Seconds added to a head turn per degree of amplitude.</summary>
        public float HeadTurnSecondsPerDegree => gazeShiftLadder.headTurnSecondsPerDegree;
        /// <summary>Seconds a small chest turn takes.</summary>
        public float TorsoTurnBaseSeconds => gazeShiftLadder.torsoTurnBaseSeconds;
        /// <summary>Seconds added to a chest turn per degree of amplitude.</summary>
        public float TorsoTurnSecondsPerDegree => gazeShiftLadder.torsoTurnSecondsPerDegree;
        /// <summary>
        ///     0–1 asymmetry of the movement's velocity profile. 0 is symmetric; the default puts
        ///     the peak at roughly 42 % of the movement, which is where real head movements peak.
        /// </summary>
        public float MovementSkew => gazeShiftLadder.movementSkew;
        /// <summary>
        ///     How far the allocated aim must jump between frames before it is treated as a new
        ///     movement rather than an adjustment to the one in flight.
        /// </summary>
        public float ShiftTriggerDegrees => gazeShiftLadder.shiftTriggerDegrees;
        /// <summary>
        ///     Multiplier on movement duration while the character is looking around idly rather
        ///     than at something. Above 1 makes idle life slower than purposeful looking.
        /// </summary>
        public float IdleDriftTempoScale => gazeShiftLadder.idleDriftTempoScale;
        /// <summary>
        ///     Eye eccentricity (degrees) beyond which sustained deviation starts recruiting
        ///     more head, so the eyes return toward centre instead of holding at the corner.
        /// </summary>
        public float EyeComfortDegrees => gazeShiftLadder.eyeComfortDegrees;
        /// <summary>
        ///     Head yaw (degrees) beyond which a sustained turn asks for the feet, even when
        ///     nothing about the look is still unmet.
        /// </summary>
        public float HeadComfortYawDegrees => gazeShiftLadder.headComfortYawDegrees;
        /// <summary>Maximum yaw (degrees) contributed by chest+upper-chest together.</summary>
        public float MaxTorsoYawDegrees => headAndTorso.maxTorsoYawDegrees;
        /// <summary>Maximum pitch (degrees) contributed by chest+upper-chest together.</summary>
        public float MaxTorsoPitchDegrees => headAndTorso.maxTorsoPitchDegrees;

        /// <summary>Eye output backend.</summary>
        public GazeEyeActuationMode EyeActuationMode => eyes.eyeActuationMode;
        /// <summary>Oculomotor range: maximum eye yaw (degrees) from rest.</summary>
        public float EyeMaxYawDegrees => eyes.eyeMaxYawDegrees;
        /// <summary>Oculomotor range: maximum upward eye pitch (degrees).</summary>
        public float EyeMaxPitchUpDegrees => eyes.eyeMaxPitchUpDegrees;
        /// <summary>Oculomotor range: maximum downward eye pitch (degrees).</summary>
        public float EyeMaxPitchDownDegrees => eyes.eyeMaxPitchDownDegrees;
        /// <summary>Fraction of the oculomotor range where soft-limit compression begins.</summary>
        public float EyeSoftLimitFraction => eyes.eyeSoftLimitFraction;
        /// <summary>How strongly the eyes re-center in the orbit as the head catches up (0 = stay off-center).</summary>
        public float OrbitRecenteringStrength => eyes.orbitRecenteringStrength;
        /// <summary>Eye tracking sharpness during smooth pursuit (higher = tighter tracking).</summary>
        public float EyeTrackingSharpness => eyes.eyeTrackingSharpness;
        /// <summary>Minimum saccade duration (seconds) — the main-sequence intercept.</summary>
        public float SaccadeMinDurationSeconds => eyes.saccadeMinDurationSeconds;
        /// <summary>Added saccade duration per degree of amplitude (seconds/degree) — the main-sequence slope.</summary>
        public float SaccadeDurationPerDegree => eyes.saccadeDurationPerDegree;
        /// <summary>Gaze error (degrees) below which no corrective saccade is issued.</summary>
        public float SaccadeDeadzoneDegrees => eyes.saccadeDeadzoneDegrees;
        /// <summary>Saccadic reaction latency (seconds): the pause before the eyes launch toward a new or displaced target.</summary>
        public float SaccadeReactionSeconds => eyes.saccadeReactionSeconds;
        /// <summary>Pursuit error (degrees) above which a catch-up saccade fires.</summary>
        public float CatchUpErrorDegrees => eyes.catchUpErrorDegrees;
        /// <summary>
        ///     Predictive pursuit lead (seconds): during smooth pursuit the eyes aim slightly ahead
        ///     of a moving target along its measured velocity, cancelling the constant trailing error
        ///     that catch-up saccades would otherwise close. 0 disables prediction entirely. Saccades
        ///     stay ballistic to the true point and static targets are unaffected. Best kept near
        ///     1/<see cref="EyeTrackingSharpness" /> and below 2/<see cref="EyeTrackingSharpness" />,
        ///     past which the eyes over-lead and track worse.
        /// </summary>
        public float PursuitLeadSeconds => eyes.pursuitLeadSeconds;
        /// <summary>Converge the eyes on near targets (per-eye yaw differs — VR lean-ins).</summary>
        public bool EnableVergence => eyes.enableVergence;
        /// <summary>Closest supported convergence distance (meters); nearer targets clamp here.</summary>
        public float VergenceMinDistance => eyes.vergenceMinDistance;
        /// <summary>Maximum inward convergence angle per eye (degrees) — the cross-eye clamp.</summary>
        public float MaxConvergenceDegrees => eyes.maxConvergenceDegrees;
        /// <summary>
        ///     Interpupillary distance (meters) used only when the rig has no eye bones: sets how far
        ///     apart the synthesized eyes sit so the blendshape backend still converges on near targets.
        /// </summary>
        public float SyntheticInterpupillaryDistance => eyes.syntheticInterpupillaryDistance;
        /// <summary>Fixation drift amplitude (degrees) — slow wander while fixating.</summary>
        public float FixationDriftDegrees => eyes.fixationDriftDegrees;
        /// <summary>Fixation drift frequency (Hz).</summary>
        public float FixationDriftFrequency => eyes.fixationDriftFrequency;
        /// <summary>Mean seconds between fixation micro-saccades.</summary>
        public float MicroSaccadeIntervalMean => eyes.microSaccadeIntervalMean;
        /// <summary>Uniform jitter (± seconds) applied to the micro-saccade interval.</summary>
        public float MicroSaccadeIntervalJitter => eyes.microSaccadeIntervalJitter;
        /// <summary>Micro-saccade amplitude (degrees).</summary>
        public float MicroSaccadeAmplitudeDegrees => eyes.microSaccadeAmplitudeDegrees;
        /// <summary>Scan between implied face landmarks (eyes/mouth) when gazing at the player or a face.</summary>
        public bool EnableFaceScan => eyes.enableFaceScan;
        /// <summary>Mean seconds between face-scan fixation shifts.</summary>
        public float FaceScanIntervalMean => eyes.faceScanIntervalMean;
        /// <summary>Uniform jitter (± seconds) applied to the face-scan interval.</summary>
        public float FaceScanIntervalJitter => eyes.faceScanIntervalJitter;
        /// <summary>Angular radius (degrees) of the implied face-landmark triangle.</summary>
        public float FaceScanRadiusDegrees => eyes.faceScanRadiusDegrees;
        /// <summary>While the player is speaking, bias face-scan fixations toward the mouth landmark (speech comprehension behavior).</summary>
        public bool EnableListenerMouthBias => eyes.enableListenerMouthBias;
        /// <summary>Multiplier on the mouth landmark's selection weight at full listener mouth-bias (player speaking).</summary>
        public float ListenerMouthBiasStrength => eyes.listenerMouthBiasStrength;

        /// <summary>Statistical blinking through the facial compositor.</summary>
        public bool EnableBlink => blinkAndLids.enableBlink;
        /// <summary>Mean seconds between spontaneous blinks.</summary>
        public float BlinkIntervalMean => blinkAndLids.blinkIntervalMean;
        /// <summary>Uniform jitter (± seconds) applied to the blink interval.</summary>
        public float BlinkIntervalJitter => blinkAndLids.blinkIntervalJitter;
        /// <summary>Lid close time (seconds).</summary>
        public float BlinkCloseSeconds => blinkAndLids.blinkCloseSeconds;
        /// <summary>Lid open time (seconds).</summary>
        public float BlinkOpenSeconds => blinkAndLids.blinkOpenSeconds;
        /// <summary>Refractory window (seconds) during which no new blink can start.</summary>
        public float BlinkRefractorySeconds => blinkAndLids.blinkRefractorySeconds;
        /// <summary>Gaze shift amplitude (degrees) above which a blink may accompany the shift (0 disables).</summary>
        public float GazeShiftBlinkThresholdDegrees => blinkAndLids.gazeShiftBlinkThresholdDegrees;
        /// <summary>Probability that a large gaze shift triggers a blink.</summary>
        public float GazeShiftBlinkProbability => blinkAndLids.gazeShiftBlinkProbability;
        /// <summary>Eyelids follow vertical eye rotation (look down lowers the lids).</summary>
        public bool EnableEyelidFollow => blinkAndLids.enableEyelidFollow;
        /// <summary>Strength of the eyelid pitch-follow.</summary>
        public float EyelidFollowStrength => blinkAndLids.eyelidFollowStrength;
        /// <summary>
        ///     Elevate blink likelihood for a short window after a cognitive boundary — the end of an
        ///     utterance, a final transcript, or the player pausing — so blinks read as beats rather
        ///     than random ticks. Never guarantees a blink, only makes one more likely.
        /// </summary>
        public bool EnableBlinkClustering => blinkAndLids.enableBlinkClustering;
        /// <summary>Blink-rate multiplier applied for ~0.7 s after a clustering cue.</summary>
        public float BlinkClusterRateMultiplier => blinkAndLids.blinkClusterRateMultiplier;

        /// <summary>Allow full-body reorientation toward the gaze target (state policy still gates it).</summary>
        public bool EnableBodyTurn => bodyTurn.enableBodyTurn;
        /// <summary>Yaw error (degrees) below which the turn is considered complete.</summary>
        public float BodyTurnCompletionToleranceDegrees => bodyTurn.bodyTurnCompletionToleranceDegrees;
        /// <summary>
        ///     While a body turn is in flight, head/torso gaze offsets are scaled to this fraction:
        ///     the neck visibly relaxes and rides the turn instead of staying pinned at its limit,
        ///     while the eyes stay on target.
        /// </summary>
        public float BodyTurnHeadRelief => bodyTurn.bodyTurnHeadRelief;
        /// <summary>Peak speed (degrees/second) of the procedural fallback turn used when no animated handler is available.</summary>
        public float ProceduralTurnSpeed => bodyTurn.proceduralTurnSpeed;

        /// <summary>Ambient eye/head exploration while no target is engaged.</summary>
        public bool EnableAmbientExploration => idleLife.enableAmbientExploration;
        /// <summary>Ambient exploration yaw range (± degrees).</summary>
        public float AmbientYawRangeDegrees => idleLife.ambientYawRangeDegrees;
        /// <summary>Ambient exploration upward pitch range (degrees).</summary>
        public float AmbientPitchUpDegrees => idleLife.ambientPitchUpDegrees;
        /// <summary>Ambient exploration downward pitch range (degrees).</summary>
        public float AmbientPitchDownDegrees => idleLife.ambientPitchDownDegrees;
        /// <summary>Minimum seconds between ambient fixation changes.</summary>
        public float AmbientIntervalMin => idleLife.ambientIntervalMin;
        /// <summary>Maximum seconds between ambient fixation changes.</summary>
        public float AmbientIntervalMax => idleLife.ambientIntervalMax;
        /// <summary>Fraction of the ambient look carried by the head (rest is eyes only).</summary>
        public float AmbientHeadFollow => idleLife.ambientHeadFollow;
        /// <summary>Bias toward re-centering instead of picking a new off-center point.</summary>
        public float AmbientRecenterBias => idleLife.ambientRecenterBias;
        /// <summary>Occasional short eye-led glances at the player while otherwise idle.</summary>
        public bool EnableCuriosityGlances => idleLife.enableCuriosityGlances;
        /// <summary>Minimum seconds between curiosity glances.</summary>
        public float CuriosityGlanceIntervalMin => idleLife.curiosityGlanceIntervalMin;
        /// <summary>Maximum seconds between curiosity glances.</summary>
        public float CuriosityGlanceIntervalMax => idleLife.curiosityGlanceIntervalMax;

        /// <summary>Whether the character watches the road while walking somewhere.</summary>
        public bool EnableTravelGaze => travel.enableTravelGaze;
        /// <summary>
        ///     Whether a walking character glances at its destination — or whoever it is
        ///     following — every few seconds. Off by default; watching the path ahead is
        ///     <see cref="EnableTravelGaze" /> and is independent of this.
        /// </summary>
        public bool EnableDestinationGlances => travel.enableDestinationGlances;

        /// <summary>Priority tier of the path-ahead target. Above the player anchor by default.</summary>
        public int TravelPathPriority => travel.travelPathPriority;

        /// <summary>Look-ahead distance at walking pace (metres).</summary>
        public float PathLookAheadMinMeters => travel.pathLookAheadMinMeters;

        /// <summary>Look-ahead distance at full pace (metres).</summary>
        public float PathLookAheadMaxMeters => travel.pathLookAheadMaxMeters;

        /// <summary>Fade-in time for travel gaze (seconds).</summary>
        public float TravelEngageSeconds => travel.travelEngageSeconds;

        /// <summary>Minimum seconds between glances at a destination.</summary>
        public float TravelGlanceIntervalMin => travel.travelGlanceIntervalMin;

        /// <summary>Maximum seconds between glances at a destination.</summary>
        public float TravelGlanceIntervalMax => travel.travelGlanceIntervalMax;

        /// <summary>Minimum seconds between glances at a person being followed.</summary>
        public float CompanionGlanceIntervalMin => travel.companionGlanceIntervalMin;

        /// <summary>Maximum seconds between glances at a person being followed.</summary>
        public float CompanionGlanceIntervalMax => travel.companionGlanceIntervalMax;

        /// <summary>Duration of one travel glance (seconds).</summary>
        public float TravelGlanceHoldSeconds => travel.travelGlanceHoldSeconds;

        /// <summary>Interval multiplier applied to travel glances while in conversation.</summary>
        public float TravelGlanceConversationScale => travel.travelGlanceConversationScale;

        /// <summary>
        ///     How far the eyes drop for a beat as the character comes to rest at the end of a
        ///     walk. Eye-only: it rides the micro-motion channel, which the head never reads.
        /// </summary>
        public float ArrivalSettleEyeDropDegrees => travel.arrivalSettleEyeDropDegrees;
        /// <summary>Length of the arrival settle, from the drop to the lift back.</summary>
        public float ArrivalSettleSeconds => travel.arrivalSettleSeconds;

        /// <summary>Distance at which the arrival settle begins (metres).</summary>
        public float ArrivalApproachMeters => travel.arrivalApproachMeters;

        /// <summary>Distance at which the path stops being a target at all (metres).</summary>
        public float ArrivalReleaseMeters => travel.arrivalReleaseMeters;

        /// <summary>Head participation scale while travelling.</summary>
        public float TravelHeadContributionScale => travel.travelHeadContributionScale;
        /// <summary>Duration (seconds) of a curiosity glance.</summary>
        public float CuriosityGlanceDuration => idleLife.curiosityGlanceDuration;
        /// <summary>
        ///     While idle, glance back at the player sooner when a Player Attention Sensor reports the
        ///     player is looking at this character. Needs curiosity glances enabled and a
        ///     <c>PlayerAttentionSensor</c> on the character.
        /// </summary>
        public bool CuriosityRespondsToAttention => idleLife.curiosityRespondsToAttention;

        /// <summary>Small acknowledgment nods while the character is listening (subtle, on by default).</summary>
        public bool EnableListeningNods => conversationalGestures.enableListeningNods;
        /// <summary>Peak downward pitch (degrees) of a listening nod.</summary>
        public float NodPitchDegrees => conversationalGestures.nodPitchDegrees;
        /// <summary>Duration (seconds) of one nod's double-bob envelope.</summary>
        public float NodDurationSeconds => conversationalGestures.nodDurationSeconds;
        /// <summary>Minimum seconds between listening nods.</summary>
        public float ListeningNodIntervalMin => conversationalGestures.listeningNodIntervalMin;
        /// <summary>Maximum seconds between listening nods.</summary>
        public float ListeningNodIntervalMax => conversationalGestures.listeningNodIntervalMax;
        /// <summary>Probability of a nod right when Listening begins — the "I'm with you" beat.</summary>
        public float AcknowledgeNodProbability => conversationalGestures.acknowledgeNodProbability;

        /// <summary>
        ///     Plays a one-shot ~1 s startle micro-reaction — re-acquisition saccade, blink and a
        ///     small head tilt — when the character is interrupted mid-sentence (Speaking →
        ///     Interrupted). Non-repeating until the character speaks again.
        /// </summary>
        public bool EnableInterruptionReaction => conversationalGestures.enableInterruptionReaction;
        /// <summary>Magnitude of the interruption startle reaction (mainly the head tilt, ~2–4°).</summary>
        public float InterruptionReactionIntensity => conversationalGestures.interruptionReactionIntensity;

        /// <summary>
        ///     Turn-taking gaze choreography during Speaking (Natural eye-contact mode): direct
        ///     contact for short or reactive replies, sparse bounded breaks for genuinely planned or
        ///     extended answers, and a floor-yield cue after speech ends.
        /// </summary>
        public bool EnableTurnTakingGaze => conversationRhythm.enableTurnTakingGaze;
        /// <summary>Propensity for eligible planning breaks.</summary>
        public float PlanningBreakProbability => conversationRhythm.planningBreakProbability;
        /// <summary>Plays a deliberate blink as part of the floor-yield cue near the end of an utterance.</summary>
        public bool EnableYieldBlink => conversationRhythm.enableYieldBlink;
        /// <summary>Adds a small downward head dip to the floor-yield cue after speech ends.</summary>
        public bool EnableYieldHeadDip => conversationRhythm.enableYieldHeadDip;

        /// <summary>Scale gaze behavior by the dominant emotion.</summary>
        public bool EnableEmotionModulation => emotionModulation.enableEmotionModulation;
        /// <summary>Per-emotion modifiers applied while that emotion is dominant.</summary>
        public IReadOnlyList<EmotionGazeModifier> EmotionModifiers => emotionModulation.emotionModifiers;

        /// <summary>
        ///     Soften eye contact instead of holding a fixed stare as the player leans in close (VR):
        ///     the aversion floor rises, the face-scan radius widens and the blink rate quickens the
        ///     closer they get, all backing off smoothly as they step back. Bypassed entirely while an
        ///     eye-contact lock is in force — a kiosk keeps staring.
        /// </summary>
        public bool EnableProxemicRegulation => proxemics.enableProxemicRegulation;
        /// <summary>Distance (meters) at which the player starts to read as 'close' — proxemic softening ramps in below this distance.</summary>
        public float ProxemicCloseDistanceMeters => proxemics.proxemicCloseDistanceMeters;
        /// <summary>Overall strength of the proxemic softening effect (0 disables the effect while leaving the toggle on, 1 = full authored effect).</summary>
        public float ProxemicIntensity => proxemics.proxemicIntensity;

        /// <summary>Level-of-detail governor for crowds: far characters think less often and off-screen characters skip the solver stage entirely.</summary>
        public bool EnableGazeLod => performance.enableGazeLod;
        /// <summary>Distance (meters) beyond which the cognition tick drops to the far rate (with 1 m hysteresis).</summary>
        public float LodFarDistance => performance.lodFarDistance;
        /// <summary>Cognition rate (Hz) for characters beyond the far distance.</summary>
        public float LodFarCognitionHz => performance.lodFarCognitionHz;
        /// <summary>Skip the LateUpdate solver stage while none of the character's renderers is visible to a camera.</summary>
        public bool SkipWhenInvisible => performance.skipWhenInvisible;

        /// <summary>Diagnostics verbosity for this character's gaze trace.</summary>
        public GazeTraceVerbosity TraceVerbosity => diagnostics.traceVerbosity;
        /// <summary>Maximum firehose dump rate (Hz).</summary>
        public float FirehoseHz => diagnostics.firehoseHz;

        // ── Resolution helpers ───────────────────────────────────────────────

        /// <summary>
        ///     Resolves the policy for <paramref name="state" />, falling back to the Idle
        ///     entry (or a conservative built-in default) when the state is not authored.
        /// </summary>
        public GazeStatePolicy GetStatePolicy(DialogueState state)
        {
            GazeStatePolicy idleFallback = default;
            bool foundIdle = false;

            List<GazeStatePolicy> policies = conversationStates.statePolicies;
            for (int i = 0; policies != null && i < policies.Count; i++)
            {
                GazeStatePolicy entry = policies[i];
                if (entry.State == state) return Sanitize(entry);
                if (entry.State == DialogueState.Idle && !foundIdle)
                {
                    idleFallback = entry;
                    foundIdle = true;
                }
            }

            if (foundIdle) return Sanitize(idleFallback);

            return new GazeStatePolicy
            {
                State = state,
                Engagement = 0f,
                AllowPlayerTarget = false,
                HeadContribution = 0.5f,
                AllowBodyTurn = false,
                AversionMode = GazeAversionMode.None,
                AversionStrength = 0f,
                FixationLiveliness = 1f
            };
        }

        /// <summary>
        ///     Resolves the modifier for <paramref name="emotionLabel" /> (case-insensitive).
        ///     Returns <c>false</c> when modulation is disabled or the label is not authored.
        /// </summary>
        public bool TryGetEmotionModifier(string emotionLabel, out EmotionGazeModifier modifier)
        {
            modifier = default;
            if (!emotionModulation.enableEmotionModulation || string.IsNullOrEmpty(emotionLabel)) return false;

            List<EmotionGazeModifier> modifiers = emotionModulation.emotionModifiers;
            for (int i = 0; modifiers != null && i < modifiers.Count; i++)
            {
                if (string.Equals(modifiers[i].EmotionLabel, emotionLabel,
                        StringComparison.OrdinalIgnoreCase))
                {
                    modifier = modifiers[i];
                    return true;
                }
            }

            return false;
        }

        private static GazeStatePolicy Sanitize(GazeStatePolicy policy)
        {
            policy.Engagement = Mathf.Clamp01(policy.Engagement);
            policy.HeadContribution = Mathf.Clamp01(policy.HeadContribution);
            policy.AversionStrength = Mathf.Clamp01(policy.AversionStrength);
            policy.FixationLiveliness = Mathf.Clamp(policy.FixationLiveliness, 0f, 2f);
            return policy;
        }

        private void OnValidate()
        {
            targeting.playerFullRelevanceDistance =
                Mathf.Min(targeting.playerFullRelevanceDistance, targeting.playerMaxDistance);
            idleLife.ambientIntervalMax = Mathf.Max(idleLife.ambientIntervalMax, idleLife.ambientIntervalMin);
            idleLife.curiosityGlanceIntervalMax =
                Mathf.Max(idleLife.curiosityGlanceIntervalMax, idleLife.curiosityGlanceIntervalMin);
            conversationalGestures.listeningNodIntervalMax =
                Mathf.Max(conversationalGestures.listeningNodIntervalMax, conversationalGestures.listeningNodIntervalMin);
            eyes.eyeMaxPitchUpDegrees = Mathf.Min(eyes.eyeMaxPitchUpDegrees, eyes.eyeMaxPitchDownDegrees + 20f);
            travel.pathLookAheadMaxMeters = Mathf.Max(travel.pathLookAheadMaxMeters, travel.pathLookAheadMinMeters);
            travel.travelGlanceIntervalMax = Mathf.Max(travel.travelGlanceIntervalMax, travel.travelGlanceIntervalMin);
            travel.companionGlanceIntervalMax =
                Mathf.Max(travel.companionGlanceIntervalMax, travel.companionGlanceIntervalMin);
            // The release distance must stay strictly inside the approach distance, or the settle
            // has no window to fade across and the path target vanishes in a single frame.
            travel.arrivalReleaseMeters = Mathf.Min(travel.arrivalReleaseMeters, travel.arrivalApproachMeters - 0.1f);

            List<GazeStatePolicy> policies = conversationStates.statePolicies;
            if (policies == null) return;
            for (int i = 0; i < policies.Count; i++)
                policies[i] = Sanitize(policies[i]);
        }

        /// <summary>Creates the runtime default profile (never saved as an asset).</summary>
        public static ConvaiGazeProfile CreateDefault()
        {
            ConvaiGazeProfile instance = CreateInstance<ConvaiGazeProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        /// <summary>
        ///     Shipped defaults: Idle keeps the player suppressed with ambient life on;
        ///     Speaking uses high contact with bounded turn-taking rhythm; Thinking breaks contact with
        ///     cognitive aversion; body turns fire in the conversation states.
        /// </summary>
        private static List<GazeStatePolicy> BuildDefaultStatePolicies() => new()
        {
            new GazeStatePolicy { State = DialogueState.Idle,        Engagement = 0f,    AllowPlayerTarget = false, HeadContribution = 0.35f, AllowBodyTurn = false, AversionMode = GazeAversionMode.None,      AversionStrength = 0f,    FixationLiveliness = 1f },
            new GazeStatePolicy { State = DialogueState.Attending,   Engagement = 0.9f,  AllowPlayerTarget = true,  HeadContribution = 0.85f, AllowBodyTurn = true,  AversionMode = GazeAversionMode.Natural,   AversionStrength = 0.15f, FixationLiveliness = 1f },
            new GazeStatePolicy { State = DialogueState.Listening,   Engagement = 0.95f, AllowPlayerTarget = true,  HeadContribution = 0.85f, AllowBodyTurn = true,  AversionMode = GazeAversionMode.Natural,   AversionStrength = 0.08f, FixationLiveliness = 1.1f },
            new GazeStatePolicy { State = DialogueState.Thinking,    Engagement = 0.7f,  AllowPlayerTarget = true,  HeadContribution = 0.6f,  AllowBodyTurn = false, AversionMode = GazeAversionMode.Cognitive, AversionStrength = 0.7f,  FixationLiveliness = 1.3f },
            new GazeStatePolicy { State = DialogueState.Speaking,    Engagement = 1f,    AllowPlayerTarget = true,  HeadContribution = 0.85f, AllowBodyTurn = true,  AversionMode = GazeAversionMode.None,      AversionStrength = 0f,    FixationLiveliness = 1f },
            new GazeStatePolicy { State = DialogueState.Reacting,    Engagement = 1f,    AllowPlayerTarget = true,  HeadContribution = 0.9f,  AllowBodyTurn = true,  AversionMode = GazeAversionMode.None,      AversionStrength = 0f,    FixationLiveliness = 1.2f },
            new GazeStatePolicy { State = DialogueState.Interrupted, Engagement = 0.95f, AllowPlayerTarget = true,  HeadContribution = 0.9f,  AllowBodyTurn = true,  AversionMode = GazeAversionMode.None,      AversionStrength = 0f,    FixationLiveliness = 1.1f },
            new GazeStatePolicy { State = DialogueState.Settling,    Engagement = 0.6f,  AllowPlayerTarget = true,  HeadContribution = 0.6f,  AllowBodyTurn = false, AversionMode = GazeAversionMode.Natural,   AversionStrength = 0.25f, FixationLiveliness = 0.9f },
        };

        /// <summary>
        ///     Shipped emotional gaze signatures (opt-in via <see cref="EnableEmotionModulation" />):
        ///     sadness looks down and reacts slower (a withdrawn, low-energy signature); joy
        ///     reacts quicker and scans more (a lively, engaged signature); anger looks aside
        ///     quickly (a sharp, confrontational-avoidant signature). The engagement, aversion
        ///     and blink scales are left neutral (1) on purpose: these rows say where the eyes go
        ///     and how quickly, and leave how much the character engages to the state table.
        /// </summary>
        private static List<EmotionGazeModifier> BuildDefaultEmotionModifiers() => new()
        {
            new EmotionGazeModifier
            {
                EmotionLabel = "sadness", EngagementScale = 1f, AversionScale = 1f, BlinkRateScale = 1f, LidApertureScale = 1f,
                AversionBias = GazeAversionBias.Down, SaccadeTempoScale = 0.8f, FixationLivelinessScale = 1f
            },
            new EmotionGazeModifier
            {
                EmotionLabel = "joy", EngagementScale = 1f, AversionScale = 1f, BlinkRateScale = 1f, LidApertureScale = 1f,
                AversionBias = GazeAversionBias.CognitiveDefault, SaccadeTempoScale = 1.15f, FixationLivelinessScale = 1.2f
            },
            new EmotionGazeModifier
            {
                EmotionLabel = "anger", EngagementScale = 1f, AversionScale = 1f, BlinkRateScale = 1f, LidApertureScale = 1f,
                AversionBias = GazeAversionBias.Side, SaccadeTempoScale = 1.2f, FixationLivelinessScale = 1f
            },
        };
    }
}
