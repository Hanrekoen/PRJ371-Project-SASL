namespace Convai.Modules.ConversationFlow.Core
{
    /// <summary>
    ///     Immutable snapshot of the raw per-frame signals the conversation-flow state
    ///     machine uses to decide transitions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ConversationFlowSignalAggregator" /> folds domain events into this
    ///         shape so the state machine itself (pure POCO) can be edit-mode tested without
    ///         a Unity scene.
    ///     </para>
    /// </remarks>
    public readonly struct ConversationFlowInputs
    {
        /// <summary>Character has signaled ready-to-interact via <c>CharacterReady</c>.</summary>
        public bool IsCharacterReady { get; }

        /// <summary>VAD reports the player is currently producing voice.</summary>
        public bool IsPlayerSpeaking { get; }

        /// <summary>
        ///     At least one final player transcript has been committed since the last
        ///     character turn started; cleared when the character begins speaking.
        /// </summary>
        public bool HasPendingPlayerTurn { get; }

        /// <summary>Character is currently producing audio (as reported by transport).</summary>
        public bool IsCharacterSpeaking { get; }

        /// <summary>LipSync is actually playing speech frames this moment.</summary>
        public bool IsLipSyncSpeaking { get; }

        /// <summary>A turn-completed signal with <c>wasInterrupted=true</c> arrived recently.</summary>
        public bool WasRecentlyInterrupted { get; }

        /// <summary>A turn-completed signal arrived in the current frame.</summary>
        public bool TurnJustCompleted { get; }

        /// <summary>
        ///     The character is carrying out something it was asked to do. Counts as engagement:
        ///     running an errand for the player is being with the player, even in silence.
        /// </summary>
        public bool IsPerformingAction { get; }

        public ConversationFlowInputs(
            bool isCharacterReady,
            bool isPlayerSpeaking,
            bool hasPendingPlayerTurn,
            bool isCharacterSpeaking,
            bool isLipSyncSpeaking,
            bool wasRecentlyInterrupted,
            bool turnJustCompleted,
            bool isPerformingAction = false)
        {
            IsPerformingAction = isPerformingAction;
            IsCharacterReady = isCharacterReady;
            IsPlayerSpeaking = isPlayerSpeaking;
            HasPendingPlayerTurn = hasPendingPlayerTurn;
            IsCharacterSpeaking = isCharacterSpeaking;
            IsLipSyncSpeaking = isLipSyncSpeaking;
            WasRecentlyInterrupted = wasRecentlyInterrupted;
            TurnJustCompleted = turnJustCompleted;
        }
    }
}
