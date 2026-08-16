using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.EventSystem;
using Convai.Runtime.Animation;
using Convai.Runtime.Animation.ProceduralPose;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Domain.Logging;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Character-scoped composition root that exposes the shared infrastructure used by
    ///     decoupled embodiment modules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The context lives on the character root and is populated at runtime through the
    ///         neutral character dependency injection contract. Embodiment modules resolve it via
    ///         <see cref="TryResolve(Component, out EmbodimentContext)" /> and keep the reference for
    ///         the component lifetime.
    ///     </para>
    ///     <para>
    ///         Cross-module contracts live in a <see cref="CharacterServiceRegistry" />, so adding a
    ///         contract is a Domain interface and nothing more — this file does not change. Modules
    ///         publish through <see cref="Provide{TContract}" /> / <see cref="Contribute{TContract}" />
    ///         and hold the returned token; the named properties below are the read side, kept typed
    ///         so a module author can discover what a character offers by typing <c>Context.</c>.
    ///     </para>
    ///     <para>
    ///         Infrastructure components (compositor, animator conductor, tick scheduler, rig
    ///         binding, dialogue-phase adapter) are resolved lazily through
    ///         <see cref="EmbodimentDependencyResolver" /> so the context stays usable in edit-mode
    ///         previews, before <see cref="Populate" /> has run.
    ///     </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EmbodimentExecutionOrders.Context)]
    [AddComponentMenu("")]
    public sealed partial class EmbodimentContext : MonoBehaviour, IInjectable<IConvaiCharacterDependencies>
    {
        [Tooltip(
            "Optional. How much of the face Emotion and LipSync each get while the character is " +
            "speaking. Leave empty for the built-in balance, which suits every supported rig; " +
            "assign a profile only to rebalance it or to classify unusually named blendshapes.")]
        [SerializeField] private ConvaiFacialCompositionProfile _facialCompositionProfileOverride;

        private IEventHub _eventHub;
        private ILogger _logger;

        private EmbodimentDependencyResolver _dependencies;
        private CharacterServiceRegistry _services;

        private readonly EmbodimentProfileReceiverIndex _profileReceivers = new();

        private bool _speechEnergyProvisionAttempted;
        private bool _conversationFlowDriverDemanded;

        // ── infrastructure ──────────────────────────────────────────────────────────

        /// <summary>Character root transform.</summary>
        public Transform CharacterRoot => transform;

        /// <summary>Event bus for listening to domain events.</summary>
        public IEventHub EventHub => _eventHub;

        /// <summary>Logger for structured diagnostics (may be null if not injected).</summary>
        public ILogger Logger => _logger;

        /// <summary>Compositor host responsible for writing facial blendshapes.</summary>
        internal FacialBlendshapeCompositorHost Compositor => _dependencies.Compositor;

        /// <summary>
        ///     Optional facial composition profile override assigned in the inspector. When
        ///     <c>null</c>, the compositor builds the built-in default via
        ///     <see cref="ConvaiFacialCompositionProfile.CreateDefault" /> and owns it.
        /// </summary>
        public ConvaiFacialCompositionProfile FacialCompositionProfileOverride => _facialCompositionProfileOverride;

        /// <summary>Single-writer animator conductor.</summary>
        internal AnimatorConductor AnimatorConductor => _dependencies.AnimatorConductor;

        /// <summary>Deterministic tick scheduler for embodiment modules.</summary>
        internal EmbodimentTickScheduler TickScheduler => _dependencies.TickScheduler;

        /// <summary>Rig binding abstraction (semantic bones / blendshapes).</summary>
        public IStandardRigBinding RigBinding => _dependencies.RigBinding;

        /// <summary>The owning character, when one is present on this hierarchy.</summary>
        public ConvaiCharacter Character => _dependencies.Character;

        /// <summary>Adapter exposing LipSync's speech-state through <see cref="IDialoguePhaseProvider" />.</summary>
        internal IDialoguePhaseProvider DialoguePhase => _dependencies.DialoguePhase;

        // ── cross-module contracts: publish ─────────────────────────────────────────

        /// <summary>
        ///     Publishes this character's provider of <typeparamref name="TContract" />. One provider
        ///     per character: a second is rejected with a warning naming both instances, and the
        ///     first keeps the contract.
        /// </summary>
        /// <remarks>
        ///     Always name the contract explicitly — <c>Provide&lt;IGazeSource&gt;(this)</c>. Letting
        ///     C# infer it from the instance would key the entry on the concrete component type,
        ///     where no consumer would ever look for it.
        /// </remarks>
        /// <returns>A token whose <c>Release()</c> withdraws the registration.</returns>
        internal CharacterServiceRegistry.ServiceToken Provide<TContract>(TContract service)
            where TContract : class => Services.Provide(service);

        /// <summary>
        ///     Adds a contributor for a fan-out contract (many observers per character), preserving
        ///     registration order. Read with <see cref="GetAll{TContract}" />.
        /// </summary>
        internal CharacterServiceRegistry.ServiceToken Contribute<TContract>(TContract service)
            where TContract : class => Services.Contribute(service);

        /// <summary>
        ///     Withdraws <paramref name="service" /> from <typeparamref name="TContract" /> without
        ///     the token. Prefer the token — this is for call sites where registration and
        ///     withdrawal are far enough apart that threading one would obscure the code.
        /// </summary>
        internal void Withdraw<TContract>(TContract service) where TContract : class =>
            Services.Withdraw(service);

        /// <summary>Resolves the provider of <typeparamref name="TContract" />, or <c>null</c>.</summary>
        internal TContract GetService<TContract>() where TContract : class => Services.Get<TContract>();

        /// <summary>Copies the contributors for <typeparamref name="TContract" /> into a buffer.</summary>
        internal void GetAll<TContract>(List<TContract> buffer) where TContract : class =>
            Services.GetAll(buffer);

        /// <summary>
        ///     Subscribes to a contract being published or withdrawn. The handler receives the new
        ///     provider, or <c>null</c> when the contract became vacant.
        /// </summary>
        internal void AddServiceChangedHandler<TContract>(Action<TContract> handler)
            where TContract : class => Services.AddChangedHandler(handler);

        /// <summary>Removes a handler added by <see cref="AddServiceChangedHandler{TContract}" />.</summary>
        internal void RemoveServiceChangedHandler<TContract>(Action<TContract> handler)
            where TContract : class => Services.RemoveChangedHandler(handler);

        // ── lifecycle events ───────────────────────────────────────────────────────
        // Not service changes, so deliberately not folded into the registry: these describe the
        // context's own state, not who is providing a contract.

        /// <summary>
        ///     Raised when the semantic rig binding is rebuilt or replaced at runtime. Modules that
        ///     cache bone or mesh references should resolve them again when this fires.
        /// </summary>
        public event Action<IStandardRigBinding> RigBindingChanged;

        /// <summary>
        ///     Raised after embodiment-module configuration has been updated at runtime (for example
        ///     by swapping a preset).
        /// </summary>
        public event Action EmbodimentConfigurationChanged;

        /// <summary>
        ///     Raised after runtime-only dependencies such as <see cref="EventHub" /> and the lazily
        ///     provisioned scheduler / animator infrastructure become available.
        /// </summary>
        public event Action DependenciesPopulated;

        /// <summary>Raised when a profile receiver registers on this character.</summary>
        internal event Action<EmbodimentProfileReceiverRegistration> ProfileReceiverRegistered
        {
            add => _profileReceivers.Registered += value;
            remove => _profileReceivers.Registered -= value;
        }

        // ── composition ────────────────────────────────────────────────────────────

        private CharacterServiceRegistry Services
        {
            get
            {
                EnsureCollaborators();
                return _services;
            }
        }

        int IInjectable<IConvaiCharacterDependencies>.InjectionOrder => -100;

        void IInjectable<IConvaiCharacterDependencies>.InjectDependencies(IConvaiCharacterDependencies dependencies)
        {
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
            Populate(dependencies.EventHub, dependencies.Logger);
        }

        private void Awake()
        {
            hideFlags = RuntimeInfrastructureHideFlags();
            EnsureCollaborators();
            _dependencies.ResolveOptionalComponents(_facialCompositionProfileOverride);
        }

        /// <summary>
        ///     Locates or creates a context on the supplied component's character root and resolves
        ///     any already-authored infrastructure components.
        /// </summary>
        public static bool TryResolve(Component origin, out EmbodimentContext context)
        {
            context = null;
            if (origin == null) return false;

            context = origin.GetComponentInParent<EmbodimentContext>(true);
            if (context != null)
            {
                context.EnsureCollaborators();
                context._dependencies.ResolveOptionalComponents(context._facialCompositionProfileOverride);
                return true;
            }

            // Only a real character root gets a composition root, so dropping an embodiment component
            // on some unrelated GameObject does not silently grow one and turn a user mistake into a
            // working-but-wrong setup.
            //
            // Refused quietly on purpose: this overload is also the "is there one?" lookup, used by
            // callers for which a missing context is a normal answer rather than a problem — the
            // preset binding works without one. The caller that *needs* a context is the one that
            // knows it, so TryResolveFor does the reporting.
            ConvaiCharacter character = origin.GetComponentInParent<ConvaiCharacter>(true);
            if (character == null) return false;

            GameObject owner = character.gameObject;
            context = owner.GetComponent<EmbodimentContext>();
            if (context == null)
            {
                context = owner.AddComponent<EmbodimentContext>();
                context.hideFlags = RuntimeInfrastructureHideFlags();
            }

            context.EnsureCollaborators();
            context._dependencies.ResolveOptionalComponents(context._facialCompositionProfileOverride);
            return true;
        }

        /// <summary>
        ///     Resolves the context for a component that <em>needs</em> one, reporting when there is
        ///     none so the caller can disable itself and the user learns why.
        /// </summary>
        /// <remarks>
        ///     Use this from anything that is inert without a character — a module controller, a gaze
        ///     satellite, a command adapter. Use <see cref="TryResolve(Component, out EmbodimentContext)" />
        ///     instead when a missing context is a normal answer rather than a setup mistake.
        /// </remarks>
        public static bool TryResolveFor(Component owner, out EmbodimentContext context)
        {
            if (owner == null)
            {
                context = null;
                ConvaiLogger.Warning(
                    "TryResolveFor was called with a null owner reference.",
                    LogCategory.Character);
                return false;
            }

            if (TryResolve(owner, out context)) return true;

            ReportMissingCharacter(owner);
            return false;
        }

        /// <summary>
        ///     Called after dependency composition so embodiment modules have a live event hub and
        ///     logger.
        /// </summary>
        public void Populate(IEventHub eventHub, ILogger logger)
        {
            _eventHub = eventHub;
            _logger = logger;

            EnsureCollaborators();
            _dependencies.ResolveOptionalComponents(_facialCompositionProfileOverride);
            RaiseDependenciesPopulated();
        }

        // ── custom tickables ───────────────────────────────────────────────────────

        /// <summary>
        ///     Drives <paramref name="tickable" /> from this character's embodiment scheduler instead
        ///     of Unity's per-component <c>Update</c>, so its writes land in a declared place relative
        ///     to Convai's own.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Call from <c>OnEnable</c> and pair with <see cref="UnregisterTickable" /> in
        ///         <c>OnDisable</c>. Registering the same instance twice is a no-op, and registering
        ///         during a tick takes effect on the next frame.
        ///     </para>
        ///     <para>
        ///         This is the registration half of <see cref="IEmbodimentTickable" />. The scheduler
        ///         component itself stays internal — it is infrastructure, not API — so this pair is
        ///         the supported way for a project's own component to join the character's tick.
        ///     </para>
        ///     <para>
        ///         Returns <c>false</c> when there is no scheduler to join, which outside Play Mode is
        ///         the normal answer: the scheduler is provisioned at runtime. A caller that registers
        ///         in <c>OnEnable</c> and ignores the result behaves correctly.
        ///     </para>
        /// </remarks>
        /// <returns>Whether <paramref name="tickable" /> is now registered.</returns>
        public bool RegisterTickable(IEmbodimentTickable tickable)
        {
            if (tickable == null) return false;

            // A caller's OnEnable can beat this context's Awake — script execution order decides,
            // and a customer's component carries no Convai execution order. Provision collaborators
            // the same way the other lifecycle entry points do rather than dereferencing a resolver
            // that may not exist yet.
            EnsureCollaborators();

            EmbodimentTickScheduler scheduler = EnsureTickScheduler();
            if (scheduler == null) return false;

            scheduler.Register(tickable);
            return true;
        }

        /// <summary>
        ///     Stops driving <paramref name="tickable" />. Safe to call from <c>OnDisable</c>, safe
        ///     when it was never registered, and safe when the scheduler is already gone.
        /// </summary>
        public void UnregisterTickable(IEmbodimentTickable tickable)
        {
            if (tickable == null) return;
            _dependencies?.TickScheduler?.Unregister(tickable);
        }

        /// <summary>Registers an embodiment profile receiver for live preset application.</summary>
        internal void RegisterProfileReceiver(IEmbodimentProfileReceiver receiver, Component owner) =>
            _profileReceivers.Register(receiver, owner);

        /// <summary>Unregisters a profile receiver when its component disables.</summary>
        internal void UnregisterProfileReceiver(IEmbodimentProfileReceiver receiver) =>
            _profileReceivers.Unregister(receiver);

        /// <summary>Copies active profile receiver registrations into <paramref name="results" />.</summary>
        internal void GetProfileReceivers(List<EmbodimentProfileReceiverRegistration> results) =>
            _profileReceivers.CopyTo(results);

        // ── lazy infrastructure provisioning ───────────────────────────────────────

        internal static void RegisterDefaultConversationFlowSourceFactory(
            Func<EmbodimentContext, IConversationFlowSource> factory,
            string installerName = null) =>
            EmbodimentContextConversationFlowProvisioner.RegisterDefaultFactory(factory, installerName);

        /// <summary>
        ///     Marks that an embodiment module on this character wants the default conversation-flow
        ///     driver auto-provisioned when <see cref="TryEnsureConversationFlowSource" /> runs.
        /// </summary>
        internal void MarkConversationFlowDriverDemanded() => _conversationFlowDriverDemanded = true;

        /// <summary>Whether a module has signaled that auto-creating a flow driver is allowed.</summary>
        internal bool IsConversationFlowDriverDemanded => _conversationFlowDriverDemanded;

        internal bool TryEnsureConversationFlowSource()
        {
            if (ConversationFlowSource != null) return true;
            if (!UnityEngine.Application.isPlaying) return false;

            IConversationFlowSource source = EmbodimentContextConversationFlowProvisioner.CreateDefault(this);
            if (source != null && ConversationFlowSource == null)
                Provide(source);

            return ConversationFlowSource != null;
        }

        /// <summary>
        ///     Returns the currently registered speech energy provider, or attempts to bootstrap one
        ///     via the LipSync bridge if none is registered.
        /// </summary>
        /// <remarks>
        ///     The bridge is attempted at most once per context: a LipSync adapter added later
        ///     always self-registers via its own <c>OnEnable</c>, so repeated attempts would only add
        ///     per-tick reflection cost for characters that never carry a LipSync component.
        /// </remarks>
        internal ISpeechEnergyProvider EnsureSpeechEnergyProvider()
        {
            ISpeechEnergyProvider current = SpeechEnergyProvider;
            if (current != null) return current;

            if (!_speechEnergyProvisionAttempted && UnityEngine.Application.isPlaying)
            {
                _speechEnergyProvisionAttempted = true;
                EmbodimentLipSyncBridge.TryRegisterSpeechEnergyAdapter(this);
            }

            return SpeechEnergyProvider;
        }

        /// <summary>
        ///     Publishes that the character's semantic rig binding has changed. Modules use this to
        ///     rebuild cached bone / blendshape resolution without a disable-enable cycle.
        /// </summary>
        public void NotifyRigBindingChanged(IStandardRigBinding rigBinding = null)
        {
            // Rig rebinds can arrive before Awake() has run (e.g. Edit Mode AddComponent flows where
            // StandardRigBinding.Rebuild() fires immediately), so provision collaborators lazily
            // just like TryResolve() does.
            EnsureCollaborators();

            // A rig rebind is the avatar-swap signal, so re-resolve the whole infrastructure set
            // rather than only the binding: the compositor, conductor, scheduler, character and
            // dialogue adapter all belong to the hierarchy that just changed.
            _dependencies.ReResolveForHierarchyChange(_facialCompositionProfileOverride);
            _dependencies.NotifyRigBindingChanged(rigBinding);

            // A rig rebind can change which components exist under this character (e.g. an avatar
            // swap that newly introduces a LipSync component), so allow one more one-shot
            // speech-energy bridge attempt instead of being permanently stuck on the first outcome.
            _speechEnergyProvisionAttempted = false;

            RaiseRigBindingChanged(_dependencies.RigBinding);
        }

        /// <summary>Publishes that one or more embodiment-module configurations changed at runtime.</summary>
        public void NotifyEmbodimentConfigurationChanged()
        {
            Action handler = EmbodimentConfigurationChanged;
            if (handler == null) return;

            try
            {
                handler.Invoke();
            }
            catch (Exception ex)
            {
                LogEventSubscriberException(
                    ex,
                    "[EmbodimentContext] A subscriber threw while handling EmbodimentConfigurationChanged.");
            }
        }

        internal FacialBlendshapeCompositorHost EnsureCompositor() =>
            _dependencies.EnsureCompositor(_facialCompositionProfileOverride);

        internal AnimatorConductor EnsureAnimatorConductor() => _dependencies.EnsureAnimatorConductor();

        internal EmbodimentTickScheduler EnsureTickScheduler() => _dependencies.EnsureTickScheduler();

        internal IStandardRigBinding EnsureRigBinding() => _dependencies.EnsureRigBinding();

        internal IDialoguePhaseProvider EnsureDialoguePhase() =>
            _dependencies.EnsureDialoguePhase(_facialCompositionProfileOverride);

        private void EnsureCollaborators()
        {
            _services ??= new CharacterServiceRegistry(this);
            _profileReceivers.EnsureAttached(this);
            _dependencies ??= new EmbodimentDependencyResolver(this);
        }

        private void LogEventSubscriberException(Exception ex, string message)
        {
            if (_logger != null)
                _logger.Error(ex, message, LogCategory.Character);
            else
                ConvaiLogger.Exception(ex, LogCategory.Character);
        }

        /// <summary>
        ///     Reports that an embodiment component is not on a Convai character and so cannot work.
        /// </summary>
        /// <remarks>
        ///     Not de-duplicated. A static set of already-reported instance ids would be
        ///     process-global mutable state, never cleared, and keyed on ids Unity recycles, so it
        ///     could go on to silence a genuine report. The caller disables itself immediately after
        ///     this, so the message is naturally once per enable.
        /// </remarks>
        private static void ReportMissingCharacter(Component origin)
        {
            if (origin == null) return;

            EmbodimentDiagnostics.SetupError(
                $"[{origin.GetType().Name}] '{origin.gameObject.name}' is not on a Convai character, " +
                "so it has nothing to drive. Move this component onto the object with the " +
                "Convai Character component (or one of its children).");
        }

        private void RaiseDependenciesPopulated()
        {
            Action handler = DependenciesPopulated;
            if (handler == null) return;

            try
            {
                handler.Invoke();
            }
            catch (Exception ex)
            {
                LogEventSubscriberException(
                    ex,
                    "[EmbodimentContext] A subscriber threw while handling DependenciesPopulated.");
            }
        }

        private void RaiseRigBindingChanged(IStandardRigBinding rigBinding)
        {
            Action<IStandardRigBinding> handler = RigBindingChanged;
            if (handler == null) return;

            try
            {
                handler.Invoke(rigBinding);
            }
            catch (Exception ex)
            {
                LogEventSubscriberException(
                    ex,
                    "[EmbodimentContext] A subscriber threw while handling RigBindingChanged.");
            }
        }

        /// <summary>
        ///     Hide flags applied to lazily provisioned embodiment infrastructure.
        /// </summary>
        /// <remarks>
        ///     Always <see cref="HideFlags.None" />, so lazily provisioned embodiment infrastructure
        ///     stays visible in the inspector and a user can see, inspect, and debug it.
        /// </remarks>
        internal static HideFlags RuntimeInfrastructureHideFlags() => HideFlags.None;
    }
}
