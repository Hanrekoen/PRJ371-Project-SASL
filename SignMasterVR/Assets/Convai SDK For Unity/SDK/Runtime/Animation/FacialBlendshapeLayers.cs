namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Layer identifiers for the facial blendshape compositor.
    ///     Built-in layers have fixed IDs below <see cref="CustomLayerStart" />.
    ///     Register custom layers via <see cref="FacialBlendshapeCompositorHost.RegisterCustomLayer" />.
    /// </summary>
    public static class FacialBlendshapeLayers
    {
        public const int EmotionGeneral = 0;
        public const int EmotionMouth = 1;
        public const int LipSync = 2;
        public const int Eyes = 3;
        public const int HeadLook = 4;

        /// <summary>
        ///     Micro-expression "life" layer (idle drift + speech-coupled accents). Composes
        ///     additively on top of <see cref="EmotionGeneral" />/<see cref="EmotionMouth" />
        ///     rather than max-blending, so it can never suppress the base expression — see
        ///     <see cref="FacialBlendshapeCompositorHost.ComposeAndWriteRegion" />.
        /// </summary>
        public const int EmotionMicro = 5;

        /// <summary>First ID available for user-registered custom layers.</summary>
        public const int CustomLayerStart = 100;

        internal const int BuiltInCount = 6;

        internal static bool IsBuiltIn(int layerId) => layerId >= 0 && layerId < CustomLayerStart;
        internal static bool IsCustom(int layerId) => layerId >= CustomLayerStart;
    }
}
