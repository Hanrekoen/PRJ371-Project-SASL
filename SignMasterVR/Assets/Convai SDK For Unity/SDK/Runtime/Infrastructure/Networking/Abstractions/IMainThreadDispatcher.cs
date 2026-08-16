using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Abstraction for dispatching actions to the main/Unity thread.
    ///     Enables network callbacks to safely interact with Unity APIs.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>
        ///     Attempts to dispatch an action to the main thread.
        /// </summary>
        /// <param name="action">The action to execute on the main thread.</param>
        /// <returns>True if the action was successfully queued; false otherwise.</returns>
        public bool TryDispatch(Action action);
    }
}
