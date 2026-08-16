using Convai.Domain.Models;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Convenience base class for player behaviours providing default no-op implementations and inspector-configurable
    ///     priority.
    /// </summary>
    public abstract class ConvaiPlayerBehaviorBase : MonoBehaviour, IConvaiPlayerBehavior
    {
        /// <summary>Inspector-configurable priority used to order behaviors.</summary>
        [SerializeField] [Tooltip("Higher values execute earlier in the behaviour chain.")]
        protected int priority;

        /// <inheritdoc />
        public virtual int Priority => priority;

        /// <inheritdoc />
        public virtual void OnPlayerInitialized(IConvaiPlayerAgent agent) { }

        /// <inheritdoc />
        public virtual void OnPlayerShutdown(IConvaiPlayerAgent agent) { }

        /// <inheritdoc />
        public virtual bool
            OnTranscriptReceived(IConvaiPlayerAgent agent, string transcript, TranscriptionPhase phase) => false;

        /// <inheritdoc />
        public virtual bool OnInputStarted(IConvaiPlayerAgent agent) => false;

        /// <inheritdoc />
        public virtual bool OnInputStopped(IConvaiPlayerAgent agent, bool producedFinalTranscript) => false;
    }
}
