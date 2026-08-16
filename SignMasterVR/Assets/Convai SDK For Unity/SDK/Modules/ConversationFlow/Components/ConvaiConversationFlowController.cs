using System;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Readings;
using Convai.Modules.ConversationFlow.Core;
using Convai.Modules.ConversationFlow.Profiles;
using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Components;
using UnityEngine;

namespace Convai.Modules.ConversationFlow.Components
{
    /// <summary>
    ///     MonoBehaviour front-end for the ConversationFlow module. Bridges the
    ///     <see cref="IEventHub" /> signal stream and the per-frame tick into the pure POCO
    ///     state machine.
    /// </summary>
    [EmbodimentModule(ModuleIds.ConversationFlow, "Conversation Flow",
        Description = "Tracks whether the character is idle, listening, thinking or talking.",
        Absence = "the other features cannot tell listening apart from speaking, and fall back to " +
                  "their neutral behaviour.",
        Order = 5)]
    [AddComponentMenu("Convai/Embodiment/Conversation Flow")]
    [DisallowMultipleComponent]
    public sealed class ConvaiConversationFlowController : ConvaiCharacterModule<ConvaiConversationFlowProfile>,
        IConversationFlowSource,
        IEmbodimentTickable
    {
        private ConversationFlowStateMachine _stateMachine;
        private ConversationFlowSignalAggregator _aggregator;
        private bool _dependenciesChangedHandlerRegistered;
        private bool _scopeChangeHandlerRegistered;
        private bool _lastAcceptingUnscoped = true;

        private ConvaiCharacter _character;

        /// <inheritdoc />
        public DialogueStateReading Current => _stateMachine?.Current ?? DialogueStateReading.Idle;

        /// <inheritdoc />
        public event Action<DialogueStateReading> Changed;

        /// <inheritdoc />
        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <inheritdoc />
        protected override string ProfileModuleId => ModuleIds.ConversationFlow;

        /// <inheritdoc />
        protected override System.Func<ConvaiConversationFlowProfile> DefaultProfileFactory => ConvaiConversationFlowProfile.CreateDefault;

        protected override void Awake()
        {
            base.Awake();

            _stateMachine = new ConversationFlowStateMachine();
            _stateMachine.Changed += OnStateMachineChanged;

            _character = GetComponentInParent<ConvaiCharacter>(true);
            _aggregator = new ConversationFlowSignalAggregator(ResolveCharacterId());
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;

            ProvideService<IConversationFlowSource>(this);
            Context.DependenciesPopulated += HandleDependenciesPopulated;
            _dependenciesChangedHandlerRegistered = true;
            Context.EnsureTickScheduler()?.Register(this);

            ResetRuntimeState();
            RefreshRuntimeBindings();

            ConvaiConversationFlowDriverRegistry.Register(this);
            ConvaiConversationFlowDriverRegistry.ScopeChanged += HandleDriverScopeChanged;
            _scopeChangeHandlerRegistered = true;
            RebindPlayerScope();
        }

        protected override void OnDisable()
        {
            if (_scopeChangeHandlerRegistered)
            {
                ConvaiConversationFlowDriverRegistry.ScopeChanged -= HandleDriverScopeChanged;
                _scopeChangeHandlerRegistered = false;
            }
            ConvaiConversationFlowDriverRegistry.Unregister(this);

            if (_dependenciesChangedHandlerRegistered && Context != null)
            {
                Context.DependenciesPopulated -= HandleDependenciesPopulated;
                _dependenciesChangedHandlerRegistered = false;
            }

            _aggregator?.Detach();
            // Released by the base class in base.OnDisable().
            Context?.TickScheduler?.Unregister(this);
            ResetRuntimeState();

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_stateMachine != null)
                _stateMachine.Changed -= OnStateMachineChanged;

            base.OnDestroy();
        }

        /// <inheritdoc />
        protected override void OnProfileApplied(ConvaiConversationFlowProfile newProfile)
        {
            ResetRuntimeState();
            if (Context != null && isActiveAndEnabled)
                RefreshRuntimeBindings();
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (_aggregator == null || _stateMachine == null) return;

            // An errand the character is running counts as engagement, so a Walk To or Follow
            // Me does not decay to Idle in silence. Absent seam → false → previous behaviour.
            bool performingAction = Context?.ActionActivitySource?.IsPerformingAction ?? false;
            ConversationFlowInputs inputs = _aggregator.Sample(performingAction);
            ConversationFlowTimings timings = EffectiveProfile.ToTimings();
            _stateMachine.Tick(inputs, timings, deltaTime);
        }

        private void OnStateMachineChanged(DialogueStateReading reading)
        {
            Changed?.Invoke(reading);
        }

        private void HandleDependenciesPopulated()
        {
            RefreshRuntimeBindings();
        }

        private void HandleDriverScopeChanged() => RebindPlayerScope();

        private void RefreshRuntimeBindings()
        {
            if (Context == null || _aggregator == null) return;

            Context.EnsureTickScheduler()?.Register(this);
            _aggregator.SetCharacterId(ResolveCharacterId());
            RebindPlayerScope();
            _aggregator.Attach(Context.EventHub, Context.DialoguePhase);

            if (_character != null && _character.IsCharacterReady)
                _aggregator.SetCharacterReady(true);
        }

        private string ResolveCharacterId()
        {
            if (Context != null && Context.Character != null)
                _character = Context.Character;

            if (_character == null)
                _character = GetComponentInParent<ConvaiCharacter>(true);
            if (_character == null)
                _character = GetComponentInChildren<ConvaiCharacter>(true);

            return _character != null ? _character.CharacterId : null;
        }

        private void RebindPlayerScope()
        {
            if (_aggregator == null) return;

            bool acceptUnscoped = ConvaiConversationFlowDriverRegistry.ActiveCount <= 1;
            _aggregator.SetAcceptUnscopedPlayerSignals(acceptUnscoped);

            if (!acceptUnscoped && _lastAcceptingUnscoped && UnityEngine.Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[ConvaiConversationFlowController] '{name}' is ignoring unscoped player speech/transcript events " +
                    "because multiple conversation flow drivers are active. Provide a scoped conversation target " +
                    "to drive per-character player-turn state in multi-character scenes.",
                    this);
            }

            _lastAcceptingUnscoped = acceptUnscoped;
        }

        private void ResetRuntimeState()
        {
            _aggregator?.Reset();
            _stateMachine?.Reset();
        }
    }
}
