using System;
using System.Collections.Generic;
using System.Text;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Pure, GUI-free helpers backing <c>ConvaiActionTargetEditor</c>'s "Check a phrase"
    ///     mini-tool: mirrors the plain-English ladder steps documented on
    ///     <c>Convai.Runtime.Actions.ConvaiResolvedActionTarget</c> (exact, alias, normalized,
    ///     contains) scoped to a single target's own name/aliases — there is exactly one
    ///     candidate, so the ladder's duplicate-match tie-break does not apply. The runtime
    ///     ladder's own step implementations are private, so this reproduces their documented
    ///     semantics locally rather than forking private code; kept free of
    ///     <see cref="UnityEditor.SerializedProperty" />/GUI types so it is unit-testable without
    ///     a scene.
    /// </summary>
    internal static class ConvaiActionTargetPhraseMatcher
    {
        /// <summary>Which ladder step matched a checked phrase.</summary>
        internal enum MatchStep
        {
            /// <summary>Nothing matched.</summary>
            None = 0,

            /// <summary>The phrase is exactly the target's name (ladder step 1).</summary>
            Exact = 1,

            /// <summary>The phrase is exactly one of the target's aliases (ladder step 2).</summary>
            Alias = 2,

            /// <summary>
            ///     The phrase matches once "the"/"a"/"an" and extra spacing are ignored
            ///     (ladder step 3).
            /// </summary>
            Normalized = 3,

            /// <summary>The phrase is a partial (contains) match against the name (ladder step 4).</summary>
            Contains = 4
        }

        /// <summary>Result of checking one phrase against one target's name/aliases.</summary>
        internal readonly struct MatchResult
        {
            /// <summary>Which step matched, or <see cref="MatchStep.None" />.</summary>
            internal MatchStep Step { get; }

            /// <summary>The name/alias text that matched; null when <see cref="Step" /> is <see cref="MatchStep.None" />.</summary>
            internal string MatchedText { get; }

            internal MatchResult(MatchStep step, string matchedText)
            {
                Step = step;
                MatchedText = matchedText;
            }
        }

        private static readonly string[] LeadingArticles = { "the ", "an ", "a " };

        /// <summary>
        ///     Checks <paramref name="phrase" /> against <paramref name="targetName" /> and
        ///     <paramref name="aliases" /> in the same step order the runtime ladder applies:
        ///     exact, then alias, then normalized, then contains.
        /// </summary>
        internal static MatchResult Match(string phrase, string targetName, IReadOnlyList<string> aliases)
        {
            string trimmedPhrase = phrase?.Trim() ?? string.Empty;
            string trimmedName = targetName?.Trim() ?? string.Empty;
            if (trimmedPhrase.Length == 0 || trimmedName.Length == 0)
                return new MatchResult(MatchStep.None, null);

            if (string.Equals(trimmedName, trimmedPhrase, StringComparison.OrdinalIgnoreCase))
                return new MatchResult(MatchStep.Exact, trimmedName);

            if (aliases != null)
            {
                for (int i = 0; i < aliases.Count; i++)
                {
                    string alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;

                    // Trimmed, exactly like the runtime ladder's alias step
                    // (ConvaiResolvedActionTarget.HasAliasMatch): an alias typed with stray padding
                    // matches there, so it must match here too — the ladder-parity EditMode tests
                    // pin this mirror, and they are the reason a change to one side cannot quietly
                    // leave the other behind.
                    if (string.Equals(alias.Trim(), trimmedPhrase, StringComparison.OrdinalIgnoreCase))
                        return new MatchResult(MatchStep.Alias, alias.Trim());
                }
            }

            string normalizedPhrase = Normalize(trimmedPhrase);
            string normalizedName = Normalize(trimmedName);

            if (normalizedPhrase.Length > 0 &&
                string.Equals(normalizedPhrase, normalizedName, StringComparison.OrdinalIgnoreCase))
                return new MatchResult(MatchStep.Normalized, trimmedName);

            if (normalizedPhrase.Length > 0 && normalizedName.Length > 0 &&
                (normalizedName.IndexOf(normalizedPhrase, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 normalizedPhrase.IndexOf(normalizedName, StringComparison.OrdinalIgnoreCase) >= 0))
                return new MatchResult(MatchStep.Contains, trimmedName);

            return new MatchResult(MatchStep.None, null);
        }

        /// <summary>Beginner-language description of a <see cref="MatchResult" />, for the mini-tool's result line.</summary>
        internal static string Describe(MatchResult result) =>
            result.Step switch
            {
                MatchStep.Exact => "Matches: exact name (step 1 — Exact Name).",
                MatchStep.Alias => $"Matches: alias '{result.MatchedText}' (step 2 — Alias).",
                MatchStep.Normalized =>
                    "Matches: normalized match, ignoring \"the\"/\"a\"/\"an\" and extra spaces (step 3 — Normalized).",
                MatchStep.Contains => $"Matches: partial match with '{result.MatchedText}' (step 4 — Contains).",
                _ => "No match — try the exact name or one of the aliases."
            };

        /// <summary>
        ///     The name the resolution ladder actually uses for a target: the authored name when
        ///     set, otherwise the owning GameObject's name (mirrors
        ///     <c>Convai.Runtime.Actions.ConvaiActionTarget.TargetName</c>).
        /// </summary>
        internal static string EffectiveName(string authoredName, string gameObjectName) =>
            string.IsNullOrWhiteSpace(authoredName) ? gameObjectName ?? string.Empty : authoredName;

        /// <summary>Strips a leading "the"/"a"/"an" and collapses internal whitespace, case-insensitively.</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string collapsed = CollapseWhitespace(value.Trim());
            return StripLeadingArticle(collapsed);
        }

        private static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) builder.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
            }

            return builder.ToString();
        }

        private static string StripLeadingArticle(string value)
        {
            for (int i = 0; i < LeadingArticles.Length; i++)
            {
                string article = LeadingArticles[i];
                if (value.Length > article.Length && value.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(article.Length);
            }

            return value;
        }
    }
}
