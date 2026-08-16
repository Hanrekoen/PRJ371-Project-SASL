namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Handles generated Convai responses before they flow into the default Character transcript pipeline.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Interceptor Pattern:</b> <see cref="ProcessResponse" /> returns <c>bool</c> to control whether
    ///         the default SDK behavior executes. Return <c>true</c> to suppress the default transcript broadcast;
    ///         return <c>false</c> to allow it to proceed.
    ///     </para>
    ///     <para>
    ///         Handlers are executed in descending <see cref="Priority" /> order. Higher priority handlers
    ///         run first and can intercept responses before lower priority handlers see them.
    ///     </para>
    ///     <para>
    ///         All methods on this interface are invoked on the Unity main thread. It is safe to call
    ///         Unity APIs directly from any method implementation.
    ///     </para>
    /// </remarks>
    public interface IConvaiResponseHandler
    {
        /// <summary>
        ///     Higher values run first. Handlers execute in descending order until one returns <c>true</c>.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        ///     Determines whether this handler wants to inspect the response.
        /// </summary>
        /// <param name="agent">The Character agent that received the response.</param>
        /// <param name="text">The response text content.</param>
        /// <param name="isFinal">Whether this is the final response chunk or an interim result.</param>
        /// <returns>
        ///     <c>true</c> if this handler should process the response via <see cref="ProcessResponse" />;
        ///     <c>false</c> to skip this handler.
        /// </returns>
        public bool CanHandle(IConvaiCharacterAgent agent, string text, bool isFinal);

        /// <summary>
        ///     Processes the response.
        /// </summary>
        /// <param name="agent">The Character agent that received the response.</param>
        /// <param name="text">The response text content.</param>
        /// <param name="isFinal">Whether this is the final response chunk or an interim result.</param>
        /// <returns><c>true</c> to suppress the default transcript broadcast; <c>false</c> to allow it.</returns>
        public bool ProcessResponse(IConvaiCharacterAgent agent, string text, bool isFinal);
    }
}
