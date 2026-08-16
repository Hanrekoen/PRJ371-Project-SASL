using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Contract for runtime systems that submit facial blendshape weights to the shared compositor.
    /// </summary>
    internal interface IFacialBlendshapeSource
    {
        /// <summary>Owning Unity component used for host resolution and diagnostics.</summary>
        public Component SourceComponent { get; }

        /// <summary>Human-readable source name for debugging.</summary>
        public string SourceName { get; }
    }
}
