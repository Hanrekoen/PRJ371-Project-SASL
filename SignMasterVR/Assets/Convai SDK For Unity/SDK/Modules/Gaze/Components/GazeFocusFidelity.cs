namespace Convai.Modules.Gaze.Components
{
    /// <summary>How precisely an active eye-contact mode holds its player anchor.</summary>
    public enum GazeFocusFidelity
    {
        /// <summary>
        ///     Preserves small fixation motion and socially useful head gestures while keeping
        ///     the player as the conversational target. Recommended for dialogue characters.
        /// </summary>
        Social = 0,

        /// <summary>
        ///     Suppresses intentional look-aways and fixation offsets while focus is active.
        ///     Blinks, eyelids, pupils, vergence, and anatomical body turns remain available.
        /// </summary>
        Exact = 1
    }
}
