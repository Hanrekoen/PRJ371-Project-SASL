using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Joins transcript text without adding whitespace when either boundary already contains it.
    /// </summary>
    internal static class TranscriptTextMerge
    {
        internal static string Append(string existing, string incoming)
        {
            bool hasExisting = !string.IsNullOrWhiteSpace(existing);
            bool hasIncoming = !string.IsNullOrWhiteSpace(incoming);

            if (!hasExisting) return incoming ?? string.Empty;
            if (!hasIncoming) return existing ?? string.Empty;

            char lastChar = existing[existing.Length - 1];
            char firstIncomingChar = incoming[0];
            return char.IsWhiteSpace(lastChar) || char.IsWhiteSpace(firstIncomingChar)
                ? existing + incoming
                : existing + " " + incoming;
        }

        internal static string Merge(string existing, string incoming)
        {
            if (string.IsNullOrWhiteSpace(existing)) return incoming ?? string.Empty;
            if (string.IsNullOrWhiteSpace(incoming)) return existing ?? string.Empty;

            if (incoming.Length > existing.Length && incoming.StartsWith(existing, StringComparison.Ordinal))
                return incoming;

            return Append(existing, incoming);
        }
    }
}
