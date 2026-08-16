using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The one editor-side seam for injecting local action commands into a
    ///     <see cref="ConvaiActionDispatcher" />, so the Actions Editor window's Test
    ///     Run and the Live mode's Advanced &gt; Send a Raw Command card share exactly one code path
    ///     into <see cref="ConvaiActionDispatcher.EnqueueActions" /> — the same entry the backend's
    ///     received batches flow through (same policies, cloning, enrichment, and events). No
    ///     parallel dispatch path exists in editor tooling.
    /// </summary>
    internal static class ConvaiActionTestRunService
    {
        /// <summary>
        ///     Resolves the enrichment context for a locally built command, preferring the live
        ///     character's session config/definitions and falling back to the authored
        ///     <see cref="ConvaiActionConfigSource" />.
        /// </summary>
        internal static void ResolveInjectionContext(
            ConvaiCharacter character,
            ConvaiActionConfigSource source,
            out ConvaiActionConfig actionConfig,
            out IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            actionConfig = null;
            definitions = null;

            if (character != null)
            {
                actionConfig = character.GetRuntimeActionConfig()?.Clone() ?? character.ActionConfig;
                definitions = character.ActionDefinitions;
                if (actionConfig != null && definitions != null && definitions.Count > 0)
                    return;
            }

            if (source == null)
                return;

            actionConfig = source.BuildActionConfig();
            definitions = source.Definitions;
        }

        /// <summary>
        ///     Builds a wire-equivalent command for <paramref name="actionName" /> /
        ///     <paramref name="targetText" /> and enriches it through the real parser
        ///     (<see cref="ConvaiActionResponseParser.Enrich" />) when definitions are available —
        ///     exactly what happens to a command received from the backend.
        /// </summary>
        internal static ConvaiActionCommand BuildEnrichedCommand(
            ConvaiCharacter character,
            ConvaiActionConfigSource source,
            string actionName,
            string targetText)
        {
            var command = new ConvaiActionCommand(actionName, targetText);
            ResolveInjectionContext(character, source,
                out ConvaiActionConfig actionConfig,
                out IReadOnlyList<ConvaiActionDefinition> definitions);
            if (definitions != null && definitions.Count > 0)
                command = ConvaiActionResponseParser.Enrich(command, actionConfig, definitions);

            // Deliberately does NOT set ConvaiActionCommand.BypassSpeechGate: the Debug Window
            // injects through here and its historical behavior (speech gate waits up to its
            // timeout) must stay identical. The Actions Editor's Test Run builds its commands in
            // ConvaiActionTestRunModel.BuildCommand, which does set the bypass.
            return command;
        }

        /// <summary>
        ///     Builds, enriches, and enqueues one command through the dispatcher's real ingest path.
        ///     Returns the enqueued command (for logging/inspection), or null when no dispatcher was
        ///     supplied.
        /// </summary>
        internal static ConvaiActionCommand Inject(
            ConvaiActionDispatcher dispatcher,
            ConvaiCharacter character,
            ConvaiActionConfigSource source,
            string actionName,
            string targetText)
        {
            if (dispatcher == null)
                return null;

            ConvaiActionCommand command = BuildEnrichedCommand(character, source, actionName, targetText);
            dispatcher.EnqueueActions(new[] { command });
            return command;
        }

        /// <summary>
        ///     Enqueues several already-built commands as one ordered batch — the dispatcher runs
        ///     them sequentially with the same policies as a real multi-step backend command.
        /// </summary>
        internal static void EnqueueBatch(
            ConvaiActionDispatcher dispatcher,
            IReadOnlyList<ConvaiActionCommand> commands)
        {
            if (dispatcher == null || commands == null || commands.Count == 0)
                return;

            dispatcher.EnqueueActions(commands);
        }
    }
}
