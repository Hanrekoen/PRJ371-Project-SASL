using Convai.Domain.Embodiment.Semantics;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    ///     Immutable snapshot of the character's current dialogue-phase state exposed by
    ///     <see cref="IConversationFlowSource" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reading always describes a blended transition between two states so that
    ///         consumers can cross-fade smoothly instead of snapping. When no transition is in
    ///         flight, <see cref="Primary" /> and <see cref="BlendTo" /> are equal and
    ///         <see cref="BlendWeight" /> is <c>0</c>.
    ///     </para>
    ///     <para>
    ///         Passed by <c>in</c> parameter in hot paths to avoid the 16+ byte copy cost.
    ///     </para>
    /// </remarks>
    public readonly struct DialogueStateReading
    {
        /// <summary>Current authoritative dialogue state.</summary>
        public DialogueState Primary { get; }

        /// <summary>
        ///     The state being blended toward. Equals <see cref="Primary" /> when not
        ///     transitioning.
        /// </summary>
        public DialogueState BlendTo { get; }

        /// <summary>
        ///     Linear blend weight from <see cref="Primary" /> to <see cref="BlendTo" /> in
        ///     <c>[0, 1]</c>. A value of <c>0</c> means the character is fully in
        ///     <see cref="Primary" />; <c>1</c> means the transition is complete.
        /// </summary>
        public float BlendWeight { get; }

        /// <summary>Seconds the character has been in <see cref="Primary" />.</summary>
        public float TimeInState { get; }

        /// <summary>
        ///     Normalized energy level in <c>[0, 1]</c> used by gesture schedulers and
        ///     micro-behavior intensity. Higher values correspond to more
        ///     expressive beats (e.g. <see cref="DialogueState.Speaking" /> loud passages,
        ///     <see cref="DialogueState.Reacting" />).
        /// </summary>
        public float EnergyLevel { get; }

        public DialogueStateReading(
            DialogueState primary,
            DialogueState blendTo,
            float blendWeight,
            float timeInState,
            float energyLevel)
        {
            Primary = primary;
            BlendTo = blendTo;
            BlendWeight = blendWeight < 0f ? 0f : blendWeight > 1f ? 1f : blendWeight;
            TimeInState = timeInState < 0f ? 0f : timeInState;
            EnergyLevel = energyLevel < 0f ? 0f : energyLevel > 1f ? 1f : energyLevel;
        }

        /// <summary>Reading for a steady-state character in <see cref="DialogueState.Idle" />.</summary>
        public static DialogueStateReading Idle => new(DialogueState.Idle, DialogueState.Idle, 0f, 0f, 0f);

        /// <summary>
        ///     Returns <c>true</c> when a transition is actively being blended (non-zero weight
        ///     toward a different state).
        /// </summary>
        public bool IsTransitioning => Primary != BlendTo && BlendWeight > 0f;
    }
}
