namespace Convai.Modules.BodyAnimation.Core.Diagnostics
{
    /// <summary>
    ///     Verbosity levels for body animation diagnostics. Each level includes everything
    ///     below it, so raising the level only ever adds information.
    /// </summary>
    public enum AnimTraceVerbosity
    {
        /// <summary>
        ///     No trace output. Warnings and errors are still logged. The shipped default: a
        ///     character that walks and talks produces a steady stream of transitions, and a
        ///     console full of routine play-by-play is where a real warning goes unnoticed.
        /// </summary>
        Off = 0,

        /// <summary>
        ///     State-machine transitions, layer ownership changes, action lifecycle, clip
        ///     selections, and startup feature summaries. The level to raise a character to while
        ///     diagnosing what it did.
        /// </summary>
        State = 1,

        /// <summary>
        ///     Adds selector decisions (angles, distances, foot phase), variant rolls with
        ///     weights, speed-warp clamps, and executor begin/end markers.
        /// </summary>
        Detail = 2,

        /// <summary>
        ///     Adds throttled per-tick dumps of layer weights and blend positions. Extremely
        ///     chatty; intended only for short debugging sessions.
        /// </summary>
        Firehose = 3
    }
}
