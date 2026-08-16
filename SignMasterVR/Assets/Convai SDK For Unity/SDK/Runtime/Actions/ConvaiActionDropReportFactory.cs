using System.Collections.Generic;
using System.Text;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Writes the sentence a developer reads when an action command was dropped.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One owner for all of them, because a dropped command is diagnosed by its wording and
    ///         wording written at the call site gets copied: the first version of this lived inside
    ///         the required-target check and covered one reason out of seven, which is how the other
    ///         six stayed silent long after the problem they cause was understood.
    ///     </para>
    ///     <para>
    ///         Every sentence answers the same four questions in the same order — which action, what
    ///         it asked for, why that failed, and what to do — so that reading the second one takes no
    ///         effort once the first has been read. None of them is built unless
    ///         <see cref="ConvaiActionDropCollector.WantsDetail" /> is true.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionDropReportFactory
    {
        /// <summary>How many target names a sentence lists before summarizing the rest.</summary>
        private const int OfferedNameLimit = 12;

        /// <summary>
        ///     The action named a target that matches nothing, or named no target at all. The two
        ///     read differently on purpose: one is fixed with an alias, the other by rewriting the
        ///     action's description, and telling a developer to do the first when they need the
        ///     second costs them the afternoon.
        /// </summary>
        internal static ConvaiActionDropReport RequiredTargetUnresolved(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetOutcome outcome,
            ConvaiResolvedActionTarget nearMiss)
        {
            string action = ResolveActionName(command, definition);
            string requested = ResolveRequestedTarget(command);
            string offered = DescribeOfferedTargets(actionConfig);
            string explanation;

            if (requested.Length == 0)
            {
                explanation =
                    $"Dropped '{action}': it needs something to act on and the Convai Character named " +
                    "nothing. The action was chosen but no place, object or person came with it — " +
                    "usually the action's description does not make clear that it takes one. " +
                    $"Offered right now: {offered}.";
            }
            else if (outcome == ConvaiActionTargetOutcome.WrongKind && nearMiss != null)
            {
                string found = nearMiss.Kind == ConvaiActionTargetKind.Character ? "a person" : "an object";
                string wanted = definition?.TargetRequirement == ConvaiActionTargetRequirement.Character
                    ? "a person"
                    : "an object";
                explanation =
                    $"Dropped '{action}': it asked for '{requested}', and this character does have " +
                    $"something by that name — but it is {found} and the action needs {wanted}. Point " +
                    "the request at the right entry, or change what kind of target the action asks for.";
            }
            else if (outcome == ConvaiActionTargetOutcome.NotInTheScene)
            {
                explanation =
                    $"Dropped '{action}': '{requested}' is a name this character knows, but nothing in " +
                    "the scene answers to it, so there is nothing to act on. Link a GameObject to that " +
                    "entry — or, if it is meant to be talked about and never acted on, tick Text Only " +
                    "so it stops being offered as a target.";
            }
            else
            {
                explanation =
                    $"Dropped '{action}': it asked for '{requested}', which matches nothing this " +
                    $"character offers. Add '{requested}' as an alias on the intended Convai Action " +
                    $"Target, or rename that target. Offered right now: {offered}.";
            }

            return new ConvaiActionDropReport(
                ConvaiActionDropReason.RequiredTargetUnresolved, action, requested, offered, explanation);
        }

        /// <summary>
        ///     The action name has no executable definition here. Two very different causes share this
        ///     door — a name this character does not have, and a name it does have with no Action
        ///     Behavior bound to it — so the sentence names both rather than making the reader guess.
        /// </summary>
        internal static ConvaiActionDropReport UnknownOrUnexecutableAction(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            string action = ResolveActionName(command, definition);
            string known = DescribeExecutableActions(definitions);

            string explanation = definition == null
                ? $"Dropped '{action}': this character has no action by that name, so nothing could " +
                  "run it. Either the Convai Character was told about an action Unity does not have, " +
                  $"or the names differ. Runnable right now: {known}."
                : $"Dropped '{action}': the action exists but has no Action Behavior bound to it, so " +
                  "there is nothing to run. Bind a behavior to it in the Actions editor, or remove it " +
                  $"from what this character offers. Runnable right now: {known}.";

            return new ConvaiActionDropReport(
                ConvaiActionDropReason.UnknownOrUnexecutableAction, action, string.Empty, known, explanation);
        }

        /// <summary>A Reference parameter named something that is not a registered target.</summary>
        internal static ConvaiActionDropReport ReferenceParameterUnresolved(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            string parameterName,
            string requestedValue)
        {
            string action = ResolveActionName(command, definition);
            string requested = ConvaiActionText.Normalize(requestedValue);
            string offered = DescribeOfferedTargets(actionConfig);

            string explanation =
                $"Dropped '{action}': its '{parameterName}' parameter asked for '{requested}', which " +
                "is not one of this character's targets. Add it as an alias on the intended Convai " +
                $"Action Target, or rename that target. Offered right now: {offered}.";

            return new ConvaiActionDropReport(
                ConvaiActionDropReason.ReferenceParameterUnresolved, action, requested, offered, explanation);
        }

        /// <summary>An entry in the response payload could not be read as an action command.</summary>
        internal static ConvaiActionDropReport MalformedEntry(int howMany) =>
            new(ConvaiActionDropReason.MalformedEntry,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Dropped {howMany} action {(howMany == 1 ? "entry" : "entries")} that could not be read " +
                "as a command. The Convai Character sent something this SDK version does not " +
                "understand; if the character is otherwise healthy this is usually safe to ignore, and " +
                "worth reporting if it repeats.");

        /// <summary>Commands arrived for a participant with no character able to interpret them.</summary>
        internal static ConvaiActionDropReport RuntimeSourceUnavailable(int howMany, string characterId) =>
            new(ConvaiActionDropReason.RuntimeSourceUnavailable,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Dropped {howMany} action command(s): no Convai Character in the scene is answering for " +
                $"'{characterId}', so there was nothing to run them. This is a wiring problem rather than " +
                "an authoring one — the commands arrived correctly and had nowhere to go.");

        /// <summary>
        ///     The dispatcher's batch policy discarded a batch because earlier work was still running.
        /// </summary>
        internal static ConvaiActionDropReport QueueBusy(string policyName, string currentActionName)
        {
            string running = string.IsNullOrEmpty(currentActionName)
                ? "an earlier batch"
                : $"'{currentActionName}'";

            return new ConvaiActionDropReport(
                ConvaiActionDropReason.QueueBusy,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Dropped an incoming action batch: {running} is still running and this character's " +
                $"Batch Policy is {policyName}, which discards anything that arrives meanwhile. The " +
                "commands were never queued. Change the policy to queue or replace them if arriving " +
                "work should not be lost.");
        }

        /// <summary>
        ///     The target moved out from under a command between admission and dispatch.
        /// </summary>
        /// <remarks>
        ///     Worded as an observation rather than a fault, because it is one: the command was
        ///     judged against the scene as it was and will run against the scene as it is, and both
        ///     of those are right. What was wrong was that it happened in silence.
        /// </remarks>
        internal static ConvaiActionDropReport TargetDrifted(
            string actionName, string admittedTargetName, string currentDescription)
        {
            string action = string.IsNullOrEmpty(actionName) ? "An action" : $"'{actionName}'";
            return new ConvaiActionDropReport(
                ConvaiActionDropReason.TargetDrifted,
                actionName ?? string.Empty,
                admittedTargetName ?? string.Empty,
                string.Empty,
                $"{action} was accepted for '{admittedTargetName}', but by the time it ran that name " +
                $"resolved to {currentDescription}. The scene changed while the command was waiting — " +
                "the target was withdrawn or destroyed, or another target of the same name became the " +
                "nearer one. It ran against the current answer, which is the right one; this is said " +
                "only because nothing else would have said it.");
        }

        /// <summary>The dispatcher could not accept work at all.</summary>
        internal static ConvaiActionDropReport DispatcherUnavailable(string detail) =>
            new(ConvaiActionDropReason.DispatcherUnavailable,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Dropped an incoming action batch: {detail} The commands arrived and were discarded " +
                "without running.");

        // ── Shared description helpers ───────────────────────────────────────────────────

        /// <summary>Names what the character can currently be asked to act on, bounded for one line.</summary>
        private static string DescribeOfferedTargets(ConvaiActionConfig actionConfig)
        {
            if (actionConfig == null)
                return "nothing (this character has no action config)";

            var builder = new StringBuilder();
            int written = 0;
            int total = 0;

            AppendObjectNames(builder, actionConfig.Objects, ref written, ref total);
            AppendCharacterNames(builder, actionConfig.Characters, ref written, ref total);

            if (total == 0)
                return "nothing";

            if (total > written)
                builder.Append(" (+").Append(total - written).Append(" more)");

            return builder.ToString();
        }

        /// <summary>Names the actions that could actually run, bounded for one line.</summary>
        private static string DescribeExecutableActions(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (definitions == null || definitions.Count == 0)
                return "nothing (this character has no actions)";

            var builder = new StringBuilder();
            int written = 0;
            int total = 0;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                if (!ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                    continue;

                AppendName(builder, definition.ActionName, ref written, ref total);
            }

            if (total == 0)
                return "nothing (no action on this character has a behavior bound to it)";

            if (total > written)
                builder.Append(" (+").Append(total - written).Append(" more)");

            return builder.ToString();
        }

        private static void AppendObjectNames(
            StringBuilder builder,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            ref int written,
            ref int total)
        {
            if (objects == null) return;
            for (int i = 0; i < objects.Count; i++)
                AppendName(builder, objects[i]?.Name, ref written, ref total);
        }

        private static void AppendCharacterNames(
            StringBuilder builder,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            ref int written,
            ref int total)
        {
            if (characters == null) return;
            for (int i = 0; i < characters.Count; i++)
                AppendName(builder, characters[i]?.Name, ref written, ref total);
        }

        /// <summary>
        ///     Appends one name, counting every candidate but writing only the first
        ///     <see cref="OfferedNameLimit" />, so a scene with two hundred targets still produces a
        ///     line somebody can read.
        /// </summary>
        private static void AppendName(StringBuilder builder, string rawName, ref int written, ref int total)
        {
            string name = ConvaiActionText.Normalize(rawName);
            if (name.Length == 0) return;

            total++;
            if (written >= OfferedNameLimit) return;

            if (written > 0) builder.Append(", ");
            builder.Append(name);
            written++;
        }

        private static string ResolveActionName(ConvaiActionCommand command, ConvaiActionDefinition definition)
        {
            string name = ConvaiActionText.Normalize(definition?.ActionName);
            return name.Length > 0 ? name : ConvaiActionText.Normalize(command?.Name);
        }

        /// <summary>
        ///     What the command asked to act on: its own target field, or the implicit key the name
        ///     lands under when the backend sends it inside the action text.
        /// </summary>
        private static string ResolveRequestedTarget(ConvaiActionCommand command)
        {
            string requested = ConvaiActionText.Normalize(command?.Target);
            if (requested.Length > 0 || command?.Parameters == null)
                return requested;

            return command.Parameters.TryGetValue(
                ConvaiActionCommand.TargetParameterKey, out ConvaiActionParameterValue implicitTarget)
                ? ConvaiActionText.Normalize(implicitTarget?.StringValue)
                : string.Empty;
        }
    }
}
