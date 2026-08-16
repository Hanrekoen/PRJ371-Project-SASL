using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Pure word-matcher for referential gestures (the character gestures at what
    ///     it says). Given a spoken line, decides whether it contains a second-person word, a
    ///     first-person word, an ordinal/number word, and/or the name of a registered scene
    ///     object. Zero-alloc-per-category (a single tokenize pass feeds all four checks); a
    ///     plain POCO so it is unit-testable without a scene, character, or metadata registry.
    /// </summary>
    /// <remarks>
    ///     Word matching mirrors <c>Convai.Modules.Gaze.Providers.GazeReferentialGlances</c> —
    ///     the shipped referential-glance precedent — deliberately: same tokenization
    ///     (alphanumeric runs, lowercased, punctuation-agnostic), same whole-word/contiguous-run
    ///     object-name matching (longest name wins), same "modest allocation on an event, not a
    ///     per-frame path" budget. Duplicated rather than shared because BodyAnimation must
    ///     never reference the Gaze module.
    /// </remarks>
    internal static class ReferentialGestureMatcher
    {
        /// <summary>Longest registered object name (in words) that is matched — keeps matching cheap and sane.</summary>
        internal const int DefaultMaxObjectMentionWords = 4;

        private static readonly string[] SecondPersonWords = { "you", "your", "yours", "yourself" };
        private static readonly string[] FirstPersonWords = { "i", "me", "my", "mine", "myself" };

        private static readonly string[] OrdinalWords =
        {
            "first", "second", "third", "fourth", "fifth",
            "one", "two", "three", "four", "five"
        };

        /// <summary>Which referential classes a spoken line matched. Zero-alloc value type.</summary>
        internal readonly struct MatchResult
        {
            /// <summary>The line contains a whole-word second-person pronoun (you/your/yours/yourself).</summary>
            public readonly bool SecondPerson;

            /// <summary>The line contains a whole-word first-person pronoun (I/me/my/mine/myself).</summary>
            public readonly bool FirstPerson;

            /// <summary>The line contains a whole-word ordinal or number word (first..fifth, one..five).</summary>
            public readonly bool Ordinal;

            /// <summary>
            ///     The registered object name mentioned in the line (longest match wins), or
            ///     <c>null</c> when no registered object was mentioned.
            /// </summary>
            public readonly string ObjectName;

            public MatchResult(bool secondPerson, bool firstPerson, bool ordinal, string objectName)
            {
                SecondPerson = secondPerson;
                FirstPerson = firstPerson;
                Ordinal = ordinal;
                ObjectName = objectName;
            }

            /// <summary>True when a registered scene object was mentioned.</summary>
            public bool HasObjectMention => ObjectName != null;

            /// <summary>True when at least one referential class matched.</summary>
            public bool HasMatch => SecondPerson || FirstPerson || Ordinal || HasObjectMention;

            /// <summary>The no-match result: every class false, no object.</summary>
            internal static readonly MatchResult None = new(false, false, false, null);
        }

        /// <summary>
        ///     Matches <paramref name="utterance" /> against the built-in pronoun/ordinal word
        ///     lists and, when supplied, the registered object names — same source
        ///     (<c>ConvaiMetadataRegistry.GetValidMetadata()</c>) the Gaze referential-glance
        ///     precedent reads. <paramref name="objectNames" /> may be null/empty (no object
        ///     check performed).
        /// </summary>
        internal static MatchResult Match(
            string utterance, IReadOnlyList<string> objectNames, int maxObjectMentionWords = DefaultMaxObjectMentionWords)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return MatchResult.None;

            List<string> words = Tokenize(utterance);
            if (words.Count == 0) return MatchResult.None;

            bool secondPerson = ContainsAny(words, SecondPersonWords);
            bool firstPerson = ContainsAny(words, FirstPersonWords);
            bool ordinal = ContainsAny(words, OrdinalWords);

            string objectName = null;
            if (objectNames != null && objectNames.Count > 0)
                TryMatchObjectMention(words, objectNames, maxObjectMentionWords, out objectName);

            return new MatchResult(secondPerson, firstPerson, ordinal, objectName);
        }

        private static bool ContainsAny(List<string> words, string[] set)
        {
            for (int i = 0; i < words.Count; i++)
            {
                for (int j = 0; j < set.Length; j++)
                {
                    if (string.Equals(words[i], set[j], StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Finds the longest registered object name that appears as a whole-word,
        ///     contiguous run in <paramref name="words" /> (case-insensitive, punctuation
        ///     agnostic — mirrors <c>GazeReferentialGlances.TryMatchMention</c> exactly).
        ///     "Greedy" = the longest matching name wins, so "magic painting" beats a bare
        ///     "painting".
        /// </summary>
        private static bool TryMatchObjectMention(
            List<string> words, IReadOnlyList<string> objectNames, int maxMentionWords, out string matchedName)
        {
            matchedName = null;
            int limit = Mathf.Max(1, maxMentionWords);
            int bestWordCount = 0;
            var nameWords = new List<string>(8);

            for (int i = 0; i < objectNames.Count; i++)
            {
                string objName = objectNames[i];
                if (string.IsNullOrWhiteSpace(objName)) continue;

                nameWords.Clear();
                TokenizeInto(objName, nameWords);
                if (nameWords.Count == 0 || nameWords.Count > limit) continue;

                if (nameWords.Count > bestWordCount && ContainsSequence(words, nameWords))
                {
                    bestWordCount = nameWords.Count;
                    matchedName = objName;
                }
            }

            return matchedName != null;
        }

        private static List<string> Tokenize(string text)
        {
            var result = new List<string>(16);
            TokenizeInto(text, result);
            return result;
        }

        private static void TokenizeInto(string text, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return;

            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                bool alphanumeric = char.IsLetterOrDigit(text[i]);
                if (alphanumeric && start < 0)
                {
                    start = i;
                }
                else if (!alphanumeric && start >= 0)
                {
                    into.Add(text.Substring(start, i - start).ToLowerInvariant());
                    start = -1;
                }
            }

            if (start >= 0) into.Add(text.Substring(start).ToLowerInvariant());
        }

        private static bool ContainsSequence(List<string> haystack, List<string> needle)
        {
            if (needle.Count == 0 || needle.Count > haystack.Count) return false;

            for (int i = 0; i <= haystack.Count - needle.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Count; j++)
                {
                    if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return true;
            }

            return false;
        }
    }
}
