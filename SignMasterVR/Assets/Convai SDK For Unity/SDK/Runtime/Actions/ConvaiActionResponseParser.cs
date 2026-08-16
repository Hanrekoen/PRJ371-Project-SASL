using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Enriches raw backend action commands into typed parameter sets using the
    ///     character's authored action templates.
    /// </summary>
    internal static class ConvaiActionResponseParser
    {
        // Rejection keys read from the one place that spells them, so the summary line, the
        // diagnostic event and these constants can never drift apart. See ConvaiActionDropReport.
        internal static readonly string RejectionMalformedEntry =
            ConvaiActionDropReport.ReasonKey(ConvaiActionDropReason.MalformedEntry);

        internal static readonly string RejectionRuntimeSourceUnavailable =
            ConvaiActionDropReport.ReasonKey(ConvaiActionDropReason.RuntimeSourceUnavailable);

        internal static readonly string RejectionUnknownOrUnexecutableAction =
            ConvaiActionDropReport.ReasonKey(ConvaiActionDropReason.UnknownOrUnexecutableAction);

        internal static readonly string RejectionRequiredTargetUnresolved =
            ConvaiActionDropReport.ReasonKey(ConvaiActionDropReason.RequiredTargetUnresolved);

        internal static readonly string RejectionReferenceParameterUnresolved =
            ConvaiActionDropReport.ReasonKey(ConvaiActionDropReason.ReferenceParameterUnresolved);

        private static readonly Regex BraceWrappedRegex =
            new("\\{([^}]*)\\}", RegexOptions.Compiled);

        private static readonly Regex QuotedRegex =
            new("\"([^\"]*)\"|'([^']*)'", RegexOptions.Compiled);

        private static readonly Regex BracketedNoiseRegex =
            new("\\[[^\\]]*\\]", RegexOptions.Compiled);

        /// <summary>
        ///     Returns an enriched copy of <paramref name="command" /> with typed parameters
        ///     split and coerced against the matching action definition template.
        /// </summary>
        public static ConvaiActionCommand Enrich(
            ConvaiActionCommand command,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions) =>
            Enrich(command, actionConfig, definitions, ConvaiActionReadSource.Wire);

        /// <summary>
        ///     Returns an enriched copy of <paramref name="command" />, taking the action's speech
        ///     gate from its definition only when the command came off the wire.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why the source matters, and it only matters for two fields.</b> The backend
        ///         sends a name and a target and nothing else (measured), so a command off the wire
        ///         carries no opinion about the speech gate and the definition's is the only one
        ///         there is. A command a caller built in their own code may carry a deliberate one —
        ///         <c>WaitForBotSpeech</c> and <c>DelayAfterBotSpeechSeconds</c> are public settable
        ///         properties precisely so it can.
        ///     </para>
        ///     <para>
        ///         Before enrichment became unconditional, a locally injected command whose name
        ///         matched a definition was never enriched at all, so it kept whatever the caller
        ///         set. Applying the definition's gate to it now would silently discard that — a
        ///         command asked to wait for the character to finish speaking would start
        ///         immediately. Same reasoning as the parameter dictionary below, which was given
        ///         fill-if-absent for exactly this: derived values fill what the caller left alone,
        ///         and never overwrite what they chose.
        ///     </para>
        /// </remarks>
        internal static ConvaiActionCommand Enrich(
            ConvaiActionCommand command,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            ConvaiActionReadSource source)
        {
            if (command == null)
                return new ConvaiActionCommand { Enriched = true };

            ConvaiActionCommand enriched = command.Clone();
            enriched.Enriched = true;

            // The one place the wire is read, and therefore the only place a model's own decoration
            // is stripped: the quotes it wraps values in and the separator it echoes back off the
            // template it was shown. Written back onto the enriched copy so everything downstream —
            // resolution, the executor, the drop report — sees the cleaned name rather than
            // re-deriving it.
            // The config travels into the cleaner so a repair can never take a name away from
            // something this character actually has — see ConvaiActionWireText.Clean.
            string rawName = ConvaiActionWireText.Clean(command.Name, actionConfig);
            string target = ConvaiActionWireText.Clean(command.Target, actionConfig);
            enriched.Name = rawName;
            enriched.Target = target;
            enriched.ActionString = string.IsNullOrEmpty(target) ? rawName : $"{rawName} {target}".Trim();
            // Deliberately not cleared. On the wire path the dictionary is always empty — the
            // backend sends only a name and a target — so clearing it never did anything there. It
            // would matter the moment a caller builds a command by hand and hands it to
            // ConvaiActionDispatcher.EnqueueActions: enrichment would wipe the parameters they set.
            // Derived values fill the slots nobody claimed; a caller's own values win.
            enriched.Parameters ??= new Dictionary<string, ConvaiActionParameterValue>(StringComparer.OrdinalIgnoreCase);

            ConvaiActionDefinition definition = FindTemplate(rawName, definitions);
            if (definition == null)
            {
                if (!string.IsNullOrEmpty(target))
                    FillIfAbsent(enriched, ConvaiActionCommand.TargetParameterKey,
                        Coerce(target, ConvaiActionParameterType.Auto, actionConfig, null));

                return enriched;
            }

            enriched.Name = ConvaiActionDefinition.NormalizeActionName(definition.ActionName);

            // Only for a command off the wire — see the remarks on this overload. A caller's own
            // speech-gate choice is theirs to keep.
            if (source == ConvaiActionReadSource.Wire)
            {
                enriched.WaitForBotSpeech = definition.WaitForBotSpeech;
                enriched.DelayAfterBotSpeechSeconds = definition.WaitForBotSpeech
                    ? Math.Max(0f, definition.DelayAfterBotSpeechSeconds)
                    : 0f;
            }

            string nameLeftover = StripTemplatePrefix(rawName, definition);
            string blob = Combine(nameLeftover, target);

            // The slots the template actually presented, not just the ones the author declared. An
            // action that needs a target but declares no parameter to carry it is rendered with an
            // extra {target: reference} slot; sizing the split by the declared parameters alone read
            // a different template from the one that was sent, and the Convai Character's answer for
            // that slot was truncated away — after which the action was dropped for having no
            // target. See ConvaiActionWireGrammar.SlotsOf.
            IReadOnlyList<ConvaiActionParameterDefinition> parameters = ConvaiActionWireGrammar.SlotsOf(definition);
            int declaredCount = definition.Parameters?.Count ?? 0;
            if (parameters.Count == 0)
            {
                if (!string.IsNullOrEmpty(blob))
                    FillIfAbsent(enriched, ConvaiActionCommand.TargetParameterKey,
                        Coerce(blob, ConvaiActionParameterType.Auto, actionConfig, null));

                return enriched;
            }

            List<string> values = SplitParameterValues(blob, parameters, actionConfig);
            for (int i = 0; i < parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = parameters[i];
                string name = ConvaiActionParameterDefinition.Normalize(parameter?.Name);
                if (string.IsNullOrEmpty(name))
                    continue;

                // A null here is a slot nothing reached, not a slot filled in as empty — see Pad.
                string suppliedValue = i < values.Count ? values[i] : null;
                bool provided = suppliedValue != null;

                string rawValue = provided
                    ? StripParamNameMimicry(suppliedValue, name, actionConfig)
                    : string.Empty;
                ConvaiActionParameterValue value = Coerce(rawValue, parameter.Type, actionConfig, parameter);
                value.Presence = provided
                    ? ConvaiActionParameterPresence.Provided
                    : ConvaiActionParameterPresence.Missing;
                bool wrote = FillIfAbsent(enriched, name, value);

                // Only the author's own parameters are worth a word here. The implicit target slot is
                // the SDK's doing, and an action that simply was not given a target is already
                // reported — far better — by the admission stage, which can name what was offered.
                if (wrote && !provided && i < declaredCount &&
                    LoggingConfig.IsWarningEnabled(LogCategory.Actions))
                {
                    // Said out loud because the alternative is what this used to be: the slot was
                    // padded with an empty string, the Action Behavior read it as an instruction,
                    // and the character did something slightly wrong for a reason nothing recorded.
                    ConvaiLogger.Warning(
                        $"Action '{enriched.Name}' declares the parameter '{name}', and the Convai " +
                        "Character sent no value for it. The parameter is marked Missing rather than " +
                        "guessed at — an Action Behavior reading it should decide for itself. If this " +
                        "keeps happening, the action's wording may not make clear that the parameter " +
                        "is needed.",
                        LogCategory.Actions);
                }

                if (parameter.Type == ConvaiActionParameterType.Choice &&
                    provided &&
                    !value.IsConstraintMatch &&
                    LoggingConfig.IsWarningEnabled(LogCategory.Actions))
                {
                    string nearest = NearestChoice(value.StringValue, parameter.Choices);
                    ConvaiLogger.Warning(
                        $"Action '{enriched.Name}' parameter '{name}' value '{rawValue}' is not one of its " +
                        "authored choices, so the parameter fell back to its default." +
                        (nearest == null ? string.Empty : $" Did you mean '{nearest}'?") +
                        " Authored choices: " +
                        $"{(parameter.Choices == null || parameter.Choices.Count == 0 ? "none" : string.Join(", ", parameter.Choices))}.",
                        LogCategory.Actions);
                }
            }

            return enriched;
        }

        /// <summary>
        ///     Writes a derived parameter, unless the caller already supplied one under that name.
        /// </summary>
        /// <returns>Whether the value was written.</returns>
        /// <remarks>
        ///     Enrichment derives values from what the Convai Character said; a caller who built the
        ///     command by hand already knows what it means. On the wire path this never fires,
        ///     because nothing arrives with parameters — it exists so that handing a hand-built
        ///     command to <c>ConvaiActionDispatcher.EnqueueActions</c> does not silently discard it.
        /// </remarks>
        private static bool FillIfAbsent(
            ConvaiActionCommand command, string name, ConvaiActionParameterValue value)
        {
            if (command.Parameters.ContainsKey(name))
                return false;

            command.Parameters[name] = value;
            return true;
        }

        /// <summary>Enriches every command in a batch; see <see cref="Enrich" />.</summary>
        public static IReadOnlyList<ConvaiActionCommand> EnrichBatch(
            IReadOnlyList<ConvaiActionCommand> commands,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (commands == null || commands.Count == 0)
                return Array.Empty<ConvaiActionCommand>();

            var enriched = new ConvaiActionCommand[commands.Count];
            for (int i = 0; i < commands.Count; i++)
                enriched[i] = Enrich(commands[i], actionConfig, definitions);

            return enriched;
        }

        /// <summary>
        ///     Parses valid commands from an action-response payload while counting malformed entries.
        /// </summary>
        public static bool TryParseBatch(
            JObject payload,
            out IReadOnlyList<ConvaiActionCommand> actions,
            out int skippedEntries)
        {
            actions = Array.Empty<ConvaiActionCommand>();
            skippedEntries = 0;

            if (payload?["actions"] is not JArray actionArray)
                return false;

            var parsed = new List<ConvaiActionCommand>(actionArray.Count);
            for (int i = 0; i < actionArray.Count; i++)
            {
                JToken token = actionArray[i];
                if (token == null || token.Type != JTokenType.Object)
                {
                    skippedEntries++;
                    continue;
                }

                ConvaiInboundActionCommand inbound;
                try
                {
                    // Read through the wire's own two-field type, so a payload cannot reach in and
                    // set pipeline state — see ConvaiInboundActionCommand.
                    inbound = token.ToObject<ConvaiInboundActionCommand>();
                }
                catch (JsonException)
                {
                    skippedEntries++;
                    continue;
                }

                if (inbound == null || string.IsNullOrWhiteSpace(inbound.Name))
                {
                    skippedEntries++;
                    continue;
                }

                LogReceived(inbound);
                parsed.Add(new ConvaiActionCommand(inbound.Name, inbound.Target));
            }

            actions = parsed;
            return true;
        }

        /// <summary>
        ///     Enriches and filters one backend batch against the Unity-executable catalog and the
        ///     latest backend-confirmed action config. Rejected commands never reach public events.
        /// </summary>
        internal static IReadOnlyList<ConvaiActionCommand> FilterExecutableBatch(
            IReadOnlyList<ConvaiActionCommand> commands,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            ConvaiActionDropCollector drops,
            Vector3? origin = null) =>
            ReadBatch(commands, actionConfig, definitions, drops, origin, ConvaiActionReadSource.Wire);

        /// <summary>
        ///     Reads a batch the same way the response filter does, and explains anything that will
        ///     not work — but returns every command regardless.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         For commands a caller injects rather than the backend sending. They deserve the
        ///         same reading — the same cleaning, the same parameter parsing, the same
        ///         explanations — but not the same refusal: the filter turns commands away because a
        ///         stale or hallucinated one should never reach an Action Behavior, and a command
        ///         written by hand in your own code is neither.
        ///     </para>
        ///     <para>
        ///         Sharing the body with <see cref="FilterExecutableBatch" /> is the point. Two
        ///         copies of "what is wrong with this command" would answer differently within a
        ///         release, and then the explanation a developer reads while testing would stop
        ///         matching what happens in a conversation.
        ///     </para>
        /// </remarks>
        internal static IReadOnlyList<ConvaiActionCommand> ReadWithoutRefusing(
            IReadOnlyList<ConvaiActionCommand> commands,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            ConvaiActionDropCollector drops,
            Vector3? origin = null) =>
            ReadBatch(commands, actionConfig, definitions, drops, origin, ConvaiActionReadSource.LocalCaller);

        private static IReadOnlyList<ConvaiActionCommand> ReadBatch(
            IReadOnlyList<ConvaiActionCommand> commands,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            ConvaiActionDropCollector drops,
            Vector3? origin,
            ConvaiActionReadSource source)
        {
            // Where a command came from decides two things and nothing else: whether it may be
            // refused, and whether the definition's speech gate overrides one the caller set.
            bool refuse = source == ConvaiActionReadSource.Wire;
            if (commands == null || commands.Count == 0)
                return Array.Empty<ConvaiActionCommand>();

            // Before anything is judged: a command the Convai Character wrote as several entries is
            // one command. Judging the pieces separately condemns all of them — the action for
            // naming nothing to act on, and the pieces for not being actions.
            commands = ConvaiActionSplitCommandRejoin.Apply(
                commands, actionConfig, definitions, out int rejoinedEntries);
            if (rejoinedEntries > 0 && LoggingConfig.IsInfoEnabled(LogCategory.Actions))
            {
                ConvaiLogger.Info(
                    $"[ConvaiActionResponseParser] Put {rejoinedEntries} stray action-list " +
                    "entr" + (rejoinedEntries == 1 ? "y" : "ies") + " back onto the command " +
                    "before it. The Convai Character wrote one request as several entries — each " +
                    "piece names a slot the action left empty, so they belong together. Said out " +
                    "loud because a command that was repaired is not the command that was sent.",
                    LogCategory.Actions);
            }

            var accepted = new List<ConvaiActionCommand>(commands.Count);
            for (int i = 0; i < commands.Count; i++)
            {
                ConvaiActionCommand command = commands[i];
                ConvaiActionDefinition definition = FindTemplate(
                    ConvaiActionText.Normalize(command?.Name),
                    definitions);
                if (!ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                {
                    Record(
                        drops,
                        ConvaiActionDropReason.UnknownOrUnexecutableAction,
                        drops.WantsDetail
                            ? ConvaiActionDropReportFactory.UnknownOrUnexecutableAction(
                                command, definition, definitions)
                            : default);

                    // Nothing below can run without a definition — the reference-parameter check
                    // reads its parameter list — so this exit is taken whether or not we refuse.
                    // A caller who injected an unknown action still gets it back, read as far as it
                    // can be, along with the explanation of why nothing will run it.
                    if (!refuse)
                        accepted.Add(Enrich(command, actionConfig, definitions, source));
                    continue;
                }

                ConvaiActionCommand enriched = Enrich(command, actionConfig, definitions, source);
                // The same ladder the dispatcher climbs, with the same origin, so admitting a
                // command and acting on it cannot reach different conclusions.
                ConvaiActionTargetOutcome outcome = ConvaiActionTargetResolution.ResolveForDispatch(
                    enriched, definition, actionConfig, origin, out ConvaiResolvedActionTarget resolved);
                if (outcome != ConvaiActionTargetOutcome.Resolved)
                {
                    Record(
                        drops,
                        ConvaiActionDropReason.RequiredTargetUnresolved,
                        drops.WantsDetail
                            ? ConvaiActionDropReportFactory.RequiredTargetUnresolved(
                                enriched, definition, actionConfig, outcome, resolved)
                            : default);
                    if (refuse) continue;
                }

                if (!TryFindUnresolvedReferenceParameter(
                        enriched, definition, actionConfig, out string parameterName, out string requestedValue))
                {
                    // The expectation the dispatcher will check its own resolution against — see
                    // ConvaiActionCommand.AdmittedTargetName.
                    enriched.AdmittedTargetName = resolved?.Name;
                    accepted.Add(enriched);
                    continue;
                }

                Record(
                    drops,
                    ConvaiActionDropReason.ReferenceParameterUnresolved,
                    drops.WantsDetail
                        ? ConvaiActionDropReportFactory.ReferenceParameterUnresolved(
                            enriched, definition, actionConfig, parameterName, requestedValue)
                        : default);
                if (!refuse) accepted.Add(enriched);
            }

            return accepted;
        }

        /// <summary>
        ///     Records one dropped command, with its explanation when one was built.
        /// </summary>
        /// <remarks>
        ///     The explanation is passed already built rather than as a callback: a callback here
        ///     would allocate a closure on every drop, including the drops nobody is listening to,
        ///     which is the cost this whole path is written to avoid.
        /// </remarks>
        private static void Record(
            ConvaiActionDropCollector drops,
            ConvaiActionDropReason reason,
            in ConvaiActionDropReport report)
        {
            if (drops == null)
                return;

            if (drops.WantsDetail)
                drops.Add(report);
            else
                drops.Count(reason);
        }

        /// <summary>
        ///     Records a command exactly as it arrived, before anything has been read from it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The first question anyone asks about a command that did not run is "what actually
        ///         came?", and until now nothing anywhere could answer it. Every later message
        ///         describes the command <em>after</em> the SDK has cleaned it, split it and mapped it
        ///         to a definition — so when one of those steps is what went wrong, every account of
        ///         the failure is written in terms of the mistake.
        ///     </para>
        ///     <para>
        ///         That gap matters more here than it would elsewhere, because the backend's own
        ///         splitter does not fire for the templates this SDK sends: an action's target
        ///         normally arrives glued inside the name rather than in the target field, and the
        ///         exact shape of that gluing is the thing a diagnosis turns on.
        ///     </para>
        ///     <para>
        ///         Written at <c>Debug</c>, and the string is not composed unless somebody has turned
        ///         that on — a shipped build pays nothing.
        ///     </para>
        /// </remarks>
        private static void LogReceived(ConvaiInboundActionCommand inbound)
        {
            if (!LoggingConfig.IsDebugEnabled(LogCategory.Actions))
                return;

            ConvaiLogger.Debug(
                $"[ConvaiActionResponseParser] Received from Convai, before anything was read: " +
                $"name='{inbound.Name}' target='{inbound.Target ?? "<none>"}'",
                LogCategory.Actions);
        }

        internal static ConvaiActionDefinition FindTemplate(
            string rawName,
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (definitions == null || string.IsNullOrEmpty(rawName))
                return null;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                if (string.Equals(rawName, definition?.ActionName, StringComparison.OrdinalIgnoreCase))
                    return definition;
            }

            ConvaiActionDefinition best = null;
            int bestLength = -1;
            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string name = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                if (name.Length <= bestLength || !StartsWithActionName(rawName, name))
                    continue;

                best = definition;
                bestLength = name.Length;
            }

            return best ?? FindTemplateByContentWords(rawName, definitions);
        }

        /// <summary>
        ///     Last resort: matches on the words that carry the meaning, ignoring the articles and
        ///     punctuation between them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why an action can be dropped for a word nobody meant.</b> A Convai Character is
        ///         shown an action's name and asked to write it back. Given <c>Light The Room</c> it
        ///         will sometimes answer <c>Light Room</c> — the same action, in the English a person
        ///         would actually speak. Neither the exact nor the prefix pass accepts that, so the
        ///         command was dropped and the room stayed dark, over a dropped <c>The</c>.
        ///     </para>
        ///     <para>
        ///         <b>It stays strict about everything except the articles.</b> The words must still
        ///         match in order, and the action's words must still be a <em>prefix</em> of what
        ///         arrived — so <c>Walk Toward The Bench</c> is still not the action <c>Walk To</c>,
        ///         because <c>toward</c> is not <c>to</c>. That is the whole licence taken here: a
        ///         missing or added <c>a</c>, <c>an</c> or <c>the</c>, and whatever punctuation sits
        ///         between words.
        ///     </para>
        ///     <para>
        ///         <b>A tie is refused, not guessed.</b> If two actions reduce to the same words — a
        ///         character authored with both <c>Light Room</c> and <c>Light The Room</c> — running
        ///         either one is a coin toss, and a coin toss that moves a character is worse than a
        ///         drop the developer can read in the console. Ties return nothing and the command is
        ///         reported as unknown, which is the message that leads to the real problem.
        ///     </para>
        /// </remarks>
        private static ConvaiActionDefinition FindTemplateByContentWords(
            string rawName,
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            ConvaiActionDefinition best = null;
            var bestWords = 0;
            var tied = false;

            for (var i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                int words = ContentWordPrefixLength(rawName, definition?.ActionName, out _);

                if (words <= 0 || words < bestWords)
                    continue;

                if (words == bestWords)
                {
                    tied = true;
                    continue;
                }

                best = definition;
                bestWords = words;
                tied = false;
            }

            return tied ? null : best;
        }

        /// <summary>
        ///     How many of <paramref name="actionName" />'s content words <paramref name="rawName" />
        ///     opens with, or <c>-1</c> when it does not open with all of them.
        /// </summary>
        /// <param name="rawEnd">
        ///     Where the matched words end in <paramref name="rawName" />, so the caller can take the
        ///     remainder as the text the model wrote <em>after</em> the name. Without this the name
        ///     itself would be carved up as parameter values.
        /// </param>
        private static int ContentWordPrefixLength(string rawName, string actionName, out int rawEnd)
        {
            rawEnd = 0;

            if (string.IsNullOrEmpty(actionName))
                return -1;

            var rawIndex = 0;
            var nameIndex = 0;
            var matched = 0;

            while (TryReadContentWord(actionName, ref nameIndex, out int nameStart, out int nameLength))
            {
                // The raw side stops where the name stops. Without this the scan reads on into what
                // the character wrote for the parameters, and since the longest match wins, an action
                // called "Light The Room Mode" beats the "Light The Room" that was actually named by
                // swallowing the slot label out of "Light Room {mode: on}". The tie rule cannot save
                // that — the two scores genuinely differ.
                if (!TryReadContentWord(rawName, ref rawIndex, out int rawStart, out int rawLength,
                        stopAtParameterText: true))
                {
                    return -1;
                }

                if (rawLength != nameLength ||
                    string.Compare(rawName, rawStart, actionName, nameStart, nameLength,
                        StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return -1;
                }

                matched++;
                rawEnd = rawIndex;
            }

            // Punctuation the match stepped over belongs to the name, not to the first parameter.
            // "Ring Bell? loud" against "Ring The Bell!" matched both words and then handed on
            // "? loud", and the question mark arrived as part of somebody's answer. Only sentence
            // punctuation is taken: a brace or a label separator is where the parameters start, and
            // eating one of those would break the very carving this is protecting.
            while (rawEnd < rawName.Length && IsNameTrailingPunctuation(rawName[rawEnd]))
                rawEnd++;

            return matched;
        }

        /// <summary>Whether this character can only be the tail of a name, never the start of a value.</summary>
        private static bool IsNameTrailingPunctuation(char value) =>
            value is '.' or '!' or '?' or ';' or ' ' or '\t';

        /// <summary>
        ///     Reads the next letters-and-digits word from <paramref name="text" />, skipping
        ///     punctuation and the articles that carry no meaning in an action's name.
        /// </summary>
        /// <param name="stopAtParameterText">
        ///     Set when reading what the character wrote, where a slot brace or a <c>label:</c> marks
        ///     the end of the name and the start of its parameters. The authored name is read without
        ///     it, because an action's name is the whole of its own text.
        /// </param>
        private static bool TryReadContentWord(
            string text,
            ref int index,
            out int start,
            out int length,
            bool stopAtParameterText = false)
        {
            start = 0;
            length = 0;

            if (string.IsNullOrEmpty(text))
                return false;

            while (index < text.Length)
            {
                while (index < text.Length && !char.IsLetterOrDigit(text[index]))
                {
                    if (stopAtParameterText && text[index] == '{')
                        return false;

                    index++;
                }

                if (index >= text.Length)
                    return false;

                int wordStart = index;
                while (index < text.Length && char.IsLetterOrDigit(text[index]))
                    index++;

                // 'mode' in "mode: on" names a slot, not the action. Only when it is written against
                // the separator: a colon with a space before it is punctuation in a sentence.
                if (stopAtParameterText && index < text.Length && (text[index] == ':' || text[index] == '='))
                    return false;

                int wordLength = index - wordStart;
                if (IsArticle(text, wordStart, wordLength))
                    continue;

                start = wordStart;
                length = wordLength;
                return true;
            }

            return false;
        }

        /// <summary>Whether the word at this span is <c>a</c>, <c>an</c> or <c>the</c>.</summary>
        private static bool IsArticle(string text, int start, int length)
        {
            switch (length)
            {
                case 1:
                    return text[start] is 'a' or 'A';
                case 2:
                    return string.Compare(text, start, "an", 0, 2, StringComparison.OrdinalIgnoreCase) == 0;
                case 3:
                    return string.Compare(text, start, "the", 0, 3, StringComparison.OrdinalIgnoreCase) == 0;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Finds the first Reference parameter that named something this character does not have.
        /// </summary>
        /// <remarks>
        ///     Reports which parameter and which value rather than answering yes/no, because that is
        ///     the difference between a message a developer can act on and one that only says a
        ///     reference "did not resolve" — and an action with three Reference parameters gives no
        ///     clue which of the three was the problem.
        /// </remarks>
        private static bool TryFindUnresolvedReferenceParameter(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            out string parameterName,
            out string requestedValue)
        {
            parameterName = string.Empty;
            requestedValue = string.Empty;

            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters == null || parameters.Count == 0)
                return false;

            for (int i = 0; i < parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = parameters[i];
                if (parameter?.Type != ConvaiActionParameterType.Reference)
                    continue;

                string name = ConvaiActionParameterDefinition.Normalize(parameter.Name);
                if (name.Length == 0)
                {
                    parameterName = "(unnamed)";
                    return true;
                }

                ConvaiActionParameterValue value = null;
                bool present = command?.Parameters != null &&
                               command.Parameters.TryGetValue(name, out value);

                if (!present || value?.ResolvedReference == null)
                {
                    parameterName = name;
                    requestedValue = value?.StringValue ?? string.Empty;
                    return true;
                }

                ConvaiResolvedActionTarget resolved = ConvaiActionTargetReferenceResolver.Resolve(
                    value,
                    actionConfig,
                    definition.TargetRequirement);
                if (resolved?.GameObjectReference == null)
                {
                    parameterName = name;
                    requestedValue = value.StringValue ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Whether <paramref name="rawName" /> begins with this action's name, and ends it —
        ///     rather than merely starting with the same letters.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A name ends where a word does.</b> This used to list the boundaries it would
        ///         accept — whitespace, or a slot brace — and a Convai Character that answered
        ///         <c>Follow Me(mode='follow')</c> was told it had named an action this character
        ///         does not have. It had not: it had written its parameters in brackets instead of
        ///         braces, which no list of accepted punctuation was ever going to keep up with.
        ///     </para>
        ///     <para>
        ///         Asking instead that the next character not continue a word covers every
        ///         punctuation a model might reach for, and still refuses the case the list existed
        ///         for: <c>Walk Toward The Bench</c> does not begin with the action <c>Walk To</c>,
        ///         because <c>w</c> carries the word on.
        ///     </para>
        /// </remarks>
        private static bool StartsWithActionName(string rawName, string actionName)
        {
            if (string.IsNullOrEmpty(actionName) || rawName.Length < actionName.Length)
                return false;

            if (!rawName.StartsWith(actionName, StringComparison.OrdinalIgnoreCase))
                return false;

            return rawName.Length == actionName.Length ||
                   !char.IsLetterOrDigit(rawName[actionName.Length]);
        }

        private static string StripTemplatePrefix(string rawName, ConvaiActionDefinition definition)
        {
            rawName = ConvaiActionText.Normalize(rawName);
            if (definition == null)
                return rawName;

            string rendered = ConvaiActionText.Normalize(definition.ToActionConfigString());
            if (!string.IsNullOrEmpty(rendered) &&
                rawName.StartsWith(rendered, StringComparison.OrdinalIgnoreCase))
            {
                return rawName.Length == rendered.Length
                    ? string.Empty
                    : rawName.Substring(rendered.Length).Trim();
            }

            return StripActionPrefix(rawName, definition.ActionName);
        }

        /// <summary>
        ///     Takes the action's name off the front of what arrived, leaving the text the model
        ///     wrote after it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The second half of the article tolerance.</b> Matching <c>Light Room</c> to the
        ///         action <c>Light The Room</c> is only half the job: the name has to come off by the
        ///         length the model actually wrote, not by the length of the authored name. Cutting by
        ///         the authored length would leave a stray fragment, and refusing to cut at all would
        ///         hand the whole line — action name included — to the parameter carver, which then
        ///         reads <c>Light</c> as somebody's answer. Both are worse than the drop this
        ///         tolerance exists to prevent, so the cut is measured in words, the same way the
        ///         match was.
        ///     </para>
        /// </remarks>
        private static string StripActionPrefix(string rawName, string actionName)
        {
            rawName = ConvaiActionText.Normalize(rawName);
            actionName = ConvaiActionDefinition.NormalizeActionName(actionName);
            if (StartsWithActionName(rawName, actionName))
                return rawName.Substring(actionName.Length).Trim();

            return ContentWordPrefixLength(rawName, actionName, out int rawEnd) > 0
                ? rawName.Substring(rawEnd).Trim()
                : rawName;
        }

        private static string Combine(string first, string second)
        {
            first = ConvaiActionText.Normalize(first);
            second = ConvaiActionText.Normalize(second);
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;
            return $"{first} {second}";
        }

        /// <summary>
        ///     Carves the leftover text into one value per slot the template presented.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Ordered by how certain each answer is, not by how clever it is.</b> Carving is
        ///         only ever necessary because two or more values had to share one string. Where that
        ///         is not the case there is nothing to decide, and the stages below are skipped
        ///         entirely rather than run and then hopefully agreed with — every one of them is a
        ///         guess, and a guess made where the answer was already known is pure risk.
        ///     </para>
        ///     <para>
        ///         Then, where carving really is necessary, <b>a value delimited at both ends beats
        ///         one that is only marked at the start</b>. A brace group says where its value ends;
        ///         a <c>name:</c> anchor does not, and has to assume the next anchor begins
        ///         immediately after. Sent <c>{low: 20} {high: 80}</c>, both stages match — and the
        ///         anchors carve <c>low</c> as <c>20} {</c>, because the punctuation between the two
        ///         values belongs to neither of them. Measured: it cost this exact command, which is
        ///         why the order is written down here rather than left to look arbitrary.
        ///     </para>
        ///     <para>
        ///         Every stage is accepted only when it accounts for every slot, and falls through
        ///         otherwise. It used to be accepted on finding anything at all, with the remainder
        ///         padded out — so an action with two parameters and an implicit target slot, sent
        ///         <c>{20} {80} Power Generator</c>, took the two brace groups, padded the target
        ///         away, and was dropped for naming nothing.
        ///     </para>
        ///     <para>
        ///         <b>R-3 is not repeated here.</b> Every value produced goes on through
        ///         <see cref="StripParamNameMimicry" /> and <see cref="ConvaiActionWireText.Clean(string, ConvaiActionConfig)" />,
        ///         both of which consult the vocabulary. The single-slot check that used to sit at
        ///         the top of this method was that same question asked a third time, and it is gone
        ///         because the case it protected no longer reaches a splitter at all.
        ///     </para>
        /// </remarks>
        private static List<string> SplitParameterValues(
            string blob,
            IReadOnlyList<ConvaiActionParameterDefinition> parameters,
            ConvaiActionConfig vocabulary)
        {
            int expected = parameters?.Count ?? 0;
            var values = new List<string>(expected);
            blob = ConvaiActionText.Normalize(blob);
            if (expected == 0)
                return values;

            // One slot, so nothing is sharing the string and there is nothing to carve. Whatever
            // came is the value for it, decoration and all: the cleaner takes decoration off a whole
            // value far more safely than a splitter can, because it can see both ends of it at once.
            // Carving here actively destroyed values — `"first" and "second"` came back as `first`,
            // because the quoted-group stage found two groups where the template had one slot and
            // the extra was padded away in silence.
            if (expected == 1)
            {
                values.Add(Unbracket(blob, vocabulary));
                return values;
            }

            blob = Unbracket(blob, vocabulary);

            // Delimited at both ends: the value's own end is stated rather than inferred.
            values = SplitBraceWrapped(blob, vocabulary);
            if (values.Count >= expected)
                return Pad(values, expected);

            // Marked at the start only: each value runs until the next mark.
            values = SplitNamedAnchors(blob, parameters, vocabulary);
            if (values.Count >= expected)
                return Pad(values, expected);

            values = SplitByConnectors(blob, parameters);
            if (values.Count >= expected)
                return Pad(values, expected);

            values = SplitQuoted(blob, vocabulary);
            if (values.Count >= expected)
                return Pad(values, expected);

            // The last resort, and the one place R-3 cannot reach. Splitting on whitespace has to
            // guess where one value ends, so a multi-word name is unrecoverable here whatever the
            // vocabulary says — there is no candidate substring to offer it. Two or more slots
            // filled with multi-word names are the stated limit of the repair set.
            return Pad(SplitWhitespace(blob, expected), expected);
        }

        /// <summary>
        ///     Removes the brackets a Convai Character wrapped its whole answer in — unless the
        ///     bracketed text is already the name of something this character has.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Three bracket kinds, because models write objects in all three:</b>
        ///         <c>{step: 4, target: "Bench"}</c>, <c>(mode='follow')</c>, <c>[mode=stop]</c>.
        ///         All three were measured on one live run, from one character, inside ten minutes.
        ///         A reader that knows only braces loses the other two whole.
        ///     </para>
        ///     <para>
        ///         <b>R-3 applies</b>, and this is the last place it can be applied: a bay really
        ///         called <c>{Annex}</c> is not an object literal, and once the brackets are off
        ///         nothing downstream can tell the difference. So the vocabulary is asked before
        ///         anything is removed, exactly as in
        ///         <see cref="ConvaiActionWireText.Clean(string, ConvaiActionConfig)" />.
        ///     </para>
        /// </remarks>
        private static string Unbracket(string blob, ConvaiActionConfig vocabulary) =>
            ConvaiActionWireText.NamesSomething(vocabulary, blob)
                ? blob
                : UnwrapEnclosingBrackets(blob);

        /// <summary>
        ///     Removes one pair of brackets wrapping the entire text, when nothing of the same kind
        ///     is nested inside it.
        /// </summary>
        /// <remarks>
        ///     The nesting guard is what separates an object from a run of filled slots:
        ///     <c>{20} {80}</c> also begins and ends with a brace, and stripping that pair would
        ///     splice the first value onto the last. Same rule, and the same reason, as
        ///     <c>ConvaiActionWireText</c>'s slot unwrap.
        /// </remarks>
        private static string UnwrapEnclosingBrackets(string blob)
        {
            while (blob.Length >= 2)
            {
                char open = blob[0];
                char close = Closer(open);
                if (close == '\0' || blob[blob.Length - 1] != close)
                    return blob;

                for (int i = 1; i < blob.Length - 1; i++)
                {
                    if (blob[i] == open || blob[i] == close)
                        return blob;
                }

                blob = blob.Substring(1, blob.Length - 2).Trim();
            }

            return blob;
        }

        /// <summary>The closing half of a bracket pair, or NUL when this is not an opening one.</summary>
        private static char Closer(char open) =>
            open switch
            {
                ConvaiActionWireGrammar.SlotOpen => ConvaiActionWireGrammar.SlotClose,
                '(' => ')',
                '[' => ']',
                _ => '\0'
            };

        /// <summary>
        ///     Splits a blob that arrived as a run of <c>{…}</c> groups, one per slot.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>R-3 applies here too, per group.</b> Unwrapping braces is a repair, and the
        ///         whole-blob check above only fires for a single-slot action — with two or more
        ///         slots, <c>{Annex} {ok}</c> is not any one target's name, so that check cannot help
        ///         and an object genuinely called <c>{Annex}</c> lost its braces and stopped
        ///         resolving. The question is asked at the only place it can be: of each group, in
        ///         the braced form it arrived in.
        ///     </para>
        /// </remarks>
        private static List<string> SplitBraceWrapped(string blob, ConvaiActionConfig vocabulary)
        {
            var values = new List<string>();
            foreach (Match match in BraceWrappedRegex.Matches(blob))
            {
                string braced = match.Value.Trim();
                values.Add(ConvaiActionWireText.NamesSomething(vocabulary, braced)
                    ? braced
                    : match.Groups[1].Value.Trim());
            }

            return values;
        }

        /// <summary>
        ///     Splits a blob on the action's own parameter names used as <c>name:</c> anchors.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>This is a repair, whatever its name says</b>, and R-3 applies to it. Treating
        ///         <c>name:</c> as an anchor <em>removes</em> that text from the value, which is
        ///         exactly what a stripper does. The rule was missed here once already, and the fix
        ///         made at the time only covered a single-slot action — for two or more slots this
        ///         stage still dropped a leading <c>word:</c> without asking whether the whole blob
        ///         was already a name this character answers to.
        ///     </para>
        ///     <para>
        ///         So the vocabulary is checked first, at the only place it can be checked: on the
        ///         blob, before any anchor is believed. A value that names something real is not a
        ///         set of anchored fields.
        ///     </para>
        ///     <para>
        ///         <b>Not every slot needs a label</b> — see
        ///         <see cref="AnchorsAccountForTheWholeBlob" /> for the two conditions that make a
        ///         partial set of labels safe to believe, and why refusing them outright was worse
        ///         than believing them.
        ///     </para>
        /// </remarks>
        private static List<string> SplitNamedAnchors(
            string blob,
            IReadOnlyList<ConvaiActionParameterDefinition> parameters,
            ConvaiActionConfig vocabulary)
        {
            if (ConvaiActionWireText.NamesSomething(vocabulary, blob))
                return new List<string>();

            var anchors = new List<(int ParameterIndex, int StartIndex, int ValueIndex)>();
            for (int i = 0; i < parameters.Count; i++)
            {
                string name = ConvaiActionParameterDefinition.Normalize(parameters[i]?.Name);
                if (string.IsNullOrEmpty(name))
                    continue;

                if (TryFindAnchor(blob, name, out int startIndex, out int valueIndex))
                    anchors.Add((i, startIndex, valueIndex));
            }

            if (anchors.Count == 0 || !AnchorsAccountForTheWholeBlob(blob, anchors, parameters.Count))
                return new List<string>();

            anchors.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
            var values = new string[parameters.Count];
            for (int i = 0; i < anchors.Count; i++)
            {
                int start = anchors[i].ValueIndex;
                int end = i + 1 < anchors.Count ? anchors[i + 1].StartIndex : blob.Length;
                values[anchors[i].ParameterIndex] =
                    TrimFieldSeparators(blob.Substring(start, Math.Max(0, end - start)));
            }

            return new List<string>(values);
        }

        /// <summary>
        ///     Finds where a slot's label sits in the blob, however the model punctuated it.
        /// </summary>
        /// <remarks>
        ///     A colon is what the template shows and a colon is what usually comes back, but
        ///     <c>mode='follow'</c> was measured live from the same character on the same run. Both
        ///     say the same thing — this text names that slot — so both are read.
        /// </remarks>
        private static bool TryFindAnchor(string blob, string name, out int startIndex, out int valueIndex)
        {
            CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
            int from = 0;
            while (from <= blob.Length - name.Length)
            {
                int index = compare.IndexOf(
                    blob, name, from, blob.Length - from, CompareOptions.IgnoreCase);
                if (index < 0)
                    break;

                // Past the quote a model may have closed the key with, and any space before the
                // separator: `"low": 20` and `low = 20` name the same slot as `low:` does.
                int after = index + name.Length;
                while (after < blob.Length &&
                       (ConvaiActionWireText.IsQuote(blob[after]) || char.IsWhiteSpace(blob[after])))
                    after++;

                if (after < blob.Length && IsLabelSeparator(blob[after]))
                {
                    // The label begins at the quote that opened it, when there was one — otherwise
                    // that quote is left behind as the previous value's last character.
                    startIndex = index > 0 && ConvaiActionWireText.IsQuote(blob[index - 1])
                        ? index - 1
                        : index;
                    valueIndex = after + 1;
                    return true;
                }

                from = index + 1;
            }

            startIndex = 0;
            valueIndex = 0;
            return false;
        }

        private static bool IsLabelSeparator(char c)
        {
            for (int i = 0; i < LabelSeparatorCharacters.Length; i++)
            {
                if (LabelSeparatorCharacters[i] == c)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether the labels found explain every part of the blob, so that believing them
        ///     cannot lose text.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A label is evidence and a missing label is not.</b> Shown two slots and asked
        ///         for the next step, a Convai Character answers <c>{target: Assembly Bench}</c> —
        ///         it filled one slot and deliberately left the other out, and it said which. That
        ///         used to be refused for not covering every slot, after which the blob fell to the
        ///         whitespace guess and <c>step</c> arrived holding <c>{target:</c>. Believing the
        ///         label and marking the unlabelled slot Missing is not a guess; it is what was
        ///         said.
        ///     </para>
        ///     <para>
        ///         Two conditions make that safe, and both are about text with nowhere to go. The
        ///         <b>last slot must be labelled</b>, because the final value runs to the end of the
        ///         blob — leave the last slot unlabelled and its text is swallowed by whichever
        ///         label came before it. And <b>nothing may precede the first label</b>, because
        ///         text there belongs to some slot no label claimed and there is no way to tell
        ///         which. Sent <c>low: 20 high: 80 Power Generator</c> against three slots, the
        ///         first condition fails and the blob is handed on, exactly as before.
        ///     </para>
        /// </remarks>
        private static bool AnchorsAccountForTheWholeBlob(
            string blob,
            List<(int ParameterIndex, int StartIndex, int ValueIndex)> anchors,
            int slotCount)
        {
            if (anchors.Count == slotCount)
                return true;

            int firstStart = int.MaxValue;
            bool lastSlotIsLabelled = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].StartIndex < firstStart)
                    firstStart = anchors[i].StartIndex;

                if (anchors[i].ParameterIndex == slotCount - 1)
                    lastSlotIsLabelled = true;
            }

            if (!lastSlotIsLabelled)
                return false;

            for (int i = 0; i < firstStart; i++)
            {
                if (!char.IsWhiteSpace(blob[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        ///     Removes the punctuation that separated one field from the next.
        /// </summary>
        /// <remarks>
        ///     A value carved out by labels ends where the next label begins, so it keeps whatever
        ///     the model wrote between them. Measured: <c>{step: 4, target: "Bench"}</c> gave
        ///     <c>step</c> the text <c>4,</c>, which is not a number — the visitor asked for step
        ///     four and the build advanced by one instead, twice, without a word anywhere.
        /// </remarks>
        private static string TrimFieldSeparators(string value)
        {
            value = value.Trim();
            int end = value.Length;
            while (end > 0 && (value[end - 1] == ',' || value[end - 1] == ';'))
                end--;

            return end == value.Length ? value : value.Substring(0, end).TrimEnd();
        }

        private static List<string> SplitByConnectors(
            string blob,
            IReadOnlyList<ConvaiActionParameterDefinition> parameters)
        {
            if (parameters.Count < 2)
                return new List<string>();

            var values = new List<string>(parameters.Count);
            string remaining = blob;
            for (int i = 1; i < parameters.Count; i++)
            {
                string connector = ConvaiActionParameterDefinition.Normalize(parameters[i]?.Connector);
                if (string.IsNullOrEmpty(connector))
                    return new List<string>();

                string separator = " " + connector + " ";
                int index = CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                    remaining,
                    separator,
                    CompareOptions.IgnoreCase);
                if (index < 0)
                    return new List<string>();

                values.Add(remaining.Substring(0, index).Trim());
                remaining = remaining.Substring(index + separator.Length).Trim();
            }

            values.Add(remaining);
            return values;
        }

        /// <summary>
        ///     Splits a blob whose slots arrived individually quoted.
        /// </summary>
        /// <remarks>
        ///     Removing quotes is a repair, so the same per-group vocabulary question is asked here
        ///     as in <see cref="SplitBraceWrapped" />: a target genuinely called <c>'Q'</c> keeps its
        ///     quotes rather than becoming a target called <c>Q</c> that nothing answers to.
        /// </remarks>
        private static List<string> SplitQuoted(string blob, ConvaiActionConfig vocabulary)
        {
            var values = new List<string>();
            foreach (Match match in QuotedRegex.Matches(blob))
            {
                string quoted = match.Value.Trim();
                values.Add(ConvaiActionWireText.NamesSomething(vocabulary, quoted)
                    ? quoted
                    : (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim());
            }

            return values;
        }

        /// <summary>
        ///     The last resort: cut the blob at spaces and let the final slot take the tail.
        /// </summary>
        /// <remarks>
        ///     Only ever reached with two or more slots — a single slot is answered before any
        ///     splitter runs. It used to carry a branch for one slot as well; that branch became
        ///     unreachable and is gone, because a guard nothing can reach reads as a case somebody
        ///     still has to think about.
        /// </remarks>
        private static List<string> SplitWhitespace(string blob, int expected)
        {
            var values = new List<string>();
            if (string.IsNullOrEmpty(blob))
                return values;

            string[] tokens = blob.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length <= expected)
            {
                values.AddRange(tokens);
                return values;
            }

            for (int i = 0; i < expected - 1; i++)
                values.Add(tokens[i]);

            values.Add(string.Join(" ", tokens, expected - 1, tokens.Length - expected + 1));
            return values;
        }

        /// <summary>
        ///     Lines the split values up with the authored parameter order, marking the slots
        ///     nothing reached with <c>null</c>.
        /// </summary>
        /// <remarks>
        ///     <c>null</c> rather than <see cref="string.Empty" />, and that is the whole point. A
        ///     slot the Convai Character never filled in and one it filled in as empty used to
        ///     arrive identically, so an Action Behavior could not tell "no destination was given"
        ///     from "the destination is blank". <see cref="ConvaiActionParameterPresence" /> carries
        ///     the difference outward; this is where it is known.
        /// </remarks>
        private static List<string> Pad(List<string> values, int expected)
        {
            values ??= new List<string>();
            while (values.Count < expected)
                values.Add(null);
            if (values.Count > expected)
                values.RemoveRange(expected, values.Count - expected);
            return values;
        }

        /// <summary>
        ///     Removes the prompt's own shape when the Convai Character copies it into a value —
        ///     unless the value is already the name of something this character has.
        /// </summary>
        /// <remarks>
        ///     Shown <c>{destination: reference}</c>, a model may answer <c>destination: East Hall</c>
        ///     or <c>[reference] East Hall</c>, and the label has to come off or the name matches
        ///     nothing. But a target legitimately called <c>Bay 2: North</c> is not mimicry, and
        ///     stripping it takes the name away from something that exists — so the vocabulary is
        ///     asked first, exactly as in <see cref="ConvaiActionWireText.Clean(string, ConvaiActionConfig)" />.
        /// </remarks>
        private static string StripParamNameMimicry(
            string value, string parameterName, ConvaiActionConfig vocabulary)
        {
            value = ConvaiActionText.Normalize(value);
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(parameterName))
                return value;

            string repaired = DropSlotLabel(
                BracketedNoiseRegex.Replace(value, string.Empty).Trim(), parameterName);

            if (!string.Equals(repaired, value, StringComparison.Ordinal) &&
                NamesSomethingExactly(vocabulary, value))
                return value;

            return repaired;
        }

        /// <summary>
        ///     Removes a leading <c>parameterName:</c> label from a value, however the model wrote
        ///     the label.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The label is what gets cleaned, never the value.</b> Asked for a slot, a model
        ///         may echo the whole thing as an object — <c>{"gesture": "wave"}</c> — and by the
        ///         time the value reaches here the braces have already been taken off by
        ///         <see cref="SplitBraceWrapped" />, leaving <c>"gesture": "wave"</c>. The key is
        ///         quoted and the plain <c>StartsWith("gesture:")</c> this used to do missed it, so
        ///         the label stayed glued on, the Choice matched nothing, and the parameter fell back
        ///         to its authored default. Measured live: a character announced a wave and stood
        ///         still.
        ///     </para>
        ///     <para>
        ///         Running the unbounded clean over the label is safe in a way it would never be over
        ///         the value: this text is about to be compared with an authored parameter name and
        ///         then thrown away, so nothing can lose its name here.
        ///     </para>
        ///     <para>
        ///         The whole segment before the colon must <em>be</em> the parameter name, not merely
        ///         start with it — which is stricter than what it replaces, and what leaves a target
        ///         called <c>Bay 2: North</c> intact even before the vocabulary is consulted.
        ///     </para>
        /// </remarks>
        private static string DropSlotLabel(string value, string parameterName)
        {
            int separator = value.IndexOfAny(LabelSeparatorCharacters);
            if (separator <= 0)
                return value;

            string label = ConvaiActionWireText.CleanWithoutVocabulary(value.Substring(0, separator));
            return string.Equals(label, parameterName, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(separator + 1).Trim()
                : value;
        }

        /// <summary>
        ///     The two ways a Convai Character writes "this is the value for that slot".
        /// </summary>
        /// <remarks>
        ///     One list, read by both the label drop above and <see cref="TryFindAnchor" />. Spelled
        ///     twice, a punctuation one of them learns and the other does not is a value that parses
        ///     in a two-slot action and not in a one-slot one — which is a bug nobody would think to
        ///     look for.
        /// </remarks>
        private static readonly char[] LabelSeparatorCharacters = { ':', '=' };

        /// <summary>
        ///     Whether this text is exactly the name or an alternate name of an available target.
        /// </summary>
        private static bool NamesSomethingExactly(ConvaiActionConfig vocabulary, string value) =>
            vocabulary != null &&
            ConvaiActionWireText.NamesSomething(vocabulary, value);

        private static ConvaiActionParameterValue Coerce(
            string rawValue,
            ConvaiActionParameterType declaredType,
            ConvaiActionConfig actionConfig,
            ConvaiActionParameterDefinition definition)
        {
            rawValue = ConvaiActionWireText.Clean(rawValue, actionConfig);
            bool hasNumber = float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float number);
            bool hasBool = TryParseBool(rawValue, out bool boolValue);
            ConvaiResolvedActionTarget reference = ConvaiResolvedActionTarget.Resolve(
                rawValue,
                actionConfig,
                (ConvaiActionTargetRequirement?)null);
            bool hasReference = reference != null;
            bool isConstraintMatch = MatchesChoice(rawValue, definition?.Choices);

            ConvaiActionParameterType type = declaredType;
            if (type == ConvaiActionParameterType.Auto)
            {
                if (hasReference) type = ConvaiActionParameterType.Reference;
                else if (hasNumber) type = ConvaiActionParameterType.Number;
                else if (hasBool) type = ConvaiActionParameterType.Bool;
                else type = ConvaiActionParameterType.String;
            }

            return new ConvaiActionParameterValue
            {
                Type = type,
                RawValue = rawValue,
                StringValue = rawValue,
                NumberValue = hasNumber ? number : 0f,
                BoolValue = hasBool && boolValue,
                ResolvedReference = hasReference
                    ? new ConvaiActionParameterReference(reference.Name, reference.Kind)
                    : null,
                IsConstraintMatch = declaredType != ConvaiActionParameterType.Choice || isConstraintMatch
            };
        }

        private static bool TryParseBool(string value, out bool result)
        {
            value = ConvaiActionText.Normalize(value).ToLowerInvariant();
            if (value == "true" || value == "yes" || value == "1")
            {
                result = true;
                return true;
            }

            if (value == "false" || value == "no" || value == "0")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        /// <summary>
        ///     The authored choice a rejected value most nearly resembles, or <c>null</c> when it
        ///     resembles none of them closely enough to be worth naming.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>For the sentence, never for the decision.</b> A Convai Character that answers
        ///         <c>waving</c> where the action offers <c>wave</c> has made one small mistake, and
        ///         whoever is reading the console can see it the instant something says so. Choosing
        ///         <c>wave</c> on its behalf is a different act altogether: the character would then
        ///         perform a value nobody sent, and the next near-miss — <c>walk</c> against
        ///         <c>wave</c> and <c>wink</c> — would be performed just as confidently.
        ///     </para>
        ///     <para>
        ///         The Unreal SDK's reader does choose this way, by edit distance. It is the one idea
        ///         from it deliberately not taken: naming the suspect is diagnosis, convicting it is
        ///         a guess wearing diagnosis as a disguise.
        ///     </para>
        ///     <para>
        ///         Only reached while composing a warning that is already being written, so the cost
        ///         is paid where somebody is about to read the result.
        ///     </para>
        /// </remarks>
        private static string NearestChoice(string value, IReadOnlyList<string> choices)
        {
            value = ConvaiActionText.Normalize(value);
            if (choices == null || choices.Count == 0 || value.Length == 0)
                return null;

            // A third of the word, so a longer choice tolerates a longer slip, and nothing unrelated
            // is ever offered: 'stop' is not a near miss for 'wave' at any length.
            int budget = Math.Max(1, value.Length / 3);
            string nearest = null;
            foreach (string authored in choices)
            {
                string choice = ConvaiActionParameterDefinition.Normalize(authored);
                if (choice.Length == 0)
                    continue;

                int distance = EditDistance(value, choice, budget);
                if (distance > budget)
                    continue;

                budget = distance;
                nearest = choice;
            }

            return nearest;
        }

        /// <summary>
        ///     How many single-character edits separate two short strings, giving up as soon as the
        ///     answer is known to exceed <paramref name="budget" />.
        /// </summary>
        /// <remarks>
        ///     Two rows rather than a full matrix, and an early exit on the row minimum: choices and
        ///     the values sent for them are words, and this runs only while a warning is being
        ///     composed, so the cheap version is the whole requirement.
        /// </remarks>
        private static int EditDistance(string left, string right, int budget)
        {
            if (Math.Abs(left.Length - right.Length) > budget)
                return budget + 1;

            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowBest = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitution = previous[j - 1] +
                                       (char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1])
                                           ? 0
                                           : 1);
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
                    if (current[j] < rowBest)
                        rowBest = current[j];
                }

                if (rowBest > budget)
                    return budget + 1;

                (previous, current) = (current, previous);
            }

            return previous[right.Length];
        }

        private static bool MatchesChoice(string value, IReadOnlyList<string> choices)
        {
            if (choices == null || choices.Count == 0)
                return true;

            for (int i = 0; i < choices.Count; i++)
            {
                string choice = ConvaiActionParameterDefinition.Normalize(choices[i]);
                if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
