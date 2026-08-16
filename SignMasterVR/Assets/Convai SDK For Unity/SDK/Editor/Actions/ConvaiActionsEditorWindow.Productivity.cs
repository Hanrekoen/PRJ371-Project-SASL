using System;
using System.Collections.Generic;
using System.IO;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Logging;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Productivity pack of the Actions Editor window : row operations
    ///     (duplicate/copy/paste/delete/make-local-copy via right-click and the detail pane's
    ///     overflow menu), Ctrl/Shift multi-select with bulk operations, "Extract to Action Set",
    ///     JSON import/export, and the scope filter next to the search field. All pure decisions
    ///     live in <see cref="ConvaiActionsProductivityModel" /> and
    ///     <see cref="ConvaiActionsTransferModel" />; this partial only wires them to IMGUI, Undo,
    ///     and the file panels.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string ListFilterSessionKey = "Convai.ActionsEditor.ListFilter";
        private const string MultiSelectionSessionKey = "Convai.ActionsEditor.MultiSelection";
        private const char MultiSelectionKeySeparator = '\n';

        private ConvaiActionsListFilter _listFilter;

        // Ordered by click sequence; authoritative order for bulk operations comes from the
        // visible row order in _lastGroups, not from this list.
        private readonly List<ConvaiActionDefinition> _multiSelection = new();
        private bool _multiSelectionRestorePending;
        private List<ConvaiActionGroup> _lastGroups;

        // Cached so the multi-selection pane allocates nothing per repaint.
        private GUIContent _multiTitleContent;
        private int _multiTitleCount = -1;

        private static readonly string[] s_emptyParameterTexts = Array.Empty<string>();

        /// <summary>Two or more actions selected — the detail pane shows the bulk card instead.</summary>
        private bool HasMultiSelection => _multiSelection.Count > 1;

        /// <summary>Search text and/or scope filter currently narrowing the action list.</summary>
        private bool HasActiveListNarrowing =>
            !string.IsNullOrWhiteSpace(_searchFilter) || _listFilter != ConvaiActionsListFilter.All;

        private void LoadProductivitySessionState()
        {
            _listFilter = (ConvaiActionsListFilter)SessionState.GetInt(
                ListFilterSessionKey, (int)ConvaiActionsListFilter.All);
            _multiSelectionRestorePending = true;
        }

        #region Scope filter

        private static GUIContent FilterChoiceContent(ConvaiActionsListFilter filter) => filter switch
        {
            ConvaiActionsListFilter.NeedsAttention => ConvaiActionsEditorStrings.FilterChoiceNeedsAttention,
            ConvaiActionsListFilter.NotOffered => ConvaiActionsEditorStrings.FilterChoiceNotOffered,
            ConvaiActionsListFilter.ThisCharacter => ConvaiActionsEditorStrings.FilterChoiceThisCharacter,
            ConvaiActionsListFilter.FromActionSets => ConvaiActionsEditorStrings.FilterChoiceFromSets,
            _ => ConvaiActionsEditorStrings.FilterChoiceAll
        };

        private void ShowListFilterMenu()
        {
            var menu = new GenericMenu();
            AddFilterMenuItem(menu, ConvaiActionsEditorStrings.FilterMenuAll, ConvaiActionsListFilter.All);
            AddFilterMenuItem(menu, ConvaiActionsEditorStrings.FilterMenuNeedsAttention, ConvaiActionsListFilter.NeedsAttention);
            AddFilterMenuItem(menu, ConvaiActionsEditorStrings.FilterMenuNotOffered, ConvaiActionsListFilter.NotOffered);
            AddFilterMenuItem(menu, ConvaiActionsEditorStrings.FilterMenuThisCharacter, ConvaiActionsListFilter.ThisCharacter);
            AddFilterMenuItem(menu, ConvaiActionsEditorStrings.FilterMenuFromSets, ConvaiActionsListFilter.FromActionSets);
            menu.ShowAsContext();
        }

        private void AddFilterMenuItem(GenericMenu menu, GUIContent label, ConvaiActionsListFilter filter) =>
            menu.AddItem(new GUIContent(label.text), _listFilter == filter, () => SetListFilter(filter));

        private void SetListFilter(ConvaiActionsListFilter filter)
        {
            _listFilter = filter;
            SessionState.SetInt(ListFilterSessionKey, (int)filter);
            Repaint();
        }

        #endregion

        #region Multi-select state

        /// <summary>
        ///     Once per window enable, resolves the SessionState-persisted context keys back to live
        ///     definitions; afterwards, drops selected definitions that are no longer authored
        ///     anywhere on the character (removed rows, undone additions, switched characters).
        /// </summary>
        private void RestoreOrPruneMultiSelection(ConvaiActionConfigSource source)
        {
            if (_multiSelectionRestorePending)
            {
                _multiSelectionRestorePending = false;
                _multiSelection.Clear();
                string stored = SessionState.GetString(MultiSelectionSessionKey, string.Empty);
                if (!string.IsNullOrEmpty(stored))
                {
                    var byKey = new Dictionary<string, ConvaiActionDefinition>(StringComparer.Ordinal);
                    ConvaiActionsProductivityModel.CollectDefinitionsByContextKey(source, byKey);
                    string[] keys = stored.Split(MultiSelectionKeySeparator);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (byKey.TryGetValue(keys[i], out ConvaiActionDefinition definition) &&
                            !MultiSelectionContains(definition))
                            _multiSelection.Add(definition);
                    }
                }

                return;
            }

            for (int i = _multiSelection.Count - 1; i >= 0; i--)
            {
                if (!ConvaiActionsProductivityModel.IsDefinitionAuthored(source, _multiSelection[i]))
                    _multiSelection.RemoveAt(i);
            }
        }

        private bool MultiSelectionContains(ConvaiActionDefinition definition)
        {
            for (int i = 0; i < _multiSelection.Count; i++)
            {
                if (ReferenceEquals(_multiSelection[i], definition))
                    return true;
            }

            return false;
        }

        /// <summary>Row highlight test: the multi-selection when active, the single selection otherwise.</summary>
        private bool IsRowSelected(ConvaiActionDefinition definition) =>
            HasMultiSelection ? MultiSelectionContains(definition) : ReferenceEquals(_selectedDefinition, definition);

        private void ClearMultiSelection()
        {
            if (_multiSelection.Count == 0)
                return;

            _multiSelection.Clear();
            SaveMultiSelectionState();
        }

        /// <summary>
        ///     Persists the current multi-selection as its rows' validator context keys — the same
        ///     positional keys <see cref="ConvaiActionsEditorModel.BuildRowContextKey" /> produces —
        ///     so it survives domain reloads. Click-time only, never per repaint.
        /// </summary>
        private void SaveMultiSelectionState()
        {
            if (_multiSelection.Count == 0)
            {
                SessionState.SetString(MultiSelectionSessionKey, string.Empty);
                return;
            }

            ConvaiActionConfigSource source = _character != null ? _character.GetActionConfigSource() : null;
            if (source == null)
                return;

            var byKey = new Dictionary<string, ConvaiActionDefinition>(StringComparer.Ordinal);
            ConvaiActionsProductivityModel.CollectDefinitionsByContextKey(source, byKey);
            var keys = new List<string>(_multiSelection.Count);
            foreach (KeyValuePair<string, ConvaiActionDefinition> pair in byKey)
            {
                if (MultiSelectionContains(pair.Value))
                    keys.Add(pair.Key);
            }

            SessionState.SetString(MultiSelectionSessionKey, string.Join(MultiSelectionKeySeparator, keys));
        }

        /// <summary>
        ///     Click dispatch for a row's name button: plain click selects that one action,
        ///     Ctrl/Cmd-click toggles it in the multi-selection, Shift-click selects the visible
        ///     range from the current selection anchor.
        /// </summary>
        private void HandleRowClick(ConvaiActionRow row)
        {
            Event current = Event.current;
            bool toggle = current != null && (current.control || current.command);
            bool range = current != null && current.shift;

            if (range && _selectedDefinition != null && !ReferenceEquals(_selectedDefinition, row.Definition))
                SelectVisibleRange(_selectedDefinition, row.Definition);
            else if (toggle)
                ToggleMultiSelection(row.Definition);
            else
            {
                ClearMultiSelection();
                _selectedDefinition = row.Definition;
            }

            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        private void ToggleMultiSelection(ConvaiActionDefinition definition)
        {
            if (definition == null)
                return;

            // Seed with the current single selection so the first Ctrl-click grows a pair.
            if (_multiSelection.Count == 0 && _selectedDefinition != null &&
                !ReferenceEquals(_selectedDefinition, definition))
                _multiSelection.Add(_selectedDefinition);

            int existing = -1;
            for (int i = 0; i < _multiSelection.Count; i++)
            {
                if (ReferenceEquals(_multiSelection[i], definition))
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
                _multiSelection.RemoveAt(existing);
            else
                _multiSelection.Add(definition);

            if (_multiSelection.Count == 1)
            {
                _selectedDefinition = _multiSelection[0];
                _multiSelection.Clear();
            }
            else if (_multiSelection.Count > 1)
            {
                _selectedDefinition = existing >= 0 ? _multiSelection[_multiSelection.Count - 1] : definition;
            }

            SaveMultiSelectionState();
        }

        private void SelectVisibleRange(ConvaiActionDefinition anchor, ConvaiActionDefinition target)
        {
            var visible = new List<ConvaiActionRow>();
            CollectVisibleRows(visible);

            int anchorIndex = -1;
            int targetIndex = -1;
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i].Definition, anchor))
                    anchorIndex = i;
                if (ReferenceEquals(visible[i].Definition, target))
                    targetIndex = i;
            }

            if (anchorIndex < 0 || targetIndex < 0)
            {
                ClearMultiSelection();
                _selectedDefinition = target;
                return;
            }

            _multiSelection.Clear();
            int start = Mathf.Min(anchorIndex, targetIndex);
            int end = Mathf.Max(anchorIndex, targetIndex);
            for (int i = start; i <= end; i++)
                _multiSelection.Add(visible[i].Definition);

            if (_multiSelection.Count == 1)
                _multiSelection.Clear();

            SaveMultiSelectionState();
        }

        /// <summary>Visible rows in display order (skipping collapsed sets), from the last built groups.</summary>
        private void CollectVisibleRows(List<ConvaiActionRow> rows)
        {
            if (_lastGroups == null)
                return;

            for (int g = 0; g < _lastGroups.Count; g++)
            {
                ConvaiActionGroup group = _lastGroups[g];
                if (IsGroupCollapsed(group.Key))
                    continue;

                rows.AddRange(group.Rows);
            }
        }

        /// <summary>Selected rows in visible display order — the authoritative order for bulk operations.</summary>
        private void CollectSelectedRowsInOrder(List<ConvaiActionRow> rows)
        {
            if (_lastGroups == null)
                return;

            for (int g = 0; g < _lastGroups.Count; g++)
            {
                List<ConvaiActionRow> groupRows = _lastGroups[g].Rows;
                for (int r = 0; r < groupRows.Count; r++)
                {
                    if (MultiSelectionContains(groupRows[r].Definition))
                        rows.Add(groupRows[r]);
                }
            }
        }

        #endregion

        #region Multi-selection pane

        private GUIContent MultiSelectionTitle(int count)
        {
            if (_multiTitleCount != count || _multiTitleContent == null)
            {
                _multiTitleCount = count;
                _multiTitleContent = ConvaiActionsEditorStrings.BuildMultiSelectionTitle(count);
            }

            return _multiTitleContent;
        }

        /// <summary>Replaces the single-action detail pane while two or more actions are selected.</summary>
        private void DrawMultiSelectionPane(ConvaiActionConfigSource source)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
                using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
                {
                    GUILayout.Label(MultiSelectionTitle(_multiSelection.Count), Theme.SelectedTitle);
                    GUILayout.Space(4f);
                    GUILayout.Label(ConvaiActionsEditorStrings.MultiSelectionExplainer, Theme.MutedWrapped);
                    GUILayout.Space(10f);

                    Theme.BeginCard();

                    bool anyShared = false;
                    bool anyInline = false;
                    for (int i = 0; i < _multiSelection.Count && (!anyShared || !anyInline); i++)
                    {
                        bool inlineOwned = IndexOfDefinition(source.Definitions, _multiSelection[i]) >= 0;
                        anyInline |= inlineOwned;
                        anyShared |= !inlineOwned;
                    }

                    if (anyShared)
                    {
                        Theme.BeginPanel(Theme.StatusWarn);
                        GUILayout.Label(ConvaiActionsEditorStrings.MultiSharedSelectionNote, Theme.BodyWrapped);
                        Theme.EndPanel();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect offerRect = GUILayoutUtility.GetRect(120f, 26f, GUILayout.Width(120f), GUILayout.Height(26f));
                        if (Theme.GhostButton(offerRect, ConvaiActionsEditorStrings.MultiOfferButton))
                            BulkSetOffered(source, true);

                        GUILayout.Space(8f);
                        Rect stopRect = GUILayoutUtility.GetRect(170f, 26f, GUILayout.Width(170f), GUILayout.Height(26f));
                        if (Theme.GhostButton(stopRect, ConvaiActionsEditorStrings.MultiStopOfferButton))
                            BulkSetOffered(source, false);

                        GUILayout.FlexibleSpace();
                    }

                    GUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect duplicateRect = GUILayoutUtility.GetRect(150f, 26f, GUILayout.Width(150f), GUILayout.Height(26f));
                        if (Theme.GhostButton(duplicateRect, ConvaiActionsEditorStrings.MultiDuplicateButton))
                            BulkDuplicate(source);

                        GUILayout.Space(8f);
                        Rect deleteRect = GUILayoutUtility.GetRect(130f, 26f, GUILayout.Width(130f), GUILayout.Height(26f));
                        if (Theme.GhostButton(deleteRect, ConvaiActionsEditorStrings.MultiDeleteButton))
                            BulkDelete(source);

                        GUILayout.FlexibleSpace();
                    }

                    GUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!anyInline))
                        {
                            Rect extractRect = GUILayoutUtility.GetRect(180f, 26f, GUILayout.Width(180f), GUILayout.Height(26f));
                            if (Theme.GhostButton(extractRect, ConvaiActionsEditorStrings.MultiExtractButton))
                                ShowExtractMenuForSelection(source);
                        }

                        GUILayout.Space(8f);
                        Rect categoryRect = GUILayoutUtility.GetRect(180f, 26f, GUILayout.Width(180f), GUILayout.Height(26f));
                        if (Theme.GhostButton(categoryRect, ConvaiActionsEditorStrings.RowMenuCategoryRoot))
                            ShowCategoryMenuForSelection(source);

                        GUILayout.FlexibleSpace();
                    }

                    GUILayout.Space(10f);
                    DrawMultiRunSection(source);

                    Theme.EndCard();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawMultiRunSection(ConvaiActionConfigSource source)
        {
            if (!EditorApplication.isPlaying)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.MultiRunNeedsPlayMode, Theme.MutedWrapped);
                return;
            }

            EnsureSettingsBindings();
            if (_settingsDispatcher == null)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunNeedsDispatcher, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect runRect = GUILayoutUtility.GetRect(200f, 26f, GUILayout.Width(200f), GUILayout.Height(26f));
                if (Theme.PrimaryButton(runRect, ConvaiActionsEditorStrings.MultiRunInOrderButton))
                    RunSelectionInOrder(source);

                GUILayout.FlexibleSpace();
            }

            DrawTestRunResult();
        }

        /// <summary>
        ///     Runs the selected actions as one sequential injected batch — the same dispatcher seam
        ///     the "Run All In Order" list uses — with no target and no parameter values (rehearse
        ///     a targeted step from its own Try It card instead).
        /// </summary>
        private void RunSelectionInOrder(ConvaiActionConfigSource source)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            if (rows.Count == 0)
                return;

            ConvaiActionTestRunService.ResolveInjectionContext(_character, source,
                out ConvaiActionConfig actionConfig,
                out IReadOnlyList<ConvaiActionDefinition> definitions);

            var commands = new List<ConvaiActionCommand>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                ConvaiActionCommand command = ConvaiActionTestRunModel.BuildCommand(
                    rows[i].Definition, string.Empty, s_emptyParameterTexts, actionConfig, definitions);
                if (command != null)
                    commands.Add(command);
            }

            if (commands.Count == 0)
                return;

            BeginTrackedRun(commands[0].Name);
            ConvaiActionTestRunService.EnqueueBatch(_settingsDispatcher, commands);
        }

        #endregion

        #region Bulk operations

        private void BulkSetOffered(ConvaiActionConfigSource source, bool offered)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            if (rows.Count == 0)
                return;

            string label = offered ? "Offer Actions" : "Stop Offering Actions";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);

            for (int i = 0; i < rows.Count; i++)
            {
                UnityEngine.Object owner = OwnerOf(source, rows[i]);
                Undo.RecordObject(owner, label);
                rows[i].Definition.Enabled = offered;
                MarkDirty(owner);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Repaint();
        }

        /// <summary>
        ///     Adds a copy of every selected action to the character's inline list, in visible order.
        ///     Inline originals keep their bound behavior component; shared originals become detached
        ///     local copies (behavior carried as a type hint), and their sets are never modified.
        /// </summary>
        private void BulkDuplicate(ConvaiActionConfigSource source)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            if (rows.Count == 0)
                return;

            Undo.RecordObject(source, "Duplicate Actions");
            List<string> names = ConvaiActionsProductivityModel.CollectEffectiveActionNames(source);
            var inline = new List<ConvaiActionDefinition>(source.Definitions);
            ConvaiActionDefinition lastCopy = null;
            for (int i = 0; i < rows.Count; i++)
            {
                ConvaiActionDefinition copy = rows[i].IsShared
                    ? ConvaiActionsProductivityModel.CreateDetachedSnapshot(rows[i].Definition)
                    : rows[i].Definition.Clone();
                if (copy == null)
                    continue;

                copy.ActionName = ConvaiActionsProductivityModel.MakeDuplicateActionName(copy.ActionName, names);
                names.Add(copy.ActionName);
                inline.Add(copy);
                lastCopy = copy;
            }

            source.ReplaceDefinitions(inline);
            MarkDirty(source);

            if (lastCopy != null)
            {
                ClearMultiSelection();
                _selectedDefinition = lastCopy;
            }

            Repaint();
        }

        private void BulkDelete(ConvaiActionConfigSource source)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            if (rows.Count == 0)
                return;

            int sharedCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsShared)
                    sharedCount++;
            }

            string body = sharedCount > 0
                ? $"{rows.Count} selected action{(rows.Count == 1 ? string.Empty : "s")} will be removed. " +
                  $"{sharedCount} of them live in shared Action Sets, so every Convai Character using those " +
                  "sets loses them too.\n\nThis can be undone."
                : $"{rows.Count} selected action{(rows.Count == 1 ? string.Empty : "s")} will be removed from " +
                  "this Convai Character.\n\nThis can be undone.";
            if (!EditorUtility.DisplayDialog("Delete selected actions?", body, "Delete", "Cancel"))
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Delete Actions");

            var inline = new List<ConvaiActionDefinition>(source.Definitions);
            bool inlineChanged = false;
            var touchedSets = new List<ConvaiActionSet>();

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].OwningSet == null)
                {
                    int index = IndexOfDefinition(inline, rows[i].Definition);
                    if (index >= 0)
                    {
                        if (!inlineChanged)
                        {
                            Undo.RecordObject(source, "Delete Actions");
                            inlineChanged = true;
                        }

                        inline.RemoveAt(index);
                    }
                }
                else if (!touchedSets.Contains(rows[i].OwningSet))
                {
                    touchedSets.Add(rows[i].OwningSet);
                }
            }

            if (inlineChanged)
            {
                source.ReplaceDefinitions(inline);
                MarkDirty(source);
            }

            for (int s = 0; s < touchedSets.Count; s++)
            {
                ConvaiActionSet set = touchedSets[s];
                Undo.RecordObject(set, "Delete Actions");
                var kept = new List<ConvaiActionDefinition>();
                IReadOnlyList<ConvaiActionDefinition> setDefinitions = set.Definitions;
                for (int d = 0; d < setDefinitions.Count; d++)
                {
                    if (!MultiSelectionContains(setDefinitions[d]))
                        kept.Add(setDefinitions[d]);
                }

                set.ReplaceDefinitions(kept);
                MarkDirty(set);
            }

            Undo.CollapseUndoOperations(undoGroup);
            ClearMultiSelection();
            _selectedDefinition = null;
            Repaint();
        }

        #endregion

        #region Row operations menu

        /// <summary>
        ///     The per-row operations menu, reachable by right-clicking a list row and from the "⋯"
        ///     button in the detail pane's header. Inline rows get the full set; rows shared from an
        ///     Action Set get Make Local Copy (the set asset itself is never cloned into) plus
        ///     Copy/Paste and the existing confirmed removal path.
        /// </summary>
        private void ShowRowOperationsMenu(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            var menu = new GenericMenu();
            ConvaiActionDefinition definition = row.Definition;

            if (row.IsShared)
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuMakeLocalCopy.text), false,
                    () => MakeLocalCopy(source, row));
            else
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuDuplicate.text), false,
                    () => DuplicateRow(source, row));

            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuCopy.text), false, () =>
            {
                ConvaiActionsClipboard.Copy(definition);
                Repaint();
            });

            if (ConvaiActionsClipboard.HasContent)
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuPaste.text), false,
                    () => PasteAction(source));
            else
                menu.AddDisabledItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuPaste.text));

            menu.AddSeparator(string.Empty);
            AddCategoryMenuItems(menu, source, new List<ConvaiActionRow> { row });

            if (!row.IsShared)
            {
                menu.AddSeparator(string.Empty);
                AddExtractMenuItems(menu, source, new List<ConvaiActionDefinition> { definition });
            }

            menu.AddSeparator(string.Empty);
            GUIContent removeLabel = row.IsShared
                ? ConvaiActionsEditorStrings.RowMenuRemoveFromSet
                : ConvaiActionsEditorStrings.RowMenuDelete;
            menu.AddItem(new GUIContent(removeLabel.text), false, () =>
            {
                RemoveAction(source, row);
                Repaint();
            });

            menu.ShowAsContext();
        }

        private void DuplicateRow(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            Undo.RecordObject(source, "Duplicate Action");
            List<string> names = ConvaiActionsProductivityModel.CollectEffectiveActionNames(source);
            ConvaiActionDefinition copy = row.Definition.Clone();
            copy.ActionName = ConvaiActionsProductivityModel.MakeDuplicateActionName(copy.ActionName, names);

            var inline = new List<ConvaiActionDefinition>(source.Definitions);
            int insertAt = Mathf.Clamp(row.OwnerIndex + 1, 0, inline.Count);
            inline.Insert(insertAt, copy);
            source.ReplaceDefinitions(inline);
            MarkDirty(source);

            ClearMultiSelection();
            _selectedDefinition = copy;
            Repaint();
        }

        /// <summary>
        ///     Clones a shared row into this character's own inline list under a collision-safe name.
        ///     The behavior travels as a type hint and re-binds to a matching component on this
        ///     character when one exists; the Action Set asset is untouched.
        /// </summary>
        private void MakeLocalCopy(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            ConvaiActionDefinition copy = ConvaiActionsProductivityModel.CreateDetachedSnapshot(row.Definition);
            if (copy == null)
                return;

            Undo.RecordObject(source, "Make Local Copy");
            List<string> names = ConvaiActionsProductivityModel.CollectEffectiveActionNames(source);
            copy.ActionName = ConvaiActionsProductivityModel.MakeUniqueActionName(copy.ActionName, names);
            TryBindDetachedBehavior(source, copy);

            var inline = new List<ConvaiActionDefinition>(source.Definitions) { copy };
            source.ReplaceDefinitions(inline);
            MarkDirty(source);

            ClearMultiSelection();
            _selectedDefinition = copy;
            Repaint();
        }

        private void PasteAction(ConvaiActionConfigSource source)
        {
            ConvaiActionDefinition pasted = ConvaiActionsClipboard.CreatePasteClone();
            if (pasted == null || source == null)
                return;

            Undo.RecordObject(source, "Paste Action");
            List<string> names = ConvaiActionsProductivityModel.CollectEffectiveActionNames(source);
            pasted.ActionName = ConvaiActionsProductivityModel.MakeUniqueActionName(pasted.ActionName, names);
            TryBindDetachedBehavior(source, pasted);

            var inline = new List<ConvaiActionDefinition>(source.Definitions) { pasted };
            source.ReplaceDefinitions(inline);
            MarkDirty(source);

            ClearMultiSelection();
            _selectedDefinition = pasted;
            Repaint();
        }

        /// <summary>
        ///     Binds a detached definition's behavior type hint to a matching component already on
        ///     this character, mirroring runtime auto-binding — an explicit paste/copy action, never
        ///     a repaint side effect. Definitions with no resolvable hint are left as-is; the Scene
        ///     Behavior card then offers its usual add-and-bind path.
        /// </summary>
        private static void TryBindDetachedBehavior(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            if (definition.Executor != null || string.IsNullOrWhiteSpace(definition.ExecutorTypeHint))
                return;

            if (!ConvaiActionExecutorBinder.TryResolveType(definition.ExecutorTypeHint, out Type resolvedType))
                return;

            if (source.GetComponentInChildren(resolvedType, true) is MonoBehaviour candidate &&
                candidate is IConvaiActionExecutor)
                definition.Executor = candidate;
        }

        #endregion

        #region Extract to Action Set

        private void ShowExtractMenuForSelection(ConvaiActionConfigSource source)
        {
            var rows = new List<ConvaiActionRow>();
            CollectSelectedRowsInOrder(rows);
            var inlineDefinitions = new List<ConvaiActionDefinition>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsShared)
                    inlineDefinitions.Add(rows[i].Definition);
            }

            if (inlineDefinitions.Count == 0)
                return;

            var menu = new GenericMenu();
            AddExtractMenuItems(menu, source, inlineDefinitions);
            menu.ShowAsContext();
        }

        private void AddExtractMenuItems(
            GenericMenu menu,
            ConvaiActionConfigSource source,
            List<ConvaiActionDefinition> inlineDefinitions)
        {
            string root = ConvaiActionsEditorStrings.ExtractMenuRoot.text;
            menu.AddItem(new GUIContent(root + "/" + ConvaiActionsEditorStrings.ExtractNewSetMenuItem.text), false,
                () => ExtractToNewSet(source, inlineDefinitions));

            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            if (sets == null)
                return;

            for (int i = 0; i < sets.Count; i++)
            {
                ConvaiActionSet set = sets[i];
                if (set == null)
                    continue;

                menu.AddItem(new GUIContent(root + "/" + set.name), false,
                    () => ExtractToSet(source, set, inlineDefinitions));
            }
        }

        private void ExtractToNewSet(ConvaiActionConfigSource source, List<ConvaiActionDefinition> inlineDefinitions)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Action Set",
                "ConvaiActionSet",
                "asset",
                "Create the Action Set asset the selected actions move into.");
            if (string.IsNullOrWhiteSpace(path))
                return;

            ConvaiActionSet asset = ConvaiActionSet.CreateDefault();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            ExtractToSet(source, asset, inlineDefinitions);
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        ///     Moves inline definitions into <paramref name="set" /> as one Undo group: each is
        ///     converted for asset life (bound behavior component → type hint), renamed
        ///     collision-safe against the set, appended, removed from the inline list, and the set is
        ///     assigned to this character when it was not already. Actions whose behavior could not
        ///     be carried as a hint are reported afterwards — they will not run from the set until a
        ///     behavior is chosen.
        /// </summary>
        private void ExtractToSet(
            ConvaiActionConfigSource source,
            ConvaiActionSet set,
            List<ConvaiActionDefinition> inlineDefinitions)
        {
            if (source == null || set == null || inlineDefinitions == null || inlineDefinitions.Count == 0)
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Extract To Action Set");

            Undo.RecordObject(set, "Extract To Action Set");
            Undo.RecordObject(source, "Extract To Action Set");

            List<string> setNames = ConvaiActionsProductivityModel.CollectSetActionNames(set);
            var setList = new List<ConvaiActionDefinition>(set.Definitions);
            var withoutBehavior = new List<string>();
            ConvaiActionDefinition firstMoved = null;

            var inline = new List<ConvaiActionDefinition>(source.Definitions);
            for (int i = 0; i < inlineDefinitions.Count; i++)
            {
                int inlineIndex = IndexOfDefinition(inline, inlineDefinitions[i]);
                if (inlineIndex < 0)
                    continue;

                ConvaiActionDefinition converted =
                    ConvaiActionsProductivityModel.ConvertForActionSet(inlineDefinitions[i], out bool behaviorLost);
                if (converted == null)
                    continue;

                if (behaviorLost)
                    withoutBehavior.Add(string.IsNullOrWhiteSpace(converted.ActionName)
                        ? "(unnamed action)"
                        : converted.ActionName);

                converted.ActionName = ConvaiActionsProductivityModel.MakeUniqueActionName(converted.ActionName, setNames);
                setNames.Add(converted.ActionName);
                setList.Add(converted);
                inline.RemoveAt(inlineIndex);
                firstMoved ??= converted;
            }

            if (firstMoved == null)
            {
                Undo.CollapseUndoOperations(undoGroup);
                return;
            }

            set.ReplaceDefinitions(setList);
            MarkDirty(set);

            source.ReplaceDefinitions(inline);
            if (!ConvaiActionsEditorModel.IsSetAssigned(source, set))
            {
                var sets = new List<ConvaiActionSet>(source.ActionSets) { set };
                source.ReplaceActionSets(sets);
            }

            MarkDirty(source);
            Undo.CollapseUndoOperations(undoGroup);

            ClearMultiSelection();
            _selectedDefinition = firstMoved;
            ExpandGroup(ConvaiActionsEditorModel.BuildActionSetGroupKey(set));

            if (withoutBehavior.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Some actions have no behavior yet",
                    $"These actions moved into '{set.name}' without a Scene Behavior: " +
                    string.Join(", ", withoutBehavior) +
                    ".\n\nThey will not run from the set until a behavior is chosen for them.",
                    "OK");
            }

            Repaint();
        }

        #endregion

        #region Toolbar overflow — import/export

        private void ShowToolbarOverflowMenu(ConvaiActionConfigSource source)
        {
            var menu = new GenericMenu();
            if (ConvaiActionsClipboard.HasContent)
                menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuPaste.text), false,
                    () => PasteAction(source));
            else
                menu.AddDisabledItem(new GUIContent(ConvaiActionsEditorStrings.RowMenuPaste.text));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.ExportActionsMenuItem.text), false,
                () => ExportActions(source, includeSceneKnowledge: false));
            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.ExportWithKnowledgeMenuItem.text), false,
                () => ExportActions(source, includeSceneKnowledge: true));
            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.ImportActionsMenuItem.text), false,
                () => ImportActions(source));
            menu.ShowAsContext();
        }

        private void ExportActions(ConvaiActionConfigSource source, bool includeSceneKnowledge)
        {
            if (source == null)
                return;

            string path = EditorUtility.SaveFilePanel(
                "Export Actions", string.Empty, source.name + "-actions", "json");
            if (string.IsNullOrWhiteSpace(path))
                return;

            ConvaiActionsTransferModel.ExportDocument document = ConvaiActionsTransferModel.BuildDocument(
                source.Definitions,
                includeSceneKnowledge,
                source.Objects,
                source.Characters,
                source.InitialAttentionObject);

            try
            {
                File.WriteAllText(path, ConvaiActionsTransferModel.ToJson(document));
            }
            catch (SystemException exception)
            {
                EditorUtility.DisplayDialog(
                    "Export Actions",
                    "The file could not be written.\n\n" + exception.Message,
                    "OK");
                return;
            }

            ConvaiLogger.Info(
                $"[ConvaiActionsEditorWindow] Exported {document.Actions.Count} action(s) from '{source.name}' to '{path}'.",
                LogCategory.Editor);
            EditorUtility.RevealInFinder(path);
        }

        private void ImportActions(ConvaiActionConfigSource source)
        {
            if (source == null)
                return;

            string path = EditorUtility.OpenFilePanel("Import Actions", string.Empty, "json");
            if (string.IsNullOrWhiteSpace(path))
                return;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (SystemException exception)
            {
                EditorUtility.DisplayDialog(
                    "Import Actions",
                    "The file could not be read.\n\n" + exception.Message,
                    "OK");
                return;
            }

            if (!ConvaiActionsTransferModel.TryParse(json, out ConvaiActionsTransferModel.ExportDocument document,
                    out string error))
            {
                EditorUtility.DisplayDialog(
                    "Import Actions",
                    "The file could not be imported.\n\n" + error,
                    "OK");
                return;
            }

            var collisionMode = ConvaiActionsImportCollisionMode.Skip;
            int collisions = CountImportCollisions(source.Definitions, document);
            if (collisions > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Import Actions",
                    $"{collisions} imported action{(collisions == 1 ? " has" : "s have")} the same name as an " +
                    $"action already on '{source.name}'.\n\nKeep Both adds them under a 'Copy' name, Skip These " +
                    "leaves the existing actions untouched, Overwrite replaces them with the imported versions.",
                    "Keep Both",
                    "Skip These",
                    "Overwrite");
                collisionMode = choice switch
                {
                    0 => ConvaiActionsImportCollisionMode.Rename,
                    2 => ConvaiActionsImportCollisionMode.Overwrite,
                    _ => ConvaiActionsImportCollisionMode.Skip
                };
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Import Actions");
            Undo.RecordObject(source, "Import Actions");

            ConvaiActionsTransferModel.ImportResult result = ConvaiActionsTransferModel.ApplyImport(
                source.Definitions,
                ConvaiActionsProductivityModel.CollectEffectiveActionNames(source),
                document,
                collisionMode);
            for (int i = 0; i < result.Imported.Count; i++)
                TryBindDetachedBehavior(source, result.Imported[i]);
            source.ReplaceDefinitions(result.Definitions);

            int objectsAdded = 0;
            if (document.Objects != null && document.Objects.Count > 0)
            {
                var objects = new List<ConvaiActionObjectDefinition>(source.Objects);
                objectsAdded = ConvaiActionsTransferModel.MergeKnownObjects(objects, document.Objects);
                if (objectsAdded > 0)
                    source.ReplaceObjects(objects);
            }

            int charactersAdded = 0;
            if (document.Characters != null && document.Characters.Count > 0)
            {
                var characters = new List<ConvaiActionCharacterDefinition>(source.Characters);
                charactersAdded = ConvaiActionsTransferModel.MergeKnownCharacters(characters, document.Characters);
                if (charactersAdded > 0)
                    source.ReplaceCharacters(characters);
            }

            MarkDirty(source);

            // Only fills a blank starting focus — an authored one is never silently overwritten.
            if (!string.IsNullOrWhiteSpace(document.InitialAttentionObject) &&
                string.IsNullOrWhiteSpace(source.InitialAttentionObject))
                WriteInitialAttention(source, document.InitialAttentionObject);

            Undo.CollapseUndoOperations(undoGroup);
            MarkSceneKnowledgeCachesStale();

            string knowledgeSummary = objectsAdded > 0 || charactersAdded > 0
                ? $"\nScene knowledge: {objectsAdded} object(s) and {charactersAdded} character(s) added."
                : string.Empty;
            EditorUtility.DisplayDialog(
                "Import Actions",
                $"Import finished: {result.AddedCount} added, {result.RenamedCount} kept under a new name, " +
                $"{result.OverwrittenCount} overwritten, {result.SkippedCount} skipped.{knowledgeSummary}",
                "OK");
            Repaint();
        }

        private static int CountImportCollisions(
            IReadOnlyList<ConvaiActionDefinition> existingInline,
            ConvaiActionsTransferModel.ExportDocument document)
        {
            if (existingInline == null || document?.Actions == null)
                return 0;

            int collisions = 0;
            for (int i = 0; i < document.Actions.Count; i++)
            {
                string incoming = document.Actions[i]?.Name?.Trim();
                if (string.IsNullOrEmpty(incoming))
                    continue;

                for (int e = 0; e < existingInline.Count; e++)
                {
                    string existing = existingInline[e]?.ActionName;
                    if (!string.IsNullOrWhiteSpace(existing) &&
                        string.Equals(existing.Trim(), incoming, StringComparison.OrdinalIgnoreCase))
                    {
                        collisions++;
                        break;
                    }
                }
            }

            return collisions;
        }

        #endregion
    }
}
