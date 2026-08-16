namespace Convai.Modules.Gaze.Components
{
    /// <summary>
    ///     How a <see cref="ConvaiGazeController" /> governs eye contact with its player
    ///     anchor. The mode is the product-level switch; fine-grained per-state tuning stays
    ///     in the profile's State Policies table (which only <see cref="Natural" /> consults).
    /// </summary>
    public enum GazeEyeContactMode
    {
        /// <summary>
        ///     The profile's per-dialogue-state policy table drives engagement, aversion, and
        ///     head participation — the authored, fully natural behavior (default).
        /// </summary>
        Natural = 0,

        /// <summary>
        ///     Full commitment to the player anchor in every conversational (non-Idle)
        ///     dialogue state — engagement 1, no aversion, full head participation, body
        ///     turns allowed — while Idle keeps the authored table row (ambient life, player
        ///     suppression). The guarantee that a speaking character never reads as looking
        ///     elsewhere, without turning the character into a statue between conversations.
        /// </summary>
        ConversationLock = 1,

        /// <summary>
        ///     Full commitment to the player anchor in every dialogue state, Idle included —
        ///     for kiosk greeters, demo booths, and characters that must never look away.
        ///     Micro-life (blinks, fixation drift, face scanning) stays on so the lock still
        ///     reads as alive rather than a frozen stare.
        /// </summary>
        AlwaysLock = 2,

        /// <summary>
        ///     Commits to the player anchor only while the character is producing speech.
        ///     Listening, thinking, and idle behavior remain profile-driven and natural.
        /// </summary>
        SpeakingFocus = 3
    }
}
