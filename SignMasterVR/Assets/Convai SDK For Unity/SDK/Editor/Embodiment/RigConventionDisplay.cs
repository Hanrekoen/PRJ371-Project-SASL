using Convai.Domain.Embodiment.Semantics;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Embodiment
{
    /// <summary>
    ///     How a detected rig convention and its detection confidence are spelled to a user.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why names live here rather than at the call site.</b> The enum's own identifiers are
    ///         written for code — <c>ReallusionCC4Extended</c>, <c>ARKit</c> — and every surface that
    ///         printed <c>convention.ToString()</c> was showing the user an identifier and hoping they
    ///         recognised the product inside it. One table means the rig inspector, the setup report
    ///         and the Embodiment window all call the same rig the same thing.
    ///     </para>
    ///     <para>
    ///         <b>Why confidence is banded.</b> Detection confidence is a fraction of matched
    ///         signature blendshapes. Shown raw it asks the reader to know what a good score is;
    ///         <c>0.79</c> tells a rig author nothing and tells a beginner less. The band answers the
    ///         only question the number is ever asked — is this recognition trustworthy — and the raw
    ///         value stays available for anyone who wants it.
    ///     </para>
    /// </remarks>
    internal static class RigConventionDisplay
    {
        /// <summary>Below this, a detection is a guess worth double-checking.</summary>
        internal const float LowConfidence = 0.5f;

        /// <summary>At or above this, a detection is as certain as name matching gets.</summary>
        internal const float StrongConfidence = 0.75f;

        /// <summary>Full product name, for a reading the user has room to read.</summary>
        internal static string DisplayName(RigConvention convention)
        {
            return convention switch
            {
                RigConvention.ARKit => "Apple ARKit",
                RigConvention.ReallusionCC3 => "Reallusion Character Creator 3",
                RigConvention.ReallusionCC4Extended => "Reallusion Character Creator 4 (Extended)",
                RigConvention.MetaHuman => "Epic MetaHuman",
                RigConvention.Custom => "Custom name map",
                _ => "Not recognised"
            };
        }

        /// <summary>Abbreviated name, for a collapsed section's right-aligned summary.</summary>
        internal static string ShortName(RigConvention convention)
        {
            return convention switch
            {
                RigConvention.ARKit => "ARKit",
                RigConvention.ReallusionCC3 => "CC3",
                RigConvention.ReallusionCC4Extended => "CC4 Extended",
                RigConvention.MetaHuman => "MetaHuman",
                RigConvention.Custom => "Custom",
                _ => "Not recognised"
            };
        }

        /// <summary>Banded confidence, with the raw fraction kept alongside it.</summary>
        internal static string MatchStrength(RigConvention convention, float confidence)
        {
            if (convention == RigConvention.Unknown) return "No match";

            string band = confidence >= StrongConfidence ? "Strong"
                : confidence >= LowConfidence ? "Partial"
                : "Weak";

            return $"{band}  —  {confidence:P0} of this type's marker shapes were found";
        }

        /// <summary>The tint that agrees with <see cref="MatchStrength" />.</summary>
        internal static Color MatchTint(RigConvention convention, float confidence)
        {
            if (convention == RigConvention.Unknown) return Theme.StatusWarn;
            return confidence >= LowConfidence ? Theme.StatusReady : Theme.StatusWarn;
        }
    }
}
