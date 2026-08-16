using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Adapter that surfaces <see cref="FacialBlendshapeCompositorHost" />'s speech-blend
    ///     state through the engine-free <see cref="IDialoguePhaseProvider" /> contract.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The adapter keeps the LipSync module fully decoupled from
    ///         <c>Convai.Domain.Embodiment</c>: LipSync continues to call
    ///         <see cref="FacialBlendshapeCompositorHost.SetLipSyncSpeechActive" /> as before,
    ///         and this adapter translates that state for consumers (primarily the
    ///         ConversationFlow state machine).
    ///     </para>
    ///     <para>
    ///         Auto-provisioned next to the compositor host by EmbodimentContext.
    ///     </para>
    /// </remarks>
    [RequireComponent(typeof(FacialBlendshapeCompositorHost))]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class CompositorDialoguePhaseAdapter : MonoBehaviour, IDialoguePhaseProvider
    {
        private FacialBlendshapeCompositorHost _host;

        /// <inheritdoc />
        public bool IsSpeechActive => SpeechBlendFactor > 0.01f;

        /// <inheritdoc />
        public float SpeechBlendFactor
        {
            get
            {
                if (_host == null) _host = GetComponent<FacialBlendshapeCompositorHost>();
                return _host != null ? _host.SpeechBlendFactor : 0f;
            }
        }

        private void Awake()
        {
            hideFlags = Convai.Runtime.Embodiment.EmbodimentContext.RuntimeInfrastructureHideFlags();
            _host = GetComponent<FacialBlendshapeCompositorHost>();
        }
    }
}
