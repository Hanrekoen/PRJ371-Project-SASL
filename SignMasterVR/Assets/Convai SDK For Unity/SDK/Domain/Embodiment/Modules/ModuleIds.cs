namespace Convai.Domain.Embodiment.Modules
{
    /// <summary>
    ///     Canonical module identifiers used by embodiment receivers when registering profiles
    ///     with the host context. Centralizing the strings here eliminates the silent
    ///     dispatch failures caused by typos in per-component literals.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Values are <see langword="const"/> so external references continue to compile
    ///         against the literal strings; updating a constant is a binary-compatible
    ///         operation as long as the literal value is preserved.
    ///     </para>
    ///     <para>
    ///         Each id is the routing key written into a
    ///         <c>ConvaiEmbodimentPreset</c> profile slot. Renaming a literal here invalidates
    ///         every preset asset referencing the old key, so prefer additive changes.
    ///     </para>
    /// </remarks>
    public static class ModuleIds
    {
        /// <summary>Routes <c>ConvaiBodyAnimationProfile</c> to <c>ConvaiBodyAnimationController</c>.</summary>
        public const string BodyAnimation = "convai.body-animation";

        /// <summary>Routes <c>ConvaiBodyLanguageProfile</c> to <c>ConvaiBodyLanguageController</c>.</summary>
        public const string BodyLanguage = "convai.body-language";

        /// <summary>Routes <c>ConvaiConversationFlowProfile</c> to <c>ConvaiConversationFlowController</c>.</summary>
        public const string ConversationFlow = "convai.conversation-flow";

        /// <summary>Routes <c>ConvaiEmotionProfile</c> to <c>ConvaiEmotionController</c>.</summary>
        public const string Emotion = "convai.emotion";

        /// <summary>Routes <c>ConvaiGazeProfile</c> to <c>ConvaiGazeController</c>.</summary>
        public const string Gaze = "convai.gaze";
    }
}
