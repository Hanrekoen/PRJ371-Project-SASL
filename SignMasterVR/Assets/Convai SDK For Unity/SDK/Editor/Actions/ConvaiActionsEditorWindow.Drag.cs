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
    ///     Dragging actions in the left pane: one gesture that both files an action under a category
    ///     and places it where it was dropped.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two drop targets, drawn differently and never both live. Between two rows draws an
    ///         insertion line and lands the action at that position, taking that section's category.
    ///         On a header it draws a tinted plate and lands the action at the end of that group.
    ///     </para>
    ///     <para>
    ///         One drag is one undo entry, a drop that changes nothing records none at all, and a drop
    ///         that would change which object owns the action is refused outright — an action cannot
    ///         move between this character and a shared Action Set by being dragged, because that is a
    ///         change of ownership, not of arrangement.
    ///     </para>
    /// </remarks>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string DragDataKey = "Convai.ActionsEditor.DraggedActions";
        private const string DragTitle = "Move Actions";

        /// <summary>How far the pointer must travel before a click on a row becomes a drag.</summary>
        private const float DragThreshold = 4f;

        private readonly List<ConvaiActionRow> _draggedRows = new();
        private Vector2 _dragCandidateStart;
        private ConvaiActionDefinition _dragCandidate;

        /// <summary>True while this window is the source of the in-flight drag.</summary>
        private bool IsDraggingRows =>
            _draggedRows.Count > 0 && ReferenceEquals(DragAndDrop.GetGenericData(DragDataKey), this);

        // ── Source ─────────────────────────────────────────────────────────────

        /// <summary>
        ///     Turns a press-and-move on a row card into a drag of the whole selection (or of just
        ///     that row when it is not part of the selection).
        /// </summary>
        private void HandleRowDragSource(ConvaiActionRow row, Rect card)
        {
            Event current = Event.current;
            switch (current.type)
            {
                case EventType.MouseDown when current.button == 0 && card.Contains(current.mousePosition):
                    _dragCandidate = row.Definition;
                    _dragCandidateStart = current.mousePosition;
                    break;

                case EventType.MouseDrag when _dragCandidate != null &&
                                              ReferenceEquals(_dragCandidate, row.Definition) &&
                                              Vector2.Distance(current.mousePosition, _dragCandidateStart) > DragThreshold:
                    BeginRowDrag(row);
                    current.Use();
                    break;

                case EventType.MouseUp:
                    _dragCandidate = null;
                    break;
            }
        }

        private void BeginRowDrag(ConvaiActionRow row)
        {
            _draggedRows.Clear();

            // Dragging a row that is part of a multi-selection carries the whole selection, in the
            // order it is displayed in; dragging any other row carries just that row.
            if (IsRowSelected(row.Definition) && HasMultiSelection)
                CollectSelectedRowsInOrder(_draggedRows);

            if (_draggedRows.Count == 0)
                _draggedRows.Add(row);

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragDataKey, this);
            DragAndDrop.objectReferences = new Object[0];
            DragAndDrop.StartDrag(_draggedRows.Count == 1 ? _draggedRows[0].DisplayName : DragTitle);
            _dragCandidate = null;
        }

        private void EndRowDrag()
        {
            _draggedRows.Clear();
            _dragCandidate = null;
            DragAndDrop.SetGenericData(DragDataKey, null);
        }

        // ── Drop: between rows ─────────────────────────────────────────────────

        /// <summary>
        ///     Handles a drop onto one row card: the top half means "before this action", the bottom
        ///     half "after it", and either way the dragged actions take this row's category.
        /// </summary>
        private void HandleRowDropTarget(ConvaiActionConfigSource source, ConvaiActionRow row, Rect card)
        {
            if (!IsDraggingRows || !card.Contains(Event.current.mousePosition))
                return;

            bool after = Event.current.mousePosition.y > card.center.y;
            bool allowed = CanDropOn(row);

            switch (Event.current.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                    if (allowed)
                        DrawInsertionLine(card, after);
                    Event.current.Use();
                    break;

                case EventType.Repaint when allowed:
                    DrawInsertionLine(card, after);
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    if (allowed)
                        DropRows(source, row.OwningSet, row.Definition, after,
                            ConvaiActionCategory.Normalize(row.Definition?.Category));
                    EndRowDrag();
                    Event.current.Use();
                    break;

                case EventType.DragExited:
                    EndRowDrag();
                    break;
            }
        }

        /// <summary>Handles a drop onto a category header: the actions land at the end of that group.</summary>
        private void HandleHeaderDropTarget(ConvaiActionConfigSource source, ConvaiActionGroup group, Rect header)
        {
            if (!IsDraggingRows || group.Kind != ConvaiActionGroupKind.Category ||
                !header.Contains(Event.current.mousePosition))
                return;

            bool allowed = CanDropOnHeader();

            switch (Event.current.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                    if (allowed)
                        DrawHeaderDropPlate(header);
                    Event.current.Use();
                    break;

                case EventType.Repaint when allowed:
                    DrawHeaderDropPlate(header);
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    if (allowed)
                        DropOnCategory(source, group.CategoryName);
                    EndRowDrag();
                    Event.current.Use();
                    break;

                case EventType.DragExited:
                    EndRowDrag();
                    break;
            }
        }

        /// <summary>
        ///     Whether the carried rows may land on this one: only when every one of them is owned by
        ///     the same list. Moving an action between this character and an Action Set changes who owns
        ///     it, which is a decision, not a drag.
        /// </summary>
        private bool CanDropOn(ConvaiActionRow target)
        {
            for (int i = 0; i < _draggedRows.Count; i++)
            {
                if (!ReferenceEquals(_draggedRows[i].OwningSet, target.OwningSet))
                    return false;

                if (ReferenceEquals(_draggedRows[i].Definition, target.Definition))
                    return false; // Dropping a row onto itself is a no-op, not a move.
            }

            return true;
        }

        /// <summary>A header drop needs the carried rows to agree on an owner; the header itself has none.</summary>
        private bool CanDropOnHeader()
        {
            for (int i = 1; i < _draggedRows.Count; i++)
            {
                if (!ReferenceEquals(_draggedRows[i].OwningSet, _draggedRows[0].OwningSet))
                    return false;
            }

            return _draggedRows.Count > 0;
        }

        private static void DrawInsertionLine(Rect card, bool after)
        {
            var line = new Rect(card.x + 2f, after ? card.yMax - 1f : card.y - 1f, card.width - 4f, 2f);
            Theme.FillRounded(line, Theme.Accent, 1f);
        }

        private static void DrawHeaderDropPlate(Rect header)
        {
            Theme.FillRounded(header, Theme.Fade(Theme.Accent, 0.18f), 4f);
            Theme.StrokeRounded(header, Theme.Fade(Theme.Accent, 0.7f), 4f);
        }

        // ── Drop: the write ────────────────────────────────────────────────────

        private void DropOnCategory(ConvaiActionConfigSource source, string category)
        {
            ConvaiActionSet owningSet = _draggedRows[0].OwningSet;
            IReadOnlyList<ConvaiActionDefinition> owned =
                owningSet != null ? owningSet.Definitions : source.Definitions;

            // "End of this group" is expressed as an anchor rather than an index, so the arithmetic
            // stays in one place — the one that already knows what removing the moved rows does.
            ConvaiActionDefinition anchor = ConvaiActionsGrouping.FindLastInCategory(owned, category);
            DropRows(source, owningSet, anchor, true, category);
        }

        /// <summary>
        ///     Applies one drag: re-files the carried rows under <paramref name="category" /> and moves
        ///     them next to <paramref name="anchor" />, as a single undo entry. A drop that would change
        ///     nothing writes nothing.
        /// </summary>
        private void DropRows(
            ConvaiActionConfigSource source,
            ConvaiActionSet owningSet,
            ConvaiActionDefinition anchor,
            bool after,
            string category)
        {
            if (source == null || _draggedRows.Count == 0)
                return;

            IReadOnlyList<ConvaiActionDefinition> owned =
                owningSet != null ? owningSet.Definitions : source.Definitions;

            var moving = new List<ConvaiActionDefinition>(_draggedRows.Count);
            bool categoryChanges = false;
            for (int i = 0; i < _draggedRows.Count; i++)
            {
                ConvaiActionDefinition definition = _draggedRows[i].Definition;
                if (definition == null)
                    continue;

                moving.Add(definition);
                categoryChanges |= !ConvaiActionCategory.AreSame(definition.Category, category);
            }

            if (moving.Count == 0)
                return;

            List<ConvaiActionDefinition> reordered =
                ConvaiActionsGrouping.MoveWithin(owned, moving, anchor, after);

            if (!categoryChanges && SameOrder(owned, reordered))
                return;

            Object owner = OwnerOf(source, _draggedRows[0]);
            string label = ConvaiActionCategory.IsUncategorized(category)
                ? DragTitle
                : $"File Actions Under '{category}'";

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);

            Undo.RecordObject(owner, label);
            for (int i = 0; i < moving.Count; i++)
                moving[i].Category = category;

            ReplaceOwnedDefinitions(source, owningSet, reordered);
            MarkDirty(owner);
            Undo.CollapseUndoOperations(undoGroup);

            InvalidateAutomaticGroupAxis();
            ApplyAutomaticGroupAxis(source);
            ExpandGroup(ConvaiActionsGrouping.BuildCategoryGroupKey(category));
            Repaint();
        }

        private static bool SameOrder(
            IReadOnlyList<ConvaiActionDefinition> left, IReadOnlyList<ConvaiActionDefinition> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!ReferenceEquals(left[i], right[i]))
                    return false;
            }

            return true;
        }
    }
}
