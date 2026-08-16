using Convai.Domain.Models;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Contract for extending player behaviour using the interceptor pattern.
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
    ///         <c>PlayerTranscriptReceived</c> instead.
    ///     </para>
    /// </remarks>
    public interface IConvaiPlayerBehavior
    {
        /// <summary>
        ///     Higher values run first. Behaviours execute in descending priority order.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        ///     Called once the player is fully initialised and dependency injection completes.
        /// </summary>
        public void OnPlayerInitialized(IConvaiPlayerAgent agent);

        /// <summary>
        ///     Called when the player is disabled or destroyed.
        /// </summary>
        public void OnPlayerShutdown(IConvaiPlayerAgent agent);

        /// <summary>
        ///     Inspect or replace incoming transcript text.
        /// </summary>
        /// <param name="agent">The player agent that received the transcript.</param>
        /// <param name="transcript">The transcript text content.</param>
        /// <param name="phase">The current phase of the transcription lifecycle.</param>
        /// <returns><c>true</c> to suppress the default transcript broadcast; <c>false</c> to allow it.</returns>
        public bool OnTranscriptReceived(IConvaiPlayerAgent agent, string transcript, TranscriptionPhase phase);

        /// <summary>
        ///     Invoked when microphone capture begins.
        /// </summary>
        /// <param name="agent">The player agent that started input.</param>
        /// <returns><c>true</c> to suppress the default input handling; <c>false</c> to allow it.</returns>
        public bool OnInputStarted(IConvaiPlayerAgent agent);

        /// <summary>
        ///     Invoked when microphone capture ends.
        /// </summary>
        /// <param name="agent">The player agent that stopped input.</param>
        /// <param name="producedFinalTranscript">Whether a final transcript was produced during this input session.</param>
        /// <returns><c>true</c> to suppress the default input handling; <c>false</c> to allow it.</returns>
        public bool OnInputStopped(IConvaiPlayerAgent agent, bool producedFinalTranscript);
    }
}
