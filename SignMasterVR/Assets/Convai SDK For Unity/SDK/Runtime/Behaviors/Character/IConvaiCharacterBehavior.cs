namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Contract for extending Character behaviour using the interceptor pattern.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Interceptor Pattern:</b> Methods returning <c>bool</c> act as interceptors. Return <c>true</c>
    ///         to suppress the default SDK behavior and prevent further behaviors in the chain from executing.
    ///         Return <c>false</c> to allow the default behavior to proceed.
    ///     </para>
    ///     <para>
    ///         Behaviors are executed in descending <see cref="Priority" /> order. Higher priority behaviors
    ///         run first and can intercept events before lower priority behaviors see them.
    ///     </para>
    ///     <para>
    ///         All methods on this interface are invoked on the Unity main thread. It is safe to call
    ///         Unity APIs directly from any method implementation.
    ///     </para>
    ///     <para>
    ///         For fire-and-forget event handling without interception, subscribe to EventHub events like
    ///         <c>CharacterTranscriptReceived</c> instead.
    ///     </para>
    /// </remarks>
    public interface IConvaiCharacterBehavior
    {
        /// <summary>
        ///     Higher values run first. Behaviours execute in descending priority order.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        ///     Called once the Character is fully initialised and dependency injection completes.
        /// </summary>
        public void OnCharacterInitialized(IConvaiCharacterAgent agent);

        /// <summary>
        ///     Called when the Character is disabled or destroyed.
        /// </summary>
        public void OnCharacterShutdown(IConvaiCharacterAgent agent);

        /// <summary>
        ///     Inspect or replace incoming transcript text.
        /// </summary>
        /// <param name="agent">The Character agent that received the transcript.</param>
        /// <param name="transcript">The transcript text content.</param>
        /// <param name="isFinal">Whether this is the final transcript or an interim result.</param>
        /// <returns><c>true</c> to suppress the default transcript broadcast; <c>false</c> to allow it.</returns>
        public bool OnTranscriptReceived(IConvaiCharacterAgent agent, string transcript, bool isFinal);

        /// <summary>
        ///     Invoked when the Character begins speaking.
        /// </summary>
        /// <param name="agent">The Character agent that started speaking.</param>
        /// <returns><c>true</c> to suppress the default speech-start pipeline; <c>false</c> to allow it.</returns>
        public bool OnSpeechStarted(IConvaiCharacterAgent agent);

        /// <summary>
        ///     Invoked when the Character stops speaking.
        /// </summary>
        /// <param name="agent">The Character agent that stopped speaking.</param>
        /// <returns><c>true</c> to suppress the default speech-stop pipeline; <c>false</c> to allow it.</returns>
        public bool OnSpeechStopped(IConvaiCharacterAgent agent);

        /// <summary>
        ///     Invoked when the Character completes its full turn (all audio played).
        /// </summary>
        /// <param name="agent">The Character agent that completed the turn.</param>
        /// <param name="wasInterrupted">True if the turn ended due to interruption.</param>
        /// <returns><c>true</c> to suppress the default turn-complete pipeline; <c>false</c> to allow it.</returns>
        public bool OnTurnCompleted(IConvaiCharacterAgent agent, bool wasInterrupted);

        /// <summary>
        ///     Informational callback when the backend signals that the Character is ready.
        /// </summary>
        public void OnCharacterReady(IConvaiCharacterAgent agent);
    }
}
