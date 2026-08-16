using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Types;

namespace Convai.Editor.Actions
{
    /// <summary>How the action list is grouped — the axis behind the Actions Editor's "Group by" control.</summary>
    internal enum ConvaiActionsGroupAxis
    {
        /// <summary>Where the action comes from: the character itself, or one of its Action Sets. The original list.</summary>
        Source = 0,

        /// <summary>The category the user filed the action under.</summary>
        Category = 1,

        /// <summary>Traffic-light health: what is broken, what needs attention, what is ready.</summary>
        Status = 2,

        /// <summary>The behavior family that powers the action (Gaze, Body Animation, …).</summary>
        Behavior = 3
    }

    /// <summary>
    ///     Pure, GUI-free regrouping of an already-built action list along a chosen
    ///     <see cref="ConvaiActionsGroupAxis" />, plus the category-name hygiene the authoring commands
    ///     share (existing names, near-duplicate detection, cold-start suggestions).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately a <em>re</em>grouping of <see cref="ConvaiActionsEditorModel.BuildGroups" />'s
    ///         output rather than a second row builder. Row status, search/scope filtering and — most
    ///         importantly — <see cref="ConvaiActionRow.OwnerIndex" />/<see cref="ConvaiActionRow.OwningSet" />
    ///         are computed in exactly one place, so no matter how the list is displayed, every edit
    ///         still writes to the true authored position.
    ///     </para>
    ///     <para>
    ///         Nothing here knows what a category <em>means</em>: identity, casing and length live in
    ///         <see cref="ConvaiActionCategory" />, and the behavior family is supplied by the caller
    ///         as a resolver so this stays testable without a scene or a type catalog.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionsGrouping
    {
        /// <summary>Header shown for actions the user has not filed under any category.</summary>
        internal const string UncategorizedTitle = "Uncategorized";

        /// <summary>Header shown when a behavior family cannot be determined (no bound behavior yet).</summary>
        internal const string UnknownBehaviorTitle = "Not Set Up Yet";

        /// <summary>Group key for one category (case-insensitive, so a re-cased name keeps its collapse state).</summary>
        internal static string BuildCategoryGroupKey(string category) =>
            "category:" + ConvaiActionCategory.Normalize(category).ToLowerInvariant();

        /// <summary>Group key for one status bucket.</summary>
        internal static string BuildStatusGroupKey(ConvaiActionRowStatus status) => "status:" + (int)status;

        /// <summary>Group key for one behavior family.</summary>
        internal static string BuildBehaviorGroupKey(string family) =>
            "behavior:" + (family ?? string.Empty).ToLowerInvariant();

        /// <summary>
        ///     Regroups <paramref name="sourceGroups" /> along <paramref name="axis" />.
        ///     <see cref="ConvaiActionsGroupAxis.Source" /> returns the list unchanged — it is what the
        ///     source groups already are.
        /// </summary>
        /// <param name="resolveBehaviorFamily">
        ///     Maps one definition to its behavior family; only consulted for
        ///     <see cref="ConvaiActionsGroupAxis.Behavior" />. Null or an empty result files the row
        ///     under <see cref="UnknownBehaviorTitle" />.
        /// </param>
        internal static List<ConvaiActionGroup> Regroup(
            List<ConvaiActionGroup> sourceGroups,
            ConvaiActionsGroupAxis axis,
            Func<ConvaiActionDefinition, string> resolveBehaviorFamily = null)
        {
            if (sourceGroups == null)
                return new List<ConvaiActionGroup>();

            switch (axis)
            {
                case ConvaiActionsGroupAxis.Category:
                    return GroupByCategory(sourceGroups);
                case ConvaiActionsGroupAxis.Status:
                    return GroupByStatus(sourceGroups);
                case ConvaiActionsGroupAxis.Behavior:
                    return GroupByBehavior(sourceGroups, resolveBehaviorFamily);
                default:
                    return sourceGroups;
            }
        }

        /// <summary>
        ///     Categories in alphabetical order with the uncategorized bucket always last, because it is
        ///     the "everything else" pile and a pile belongs at the bottom. A category's header uses the
        ///     casing it was first authored with, so re-typing "tour" later does not rewrite the group's
        ///     name in the list.
        /// </summary>
        private static List<ConvaiActionGroup> GroupByCategory(List<ConvaiActionGroup> sourceGroups)
        {
            var byKey = new Dictionary<string, ConvaiActionGroup>(ConvaiActionCategory.Comparer);
            var ordered = new List<ConvaiActionGroup>();
            ConvaiActionGroup uncategorized = null;

            for (int g = 0; g < sourceGroups.Count; g++)
            {
                List<ConvaiActionRow> rows = sourceGroups[g]?.Rows;
                for (int r = 0; rows != null && r < rows.Count; r++)
                {
                    ConvaiActionRow row = rows[r];
                    string category = ConvaiActionCategory.Normalize(row.Definition?.Category);

                    if (category.Length == 0)
                    {
                        uncategorized ??= new ConvaiActionGroup
                        {
                            Title = UncategorizedTitle,
                            Kind = ConvaiActionGroupKind.Category,
                            Key = BuildCategoryGroupKey(string.Empty),
                            CategoryName = string.Empty
                        };
                        uncategorized.Rows.Add(row);
                        continue;
                    }

                    if (!byKey.TryGetValue(category, out ConvaiActionGroup group))
                    {
                        group = new ConvaiActionGroup
                        {
                            Title = category,
                            Kind = ConvaiActionGroupKind.Category,
                            Key = BuildCategoryGroupKey(category),
                            CategoryName = category
                        };
                        byKey.Add(category, group);
                        ordered.Add(group);
                    }

                    group.Rows.Add(row);
                }
            }

            ordered.Sort(static (left, right) =>
                string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));

            if (uncategorized != null)
                ordered.Add(uncategorized);

            RefreshRollups(ordered);
            return ordered;
        }

        /// <summary>
        ///     Status buckets, worst first: what is broken is what the user came to fix, and a list that
        ///     opens on "Ready" would bury it.
        /// </summary>
        private static List<ConvaiActionGroup> GroupByStatus(List<ConvaiActionGroup> sourceGroups)
        {
            ConvaiActionGroup broken = null;
            ConvaiActionGroup needsAttention = null;
            ConvaiActionGroup ready = null;

            for (int g = 0; g < sourceGroups.Count; g++)
            {
                List<ConvaiActionRow> rows = sourceGroups[g]?.Rows;
                for (int r = 0; rows != null && r < rows.Count; r++)
                {
                    ConvaiActionRow row = rows[r];
                    switch (row.Status)
                    {
                        case ConvaiActionRowStatus.Broken:
                            broken ??= CreateStatusGroup(ConvaiActionRowStatus.Broken);
                            broken.Rows.Add(row);
                            break;
                        case ConvaiActionRowStatus.NeedsAttention:
                            needsAttention ??= CreateStatusGroup(ConvaiActionRowStatus.NeedsAttention);
                            needsAttention.Rows.Add(row);
                            break;
                        default:
                            ready ??= CreateStatusGroup(ConvaiActionRowStatus.Ready);
                            ready.Rows.Add(row);
                            break;
                    }
                }
            }

            var ordered = new List<ConvaiActionGroup>(3);
            if (broken != null) ordered.Add(broken);
            if (needsAttention != null) ordered.Add(needsAttention);
            if (ready != null) ordered.Add(ready);

            RefreshRollups(ordered);
            return ordered;
        }

        private static ConvaiActionGroup CreateStatusGroup(ConvaiActionRowStatus status) =>
            new()
            {
                Title = status switch
                {
                    ConvaiActionRowStatus.Broken => "Not Working Yet",
                    ConvaiActionRowStatus.NeedsAttention => "Needs Attention",
                    _ => "Ready"
                },
                Kind = ConvaiActionGroupKind.Status,
                Key = BuildStatusGroupKey(status)
            };

        /// <summary>
        ///     Behavior families in alphabetical order, with the actions that have no behavior yet last —
        ///     same reasoning as the uncategorized pile.
        /// </summary>
        private static List<ConvaiActionGroup> GroupByBehavior(
            List<ConvaiActionGroup> sourceGroups,
            Func<ConvaiActionDefinition, string> resolveBehaviorFamily)
        {
            var byKey = new Dictionary<string, ConvaiActionGroup>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ConvaiActionGroup>();
            ConvaiActionGroup unknown = null;

            for (int g = 0; g < sourceGroups.Count; g++)
            {
                List<ConvaiActionRow> rows = sourceGroups[g]?.Rows;
                for (int r = 0; rows != null && r < rows.Count; r++)
                {
                    ConvaiActionRow row = rows[r];
                    string family = resolveBehaviorFamily == null
                        ? string.Empty
                        : resolveBehaviorFamily(row.Definition) ?? string.Empty;

                    if (family.Length == 0)
                    {
                        unknown ??= new ConvaiActionGroup
                        {
                            Title = UnknownBehaviorTitle,
                            Kind = ConvaiActionGroupKind.Behavior,
                            Key = BuildBehaviorGroupKey(string.Empty)
                        };
                        unknown.Rows.Add(row);
                        continue;
                    }

                    if (!byKey.TryGetValue(family, out ConvaiActionGroup group))
                    {
                        group = new ConvaiActionGroup
                        {
                            Title = family,
                            Kind = ConvaiActionGroupKind.Behavior,
                            Key = BuildBehaviorGroupKey(family)
                        };
                        byKey.Add(family, group);
                        ordered.Add(group);
                    }

                    group.Rows.Add(row);
                }
            }

            ordered.Sort(static (left, right) =>
                string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));

            if (unknown != null)
                ordered.Add(unknown);

            RefreshRollups(ordered);
            return ordered;
        }

        private static void RefreshRollups(List<ConvaiActionGroup> groups)
        {
            for (int i = 0; i < groups.Count; i++)
                groups[i].RefreshRollup();
        }

        // ── Category names ─────────────────────────────────────────────────────

        /// <summary>
        ///     Whether this character has filed anything under a category yet — inline or through an
        ///     assigned set. What decides whether the window opens on the Category axis at all.
        /// </summary>
        internal static bool HasAnyCategory(ConvaiActionConfigSource source)
        {
            if (source == null)
                return false;

            if (HasAnyCategory(source.Definitions))
                return true;

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            for (int i = 0; sets != null && i < sets.Count; i++)
            {
                if (sets[i] != null && HasAnyCategory(sets[i].Definitions))
                    return true;
            }

            return false;
        }

        private static bool HasAnyCategory(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                if (definitions[i] != null && !ConvaiActionCategory.IsUncategorized(definitions[i].Category))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Every category name in use on this character, alphabetically, in the casing each was
        ///     first authored with. Backs the "Move To Category" menu and the new-category field's
        ///     autocomplete, so the two can never offer different names.
        /// </summary>
        internal static List<string> CollectCategoryNames(ConvaiActionConfigSource source)
        {
            var seen = new HashSet<string>(ConvaiActionCategory.Comparer);
            var names = new List<string>();
            if (source == null)
                return names;

            CollectCategoryNames(source.Definitions, seen, names);

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            for (int i = 0; sets != null && i < sets.Count; i++)
            {
                if (sets[i] != null)
                    CollectCategoryNames(sets[i].Definitions, seen, names);
            }

            names.Sort(static (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            return names;
        }

        private static void CollectCategoryNames(
            IReadOnlyList<ConvaiActionDefinition> definitions, HashSet<string> seen, List<string> names)
        {
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                string category = ConvaiActionCategory.Normalize(definitions[i]?.Category);
                if (category.Length > 0 && seen.Add(category))
                    names.Add(category);
            }
        }

        /// <summary>
        ///     Finds an existing category that <paramref name="candidate" /> is suspiciously close to —
        ///     the same name but for casing, spacing, punctuation or a plural "s" — or null when the
        ///     name is genuinely new.
        /// </summary>
        /// <remarks>
        ///     This is how a category list stays a list instead of decaying into tag soup: "Tour" and
        ///     "Tours" are almost never two intentions, and the moment both exist the user's own
        ///     grouping stops meaning anything. The window warns; it never refuses.
        /// </remarks>
        internal static string FindNearDuplicate(IReadOnlyList<string> existing, string candidate)
        {
            string reduced = Reduce(candidate);
            if (reduced.Length == 0 || existing == null)
                return null;

            for (int i = 0; i < existing.Count; i++)
            {
                string other = existing[i];
                if (ConvaiActionCategory.AreSame(other, candidate))
                    return null; // Exactly the same category — that is a pick, not a near miss.

                if (string.Equals(Reduce(other), reduced, StringComparison.Ordinal))
                    return other;
            }

            return null;
        }

        /// <summary>
        ///     Reduces a name to what a human would consider "the same word": lower case, letters and
        ///     digits only, and without a trailing plural "s".
        /// </summary>
        private static string Reduce(string value)
        {
            string normalized = ConvaiActionCategory.Normalize(value);
            if (normalized.Length == 0)
                return string.Empty;

            var builder = new System.Text.StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }

            if (builder.Length > 1 && builder[builder.Length - 1] == 's')
                builder.Length--;

            return builder.ToString();
        }

        // ── Reordering ─────────────────────────────────────────────────────────

        /// <summary>
        ///     Rebuilds an authored list with <paramref name="moving" /> taken out and re-inserted at
        ///     <paramref name="anchor" /> (before it, or <paramref name="after" /> it). Returns a new
        ///     list; the input is untouched.
        /// </summary>
        /// <remarks>
        ///     The index arithmetic is the whole reason a drag can go wrong: the insertion point has to
        ///     be computed <em>after</em> the moved rows are removed, or dragging an action downwards
        ///     lands it one position short of where it was dropped. Kept here, as plain list logic, so
        ///     it can be proven without a window.
        /// </remarks>
        internal static List<ConvaiActionDefinition> MoveWithin(
            IReadOnlyList<ConvaiActionDefinition> ordered,
            IReadOnlyList<ConvaiActionDefinition> moving,
            ConvaiActionDefinition anchor,
            bool after)
        {
            var result = new List<ConvaiActionDefinition>(ordered?.Count ?? 0);
            for (int i = 0; ordered != null && i < ordered.Count; i++)
                result.Add(ordered[i]);

            if (moving == null || moving.Count == 0)
                return result;

            // Dropping rows onto one of themselves has no meaning — there is no position "before" or
            // "after" a row that is being carried. The list stays exactly as it was.
            for (int i = 0; anchor != null && i < moving.Count; i++)
            {
                if (ReferenceEquals(moving[i], anchor))
                    return result;
            }

            var carried = new List<ConvaiActionDefinition>(moving.Count);
            for (int i = 0; i < moving.Count; i++)
            {
                int index = IndexOf(result, moving[i]);
                if (index < 0)
                    continue;

                carried.Add(result[index]);
                result.RemoveAt(index);
            }

            if (carried.Count == 0)
                return result;

            // No anchor at all means "the end of the list" — which is what a drop on an empty
            // category's header asks for.
            int anchorIndex = IndexOf(result, anchor);
            int insertAt = anchorIndex < 0
                ? result.Count
                : after
                    ? anchorIndex + 1
                    : anchorIndex;

            result.InsertRange(Math.Clamp(insertAt, 0, result.Count), carried);
            return result;
        }

        /// <summary>
        ///     The last definition filed under <paramref name="category" /> in <paramref name="ordered" />,
        ///     or null when the category has none there — the anchor a "drop on the header" lands after,
        ///     which is what makes that drop mean "at the end of this group".
        /// </summary>
        internal static ConvaiActionDefinition FindLastInCategory(
            IReadOnlyList<ConvaiActionDefinition> ordered, string category)
        {
            ConvaiActionDefinition last = null;
            for (int i = 0; ordered != null && i < ordered.Count; i++)
            {
                if (ordered[i] != null && ConvaiActionCategory.AreSame(ordered[i].Category, category))
                    last = ordered[i];
            }

            return last;
        }

        private static int IndexOf(List<ConvaiActionDefinition> list, ConvaiActionDefinition definition)
        {
            if (definition == null)
                return -1;

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], definition))
                    return i;
            }

            return -1;
        }

        // ── Cold start ─────────────────────────────────────────────────────────

        /// <summary>One proposed category and the actions that would move into it.</summary>
        internal sealed class CategorySuggestion
        {
            internal CategorySuggestion(string category) => Category = category;

            /// <summary>Proposed category name — editable before anything is written.</summary>
            internal string Category;

            /// <summary>Rows that would be filed under it.</summary>
            internal readonly List<ConvaiActionRow> Rows = new();
        }

        /// <summary>
        ///     Proposes a starting set of categories for a character whose actions are all
        ///     uncategorized, by filing each action under its behavior family. Returns an empty list
        ///     when there is nothing worth proposing — fewer than two families, or nothing to file.
        /// </summary>
        /// <remarks>
        ///     The honest failure mode of an optional organization feature is an empty field nobody
        ///     fills in. This is the answer to that: one click to a structure that already reflects what
        ///     the character can do, which the user then renames into their own words. It is a proposal
        ///     — the caller shows it and writes nothing until the user accepts.
        /// </remarks>
        internal static List<CategorySuggestion> SuggestCategories(
            List<ConvaiActionGroup> sourceGroups,
            Func<ConvaiActionDefinition, string> resolveBehaviorFamily)
        {
            var suggestions = new List<CategorySuggestion>();
            if (sourceGroups == null || resolveBehaviorFamily == null)
                return suggestions;

            var byFamily = new Dictionary<string, CategorySuggestion>(StringComparer.OrdinalIgnoreCase);
            for (int g = 0; g < sourceGroups.Count; g++)
            {
                List<ConvaiActionRow> rows = sourceGroups[g]?.Rows;
                for (int r = 0; rows != null && r < rows.Count; r++)
                {
                    ConvaiActionRow row = rows[r];

                    // Never re-file what the user already filed themselves.
                    if (row.Definition == null || !ConvaiActionCategory.IsUncategorized(row.Definition.Category))
                        continue;

                    string family = ConvaiActionCategory.Normalize(resolveBehaviorFamily(row.Definition));
                    if (family.Length == 0)
                        continue;

                    if (!byFamily.TryGetValue(family, out CategorySuggestion suggestion))
                    {
                        suggestion = new CategorySuggestion(family);
                        byFamily.Add(family, suggestion);
                        suggestions.Add(suggestion);
                    }

                    suggestion.Rows.Add(row);
                }
            }

            // One bucket is not a grouping — it is the same flat list with a header on it.
            if (suggestions.Count < 2)
                suggestions.Clear();

            suggestions.Sort(static (left, right) =>
                string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase));
            return suggestions;
        }
    }
}
