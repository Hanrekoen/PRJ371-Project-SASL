using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Caches the wire form of <see cref="ConvaiActionDefinition" /> templates.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The format itself lives in <see cref="ConvaiActionWireGrammar" /> — this type only
    ///         remembers what that produced. It used to own the format as well, which is what let
    ///         the reader drift from it: two files describing one grammar, neither able to see the
    ///         other change.
    ///     </para>
    ///     <para>
    ///         Definitions render repeatedly (config builds, allow-list filtering, command
    ///         enrichment), so the result is cached per definition instance and validated by a hash
    ///         of the fields that feed it, which keeps an inspector edit from serving a stale
    ///         template.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionTemplateRenderer
    {
        private sealed class RenderCacheEntry
        {
            public int InputHash;
            public string Rendered;
        }

        private static readonly ConditionalWeakTable<ConvaiActionDefinition, RenderCacheEntry> RenderCache = new();

        /// <summary>Renders the wire template for <paramref name="definition" />.</summary>
        public static string Render(ConvaiActionDefinition definition)
        {
            if (definition == null)
                return string.Empty;

            int inputHash = ComputeInputHash(definition);
            RenderCacheEntry entry = RenderCache.GetOrCreateValue(definition);
            if (entry.Rendered != null && entry.InputHash == inputHash)
                return entry.Rendered;

            string rendered = ConvaiActionWireGrammar.Render(definition);
            entry.InputHash = inputHash;
            entry.Rendered = rendered;
            return rendered;
        }

        private static int ComputeInputHash(ConvaiActionDefinition definition)
        {
            var hash = new HashCode();
            AddString(ref hash, definition.ActionName);
            AddString(ref hash, definition.Description);

            // Part of the rendered string since actions that need a target say so on the wire; without
            // it here, changing the requirement would serve a stale template out of the cache.
            hash.Add((int)definition.TargetRequirement);

            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    ConvaiActionParameterDefinition parameter = parameters[i];
                    if (parameter == null)
                    {
                        hash.Add(0);
                        continue;
                    }

                    AddString(ref hash, parameter.Name);
                    AddString(ref hash, parameter.Description);
                    hash.Add((int)parameter.Type);
                    AddString(ref hash, parameter.Connector);

                    if (parameter.Choices != null)
                    {
                        for (int choiceIndex = 0; choiceIndex < parameter.Choices.Count; choiceIndex++)
                            AddString(ref hash, parameter.Choices[choiceIndex]);
                    }
                }
            }

            return hash.ToHashCode();
        }

        private static void AddString(ref HashCode hash, string value) =>
            hash.Add(value == null ? 0 : StringComparer.Ordinal.GetHashCode(value));
    }
}
