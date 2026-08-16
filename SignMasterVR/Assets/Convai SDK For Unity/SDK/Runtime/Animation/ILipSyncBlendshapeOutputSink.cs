using System.Collections.Generic;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Lip-sync blendshape output port: either writes meshes directly or submits to an
    ///     existing <see cref="FacialBlendshapeCompositorHost" /> (never creates the host).
    /// </summary>
    internal interface ILipSyncBlendshapeOutputSink
    {
        void PushFrame(IReadOnlyList<BlendshapeTargetKey> targets, IReadOnlyList<float> weights, int count);

        void SetSpeechActive(bool isActive);

        /// <summary>
        ///     Clears driven mesh weights (direct path). Compositor path is a no-op; the host
        ///     recomposes when the lip-sync layer stops receiving submissions.
        /// </summary>
        void ResetDrivenTargets();
    }
}
