using System.Collections.Generic;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Result of <see cref="ConvaiBodyAnimationConfig.ValidateForRuntime" />: what, if
    ///     anything, was out of range on the asset and had to be corrected before the runtime
    ///     could safely read it. Silent correction is not acceptable — this is how the caller
    ///     surfaces one warning naming every corrected field instead of the character quietly
    ///     misbehaving with no diagnostic.
    /// </summary>
    internal readonly struct BodyAnimationConfigCorrections
    {
        /// <summary>One human-readable line per corrected field, e.g. "Jog Speed (0.80) was below Walk Speed (1.20) and was raised to 1.20."</summary>
        public readonly List<string> Descriptions;

        public bool HasCorrections => Descriptions != null && Descriptions.Count > 0;

        public BodyAnimationConfigCorrections(List<string> descriptions)
        {
            Descriptions = descriptions;
        }
    }
}
