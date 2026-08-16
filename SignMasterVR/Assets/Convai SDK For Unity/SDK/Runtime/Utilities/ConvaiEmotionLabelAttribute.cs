using System;
using UnityEngine;

namespace Convai.Runtime.Utilities
{
    /// <summary>
    ///     Marks a string field as the name of an emotion, so the Inspector offers the character's
    ///     emotion vocabulary as a dropdown instead of a text box.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An emotion name only does something when the character's vocabulary defines it, so a
    ///         typed name is a setting that looks configured and silently does nothing the moment it
    ///         is misspelled — or spelled in a vocabulary the character does not use. The dropdown is
    ///         built from that same vocabulary, which is also why an author who adds their own
    ///         emotions sees them offered here without any SDK change.
    ///     </para>
    ///     <para>
    ///         A name the vocabulary no longer defines stays selectable and is marked as unknown,
    ///         rather than being silently rewritten to whatever happens to be first in the list.
    ///     </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ConvaiEmotionLabelAttribute : PropertyAttribute
    {
        /// <summary>
        ///     Marks the field, optionally allowing "no emotion" as a choice.
        /// </summary>
        /// <param name="emptyOptionLabel">
        ///     Wording for the entry that clears the field, e.g. <c>"None — plain neutral rest"</c>.
        ///     Leave <c>null</c> when an empty value is not a meaningful setting, and the dropdown
        ///     will offer emotions only.
        /// </param>
        public ConvaiEmotionLabelAttribute(string emptyOptionLabel = null)
        {
            EmptyOptionLabel = emptyOptionLabel;
        }

        /// <summary>
        ///     Wording of the "no emotion chosen" entry, or <c>null</c> when the field must name one.
        /// </summary>
        public string EmptyOptionLabel { get; }

        /// <summary>Whether an empty value is a valid choice for this field.</summary>
        public bool AllowsEmpty => !string.IsNullOrEmpty(EmptyOptionLabel);
    }
}
