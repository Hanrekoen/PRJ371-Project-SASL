using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Taxonomy;
using UnityEngine;

namespace Convai.Modules.Emotion.Outputs
{
    /// <summary>
    ///     Strategy interface for projecting composed emotion scores onto a concrete output
    ///     target beyond the face itself.
    /// </summary>
    /// <remarks>
    ///     Facial expression is not routed through this interface — it goes through the
    ///     rig-independent expression recipes and <c>SemanticBlendshapeEmotionOutput</c>. This seam
    ///     exists for optional additional outputs authored on the <c>ConvaiEmotionProfile</c>;
    ///     <c>MaterialPropertyEmotionBinding</c> (blush, tears, sweat) is the shipped implementer.
    /// </remarks>
    internal interface IEmotionOutputBinding
    {
        /// <summary>
        ///     Called once when the owning controller initializes and again whenever the profile
        ///     or the character's rig changes. Implementations resolve and cache their output
        ///     targets here, from <paramref name="rig" /> where they need one.
        /// </summary>
        void Bind(Object owner, IEmotionTaxonomy taxonomy, IStandardRigBinding rig);

        /// <summary>
        ///     Writes the current frame's scores to the bound output. <paramref name="intensityGain" />
        ///     is a global multiplier on the composed intensity — <c>1</c> means no change.
        ///     Implementations multiply their composed intensity by it and clamp the result
        ///     afterward, exactly as they already clamp the unmodified composition.
        /// </summary>
        void Apply(IReadOnlyDictionary<string, float> scores, float intensityGain);

        /// <summary>Releases any owned resources and restores the output to its unbound state.</summary>
        void Unbind(Object owner);
    }
}
