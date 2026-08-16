namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Facial regions used by the compositor to apply per-region composition policies.
    ///     Each blendshape target is classified into exactly one region based on name-pattern matching
    ///     configured in <see cref="ConvaiFacialCompositionProfile" />.
    /// </summary>
    public enum FacialBlendshapeRegion
    {
        /// <summary>Lip, tongue, and mouth-corner blendshapes driven primarily by lip sync during speech.</summary>
        Mouth = 0,

        /// <summary>Brow and forehead blendshapes ; emotion-dominant with subtle lip sync additive.</summary>
        Brow = 1,

        /// <summary>Blink, squint, wide-eye, and look blendshapes ; eye gaze dominant.</summary>
        Eye = 2,

        /// <summary>Cheek puff, cheek squint, and nose sneer blendshapes ; emotion-dominant.</summary>
        Cheek = 3,

        /// <summary>Jaw forward/left/right blendshapes ; lip sync dominant during speech (separate from mouth).</summary>
        Jaw = 4,

        /// <summary>Fallback region for blendshapes not matched by any configured pattern.</summary>
        Other = 5
    }
}
