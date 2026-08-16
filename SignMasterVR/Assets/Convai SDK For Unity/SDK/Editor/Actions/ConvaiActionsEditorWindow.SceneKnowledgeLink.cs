using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The half of a Known entry that says <em>where</em> it is: the Scene Object field, the
    ///     status line that tells the user in one sentence what this character can currently do with
    ///     the entry, the one-click ways to make the link, and the optional extras (other names, an
    ///     interaction point) the entry has always supported without ever showing them.
    /// </summary>
    /// <remarks>
    ///     Before this, a Known entry had a name and a description and no way to point at anything,
    ///     while the runtime skipped every entry that pointed at nothing — so the window's own Add
    ///     button produced an error the product had no way to resolve. Every state below is therefore
    ///     written to be actionable: it says what is true now, and offers the next move.
    /// </remarks>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        /// <summary>Gap between an entry's own fields and its <c>Extras (optional)</c> section header.</summary>
        private const float EntryExtrasGapAbove = 8f;

        /// <summary>Gap between the extras (folded or open) and the bottom of the entry's panel.</summary>
        private const float EntryExtrasGapBelow = 4f;

        /// <summary>
        ///     One Known entry, whichever of the two kinds it is. A struct so that reading an entry's
        ///     link state through a shared code path costs no allocation per row per repaint.
        /// </summary>
        private readonly struct KnownEntryHandle
        {
            private readonly ConvaiActionObjectDefinition _object;
            private readonly ConvaiActionCharacterDefinition _character;

            internal KnownEntryHandle(ConvaiActionObjectDefinition entry)
            {
                _object = entry;
                _character = null;
            }

            internal KnownEntryHandle(ConvaiActionCharacterDefinition entry)
            {
                _object = null;
                _character = entry;
            }

            internal ConvaiActionTargetKind Kind =>
                _object != null ? ConvaiActionTargetKind.Object : ConvaiActionTargetKind.Character;

            internal string UndoLabel => _object != null ? "Edit Known Object" : "Edit Known Character";

            internal string Name
            {
                get => _object != null ? _object.Name : _character?.Name;
                set
                {
                    if (_object != null) _object.Name = value;
                    else if (_character != null) _character.Name = value;
                }
            }

            internal GameObject Reference
            {
                get => _object != null ? _object.GameObjectReference : _character?.GameObjectReference;
                set
                {
                    if (_object != null) _object.GameObjectReference = value;
                    else if (_character != null) _character.GameObjectReference = value;
                }
            }

            internal bool TextOnly
            {
                get => _object != null ? _object.TextOnly : _character?.TextOnly ?? false;
                set
                {
                    if (_object != null) _object.TextOnly = value;
                    else if (_character != null) _character.TextOnly = value;
                }
            }

            internal Transform InteractionPoint
            {
                get => _object != null ? _object.InteractionPoint : _character?.InteractionPoint;
                set
                {
                    if (_object != null) _object.InteractionPoint = value;
                    else if (_character != null) _character.InteractionPoint = value;
                }
            }

            internal List<string> Aliases
            {
                get
                {
                    if (_object != null)
                        return _object.Aliases ??= new List<string>();

                    if (_character != null)
                        return _character.Aliases ??= new List<string>();

                    return null;
                }
            }

            internal ConvaiKnownEntryLinkState Classify(IReadOnlyList<ConvaiActionTarget> sceneTargets) =>
                _object != null
                    ? ConvaiSceneKnowledgeLinkModel.ClassifyObject(_object, sceneTargets)
                    : ConvaiSceneKnowledgeLinkModel.ClassifyCharacter(_character, sceneTargets);
        }

        /// <summary>Scene targets behind the link status, refreshed only when something could have changed.</summary>
        private ConvaiActionTarget[] _linkTargets = System.Array.Empty<ConvaiActionTarget>();
        private bool _linkTargetsStale = true;

        /// <summary>
        ///     The two list cards' rects, captured on Repaint so a drag event — which arrives in its
        ///     own pass — has a dependable rect to hit-test against.
        /// </summary>
        private Rect _knownObjectsCardRect;
        private Rect _knownCharactersCardRect;

        /// <summary>Result of the last "Find In Scene", shown under the entry that asked for it.</summary>
        private string _linkSearchKey;
        private GUIContent _linkSearchMessage;

        private void MarkSceneKnowledgeLinkTargetsStale() => _linkTargetsStale = true;

        /// <summary>
        ///     Refreshes the scene-target list once per draw pass at most, and only when the scene or
        ///     the entries have actually changed. A per-repaint scene search would cost more than
        ///     everything else this window does put together.
        /// </summary>
        private void EnsureLinkTargets()
        {
            if (!_linkTargetsStale)
                return;

            _linkTargets = ConvaiObjectFind.All<ConvaiActionTarget>(FindObjectsInactive.Include);
            _linkTargetsStale = false;
        }

        /// <summary>
        ///     Draws an entry's Scene Object field, its state in one sentence, and whatever single
        ///     move that state makes obvious.
        /// </summary>
        private void DrawKnownEntryLink(ConvaiActionConfigSource source, KnownEntryHandle entry, int index)
        {
            EnsureLinkTargets();
            ConvaiKnownEntryLinkState state = entry.Classify(_linkTargets);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(entry.TextOnly))
                {
                    EditorGUI.BeginChangeCheck();
                    var picked = (GameObject)EditorGUILayout.ObjectField(
                        ConvaiActionsEditorStrings.KnownEntrySceneObjectField, entry.Reference, typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck())
                        AssignEntryReference(source, entry, picked);
                }

                GUILayout.Space(6f);
                DrawKnownEntryLinkedBadge(state);
                GUILayout.Space(4f);
                DrawKnownEntryOverridesBadge(entry);
                GUILayout.Space(6f);

                // Measured rather than a round 96, so the toggle ends exactly on the card's right
                // margin like the Name and Description rows above it. A fixed width wider than the
                // content leaves dead space, and a form whose rows end on three different edges reads
                // as unfinished long before anyone works out why.
                float toggleWidth = ConvaiEditorTextMetrics.Width(
                    EditorStyles.toggle, ConvaiActionsEditorStrings.KnownEntryTextOnlyToggle) + 2f;

                EditorGUI.BeginChangeCheck();
                bool textOnly = GUILayout.Toggle(
                    entry.TextOnly,
                    ConvaiActionsEditorStrings.KnownEntryTextOnlyToggle,
                    GUILayout.Width(toggleWidth));
                if (EditorGUI.EndChangeCheck())
                    SetEntryTextOnly(source, entry, textOnly);
            }

            DrawKnownEntryLinkStatus(source, entry, index, state);
            DrawKnownEntryAdvanced(source, entry, index);
        }

        /// <summary>
        ///     The settled-state badge, beside the Scene Object field it reports on.
        /// </summary>
        /// <remarks>
        ///     Its width is reserved in every state and filled in only when the entry is linked, so
        ///     the object field does not change width as an entry is linked and unlinked — geometry
        ///     that shifts under the pointer while you are working in it is its own small defect.
        ///     Nothing is drawn for the other three states: two of them say their piece on the row
        ///     below, with the button that resolves them, and text-only is already stated by the tick
        ///     box two controls to the right.
        /// </remarks>
        private static void DrawKnownEntryLinkedBadge(ConvaiKnownEntryLinkState state)
        {
            float width = Theme.PillWidth(ConvaiActionsEditorStrings.KnownEntryLinkedStatus, true);
            Rect rect = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width), GUILayout.Height(18f));

            if (state == ConvaiKnownEntryLinkState.Linked)
                Theme.Pill(rect, ConvaiActionsEditorStrings.KnownEntryLinkedStatus, Theme.StatusReady, true);
        }

        /// <summary>
        ///     Marks an entry whose name a scene <see cref="ConvaiActionTarget" /> also answers to,
        ///     because the entry silently wins: Convai receives the entry's description and never the
        ///     component's.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A pill rather than a sentence. The rule behind it is explained once, on Find
        ///         Targets In Your Scene, and repeating it on every matching row would bury the two
        ///         status lines that actually ask the author to do something.
        ///     </para>
        ///     <para>
        ///         Independent of the link state: an entry pointing at its own object still overrides
        ///         a same-named component's text, and that is the case most likely to surprise —
        ///         editing the component then appears to do nothing at all.
        ///     </para>
        ///     <para>
        ///         Its width is reserved whether or not it is shown, like the badge beside it, so a
        ///         row does not change shape as its name is typed.
        ///     </para>
        /// </remarks>
        private void DrawKnownEntryOverridesBadge(KnownEntryHandle entry)
        {
            float width = Theme.PillWidth(ConvaiActionsEditorStrings.KnownEntryOverridesTargetStatus);
            Rect rect = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width), GUILayout.Height(18f));

            if (ConvaiSceneKnowledgeLinkModel.FindTargetByName(entry.Name, entry.Kind, _linkTargets) != null)
                Theme.Pill(rect, ConvaiActionsEditorStrings.KnownEntryOverridesTargetStatus, Theme.TextMuted);
        }

        /// <summary>
        ///     The status row, for the two states that have something to ask of the author: a dot, the
        ///     sentence, and the next move. A linked or text-only entry draws no row here at all.
        /// </summary>
        private void DrawKnownEntryLinkStatus(
            ConvaiActionConfigSource source, KnownEntryHandle entry, int index, ConvaiKnownEntryLinkState state)
        {
            if (state is ConvaiKnownEntryLinkState.Unlinked or ConvaiKnownEntryLinkState.AnsweredByTarget)
            {
                bool answered = state == ConvaiKnownEntryLinkState.AnsweredByTarget;
                GUIContent message = answered
                    ? ConvaiActionsEditorStrings.BuildKnownEntryAnsweredStatus(AnsweringTargetName(entry))
                    : ConvaiActionsEditorStrings.KnownEntryUnlinkedStatus;
                Color tint = answered ? Theme.Accent : Theme.StatusError;

                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect dotRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
                    Theme.StatusDot(dotRect, tint, !answered);
                    GUILayout.Space(2f);
                    GUILayout.Label(message, Theme.MutedWrapped);
                }
            }

            DrawKnownEntryLinkActions(source, entry, index, state);
        }

        private void DrawKnownEntryLinkActions(
            ConvaiActionConfigSource source, KnownEntryHandle entry, int index, ConvaiKnownEntryLinkState state)
        {
            string searchKey = BuildEntryStateKey(entry, index);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16f);

                if (state == ConvaiKnownEntryLinkState.AnsweredByTarget)
                {
                    ConvaiActionTarget answering = ConvaiSceneKnowledgeLinkModel.FindTargetByName(
                        entry.Name, entry.Kind, _linkTargets);
                    if (answering != null && DrawLinkActionButton(ConvaiActionsEditorStrings.KnownEntryLinkTargetButton, 78f))
                        AssignEntryReference(source, entry, answering.gameObject);
                }

                if (state == ConvaiKnownEntryLinkState.Unlinked &&
                    !string.IsNullOrWhiteSpace(entry.Name) &&
                    DrawLinkActionButton(ConvaiActionsEditorStrings.KnownEntryFindInSceneButton, 110f))
                    FindSceneObjectForEntry(source, entry, searchKey);

                GUILayout.FlexibleSpace();
            }

            // The result of a search the user just ran, shown where they ran it and dropped as soon
            // as they act on it.
            if (_linkSearchMessage != null && string.Equals(_linkSearchKey, searchKey, System.StringComparison.Ordinal))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16f);
                    GUILayout.Label(_linkSearchMessage, Theme.MutedWrapped);
                }
            }
        }

        private static bool DrawLinkActionButton(GUIContent label, float width)
        {
            Rect rect = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width), GUILayout.Height(20f));
            bool clicked = Theme.GhostButton(rect, label);
            GUILayout.Space(6f);
            return clicked;
        }

        /// <summary>
        ///     The two optional extras, folded away: other words that should match this entry, and the
        ///     exact spot to approach it from. Both have always been honoured by target resolution and
        ///     neither has ever been authorable on an entry.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Two labelled fields, nothing else. Both extras used to be sub-sections with a
        ///         heading, a <c>?</c> hover mark and a line of prose — eight rows of explanation
        ///         wrapped around two controls, redrawn in full for every entry in the list. Guidance
        ///         that repeats per entry is the defect: a label repeats invisibly because the eye
        ///         reads it as structure, but prose and the marks that promise prose are read as new
        ///         information, and a user betrayed by the same sentence four rows down learns to skip
        ///         the whole region.
        ///     </para>
        ///     <para>
        ///         So the explanation moved up to the one control that owns both fields — the
        ///         <c>Extras (optional)</c> row's tooltip, which cannot be bypassed since the fields
        ///         are behind it — and the example moved down inside the alias field itself, where it
        ///         costs no row and vanishes as soon as there is a real value. Nothing was deleted
        ///         except repetition.
        ///     </para>
        /// </remarks>
        private void DrawKnownEntryAdvanced(ConvaiActionConfigSource source, KnownEntryHandle entry, int index)
        {
            List<string> aliases = entry.Aliases;
            bool isCharacter = entry.Kind == ConvaiActionTargetKind.Character;
            string sectionId = "KnownEntry:" + BuildEntryStateKey(entry, index);
            bool expanded = ConvaiEditorSectionState.Get(SectionStateHostId, sectionId, false);

            // The section header opens a new region of the entry; the rows above it are the entry's
            // own form. Butted straight against the Scene Object row it read as one more row of that
            // form, and a folded one sat on the panel's bottom edge. The design system's row carries
            // no margin of its own — a card's first header must start at the top of the card — so the
            // breathing room belongs here, where the header follows something.
            GUILayout.Space(EntryExtrasGapAbove);

            int extras = (aliases?.Count ?? 0) + (entry.InteractionPoint != null ? 1 : 0);
            bool nowExpanded = Theme.SectionHeaderRow(
                ConvaiEditorGlyphs.Contract,
                ConvaiActionsEditorStrings.KnownEntryExtrasTitle,
                expanded,
                Theme.Accent,
                extras > 0 ? extras.ToString() : null);

            if (nowExpanded != expanded)
                ConvaiEditorSectionState.Set(SectionStateHostId, sectionId, nowExpanded);

            if (!nowExpanded)
            {
                GUILayout.Space(EntryExtrasGapBelow);
                return;
            }

            DrawKnownEntryAliases(source, entry, aliases, sectionId);
            DrawKnownEntryApproachPoint(source, entry, isCharacter);
            GUILayout.Space(EntryExtrasGapBelow);
        }

        /// <summary>
        ///     The alias rows, laid out on the same label column as Name and Description above them so
        ///     the extras read as more of the same form rather than as a panel of their own.
        /// </summary>
        /// <remarks>
        ///     Only the first row is labelled; the rest align under it, because a label repeated once
        ///     per row stops being a label and starts being noise. The empty case draws no "none yet"
        ///     line — the labelled Add button already says both that there are none and what to do
        ///     about it, and the example the line used to carry now waits inside the field it
        ///     describes.
        /// </remarks>
        private void DrawKnownEntryAliases(
            ConvaiActionConfigSource source, KnownEntryHandle entry, List<string> aliases, string sectionId)
        {
            int count = aliases?.Count ?? 0;
            int removeAt = -1;

            for (int i = 0; i < count; i++)
            {
                Rect row = EditorGUILayout.GetControlRect();
                Rect field = EditorGUI.PrefixLabel(
                    row, i == 0 ? ConvaiActionsEditorStrings.KnownEntryAliasesField : GUIContent.none);

                var textRect = new Rect(field.x, field.y, Mathf.Max(40f, field.width - 24f), field.height);

                EditorGUI.BeginChangeCheck();
                string alias = Theme.PlaceholderTextField(
                    textRect,
                    aliases[i],
                    ConvaiActionsEditorStrings.KnownEntryAliasPlaceholder,
                    sectionId + ":alias:" + i);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(source, entry.UndoLabel);
                    aliases[i] = alias;
                    MarkDirty(source);
                    MarkSceneKnowledgeCachesStale();
                }

                var removeRect = new Rect(field.xMax - 20f, field.y, 20f, field.height);
                if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.KnownEntryRemoveAliasButton))
                    removeAt = i;
            }

            if (removeAt >= 0)
            {
                Undo.RecordObject(source, entry.UndoLabel);
                aliases.RemoveAt(removeAt);
                MarkDirty(source);
                MarkSceneKnowledgeCachesStale();
            }

            Rect addRow = EditorGUILayout.GetControlRect(true, 20f);
            Rect addField = EditorGUI.PrefixLabel(
                addRow, count == 0 ? ConvaiActionsEditorStrings.KnownEntryAliasesField : GUIContent.none);
            var addRect = new Rect(addField.x, addField.y, 170f, 20f);
            if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.KnownEntryAddAliasButton))
            {
                Undo.RecordObject(source, entry.UndoLabel);
                aliases?.Add(string.Empty);
                MarkDirty(source);
                MarkSceneKnowledgeCachesStale();
            }
        }

        /// <summary>
        ///     Where the character ends up when it acts on this entry. Offered for characters as well as
        ///     objects on purpose: walking to a person means stopping in front of them at talking
        ///     distance, not arriving inside them.
        /// </summary>
        private void DrawKnownEntryApproachPoint(
            ConvaiActionConfigSource source, KnownEntryHandle entry, bool isCharacter)
        {
            EditorGUI.BeginChangeCheck();
            var point = (Transform)EditorGUILayout.ObjectField(
                isCharacter
                    ? ConvaiActionsEditorStrings.KnownEntryCharacterApproachField
                    : ConvaiActionsEditorStrings.KnownEntryObjectApproachField,
                entry.InteractionPoint,
                typeof(Transform),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(source, entry.UndoLabel);
                entry.InteractionPoint = point;
                MarkDirty(source);
                MarkSceneKnowledgeCachesStale();
            }
        }

        // ── Commands ───────────────────────────────────────────────────────────

        /// <summary>
        ///     Links (or unlinks) an entry. Naming an unnamed entry after the object it just received
        ///     is part of the same step: an entry created by dropping an object should not then have to
        ///     be named by hand.
        /// </summary>
        private void AssignEntryReference(
            ConvaiActionConfigSource source, KnownEntryHandle entry, GameObject reference)
        {
            Undo.RecordObject(source, entry.UndoLabel);
            entry.Reference = reference;

            if (reference != null)
            {
                entry.TextOnly = false;
                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = reference.name;
            }

            ClearLinkSearchMessage();
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>
        ///     Declares that nothing in the scene answers to this entry. Turning it on drops any object
        ///     the entry held, because the two states cannot both be true and a half-applied one is how
        ///     a user ends up not knowing which is in effect.
        /// </summary>
        private void SetEntryTextOnly(ConvaiActionConfigSource source, KnownEntryHandle entry, bool textOnly)
        {
            Undo.RecordObject(source, entry.UndoLabel);
            entry.TextOnly = textOnly;
            if (textOnly)
                entry.Reference = null;

            ClearLinkSearchMessage();
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        private void UseObjectNameForEntry(ConvaiActionConfigSource source, KnownEntryHandle entry)
        {
            GameObject reference = entry.Reference;
            if (reference == null)
                return;

            Undo.RecordObject(source, entry.UndoLabel);
            entry.Name = reference.name;
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();

            // The Name field is a control away from this button, so it may well be the focused one and
            // holding the text the user was editing. Without this it keeps drawing that stale text and
            // the rename looks like it did nothing.
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>
        ///     Offered beside the Name field, because renaming the entry is what it does — it used to
        ///     sit two rows lower among the link fixes, where it read as being about the scene object.
        ///     Drawn only when the entry's name and its object's have actually drifted apart.
        /// </summary>
        private void DrawUseObjectNameButton(ConvaiActionConfigSource source, KnownEntryHandle entry)
        {
            if (!ConvaiSceneKnowledgeLinkModel.NameDiffersFromObject(entry.Name, entry.Reference))
                return;

            // The Name field expands to fill the row, so without this the button is pressed flat
            // against it — a control touching the field it acts on reads as part of the field.
            GUILayout.Space(10f);

            const float width = 130f;
            Rect rect = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width), GUILayout.Height(18f));
            if (Theme.GhostButton(rect, ConvaiActionsEditorStrings.KnownEntryUseObjectNameButton))
                UseObjectNameForEntry(source, entry);

            GUILayout.Space(10f);
        }

        /// <summary>
        ///     Looks for a scene object that answers to this entry's name: one match links straight
        ///     away, several offer a menu, none says so in plain words rather than doing nothing.
        /// </summary>
        private void FindSceneObjectForEntry(
            ConvaiActionConfigSource source, KnownEntryHandle entry, string searchKey)
        {
            GameObject[] sceneObjects = ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Include);
            List<GameObject> matches = ConvaiSceneKnowledgeLinkModel.FindObjectsByName(entry.Name, sceneObjects);

            if (matches.Count == 1)
            {
                AssignEntryReference(source, entry, matches[0]);
                return;
            }

            _linkSearchKey = searchKey;
            if (matches.Count == 0)
            {
                _linkSearchMessage = ConvaiActionsEditorStrings.BuildKnownEntryNoMatchFound(entry.Name);
                Repaint();
                return;
            }

            _linkSearchMessage = ConvaiActionsEditorStrings.BuildKnownEntryManyMatchesFound(matches.Count);

            var menu = new GenericMenu();
            for (int i = 0; i < matches.Count; i++)
            {
                GameObject candidate = matches[i];
                menu.AddItem(
                    new GUIContent(ConvaiObjectPathLabel(candidate)),
                    false,
                    () => AssignEntryReference(source, entry, candidate));
            }

            menu.ShowAsContext();
        }

        private void ClearLinkSearchMessage()
        {
            _linkSearchKey = null;
            _linkSearchMessage = null;
        }

        /// <summary>Hierarchy path of a candidate, so two objects with one name can be told apart.</summary>
        private static string ConvaiObjectPathLabel(GameObject gameObject)
        {
            if (gameObject == null)
                return string.Empty;

            string path = gameObject.name;
            for (Transform parent = gameObject.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }

        private string AnsweringTargetName(KnownEntryHandle entry)
        {
            ConvaiActionTarget target = ConvaiSceneKnowledgeLinkModel.FindTargetByName(
                entry.Name, entry.Kind, _linkTargets);
            return target == null ? string.Empty : target.gameObject.name;
        }

        /// <summary>Stable-enough key for per-entry editor state (fold, last search result).</summary>
        private static string BuildEntryStateKey(KnownEntryHandle entry, int index) =>
            string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Kind + "#" + index
                : entry.Kind + ":" + entry.Name.Trim();
    }
}
