using System.Collections.Generic;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Submits lip-sync weights to an existing compositor host (layer
    ///     <see cref="FacialBlendshapeLayers.LipSync" />).
    /// </summary>
    internal sealed class CompositorLipSyncBlendshapeOutputSink : ILipSyncBlendshapeOutputSink
    {
        private readonly FacialBlendshapeCompositorHost _host;
        private readonly IFacialBlendshapeSource _source;

        public CompositorLipSyncBlendshapeOutputSink(FacialBlendshapeCompositorHost host,
            IFacialBlendshapeSource source)
        {
            _host = host;
            _source = source;
        }

        public void PushFrame(IReadOnlyList<BlendshapeTargetKey> targets, IReadOnlyList<float> weights, int count)
        {
            _host.SubmitLayer(_source, FacialBlendshapeLayers.LipSync, targets, weights, count);
        }

        public void SetSpeechActive(bool isActive) => _host.SetLipSyncSpeechActive(_source, isActive);

        public void ResetDrivenTargets()
        {
        }
    }
}
