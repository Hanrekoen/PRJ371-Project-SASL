namespace Convai.Domain.Emotion
{
    /// <summary>
    ///     Client-controlled emotion detection mode. Resolved at connect time to decide whether
    ///     and how the backend works out what a character is feeling.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The mode is authoritative: it fully determines the <c>emotion_config</c> sent on
    ///         connect, overriding any backend emotion setting. A character with no client-side mode
    ///         is treated as <see cref="Off" />.
    ///     </para>
    ///     <para>
    ///         <b>Declaration order is not presentation order.</b> These members are ordered by their
    ///         serialized value, which is part of the asset format and cannot change. A surface that
    ///         offers the modes as a list must map between the enum value and its own option index
    ///         explicitly — using an enum's declaration index as a list index shipped the two
    ///         providers swapped once already.
    ///     </para>
    /// </remarks>
    public enum EmotionDetectionMode
    {
        /// <summary>
        ///     No emotion detection. No <c>emotion_config</c> is sent on connect, so no emotion
        ///     updates arrive and the character never reacts emotionally.
        /// </summary>
        Off = 0,

        /// <summary>
        ///     Reads the whole reply, together with the character's backstory, to produce one
        ///     emotion for it.
        /// </summary>
        /// <remarks>
        ///     Because it weighs the reply as a whole rather than matching wording, the emotion fits
        ///     what was actually meant and holds up across languages. The trade-off is frequency:
        ///     one reading per reply instead of several, and on a long reply it arrives near the end
        ///     of it. Prefer this when the character's feelings should be right rather than early —
        ///     characters speaking a language other than English, and anything where a wrong
        ///     expression costs more than a late one.
        /// </remarks>
        Llm = 1,

        /// <summary>
        ///     Reads each part of the reply as it arrives, matching wording against an emotion
        ///     lexicon.
        /// </summary>
        /// <remarks>
        ///     The face can change more than once within a single reply and reacts almost
        ///     immediately. The trade-off is accuracy: it matches wording rather than meaning, and
        ///     the lexicon is built on English, so a character speaking another language reads less
        ///     accurately. Prefer this when responsiveness matters most — English characters, short
        ///     conversational turns, or anything where the face must move while the reply is still
        ///     being spoken.
        /// </remarks>
        Nrclex = 2
    }
}
