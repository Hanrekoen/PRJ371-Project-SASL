namespace Convai.Modules.Emotion.Core
{
    /// <summary>
    ///     Curated micro-expression channels driven by <see cref="MicroExpressionDirector" />.
    ///     Each channel resolves independently to zero or more blendshape targets via
    ///     <c>Convai.Modules.Emotion.Authoring.MicroExpressionShapeMap</c>; a channel with no
    ///     resolved targets on the bound rig is simply inert.
    /// </summary>
    internal enum MicroExpressionChannel
    {
        /// <summary>Inner brow raise — worry/sad accent.</summary>
        BrowInnerUp = 0,

        /// <summary>Outer brow raise — joy/surprise accent and the conversational emphasis raise.</summary>
        BrowOuterUp = 1,

        /// <summary>Brow knit/drop — anger/concentration accent.</summary>
        BrowDown = 2,

        /// <summary>Cheek raise — joy accent (Duchenne-adjacent life).</summary>
        CheekRaise = 3,

        /// <summary>Subtle eye squint — quiet eye-region life.</summary>
        Squint = 4,

        /// <summary>Total number of curated channels. Keep in sync with the enum members above.</summary>
        Count = 5
    }
}
