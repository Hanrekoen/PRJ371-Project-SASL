namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for a module that owns a character's brow blendshapes/expression channels and
    ///     consumes one-shot brow cues, keeping the brows coordinated with where the eyes go.
    ///     Gaze publishes cues
    ///     through this interface — an upward saccade, a backchannel nod starting, or an
    ///     interruption startle re-acquisition — when an implementation is registered on the
    ///     character's <c>EmbodimentContext</c>; the seam is entirely optional — with no
    ///     implementation registered, raising a cue costs a single null check and nothing is
    ///     written.
    /// </summary>
    /// <remarks>
    ///     Cues are edge events, not a per-frame signal: a producer calls
    ///     <see cref="RaiseBrowCue" /> once when a cue fires, and a consuming implementation owns
    ///     its own envelope/decay shaping for how long the brow motion reads before returning to
    ///     rest. Implementations must be safe to call every tick a cue happens to fire on
    ///     (including in rapid succession) without throwing.
    /// </remarks>
    internal interface IBrowCueSink
    {
        /// <summary>
        ///     Raises a one-shot brow cue of the given <paramref name="kind" />.
        /// </summary>
        /// <param name="kind">The semantic category of the cue (see <see cref="BrowCueKind" />).</param>
        /// <param name="intensity01">
        ///     Cue intensity/emphasis, normalized to <c>[0, 1]</c>. Callers pass an
        ///     already-clamped value; implementations should not re-clamp aggressively but may
        ///     defensively clamp against out-of-range input.
        /// </param>
        void RaiseBrowCue(BrowCueKind kind, float intensity01);
    }
}
