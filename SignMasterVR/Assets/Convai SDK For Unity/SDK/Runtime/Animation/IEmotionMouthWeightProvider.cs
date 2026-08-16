namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Exposes current emotion-driven mouth blendshape weights (0-100 mesh units) for targets
    ///     resolved on the character. Used by lip sync fade-out to blend toward active expression
    ///     instead of snapping through the talking mouth mix.
    /// </summary>
    /// <remarks>
    ///     This contract lives in <c>Convai.Runtime.Animation</c> (not
    ///     <c>Convai.Domain.Embodiment.Interfaces</c>) because <see cref="BlendshapeTargetKey" />
    ///     is tied to Unity <see cref="UnityEngine.SkinnedMeshRenderer" /> instances. Pure
    ///     dialogue / attention / gaze semantics remain in the domain assembly; mesh-addressed
    ///     facial outputs stay next to the compositor and rig resolution types.
    /// </remarks>
    internal interface IEmotionMouthWeightProvider
    {
        /// <summary>
        ///     Returns the accumulated emotion mouth weight for the given blendshape target for the
        ///     current frame (after the emotion applier has run for this frame).
        /// </summary>
        /// <returns>True if this target participates in emotion mouth output; otherwise false.</returns>
        bool TryGetMouthWeight(BlendshapeTargetKey key, out float weight);
    }
}
