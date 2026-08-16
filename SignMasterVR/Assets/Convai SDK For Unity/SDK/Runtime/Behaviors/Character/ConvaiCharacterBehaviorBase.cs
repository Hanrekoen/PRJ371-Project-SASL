using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Convenience base class for Character behaviours providing default no-op implementations and inspector-configurable
    ///     priority.
    /// </summary>
    public abstract class ConvaiCharacterBehaviorBase : MonoBehaviour, IConvaiCharacterBehavior
    {
        /// <summary>Inspector-configurable priority used to order behaviors.</summary>
        [SerializeField] [Tooltip("Higher values execute earlier in the behaviour chain.")]
        protected int priority;

        /// <inheritdoc />
        public virtual int Priority => priority;

        /// <inheritdoc />
        public virtual void OnCharacterInitialized(IConvaiCharacterAgent agent) { }

        /// <inheritdoc />
        public virtual void OnCharacterShutdown(IConvaiCharacterAgent agent) { }

        /// <inheritdoc />
        public virtual bool OnTranscriptReceived(IConvaiCharacterAgent agent, string transcript, bool isFinal) => false;

        /// <inheritdoc />
        public virtual bool OnSpeechStarted(IConvaiCharacterAgent agent) => false;

        /// <inheritdoc />
        public virtual bool OnSpeechStopped(IConvaiCharacterAgent agent) => false;

        /// <inheritdoc />
        public virtual bool OnTurnCompleted(IConvaiCharacterAgent agent, bool wasInterrupted) => false;

        /// <inheritdoc />
        public virtual void OnCharacterReady(IConvaiCharacterAgent agent) { }
    }
}
