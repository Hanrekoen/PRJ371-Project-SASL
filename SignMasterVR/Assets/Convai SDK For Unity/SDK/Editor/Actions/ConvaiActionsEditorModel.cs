using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;

namespace Convai.Editor.Actions
{
    /// <summary>Traffic-light health of one action row in <see cref="ConvaiActionsEditorWindow" />'s left pane.</summary>
    internal enum ConvaiActionRowStatus
    {
        /// <summary>Executable and no validator warning/error refers to it.</summary>
        Ready = 0,

        /// <summary>Bindable-but-unbound (a resolvable executor type hint with no component yet), or a validator warning refers to it.</summary>
        NeedsAttention = 1,

        /// <summary>A validator error refers to it, or it is neither executable nor resolvably bindable.</summary>
        Broken = 2
    }

    /// <summary>One row in the Actions Editor window's left-pane action list.</summary>
    internal readonly struct ConvaiActionRow
    {
        internal ConvaiActionRow(
            ConvaiActionDefinition definition,
            ConvaiActionRowStatus status,
            bool isShared,
            int ownerIndex,
            ConvaiActionSet owningSet,
            string displayName)
        {
            Definition = definition;
            Status = status;
            IsShared = isShared;
            OwnerIndex = ownerIndex;
            OwningSet = owningSet;
            DisplayName = displayName;
        }

        /// <summary>The underlying definition (inline instance, or a set's asset-owned instance).</summary>
        internal ConvaiActionDefinition Definition { get; }

        /// <summary>Computed traffic-light health for this row.</summary>
        internal ConvaiActionRowStatus Status { get; }

        /// <summary>
        ///     True when this row's definition is owned by an assigned <see cref="ConvaiActionSet" />
        ///     asset rather than authored inline on the character — i.e. editing it affects every
        ///     character using that set. Shared rows are still fully editable (the window writes
        ///     through to the owning asset); this flag drives the "shared" affordances and the
        ///     type-hint-based Scene Behavior card, not a read-only lock.
        /// </summary>
        internal bool IsShared { get; }

        /// <summary>
        ///     Index of this definition inside its owner's authored list — the character's inline
        ///     <c>Definitions</c> for an inline row, or the owning set's <c>Definitions</c> for a
        ///     shared row. Always the true authored index, never the post-filter display position, so
        ///     reorder/remove stay correct while a search filter is active.
        /// </summary>
        internal int OwnerIndex { get; }

        /// <summary>The owning action set for a shared row; null for an inline row.</summary>
        internal ConvaiActionSet OwningSet { get; }

        /// <summary>Display name for the card ("(unnamed action)" fallback for a blank <c>ActionName</c>).</summary>
        internal string DisplayName { get; }
    }

    /// <summary>What a left-pane group is grouping by — which is also what its header can offer.</summary>
    internal enum ConvaiActionGroupKind
    {
        /// <summary>The character's own inline definitions.</summary>
        ThisCharacter = 0,

        /// <summary>One assigned <see cref="ConvaiActionSet" /> asset.</summary>
        ActionSet = 1,

        /// <summary>One authoring category the user filed actions under (empty name = uncategorized).</summary>
        Category = 2,

        /// <summary>One traffic-light status bucket.</summary>
        Status = 3,

        /// <summary>One behavior family (the module that powers the action).</summary>
        Behavior = 4
    }

    /// <summary>
    ///     One left-pane group. Along the default Source axis that is "This Character" (inline,
    ///     editable) or one assigned Action Set; along the other axes it is one category, one status
    ///     bucket, or one behavior family — see <see cref="ConvaiActionsGrouping" />.
    /// </summary>
    internal sealed class ConvaiActionGroup
    {
        /// <summary>Group header text ("This Character", the set's name, the category name, …).</summary>
        internal string Title;

        /// <summary>The backing action set asset; null for every group that is not one.</summary>
        internal ConvaiActionSet Set;

        /// <summary>Rows in this group, in authored order.</summary>
        internal List<ConvaiActionRow> Rows = new();

        /// <summary>What this group groups by.</summary>
        internal ConvaiActionGroupKind Kind = ConvaiActionGroupKind.ThisCharacter;

        /// <summary>
        ///     Stable identity for this group across draw passes and editor sessions — what collapse
        ///     state is remembered against. Deliberately not the title: a category renamed from "Tour"
        ///     to "Tours" is the same group and must not silently spring open.
        /// </summary>
        internal string Key = string.Empty;

        /// <summary>
        ///     The authoring category this group collects when <see cref="Kind" /> is
        ///     <see cref="ConvaiActionGroupKind.Category" />; <see cref="string.Empty" /> is the
        ///     uncategorized bucket. Empty for every other kind.
        /// </summary>
        internal string CategoryName = string.Empty;

        /// <summary>Worst row status in the group — what a collapsed header reports.</summary>
        internal ConvaiActionRowStatus WorstStatus = ConvaiActionRowStatus.Ready;

        /// <summary>How many rows in the group are not <see cref="ConvaiActionRowStatus.Ready" />.</summary>
        internal int UnhealthyCount;

        /// <summary>
        ///     Recomputes the health rollup from <see cref="Rows" />. Called once per build rather than
        ///     per repaint: a collapsed header still reports what is wrong inside it, and paying for
        ///     that on every mouse move over the window would not be free.
        /// </summary>
        internal void RefreshRollup()
        {
            WorstStatus = ConvaiActionRowStatus.Ready;
            UnhealthyCount = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                ConvaiActionRowStatus status = Rows[i].Status;
                if (status == ConvaiActionRowStatus.Ready)
                    continue;

                UnhealthyCount++;
                if (status > WorstStatus)
                    WorstStatus = status;
            }
        }
    }

    /// <summary>
    ///     Pure, scene-free logic backing <see cref="ConvaiActionsEditorWindow" />: building the
    ///     grouped/filtered left-pane row list, computing each row's traffic-light status from
    ///     <see cref="ConvaiActionConfigValidator" /> diagnostics, and summarizing diagnostics for the
    ///     toolbar troubleshooter chip and the bottom status strip. No <c>UnityEditor</c>/GUI dependency, so
    ///     it is directly unit-testable.
    /// </summary>
    internal static class ConvaiActionsEditorModel
    {
        /// <summary>Group key of the character's own inline definitions.</summary>
        internal const string ThisCharacterGroupKey = "source:this-character";

        /// <summary>
        ///     Group key for one assigned Action Set. Keyed by the asset's own name rather than by its
        ///     position, so its collapse state survives the character being given another set.
        /// </summary>
        internal static string BuildActionSetGroupKey(ConvaiActionSet set) =>
            "source:set:" + (set == null ? string.Empty : set.name ?? string.Empty);

        /// <summary>Display name for an action set asset, matching <c>ConvaiActionConfigValidator</c>'s convention.</summary>
        internal static string GetActionSetDisplayName(ConvaiActionSet set, int index)
        {
            if (set == null || string.IsNullOrEmpty(set.name))
                return $"Action Set #{index + 1}";

            return set.name;
        }

        /// <summary>
        ///     Whether a definition matches a free-text search filter against its name, description or
        ///     category (case-insensitive, substring).
        /// </summary>
        /// <remarks>
        ///     The category counts as searchable text on purpose: once a user files actions under
        ///     "Tour", typing "tour" has to find that group, or the label they maintain is worth less
        ///     than the words they happened to type into an action's name.
        /// </remarks>
        internal static bool MatchesFilter(ConvaiActionDefinition definition, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            if (definition == null)
                return false;

            string needle = filter.Trim();
            if (!string.IsNullOrEmpty(definition.ActionName) &&
                definition.ActionName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(definition.Description) &&
                definition.Description.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return definition.Category.Length > 0 &&
                   definition.Category.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        ///     Computes a row's traffic-light status: <see cref="ConvaiActionRowStatus.Broken" /> when
        ///     any diagnostic matching <paramref name="contextKey" />/the action's name is an Error;
        ///     otherwise <see cref="ConvaiActionRowStatus.Ready" /> for an executable definition with
        ///     no matching Warning, <see cref="ConvaiActionRowStatus.NeedsAttention" /> for an
        ///     executable definition with a matching Warning or an unbound-but-resolvable executor
        ///     type hint, and <see cref="ConvaiActionRowStatus.Broken" /> otherwise.
        /// </summary>
        internal static ConvaiActionRowStatus ComputeStatus(
            ConvaiActionDefinition definition,
            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics,
            string contextKey)
        {
            if (definition == null)
                return ConvaiActionRowStatus.Broken;

            bool hasError = false;
            bool hasWarning = false;
            if (diagnostics != null)
            {
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    ConvaiActionConfigDiagnostic diagnostic = diagnostics[i];
                    if (!DiagnosticMatches(diagnostic, contextKey, definition.ActionName))
                        continue;

                    if (diagnostic.Severity == ConvaiActionConfigDiagnosticSeverity.Error)
                        hasError = true;
                    else if (diagnostic.Severity == ConvaiActionConfigDiagnosticSeverity.Warning)
                        hasWarning = true;
                }
            }

            if (hasError)
                return ConvaiActionRowStatus.Broken;

            if (ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                return hasWarning ? ConvaiActionRowStatus.NeedsAttention : ConvaiActionRowStatus.Ready;

            string hint = definition.ExecutorTypeHint;
            if (!string.IsNullOrWhiteSpace(hint) && ConvaiActionExecutorBinder.TryResolveType(hint, out _))
                return ConvaiActionRowStatus.NeedsAttention;

            return ConvaiActionRowStatus.Broken;
        }

        /// <summary>
        ///     Substring-based diagnostic-to-row match: an exact <see cref="ConvaiActionConfigDiagnostic.Context" />
        ///     match against the row's positional context key (for example <c>"Action definition #2"</c>
        ///     or <c>"Action set 'Foo' definition #1"</c>), or a quoted-action-name mention in either
        ///     the message or the context (covers per-parameter and target-availability diagnostics,
        ///     which carry no positional context). Mirrors the approach the pre-existing inspector used
        ///     for its own set-collision badge.
        /// </summary>
        private static bool DiagnosticMatches(ConvaiActionConfigDiagnostic diagnostic, string contextKey, string actionName)
        {
            if (diagnostic == null)
                return false;

            if (!string.IsNullOrEmpty(contextKey) &&
                string.Equals(diagnostic.Context, contextKey, StringComparison.Ordinal))
                return true;

            if (string.IsNullOrEmpty(actionName))
                return false;

            string quoted = "'" + actionName + "'";
            if (diagnostic.Message != null && diagnostic.Message.IndexOf(quoted, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return diagnostic.Context != null && diagnostic.Context.IndexOf(quoted, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        ///     Builds the left-pane groups: "This Character" (inline definitions, in list order, always
        ///     first) then one group per assigned, non-null <see cref="ConvaiActionConfigSource.ActionSets" />
        ///     entry, in list order. Rows within each group respect <paramref name="searchFilter" />
        ///     and the scope <paramref name="listFilter" /> (a row must pass both).
        /// </summary>
        internal static List<ConvaiActionGroup> BuildGroups(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics,
            string searchFilter,
            ConvaiActionsListFilter listFilter = ConvaiActionsListFilter.All)
        {
            var groups = new List<ConvaiActionGroup>();
            if (source == null)
                return groups;

            var thisCharacter = new ConvaiActionGroup
            {
                Title = "This Character",
                Set = null,
                Kind = ConvaiActionGroupKind.ThisCharacter,
                Key = ThisCharacterGroupKey
            };
            IReadOnlyList<ConvaiActionDefinition> inline = source.Definitions;
            if (inline != null)
            {
                for (int i = 0; i < inline.Count; i++)
                {
                    ConvaiActionDefinition definition = inline[i];
                    if (!MatchesFilter(definition, searchFilter))
                        continue;

                    string contextKey = $"Action definition #{i + 1}";
                    ConvaiActionRowStatus status = ComputeStatus(definition, diagnostics, contextKey);
                    if (!ConvaiActionsProductivityModel.MatchesListFilter(
                            listFilter, status, false, definition == null || definition.Enabled))
                        continue;

                    string displayName = string.IsNullOrWhiteSpace(definition?.ActionName) ? "(unnamed action)" : definition.ActionName;
                    thisCharacter.Rows.Add(new ConvaiActionRow(definition, status, false, i, null, displayName));
                }
            }

            thisCharacter.RefreshRollup();
            groups.Add(thisCharacter);

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            if (sets != null)
            {
                for (int s = 0; s < sets.Count; s++)
                {
                    ConvaiActionSet set = sets[s];
                    if (set == null)
                        continue;

                    string setName = GetActionSetDisplayName(set, s);
                    var group = new ConvaiActionGroup
                    {
                        Title = setName,
                        Set = set,
                        Kind = ConvaiActionGroupKind.ActionSet,
                        Key = BuildActionSetGroupKey(set)
                    };

                    IReadOnlyList<ConvaiActionDefinition> setDefinitions = set.Definitions;
                    for (int d = 0; d < setDefinitions.Count; d++)
                    {
                        ConvaiActionDefinition definition = setDefinitions[d];
                        if (!MatchesFilter(definition, searchFilter))
                            continue;

                        string contextKey = $"Action set '{setName}' definition #{d + 1}";
                        ConvaiActionRowStatus status = ComputeStatus(definition, diagnostics, contextKey);
                        if (!ConvaiActionsProductivityModel.MatchesListFilter(
                                listFilter, status, true, definition == null || definition.Enabled))
                            continue;

                        string displayName = string.IsNullOrWhiteSpace(definition?.ActionName) ? "(unnamed action)" : definition.ActionName;
                        group.Rows.Add(new ConvaiActionRow(definition, status, true, d, set, displayName));
                    }

                    group.RefreshRollup();
                    groups.Add(group);
                }
            }

            return groups;
        }

        /// <summary>
        ///     Rebuilds a row's validator context key (the same positional convention
        ///     <see cref="BuildGroups" /> uses), so panels outside the group builder — e.g. the
        ///     Try It card's findings list — can match diagnostics to one selected row.
        /// </summary>
        internal static string BuildRowContextKey(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            if (row.OwningSet == null)
                return $"Action definition #{row.OwnerIndex + 1}";

            int setIndex = 0;
            IReadOnlyList<ConvaiActionSet> sets = source?.ActionSets;
            if (sets != null)
            {
                for (int i = 0; i < sets.Count; i++)
                {
                    if (ReferenceEquals(sets[i], row.OwningSet))
                    {
                        setIndex = i;
                        break;
                    }
                }
            }

            string setName = GetActionSetDisplayName(row.OwningSet, setIndex);
            return $"Action set '{setName}' definition #{row.OwnerIndex + 1}";
        }

        /// <summary>
        ///     Whether one diagnostic refers to <paramref name="row" /> — same matching rules row
        ///     statuses are computed with (positional context key, or a quoted-name mention).
        /// </summary>
        internal static bool DiagnosticMatchesRow(
            ConvaiActionConfigSource source,
            ConvaiActionRow row,
            ConvaiActionConfigDiagnostic diagnostic) =>
            DiagnosticMatches(diagnostic, BuildRowContextKey(source, row), row.Definition?.ActionName);

        /// <summary>Counts errors and warnings across a diagnostic list (never null in the result).</summary>
        internal static (int errorCount, int warningCount) Summarize(IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics)
        {
            int errors = 0;
            int warnings = 0;
            if (diagnostics != null)
            {
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    switch (diagnostics[i]?.Severity)
                    {
                        case ConvaiActionConfigDiagnosticSeverity.Error:
                            errors++;
                            break;
                        case ConvaiActionConfigDiagnosticSeverity.Warning:
                            warnings++;
                            break;
                    }
                }
            }

            return (errors, warnings);
        }

        /// <summary>
        ///     Whether the character has anything authored worth showing the action list for: at least
        ///     one inline definition, or at least one assigned <see cref="ConvaiActionSet" />.
        /// </summary>
        /// <remarks>
        ///     An assigned set counts even when it currently holds zero definitions. This is what makes
        ///     "create a new (empty) Action Set and assign it" a usable flow: the freshly created set
        ///     stays visible as its own group so the user can author into it, instead of the window
        ///     snapping back to the "Add your first action" hero and hiding the set they just made.
        ///     A null entry in the set list never counts — it renders nothing, so on its own it must
        ///     not suppress the empty-state hero.
        /// </remarks>
        internal static bool HasAuthoredContent(ConvaiActionConfigSource source)
        {
            if (source == null)
                return false;

            if (source.Definitions != null && source.Definitions.Count > 0)
                return true;

            return CountAssignedSets(source) > 0;
        }

        /// <summary>Counts assigned, non-null <see cref="ConvaiActionSet" /> entries (null slots are ignored).</summary>
        internal static int CountAssignedSets(ConvaiActionConfigSource source)
        {
            IReadOnlyList<ConvaiActionSet> sets = source?.ActionSets;
            if (sets == null)
                return 0;

            int count = 0;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null)
                    count++;
            }

            return count;
        }

        /// <summary>
        ///     Whether <paramref name="set" /> is already assigned to <paramref name="source" />.
        ///     Hand-rolled rather than LINQ/<c>List.Contains</c> because <c>ActionSets</c> is exposed as
        ///     an <see cref="IReadOnlyList{T}" />, which carries no <c>Contains</c> of its own.
        /// </summary>
        internal static bool IsSetAssigned(ConvaiActionConfigSource source, ConvaiActionSet set)
        {
            IReadOnlyList<ConvaiActionSet> sets = source?.ActionSets;
            if (sets == null || set == null)
                return false;

            for (int i = 0; i < sets.Count; i++)
            {
                if (ReferenceEquals(sets[i], set))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Counts how many <see cref="ConvaiActionConfigSource" /> components in
        ///     <paramref name="allSources" /> have <paramref name="set" /> assigned. Backs the right
        ///     pane's "shared — also used by N other characters" banner, which is what keeps in-place
        ///     editing of a shared asset honest rather than surprising.
        /// </summary>
        internal static int CountCharactersUsingSet(
            IReadOnlyList<ConvaiActionConfigSource> allSources,
            ConvaiActionSet set)
        {
            if (allSources == null || set == null)
                return 0;

            int count = 0;
            for (int i = 0; i < allSources.Count; i++)
            {
                if (IsSetAssigned(allSources[i], set))
                    count++;
            }

            return count;
        }
    }
}
