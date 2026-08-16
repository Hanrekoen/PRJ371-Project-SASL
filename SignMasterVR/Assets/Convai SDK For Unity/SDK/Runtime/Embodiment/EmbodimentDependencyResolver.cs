using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Animation;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using UnityEngine;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Owns lazy resolution of the optional infrastructure components (compositor, animator
    ///     conductor, tick scheduler, rig binding, character, dialogue phase adapter) that
    ///     <see cref="EmbodimentContext" /> exposes to embodiment modules. Encapsulates the
    ///     "find existing or create on demand" pattern so the context itself can stay a thin
    ///     facade.
    /// </summary>
    internal sealed class EmbodimentDependencyResolver
    {
        private readonly EmbodimentContext _owner;

        private FacialBlendshapeCompositorHost _compositor;
        private AnimatorConductor _animatorConductor;
        private EmbodimentTickScheduler _tickScheduler;
        private StandardRigBinding _rigBinding;
        private ConvaiCharacter _character;
        private CompositorDialoguePhaseAdapter _dialoguePhaseAdapter;

        private bool _resolved;

        public EmbodimentDependencyResolver(EmbodimentContext owner)
        {
            _owner = owner;
        }

        public ConvaiCharacter Character => _character;
        public FacialBlendshapeCompositorHost Compositor => _compositor;
        public AnimatorConductor AnimatorConductor => _animatorConductor;
        public EmbodimentTickScheduler TickScheduler => _tickScheduler;
        public IStandardRigBinding RigBinding => _rigBinding;
        public IDialoguePhaseProvider DialoguePhase => _dialoguePhaseAdapter;

        public void ResolveOptionalComponents(ConvaiFacialCompositionProfile facialOverride)
        {
            if (_resolved) return;
            _resolved = true;
            ResolveNow(facialOverride);
        }

        /// <summary>
        ///     Re-runs resolution from scratch, for a runtime avatar swap that replaces the hierarchy
        ///     under the character.
        /// </summary>
        /// <remarks>
        ///     <see cref="ResolveOptionalComponents" /> is one-shot on purpose — it runs from several
        ///     lifecycle entry points and must stay cheap. Clearing every cached reference here — not
        ///     only the rig binding, but also the compositor, animator conductor, scheduler, character
        ///     and dialogue adapter — makes a hierarchy-changing avatar swap re-resolve completely
        ///     against the new hierarchy rather than leaving some dependencies pinned to the old one.
        /// </remarks>
        public void ReResolveForHierarchyChange(ConvaiFacialCompositionProfile facialOverride)
        {
            _compositor = null;
            _animatorConductor = null;
            _tickScheduler = null;
            _rigBinding = null;
            _character = null;
            _dialoguePhaseAdapter = null;
            _resolved = true;
            ResolveNow(facialOverride);
        }

        private void ResolveNow(ConvaiFacialCompositionProfile facialOverride)
        {

            if (_character == null)
                _character = _owner.GetComponentInChildren<ConvaiCharacter>(true);
            if (_compositor == null)
                _compositor = _owner.GetComponentInChildren<FacialBlendshapeCompositorHost>(true);
            if (_compositor != null)
                ApplyFacialCompositionProfile(facialOverride);
            if (_animatorConductor == null)
                _animatorConductor = _owner.GetComponentInChildren<AnimatorConductor>(true);
            if (_tickScheduler == null)
                _tickScheduler = _owner.GetComponentInChildren<EmbodimentTickScheduler>(true);
            if (_rigBinding == null)
                _rigBinding = ResolveRigBinding();
            if (_dialoguePhaseAdapter == null && _compositor != null)
                _dialoguePhaseAdapter = _compositor.GetComponent<CompositorDialoguePhaseAdapter>();
        }

        public FacialBlendshapeCompositorHost EnsureCompositor(ConvaiFacialCompositionProfile facialOverride)
        {
            if (_compositor == null)
                _compositor = _owner.GetComponentInChildren<FacialBlendshapeCompositorHost>(true);
            if (_compositor == null && UnityEngine.Application.isPlaying)
            {
                _compositor = FacialBlendshapeCompositorHost.GetOrCreate(_owner);
                AnnounceProvisioned(_compositor, "writes this character's facial blendshapes");
            }
            if (_compositor != null)
                ApplyFacialCompositionProfile(facialOverride);
            return _compositor;
        }

        public AnimatorConductor EnsureAnimatorConductor()
        {
            if (_animatorConductor == null)
                _animatorConductor = _owner.GetComponentInChildren<AnimatorConductor>(true);
            if (_animatorConductor == null && UnityEngine.Application.isPlaying)
            {
                _animatorConductor = AnimatorConductor.GetOrCreate(_owner);
                AnnounceProvisioned(_animatorConductor, "is the single writer for animator parameters");
            }
            return _animatorConductor;
        }

        public EmbodimentTickScheduler EnsureTickScheduler()
        {
            if (_tickScheduler == null)
                _tickScheduler = _owner.GetComponentInChildren<EmbodimentTickScheduler>(true);
            if (_tickScheduler == null && UnityEngine.Application.isPlaying)
                _tickScheduler = EmbodimentTickScheduler.GetOrCreate(_owner);
            return _tickScheduler;
        }

        public IStandardRigBinding EnsureRigBinding()
        {
            if (_rigBinding == null)
                _rigBinding = ResolveRigBinding();
            if (_rigBinding == null && UnityEngine.Application.isPlaying)
            {
                _rigBinding = _owner.gameObject.AddComponent<StandardRigBinding>();
                _rigBinding.hideFlags = EmbodimentContext.RuntimeInfrastructureHideFlags();
                AnnounceProvisioned(_rigBinding,
                    "resolves this character's bones and face meshes — open it to check the detected rig");
            }
            return _rigBinding;
        }

        public IDialoguePhaseProvider EnsureDialoguePhase(ConvaiFacialCompositionProfile facialOverride)
        {
            if (_dialoguePhaseAdapter != null) return _dialoguePhaseAdapter;

            FacialBlendshapeCompositorHost compositor = EnsureCompositor(facialOverride);
            if (compositor == null) return null;

            _dialoguePhaseAdapter = compositor.GetComponent<CompositorDialoguePhaseAdapter>();
            if (_dialoguePhaseAdapter == null && UnityEngine.Application.isPlaying)
            {
                _dialoguePhaseAdapter = compositor.gameObject.AddComponent<CompositorDialoguePhaseAdapter>();
                _dialoguePhaseAdapter.hideFlags = EmbodimentContext.RuntimeInfrastructureHideFlags();
                AnnounceProvisioned(_dialoguePhaseAdapter, "exposes speech state to the embodiment modules");
            }
            return _dialoguePhaseAdapter;
        }

        /// <summary>
        ///     Re-resolves the rig binding after a rebind, optionally preferring the binding that
        ///     announced itself.
        /// </summary>
        /// <remarks>
        ///     A <see cref="StandardRigBinding" /> on the context owner stays authoritative — that
        ///     invariant is what stops <c>Awake</c>/<c>Rebuild</c> callback order from deciding which
        ///     binding a character uses. Below it, an announcing binding that genuinely belongs to
        ///     this character is preferred over hierarchy order, so a caller that says "I am the
        ///     binding that just rebuilt" is answered rather than ignored. Anything else — a null, a
        ///     foreign implementation, a binding from another character — falls back to resolution.
        /// </remarks>
        public void NotifyRigBindingChanged(IStandardRigBinding incoming)
        {
            _rigBinding = PreferAnnouncedBinding(incoming) ?? ResolveRigBinding();

            _animatorConductor?.RefreshAnimator();
        }

        /// <summary>
        ///     The announcing binding when it may be adopted, otherwise <c>null</c>.
        /// </summary>
        private StandardRigBinding PreferAnnouncedBinding(IStandardRigBinding incoming)
        {
            if (incoming is not StandardRigBinding announced || announced == null) return null;

            // The owner's own binding outranks any announcement.
            StandardRigBinding onOwner = _owner.GetComponent<StandardRigBinding>();
            if (onOwner != null) return onOwner;

            // Only adopt a binding that belongs to this character's hierarchy.
            return announced.transform.IsChildOf(_owner.transform) ? announced : null;
        }

        private StandardRigBinding ResolveRigBinding()
        {
            StandardRigBinding onOwner = _owner.GetComponent<StandardRigBinding>();
            if (onOwner != null) return onOwner;

            StandardRigBinding[] descendants = _owner.GetComponentsInChildren<StandardRigBinding>(true);
            return descendants.Length > 0 ? descendants[0] : null;
        }

        public void ApplyFacialCompositionProfile(ConvaiFacialCompositionProfile overrideProfile)
        {
            if (_compositor == null) return;

            if (overrideProfile != null)
            {
                _compositor.SetCompositionProfile(overrideProfile);
                return;
            }

            _compositor.EnsureDefaultProfileLoaded();
        }

        /// <summary>
        ///     Logs, once per component, that the SDK added a piece of infrastructure the user did
        ///     not.
        /// </summary>
        /// <remarks>
        ///     Auto-provisioning is a supported convenience, but a component appearing on a user's
        ///     object with no explanation is not. Every provisioned component names itself and says
        ///     what it is for, so a character's composition can be read from the log.
        /// </remarks>
        private static void AnnounceProvisioned(Component provisioned, string purpose)
        {
            if (provisioned == null) return;

            ConvaiLogger.Info(
                $"[{provisioned.GetType().Name}] Added to '{provisioned.gameObject.name}' — it {purpose}.",
                LogCategory.Character);
        }

    }
}
