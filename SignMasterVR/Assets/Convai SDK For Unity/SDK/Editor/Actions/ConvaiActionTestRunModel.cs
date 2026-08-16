using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Convai.Runtime.Actions;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Pure, GUI-free command building for the Actions Editor window's Test Run panel
    ///     : turns typed parameter inputs into the same wire-shaped text the
    ///     backend would send, then runs it through the real enrichment parser
    ///     (<see cref="ConvaiActionResponseParser.Enrich" />) so type coercion, choice validation,
    ///     and reference resolution behave exactly as they do for a live conversation command.
    ///     No <c>UnityEditor</c>/GUI types, so it is unit-testable without a scene.
    /// </summary>
    internal static class ConvaiActionTestRunModel
    {
        /// <summary>
        ///     Renders parameter input texts as a brace-wrapped value blob (<c>"{a} {b}"</c>) in the
        ///     definition's parameter order — the wire shape the enrichment parser splits first and
        ///     most robustly (values may contain spaces). Blank/omitted optional inputs render as
        ///     empty braces so positional order is preserved.
        /// </summary>
        internal static string BuildParameterBlob(
            IReadOnlyList<ConvaiActionParameterDefinition> parameters,
            IReadOnlyList<string> texts)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(parameters.Count * 12);
            for (int i = 0; i < parameters.Count; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                string text = texts != null && i < texts.Count ? texts[i]?.Trim() ?? string.Empty : string.Empty;
                builder.Append('{').Append(text).Append('}');
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Whether a Number parameter's input text will coerce to a number: empty is fine
        ///     (optional, omitted), otherwise it must parse as an invariant-culture float — the
        ///     exact parse the enrichment coercion applies.
        /// </summary>
        internal static bool IsNumberTextValid(string text) =>
            string.IsNullOrWhiteSpace(text) ||
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        /// <summary>
        ///     Builds the enriched, dispatch-ready command for one test run. Parameterless actions
        ///     carry <paramref name="targetName" /> as the command target (what the backend sends
        ///     for "verb the thing" commands); parameterized actions carry the brace-wrapped value
        ///     blob and, when a target was also picked, the target name is applied to the enriched
        ///     command afterwards — the field the backend would have populated. Returns null when
        ///     the definition is unusable (null or unnamed).
        /// </summary>
        internal static ConvaiActionCommand BuildCommand(
            ConvaiActionDefinition definition,
            string targetName,
            IReadOnlyList<string> parameterTexts,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ActionName))
                return null;

            bool hasParameters = definition.Parameters != null && definition.Parameters.Count > 0;
            string trimmedTarget = targetName?.Trim() ?? string.Empty;
            string targetText = hasParameters
                ? BuildParameterBlob(definition.Parameters, parameterTexts)
                : trimmedTarget;

            var command = new ConvaiActionCommand(definition.ActionName, targetText);
            IReadOnlyList<ConvaiActionDefinition> enrichmentDefinitions =
                definitions != null && definitions.Count > 0 ? definitions : new[] { definition };
            ConvaiActionCommand enriched =
                ConvaiActionResponseParser.Enrich(command, actionConfig, enrichmentDefinitions);

            if (hasParameters && trimmedTarget.Length > 0)
            {
                // The backend carries target and parameters in one string; locally we let the real
                // parser split the parameter blob, then aim the command at the picked target — the
                // same Target field a wire command would carry into the resolution ladder.
                enriched.Target = trimmedTarget;
                enriched.ActionString = $"{enriched.Name} {enriched.Target}".Trim();
            }

            // Test runs happen without a conversation: skip the dispatcher's first-step speech
            // gate instead of stalling to its timeout (internal flag; never set on wire commands).
            enriched.BypassSpeechGate = true;
            return enriched;
        }
    }
}
