using System;
using System.Collections.Generic;

namespace Convai.Modules.Embodiment.Presets
{
    /// <summary>
    ///     Shared duplicate detection for preset and library validation.
    /// </summary>
    internal static class DuplicateDetector
    {
        /// <summary>
        ///     Detects duplicate keys in a collection and generates a diagnostic message.
        /// </summary>
        /// <typeparam name="T">Item type in the collection.</typeparam>
        /// <param name="items">Collection to check for duplicates.</param>
        /// <param name="keySelector">Function extracting the key from each item.</param>
        /// <param name="message">Diagnostic message if duplicates found; null otherwise.</param>
        /// <returns>True if duplicates exist; false otherwise.</returns>
        public static bool HasDuplicates<T>(
            IReadOnlyList<T> items,
            Func<T, string> keySelector,
            out string message)
        {
            message = null;

            if (items == null || items.Count < 2 || keySelector == null)
                return false;

            var firstOccurrence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];
                string key = keySelector(item);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (firstOccurrence.ContainsKey(key))
                {
                    // Mark first occurrence (preserves original casing).
                    duplicates.Add(firstOccurrence[key]);
                }
                else
                {
                    firstOccurrence[key] = key;
                }
            }

            if (duplicates.Count == 0)
                return false;

            var duplicateList = new List<string>(duplicates);
            duplicateList.Sort(StringComparer.OrdinalIgnoreCase);

            message = string.Join(", ", duplicateList);
            return true;
        }
    }
}
