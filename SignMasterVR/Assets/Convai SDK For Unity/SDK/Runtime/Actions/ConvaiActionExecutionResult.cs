using System;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Terminal status of a single executed action step.
    /// </summary>
    public enum ConvaiActionExecutionStatus
    {
        /// <summary>The executor completed the action.</summary>
        Succeeded = 0,

        /// <summary>The executor failed or threw.</summary>
        Failed = 1,

        /// <summary>The step was canceled (batch replaced, dispatcher disabled, or destroy).</summary>
        Canceled = 2,

        /// <summary>The step exceeded its definition timeout.</summary>
        TimedOut = 3,

        /// <summary>The executor could not handle the invocation (missing rig, peer, or capability).</summary>
        Unhandled = 4
    }

    /// <summary>
    ///     Machine-readable reason an executor failed, alongside the free-text
    ///     <see cref="ConvaiActionExecutionResult.Message" />. Lets game code and the (future)
    ///     spoken-feedback relay react to *why* a step failed without parsing strings.
    /// </summary>
    public enum ConvaiActionFailureReason
    {
        /// <summary>No failure, or the reason was not classified (default for Succeeded/Unhandled).</summary>
        None = 0,

        /// <summary>The invocation required a resolved target and none was present.</summary>
        TargetMissing,

        /// <summary>A target was resolved but could not be reached (out of range, no interaction point, etc.).</summary>
        TargetUnreachable,

        /// <summary>A path to the target exists conceptually but is blocked (e.g. no valid NavMesh path).</summary>
        PathBlocked,

        /// <summary>A required peer component (controller, locomotion, rig) was not found on the character.</summary>
        PeerMissing,

        /// <summary>The executor or a dependency was in a state that could not service the request.</summary>
        InvalidState,

        /// <summary>The step exceeded its definition timeout.</summary>
        Timeout,

        /// <summary>The step was interrupted before completion (canceled, replaced, or superseded).</summary>
        Interrupted,

        /// <summary>Any other executor-specific failure conveyed only through <see cref="ConvaiActionExecutionResult.Message" />.</summary>
        Custom,

        /// <summary>A target was resolved, but it lacks the component that would let this action act on it.</summary>
        TargetNotActionable,

        /// <summary>
        ///     The character could not take the request <em>right now</em> because it is still
        ///     performing something that uses the same part of it — the same request a moment later
        ///     would normally be accepted.
        /// </summary>
        /// <remarks>
        ///     Distinct from <see cref="InvalidState" />, and the distinction is the point: a
        ///     transient refusal reported as a state problem sends whoever reads it to check a rig
        ///     that is working perfectly well.
        /// </remarks>
        Busy
    }

    /// <summary>
    ///     Result an <see cref="IConvaiActionExecutor" /> returns for one invocation.
    /// </summary>
    public readonly struct ConvaiActionExecutionResult
    {
        /// <summary>Terminal status of the step.</summary>
        public ConvaiActionExecutionStatus Status { get; }

        /// <summary>
        ///     Optional human-readable detail for the status — written for <em>you</em>, and read in
        ///     the Console, the Actions Editor and your own game code.
        /// </summary>
        /// <remarks>
        ///     This is diagnostic text and the Convai Character never hears it. When an action finds
        ///     something out that the player asked for, that sentence goes in <see cref="Answer" />
        ///     instead — see <see cref="Answered" />.
        /// </remarks>
        public string Message { get; }

        /// <summary>
        ///     What this action found out, written as one plain sentence the Convai Character could
        ///     read aloud — <c>"The power generator reads 62 kilowatts."</c> Empty for actions that
        ///     perform a visible act rather than answer a question.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is the only part of a result the character itself is told about. Whether it is
        ///         spoken out loud, quietly remembered, or left to the character's judgement is not
        ///         decided here — it is authored per action ("When it finishes" in the
        ///         Actions Editor) and, failing that, by the character's Convai Action Feedback Relay.
        ///         An action reports what it found; it does not decide whether that gets said.
        ///     </para>
        ///     <para>
        ///         Write it in the third person, as something true about the world rather than a note
        ///         about the code: <c>"Two of the four crates are still sealed."</c>, not
        ///         <c>"count=2"</c>.
        ///     </para>
        /// </remarks>
        public string Answer { get; }

        /// <summary>Whether this result carries an <see cref="Answer" /> for the character.</summary>
        public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);

        /// <summary>Exception captured when the executor threw.</summary>
        public Exception Exception { get; }

        /// <summary>Machine-readable failure reason; <see cref="ConvaiActionFailureReason.None" /> for non-failures.</summary>
        public ConvaiActionFailureReason FailureReason { get; }

        private ConvaiActionExecutionResult(
            ConvaiActionExecutionStatus status,
            string message = null,
            Exception exception = null,
            ConvaiActionFailureReason failureReason = ConvaiActionFailureReason.None,
            string answer = null)
        {
            Status = status;
            Message = message;
            Exception = exception;
            FailureReason = failureReason;
            Answer = answer;
        }

        /// <summary>Creates a success result.</summary>
        public static ConvaiActionExecutionResult Succeeded(string message = null) =>
            new(ConvaiActionExecutionStatus.Succeeded, message);

        /// <summary>
        ///     Creates a success result that answers a question the player asked — the shape every
        ///     action that <em>finds something out</em> returns.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Use this instead of <see cref="Succeeded(string)" /> whenever the point of the
        ///         action is the thing it discovered: reading a gauge, counting a group, measuring a
        ///         distance, checking whether something is true. A plain
        ///         <c>Succeeded("the valve reads 40")</c> tells the Console and nobody else, and the
        ///         player is left listening to a character that went quiet.
        ///     </para>
        ///     <para>
        ///         Returning an answer does not make the character speak. That is decided by the
        ///         action's <c>When it finishes</c> setting and the character's Convai Action
        ///         Feedback Relay; an answer that is not spoken still reaches the character's own
        ///         memory, so it can bring the fact up later.
        ///     </para>
        /// </remarks>
        /// <param name="answer">
        ///     One plain sentence stating what was found, written as something a character could say.
        /// </param>
        /// <param name="message">
        ///     Optional separate diagnostic detail for the Console. Defaults to
        ///     <paramref name="answer" />, which is almost always what you want.
        /// </param>
        public static ConvaiActionExecutionResult Answered(string answer, string message = null) =>
            new(ConvaiActionExecutionStatus.Succeeded, message ?? answer, answer: answer);

        /// <summary>
        ///     Creates a failure result with an unclassified reason: <see cref="ConvaiActionFailureReason.Custom" />
        ///     when <paramref name="message" /> is non-empty, otherwise <see cref="ConvaiActionFailureReason.None" />.
        ///     Prefer the <see cref="Failed(string,ConvaiActionFailureReason,Exception)" /> overload in new code so
        ///     callers can react to structured failure reasons.
        /// </summary>
        public static ConvaiActionExecutionResult Failed(string message = null, Exception exception = null) =>
            new(ConvaiActionExecutionStatus.Failed, message, exception,
                string.IsNullOrEmpty(message) ? ConvaiActionFailureReason.None : ConvaiActionFailureReason.Custom);

        /// <summary>Creates a failure result with a structured, machine-readable reason.</summary>
        public static ConvaiActionExecutionResult Failed(
            string message,
            ConvaiActionFailureReason reason,
            Exception exception = null) =>
            new(ConvaiActionExecutionStatus.Failed, message, exception, reason);

        /// <summary>Creates a canceled result (<see cref="ConvaiActionFailureReason.Interrupted" />).</summary>
        public static ConvaiActionExecutionResult Canceled() =>
            new(ConvaiActionExecutionStatus.Canceled, failureReason: ConvaiActionFailureReason.Interrupted);

        /// <summary>Creates a timed-out result (<see cref="ConvaiActionFailureReason.Timeout" />).</summary>
        /// <param name="message">
        ///     What ran out of time and what to do about it. Worth passing: a timeout with no message
        ///     is reported as a bare status, and the action that never finished is exactly the one
        ///     whose report has to name itself.
        /// </param>
        public static ConvaiActionExecutionResult TimedOut(string message = null) =>
            new(ConvaiActionExecutionStatus.TimedOut, message, failureReason: ConvaiActionFailureReason.Timeout);

        /// <summary>Creates an unhandled result (executor cannot service the invocation).</summary>
        public static ConvaiActionExecutionResult Unhandled(string message = null) =>
            new(ConvaiActionExecutionStatus.Unhandled, message);

        /// <inheritdoc />
        public override string ToString() => Exception != null
            ? $"{Status}: {Message ?? Exception.Message}"
            : string.IsNullOrEmpty(Message) ? Status.ToString() : $"{Status}: {Message}";
    }

    /// <summary>
    ///     Completed-step report emitted by <see cref="ConvaiActionDispatcher.OnStepCompleted" />.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionStepReport
    {
        /// <summary>The invocation the report describes.</summary>
        public ConvaiActionInvocation Invocation { get; }

        /// <summary>Raw executor result for the step.</summary>
        public ConvaiActionExecutionResult Result { get; }

        /// <summary>Machine-readable failure reason from <see cref="Result" />; passthrough for game code.</summary>
        public ConvaiActionFailureReason FailureReason => Result.FailureReason;

        /// <summary>Whether this step aborted the remaining batch.</summary>
        public bool BatchAborted { get; }

        /// <summary>Success detail, or the failure message for non-success statuses.</summary>
        public string Message { get; }

        /// <summary>Failure detail including the batch consequence; empty on success.</summary>
        public string FailureMessage { get; }

        internal ConvaiActionStepReport(
            ConvaiActionInvocation invocation,
            ConvaiActionExecutionResult result,
            bool batchAborted,
            string failureMessage)
        {
            Invocation = invocation;
            Result = result;
            BatchAborted = batchAborted;
            FailureMessage = failureMessage ?? string.Empty;
            Message = result.Status == ConvaiActionExecutionStatus.Succeeded
                ? result.Message ?? string.Empty
                : FailureMessage;
        }
    }
}
