namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Bridge contract between the LipSync module and the ConversationFlow state machine.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         LipSync is the authoritative source of truth for whether the character's mouth
    ///         is currently producing speech (it owns the audio gate and the packed frame
    ///         buffer). ConversationFlow consumes this flag to confirm the
    ///         <c>DialogueState.Speaking</c> transition rather than relying purely on
    ///         <c>CharacterSpeechStateChanged</c>, which can fire before audio is actually
    ///         playing on WebGL.
    ///     </para>
    ///     <para>
    ///         Implementations MUST be safe to read every frame; the value is expected to be
    ///         maintained by LipSync's per-frame tick.
    ///     </para>
    /// </remarks>
    internal interface IDialoguePhaseProvider
    {
        /// <summary>
        ///     <c>true</c> when LipSync is actively playing speech frames for the owning
        ///     character.
        /// </summary>
        bool IsSpeechActive { get; }

        /// <summary>
        ///     Smoothed speech blend factor in <c>[0, 1]</c>. <c>0</c> = idle,
        ///     <c>1</c> = fully talking. Used by composition profiles that weight idle vs
        ///     speaking facial regions.
        /// </summary>
        float SpeechBlendFactor { get; }
    }
}
