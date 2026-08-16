using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Emotion.Core;

namespace Convai.Modules.Emotion.Authoring
{
    /// <summary>
    ///     Curated blendshape name lookup for the micro-expression "life" layer
    ///     (<see cref="MicroExpressionChannel" />), keyed by standard rig convention. Mirrors
    ///     the same naming approach the expression recipes use: comma-separated
    ///     per-side names resolved against the character's facial meshes at bind time. A
    ///     channel that matches no blendshape on the bound rig is simply inert — no throw, no
    ///     spam beyond the shared one-time "unmatched name" diagnostic already emitted by the
    ///     binding's resolution path.
    /// </summary>
    internal static class MicroExpressionShapeMap
    {
        /// <summary>
        ///     Returns the comma-separated candidate blendshape name list for
        ///     <paramref name="channel" /> on <paramref name="rig" />, or an empty string when
        ///     the rig convention has no curated mapping for that channel (the caller resolves
        ///     names against live meshes and treats a total miss as inert).
        /// </summary>
        public static string GetNames(MicroExpressionChannel channel, RigConvention rig)
        {
            return rig switch
            {
                RigConvention.ReallusionCC4Extended => GetCC4Extended(channel),
                RigConvention.ReallusionCC3 => GetCC3(channel),
                RigConvention.ARKit => GetARKit(channel),
                // Unknown, Custom and MetaHuman must never silently assume CC3 naming — see
                // The same rule applies wherever a comma-separated shape list is authored.
                _ => string.Empty
            };
        }

        private static string GetCC4Extended(MicroExpressionChannel channel) => channel switch
        {
            MicroExpressionChannel.BrowInnerUp => "Brow_Raise_Inner_L,Brow_Raise_Inner_R",
            MicroExpressionChannel.BrowOuterUp => "Brow_Raise_Outer_L,Brow_Raise_Outer_R",
            MicroExpressionChannel.BrowDown => "Brow_Drop_L,Brow_Drop_R",
            MicroExpressionChannel.CheekRaise => "Cheek_Raise_L,Cheek_Raise_R",
            MicroExpressionChannel.Squint => "Eye_Squint_L,Eye_Squint_R",
            _ => string.Empty
        };

        private static string GetCC3(MicroExpressionChannel channel) => channel switch
        {
            MicroExpressionChannel.BrowInnerUp => "Brow_Raise_Inner_L,Brow_Raise_Inner_R",
            MicroExpressionChannel.BrowOuterUp => "Brow_Raise_Outer_L,Brow_Raise_Outer_R",
            MicroExpressionChannel.BrowDown => "Brow_Drop_L,Brow_Drop_R",
            MicroExpressionChannel.CheekRaise => "Cheek_Raise_L,Cheek_Raise_R",
            MicroExpressionChannel.Squint => "Eye_Squint_L,Eye_Squint_R",
            _ => string.Empty
        };

        private static string GetARKit(MicroExpressionChannel channel) => channel switch
        {
            MicroExpressionChannel.BrowInnerUp => "browInnerUp",
            MicroExpressionChannel.BrowOuterUp => "browOuterUpLeft,browOuterUpRight",
            MicroExpressionChannel.BrowDown => "browDownLeft,browDownRight",
            MicroExpressionChannel.CheekRaise => "cheekSquintLeft,cheekSquintRight",
            MicroExpressionChannel.Squint => "eyeSquintLeft,eyeSquintRight",
            _ => string.Empty
        };
    }
}
