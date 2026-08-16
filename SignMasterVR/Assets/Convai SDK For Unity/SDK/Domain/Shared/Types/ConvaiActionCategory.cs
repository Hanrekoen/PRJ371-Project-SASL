using System;
using System.Text;

namespace Convai.Shared.Types
{
    /// <summary>
    ///     The single home of action-category semantics: how a category name is normalized, and what
    ///     makes two of them the same category. Categories are an authoring-time organization label —
    ///     they never reach the backend and never change what a character does.
    /// </summary>
    /// <remarks>
    ///     Everything that touches a category string goes through here. Category equality is
    ///     case-insensitive, so <c>tour</c> and <c>Tour</c> are one category rather than two that look
    ///     identical in the list; if each call site decided that for itself, one of them would
    ///     eventually decide differently and a user's grouping would quietly fork in half.
    /// </remarks>
    internal static class ConvaiActionCategory
    {
        /// <summary>
        ///     Longest category name kept. Names are labels in a narrow list column, not descriptions;
        ///     anything past this is truncated rather than allowed to push the count pill off the row.
        /// </summary>
        internal const int MaxLength = 48;

        /// <summary>
        ///     Comparer for dictionaries and sets keyed by category, so lookups agree with
        ///     <see cref="AreSame" /> by construction.
        /// </summary>
        internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        ///     Normalizes an authored category name: no category at all becomes
        ///     <see cref="string.Empty" />, and everything else is trimmed, has runs of whitespace and
        ///     any control characters collapsed to single spaces, and is capped at
        ///     <see cref="MaxLength" />.
        /// </summary>
        /// <remarks>
        ///     Allocation-free for the overwhelmingly common case — a name that is already clean is
        ///     returned as-is, which matters because this runs on every assignment and every
        ///     <c>Clone</c>, including inside editor draw passes.
        /// </remarks>
        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (IsAlreadyNormalized(value))
                return value;

            var builder = new StringBuilder(Math.Min(value.Length, MaxLength));
            bool pendingSpace = false;
            for (int i = 0; i < value.Length && builder.Length < MaxLength; i++)
            {
                char character = value[i];
                if (IsSeparator(character))
                {
                    // Only remembered, never written yet: a run of separators collapses to one space,
                    // and a trailing run writes nothing at all.
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                    if (builder.Length >= MaxLength)
                        break;
                }

                builder.Append(character);
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>Whether two authored category names name the same category.</summary>
        internal static bool AreSame(string left, string right) =>
            string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether this action carries no category and therefore belongs to the uncategorized bucket.</summary>
        internal static bool IsUncategorized(string value) => Normalize(value).Length == 0;

        /// <summary>A separator for naming purposes: ordinary whitespace, or a control character.</summary>
        private static bool IsSeparator(char character) =>
            char.IsWhiteSpace(character) || char.IsControl(character);

        private static bool IsAlreadyNormalized(string value)
        {
            if (value.Length > MaxLength || IsSeparator(value[0]) || IsSeparator(value[value.Length - 1]))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char character = value[i];
                if (!IsSeparator(character))
                    continue;

                // A single ordinary space between words is normal; anything else — a tab, a newline,
                // a second space — is not.
                if (character != ' ' || value[i - 1] == ' ')
                    return false;
            }

            return true;
        }
    }
}
