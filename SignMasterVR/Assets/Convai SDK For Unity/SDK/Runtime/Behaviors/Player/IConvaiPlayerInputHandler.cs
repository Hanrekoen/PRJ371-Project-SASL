using Convai.Domain.DomainEvents.Session;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Handles session state changes from the backend before default player logic executes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Interceptor Pattern:</b> <see cref="ProcessInput" /> returns <c>bool</c> to control whether
    ///         the default SDK behavior executes. Return <c>true</c> to suppress default behavior; return
    ///         <c>false</c> to allow it to proceed.
    ///     </para>
    ///     <para>
    ///         Handlers are executed in descending <see cref="Priority" /> order. Higher priority handlers
    ///         run first and can intercept events before lower priority handlers see them.
    ///     </para>
    ///     <para>
    ///         All methods on this interface are invoked on the Unity main thread. It is safe to call
    ///         Unity APIs directly from any method implementation.
    ///     </para>
    /// </remarks>
    public interface IConvaiPlayerInputHandler
    {
        /// <summary>
        ///     Higher values run first. Handlers execute in descending order until one returns <c>true</c>.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        ///     Determines whether the handler wants to inspect the session state change.
        /// </summary>
        /// <param name="agent">The player agent associated with the session.</param>
        /// <param name="sessionState">The session state change event containing old and new states.</param>
        /// <returns>
        ///     <c>true</c> if this handler should process the state change via <see cref="ProcessInput" />;
        ///     <c>false</c> to skip this handler.
        /// </returns>
        public bool CanHandle(IConvaiPlayerAgent agent, SessionStateChanged sessionState);

        /// <summary>
        ///     Processes the session state change.
        /// </summary>
        /// <param name="agent">The player agent associated with the session.</param>
        /// <param name="sessionState">The session state change event containing old and new states.</param>
        /// <returns><c>true</c> to suppress the default behaviour; <c>false</c> to allow it.</returns>
        public bool ProcessInput(IConvaiPlayerAgent agent, SessionStateChanged sessionState);
    }
}
