using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Chooses direct mesh writes vs compositor submission without creating a compositor.
    /// </summary>
    internal static class LipSyncBlendshapeOutputSinkFactory
    {
        internal static ILipSyncBlendshapeOutputSink Create(Component context, IFacialBlendshapeSource source)
        {
            FacialBlendshapeCompositorHost host = FacialBlendshapeCompositorHost.TryResolve(context);
            if (host != null)
                return new CompositorLipSyncBlendshapeOutputSink(host, source);

            return new DirectLipSyncBlendshapeOutputSink();
        }
    }
}
