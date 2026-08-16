using Convai.Domain.DomainEvents.Session;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Helper base class for player input handlers with inspector-configurable priority.
    /// </summary>
    public abstract class ConvaiPlayerInputHandlerBase : MonoBehaviour, IConvaiPlayerInputHandler
    {
        /// <summary>Inspector-configurable priority used to order input handlers.</summary>
        [SerializeField] [Tooltip("Higher values execute earlier in the input handler chain.")]
        protected int priority;

        /// <inheritdoc />
        public virtual int Priority => priority;

        /// <inheritdoc />
        public abstract bool CanHandle(IConvaiPlayerAgent agent, SessionStateChanged sessionState);

        /// <inheritdoc />
        public abstract bool ProcessInput(IConvaiPlayerAgent agent, SessionStateChanged sessionState);
    }
}
