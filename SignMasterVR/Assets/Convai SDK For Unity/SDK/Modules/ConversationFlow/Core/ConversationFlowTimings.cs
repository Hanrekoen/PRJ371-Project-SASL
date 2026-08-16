namespace Convai.Modules.ConversationFlow.Core
{
    /// <summary>
    ///     Timing parameters consumed by <see cref="ConversationFlowStateMachine" />.
    /// </summary>
    /// <remarks>
    ///     Passed by <c>in</c> to the state machine, typically derived from a
    ///     <c>ConvaiConversationFlowProfile</c> asset but trivially constructible in tests.
    /// </remarks>
    public readonly struct ConversationFlowTimings
    {
        /// <summary>Crossfade duration between any two states, in seconds.</summary>
        public float TransitionDuration { get; }

        /// <summary>
        ///     Minimum time (seconds) to stay in <see cref="Semantics.DialogueState.Thinking" />
        ///     after the player commits a turn. Shorter values make the character feel snappy,
        ///     longer ones more thoughtful.
        /// </summary>
        public float ThinkingMinHold { get; }

        /// <summary>
        ///     Maximum time (seconds) the character may remain in
        ///     <see cref="Semantics.DialogueState.Thinking" /> while waiting for a reply. After
        ///     this timeout the state decays back to <see cref="Semantics.DialogueState.Attending" />.
        /// </summary>
        public float ThinkingMaxHold { get; }

        /// <summary>
        ///     Time (seconds) the character briefly holds <see cref="Semantics.DialogueState.Attending" />
        ///     after the player finishes speaking but before committing a turn.
        /// </summary>
        public float AttendingGracePeriod { get; }

        /// <summary>
        ///     Time (seconds) to dwell in <see cref="Semantics.DialogueState.Settling" /> after
        ///     the character stops speaking before returning to an engaged
        ///     <see cref="Semantics.DialogueState.Attending" /> stance.
        /// </summary>
        public float SettlingDuration { get; }

        /// <summary>
        ///     Continuous inactivity time (seconds) required before the conversation fully
        ///     cools back to <see cref="Semantics.DialogueState.Idle" />. Any meaningful turn
        ///     activity resets this timer.
        /// </summary>
        public float IdleReturnDelay { get; }

        /// <summary>
        ///     Time (seconds) the character freezes in <see cref="Semantics.DialogueState.Interrupted" />
        ///     before returning to <see cref="Semantics.DialogueState.Attending" />.
        /// </summary>
        public float InterruptedFreezeDuration { get; }

        /// <summary>
        ///     Default energy level (0..1) for <see cref="Semantics.DialogueState.Speaking" />
        ///     if no other signal overrides it. Controls body-language intensity.
        /// </summary>
        public float SpeakingBaseEnergy { get; }

        public ConversationFlowTimings(
            float transitionDuration,
            float thinkingMinHold,
            float thinkingMaxHold,
            float attendingGracePeriod,
            float settlingDuration,
            float idleReturnDelay,
            float interruptedFreezeDuration,
            float speakingBaseEnergy)
        {
            TransitionDuration = transitionDuration < 0f ? 0f : transitionDuration;
            ThinkingMinHold = thinkingMinHold < 0f ? 0f : thinkingMinHold;
            ThinkingMaxHold = thinkingMaxHold < thinkingMinHold ? thinkingMinHold : thinkingMaxHold;
            AttendingGracePeriod = attendingGracePeriod < 0f ? 0f : attendingGracePeriod;
            SettlingDuration = settlingDuration < 0f ? 0f : settlingDuration;
            IdleReturnDelay = idleReturnDelay < 0f ? 0f : idleReturnDelay;
            InterruptedFreezeDuration = interruptedFreezeDuration < 0f ? 0f : interruptedFreezeDuration;
            SpeakingBaseEnergy = speakingBaseEnergy < 0f ? 0f :
                speakingBaseEnergy > 1f ? 1f : speakingBaseEnergy;
        }

        /// <summary>Sensible defaults for the typical conversational preset.</summary>
        public static ConversationFlowTimings Default => new(
            transitionDuration: 0.25f,
            thinkingMinHold: 0.25f,
            thinkingMaxHold: 2.5f,
            attendingGracePeriod: 0.3f,
            settlingDuration: 0.6f,
            idleReturnDelay: 60f,
            interruptedFreezeDuration: 0.25f,
            speakingBaseEnergy: 0.6f);
    }
}
