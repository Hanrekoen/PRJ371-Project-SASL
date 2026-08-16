using System.Collections.Generic;
using System.Text;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Builds a compact, third-person world-fact sentence from a batch's step reports —
    ///     the text <see cref="ConvaiActionFeedbackRelay" /> stages as dynamic context or speaks.
    ///     Pure/static so it is unit-testable without a scene or dispatcher.
    /// </summary>
    internal static class ConvaiActionFeedbackComposer
    {
        /// <summary>Which outcome class a composed batch resolved to.</summary>
        internal enum OutcomeKind
        {
            /// <summary>Nothing worth reporting happened (empty batch).</summary>
            None = 0,

            /// <summary>The batch contains a hard failure (Failed/TimedOut/Canceled) step.</summary>
            Failure = 1,

            /// <summary>The batch has no hard failure (all Succeeded and/or Unhandled steps).</summary>
            Success = 2
        }

        /// <summary>Composed batch outcome: a ready-to-send fact plus the structured data behind it.</summary>
        internal readonly struct Outcome
        {
            public OutcomeKind Kind { get; }
            public string Fact { get; }
            public bool ForceSilent { get; }
            public ConvaiActionFailureReason FailureReason { get; }
            public string ActionToken { get; }
            public string TargetToken { get; }

            /// <summary>
            ///     Whether any step in this batch answered a question. Answers are delivered under
            ///     their own rules: the relay's chatter guards must not discard one.
            /// </summary>
            public bool HasAnswer { get; }

            /// <summary>
            ///     What the batch's answering steps asked for, already reduced to one decision. Only
            ///     meaningful when <see cref="HasAnswer" />.
            /// </summary>
            public ConvaiActionAnswerDelivery AnswerDelivery { get; }

            public Outcome(
                OutcomeKind kind,
                string fact,
                bool forceSilent,
                ConvaiActionFailureReason failureReason,
                string actionToken,
                string targetToken,
                bool hasAnswer = false,
                ConvaiActionAnswerDelivery answerDelivery = ConvaiActionAnswerDelivery.UseCharacterSetting)
            {
                Kind = kind;
                Fact = fact ?? string.Empty;
                ForceSilent = forceSilent;
                FailureReason = failureReason;
                ActionToken = actionToken ?? string.Empty;
                TargetToken = targetToken ?? string.Empty;
                HasAnswer = hasAnswer;
                AnswerDelivery = answerDelivery;
            }

            public static readonly Outcome None =
                new(OutcomeKind.None, string.Empty, false, ConvaiActionFailureReason.None, string.Empty, string.Empty);
        }

        /// <summary>
        ///     Composes at most one outcome for a completed batch: the first hard failure if one
        ///     occurred (Failed/TimedOut/Canceled — Unhandled steps never count as a hard failure),
        ///     otherwise a success summary of the succeeded action names. A batch that produced no
        ///     successes (Unhandled steps only) still composes an outcome, but forces
        ///     <see cref="Outcome.ForceSilent" /> so it is never voiced regardless of the configured
        ///     success feedback mode.
        /// </summary>
        /// <summary>
        ///     Composes the world-fact for a command that was dropped before it could run.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         From the character's side there is no difference worth voicing between a walk that
        ///         failed and a walk that was never started: both end with the visitor's request
        ///         unmet, and a character that stays silent about the second is a character that
        ///         appears to have ignored them. The sentence is deliberately about the world rather
        ///         than about the SDK — the developer's explanation of *why* it dropped belongs in
        ///         the console, never in the character's mouth.
        ///     </para>
        ///     <para>
        ///         Mapping the drop onto <see cref="ConvaiActionFailureReason" /> is what lets the
        ///         relay's existing authored lines cover this with no second line set to maintain.
        ///     </para>
        /// </remarks>
        internal static Outcome ComposeDrop(in ConvaiActionDropReport report)
        {
            ConvaiActionFailureReason reason = ConvaiActionDropReport.ToFailureReason(report.Reason);
            string action = string.IsNullOrEmpty(report.ActionName) ? "what was asked" : report.ActionName;

            string fact = string.IsNullOrEmpty(report.RequestedTarget)
                ? $"You were unable to {action}."
                : $"You were unable to {action}: there is no {report.RequestedTarget} you can act on.";

            return new Outcome(
                OutcomeKind.Failure,
                fact,
                false,
                reason,
                action,
                report.RequestedTarget);
        }

        internal static Outcome Compose(IReadOnlyList<ConvaiActionStepReport> reports)
        {
            if (reports == null || reports.Count == 0) return Outcome.None;

            // Answers are gathered across the whole batch before anything else is decided, because a
            // question answered early is still answered when a later step fails. Reporting only the
            // blocked door — and throwing away the reading the visitor actually asked for — is the
            // one thing this pass exists to stop.
            var answers = new List<string>();
            ConvaiActionAnswerDelivery delivery = ConvaiActionAnswerDelivery.UseCharacterSetting;
            for (int i = 0; i < reports.Count; i++)
            {
                ConvaiActionStepReport report = reports[i];
                if (report == null || !report.Result.HasAnswer) continue;

                ConvaiActionAnswerDelivery stepDelivery = report.Invocation?.Definition?.AnswerDelivery
                                                          ?? ConvaiActionAnswerDelivery.UseCharacterSetting;

                // The first answering step seeds the decision rather than being merged into a
                // default. Seeding with UseCharacterSetting instead would out-rank a lone
                // RememberOnly — deferring beats it on purpose, so an action that asked to be kept
                // quiet would have been narrated by a talkative character anyway.
                delivery = answers.Count == 0 ? stepDelivery : StrongerDelivery(delivery, stepDelivery);
                answers.Add(report.Result.Answer.Trim());
            }

            for (int i = 0; i < reports.Count; i++)
            {
                ConvaiActionStepReport report = reports[i];
                if (report == null) continue;

                ConvaiActionExecutionStatus status = report.Result.Status;
                if (status == ConvaiActionExecutionStatus.Failed ||
                    status == ConvaiActionExecutionStatus.TimedOut ||
                    status == ConvaiActionExecutionStatus.Canceled)
                {
                    return ComposeFailure(report, answers, delivery);
                }
            }

            var succeeded = new List<string>();
            var unhandled = new List<string>();
            for (int i = 0; i < reports.Count; i++)
            {
                ConvaiActionStepReport report = reports[i];
                if (report == null) continue;

                // A step that answered is described by its answer. Naming it as well would have the
                // character say it read the gauge and then say what the gauge said.
                if (report.Result.HasAnswer) continue;

                string action = ActionToken(report);
                if (report.Result.Status == ConvaiActionExecutionStatus.Succeeded)
                    succeeded.Add(action);
                else if (report.Result.Status == ConvaiActionExecutionStatus.Unhandled)
                    unhandled.Add(action);
            }

            if (succeeded.Count > 0 || answers.Count > 0)
            {
                string joined = string.Join(", ", succeeded);
                string completion = succeeded.Count > 0 ? $"You completed: {joined}." : string.Empty;

                return new Outcome(
                    OutcomeKind.Success,
                    JoinSentences(completion, answers),
                    forceSilent: false,
                    ConvaiActionFailureReason.None,
                    answers.Count > 0 && succeeded.Count == 0 ? ActionTokenOfFirstAnswer(reports) : joined,
                    string.Empty,
                    answers.Count > 0,
                    delivery);
            }

            if (unhandled.Count > 0)
            {
                string joined = string.Join(", ", unhandled);
                return new Outcome(
                    OutcomeKind.Success,
                    $"You did not perform: {joined}.",
                    forceSilent: true,
                    ConvaiActionFailureReason.None,
                    joined,
                    string.Empty);
            }

            return Outcome.None;
        }

        /// <summary>
        ///     Reduces the batch's answering steps to one delivery decision: the most talkative wins.
        /// </summary>
        /// <remarks>
        ///     A visitor who asked a question gets their answer even when the character also walked
        ///     somewhere on the way, so one step asking to be told tells the batch. Deferring
        ///     out-ranks <see cref="ConvaiActionAnswerDelivery.RememberOnly" /> deliberately: the
        ///     character's own setting may well be to speak, and an action that explicitly asked to
        ///     be remembered should silence the batch only when every answering step agrees.
        /// </remarks>
        private static ConvaiActionAnswerDelivery StrongerDelivery(
            ConvaiActionAnswerDelivery current,
            ConvaiActionAnswerDelivery candidate) =>
            DeliveryRank(candidate) > DeliveryRank(current) ? candidate : current;

        private static int DeliveryRank(ConvaiActionAnswerDelivery delivery) => delivery switch
        {
            ConvaiActionAnswerDelivery.TellThePlayer => 3,
            ConvaiActionAnswerDelivery.MentionIfRelevant => 2,
            ConvaiActionAnswerDelivery.UseCharacterSetting => 1,
            _ => 0
        };

        /// <summary>Joins a completion summary and the batch's answers into one spoken-ready fact.</summary>
        private static string JoinSentences(string completion, List<string> answers)
        {
            if (answers.Count == 0) return completion;

            var builder = new StringBuilder(completion);
            for (int i = 0; i < answers.Count; i++)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(answers[i]);
            }

            return builder.ToString();
        }

        private static string ActionTokenOfFirstAnswer(IReadOnlyList<ConvaiActionStepReport> reports)
        {
            for (int i = 0; i < reports.Count; i++)
                if (reports[i] != null && reports[i].Result.HasAnswer)
                    return ActionToken(reports[i]);

            return string.Empty;
        }

        private static Outcome ComposeFailure(
            ConvaiActionStepReport report,
            List<string> answers,
            ConvaiActionAnswerDelivery delivery)
        {
            string action = ActionToken(report);
            string target = TargetToken(report);
            ConvaiActionFailureReason reason = report.FailureReason;

            string fact = reason switch
            {
                ConvaiActionFailureReason.TargetMissing => string.IsNullOrEmpty(target)
                    ? $"You could not find what you needed to {action}."
                    : $"You tried to {action} but could not find '{target}'.",
                ConvaiActionFailureReason.TargetUnreachable => string.IsNullOrEmpty(target)
                    ? $"You tried to {action} but could not reach the target."
                    : $"You tried to reach '{target}' but could not get there.",
                ConvaiActionFailureReason.PathBlocked => string.IsNullOrEmpty(target)
                    ? "You tried to walk somewhere but the path is blocked."
                    : $"You tried to walk to '{target}' but the path is blocked.",
                ConvaiActionFailureReason.PeerMissing =>
                    $"You could not {action} — something you needed for that is missing.",
                ConvaiActionFailureReason.Timeout =>
                    $"You could not finish '{action}' in time.",
                ConvaiActionFailureReason.Interrupted =>
                    $"You stopped '{action}' before finishing it.",
                ConvaiActionFailureReason.InvalidState => BuildCustomFact(action, report.Result.Message),
                ConvaiActionFailureReason.Custom => BuildCustomFact(action, report.Result.Message),
                _ => $"You could not perform '{action}'."
            };

            // Answers first: they happened before the failure did, and reading them out after "the
            // path is blocked" would put the batch's events in the wrong order.
            string joinedAnswers = JoinSentences(string.Empty, answers);
            string factWithAnswers = joinedAnswers.Length > 0 ? $"{joinedAnswers} {fact}" : fact;

            return new Outcome(
                OutcomeKind.Failure,
                factWithAnswers,
                forceSilent: false,
                reason,
                action,
                target,
                answers.Count > 0,
                delivery);
        }

        private static string BuildCustomFact(string action, string message) =>
            string.IsNullOrWhiteSpace(message)
                ? $"You could not perform '{action}'."
                : $"You could not perform '{action}': {message}";

        private static string ActionToken(ConvaiActionStepReport report) =>
            report.Invocation?.Definition?.ActionName ?? report.Invocation?.Command?.Name ?? "that";

        private static string TargetToken(ConvaiActionStepReport report) =>
            report.Invocation?.ResolvedTarget?.Name ?? report.Invocation?.Command?.Target ?? string.Empty;
    }
}
