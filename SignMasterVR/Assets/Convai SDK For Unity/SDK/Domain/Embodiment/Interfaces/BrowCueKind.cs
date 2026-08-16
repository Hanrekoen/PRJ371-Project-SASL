namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Semantic categories of a one-shot brow cue a gaze-side producer can request through
    ///     <see cref="IBrowCueSink" />.
    /// </summary>
    public enum BrowCueKind
    {
        /// <summary>
        ///     A small, sustained brow-up bias driven by an upward saccade/fixation — the brows
        ///     lift slightly while the eyes look up, easing back out as the eyes settle.
        /// </summary>
        SubtleRaise = 0,

        /// <summary>
        ///     A quick up-down brow flash — pairs with an acknowledgment/backchannel nod ("I hear
        ///     you").
        /// </summary>
        Flash = 1,

        /// <summary>
        ///     A stronger, faster brow flash — pairs with a startle re-acquisition (e.g. the
        ///     character being interrupted mid-turn).
        /// </summary>
        SurpriseFlash = 2
    }
}
