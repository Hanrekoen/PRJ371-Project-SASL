using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Abstract base for any <see cref="MonoBehaviour" /> that implements
    ///     <see cref="IEmbodimentProfileReceiver" />, centralizing the boilerplate shared by
    ///     every embodiment module: context resolution, profile receiver registration, owned
    ///     default lifecycle, and the typed/untyped <c>ApplyProfile</c> overloads.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <c>[ExecuteAlways]</c> attribute is required so that Unity invokes
    ///         <c>OnEnable</c> and <c>OnDisable</c> during EditMode — both when
    ///         <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)" /> /
    ///         <c>AddComponent</c> is called and when the <see cref="enabled" /> property is
    ///         toggled. Without it Unity only calls <c>Awake</c> in EditMode, which means slot
    ///         and profile-receiver registration never happens in the editor or in EditMode tests.
    ///     </para>
    ///     <para>
    ///         All derived OnEnable / OnDisable overrides must remain safe to execute outside
    ///         Play Mode. Use <c>Application.isPlaying</c> guards for any operation that must
    ///         be deferred to runtime (e.g. AddComponent calls, physics, network setup).
    ///     </para>
    /// </remarks>
    /// <typeparam name="TProfile">ScriptableObject profile type owned by this receiver.</typeparam>
    [ExecuteAlways]
    public abstract class ConvaiCharacterModule<TProfile> : MonoBehaviour, IEmbodimentProfileReceiver
        where TProfile : ScriptableObject
    {
        [SerializeField] protected TProfile profile;

        private OwnedProfile<TProfile> _ownedProfile;

        /// <summary>The resolved <see cref="EmbodimentContext" /> for this character.</summary>
        protected EmbodimentContext Context { get; private set; }

        /// <summary>
        ///     Whether this component disabled itself because no Convai character could be resolved.
        ///     Surfaced so the inspector can show the failure rather than leaving the user to guess.
        /// </summary>
        internal bool ContextResolutionFailed { get; private set; }

        /// <summary>The effective profile: authored asset if assigned, otherwise the runtime default.</summary>
        /// <remarks>
        ///     Returns <c>null</c> if <c>Awake</c> has not yet been called (e.g. during a domain
        ///     reload race with <c>[ExecuteAlways]</c> callbacks). Callers must null-check or guard
        ///     with <c>Application.isPlaying</c> before accessing profile data.
        /// </remarks>
        protected TProfile EffectiveProfile => _ownedProfile?.Resolve(profile);

        /// <summary>Module identifier forwarded to <see cref="IEmbodimentProfileReceiver.ModuleId" />.</summary>
        protected abstract string ProfileModuleId { get; }

        /// <summary>Factory used to create the runtime default when no profile asset is assigned.</summary>
        protected abstract Func<TProfile> DefaultProfileFactory { get; }

        // IEmbodimentProfileReceiver — explicit so the untyped overloads stay off the public surface.

        string IEmbodimentProfileReceiver.ModuleId => ProfileModuleId;

        bool IEmbodimentProfileReceiver.CanApplyProfile(ScriptableObject candidate) =>
            candidate == null || candidate is TProfile;

        bool IEmbodimentProfileReceiver.ApplyProfile(ScriptableObject candidate)
        {
            TProfile typed = candidate as TProfile;
            if (candidate != null && typed == null) return false;

            profile = typed;
            // Apply(typed), not Apply(null): passing the typed value states the actual intent —
            // adopt this profile as effective — without releasing the owned default or clearing the
            // cached effective profile.
            _ownedProfile?.Apply(typed);
            OnProfileApplied(typed);
            return true;
        }

        /// <summary>
        ///     Called after the profile slot is updated via
        ///     <see cref="IEmbodimentProfileReceiver.ApplyProfile" />. Override to apply
        ///     per-module side-effects (reset state machines, re-resolve bindings, etc.).
        /// </summary>
        protected virtual void OnProfileApplied(TProfile newProfile) { }

        protected virtual void Awake()
        {
            _ownedProfile = new OwnedProfile<TProfile>(DefaultProfileFactory);
        }

        protected virtual void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out EmbodimentContext ctx))
            {
                // When no Convai character can be resolved, TryResolveFor reports the actual cause
                // (the component is not on a Convai character) and this disables the component as
                // the consequence, so it never just silently does nothing.
                enabled = false;
                ContextResolutionFailed = true;
                return;
            }

            ContextResolutionFailed = false;

            Context = ctx;
            Context.RegisterProfileReceiver(this, this);
        }

        protected virtual void OnDisable()
        {
            ReleaseProvidedServices();
            Context?.UnregisterProfileReceiver(this);
        }

        protected virtual void OnDestroy()
        {
            _ownedProfile?.Release();
        }

        // ── cross-module contract publication ───────────────────────────────────────

        private List<CharacterServiceRegistry.ServiceToken> _serviceTokens;

        /// <summary>
        ///     Publishes this component as the character's provider of
        ///     <typeparamref name="TContract" /> and keeps the withdrawal token, so the base class
        ///     releases it automatically in <see cref="OnDisable" />.
        /// </summary>
        /// <remarks>
        ///     Always name the contract — <c>ProvideService&lt;IGazeSource&gt;(this)</c>. Inferring it
        ///     from the instance would key the registration on the concrete component type, where no
        ///     consumer looks.
        /// </remarks>
        protected void ProvideService<TContract>(TContract service) where TContract : class
        {
            if (Context == null || service == null) return;
            Track(Context.Provide(service));
        }

        /// <summary>
        ///     Adds this component as a contributor to a fan-out contract, tracked and released like
        ///     <see cref="ProvideService{TContract}" />.
        /// </summary>
        protected void ContributeService<TContract>(TContract service) where TContract : class
        {
            if (Context == null || service == null) return;
            Track(Context.Contribute(service));
        }

        /// <summary>
        ///     Withdraws every contract this component published. Called from
        ///     <see cref="OnDisable" />; safe to call more than once.
        /// </summary>
        /// <remarks>
        ///     A derived class that overrides <c>OnDisable</c> must call <c>base.OnDisable()</c> —
        ///     otherwise its registrations outlive it. An architecture guard test enforces that.
        /// </remarks>
        protected void ReleaseProvidedServices()
        {
            if (_serviceTokens == null) return;

            for (int i = _serviceTokens.Count - 1; i >= 0; i--)
                _serviceTokens[i].Release();

            _serviceTokens.Clear();
        }

        private void Track(CharacterServiceRegistry.ServiceToken token)
        {
            if (!token.IsValid) return;
            _serviceTokens ??= new List<CharacterServiceRegistry.ServiceToken>(4);
            _serviceTokens.Add(token);
        }
    }
}
