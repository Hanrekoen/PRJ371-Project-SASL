using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>How talk gestures behave while the character is walking.</summary>
    public enum MovingTalkMode
    {
        /// <summary>
        ///     Best available per entry: entries with an additive clip play it as a delta
        ///     over the gait (arm swing survives under the gesture); entries without one
        ///     fall back to the softened override.
        /// </summary>
        // InspectorName changes only what the dropdown reads; the identifier and its serialized
        // numeric value are untouched, so no asset or public API is affected.
        [InspectorName("Best available (recommended)")]
        Auto = 0,

        /// <summary>
        ///     Always keep the override gesture but cap its weight while moving so the
        ///     gait's arm swing bleeds through instead of freezing the upper body.
        /// </summary>
        [InspectorName("Blend gestures into the walk")]
        SoftenedOverride = 1,

        /// <summary>Fade talk gestures out entirely while moving.</summary>
        [InspectorName("Stop gesturing while walking")]
        Suppress = 2
    }

    /// <summary>
    ///     Runtime tuning for the body animation system: transition timings, layer behavior,
    ///     locomotion synchronization, feature toggles, and diagnostics verbosity. Content
    ///     lives in <see cref="ConvaiBodyAnimationSet" />; this asset only shapes behavior,
    ///     so one config can be shared across many characters.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConvaiBodyAnimationConfig",
        menuName = "Convai/Embodiment/Body Animation Config",
        order = 142)]
    public sealed class ConvaiBodyAnimationConfig : ScriptableObject
    {
        // No [Header] on serialized fields: BodyAnimationConfigSections groups every field into
        // named Convai sections, and a Header decorator would draw a second, unstyled title inside
        // them. The two groupings also disagree by design — the section table is worded for someone
        // tuning a character, not for the order the fields happen to be declared in.
        [SerializeField, Min(0.01f)]
        [Tooltip("Crossfade between idle variants.")]
        private float _idleCrossfadeSeconds = 0.6f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Talk layer fade-in when the character starts speaking.")]
        private float _talkFadeInSeconds = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Talk layer fade-out when the character stops speaking.")]
        private float _talkFadeOutSeconds = 0.9f;

        [SerializeField, Min(0f)]
        [Tooltip("Short hold before talk fades out after speech stops, letting the current gesture settle before blending to idle.")]
        private float _talkReleaseDelaySeconds = 0.16f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Playback speed used during the short speech-release window. Slowing the clip prevents an authored arm motion from continuing to rise after speech has ended.")]
        private float _talkReleasePlaybackSpeed = 0.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Default action/gesture fade-in (entries may override).")]
        private float _actionFadeInSeconds = 0.25f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Default action/gesture fade-out (entries may override).")]
        private float _actionFadeOutSeconds = 0.35f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Pointing layer fade in/out.")]
        private float _pointingFadeSeconds = 0.3f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Crossfade when a live pointing gesture re-aims to a new direction or is " +
                 "re-targeted mid-release.")]
        private float _pointingReaimCrossfadeSeconds = 0.25f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Clip crossfade inside action chains (intro→main→outro segments) and for " +
                 "same-mask action replacement.")]
        private float _actionChainCrossfadeSeconds = 0.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Crossfade between locomotion states (idle↔move, starts, stops, turns).")]
        private float _locomotionCrossfadeSeconds = 0.25f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How fully a playing action replaces what is underneath it. 1 shows the action as " +
                 "authored; lower values let the idle or walk pose bleed through it.")]
        private float _actionLayerWeight = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How fully a pointing gesture replaces the arm pose underneath it. Lower values " +
                 "read as a looser, less committed point.")]
        private float _pointingLayerWeight = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How strongly speech accents ride on top of the talk gesture. Lower values make " +
                 "them a subtle inflection rather than a visible movement.")]
        private float _beatLayerWeight = 1f;

        [SerializeField]
        [Tooltip("Easing applied to every layer/state weight fade. Must go 0→1.")]
        private AnimationCurve _blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField, Min(1f)]
        [Tooltip("Minimum seconds an idle variant plays before the scheduler may swap it.")]
        private float _idleVariantIntervalMin = 8f;

        [SerializeField, Min(1f)]
        [Tooltip("Maximum seconds an idle variant plays before the scheduler swaps it.")]
        private float _idleVariantIntervalMax = 16f;

        [SerializeField]
        [Tooltip("Scale the talk layer weight by live speech energy so soft speech gestures less.")]
        private bool _useSpeechEnergy = true;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Talk layer weight at zero speech energy (when Use Speech Energy is on).")]
        private float _talkWeightAtLowEnergy = 0.2f;

        // These four talk values, and the two above, are the ones Convai tuned by hand on the
        // shipped settings and then never brought back here. A config made from the Create menu
        // gestured at full overlay weight and kept gesturing at 0.65 through silence, so it was the
        // loudest character in any project and nothing in the Inspector said why the samples looked
        // different. BodyAnimationShippedConfigGuardTests now fails if the two drift again.
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Maximum weight the talk overlay can reach. Lower values keep more of the idle pose under speech gestures.")]
        private float _talkOverlayWeight = 0.45f;

        [SerializeField]
        [Tooltip("Swap to another talk variant when the current one loops (long speeches vary).")]
        private bool _switchTalkVariantOnLoop = true;

        [SerializeField, Min(0.01f)]
        [Tooltip("Crossfade between talk variants during a long speech.")]
        private float _talkVariantCrossfadeSeconds = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Talk layer fade-in when the character enters Listening/Attending and starts " +
                 "a Listen pose. Slower than Talk Fade In Seconds so the listening pose settles " +
                 "in gently rather than snapping on.")]
        private float _listenFadeInSeconds = 0.8f;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds DialogueState must continuously be Thinking before a Think pose " +
                 "commits. Sub-second replies stay below this gate and never twitch a think " +
                 "clip in and out.")]
        private float _thinkingEnterDelaySeconds = 0.4f;

        [SerializeField, Min(0f)]
        [Tooltip("When DialogueState becomes Interrupted while the talk layer is actively " +
                 "playing, the current pose is frozen (not faded) for this many seconds before " +
                 "the faster interrupted release begins.")]
        private float _interruptedFreezeSeconds = 0.25f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Multiplier on Talk Fade Out Seconds applied to the release that follows an " +
                 "interruption freeze — lower values settle out faster than a normal fade-out.")]
        private float _interruptedReleaseScale = 0.6f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Upper bound on the added latency an authored Outro Clip may introduce when " +
                 "talk ends: the release fade-out is capped to at most this many seconds even " +
                 "when the outro clip itself is longer, so a long wind-down animation never " +
                 "delays the character settling back to idle. Entries without an Outro Clip are " +
                 "unaffected.")]
        private float _talkOutroMaxSeconds = 0.7f;

        [SerializeField]
        [Tooltip("How talk gestures behave while the character walks. Auto plays additive " +
                 "gesture deltas over the gait for entries that have an Additive Clip and " +
                 "softens the override for those that don't; Softened Override always uses the " +
                 "reduced-weight override; Suppress fades talk out entirely while moving.")]
        private MovingTalkMode _movingTalkMode = MovingTalkMode.Auto;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Weight of the additive walk-and-talk overlay (arms and hands) while moving.")]
        private float _movingTalkWeight = 0.7f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Cap on the override talk weight while moving when no additive clip exists — " +
                 "lets the gait's arm swing bleed through instead of freezing the upper body.")]
        private float _movingTalkOverrideWeight = 0.45f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Crossfade between the stationary and moving talk overlays when movement " +
                 "starts or stops mid-speech.")]
        private float _movingTalkBlendSeconds = 0.35f;

        [SerializeField]
        [Tooltip("Fire short additive beat gestures on detected onsets in the live speech-energy " +
                 "signal, riding on top of the talk overlay. Off by default because it plays " +
                 "AUTHORED clips: turn it on once the animation set has at least one action " +
                 "tagged with the Beat or Emphatic cue, and the setup checklist will point that " +
                 "out for you as soon as it does.")]
        private bool _enableBeatGestures;

        [SerializeField, Min(0.05f)]
        [Tooltip("Minimum seconds between two beat gestures, regardless of how many onsets are " +
                 "detected in between.")]
        private float _beatRefractorySeconds = 1.2f;

        [SerializeField, Range(0f, 1.5f)]
        [Tooltip("Multiplier applied to a beat gesture's onset-strength-derived weight. 1 = " +
                 "strength maps directly to weight; lower values mute beats, higher values push " +
                 "weaker onsets closer to full weight.")]
        private float _beatWeightScale = 1f;

        [SerializeField]
        [Tooltip("Additionally derive SPECULATIVE accents from the speech-energy envelope alone, " +
                 "with no semantic evidence behind them, and publish those for peer performers " +
                 "too. Off by default — it adds motion the character did not specifically mean. " +
                 "Cues that ARE meant (classified from the final transcript, or handed over by " +
                 "the referential-gesture director) are always published regardless of this " +
                 "setting; this only controls the speculative tier.")]
        private bool _enableAdvancedCoSpeech;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("How loud speech has to get before it counts as an accent. Raise it so only " +
                 "genuinely emphatic moments produce one; lower it to accent more of the line.")]
        private float _coSpeechMinimumAccentEnergy = 0.42f;

        [SerializeField, Range(0.1f, 5f)]
        [Tooltip("How sharply the volume has to rise to read as emphasis. Higher values need a " +
                 "steeper jump, so gradual swells are ignored and only punchy moments accent.")]
        private float _coSpeechEmphasisDerivative = 1.2f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Chance that a qualifying moment actually becomes an accent. Below 1 the " +
                 "character skips some of them, which keeps the performance from reading " +
                 "mechanical.")]
        private float _coSpeechAccentProbability = 0.48f;

        [SerializeField, Range(0.3f, 3f)]
        [Tooltip("Minimum seconds between two accents, however many qualifying moments occur in " +
                 "between.")]
        private float _coSpeechAccentRefractorySeconds = 0.85f;

        [SerializeField, Range(0.01f, 0.3f)]
        [Tooltip("How far volume must fall below the running average to count as a phrase break. " +
                 "Larger values need a clearer pause, so mid-sentence dips are not mistaken for one.")]
        private float _coSpeechPhraseEnergyMargin = 0.08f;

        [SerializeField, Range(0.05f, 0.6f)]
        [Tooltip("Wind-up before the accent lands — the hand rises for this long first. Real " +
                 "gesture leads the word it stresses rather than landing on it.")]
        private float _coSpeechPreparationSeconds = 0.22f;

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Length of the accent itself, the fast part of the movement. Short reads as a " +
                 "jab, longer as a sweep.")]
        private float _coSpeechStrokeSeconds = 0.16f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How long the pose is held at full extension after the accent lands, before it " +
                 "starts settling. Zero settles immediately.")]
        private float _coSpeechReferentialHoldSeconds = 0.28f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("How long the arm takes to settle back after the hold. Longer reads calmer; " +
                 "shorter snaps back.")]
        private float _coSpeechRetractionSeconds = 0.38f;

        [SerializeField]
        [Tooltip("Gesture at what the character says: a second-person word (\"you\"/\"your\") " +
                 "plays a palm-open-toward-player gesture, a first-person word (\"I\"/\"me\") " +
                 "plays hand-to-chest, a mentioned registered scene object plays an indicate " +
                 "gesture, and an ordinal/number word (\"first\"/\"three\") plays an enumerate " +
                 "beat — additive one-shots riding the talk overlay. An animation set that " +
                 "authors a clip tagged PalmToPlayer/HandToChest/IndicateObject/Enumerate plays " +
                 "that clip; one that doesn't hands the cue to any peer performer registered on " +
                 "the character (Convai Body Language performs it procedurally), so the gesture " +
                 "still happens either way.")]
        private bool _enableReferentialGestures = true;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Minimum seconds between any two referential gestures, regardless of how many " +
                 "spoken lines match in between.")]
        private float _referentialGestureRefractorySeconds = 6f;

        [SerializeField, Range(1f, 60f)]
        [Tooltip("Minimum seconds before the SAME referential-gesture class (e.g. " +
                 "PalmToPlayer) can fire again.")]
        private float _referentialGestureClassCooldownSeconds = 10f;

        [SerializeField, Range(0f, 1.5f)]
        [Tooltip("Multiplier applied to a referential gesture's weight, before the proximity " +
                 "expressiveness multiplier.")]
        private float _referentialGestureWeight = 1f;

        [SerializeField]
        [Tooltip("Scale talk-gesture expressiveness by conversation distance: farther away reads " +
                 "broader, closer reads subtler. Off leaves the talk overlay and beat gestures " +
                 "unscaled by distance.")]
        private bool _proximityExpressiveness = true;

        [SerializeField, Min(0.1f)]
        [Tooltip("Distance (m) at or below which Proximity Near Scale applies fully.")]
        private float _proximityNearDistance = 1.5f;

        [SerializeField, Range(0.8f, 1.15f)]
        [Tooltip("Expressiveness multiplier at or below Proximity Near Distance (close conversation reads subtler).")]
        private float _proximityNearScale = 0.85f;

        [SerializeField, Min(0.2f)]
        [Tooltip("Distance (m) at or beyond which Proximity Far Scale applies fully.")]
        private float _proximityFarDistance = 6f;

        [SerializeField, Range(0.8f, 1.15f)]
        [Tooltip("Expressiveness multiplier at or beyond Proximity Far Distance (far conversation reads broader).")]
        private float _proximityFarScale = 1.15f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Seconds the proximity multiplier smooths over — walking toward the character must " +
                 "not visibly pump gesture size.")]
        private float _proximitySmoothingSeconds = 0.5f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Multiplies talk-gesture expressiveness: the talk overlay weight cap, the " +
                 "variant-switch-on-loop probability, and the beat-gesture rate (inversely, via " +
                 "the refractory window). 1 = today's default behavior; 0 = inert/still; 2 = " +
                 "maximally lively. Lets characters sharing one animation set read as different " +
                 "people.")]
        private float _gestureLiveliness = 1f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Stretches idle variant intervals and slightly lengthens talk fade-ins, so " +
                 "higher values read as a more composed, deliberate character. 1 = today's " +
                 "default behavior.")]
        private float _calmness = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Agent speed (m/s) commanded for walking. Also the fallback authored walk " +
                 "speed until the Clip Motion Analyzer fills metadata.")]
        private float _walkSpeed = 1.2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Agent speed (m/s) commanded for jogging.")]
        private float _jogSpeed = 2.6f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Smoothing time for the agent-speed reading that drives the movement blend.")]
        private float _speedDampingSeconds = 0.12f;

        [SerializeField, Range(0.5f, 1f)]
        [Tooltip("Lower clamp for playback-rate warping (agent speed / authored speed).")]
        private float _rateWarpMin = 0.85f;

        [SerializeField, Range(1f, 1.5f)]
        [Tooltip("Upper clamp for playback-rate warping.")]
        private float _rateWarpMax = 1.2f;

        [SerializeField, Min(1f)]
        [Tooltip("Yaw error (degrees) that triggers turn-in-place instead of moving off.")]
        private float _turnInPlaceMinAngle = 60f;

        [SerializeField, Min(90f)]
        [Tooltip("Yaw error (degrees) above which the 180° turn/start variants are used.")]
        private float _turn180MinAngle = 135f;

        [SerializeField, Range(0.5f, 0.98f)]
        [Tooltip("Normalized time where start/turn/speed-change one-shots hand off (crossfade) " +
                 "into their follow-up state.")]
        private float _motionHandoffNormalizedTime = 0.85f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("Fraction of walk speed below which the low-speed stop clip is chosen.")]
        private float _lowSpeedStopFraction = 0.6f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (m) a leg must cover at cruise speed before a planted stop clip " +
                 "may play. Shorter repositions settle with agent braking and a plain idle " +
                 "blend — a full-momentum plant on a two-step move reads theatrical.")]
        private float _plantedStopMinTravel = 1.2f;

        [SerializeField]
        [Tooltip("When the character points at something, also glance at it briefly before " +
                 "gaze returns to the player — instead of eyes staying locked on the player " +
                 "while the arm points. Requires an IGazeGlanceHandler registered on the " +
                 "context (the Convai Gaze module); harmless no-op otherwise.")]
        private bool _enablePointGlance = true;

        [SerializeField, Range(0.2f, 3f)]
        [Tooltip("How long the glance holds on the point target before gaze returns to the player.")]
        private float _pointGlanceSeconds = 0.9f;

        [SerializeField]
        [Tooltip("When nobody has engaged the character in conversation for a while, perform an " +
                 "Ambient-tagged action (stretching, tidying, examining) on a randomized cadence " +
                 "instead of standing motionless, and wind it down gracefully the instant the " +
                 "player engages. Off by default because it plays AUTHORED clips: turn it on " +
                 "once the animation set has at least one action tagged Ambient, and the setup " +
                 "checklist will point that out for you as soon as it does.")]
        private bool _enableAmbientActivities;

        [SerializeField, Range(3f, 120f)]
        [Tooltip("Seconds DialogueState must continuously be Idle before the first ambient " +
                 "activity may fire.")]
        private float _ambientStartDelaySeconds = 12f;

        [SerializeField, Range(5f, 300f)]
        [Tooltip("Mean seconds between ambient activities once armed (±40% jitter).")]
        private float _ambientIntervalSeconds = 20f;

        [SerializeField, Range(1f, 20f)]
        [Tooltip("Do not start a new ambient activity while the conversation partner is closer " +
                 "than this distance (m).")]
        private float _ambientSuppressDistance = 4f;

        [SerializeField]
        [Tooltip("When the conversation partner sustains a position inside the character's " +
                 "personal-space bubble, take a short NavMesh reposition to just outside it and " +
                 "resettle, instead of standing statue-still while the player clips through. " +
                 "Zero new clips — reuses the existing locomotion short-move/settle path. Off " +
                 "by default.")]
        private bool _enableSocialSpacing;

        [SerializeField, Range(0.3f, 2f)]
        [Tooltip("Personal-space radius (m). A sustained conversant distance below this " +
                 "triggers a reposition.")]
        private float _comfortRadius = 0.7f;

        [SerializeField, Range(0.1f, 3f)]
        [Tooltip("Seconds the conversant must continuously be inside Comfort Radius before a " +
                 "reposition triggers — a brief brush-past doesn't count.")]
        private float _comfortHoldSeconds = 0.6f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Hard cap on social-spacing repositions per rolling minute, regardless of how " +
                 "often the trigger condition re-arms.")]
        private float _maxRepositionsPerMinute = 3f;

        [SerializeField]
        [Tooltip("Scale commanded walk/jog speed by the character's current emotion (arousal-" +
                 "derived): excited emotions walk faster, low-arousal ones trudge. Applied " +
                 "before the existing measured-speed rate-warp, so foot sync is preserved by " +
                 "construction, and smoothed over ~1s so an emotion flip never jerks the gait. " +
                 "Off by default: it is a stylistic choice, not every character should read its " +
                 "mood through walking pace.")]
        private bool _enableEmotionalGait;

        [SerializeField, Range(0f, 0.3f)]
        [Tooltip("Maximum fractional speed change from emotion, in both directions (e.g. 0.15 " +
                 "= walk speed ranges 85%..115% of its configured value).")]
        private float _emotionGaitRange = 0.15f;

        [SerializeField]
        [Tooltip("Publish a normalized locomotion-effort signal (IExertionSource) on the " +
                 "context so a peer module (Body Language) can fold physical effort into its " +
                 "breathing. Harmless when no consumer is registered.")]
        private bool _publishExertion = true;

        [SerializeField, Range(0.5f, 20f)]
        [Tooltip("Seconds of sustained full-run effort it takes exertion to climb from 0 to 1.")]
        private float _exertionRiseSeconds = 8f;

        [SerializeField, Range(0.5f, 20f)]
        [Tooltip("Seconds it takes exertion to decay from 1 back to 0 once the character slows or stops.")]
        private float _exertionRecoverySeconds = 6f;

        [SerializeField]
        [Tooltip("Play an authored turn clip when the character has to face a new direction before " +
                 "setting off, instead of pivoting on the spot while standing. Needs turn clips in " +
                 "the animation set; without them the character simply rotates.")]
        private bool _enableTurnInPlace = true;

        [SerializeField]
        [Tooltip("Start walking with a clip authored for the direction of travel, rather than " +
                 "blending into the walk loop from wherever the feet happened to be. Needs start " +
                 "clips in the animation set.")]
        private bool _enableDirectionalStarts = true;

        [SerializeField]
        [Tooltip("Finish a move with an authored stop that plants a foot, instead of easing to a " +
                 "halt. Needs stop clips in the animation set.")]
        private bool _enablePlantedStops = true;

        [SerializeField]
        [Tooltip("Also play planted stop clips when arriving at WALKING pace. Off by " +
                 "default: the plant performance reads right with jog momentum, while " +
                 "walking arrivals settle more naturally via braking + idle blend.")]
        private bool _plantedStopsWhileWalking;

        [SerializeField]
        [Tooltip("Play a transition clip when changing between walking and jogging, instead of " +
                 "blending the two loops. Needs walk↔jog clips in the animation set.")]
        private bool _enableSpeedChangeClips = true;

        [SerializeField]
        [Tooltip("Nudge clip playback rate to match how fast the character is actually travelling, " +
                 "so the feet do not slide. Turn off only if a clip's measured speed is wrong and " +
                 "the correction makes it worse.")]
        private bool _enableSpeedWarping = true;

        [SerializeField]
        [Tooltip("Let Unity's foot IK settle the feet onto the ground surface. Turn off on flat " +
                 "ground to save a little cost, or if a rig reacts badly to it.")]
        private bool _enableFootIK = true;

        [SerializeField]
        [Tooltip("How much this character writes to the console while it runs. Off (the default) " +
                 "still logs every warning and error — it only silences the routine play-by-play. " +
                 "State: transitions & lifecycle. Detail: adds selector decisions. Firehose: adds " +
                 "throttled per-tick weight dumps. Raise this while diagnosing one character, not " +
                 "as a standing setting.")]
        private AnimTraceVerbosity _traceVerbosity = AnimTraceVerbosity.Off;

        [SerializeField, Min(0.05f)]
        [Tooltip("Seconds between Firehose per-tick dumps.")]
        private float _firehoseIntervalSeconds = 0.25f;

        public float IdleCrossfadeSeconds => Mathf.Max(0.01f, _idleCrossfadeSeconds);
        public float TalkFadeInSeconds => Mathf.Max(0.01f, _talkFadeInSeconds);
        public float TalkFadeOutSeconds => Mathf.Max(0.01f, _talkFadeOutSeconds);
        public float TalkReleaseDelaySeconds => Mathf.Max(0f, _talkReleaseDelaySeconds);
        public float TalkReleasePlaybackSpeed => Mathf.Clamp01(_talkReleasePlaybackSpeed);
        public float ActionFadeInSeconds => Mathf.Max(0.01f, _actionFadeInSeconds);
        public float ActionFadeOutSeconds => Mathf.Max(0.01f, _actionFadeOutSeconds);
        public float PointingFadeSeconds => Mathf.Max(0.01f, _pointingFadeSeconds);
        public float PointingReaimCrossfadeSeconds => Mathf.Max(0.05f, _pointingReaimCrossfadeSeconds);
        public float ActionChainCrossfadeSeconds => Mathf.Max(0.05f, _actionChainCrossfadeSeconds);
        public float LocomotionCrossfadeSeconds => Mathf.Max(0.01f, _locomotionCrossfadeSeconds);
        public float ActionLayerWeight => Mathf.Clamp01(_actionLayerWeight);
        public float PointingLayerWeight => Mathf.Clamp01(_pointingLayerWeight);
        public float BeatLayerWeight => Mathf.Clamp01(_beatLayerWeight);

        /// <summary>
        ///     Never degenerate: falls back to a default 0→1 ease curve when the serialized
        ///     curve is null or has fewer than two keys. <c>[Min]</c>/<c>[Range]</c>/
        ///     <c>OnValidate</c> enforcement is editor-only and never runs in a build — an empty
        ///     curve here used to make every layer's weight evaluate to 0 with zero diagnostics
        ///    . The fallback instance is built once and cached, so this stays allocation-free.
        /// </summary>
        public AnimationCurve BlendCurve => IsBlendCurveUsable(_blendCurve)
            ? _blendCurve
            : _blendCurveFallback ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private AnimationCurve _blendCurveFallback;

        private static bool IsBlendCurveUsable(AnimationCurve curve) => curve != null && curve.length >= 2;

        public float IdleVariantIntervalMin => Mathf.Min(_idleVariantIntervalMin, _idleVariantIntervalMax);
        public float IdleVariantIntervalMax => Mathf.Max(_idleVariantIntervalMin, _idleVariantIntervalMax);

        public bool UseSpeechEnergy => _useSpeechEnergy;
        public float TalkWeightAtLowEnergy => Mathf.Clamp01(_talkWeightAtLowEnergy);
        public float TalkOverlayWeight => Mathf.Clamp01(_talkOverlayWeight);
        public bool SwitchTalkVariantOnLoop => _switchTalkVariantOnLoop;
        public float TalkVariantCrossfadeSeconds => Mathf.Max(0.01f, _talkVariantCrossfadeSeconds);

        /// <summary>Talk layer fade-in used when entering a Listening/Attending Listen pose.</summary>
        public float ListenFadeInSeconds => _listenFadeInSeconds;

        /// <summary>Seconds Thinking must persist before a Think pose commits.</summary>
        public float ThinkingEnterDelaySeconds => Mathf.Max(0f, _thinkingEnterDelaySeconds);

        /// <summary>Freeze-hold duration applied when DialogueState becomes Interrupted mid-gesture.</summary>
        public float InterruptedFreezeSeconds => Mathf.Max(0f, _interruptedFreezeSeconds);

        /// <summary>Multiplier on <see cref="TalkFadeOutSeconds" /> for the release that follows an interruption freeze.</summary>
        public float InterruptedReleaseScale => Mathf.Clamp(_interruptedReleaseScale, 0.05f, 1f);

        /// <summary>Effective walk-and-talk behavior.</summary>
        public MovingTalkMode MovingTalk => _movingTalkMode;

        public float MovingTalkWeight => Mathf.Clamp01(_movingTalkWeight);
        public float MovingTalkOverrideWeight => Mathf.Clamp01(_movingTalkOverrideWeight);
        public float MovingTalkBlendSeconds => Mathf.Max(0.05f, _movingTalkBlendSeconds);

        /// <summary>Lower clamp for the resolved proximity-expressiveness multiplier.</summary>
        public const float ProximityMultiplierMin = 0.8f;

        /// <summary>Upper clamp for the resolved proximity-expressiveness multiplier.</summary>
        public const float ProximityMultiplierMax = 1.15f;

        /// <summary>
        ///     Enables the speech-rhythm beat-gesture detector, which plays authored actions
        ///     tagged with the Beat or Emphatic cue. Off by default: it has nothing to play
        ///     until such a clip exists, and it deliberately has no procedural substitute
        ///     (a peer conversational-behavior module already supplies speech-rhythm motion of
        ///     its own, so substituting here would double it).
        /// </summary>
        public bool EnableBeatGestures => _enableBeatGestures;

        /// <summary>Minimum seconds between two fired beat gestures.</summary>
        public float BeatRefractorySeconds => Mathf.Max(0.05f, _beatRefractorySeconds);

        /// <summary>Multiplier applied to a beat gesture's onset-strength-derived weight.</summary>
        public float BeatWeightScale => Mathf.Clamp(_beatWeightScale, 0f, 1.5f);

        /// <summary>
        ///     Whether SPECULATIVE co-speech accents — derived from the speech-energy envelope
        ///     alone, with no semantic evidence — are produced in addition to the meant ones.
        ///     Off by default. Cues the character actually meant (classified from its final
        ///     transcript, or handed over by the referential-gesture director because no
        ///     authored clip carries them) are published for peer performers regardless of this
        ///     setting; this gates only the speculative tier.
        /// </summary>
        public bool EnableAdvancedCoSpeech => _enableAdvancedCoSpeech;
        public float CoSpeechMinimumAccentEnergy => Mathf.Clamp01(_coSpeechMinimumAccentEnergy);
        public float CoSpeechEmphasisDerivative => Mathf.Max(0.1f, _coSpeechEmphasisDerivative);
        public float CoSpeechAccentProbability => Mathf.Clamp01(_coSpeechAccentProbability);
        public float CoSpeechAccentRefractorySeconds => Mathf.Max(0.3f, _coSpeechAccentRefractorySeconds);
        public float CoSpeechPhraseEnergyMargin => Mathf.Clamp(_coSpeechPhraseEnergyMargin, 0.01f, 0.3f);
        public float CoSpeechPreparationSeconds => Mathf.Max(0.05f, _coSpeechPreparationSeconds);
        public float CoSpeechStrokeSeconds => Mathf.Max(0.05f, _coSpeechStrokeSeconds);
        public float CoSpeechReferentialHoldSeconds => Mathf.Max(0f, _coSpeechReferentialHoldSeconds);
        public float CoSpeechRetractionSeconds => Mathf.Max(0.1f, _coSpeechRetractionSeconds);

        /// <summary>
        ///     Enables referential gestures — the character gestures at what it says. On by
        ///     default, and it resolves either way: an authored action tagged
        ///     PalmToPlayer/HandToChest/IndicateObject/Enumerate is played directly, and a set
        ///     without one hands the cue to any peer performer registered on the character
        ///     (Convai Body Language performs it procedurally).
        /// </summary>
        public bool EnableReferentialGestures => _enableReferentialGestures;

        /// <summary>Minimum seconds between any two referential gestures.</summary>
        public float ReferentialGestureRefractorySeconds => Mathf.Clamp(_referentialGestureRefractorySeconds, 1f, 30f);

        /// <summary>Minimum seconds before the same referential-gesture class can fire again.</summary>
        public float ReferentialGestureClassCooldownSeconds => Mathf.Clamp(_referentialGestureClassCooldownSeconds, 1f, 60f);

        /// <summary>Multiplier applied to a referential gesture's weight.</summary>
        public float ReferentialGestureWeight => Mathf.Clamp(_referentialGestureWeight, 0f, 1.5f);

        /// <summary>
        ///     Upper bound (seconds) on the added latency an authored Outro Clip may introduce
        ///     when talk ends. Entries without an Outro Clip are unaffected.
        /// </summary>
        public float TalkOutroMaxSeconds => Mathf.Max(0.05f, _talkOutroMaxSeconds);

        /// <summary>
        ///     Persona scalar: multiplies talk-gesture expressiveness (weight cap,
        ///     variant-switch-on-loop probability, beat rate). 1 = today's default behavior.
        /// </summary>
        public float GestureLiveliness => Mathf.Clamp(_gestureLiveliness, 0f, 2f);

        /// <summary>
        ///     Persona scalar: stretches idle variant intervals and talk fade-ins.
        ///     1 = today's default behavior.
        /// </summary>
        public float Calmness => Mathf.Clamp(_calmness, 0f, 2f);

        /// <summary>Whether conversation-distance scales talk-gesture expressiveness.</summary>
        public bool ProximityExpressiveness => _proximityExpressiveness;

        /// <summary>Distance (m) at or below which <see cref="ProximityNearScale" /> applies fully.</summary>
        public float ProximityNearDistance => Mathf.Max(0.1f, _proximityNearDistance);

        /// <summary>Expressiveness multiplier at or below <see cref="ProximityNearDistance" />.</summary>
        public float ProximityNearScale => Mathf.Clamp(_proximityNearScale, ProximityMultiplierMin, ProximityMultiplierMax);

        /// <summary>Distance (m) at or beyond which <see cref="ProximityFarScale" /> applies fully.</summary>
        public float ProximityFarDistance => Mathf.Max(ProximityNearDistance + 0.1f, _proximityFarDistance);

        /// <summary>Expressiveness multiplier at or beyond <see cref="ProximityFarDistance" />.</summary>
        public float ProximityFarScale => Mathf.Clamp(_proximityFarScale, ProximityMultiplierMin, ProximityMultiplierMax);

        /// <summary>Seconds the proximity multiplier smooths over.</summary>
        public float ProximitySmoothingSeconds => Mathf.Max(0.05f, _proximitySmoothingSeconds);

        public float WalkSpeed => Mathf.Max(0.1f, _walkSpeed);

        /// <summary>Never below <see cref="WalkSpeed" /> — a jog slower than the walk inverts the blend thresholds.</summary>
        public float JogSpeed => Mathf.Max(Mathf.Max(0.1f, _jogSpeed), WalkSpeed);

        public float SpeedDampingSeconds => Mathf.Max(0.01f, _speedDampingSeconds);
        public float RateWarpMin => Mathf.Clamp(_rateWarpMin, 0.5f, 1f);

        /// <summary>Never below <see cref="RateWarpMin" />.</summary>
        public float RateWarpMax => Mathf.Max(Mathf.Clamp(_rateWarpMax, 1f, 1.5f), RateWarpMin);

        public float TurnInPlaceMinAngle => Mathf.Max(1f, _turnInPlaceMinAngle);

        /// <summary>Never below <see cref="TurnInPlaceMinAngle" /> + 5°.</summary>
        public float Turn180MinAngle => Mathf.Max(Mathf.Max(90f, _turn180MinAngle), TurnInPlaceMinAngle + 5f);

        public float MotionHandoffNormalizedTime => Mathf.Clamp(_motionHandoffNormalizedTime, 0.5f, 0.98f);
        public float LowSpeedStopFraction => Mathf.Clamp(_lowSpeedStopFraction, 0.1f, 1f);
        public float PlantedStopMinTravel => Mathf.Max(0f, _plantedStopMinTravel);

        /// <summary>Enables social-stepping proxemics — a short NavMesh reposition when the conversation partner crowds the character's personal space. Off by default.</summary>
        public bool EnableSocialSpacing => _enableSocialSpacing;

        /// <summary>Personal-space radius (m) that triggers a reposition when sustained.</summary>
        public float ComfortRadius => Mathf.Clamp(_comfortRadius, 0.3f, 2f);

        /// <summary>Seconds the conversant must continuously be inside <see cref="ComfortRadius" /> before a reposition triggers.</summary>
        public float ComfortHoldSeconds => Mathf.Clamp(_comfortHoldSeconds, 0.1f, 3f);

        /// <summary>Hard cap on social-spacing repositions per rolling minute.</summary>
        public int MaxRepositionsPerMinute => Mathf.Clamp(Mathf.RoundToInt(_maxRepositionsPerMinute), 1, 10);

        /// <summary>Enables emotion-driven walk/jog speed modulation. Off by default.</summary>
        public bool EnableEmotionalGait => _enableEmotionalGait;

        /// <summary>Maximum fractional commanded-speed change from emotion, both directions.</summary>
        public float EmotionGaitRange => Mathf.Clamp(_emotionGaitRange, 0f, 0.3f);

        /// <summary>Whether pointing also triggers a brief glance at the point target.</summary>
        public bool EnablePointGlance => _enablePointGlance;

        /// <summary>Hold duration (seconds) of the point-glance before gaze returns to the player.</summary>
        public float PointGlanceSeconds => Mathf.Clamp(_pointGlanceSeconds, 0.2f, 3f);

        /// <summary>Whether a normalized locomotion-effort signal is published as IExertionSource.</summary>
        public bool PublishExertion => _publishExertion;

        /// <summary>Seconds of sustained full-run effort it takes exertion to climb from 0 to 1.</summary>
        public float ExertionRiseSeconds => Mathf.Clamp(_exertionRiseSeconds, 0.5f, 20f);

        /// <summary>Seconds it takes exertion to decay from 1 back to 0 at rest.</summary>
        public float ExertionRecoverySeconds => Mathf.Clamp(_exertionRecoverySeconds, 0.5f, 20f);

        /// <summary>
        ///     Enables ambient idle activities, which play authored actions tagged Ambient. Off
        ///     by default: it has nothing to play until such a clip exists, and an ambient
        ///     activity is a whole authored performance with no meaningful procedural substitute.
        /// </summary>
        public bool EnableAmbientActivities => _enableAmbientActivities;

        /// <summary>Seconds Idle must persist before the first ambient activity may fire.</summary>
        public float AmbientStartDelaySeconds => Mathf.Clamp(_ambientStartDelaySeconds, 3f, 120f);

        /// <summary>Mean seconds between ambient activities once armed, ±40% jitter.</summary>
        public float AmbientIntervalSeconds => Mathf.Clamp(_ambientIntervalSeconds, 5f, 300f);

        /// <summary>Proximity gate (m): no new ambient activity starts while the player is this close or closer.</summary>
        public float AmbientSuppressDistance => Mathf.Clamp(_ambientSuppressDistance, 1f, 20f);

        public bool EnableTurnInPlace => _enableTurnInPlace;
        public bool EnableDirectionalStarts => _enableDirectionalStarts;
        public bool EnablePlantedStops => _enablePlantedStops;
        public bool PlantedStopsWhileWalking => _plantedStopsWhileWalking;
        public bool EnableSpeedChangeClips => _enableSpeedChangeClips;
        public bool EnableSpeedWarping => _enableSpeedWarping;
        public bool EnableFootIK => _enableFootIK;

        public AnimTraceVerbosity TraceVerbosity => _traceVerbosity;
        public float FirehoseIntervalSeconds => Mathf.Max(0.05f, _firehoseIntervalSeconds);

        /// <summary>Resolves an entry override (−1 = unset) against a config default.</summary>
        public static float ResolveOverride(float overrideSeconds, float defaultSeconds) =>
            overrideSeconds >= 0f ? overrideSeconds : defaultSeconds;

        internal float ResolveTalkLayerWeightScale(bool hasSpeechEnergy, float speechEnergy)
        {
            float overlayWeight = TalkOverlayWeight;
            if (!_useSpeechEnergy || !hasSpeechEnergy)
                return overlayWeight;

            float lowEnergyWeight = Mathf.Min(TalkWeightAtLowEnergy, overlayWeight);
            return Mathf.Lerp(lowEnergyWeight, overlayWeight, Mathf.Clamp01(speechEnergy));
        }

        /// <summary>One-line feature summary logged at startup for diagnosability.</summary>
        public string DescribeFeatures() =>
            $"turnInPlace={_enableTurnInPlace} directionalStarts={_enableDirectionalStarts} " +
            $"plantedStops={_enablePlantedStops} plantedStopsWalk={_plantedStopsWhileWalking} " +
            $"speedChangeClips={_enableSpeedChangeClips} speedWarping={_enableSpeedWarping} " +
            $"footIK={_enableFootIK} advancedCoSpeech={_enableAdvancedCoSpeech} trace={_traceVerbosity}";

        /// <summary>
        ///     Applies the same clamps the corresponding getters enforce directly to the
        ///     serialized fields, so a build (where <c>[Min]</c>/<c>[Range]</c>/<c>OnValidate</c>
        ///     never run) still ends up with a self-consistent asset in memory. Shared by
        ///     <see cref="OnValidate" /> (editor, silent) and <see cref="ValidateForRuntime" />
        ///     (runtime, reported) so the two can never drift apart.
        /// </summary>
        /// <param name="corrections">
        ///     When non-null, receives one human-readable line per out-of-range field.
        ///     Pass null (as <see cref="OnValidate" /> does) to clamp silently.
        /// </param>
        /// <param name="mutate">
        ///     True writes the corrected values back to the serialized fields — correct in the
        ///     editor, where <see cref="OnValidate" /> is repairing the asset the user is editing.
        ///     False only reports. <see cref="ValidateForRuntime" /> passes false **deliberately**:
        ///     ScriptableObject field writes made during Play Mode survive exiting it, so a
        ///     mutating runtime validation would silently rewrite the customer's config asset just
        ///     because they pressed Play. Every getter already clamps, so the runtime is safe
        ///     without touching the asset at all.
        /// </param>
        private void ApplyBoundsClamps(List<string> corrections, bool mutate)
        {
            ClampMin(corrections, "Idle Crossfade Seconds", ref _idleCrossfadeSeconds, 0.01f, mutate);
            ClampMin(corrections, "Talk Fade In Seconds", ref _talkFadeInSeconds, 0.01f, mutate);
            ClampMin(corrections, "Talk Fade Out Seconds", ref _talkFadeOutSeconds, 0.01f, mutate);
            ClampMin(corrections, "Action Fade In Seconds", ref _actionFadeInSeconds, 0.01f, mutate);
            ClampMin(corrections, "Action Fade Out Seconds", ref _actionFadeOutSeconds, 0.01f, mutate);
            ClampMin(corrections, "Pointing Fade Seconds", ref _pointingFadeSeconds, 0.01f, mutate);
            ClampMin(corrections, "Locomotion Crossfade Seconds", ref _locomotionCrossfadeSeconds, 0.01f, mutate);
            ClampMin(corrections, "Talk Variant Crossfade Seconds", ref _talkVariantCrossfadeSeconds, 0.01f, mutate);
            ClampMin(corrections, "Speed Damping Seconds", ref _speedDampingSeconds, 0.01f, mutate);
            ClampMin(corrections, "Turn In Place Min Angle", ref _turnInPlaceMinAngle, 1f, mutate);
            ClampMin(corrections, "Turn 180 Min Angle", ref _turn180MinAngle, 90f, mutate);
            ClampRange(corrections, "Motion Handoff Normalized Time", ref _motionHandoffNormalizedTime, 0.5f, 0.98f, mutate);
            ClampRange(corrections, "Low Speed Stop Fraction", ref _lowSpeedStopFraction, 0.1f, 1f, mutate);
            ClampMin(corrections, "Planted Stop Min Travel", ref _plantedStopMinTravel, 0f, mutate);
            ClampMin(corrections, "Firehose Interval Seconds", ref _firehoseIntervalSeconds, 0.05f, mutate);

            ClampMin(corrections, "Walk Speed", ref _walkSpeed, 0.1f, mutate);
            ClampMin(corrections, "Jog Speed", ref _jogSpeed, 0.1f, mutate);
            if (_jogSpeed < _walkSpeed)
            {
                corrections?.Add(
                    $"Jog Speed ({_jogSpeed:0.00}) is below Walk Speed ({_walkSpeed:0.00}); " +
                    $"the runtime uses {_walkSpeed:0.00}. A jog slower than the walk inverts the movement blend.");
                if (mutate) _jogSpeed = _walkSpeed;
            }

            ClampRange(corrections, "Rate Warp Min", ref _rateWarpMin, 0.5f, 1f, mutate);
            ClampRange(corrections, "Rate Warp Max", ref _rateWarpMax, 1f, 1.5f, mutate);
            if (_rateWarpMax < _rateWarpMin)
            {
                corrections?.Add(
                    $"Rate Warp Max ({_rateWarpMax:0.00}) is below Rate Warp Min ({_rateWarpMin:0.00}); " +
                    $"the runtime uses {_rateWarpMin:0.00}.");
                if (mutate) _rateWarpMax = _rateWarpMin;
            }

            float minTurn180 = _turnInPlaceMinAngle + 5f;
            if (_turn180MinAngle < minTurn180)
            {
                corrections?.Add(
                    $"Turn 180 Min Angle ({_turn180MinAngle:0.00}) is below Turn In Place Min Angle + 5° " +
                    $"({minTurn180:0.00}); the runtime uses {minTurn180:0.00}.");
                if (mutate) _turn180MinAngle = minTurn180;
            }
        }

        private static void ClampMin(
            List<string> corrections, string label, ref float field, float min, bool mutate)
        {
            if (field >= min) return;

            corrections?.Add($"{label} ({field:0.00}) is below the minimum ({min:0.00}); the runtime uses {min:0.00}.");
            if (mutate) field = min;
        }

        private static void ClampRange(
            List<string> corrections, string label, ref float field, float min, float max, bool mutate)
        {
            float clamped = Mathf.Clamp(field, min, max);
            if (Mathf.Approximately(clamped, field)) return;

            corrections?.Add(
                $"{label} ({field:0.00}) is outside [{min:0.00}, {max:0.00}]; the runtime uses {clamped:0.00}.");
            if (mutate) field = clamped;
        }

        /// <summary>
        ///     Runtime counterpart of <see cref="OnValidate" />: reports every field a build would
        ///     otherwise leave out of range, including a degenerate <see cref="BlendCurve" />,
        ///     so the caller can surface one warning naming all of them instead of the character
        ///     quietly misbehaving. Called once from <c>BuildRuntime</c>.
        /// </summary>
        /// <remarks>
        ///     Deliberately **read-only**. The getters that back these settings all clamp, so the
        ///     runtime never needs the asset repaired — and repairing it would be actively harmful:
        ///     ScriptableObject field writes made in Play Mode persist after exiting it, so this
        ///     would rewrite the customer's asset as a side effect of pressing Play.
        /// </remarks>
        internal BodyAnimationConfigCorrections ValidateForRuntime()
        {
            var corrections = new List<string>();
            ApplyBoundsClamps(corrections, mutate: false);

            if (!IsBlendCurveUsable(_blendCurve))
            {
                corrections.Add(
                    "Blend Curve is missing or has fewer than two keys; the runtime uses a default 0→1 ease curve. " +
                    "An unusable curve would otherwise evaluate to 0 and hold every layer's weight at zero.");
            }

            return new BodyAnimationConfigCorrections(corrections);
        }

        private void OnValidate()
        {
            ApplyBoundsClamps(null, mutate: true);

            _idleVariantIntervalMax = Mathf.Max(_idleVariantIntervalMax, _idleVariantIntervalMin);
            _movingTalkWeight = Mathf.Clamp01(_movingTalkWeight);
            _movingTalkOverrideWeight = Mathf.Clamp01(_movingTalkOverrideWeight);
            _movingTalkBlendSeconds = Mathf.Max(0.05f, _movingTalkBlendSeconds);
            _talkReleaseDelaySeconds = Mathf.Max(0f, _talkReleaseDelaySeconds);
            _talkReleasePlaybackSpeed = Mathf.Clamp01(_talkReleasePlaybackSpeed);
            _listenFadeInSeconds = Mathf.Max(0.01f, _listenFadeInSeconds);
            _thinkingEnterDelaySeconds = Mathf.Max(0f, _thinkingEnterDelaySeconds);
            _interruptedFreezeSeconds = Mathf.Max(0f, _interruptedFreezeSeconds);
            _interruptedReleaseScale = Mathf.Clamp(_interruptedReleaseScale, 0.05f, 1f);
            _beatRefractorySeconds = Mathf.Max(0.05f, _beatRefractorySeconds);
            _beatWeightScale = Mathf.Clamp(_beatWeightScale, 0f, 1.5f);
            _referentialGestureRefractorySeconds = Mathf.Clamp(_referentialGestureRefractorySeconds, 1f, 30f);
            _referentialGestureClassCooldownSeconds = Mathf.Clamp(_referentialGestureClassCooldownSeconds, 1f, 60f);
            _referentialGestureWeight = Mathf.Clamp(_referentialGestureWeight, 0f, 1.5f);
            _proximityNearDistance = Mathf.Max(0.1f, _proximityNearDistance);
            _proximityFarDistance = Mathf.Max(_proximityNearDistance + 0.1f, _proximityFarDistance);
            _proximityNearScale = Mathf.Clamp(_proximityNearScale, ProximityMultiplierMin, ProximityMultiplierMax);
            _proximityFarScale = Mathf.Clamp(_proximityFarScale, ProximityMultiplierMin, ProximityMultiplierMax);
            _proximitySmoothingSeconds = Mathf.Max(0.05f, _proximitySmoothingSeconds);
            _talkOutroMaxSeconds = Mathf.Max(0.05f, _talkOutroMaxSeconds);
            _gestureLiveliness = Mathf.Clamp(_gestureLiveliness, 0f, 2f);
            _calmness = Mathf.Clamp(_calmness, 0f, 2f);
            _pointGlanceSeconds = Mathf.Clamp(_pointGlanceSeconds, 0.2f, 3f);
            _exertionRiseSeconds = Mathf.Clamp(_exertionRiseSeconds, 0.5f, 20f);
            _exertionRecoverySeconds = Mathf.Clamp(_exertionRecoverySeconds, 0.5f, 20f);
            _ambientStartDelaySeconds = Mathf.Clamp(_ambientStartDelaySeconds, 3f, 120f);
            _ambientIntervalSeconds = Mathf.Clamp(_ambientIntervalSeconds, 5f, 300f);
            _ambientSuppressDistance = Mathf.Clamp(_ambientSuppressDistance, 1f, 20f);
            _comfortRadius = Mathf.Clamp(_comfortRadius, 0.3f, 2f);
            _comfortHoldSeconds = Mathf.Clamp(_comfortHoldSeconds, 0.1f, 3f);
            _maxRepositionsPerMinute = Mathf.Clamp(_maxRepositionsPerMinute, 1f, 10f);
            _emotionGaitRange = Mathf.Clamp(_emotionGaitRange, 0f, 0.3f);
            _actionLayerWeight = Mathf.Clamp01(_actionLayerWeight);
            _pointingLayerWeight = Mathf.Clamp01(_pointingLayerWeight);
            _beatLayerWeight = Mathf.Clamp01(_beatLayerWeight);
        }

        /// <summary>Runtime default when no config asset is assigned.</summary>
        public static ConvaiBodyAnimationConfig CreateDefault()
        {
            ConvaiBodyAnimationConfig instance = CreateInstance<ConvaiBodyAnimationConfig>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }
    }
}
