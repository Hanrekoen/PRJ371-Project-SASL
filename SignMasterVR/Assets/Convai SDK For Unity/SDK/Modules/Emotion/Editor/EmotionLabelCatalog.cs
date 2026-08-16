using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     The emotion labels a character's vocabulary defines, for the editor dropdowns that offer
    ///     them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every surface that used to need this list synthesized a whole default
    ///         <see cref="EmotionTaxonomyAsset" /> and destroyed it again — inside a draw method, so
    ///         two or three times per inspector repaint, on an inspector that repaints every frame in
    ///         Play Mode. The vocabulary cannot change between two frames of the same drag, so it is
    ///         resolved once and reused.
    ///     </para>
    ///     <para>
    ///         A short lifetime rather than a permanent cache: a taxonomy asset is editable, and an
    ///         author who adds an emotion should see it in the dropdown without reopening anything.
    ///         The synthesized built-in vocabulary is held for the editor session instead, since
    ///         nothing can change it.
    ///     </para>
    /// </remarks>
    internal static class EmotionLabelCatalog
    {
        /// <summary>Seconds a resolved label list stays valid. Long enough to be free, short enough to feel live.</summary>
        private const double CacheLifetimeSeconds = 1d;

        /// <summary>
        ///     The synthesized built-in vocabulary, built at most once per editor session and never
        ///     destroyed while it is cached. Hidden and not saved, exactly as
        ///     <see cref="EmotionTaxonomyAsset.CreateDefault" /> produces it.
        /// </summary>
        private static EmotionTaxonomyAsset s_builtIn;

        private static EmotionTaxonomyAsset s_cachedFor;
        private static double s_cachedAt;
        private static string[] s_cached;

        private static EmotionTaxonomyAsset BuiltIn()
        {
            if (s_builtIn != null) return s_builtIn;

            s_builtIn = EmotionTaxonomyAsset.CreateDefault();
            s_builtIn.hideFlags = HideFlags.HideAndDontSave;
            return s_builtIn;
        }

        /// <summary>
        ///     Every label <paramref name="taxonomy" /> defines, in authoring order, including its
        ///     neutral entry. Passing <c>null</c> resolves the built-in vocabulary.
        /// </summary>
        internal static string[] LabelsFor(EmotionTaxonomyAsset taxonomy)
        {
            EmotionTaxonomyAsset resolved = taxonomy != null ? taxonomy : BuiltIn();

            double now = EditorApplication.timeSinceStartup;
            if (s_cached != null && resolved == s_cachedFor && now - s_cachedAt < CacheLifetimeSeconds)
                return s_cached;

            resolved.EnsureBuilt();
            IReadOnlyList<EmotionDescriptor> emotions = resolved.Emotions;

            var labels = new List<string>(emotions.Count);
            for (int i = 0; i < emotions.Count; i++)
            {
                EmotionDescriptor descriptor = emotions[i];
                if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Label)) continue;
                if (IndexOf(labels, descriptor.Label) < 0) labels.Add(descriptor.Label);
            }

            s_cachedFor = resolved;
            s_cachedAt = now;
            s_cached = labels.ToArray();
            return s_cached;
        }

        /// <summary>
        ///     <see cref="LabelsFor(EmotionTaxonomyAsset)" /> for the vocabulary a profile uses, with
        ///     <paramref name="keepSelectable" /> appended when the vocabulary no longer defines it.
        /// </summary>
        /// <remarks>
        ///     A hand-edited label the vocabulary has since lost must stay selectable, or merely
        ///     opening the Inspector would silently rewrite the author's value to whatever happened
        ///     to be first in the list.
        /// </remarks>
        internal static string[] LabelsFor(ConvaiEmotionProfile profile, string keepSelectable)
        {
            string[] labels = LabelsFor(profile != null ? profile.Taxonomy : null);

            if (string.IsNullOrWhiteSpace(keepSelectable) || IndexOf(labels, keepSelectable) >= 0)
                return labels;

            var extended = new string[labels.Length + 1];
            System.Array.Copy(labels, extended, labels.Length);
            extended[labels.Length] = keepSelectable.Trim();
            return extended;
        }

        /// <summary>
        ///     How an emotion is written to a person: the stored name, capitalized.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Emotion names are stored lowercase because that is what the protocol and the
        ///         vocabulary match on, but a dropdown reading "joy / anger / anticipation" looks like
        ///         raw data rather than a list of choices. Every surface that offers emotions goes
        ///         through here so the same emotion is never written two ways in the same editor.
        ///     </para>
        ///     <para>
        ///         Only the presentation changes — what gets stored is always the vocabulary's own
        ///         canonical name.
        ///     </para>
        /// </remarks>
        internal static string DisplayName(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;

            string trimmed = label.Trim();
            var text = new System.Text.StringBuilder(trimmed.Length);
            bool startOfWord = true;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char character = trimmed[i];
                text.Append(startOfWord ? char.ToUpperInvariant(character) : character);
                startOfWord = character == ' ' || character == '-' || character == '_';
            }

            return text.ToString();
        }

        /// <summary>
        ///     <see cref="DisplayName" /> applied to a whole list, ready to hand to a popup.
        /// </summary>
        internal static string[] DisplayNames(IReadOnlyList<string> labels)
        {
            if (labels == null) return System.Array.Empty<string>();

            var display = new string[labels.Count];
            for (int i = 0; i < labels.Count; i++) display[i] = DisplayName(labels[i]);
            return display;
        }

        /// <summary>Case-insensitive index of <paramref name="label" />, or <c>-1</c>.</summary>
        internal static int IndexOf(IReadOnlyList<string> labels, string label)
        {
            if (labels == null || string.IsNullOrWhiteSpace(label)) return -1;
            for (int i = 0; i < labels.Count; i++)
                if (string.Equals(labels[i], label, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
