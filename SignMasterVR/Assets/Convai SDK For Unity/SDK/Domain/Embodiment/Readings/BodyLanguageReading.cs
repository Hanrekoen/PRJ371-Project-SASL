using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    ///     Immutable snapshot of the character's current nonverbal body-language state as
    ///     published by the Body Language module through
    ///     <see cref="Interfaces.IBodyLanguageSource" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mirrors <see cref="GazeReading" />'s role: this expresses <em>what</em> the body
    ///         is currently doing (which dialogue state it is acting on, how open/tense the
    ///         posture is, whether a scripted head gesture or gesture cue is in flight) so other
    ///         modules and diagnostics can read it without depending on the Body Language module
    ///         assembly. Solvers still turn this into bone writes internally — consumers should
    ///         treat the reading as read-only telemetry, never drive the body through it (use
    ///         <c>ConvaiBodyLanguageController.Nod</c>/<c>PulseGesture</c> for that).
    ///     </para>
    ///     <para>
    ///         Engine-free by design (Domain layer): only primitives and Domain enums, no
    ///         <c>UnityEngine</c> object references, so it can be constructed and asserted on in
    ///         plain EditMode tests without a scene.
    ///     </para>
    /// </remarks>
    public readonly struct BodyLanguageReading
    {
        /// <summary>Dialogue state the policy engine is currently acting on.</summary>
        public DialogueState DialogueState { get; }

        /// <summary>Posture openness, -1 (closed/guarded) .. 1 (open), spring-settled current value.</summary>
        public float PostureOpenness { get; }

        /// <summary>Sagittal lean, -1 (leaning back) .. 1 (leaning in), spring-settled current value.</summary>
        public float PostureLean { get; }

        /// <summary>Shoulder/torso tension, -1..1, spring-settled current value.</summary>
        public float ShoulderTension { get; }

        /// <summary>Breath oscillator phase, normalized to <c>[0, 1)</c> (0 = cycle start).</summary>
        public float BreathPhase { get; }

        /// <summary>Current gesture-channel suppression reported by the conversational gesture performer.</summary>
        public GestureSuppression Suppression { get; }

        /// <summary>Whether a scripted head-gesture program (Nod/Shake/Tilt) is currently playing.</summary>
        public bool HasActiveHeadGesture { get; }

        /// <summary>
        ///     The kind of the currently playing head-gesture program. Only meaningful when
        ///     <see cref="HasActiveHeadGesture" /> is <c>true</c>; <see cref="HeadGestureKind.Nod" />
        ///     (value 0) otherwise.
        /// </summary>
        public HeadGestureKind ActiveHeadGestureKind { get; }

        /// <summary>The last semantic gesture cue kind attempted, whether accepted or refused.</summary>
        public GestureCueKind LastGestureCueKind { get; }

        /// <summary>
        ///     Stance director's current lateral pelvis weight-shift value, -1 (left) .. 1
        ///     (right). 0 when the weight-shift program is
        ///     disabled or not yet scheduled.
        /// </summary>
        public float WeightShift { get; }

        /// <summary>
        ///     This tick's effective expressiveness 0..1 — the runtime override
        ///     when set via <c>ConvaiBodyLanguageController.Expressiveness</c>, otherwise the
        ///     profile's own resolved value.
        /// </summary>
        public float Expressiveness { get; }

        /// <summary>The one-shot bodily reaction currently playing, <see cref="ReactionKind.None" /> when idle.</summary>
        public ReactionKind ActiveReaction { get; }

        /// <summary>
        ///     Full constructor — includes <see cref="WeightShift" />,
        ///     <see cref="Expressiveness" />, and <see cref="ActiveReaction" />.
        /// </summary>
        public BodyLanguageReading(
            DialogueState dialogueState,
            float postureOpenness,
            float postureLean,
            float shoulderTension,
            float breathPhase,
            GestureSuppression suppression,
            bool hasActiveHeadGesture,
            HeadGestureKind activeHeadGestureKind,
            GestureCueKind lastGestureCueKind,
            float weightShift,
            float expressiveness,
            ReactionKind activeReaction)
        {
            DialogueState = dialogueState;
            PostureOpenness = postureOpenness;
            PostureLean = postureLean;
            ShoulderTension = shoulderTension;
            BreathPhase = breathPhase;
            Suppression = suppression;
            HasActiveHeadGesture = hasActiveHeadGesture;
            ActiveHeadGestureKind = activeHeadGestureKind;
            LastGestureCueKind = lastGestureCueKind;
            WeightShift = weightShift;
            Expressiveness = expressiveness;
            ActiveReaction = activeReaction;
        }

        /// <summary>
        ///     Overload for callers that describe neither weight shift, expressiveness nor an active
        ///     reaction — delegates to the full constructor with <see cref="WeightShift" /> = 0,
        ///     <see cref="Expressiveness" /> = 0.5 (Natural), and <see cref="ActiveReaction" /> =
        ///     <see cref="ReactionKind.None" />.
        /// </summary>
        public BodyLanguageReading(
            DialogueState dialogueState,
            float postureOpenness,
            float postureLean,
            float shoulderTension,
            float breathPhase,
            GestureSuppression suppression,
            bool hasActiveHeadGesture,
            HeadGestureKind activeHeadGestureKind,
            GestureCueKind lastGestureCueKind)
            : this(
                dialogueState, postureOpenness, postureLean, shoulderTension, breathPhase, suppression,
                hasActiveHeadGesture, activeHeadGestureKind, lastGestureCueKind,
                0f, 0.5f, ReactionKind.None)
        {
        }

        /// <summary>Disengaged/at-rest reading — no active gesture, neutral posture, Idle state.</summary>
        public static BodyLanguageReading None => new(
            DialogueState.Idle, 0f, 0f, 0f, 0f,
            GestureSuppression.None, false, HeadGestureKind.Nod, GestureCueKind.None,
            0f, 0.5f, ReactionKind.None);
    }
}
