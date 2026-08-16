using System;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Why one action command never ran.
    /// </summary>
    /// <remarks>
    ///     Every value here is a place a command can disappear without reaching an executor, and
    ///     therefore without reaching any public event, any spoken failure, or any row in the action
    ///     tooling. Naming them as one closed set is what makes "nothing happened" answerable: the
    ///     question is no longer whether a command was dropped but which of these seven doors it
    ///     went through.
    /// </remarks>
    internal enum ConvaiActionDropReason
    {
        /// <summary>The payload entry was not a readable action command.</summary>
        MalformedEntry = 0,

        /// <summary>No character agent was available to interpret the command.</summary>
        RuntimeSourceUnavailable = 1,

        /// <summary>
        ///     The named action has no executable definition on this character — unknown name, or an
        ///     action with no Action Behavior bound to it.
        /// </summary>
        UnknownOrUnexecutableAction = 2,

        /// <summary>The action needs a target and none of the names it sent resolved to one.</summary>
        RequiredTargetUnresolved = 3,

        /// <summary>A Reference parameter named something that is not a registered target.</summary>
        ReferenceParameterUnresolved = 4,

        /// <summary>
        ///     The dispatcher's batch policy discarded the batch because earlier work was still
        ///     running.
        /// </summary>
        QueueBusy = 5,

        /// <summary>The dispatcher could not accept work: no character, or the component is disabled.</summary>
        DispatcherUnavailable = 6,

        /// <summary>
        ///     The scene changed between the command being admitted and being performed, so it will
        ///     act on a different target than the one it was judged on.
        /// </summary>
        /// <remarks>
        ///     The only entry here that is not a drop. Nothing was discarded — the command runs, on
        ///     the freshest answer, which is correct. It is reported through the same channel because
        ///     it is the same kind of fact: something happened to a command that nobody could see.
        /// </remarks>
        TargetDrifted = 7
    }

    /// <summary>
    ///     One dropped action command, with enough detail to act on without reproducing the session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An aggregate count is not a diagnosis. <c>rejected=1,
    ///         reasons=required_target_unresolved:1</c> says a target did not resolve and never says
    ///         which one, so it separates a two-minute fix (the model said "the gallery", the scene
    ///         calls it "Gallery Room", add an alias) from an afternoon of guesswork by exactly the
    ///         piece of information nothing else in the pipeline prints.
    ///     </para>
    ///     <para>
    ///         So a report carries four things together, and is not worth emitting without all four:
    ///         which action, what it asked for, why that failed, and what was available instead.
    ///         <see cref="Explanation" /> is the sentence a user reads; the structured fields are
    ///         what tooling groups and filters on.
    ///     </para>
    ///     <para>
    ///         Privacy-safe by construction: names the character already offers and the name the
    ///         backend sent, never the raw payload.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiActionDropReport
    {
        internal ConvaiActionDropReport(
            ConvaiActionDropReason reason,
            string actionName,
            string requestedTarget,
            string offeredTargets,
            string explanation)
        {
            Reason = reason;
            ActionName = actionName ?? string.Empty;
            RequestedTarget = requestedTarget ?? string.Empty;
            OfferedTargets = offeredTargets ?? string.Empty;
            Explanation = explanation ?? string.Empty;
        }

        /// <summary>Which door the command went through.</summary>
        public ConvaiActionDropReason Reason { get; }

        /// <summary>Action the command named, as far as it could be read.</summary>
        public string ActionName { get; }

        /// <summary>
        ///     The place, prop or person the command asked for. Empty when it named nothing — which
        ///     is a different fault from naming something unknown, and needs the opposite fix.
        /// </summary>
        public string RequestedTarget { get; }

        /// <summary>What the character could have been asked to act on, bounded for display.</summary>
        public string OfferedTargets { get; }

        /// <summary>The whole story in one sentence, ending in what to do about it.</summary>
        public string Explanation { get; }

        /// <summary>Stable identifier used by log throttling so one fault is not reported per turn.</summary>
        public string Signature => $"{(int)Reason}|{ActionName}|{RequestedTarget}";

        public override string ToString() => Explanation;

        /// <summary>
        ///     The stable wire name of a reason, as it appears in the filter summary line and in the
        ///     diagnostic event.
        /// </summary>
        /// <remarks>
        ///     These strings predate the enum and are read by existing tooling and tests, so they are
        ///     part of the contract rather than a formatting choice. Defining them here — and having
        ///     <c>ConvaiActionResponseParser</c>'s public constants read from here — keeps one spelling
        ///     of each reason instead of two that can drift apart.
        /// </remarks>
        internal static string ReasonKey(ConvaiActionDropReason reason) => reason switch
        {
            ConvaiActionDropReason.MalformedEntry => "malformed_entry",
            ConvaiActionDropReason.RuntimeSourceUnavailable => "runtime_source_unavailable",
            ConvaiActionDropReason.UnknownOrUnexecutableAction => "unknown_or_unexecutable_action",
            ConvaiActionDropReason.RequiredTargetUnresolved => "required_target_unresolved",
            ConvaiActionDropReason.ReferenceParameterUnresolved => "reference_parameter_unresolved",
            ConvaiActionDropReason.QueueBusy => "queue_busy",
            ConvaiActionDropReason.DispatcherUnavailable => "dispatcher_unavailable",
            ConvaiActionDropReason.TargetDrifted => "target_drifted",
            _ => "unknown"
        };

        /// <summary>
        ///     How a drop reads to the character, for projects that let dropped commands reach the
        ///     spoken-feedback relay.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two vocabularies stay separate — <see cref="ConvaiActionDropReason" /> is about
        ///         a command that never ran, <see cref="ConvaiActionFailureReason" /> about one that
        ///         ran and failed — but a character has no use for that distinction: from where it
        ///         stands, not finding the gallery is not finding the gallery. This is the one place
        ///         the two meet, so the relay's authored lines cover drops without a second line set
        ///         to write and keep in step.
        ///     </para>
        ///     <para>
        ///         <see cref="ConvaiActionDropReason.QueueBusy" /> maps to
        ///         <see cref="ConvaiActionFailureReason.Busy" /> rather than to a state problem
        ///         because that is exactly what it is: the same request a moment later would be
        ///         accepted.
        ///     </para>
        /// </remarks>
        internal static ConvaiActionFailureReason ToFailureReason(ConvaiActionDropReason reason) => reason switch
        {
            ConvaiActionDropReason.RequiredTargetUnresolved => ConvaiActionFailureReason.TargetMissing,
            ConvaiActionDropReason.ReferenceParameterUnresolved => ConvaiActionFailureReason.TargetMissing,
            ConvaiActionDropReason.QueueBusy => ConvaiActionFailureReason.Busy,
            ConvaiActionDropReason.UnknownOrUnexecutableAction => ConvaiActionFailureReason.InvalidState,
            ConvaiActionDropReason.DispatcherUnavailable => ConvaiActionFailureReason.InvalidState,
            ConvaiActionDropReason.RuntimeSourceUnavailable => ConvaiActionFailureReason.InvalidState,
            _ => ConvaiActionFailureReason.Custom
        };
    }
}
