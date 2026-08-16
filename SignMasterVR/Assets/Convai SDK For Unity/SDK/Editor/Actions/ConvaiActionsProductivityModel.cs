using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The Actions Editor window's list scope filter (the row's list-mode/type/kind bucket).
    ///     Combined with the free-text search: a row must pass both to stay visible. Persisted per
    ///     editor session via <c>SessionState</c>.
    /// </summary>
    internal enum ConvaiActionsListFilter
    {
        /// <summary>No scope filtering — every action passes.</summary>
        All = 0,

        /// <summary>Only rows whose computed status is not Ready (needs attention or broken).</summary>
        NeedsAttention = 1,

        /// <summary>Only rows whose action is unticked (not offered to the Convai Character).</summary>
        NotOffered = 2,

        /// <summary>Only inline rows authored directly on the picked character.</summary>
        ThisCharacter = 3,

        /// <summary>Only rows shared from an assigned Action Set asset.</summary>
        FromActionSets = 4
    }

    /// <summary>
    ///     Copy/paste clipboard for action definitions, backing the list's row-menu Copy/Paste.
    ///     Holds one <em>detached snapshot</em> — a deep clone whose scene
    ///     <see cref="ConvaiActionDefinition.Executor" /> reference is replaced by its type name in
    ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> — so a copied action can be pasted
    ///     onto a different Convai Character (or after the source character is gone) without ever
    ///     aliasing a live definition or leaking a scene reference. Static so the clipboard survives
    ///     window reloads within one editor session.
    /// </summary>
    internal static class ConvaiActionsClipboard
    {
        private static ConvaiActionDefinition s_snapshot;

        /// <summary>Whether a copied action is available to paste.</summary>
        internal static bool HasContent => s_snapshot != null;

        /// <summary>Stores a detached snapshot of <paramref name="definition" />.</summary>
        internal static void Copy(ConvaiActionDefinition definition) =>
            s_snapshot = ConvaiActionsProductivityModel.CreateDetachedSnapshot(definition);

        /// <summary>
        ///     A fresh clone of the held snapshot (each paste gets its own instance, so pasting
        ///     twice never produces two rows sharing one definition). Null when empty.
        /// </summary>
        internal static ConvaiActionDefinition CreatePasteClone() => s_snapshot?.Clone();

        /// <summary>Test seam: clears the clipboard.</summary>
        internal static void Clear() => s_snapshot = null;
    }

    /// <summary>
    ///     Pure, GUI-free logic behind the Actions Editor window's productivity pack: detached
    ///     definition snapshots for copy/duplicate/extract, collision-safe
    ///     action naming, inline→Action Set conversion, and the list scope filter predicate. No
    ///     <c>UnityEditor</c> dependency, so all of it is unit-testable without a window instance.
    /// </summary>
    internal static class ConvaiActionsProductivityModel
    {
        private const string FallbackActionName = "New Action";
        private const string CopySuffix = " Copy";

        /// <summary>
        ///     Deep clone of <paramref name="definition" /> with the scene behavior detached: a
        ///     bound <see cref="ConvaiActionDefinition.Executor" /> component becomes its type name
        ///     in <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> (the same hint semantics
        ///     Action Set assets use), and the component reference itself is dropped. A definition
        ///     that already relies on a hint keeps it.
        /// </summary>
        internal static ConvaiActionDefinition CreateDetachedSnapshot(ConvaiActionDefinition definition)
        {
            if (definition == null)
                return null;

            ConvaiActionDefinition snapshot = definition.Clone();
            if (snapshot.Executor != null)
            {
                snapshot.ExecutorTypeHint = snapshot.Executor.GetType().Name;
                snapshot.Executor = null;
            }

            return snapshot;
        }

        /// <summary>
        ///     Converts one inline definition for life inside a <see cref="ConvaiActionSet" /> asset.
        ///     <paramref name="behaviorLost" /> is true when the result carries no behavior hint at
        ///     all (no bound component and no authored hint) — the action will not run from the set
        ///     until a behavior is chosen, which callers must surface as a warning.
        /// </summary>
        internal static ConvaiActionDefinition ConvertForActionSet(
            ConvaiActionDefinition definition,
            out bool behaviorLost)
        {
            ConvaiActionDefinition converted = CreateDetachedSnapshot(definition);
            behaviorLost = converted != null && string.IsNullOrWhiteSpace(converted.ExecutorTypeHint);
            return converted;
        }

        /// <summary>
        ///     Collision-safe action name: returns <paramref name="desiredName" /> unchanged when it
        ///     is free, otherwise appends " Copy" (then " Copy 2", " Copy 3", …) until unique.
        ///     Comparison is case-insensitive, matching runtime action-name matching. A blank
        ///     desired name falls back to "New Action" first.
        /// </summary>
        internal static string MakeUniqueActionName(string desiredName, ICollection<string> existingNames)
        {
            string baseName = string.IsNullOrWhiteSpace(desiredName) ? FallbackActionName : desiredName.Trim();
            if (!ContainsName(existingNames, baseName))
                return baseName;

            return AppendCopySuffix(baseName, existingNames);
        }

        /// <summary>
        ///     Name for a duplicate row: always carries the " Copy" suffix (then " Copy 2", …), so a
        ///     duplicate is never mistaken for the original even in lists that would tolerate the
        ///     collision. Case-insensitive, like <see cref="MakeUniqueActionName" />.
        /// </summary>
        internal static string MakeDuplicateActionName(string originalName, ICollection<string> existingNames)
        {
            string baseName = string.IsNullOrWhiteSpace(originalName) ? FallbackActionName : originalName.Trim();
            return AppendCopySuffix(baseName, existingNames);
        }

        private static string AppendCopySuffix(string baseName, ICollection<string> existingNames)
        {
            string candidate = baseName + CopySuffix;
            int counter = 2;
            while (ContainsName(existingNames, candidate))
            {
                candidate = baseName + CopySuffix + " " + counter;
                counter++;
            }

            return candidate;
        }

        private static bool ContainsName(ICollection<string> existingNames, string candidate)
        {
            if (existingNames == null)
                return false;

            foreach (string existing in existingNames)
            {
                if (!string.IsNullOrEmpty(existing) &&
                    string.Equals(existing.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Every action name currently authored on <paramref name="source" /> — inline
        ///     definitions plus every assigned Action Set's — the collision domain for paste,
        ///     duplicate, and local-copy naming (a new inline name colliding with a set entry would
        ///     silently shadow it through the runtime's inline-wins merge).
        /// </summary>
        internal static List<string> CollectEffectiveActionNames(ConvaiActionConfigSource source)
        {
            var names = new List<string>();
            if (source == null)
                return names;

            AppendNames(names, source.Definitions);
            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            if (sets != null)
            {
                for (int i = 0; i < sets.Count; i++)
                {
                    if (sets[i] != null)
                        AppendNames(names, sets[i].Definitions);
                }
            }

            return names;
        }

        /// <summary>Action names inside one <see cref="ConvaiActionSet" /> (extraction's collision domain).</summary>
        internal static List<string> CollectSetActionNames(ConvaiActionSet set)
        {
            var names = new List<string>();
            if (set != null)
                AppendNames(names, set.Definitions);
            return names;
        }

        private static void AppendNames(List<string> names, IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (definitions == null)
                return;

            for (int i = 0; i < definitions.Count; i++)
            {
                string name = definitions[i]?.ActionName;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
        }

        /// <summary>
        ///     Scope filter predicate for one row . Pure so the truth table is
        ///     directly testable: status feeds Needs Attention, ownership feeds This Character /
        ///     From Action Sets, and the authored availability flag feeds Not Offered.
        /// </summary>
        internal static bool MatchesListFilter(
            ConvaiActionsListFilter filter,
            ConvaiActionRowStatus status,
            bool isShared,
            bool enabled) =>
            filter switch
            {
                ConvaiActionsListFilter.NeedsAttention => status != ConvaiActionRowStatus.Ready,
                ConvaiActionsListFilter.NotOffered => !enabled,
                ConvaiActionsListFilter.ThisCharacter => !isShared,
                ConvaiActionsListFilter.FromActionSets => isShared,
                _ => true
            };

        /// <summary>
        ///     Maps every definition authored on <paramref name="source" /> (inline and assigned
        ///     sets) to its validator context key — the same positional key
        ///     <see cref="ConvaiActionsEditorModel.BuildRowContextKey" /> produces — so a persisted
        ///     multi-selection can be restored across domain reloads. Later duplicates of a key keep
        ///     the first mapping (positional keys are unique in practice).
        /// </summary>
        internal static void CollectDefinitionsByContextKey(
            ConvaiActionConfigSource source,
            Dictionary<string, ConvaiActionDefinition> map)
        {
            if (source == null || map == null)
                return;

            IReadOnlyList<ConvaiActionDefinition> inline = source.Definitions;
            if (inline != null)
            {
                for (int i = 0; i < inline.Count; i++)
                {
                    if (inline[i] == null)
                        continue;

                    var row = new ConvaiActionRow(inline[i], ConvaiActionRowStatus.Ready, false, i, null, string.Empty);
                    string key = ConvaiActionsEditorModel.BuildRowContextKey(source, row);
                    if (!map.ContainsKey(key))
                        map.Add(key, inline[i]);
                }
            }

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            if (sets == null)
                return;

            for (int s = 0; s < sets.Count; s++)
            {
                ConvaiActionSet set = sets[s];
                if (set == null)
                    continue;

                IReadOnlyList<ConvaiActionDefinition> definitions = set.Definitions;
                for (int d = 0; d < definitions.Count; d++)
                {
                    if (definitions[d] == null)
                        continue;

                    var row = new ConvaiActionRow(definitions[d], ConvaiActionRowStatus.Ready, true, d, set, string.Empty);
                    string key = ConvaiActionsEditorModel.BuildRowContextKey(source, row);
                    if (!map.ContainsKey(key))
                        map.Add(key, definitions[d]);
                }
            }
        }

        /// <summary>
        ///     Whether <paramref name="definition" /> is still authored anywhere on
        ///     <paramref name="source" /> (inline or in an assigned set) — the multi-selection
        ///     pruning check after removals and undo.
        /// </summary>
        internal static bool IsDefinitionAuthored(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            if (source == null || definition == null)
                return false;

            if (IndexOf(source.Definitions, definition) >= 0)
                return true;

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            if (sets == null)
                return false;

            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null && IndexOf(sets[i].Definitions, definition) >= 0)
                    return true;
            }

            return false;
        }

        private static int IndexOf(IReadOnlyList<ConvaiActionDefinition> definitions, ConvaiActionDefinition definition)
        {
            if (definitions == null)
                return -1;

            for (int i = 0; i < definitions.Count; i++)
            {
                if (ReferenceEquals(definitions[i], definition))
                    return i;
            }

            return -1;
        }
    }
}
