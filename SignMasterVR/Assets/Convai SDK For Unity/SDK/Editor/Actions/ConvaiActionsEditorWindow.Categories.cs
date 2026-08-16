using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The window's grouping surface: the "Group by" control, the category commands behind the row
    ///     and header menus (file, rename, empty, select), and the cold-start suggestion. Every
    ///     decision here is taken by <see cref="ConvaiActionsGrouping" /> ; this partial only wires it
    ///     to IMGUI, Undo and the picked character.
    /// </summary>
    /// <remarks>
    ///     Categories are organization, not behavior: nothing in this file touches what is sent to
    ///     Convai. It writes exactly one field — <see cref="ConvaiActionDefinition.Category" /> — and
    ///     it writes it the same way every other edit in this window does, through
    ///     <c>Undo.RecordObject</c> on the row's true owner (the character, or the Action Set asset a
    ///     shared row lives in).
    /// </remarks>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string GroupAxisSessionKey = "Convai.ActionsEditor.GroupAxis";
        private const string GroupAxisChosenSessionKey = "Convai.ActionsEditor.GroupAxisChosen";
        private const string SuggestionDismissedPrefsKey = "Convai.ActionsEditor.SuggestionDismissed";

        /// <summary>
        ///     How many unfiled actions a character needs before the window offers to propose
        ///     categories. Below this a flat list is genuinely easier to read, and an unprompted
        ///     suggestion would be noise.
        /// </summary>
        private const int SuggestionThreshold = 8;

        private ConvaiActionsGroupAxis _groupAxis = ConvaiActionsGroupAxis.Source;

        /// <summary>
        ///     Whether the user picked the axis themselves this session. Once they have, the automatic
        ///     switch below never overrides them again.
        /// </summary>
        private bool _groupAxisChosen;

        /// <summary>Header content per computed group, so a draw pass allocates none of it.</summary>
        private readonly Dictionary<string, GUIContent> _groupTitleContents = new(StringComparer.Ordinal);

        /// <summary>The source the automatic axis choice has already been evaluated for.</summary>
        private ConvaiActionConfigSource _axisEvaluatedFor;

        /// <summary>
        ///     Whether the cold-start suggestion has been waved away. Cached rather than read from
        ///     preferences on the fly — this is consulted on every repaint of the list.
        /// </summary>
        private bool _suggestionDismissed;

        private void LoadGroupingState()
        {
            _groupAxis = (ConvaiActionsGroupAxis)SessionState.GetInt(
                GroupAxisSessionKey, (int)ConvaiActionsGroupAxis.Source);
            _groupAxisChosen = SessionState.GetBool(GroupAxisChosenSessionKey, false);
            _axisEvaluatedFor = null;
            _suggestionDismissed = EditorPrefs.GetBool(SuggestionDismissedPrefsKey, false);
            ConvaiActionsGroupCollapseState.Reload();
        }

        // ── Collapse state ─────────────────────────────────────────────────────

        /// <summary>
        ///     Whether this group is folded away. The state is shared with the Convai Action Config
        ///     Source inspector — see <see cref="ConvaiActionsGroupCollapseState" />.
        /// </summary>
        private static bool IsGroupCollapsed(string key) => ConvaiActionsGroupCollapseState.IsCollapsed(key);

        private void SetGroupCollapsed(string key, bool collapsed)
        {
            if (ConvaiActionsGroupCollapseState.SetCollapsed(key, collapsed))
                Repaint();
        }

        /// <summary>Opens a group the user is about to be shown something inside of.</summary>
        private void ExpandGroup(string key) => SetGroupCollapsed(key, false);

        // ── Axis ───────────────────────────────────────────────────────────────

        private static GUIContent GroupChoiceContent(ConvaiActionsGroupAxis axis) => axis switch
        {
            ConvaiActionsGroupAxis.Category => ConvaiActionsEditorStrings.GroupChoiceCategory,
            ConvaiActionsGroupAxis.Status => ConvaiActionsEditorStrings.GroupChoiceStatus,
            ConvaiActionsGroupAxis.Behavior => ConvaiActionsEditorStrings.GroupChoiceBehavior,
            _ => ConvaiActionsEditorStrings.GroupChoiceSource
        };

        private void ShowGroupByMenu()
        {
            var menu = new GenericMenu();
            AddGroupByMenuItem(menu, ConvaiActionsEditorStrings.GroupMenuSource, ConvaiActionsGroupAxis.Source);
            AddGroupByMenuItem(menu, ConvaiActionsEditorStrings.GroupMenuCategory, ConvaiActionsGroupAxis.Category);
            menu.AddSeparator(string.Empty);
            AddGroupByMenuItem(menu, ConvaiActionsEditorStrings.GroupMenuStatus, ConvaiActionsGroupAxis.Status);
            AddGroupByMenuItem(menu, ConvaiActionsEditorStrings.GroupMenuBehavior, ConvaiActionsGroupAxis.Behavior);
            menu.ShowAsContext();
        }

        private void AddGroupByMenuItem(GenericMenu menu, GUIContent label, ConvaiActionsGroupAxis axis) =>
            menu.AddItem(new GUIContent(label.text), _groupAxis == axis, () => SetGroupAxis(axis, true));

        private void SetGroupAxis(ConvaiActionsGroupAxis axis, bool chosenByUser)
        {
            _groupAxis = axis;
            SessionState.SetInt(GroupAxisSessionKey, (int)axis);

            if (chosenByUser)
            {
                _groupAxisChosen = true;
                SessionState.SetBool(GroupAxisChosenSessionKey, true);
            }

            Repaint();
        }

        /// <summary>
        ///     Moves an untouched window onto the Category axis the first time the picked character has
        ///     a category to show — and never afterwards, and never over a choice the user made
        ///     themselves.
        /// </summary>
        /// <remarks>
        ///     The alternative, defaulting everyone to Category, rearranges the list of every project
        ///     that upgrades the SDK without asking for anything. A project that never files an action
        ///     never sees this feature at all.
        /// </remarks>
        private void ApplyAutomaticGroupAxis(ConvaiActionConfigSource source)
        {
            if (ReferenceEquals(_axisEvaluatedFor, source))
                return;

            _axisEvaluatedFor = source;
            if (_groupAxisChosen || _groupAxis != ConvaiActionsGroupAxis.Source)
                return;

            if (ConvaiActionsGrouping.HasAnyCategory(source))
                SetGroupAxis(ConvaiActionsGroupAxis.Category, false);
        }

        /// <summary>Re-evaluates the automatic axis after an edit that may have created the first category.</summary>
        private void InvalidateAutomaticGroupAxis() => _axisEvaluatedFor = null;

        /// <summary>
        ///     The groups the left pane draws: the source groups regrouped along the chosen axis. Row
        ///     status, filtering and authored indices are all computed once, upstream — this only
        ///     rearranges the result.
        /// </summary>
        private List<ConvaiActionGroup> BuildDisplayGroups(List<ConvaiActionGroup> sourceGroups) =>
            ConvaiActionsGrouping.Regroup(sourceGroups, _groupAxis, ConvaiActionBehaviorFamily.Resolve);

        // ── Filing commands ────────────────────────────────────────────────────

        /// <summary>
        ///     Adds the "File Under Category" submenu for <paramref name="rows" /> — every category
        ///     already in use, a way to make a new one, and a way out of the current one. Shared by the
        ///     row context menu and the multi-selection pane, so one action and twenty behave the same.
        /// </summary>
        private void AddCategoryMenuItems(
            GenericMenu menu, ConvaiActionConfigSource source, List<ConvaiActionRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            string root = ConvaiActionsEditorStrings.RowMenuCategoryRoot.text + "/";
            string current = SharedCategoryOf(rows);

            List<string> categories = ConvaiActionsGrouping.CollectCategoryNames(source);
            for (int i = 0; i < categories.Count; i++)
            {
                string category = categories[i];
                menu.AddItem(
                    new GUIContent(root + category),
                    ConvaiActionCategory.AreSame(current, category),
                    () => AssignCategory(source, rows, category));
            }

            if (categories.Count > 0)
                menu.AddSeparator(root);

            menu.AddItem(
                new GUIContent(root + ConvaiActionsEditorStrings.RowMenuNewCategory.text),
                false,
                () => PromptForNewCategory(source, rows));

            menu.AddItem(
                new GUIContent(root + ConvaiActionsEditorStrings.RowMenuNoCategory.text),
                current != null && current.Length == 0,
                () => AssignCategory(source, rows, string.Empty));
        }

        /// <summary>
        ///     Opens the filing submenu for the whole current selection, so twenty actions are filed
        ///     with exactly the command that files one.
        /// </summary>
        private void ShowCategoryMenuForSelection(ConvaiActionConfigSource source)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            if (rows.Count == 0)
                return;

            var menu = new GenericMenu();
            AddCategoryMenuItems(menu, source, rows);
            menu.ShowAsContext();
        }

        /// <summary>
        ///     The category all of <paramref name="rows" /> share, or null when they disagree — what
        ///     decides whether the menu can show a tick next to the current category.
        /// </summary>
        private static string SharedCategoryOf(List<ConvaiActionRow> rows)
        {
            string shared = null;
            for (int i = 0; i < rows.Count; i++)
            {
                string category = ConvaiActionCategory.Normalize(rows[i].Definition?.Category);
                if (i == 0)
                {
                    shared = category;
                    continue;
                }

                if (!ConvaiActionCategory.AreSame(shared, category))
                    return null;
            }

            return shared;
        }

        /// <summary>
        ///     Files every row under <paramref name="category" /> (empty unfiles them) as one undoable
        ///     step, writing to whichever object actually owns each row.
        /// </summary>
        private void AssignCategory(
            ConvaiActionConfigSource source, List<ConvaiActionRow> rows, string category)
        {
            if (source == null || rows == null || rows.Count == 0)
                return;

            string normalized = ConvaiActionCategory.Normalize(category);
            string label = normalized.Length == 0 ? "Remove Actions From Category" : $"File Actions Under '{normalized}'";

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);

            bool changedAnything = false;
            for (int i = 0; i < rows.Count; i++)
            {
                ConvaiActionDefinition definition = rows[i].Definition;
                if (definition == null || ConvaiActionCategory.AreSame(definition.Category, normalized))
                    continue;

                UnityEngine.Object owner = OwnerOf(source, rows[i]);
                Undo.RecordObject(owner, label);
                definition.Category = normalized;
                MarkDirty(owner);
                changedAnything = true;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (!changedAnything)
                return;

            // Filing the first action is what turns the Category axis on for an untouched window, and
            // it must be visible immediately — not after the next character switch.
            InvalidateAutomaticGroupAxis();
            ApplyAutomaticGroupAxis(source);
            ExpandGroup(ConvaiActionsGrouping.BuildCategoryGroupKey(normalized));
            Repaint();
        }

        /// <summary>
        ///     Opens the naming prompt for a brand new category and files <paramref name="rows" /> under
        ///     whatever the user settles on.
        /// </summary>
        private void PromptForNewCategory(ConvaiActionConfigSource source, List<ConvaiActionRow> rows)
        {
            List<string> existing = ConvaiActionsGrouping.CollectCategoryNames(source);
            int sharedCount = CountShared(rows);

            // Deferred: this runs from a context-menu callback, and opening a window from inside the
            // window's own draw pass corrupts the layout for the rest of the pass.
            EditorApplication.delayCall += () => ConvaiCategoryPromptWindow.ShowNew(
                existing,
                sharedCount,
                chosen => AssignCategory(source, rows, chosen));
        }

        private static int CountShared(List<ConvaiActionRow> rows)
        {
            int shared = 0;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (rows[i].IsShared)
                    shared++;
            }

            return shared;
        }

        // ── Category-wide commands ─────────────────────────────────────────────

        private void ShowCategoryHeaderMenu(ConvaiActionConfigSource source, ConvaiActionGroup group)
        {
            var menu = new GenericMenu();

            if (group.CategoryName.Length > 0)
            {
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.CategoryHeaderMenuRename.text), false,
                    () => PromptForCategoryRename(source, group.CategoryName));
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.CategoryHeaderMenuRemove.text), false,
                    () => RemoveCategory(source, group.CategoryName));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.CategoryHeaderMenuSelectAll.text), false,
                () => SelectRows(group.Rows));

            menu.ShowAsContext();
        }

        private void PromptForCategoryRename(ConvaiActionConfigSource source, string category)
        {
            List<ConvaiActionRow> rows = CollectRowsInCategory(source, category);
            List<string> existing = ConvaiActionsGrouping.CollectCategoryNames(source);

            EditorApplication.delayCall += () => ConvaiCategoryPromptWindow.ShowRename(
                category,
                existing,
                ConvaiActionsEditorStrings.BuildCategoryRenameSummary(category, rows.Count).text,
                CountShared(rows),
                chosen =>
                {
                    if (!string.IsNullOrEmpty(chosen))
                        AssignCategory(source, rows, chosen);
                });
        }

        /// <summary>
        ///     Empties a category. Never deletes an action: a category is a label, and removing a label
        ///     removes a label.
        /// </summary>
        private void RemoveCategory(ConvaiActionConfigSource source, string category)
        {
            List<ConvaiActionRow> rows = CollectRowsInCategory(source, category);
            if (rows.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    ConvaiActionsEditorStrings.CategoryHeaderMenuRemove.text,
                    ConvaiActionsEditorStrings.BuildCategoryRemoveSummary(category, rows.Count).text,
                    ConvaiActionsEditorStrings.CategoryHeaderMenuRemove.text,
                    ConvaiActionsEditorStrings.CategoryPromptCancelButton.text))
                return;

            AssignCategory(source, rows, string.Empty);
        }

        /// <summary>
        ///     Every row currently filed under <paramref name="category" />, taken from the list the
        ///     window last drew so display order and authored indices stay the ones the user is
        ///     looking at.
        /// </summary>
        private List<ConvaiActionRow> CollectRowsInCategory(ConvaiActionConfigSource source, string category)
        {
            var rows = new List<ConvaiActionRow>();
            if (_lastGroups == null)
                return rows;

            for (int g = 0; g < _lastGroups.Count; g++)
            {
                List<ConvaiActionRow> groupRows = _lastGroups[g].Rows;
                for (int r = 0; r < groupRows.Count; r++)
                {
                    if (ConvaiActionCategory.AreSame(groupRows[r].Definition?.Category, category))
                        rows.Add(groupRows[r]);
                }
            }

            return rows;
        }

        /// <summary>Replaces the current selection with these rows, so a category can be worked on as one.</summary>
        private void SelectRows(List<ConvaiActionRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            _multiSelection.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Definition != null)
                    _multiSelection.Add(rows[i].Definition);
            }

            _selectedDefinition = _multiSelection.Count > 0 ? _multiSelection[0] : null;
            SaveMultiSelectionState();
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        // ── Cold start ─────────────────────────────────────────────────────────

        /// <summary>
        ///     Whether to offer the category suggestion above the list: enough actions for grouping to
        ///     pay off, none of them filed yet, and the user has not waved it away.
        /// </summary>
        private bool ShouldOfferCategorySuggestion(ConvaiActionConfigSource source, List<ConvaiActionGroup> groups)
        {
            if (source == null || groups == null || HasActiveListNarrowing)
                return false;

            if (_suggestionDismissed)
                return false;

            if (ConvaiActionsGrouping.HasAnyCategory(source))
                return false;

            int rowCount = 0;
            for (int g = 0; g < groups.Count; g++)
                rowCount += groups[g].Rows.Count;

            return rowCount >= SuggestionThreshold;
        }

        /// <summary>
        ///     The one-time invitation to organize a long, unfiled list. Drawn above the groups so it
        ///     reads as an offer about the list rather than an item in it, and it never writes anything
        ///     itself — the proposal window does, and only once the user accepts it.
        /// </summary>
        private void DrawCategorySuggestionBanner(ConvaiActionConfigSource source, List<ConvaiActionGroup> groups)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                using (new EditorGUILayout.VerticalScope())
                {
                    Theme.BeginPanel(Theme.Accent, 2f);
                    GUILayout.Label(ConvaiActionsEditorStrings.SuggestCategoriesExplainer, Theme.MutedWrapped);
                    GUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect suggestRect = GUILayoutUtility.GetRect(140f, 22f, GUILayout.Width(140f), GUILayout.Height(22f));
                        if (Theme.PrimaryButton(suggestRect, ConvaiActionsEditorStrings.SuggestCategoriesButton))
                            OfferCategorySuggestions(source, groups);

                        GUILayout.Space(6f);
                        Rect dismissRect = GUILayoutUtility.GetRect(86f, 22f, GUILayout.Width(86f), GUILayout.Height(22f));
                        if (Theme.GhostButton(dismissRect, ConvaiActionsEditorStrings.SuggestCategoriesDismissButton))
                            DismissCategorySuggestion();

                        GUILayout.FlexibleSpace();
                    }

                    Theme.EndPanel();
                }

                GUILayout.Space(12f);
            }
        }

        private void DismissCategorySuggestion()
        {
            _suggestionDismissed = true;
            EditorPrefs.SetBool(SuggestionDismissedPrefsKey, true);
            Repaint();
        }

        private void OfferCategorySuggestions(ConvaiActionConfigSource source, List<ConvaiActionGroup> groups)
        {
            List<ConvaiActionsGrouping.CategorySuggestion> suggestions =
                ConvaiActionsGrouping.SuggestCategories(groups, ConvaiActionBehaviorFamily.Resolve);

            if (suggestions.Count == 0)
            {
                // Nothing worth proposing — retire the offer rather than opening an empty window.
                DismissCategorySuggestion();
                return;
            }

            EditorApplication.delayCall += () => ConvaiCategorySuggestionWindow.Show(
                suggestions, accepted => ApplyCategorySuggestions(source, accepted));
        }

        /// <summary>Files every accepted proposal, all of it as one undo step.</summary>
        private void ApplyCategorySuggestions(
            ConvaiActionConfigSource source, List<ConvaiActionsGrouping.CategorySuggestion> accepted)
        {
            if (source == null || accepted == null || accepted.Count == 0)
                return;

            const string label = "File Actions Under Categories";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);

            for (int i = 0; i < accepted.Count; i++)
            {
                ConvaiActionsGrouping.CategorySuggestion suggestion = accepted[i];
                string category = ConvaiActionCategory.Normalize(suggestion.Category);
                if (category.Length == 0)
                    continue;

                for (int r = 0; r < suggestion.Rows.Count; r++)
                {
                    ConvaiActionDefinition definition = suggestion.Rows[r].Definition;
                    if (definition == null || ConvaiActionCategory.AreSame(definition.Category, category))
                        continue;

                    UnityEngine.Object owner = OwnerOf(source, suggestion.Rows[r]);
                    Undo.RecordObject(owner, label);
                    definition.Category = category;
                    MarkDirty(owner);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            InvalidateAutomaticGroupAxis();
            ApplyAutomaticGroupAxis(source);
            Repaint();
        }
    }
}
