namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     Canonical dialogue-phase states that downstream embodiment modules react to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The state model is intentionally richer than a coarse Idle/Listen/Talk split.
    ///         Each state represents a distinct beat in a natural conversation, which allows
    ///         gaze, body language, emotion, and facial animation modules to produce believable
    ///         human-like behavior without hard-coded overrides.
    ///     </para>
    ///     <para>
    ///         States are authored by <c>Convai.Modules.ConversationFlow</c> and consumed via
    ///         <see cref="IConversationFlowSource" />. Downstream modules MUST NOT redefine or
    ///         widen this enum: if new beats are needed they should be modeled here.
    ///     </para>
    /// </remarks>
    public enum DialogueState
    {
        /// <summary>
        ///     Character is not engaged in a turn. Idle micro-behavior (weight shift,
        ///     ambient gaze) is dominant.
        /// </summary>
        Idle = 0,

        /// <summary>
        ///     The player is speaking and the character is actively listening. Gaze commits to
        ///     the player, body language tends to lean in, emotion is held near neutral.
        /// </summary>
        Listening = 1,

        /// <summary>
        ///     The character is orienting toward a focus target (e.g. the player initiating a
        ///     turn) but the player has not started speaking yet, or the character is holding a
        ///     conversationally engaged stance between beats before cooling back to ambient idle.
        /// </summary>
        Attending = 2,

        /// <summary>
        ///     The player has finished speaking, the character has not yet replied. Models the
        ///     natural "cognitive pause": gaze can break, micro saccades increase, and brow lifts.
        /// </summary>
        Thinking = 3,

        /// <summary>
        ///     The character is currently speaking. Lip sync owns the mouth, emotion and gesture
        ///     intensity scale with speech energy.
        /// </summary>
        Speaking = 4,

        /// <summary>
        ///     Short reaction beat after a significant event (e.g. unexpected player input,
        ///     high-intensity emotion arrival). Allows modules to play a one-shot without
        ///     disturbing the primary state.
        /// </summary>
        Reacting = 5,

        /// <summary>
        ///     The character was interrupted mid-turn. Expressed as a brief freeze + quick
        ///     settle before transitioning back to <see cref="Attending" />.
        /// </summary>
        Interrupted = 6,

        /// <summary>
        ///     Post-turn cooldown: character has finished speaking, gaze softens, emotion
        ///     gradually decays back to its resting tone before handing off to
        ///     <see cref="Attending" />.
        /// </summary>
        Settling = 7
    }
}
