using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.UI;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Scene Knowledge mode of the Actions Editor window: authoring for what the picked
    ///     Convai Character knows about the scene —
    ///     <c>ConvaiActionConfigSource</c>'s known objects, known characters, and initial attention —
    ///     plus an on-demand scene scan for <see cref="ConvaiActionTarget" /> components, a
    ///     drag-and-drop intake, and a read-only "Sent To Convai" preview rendered from the same
    ///     <see cref="ConvaiActionConfigSource.BuildActionConfig()" /> call the runtime makes at
    ///     connect, so the preview can never drift from reality.
    /// </summary>
    /// <remarks>
    ///     Mutations follow the window's established idiom (see the class remarks in the main file):
    ///     <c>Undo.RecordObject</c> → direct field mutation / <c>Replace*</c> seam →
    ///     <c>MarkDirty</c>. The single exception is the initial-attention string, which has no
    ///     tooling seam on the component; it is written through a one-shot
    ///     <see cref="SerializedObject" />, which registers Undo and dirties the scene itself.
    /// </remarks>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const double PreviewDebounceSeconds = 0.5d;

        /// <summary>
        ///     Height a Sent To Convai delivery group takes once it is long enough to scroll. Fixed
        ///     rather than a maximum: <c>GUILayout.MaxHeight</c> on a scroll view inside a vertical
        ///     layout resolves to the *minimum* the parent will grant, which squashed both groups to
        ///     a two-row slit.
        /// </summary>
        private const float PreviewGroupScrollHeight = 260f;

        /// <summary>
        ///     Rows a delivery group may hold before it starts scrolling. Below this it draws at its
        ///     natural height, so a character that knows three things does not get a tall box with a
        ///     scrollbar and a lot of nothing in it.
        /// </summary>
        private const int PreviewGroupScrollThreshold = 9;

        private const string KnownObjectsGlyph = Glyphs.Content;
        private const string KnownCharactersGlyph = Glyphs.Identity;
        private const string InitialAttentionGlyph = Glyphs.Visibility;
        private const string ScanGlyph = Glyphs.Discovery;
        private const string DropGlyph = Glyphs.Placement;
        private const string SentGlyph = Glyphs.Motion;

        /// <summary>
        ///     Namespace for this pane's persisted section expansion. Its own host id rather than the
        ///     window's, so a section here can never collide with a same-named one in another mode.
        /// </summary>
        private const string KnowledgeSectionHost = nameof(ConvaiActionsEditorWindow) + ".SceneKnowledge";

        private const string KnownObjectsSectionId = "KnownObjects";
        private const string KnownCharactersSectionId = "KnownCharacters";
        private const string InitialAttentionSectionId = "InitialAttention";
        private const string ScanSectionId = "FindTargets";
        private const string DropSectionId = "AddFromScene";
        private const string SentSectionId = "SentToConvai";

        private Vector2 _knowledgeScroll;
        private string _pendingKnowledgeNameFocus;

        // Header summaries, so a folded section still reports what it holds. Cached rather than built
        // per repaint: these are strings on a surface that repaints on every mouse move over it, and
        // every edit that can change one already routes through MarkSceneKnowledgeCachesStale.
        private bool _knowledgeSummariesDirty = true;
        private string _knownObjectsSummary;
        private string _knownCharactersSummary;
        private string _initialAttentionSummary;
        private string _scanSummary;
        private string _sentSummary;

        // Sent-to-Convai preview cache: rebuilt only when marked stale (edits, undo/redo, mode or
        // character switches), debounced so per-keystroke edits do not re-run the runtime builder
        // (which may log its own authoring warnings) on every repaint.
        private ConvaiActionConfigSource _previewSource;
        private bool _previewDirty = true;
        private double _previewBuiltAt;
        private bool _previewOmitted;

        /// <summary>What travels in the connect payload, as drawable rows.</summary>
        private readonly List<PreviewRow> _previewConnectRows = new();

        /// <summary>Names the follow-up sync adds once the conversation starts.</summary>
        private readonly List<string> _previewSceneTargets = new();

        private int _previewConnectCount;
        private Vector2 _previewConnectScroll;
        private Vector2 _previewSceneTargetScroll;

        /// <summary>What a <see cref="PreviewRow" /> is, which is what decides how it is drawn.</summary>
        private enum PreviewRowKind
        {
            /// <summary>An "Objects (18)" / "Characters (1)" divider inside a group.</summary>
            SubHeading = 0,

            /// <summary>One named thing, with the text the backend will receive for it.</summary>
            Entry = 1,

            /// <summary>A trailing remark about the group, such as the starting attention.</summary>
            Note = 2
        }

        /// <summary>
        ///     One line of the Sent To Convai preview, kept as data rather than as pre-formatted text.
        /// </summary>
        /// <remarks>
        ///     The preview used to be a <c>List&lt;string&gt;</c> whose indentation was spaces inside
        ///     the string and whose every line drew in the same style, so a name and its description
        ///     were indistinguishable from each other and from a heading. Keeping the parts apart is
        ///     what lets the drawing decide the typography.
        /// </remarks>
        private readonly struct PreviewRow
        {
            private PreviewRow(PreviewRowKind kind, string text, string detail)
            {
                Kind = kind;
                Text = text;
                Detail = detail;
            }

            internal PreviewRowKind Kind { get; }
            internal string Text { get; }
            internal string Detail { get; }

            internal static PreviewRow SubHeading(string text) =>
                new(PreviewRowKind.SubHeading, text, null);

            internal static PreviewRow Note(string text) =>
                new(PreviewRowKind.Note, text, null);

            internal static PreviewRow Entry(string name, string details) =>
                new(
                    PreviewRowKind.Entry,
                    string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim(),
                    string.IsNullOrWhiteSpace(details) ? null : details.Trim());
        }

        // Scene-scan cache: refreshed on its own whenever the open scenes change, and on demand from
        // the Scan Again button; statuses are re-derived (without re-finding scene objects) when the
        // authored entry lists change.
        // Serialized so a scan survives the assembly reload every script compile causes. The rows
        // hold scene components, and Unity re-points those at the reloaded objects for us; what it
        // cannot do is re-derive the classifications, so OnEnable asks for that (see
        // ResetSceneKnowledgeDerivedState). Without this the user re-scanned after every compile.
        [SerializeField] private List<SceneScanRow> _scanRows;

        // Serialized alongside the rows because an empty list is ambiguous on its own: "scanned, and
        // this scene has no targets" and "never scanned" want different words on the header.
        [SerializeField] private bool _hasScannedScene;

        private int _scanNotKnownCount;
        private int _scanByEntryCount;
        private int _scanAutomaticCount;

        // Layout-stable copies of the tallies above. Everything whose *control count* depends on a
        // tally reads these — the "Add all not known" button most of all. Adding an entry re-derives
        // the live tallies immediately, and an Add clicked on a row happens mid-pass: a button that
        // consulted the live count would vanish between a frame's Layout and Repaint passes and take
        // the pane's control count with it.
        private int _scanDrawTotal;
        private int _scanDrawByEntry;
        private int _scanDrawAutomatic;
        private int _scanDrawNotKnown;

        /// <summary>
        ///     Whether the surviving scan rows still describe the current entry lists. Set by
        ///     <see cref="ResetSceneKnowledgeDerivedState" /> after an assembly reload and cleared
        ///     once the classifications have been re-derived.
        /// </summary>
        private bool _scanStatusesStale;

        /// <summary>
        ///     Whether the open scenes may have gained or lost a <see cref="ConvaiActionTarget" />
        ///     since the cached rows were found, so the next Layout pass has to search again.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This pane answers "what will this Convai Character know?", and the answer has two
        ///         halves: the entries authored above, which travel in the connect payload, and the
        ///         Convai Action Target components in the scene, which introduce themselves once the
        ///         conversation starts. The second half used to exist only if the user found and
        ///         pressed Scan Scene — so Sent To Convai showed the connect payload and called it the
        ///         whole story, while the Live tab showed the backend holding targets the preview had
        ///         never mentioned. A component that registers itself is not something the user should
        ///         have to ask about.
        ///     </para>
        ///     <para>
        ///         Set from <see cref="EditorApplication.hierarchyChanged" /> rather than polled: the
        ///         search itself is a scene-wide find, which is far too expensive to run per repaint,
        ///         and the hierarchy event covers every way a target can appear or disappear —
        ///         add, delete, duplicate, prefab instantiate, scene open and scene close.
        ///     </para>
        /// </remarks>
        private bool _sceneScanStale = true;

        /// <summary>
        ///     Reused for every scan row's clickable name. One instance rather than one per row per
        ///     repaint: this list is as long as the scene has targets, and it redraws on mouse move.
        /// </summary>
        private static readonly GUIContent ScanRowName = new();

        /// <summary>One cached scan result row: the found target and its classification.</summary>
        /// <remarks>
        ///     Serializable, with fields rather than get-only properties, because Unity's assembly
        ///     reload serialization cannot carry auto-properties — a readonly struct here is what
        ///     made a scan disappear on every compile.
        /// </remarks>
        [System.Serializable]
        private struct SceneScanRow
        {
            [SerializeField] private ConvaiActionTarget _target;
            [SerializeField] private string _displayName;
            [SerializeField] private ConvaiActionTargetKind _kind;
            [SerializeField] private ConvaiSceneKnowledgeScanStatus _status;

            internal SceneScanRow(
                ConvaiActionTarget target,
                string displayName,
                ConvaiActionTargetKind kind,
                ConvaiSceneKnowledgeScanStatus status)
            {
                _target = target;
                _displayName = displayName;
                _kind = kind;
                _status = status;
            }

            internal readonly ConvaiActionTarget Target => _target;
            internal readonly string DisplayName => _displayName;
            internal readonly ConvaiActionTargetKind Kind => _kind;
            internal readonly ConvaiSceneKnowledgeScanStatus Status => _status;
        }

        /// <summary>
        ///     Throws away everything this pane derives, so the next frame rebuilds all of it.
        ///     Called from <c>OnEnable</c>, which Unity runs after every assembly reload.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An assembly reload does not take this window's state with it evenly, and that
        ///         unevenness was a bug users hit on every script compile. Unity restores the plain
        ///         value fields — so <see cref="_scanSummary" /> came back still saying
        ///         <c>"39 found · all known"</c> and <see cref="_sentSummary" /> still saying
        ///         <c>"41 entries"</c> — while the collections behind them
        ///         (<see cref="_scanRows" />, which holds live scene components, and the preview row
        ///         lists) came back empty. Worse, the two <c>= true</c> dirty flags came back
        ///         <b>false</b>, so nothing ever rebuilt, and the object reference this pane watches
        ///         for a character change came back intact, so the reset that switching characters
        ///         performs never fired either. The result was permanent: a header counting
        ///         thirty-nine targets above a body saying none were found, until the user happened
        ///         to switch character.
        ///     </para>
        ///     <para>
        ///         A scan cannot survive a reload — its rows point at scene components — so the
        ///         honest state afterwards is "not scanned yet", which also tells the user to scan
        ///         again rather than silently showing them nothing.
        ///     </para>
        /// </remarks>
        private void ResetSceneKnowledgeDerivedState()
        {
            // The rows themselves are kept — they are serialized and Unity has re-pointed them at
            // the reloaded scene objects. Only what was *computed* from them is thrown away.
            _scanStatusesStale = true;
            _sceneScanStale = true;
            _scanNotKnownCount = 0;
            _scanSummary = null;

            // Deliberately not _previewSource: nulling it makes the next frame take the
            // character-changed branch, which clears the scan this reset just went to the trouble
            // of keeping. The dirty flags below are what force the rebuild.
            _previewConnectRows.Clear();
            _previewSceneTargets.Clear();
            _previewConnectCount = 0;
            _previewOmitted = false;
            _previewBuiltAt = 0d;
            _previewDirty = true;
            _sentSummary = null;

            _knowledgeSummariesDirty = true;
        }

        /// <summary>
        ///     Marks the cached scan rows as possibly out of date with the open scenes. Subscribed to
        ///     <see cref="EditorApplication.hierarchyChanged" /> for the whole life of the window, so
        ///     the Find Targets list and the Sent To Convai preview are never reporting a scene that
        ///     no longer exists.
        /// </summary>
        private void MarkSceneScanStale()
        {
            _sceneScanStale = true;
            Repaint();
        }

        /// <summary>
        ///     Invalidates the caches derived from the authored scene-knowledge lists: the
        ///     sent-to-Convai preview and (when a scan has run) the scan-row classifications.
        /// </summary>
        private void MarkSceneKnowledgeCachesStale()
        {
            _previewDirty = true;
            _knowledgeSummariesDirty = true;
            MarkSceneKnowledgeLinkTargetsStale();
            RefreshScanStatuses();
            ConvaiActionSetupReport.Invalidate();
        }

        private void DrawSceneKnowledgeMode(ConvaiActionConfigSource source)
        {
            if (_previewSource != source)
            {
                _previewSource = source;
                _previewDirty = true;
                _previewBuiltAt = 0d;
                _scanRows = null;
                _hasScannedScene = false;
                _sceneScanStale = true;
                _scanNotKnownCount = 0;
                _knowledgeSummariesDirty = true;
            }

            EnsureSceneScan(source);
            RefreshSceneKnowledgeSummaries(source);

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _knowledgeScroll = EditorGUILayout.BeginScrollView(_knowledgeScroll, GUILayout.ExpandHeight(true));
                using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
                {
                    float previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 120f;

                    Theme.Paragraph(ConvaiActionsEditorStrings.SceneKnowledgeIntro, Theme.MutedWrapped);
                    GUILayout.Space(10f);

                    bool playing = EditorApplication.isPlaying;
                    if (playing)
                    {
                        Theme.BeginPanel(Theme.StatusWarn);
                        Theme.Paragraph(ConvaiActionsEditorStrings.PlayModeEditingHint, Theme.BodyWrapped);
                        Theme.EndPanel(10f);
                    }

                    // Each card owns its own disabled scope rather than the three edit cards sharing
                    // one: a section header is navigation, not editing, so folding a section away
                    // has to keep working while Play Mode has the editing itself switched off.
                    DrawKnownObjectsCard(source, playing);
                    DrawKnownCharactersCard(source, playing);
                    DrawInitialAttentionCard(source, playing);
                    DrawScanCard(source, playing);
                    DrawDropCard(source, playing);
                    DrawSentToConvaiCard(source);

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        ///     Rebuilds the cached header summaries when the authored lists have moved under them.
        /// </summary>
        private void RefreshSceneKnowledgeSummaries(ConvaiActionConfigSource source)
        {
            if (!_knowledgeSummariesDirty || source == null)
                return;

            _knowledgeSummariesDirty = false;
            _knownObjectsSummary = ConvaiActionsEditorStrings.BuildKnownObjectsSummary(source.Objects?.Count ?? 0);
            _knownCharactersSummary =
                ConvaiActionsEditorStrings.BuildKnownCharactersSummary(source.Characters?.Count ?? 0);
            _initialAttentionSummary =
                ConvaiActionsEditorStrings.BuildInitialAttentionSummary(source.InitialAttentionObject);
            _scanSummary = ConvaiActionsEditorStrings.BuildScanSummary(
                _hasScannedScene, _scanRows?.Count ?? 0, _scanNotKnownCount);
        }

        /// <summary>
        ///     Whether a Scene Knowledge section is currently open. Expansion is persisted in
        ///     <see cref="ConvaiEditorSectionState" /> — the SDK-wide store — rather than in a window
        ///     field, so how a user prefers this pane laid out survives closing the window and
        ///     restarting the editor.
        /// </summary>
        private static bool IsKnowledgeSectionExpanded(string sectionId) =>
            ConvaiEditorSectionState.Get(KnowledgeSectionHost, sectionId, true);

        /// <summary>
        ///     Opens a card, draws its collapsible header and returns whether the body should be drawn.
        ///     Every Scene Knowledge card is one of these, so all six fold the same way and remember it
        ///     the same way. The caller closes the card with <see cref="Theme.EndCard" /> either way —
        ///     a collapsed section is still a card, just an empty one.
        /// </summary>
        /// <remarks>
        ///     The state <em>this pass</em> draws with is the one it started with, not the one the click
        ///     just produced. A click arrives after that event's own layout pass has already been built
        ///     from the old state, so acting on the new one immediately draws a body the layout has no
        ///     room for — the controls-in-a-group mismatch IMGUI aborts the pass over. The new state is
        ///     stored and takes effect on the next pass, which is a frame away and invisible.
        /// </remarks>
        private bool BeginKnowledgeSection(
            string glyph, GUIContent title, string sectionId, string summary, out Rect card)
        {
            card = Theme.BeginCard();

            bool wasExpanded = IsKnowledgeSectionExpanded(sectionId);
            bool toggled = Theme.SectionHeaderRow(glyph, title, wasExpanded, Theme.Accent, summary);
            if (toggled != wasExpanded)
            {
                ConvaiEditorSectionState.Set(KnowledgeSectionHost, sectionId, toggled);
                Repaint();
            }

            return wasExpanded;
        }

        /// <summary>
        ///     Opens a section the user is about to be shown something inside of — a drop that added
        ///     entries to a folded list, which would otherwise land silently.
        /// </summary>
        private void ExpandKnowledgeSection(string sectionId)
        {
            if (IsKnowledgeSectionExpanded(sectionId))
                return;

            ConvaiEditorSectionState.Set(KnowledgeSectionHost, sectionId, true);
            Repaint();
        }

        #region Known Objects / Known Characters

        private void DrawKnownObjectsCard(ConvaiActionConfigSource source, bool playing)
        {
            bool expanded = BeginKnowledgeSection(
                KnownObjectsGlyph, ConvaiActionsEditorStrings.KnownObjectsTitle,
                KnownObjectsSectionId, _knownObjectsSummary, out Rect card);

            if (Event.current.type == EventType.Repaint)
                _knownObjectsCardRect = card;

            if (expanded)
            {
                // Said once, on the section that owns the question, rather than on each of the rows
                // it applies to: what an entry is *for*, now that an object can also introduce
                // itself without one.
                Theme.Paragraph(ConvaiActionsEditorStrings.KnownListTargetNote, Theme.MutedWrapped);
                GUILayout.Space(8f);

                using (new EditorGUI.DisabledScope(playing))
                {
                    IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
                    for (int i = 0; i < objects.Count; i++)
                    {
                        ConvaiActionObjectDefinition entry = objects[i];
                        if (entry == null)
                            continue;

                        Theme.BeginPanel(null);
                        if (DrawKnownObjectRow(source, entry, i, objects.Count))
                        {
                            Theme.EndPanel();
                            break; // The list shape changed; redraw next frame.
                        }

                        Theme.EndPanel();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect addRect = GUILayoutUtility.GetRect(
                            130f, 24f, GUILayout.Width(130f), GUILayout.Height(24f));
                        if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.AddKnownObjectButton))
                            AddKnownObject(source);

                        GUILayout.Space(8f);
                        DrawInlineRowHint(ConvaiActionsEditorStrings.KnownListDropHint, 24f);
                    }
                }
            }

            Theme.EndCard();
            HandleKnownListDrop(_knownObjectsCardRect, source, false, playing);
        }

        /// <summary>Draws one Known Object entry. Returns true when the entry was reordered/removed (list stale).</summary>
        private bool DrawKnownObjectRow(ConvaiActionConfigSource source, ConvaiActionObjectDefinition entry, int index, int count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newName = DrawKnownEntryNameField(
                    ConvaiActionsEditorStrings.KnownObjectNameField,
                    ConvaiActionsEditorStrings.KnownObjectNamePlaceholder,
                    entry.Name,
                    "KnownObject.Name:" + index);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(source, "Edit Known Object");
                    entry.Name = newName;
                    MarkDirty(source);
                    MarkSceneKnowledgeCachesStale();
                }

                DrawUseObjectNameButton(source, new KnownEntryHandle(entry));

                if (DrawKnownEntryRowButtons(index, count, out int moveDelta, out bool remove))
                {
                    if (remove)
                        RemoveKnownObject(source, index);
                    else
                        MoveKnownObject(source, index, moveDelta);
                    return true;
                }
            }

            DrawKnownEntryNameError(entry.Name);

            EditorGUI.BeginChangeCheck();
            string newDescription = Theme.ProseField(
                ConvaiActionsEditorStrings.KnownObjectDescriptionField,
                entry.Description,
                "KnownObject.Description:" + index,
                ConvaiActionsEditorStrings.KnownObjectDescriptionPlaceholder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(source, "Edit Known Object");
                entry.Description = newDescription;
                MarkDirty(source);
                MarkSceneKnowledgeCachesStale();
            }

            DrawKnownEntryLink(source, new KnownEntryHandle(entry), index);
            return false;
        }

        private void DrawKnownCharactersCard(ConvaiActionConfigSource source, bool playing)
        {
            bool expanded = BeginKnowledgeSection(
                KnownCharactersGlyph, ConvaiActionsEditorStrings.KnownCharactersTitle,
                KnownCharactersSectionId, _knownCharactersSummary, out Rect card);

            if (Event.current.type == EventType.Repaint)
                _knownCharactersCardRect = card;

            if (expanded)
            {
                using (new EditorGUI.DisabledScope(playing))
                {
                    IReadOnlyList<ConvaiActionCharacterDefinition> characters = source.Characters;
                    for (int i = 0; i < characters.Count; i++)
                    {
                        ConvaiActionCharacterDefinition entry = characters[i];
                        if (entry == null)
                            continue;

                        Theme.BeginPanel(null);
                        if (DrawKnownCharacterRow(source, entry, i, characters.Count))
                        {
                            Theme.EndPanel();
                            break;
                        }

                        Theme.EndPanel();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect addRect = GUILayoutUtility.GetRect(
                            130f, 24f, GUILayout.Width(130f), GUILayout.Height(24f));
                        if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.AddKnownCharacterButton))
                            AddKnownCharacter(source);

                        GUILayout.Space(8f);
                        DrawInlineRowHint(ConvaiActionsEditorStrings.KnownListDropHint, 24f);
                    }
                }
            }

            Theme.EndCard();
            HandleKnownListDrop(_knownCharactersCardRect, source, true, playing);
        }

        /// <summary>Draws one Known Character entry. Returns true when the entry was reordered/removed (list stale).</summary>
        private bool DrawKnownCharacterRow(
            ConvaiActionConfigSource source, ConvaiActionCharacterDefinition entry, int index, int count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newName = DrawKnownEntryNameField(
                    ConvaiActionsEditorStrings.KnownCharacterNameField,
                    ConvaiActionsEditorStrings.KnownCharacterNamePlaceholder,
                    entry.Name,
                    "KnownCharacter.Name:" + index);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(source, "Edit Known Character");
                    entry.Name = newName;
                    MarkDirty(source);
                    MarkSceneKnowledgeCachesStale();
                }

                DrawUseObjectNameButton(source, new KnownEntryHandle(entry));

                if (DrawKnownEntryRowButtons(index, count, out int moveDelta, out bool remove))
                {
                    if (remove)
                        RemoveKnownCharacter(source, index);
                    else
                        MoveKnownCharacter(source, index, moveDelta);
                    return true;
                }
            }

            DrawKnownEntryNameError(entry.Name);

            EditorGUI.BeginChangeCheck();
            string newBio = Theme.ProseField(
                ConvaiActionsEditorStrings.KnownCharacterBioField,
                entry.Bio,
                "KnownCharacter.Bio:" + index,
                ConvaiActionsEditorStrings.KnownCharacterBioPlaceholder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(source, "Edit Known Character");
                entry.Bio = newBio;
                MarkDirty(source);
                MarkSceneKnowledgeCachesStale();
            }

            DrawKnownEntryLink(source, new KnownEntryHandle(entry), index);
            return false;
        }

        /// <summary>
        ///     The shared reorder/remove icon trio on an entry's name row. Returns true when a
        ///     structural action was requested, with <paramref name="moveDelta" /> (±1) or
        ///     <paramref name="remove" /> describing which.
        /// </summary>
        private static bool DrawKnownEntryRowButtons(int index, int count, out int moveDelta, out bool remove)
        {
            moveDelta = 0;
            remove = false;

            using (new EditorGUI.DisabledScope(index <= 0))
            {
                Rect upRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                if (Theme.IconButton(upRect, ConvaiActionsEditorStrings.MoveActionUpButton))
                {
                    moveDelta = -1;
                    return true;
                }
            }

            using (new EditorGUI.DisabledScope(index >= count - 1))
            {
                Rect downRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                if (Theme.IconButton(downRect, ConvaiActionsEditorStrings.MoveActionDownButton))
                {
                    moveDelta = 1;
                    return true;
                }
            }

            Rect removeRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
            if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.RemoveKnownEntryButton))
            {
                remove = true;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     The entry's Name field, with an italic example inside it while it is empty. Laid out by
        ///     rect rather than through <c>EditorGUILayout.TextField</c> because the example is painted
        ///     over the field, which needs the field's rect.
        /// </summary>
        private string DrawKnownEntryNameField(
            GUIContent label, GUIContent placeholder, string value, string key)
        {
            Rect row = GUILayoutUtility.GetRect(
                0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            Rect field = EditorGUI.PrefixLabel(row, label);
            string result = Theme.PlaceholderTextField(field, value, placeholder, key);
            if (_pendingKnowledgeNameFocus == key && Event.current.type == EventType.Repaint)
            {
                _knowledgeScroll.y = Mathf.Max(0f, row.y - 28f);
                EditorGUI.FocusTextInControl(key);
                _pendingKnowledgeNameFocus = null;
            }

            return result;
        }

        /// <summary>Inline error under an entry whose name is blank.</summary>
        private static void DrawKnownEntryNameError(string currentName)
        {
            if (!string.IsNullOrWhiteSpace(currentName))
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect dotRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
                Theme.StatusDot(dotRect, Theme.StatusError, true);
                GUILayout.Space(2f);
                GUILayout.Label(ConvaiActionsEditorStrings.KnownEntryNameMissing, Theme.BodyWrapped);
            }
        }

        private void AddKnownObject(ConvaiActionConfigSource source)
        {
            Undo.RecordObject(source, "Add Known Object");
            var list = new List<ConvaiActionObjectDefinition>(source.Objects)
            {
                new() { Name = string.Empty, Description = string.Empty }
            };
            source.ReplaceObjects(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        private void AddKnownCharacter(ConvaiActionConfigSource source)
        {
            Undo.RecordObject(source, "Add Known Character");
            var list = new List<ConvaiActionCharacterDefinition>(source.Characters)
            {
                new() { Name = string.Empty, Bio = string.Empty }
            };
            source.ReplaceCharacters(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>
        ///     Starts the target task from the action that needs it. A specific requirement is one
        ///     click; "object or character" asks the only decision the editor cannot make for the
        ///     author, then lands on a new focused Name field rather than an anonymous empty row.
        /// </summary>
        private void BeginTargetSetup(
            ConvaiActionConfigSource source,
            ConvaiActionTargetRequirement requirement)
        {
            if (source == null)
                return;

            if (requirement == ConvaiActionTargetRequirement.Either)
            {
                var menu = new GenericMenu();
                menu.AddItem(ConvaiActionsEditorStrings.AddKnownObjectButton, false,
                    () => AddTargetSetupEntry(source, false));
                menu.AddItem(ConvaiActionsEditorStrings.AddKnownCharacterButton, false,
                    () => AddTargetSetupEntry(source, true));
                menu.ShowAsContext();
                return;
            }

            AddTargetSetupEntry(source, requirement == ConvaiActionTargetRequirement.Character);
        }

        private void AddTargetSetupEntry(ConvaiActionConfigSource source, bool character)
        {
            int index = character ? source.Characters.Count : source.Objects.Count;
            if (character)
                AddKnownCharacter(source);
            else
                AddKnownObject(source);

            string sectionId = character ? KnownCharactersSectionId : KnownObjectsSectionId;
            ExpandKnowledgeSection(sectionId);
            _pendingKnowledgeNameFocus = character
                ? "KnownCharacter.Name:" + index
                : "KnownObject.Name:" + index;
            SetMode(ConvaiActionsEditorMode.SceneKnowledge);
        }

        private void RemoveKnownObject(ConvaiActionConfigSource source, int index)
        {
            var list = new List<ConvaiActionObjectDefinition>(source.Objects);
            if (index < 0 || index >= list.Count)
                return;

            Undo.RecordObject(source, "Remove Known Object");
            list.RemoveAt(index);
            source.ReplaceObjects(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        private void RemoveKnownCharacter(ConvaiActionConfigSource source, int index)
        {
            var list = new List<ConvaiActionCharacterDefinition>(source.Characters);
            if (index < 0 || index >= list.Count)
                return;

            Undo.RecordObject(source, "Remove Known Character");
            list.RemoveAt(index);
            source.ReplaceCharacters(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        private void MoveKnownObject(ConvaiActionConfigSource source, int index, int delta)
        {
            var list = new List<ConvaiActionObjectDefinition>(source.Objects);
            int other = index + delta;
            if (index < 0 || index >= list.Count || other < 0 || other >= list.Count)
                return;

            Undo.RecordObject(source, "Reorder Known Objects");
            (list[index], list[other]) = (list[other], list[index]);
            source.ReplaceObjects(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        private void MoveKnownCharacter(ConvaiActionConfigSource source, int index, int delta)
        {
            var list = new List<ConvaiActionCharacterDefinition>(source.Characters);
            int other = index + delta;
            if (index < 0 || index >= list.Count || other < 0 || other >= list.Count)
                return;

            Undo.RecordObject(source, "Reorder Known Characters");
            (list[index], list[other]) = (list[other], list[index]);
            source.ReplaceCharacters(list);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        #endregion

        #region Initial Attention

        private void DrawInitialAttentionCard(ConvaiActionConfigSource source, bool playing)
        {
            bool expanded = BeginKnowledgeSection(
                InitialAttentionGlyph, ConvaiActionsEditorStrings.InitialAttentionTitle,
                InitialAttentionSectionId, _initialAttentionSummary, out _);

            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            using (new EditorGUI.DisabledScope(playing))
            {
                string stored = source.InitialAttentionObject;
                ConvaiInitialAttentionStatus status =
                    ConvaiActionsSceneKnowledgeModel.ValidateInitialAttention(stored, source.Objects);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(
                        ConvaiActionsEditorStrings.InitialAttentionField,
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    GUIContent choice = status == ConvaiInitialAttentionStatus.NotSet
                        ? ConvaiActionsEditorStrings.BuildInitialAttentionChoice(
                            ConvaiActionsEditorStrings.InitialAttentionNoneChoice.text)
                        : ConvaiActionsEditorStrings.BuildInitialAttentionChoice(stored.Trim());
                    Rect choiceRect = GUILayoutUtility.GetRect(
                        220f, 22f, GUILayout.Width(220f), GUILayout.Height(22f));
                    if (Theme.GhostButton(choiceRect, choice))
                        ShowInitialAttentionMenu(source, status);
                    GUILayout.FlexibleSpace();
                }

                if (status == ConvaiInitialAttentionStatus.Unknown)
                {
                    GUILayout.Space(6f);
                    Theme.BeginPanel(Theme.StatusWarn);
                    Theme.Paragraph(
                        ConvaiActionsEditorStrings.BuildInitialAttentionUnknown(stored.Trim()), Theme.BodyWrapped);
                    Theme.EndPanel(0f);
                }

                GUILayout.Space(6f);
                Theme.Paragraph(ConvaiActionsEditorStrings.InitialAttentionExplainer, Theme.MutedWrapped);
            }

            Theme.EndCard();
        }

        private void ShowInitialAttentionMenu(ConvaiActionConfigSource source, ConvaiInitialAttentionStatus status)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(ConvaiActionsEditorStrings.InitialAttentionNoneChoice.text),
                status == ConvaiInitialAttentionStatus.NotSet,
                () => WriteInitialAttention(source, string.Empty));

            IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
            string stored = source.InitialAttentionObject;
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < objects.Count; i++)
            {
                string name = objects[i]?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string trimmed = name.Trim();
                if (!seen.Add(trimmed))
                    continue;

                bool isCurrent = ConvaiActionsSceneKnowledgeModel.NamesMatch(stored, trimmed);
                menu.AddItem(new GUIContent(trimmed), isCurrent, () => WriteInitialAttention(source, trimmed));
            }

            menu.ShowAsContext();
        }

        /// <summary>
        ///     Writes the initial-attention name through a one-shot <see cref="SerializedObject" />:
        ///     the component exposes no tooling seam for this field, and
        ///     <see cref="SerializedObject.ApplyModifiedProperties" /> registers Undo and dirties the
        ///     scene on its own. One-shot (not cached) because this runs only from a menu click,
        ///     never per repaint.
        /// </summary>
        private void WriteInitialAttention(ConvaiActionConfigSource source, string value)
        {
            if (source == null)
                return;

            using (var serialized = new SerializedObject(source))
            {
                SerializedProperty property = serialized.FindProperty("_initialAttentionObject");
                if (property == null)
                    return;

                property.stringValue = value ?? string.Empty;
                serialized.ApplyModifiedProperties();
            }

            MarkSceneKnowledgeCachesStale();
            Repaint();
        }

        #endregion

        #region Scan scene

        private void DrawScanCard(ConvaiActionConfigSource source, bool playing)
        {
            // Before the header, and only on the Layout event: the header's summary and the body below
            // it both report what the last scan found, and they have to be counting the same list. The
            // row list is settled once per frame here, so nothing can drop out of it between the pass
            // that measures this card and the pass that draws it.
            if (Event.current.type == EventType.Layout)
            {
                PruneDeadScanRows();

                // Re-derive once after an assembly reload: the rows came back, the verdicts about
                // them did not, and a surviving "Known" pill that no longer matches an entry would
                // be worse than no scan at all.
                if (_scanStatusesStale)
                {
                    _scanStatusesStale = false;
                    RefreshScanStatuses();
                }

                _scanDrawTotal = _scanRows?.Count ?? 0;
                _scanDrawByEntry = _scanByEntryCount;
                _scanDrawAutomatic = _scanAutomaticCount;
                _scanDrawNotKnown = _scanNotKnownCount;
            }

            bool expanded = BeginKnowledgeSection(
                ScanGlyph, ConvaiActionsEditorStrings.ScanSceneTitle, ScanSectionId, _scanSummary, out _);

            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            Theme.Paragraph(ConvaiActionsEditorStrings.ScanSceneExplainer, Theme.MutedWrapped);
            GUILayout.Space(4f);

            // The precedence rule, stated once on the card that owns the subject rather than on each
            // row it applies to. It is the answer to "I edited the description on the component and
            // nothing changed", which is otherwise a silent trap.
            Theme.Paragraph(ConvaiActionsEditorStrings.ScanPrecedenceNote, Theme.MutedWrapped);
            GUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect scanRect = GUILayoutUtility.GetRect(120f, 24f, GUILayout.Width(120f), GUILayout.Height(24f));
                if (Theme.GhostButton(scanRect, ConvaiActionsEditorStrings.ScanSceneButton))
                    RunSceneScan(source);

                // Absent rather than disabled when there is nothing to add: a permanently greyed
                // button is a question the user has to answer every time they look at it.
                if (_scanDrawNotKnown > 0)
                {
                    GUILayout.Space(6f);
                    GUIContent addAll = ConvaiActionsEditorStrings.BuildScanAddAllButton(_scanDrawNotKnown);
                    float width = Theme.GhostButtonWidth(addAll);
                    using (new EditorGUI.DisabledScope(playing))
                    {
                        Rect addRect = GUILayoutUtility.GetRect(
                            width, 24f, GUILayout.Width(width), GUILayout.Height(24f));
                        if (Theme.GhostButton(addRect, addAll))
                            AddEveryNotKnownTarget(source);
                    }
                }

                GUILayout.FlexibleSpace();
            }

            if (_hasScannedScene)
            {
                GUILayout.Space(8f);
                if (_scanRows == null || _scanRows.Count == 0)
                {
                    Theme.Paragraph(ConvaiActionsEditorStrings.ScanEmptyResult, Theme.MutedWrapped);
                }
                else
                {
                    string outcome = ConvaiActionsEditorStrings.BuildScanOutcome(
                        _scanDrawTotal, _scanDrawByEntry, _scanDrawAutomatic, _scanDrawNotKnown);
                    if (outcome != null)
                    {
                        Theme.Paragraph(outcome, Theme.BodyWrapped);
                        GUILayout.Space(6f);
                    }

                    for (int i = 0; i < _scanRows.Count; i++)
                        DrawScanRow(source, _scanRows[i], playing);
                }
            }

            Theme.EndCard();
        }

        /// <summary>
        ///     Drops scan rows whose target has been deleted since the scan, and re-derives the header
        ///     summary from what is left.
        /// </summary>
        /// <remarks>
        ///     This replaces a per-row <c>if (row.Target == null) return;</c>, which looked harmless and
        ///     was the reason the header could claim "39 found" over a body that listed nothing: a row
        ///     skipped at draw time still counted everywhere else, and it also drew fewer controls than
        ///     the layout pass had reserved. Removing a row from the list is the same decision made once,
        ///     in one place, where everything downstream sees it.
        /// </remarks>
        private void PruneDeadScanRows()
        {
            if (_scanRows == null || _scanRows.Count == 0)
                return;

            int removed = _scanRows.RemoveAll(static row => row.Target == null);
            if (removed > 0)
                RefreshScanSummary();
        }

        private void DrawScanRow(ConvaiActionConfigSource source, SceneScanRow row, bool playing)
        {
            Theme.BeginPanel(null);
            using (new EditorGUILayout.HorizontalScope())
            {
                // The name is the way back to the object. A scan can turn up forty rows, and until
                // this was clickable the only way to find out which "Spot Light_01" a row meant was
                // to go hunting in the Hierarchy.
                ScanRowName.text = row.DisplayName;
                ScanRowName.tooltip = ConvaiActionsEditorStrings.ScanRowPingTooltip;

                // Drawn in the ordinary name style, and clickable because a button takes whatever
                // style it is given. Theme.Link is centre-aligned — with a minimum width, every name
                // shorter than that centred itself and the column lost its left edge — and its blue
                // is Unity's hyperlink colour, which belongs to no other text in this window. The
                // cursor and the tooltip carry the affordance instead.
                // Rows are pruned of deleted targets every Layout, so the null check guards the gap
                // between that pass and the click, not an expected state.
                if (GUILayout.Button(ScanRowName, Theme.CardName, GUILayout.MinWidth(60f)) && row.Target != null)
                {
                    Selection.activeObject = row.Target.gameObject;
                    EditorGUIUtility.PingObject(row.Target.gameObject);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUIUtility.AddCursorRect(
                        GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                }

                GUILayout.FlexibleSpace();

                GUIContent kindPill = row.Kind == ConvaiActionTargetKind.Character
                    ? ConvaiActionsEditorStrings.ScanKindCharacterPill
                    : ConvaiActionsEditorStrings.ScanKindObjectPill;
                DrawScanPill(kindPill, Theme.TextMuted);

                GUILayout.Space(4f);
                (GUIContent statusPill, Color statusTint) = row.Status switch
                {
                    ConvaiSceneKnowledgeScanStatus.KnownByEntry =>
                        (ConvaiActionsEditorStrings.ScanStatusKnownPill, Theme.StatusReady),
                    ConvaiSceneKnowledgeScanStatus.RegistersAutomatically =>
                        (ConvaiActionsEditorStrings.ScanStatusAutoPill, Theme.Accent),
                    _ => (ConvaiActionsEditorStrings.ScanStatusNotKnownPill, Theme.StatusWarn)
                };
                DrawScanPill(statusPill, statusTint);

                if (row.Status == ConvaiSceneKnowledgeScanStatus.NotKnown)
                {
                    GUILayout.Space(6f);
                    using (new EditorGUI.DisabledScope(playing))
                    {
                        Rect addRect = GUILayoutUtility.GetRect(46f, 20f, GUILayout.Width(46f), GUILayout.Height(20f));
                        if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.ScanAddEntryButton))
                            AddDescribedEntryFromTarget(source, row.Target);
                    }
                }
            }

            Theme.EndPanel(4f);
        }

        /// <summary>
        ///     Draws a hint beside a button so the two read as one row.
        /// </summary>
        /// <remarks>
        ///     A wrapping label in a horizontal scope takes its own height and sits at the top of the
        ///     row, so a one-line hint next to a 24px button rode above the button's centre and the
        ///     pair read as two unrelated things. The flexible space either side centres the text
        ///     against the button without pinning the height: a hint that wraps to two lines still
        ///     grows the group rather than clipping inside it.
        /// </remarks>
        private static void DrawInlineRowHint(GUIContent hint, float rowHeight)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinHeight(rowHeight)))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(hint, Theme.MutedWrapped);
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawScanPill(GUIContent content, Color tint)
        {
            float width = Theme.PillWidth(content);
            Rect rect = GUILayoutUtility.GetRect(width, 16f, GUILayout.Width(width), GUILayout.Height(16f));
            Theme.Pill(rect, content, tint);
        }

        /// <summary>
        ///     Re-finds the scene's targets when they may have moved under the cached rows.
        /// </summary>
        /// <remarks>
        ///     Layout only, and for the same reason <see cref="EnsureSentPreview" /> is: a scan changes
        ///     how many rows the Find Targets card draws, and IMGUI requires a frame's Layout and
        ///     Repaint passes to agree on that count. Running on Layout, before any card in this pane
        ///     has drawn, means both passes of the frame see the same rows — a scan that landed
        ///     between them would draw controls the layout never reserved.
        /// </remarks>
        private void EnsureSceneScan(ConvaiActionConfigSource source)
        {
            if (!_sceneScanStale || source == null)
                return;

            if (Event.current.type != EventType.Layout)
            {
                Repaint();
                return;
            }

            RunSceneScan(source);
        }

        /// <summary>Finds every <see cref="ConvaiActionTarget" /> in the open scenes.</summary>
        private void RunSceneScan(ConvaiActionConfigSource source)
        {
            _sceneScanStale = false;

            ConvaiActionTarget[] found = ConvaiObjectFind.All<ConvaiActionTarget>(FindObjectsInactive.Include);
            var rows = new List<SceneScanRow>(found.Length);
            for (int i = 0; i < found.Length; i++)
                rows.Add(BuildScanRow(source, found[i]));

            rows.Sort(static (a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
            _scanRows = rows;
            _hasScannedScene = true;
            RefreshScanSummary();

            // The scan is also what the Sent To Convai card knows about the second delivery channel,
            // so a scan that did not rebuild the preview would leave that card reporting the connect
            // payload as the whole story — the exact thing this pane stopped doing.
            _previewDirty = true;
            Repaint();
        }

        /// <summary>
        ///     Re-counts the scanned targets this Convai Character still does not know, and rebuilds the
        ///     section header's summary from it.
        /// </summary>
        private void RefreshScanSummary()
        {
            int notKnown = 0;
            int byEntry = 0;
            int automatic = 0;
            if (_scanRows != null)
            {
                for (int i = 0; i < _scanRows.Count; i++)
                {
                    switch (_scanRows[i].Status)
                    {
                        case ConvaiSceneKnowledgeScanStatus.KnownByEntry:
                            byEntry++;
                            break;
                        case ConvaiSceneKnowledgeScanStatus.RegistersAutomatically:
                            automatic++;
                            break;
                        default:
                            notKnown++;
                            break;
                    }
                }
            }

            _scanNotKnownCount = notKnown;
            _scanByEntryCount = byEntry;
            _scanAutomaticCount = automatic;

            // Written straight through rather than flagged dirty: this can run after the frame's
            // summary refresh has already happened, and a header that reports the previous frame's
            // scan is the same disagreement in slower motion.
            _scanSummary = ConvaiActionsEditorStrings.BuildScanSummary(
                _hasScannedScene, _scanRows?.Count ?? 0, _scanNotKnownCount);
        }

        private SceneScanRow BuildScanRow(ConvaiActionConfigSource source, ConvaiActionTarget target)
        {
            bool autoRegisters = target.RegisterOnEnable &&
                                 _character != null &&
                                 target.AppliesToCharacter(_character);
            ConvaiSceneKnowledgeScanStatus status = ConvaiActionsSceneKnowledgeModel.Classify(
                target.TargetName, target.Kind, autoRegisters, source.Objects, source.Characters);
            return new SceneScanRow(target, target.TargetName, target.Kind, status);
        }

        /// <summary>
        ///     Re-derives cached scan rows' classifications against the current entry lists (after an
        ///     Add, edit, or undo) without re-searching the scenes — the scene itself is only
        ///     re-searched when the user clicks Scan Scene again.
        /// </summary>
        private void RefreshScanStatuses()
        {
            if (_scanRows == null || _scanRows.Count == 0)
                return;

            ConvaiActionConfigSource source = _character != null ? _character.GetActionConfigSource() : null;
            if (source == null)
                return;

            for (int i = 0; i < _scanRows.Count; i++)
            {
                ConvaiActionTarget target = _scanRows[i].Target;
                if (target == null)
                    continue;

                _scanRows[i] = BuildScanRow(source, target);
            }

            RefreshScanSummary();
        }

        /// <summary>
        ///     Creates an entry from a scanned target: its name, its text, and — the part that makes
        ///     the entry able to do anything — the target's own GameObject.
        /// </summary>
        /// <remarks>
        ///     This used to copy the name and the description and drop the object, producing an entry
        ///     the validator immediately reported as an error while the object it was made from stood
        ///     one row above it in the same list.
        /// </remarks>
        private void AddDescribedEntryFromTarget(ConvaiActionConfigSource source, ConvaiActionTarget target)
        {
            if (target == null)
                return; // Deleted between the frame that drew the row and the click on its button.

            if (target.Kind == ConvaiActionTargetKind.Character)
            {
                Undo.RecordObject(source, "Add Known Character");
                var characters = new List<ConvaiActionCharacterDefinition>(source.Characters)
                {
                    BuildCharacterEntry(target)
                };
                source.ReplaceCharacters(characters);
            }
            else
            {
                Undo.RecordObject(source, "Add Known Object");
                var objects = new List<ConvaiActionObjectDefinition>(source.Objects) { BuildObjectEntry(target) };
                source.ReplaceObjects(objects);
            }

            ExpandKnowledgeSection(target.Kind == ConvaiActionTargetKind.Character
                ? KnownCharactersSectionId
                : KnownObjectsSectionId);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>An object entry carrying everything the target already knows about itself.</summary>
        private static ConvaiActionObjectDefinition BuildObjectEntry(ConvaiActionTarget target) =>
            new()
            {
                Name = target.TargetName,
                Description = target.Description,
                GameObjectReference = target.gameObject,
                InteractionPoint = target.InteractionPoint
            };

        /// <summary>A character entry carrying everything the target already knows about itself.</summary>
        private static ConvaiActionCharacterDefinition BuildCharacterEntry(ConvaiActionTarget target) =>
            new()
            {
                Name = target.TargetName,
                Bio = target.Bio,
                GameObjectReference = target.gameObject,
                InteractionPoint = target.InteractionPoint
            };

        /// <summary>
        ///     Creates an entry for every scanned target this Convai Character cannot reach, as one
        ///     undoable step.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only <c>NotKnown</c> rows are touched. A target that already reaches the character
        ///         is deliberately left alone: copying it into an entry would freeze its description
        ///         at today's text and make the live component's own description stop mattering,
        ///         which is a downgrade wearing the costume of a convenience.
        ///     </para>
        ///     <para>
        ///         Names are de-duplicated within the batch as well as against the existing lists.
        ///         Scenes really do hold several targets with one name — this one has three called
        ///         <c>Spot Light_01</c> — and the backend rejects duplicate names outright, so adding
        ///         all three would have broken the payload that the button exists to complete.
        ///     </para>
        /// </remarks>
        private void AddEveryNotKnownTarget(ConvaiActionConfigSource source)
        {
            if (_scanRows == null || _scanRows.Count == 0)
                return;

            var objects = new List<ConvaiActionObjectDefinition>(source.Objects);
            var characters = new List<ConvaiActionCharacterDefinition>(source.Characters);
            var takenObjectNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var takenCharacterNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            int addedObjects = 0;
            int addedCharacters = 0;

            for (int i = 0; i < _scanRows.Count; i++)
            {
                SceneScanRow row = _scanRows[i];
                if (row.Status != ConvaiSceneKnowledgeScanStatus.NotKnown || row.Target == null)
                    continue;

                string name = row.Target.TargetName?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (row.Target.Kind == ConvaiActionTargetKind.Character)
                {
                    if (!takenCharacterNames.Add(name))
                        continue;

                    characters.Add(BuildCharacterEntry(row.Target));
                    addedCharacters++;
                }
                else
                {
                    if (!takenObjectNames.Add(name))
                        continue;

                    objects.Add(BuildObjectEntry(row.Target));
                    addedObjects++;
                }
            }

            if (addedObjects == 0 && addedCharacters == 0)
                return;

            Undo.RecordObject(source, "Add Scanned Targets");
            if (addedObjects > 0)
            {
                source.ReplaceObjects(objects);
                ExpandKnowledgeSection(KnownObjectsSectionId);
            }

            if (addedCharacters > 0)
            {
                source.ReplaceCharacters(characters);
                ExpandKnowledgeSection(KnownCharactersSectionId);
            }

            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        #endregion

        #region Drag and drop

        private void DrawDropCard(ConvaiActionConfigSource source, bool playing)
        {
            bool expanded = BeginKnowledgeSection(
                DropGlyph, ConvaiActionsEditorStrings.DropAreaTitle, DropSectionId, null, out _);

            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            Rect dropRect = GUILayoutUtility.GetRect(0f, 52f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                Theme.FillRounded(dropRect, Theme.InnerBg, 6f);
                Theme.StrokeRounded(dropRect,
                    dropRect.Contains(Event.current.mousePosition)
                        ? Theme.Fade(Theme.Accent, 0.75f)
                        : Theme.CardBorder, 6f);
            }

            GUI.Label(dropRect, ConvaiActionsEditorStrings.DropAreaBody, Theme.CenteredBody);
            HandleSceneObjectDrop(dropRect, source, playing);

            GUILayout.Space(6f);
            Theme.Paragraph(ConvaiActionsEditorStrings.DropChoiceExplainer, Theme.MutedWrapped);
            Theme.EndCard();
        }

        private void HandleSceneObjectDrop(Rect dropRect, ConvaiActionConfigSource source, bool playing)
        {
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;

            if (!dropRect.Contains(current.mousePosition))
                return;

            List<GameObject> dragged = CollectDraggedGameObjects();
            if (dragged.Count == 0 || playing)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type != EventType.DragPerform)
            {
                current.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            current.Use();
            OfferDropChoices(source, dragged);
        }

        private static List<GameObject> CollectDraggedGameObjects()
        {
            var result = new List<GameObject>();
            Object[] references = DragAndDrop.objectReferences;
            for (int i = 0; i < references.Length; i++)
            {
                if (references[i] is GameObject gameObject)
                    result.Add(gameObject);
            }

            return result;
        }

        /// <summary>
        ///     The drop decision, stated in one plain sentence per choice: a live Convai Action
        ///     Target component (preferred — it follows the object through spawn/despawn) versus a
        ///     static described entry (fixed text the character always knows). One dialog covers all
        ///     dropped objects.
        /// </summary>
        private void OfferDropChoices(ConvaiActionConfigSource source, List<GameObject> dropped)
        {
            string subject = dropped.Count == 1 ? $"'{dropped[0].name}'" : $"{dropped.Count} objects";
            int choice = EditorUtility.DisplayDialogComplex(
                "Add to Scene Knowledge",
                $"How should this Convai Character learn about {subject}?\n\n" +
                "Add Target Component — puts a Convai Action Target component on the object, so it " +
                "introduces itself automatically and keeps working through spawns and despawns. " +
                "(Recommended.)\n\n" +
                "Add Described Entry Only — writes a fixed name entry this character always knows, " +
                "even if the object never exists at runtime.",
                "Add Target Component",
                "Cancel",
                "Add Described Entry Only");

            if (choice == 1)
                return;

            for (int i = 0; i < dropped.Count; i++)
            {
                GameObject gameObject = dropped[i];
                if (gameObject == null)
                    continue;

                if (choice == 0)
                    AddTargetComponentTo(gameObject, source);
                else
                    AddDescribedEntryFromDrop(source, gameObject);
            }
        }

        private void AddTargetComponentTo(GameObject gameObject, ConvaiActionConfigSource source)
        {
            // A prefab asset cannot take a scene component from here — fall back to the described
            // entry so the drop still does something sensible instead of failing silently.
            if (EditorUtility.IsPersistent(gameObject))
            {
                ConvaiLogger.Warning(
                    $"[ConvaiActionsEditorWindow] '{gameObject.name}' is an asset, not a scene object — " +
                    "added it as a described entry instead of adding a Convai Action Target component.",
                    LogCategory.Editor);
                AddDescribedEntryFromDrop(source, gameObject);
                return;
            }

            if (!gameObject.TryGetComponent<ConvaiActionTarget>(out _))
                Undo.AddComponent<ConvaiActionTarget>(gameObject);

            Selection.activeObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>
        ///     Adds an entry for a dropped object, linked to it. An asset dropped from the Project
        ///     window has no scene object to link, so that entry is created as text only rather than
        ///     as a broken link.
        /// </summary>
        private void AddDescribedEntryFromDrop(ConvaiActionConfigSource source, GameObject gameObject)
        {
            bool isSceneObject = !EditorUtility.IsPersistent(gameObject);

            Undo.RecordObject(source, "Add Known Object");
            var objects = new List<ConvaiActionObjectDefinition>(source.Objects)
            {
                new()
                {
                    Name = gameObject.name,
                    Description = string.Empty,
                    GameObjectReference = isSceneObject ? gameObject : null,
                    TextOnly = !isSceneObject
                }
            };
            source.ReplaceObjects(objects);
            ExpandKnowledgeSection(KnownObjectsSectionId);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>
        ///     Adds a Known Character entry for a dropped scene object, linked to it. The Known
        ///     Characters list gets the same drop treatment as the objects list, so which list you drop
        ///     onto is what decides the kind.
        /// </summary>
        private void AddKnownCharacterFromDrop(ConvaiActionConfigSource source, GameObject gameObject)
        {
            bool isSceneObject = !EditorUtility.IsPersistent(gameObject);

            Undo.RecordObject(source, "Add Known Character");
            var characters = new List<ConvaiActionCharacterDefinition>(source.Characters)
            {
                new()
                {
                    Name = gameObject.name,
                    Bio = string.Empty,
                    GameObjectReference = isSceneObject ? gameObject : null,
                    TextOnly = !isSceneObject
                }
            };
            source.ReplaceCharacters(characters);
            ExpandKnowledgeSection(KnownCharactersSectionId);
            MarkDirty(source);
            MarkSceneKnowledgeCachesStale();
        }

        /// <summary>
        ///     Makes a whole card a drop target for Hierarchy objects: dropping onto the Known Objects
        ///     or Known Characters list adds linked entries directly, which is the one meaning a drop
        ///     onto a list can have.
        /// </summary>
        private void HandleKnownListDrop(
            Rect cardRect, ConvaiActionConfigSource source, bool characters, bool playing)
        {
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;

            if (!cardRect.Contains(current.mousePosition))
                return;

            List<GameObject> dragged = CollectDraggedGameObjects();
            if (dragged.Count == 0 || playing)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type != EventType.DragPerform)
            {
                current.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            current.Use();

            for (int i = 0; i < dragged.Count; i++)
            {
                if (dragged[i] == null)
                    continue;

                if (characters)
                    AddKnownCharacterFromDrop(source, dragged[i]);
                else
                    AddDescribedEntryFromDrop(source, dragged[i]);
            }
        }

        #endregion

        #region Sent To Convai preview

        private void DrawSentToConvaiCard(ConvaiActionConfigSource source)
        {
            // Built before the header, not inside the body: the header's summary reports what this
            // preview holds, so a collapsed section would otherwise be summarising the previous frame.
            EnsureSentPreview(source);

            bool expanded = BeginKnowledgeSection(
                SentGlyph, ConvaiActionsEditorStrings.SentToConvaiTitle, SentSectionId, _sentSummary, out _);

            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            if (_previewOmitted)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                Theme.Paragraph(ConvaiActionsEditorStrings.SentToConvaiNothing, Theme.BodyWrapped);
                Theme.EndPanel(6f);
            }
            else if (_previewConnectRows.Count == 0 && _previewSceneTargets.Count == 0)
            {
                Theme.Paragraph(ConvaiActionsEditorStrings.SentToConvaiEmpty, Theme.MutedWrapped);
                GUILayout.Space(6f);
            }
            else
            {
                DrawPreviewGroups();
            }

            // Both of these read Layout-stable fields only (see EnsureSentPreview): a paragraph that
            // appeared between a frame's Layout and Repaint passes would draw a control the layout
            // never reserved, which is the failure this card already guards its line count against.
            if (!_previewOmitted)
            {
                Theme.Paragraph(ConvaiActionsEditorStrings.SentToConvaiChannelExplainer, Theme.MutedWrapped);
                GUILayout.Space(6f);
            }

            Theme.Paragraph(ConvaiActionsEditorStrings.SentToConvaiExplainer, Theme.MutedWrapped);
            Theme.EndCard();
        }

        /// <summary>
        ///     Rebuilds the cached preview when stale, through the exact runtime path
        ///     (<see cref="ConvaiActionConfigSource.BuildActionConfig()" /> — the call the SDK makes at
        ///     connect), debounced so per-keystroke edits do not re-run the builder every repaint.
        /// </summary>
        private void EnsureSentPreview(ConvaiActionConfigSource source)
        {
            if (!_previewDirty || source == null)
                return;

            // Layout only. The number of preview lines is the number of controls this card draws, and
            // IMGUI requires a frame's Layout and Repaint passes to produce the same count. Rebuilding
            // on either pass made the debounce window the thing that decided which — a rebuild that
            // fell between the two passes of one frame added lines the layout had not reserved, and
            // Unity aborted the pass with "Getting control N's position in a group with only N
            // controls". Deferring to the next Layout costs one frame and cannot desynchronise.
            if (Event.current.type != EventType.Layout)
            {
                Repaint();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (_previewBuiltAt > 0d && now - _previewBuiltAt < PreviewDebounceSeconds)
            {
                Repaint(); // Come back once the debounce window has passed.
                return;
            }

            _previewDirty = false;
            _previewBuiltAt = now;
            _previewConnectRows.Clear();
            _previewSceneTargets.Clear();

            ConvaiActionConfig config = source.BuildActionConfig();
            _previewOmitted = config == null;
            if (_previewOmitted)
            {
                _previewConnectCount = 0;
                _sentSummary = ConvaiActionsEditorStrings.BuildSentToConvaiSummary(true, 0, 0);
                return;
            }

            int objectCount = config.Objects?.Count ?? 0;
            int characterCount = config.Characters?.Count ?? 0;
            ConvaiSceneKnowledgeReach reach = ConvaiActionsSceneKnowledgeModel.ComputeReach(
                objectCount, characterCount, BuildScannedTargetNames());
            _previewConnectCount = reach.AtConnectCount;
            _sentSummary = ConvaiActionsEditorStrings.BuildSentToConvaiSummary(
                false, reach.AtConnectCount, reach.AtConversationStart.Count);

            if (objectCount > 0)
            {
                _previewConnectRows.Add(PreviewRow.SubHeading(
                    ConvaiActionsEditorStrings.BuildPreviewObjectsLabel(objectCount)));
                for (int i = 0; i < objectCount; i++)
                {
                    ConvaiActionObjectDefinition entry = config.Objects[i];
                    _previewConnectRows.Add(PreviewRow.Entry(entry?.Name, entry?.Description));
                }
            }

            if (characterCount > 0)
            {
                _previewConnectRows.Add(PreviewRow.SubHeading(
                    ConvaiActionsEditorStrings.BuildPreviewCharactersLabel(characterCount)));
                for (int i = 0; i < characterCount; i++)
                {
                    ConvaiActionCharacterDefinition entry = config.Characters[i];
                    _previewConnectRows.Add(PreviewRow.Entry(entry?.Name, entry?.Bio));
                }
            }

            if (!string.IsNullOrWhiteSpace(config.CurrentAttentionObject))
                _previewConnectRows.Add(PreviewRow.Note($"Starting attention: {config.CurrentAttentionObject}"));

            IReadOnlyList<string> arriving = reach.AtConversationStart;
            for (int i = 0; i < arriving.Count; i++)
                _previewSceneTargets.Add(arriving[i]);
        }

        /// <summary>
        ///     Draws the preview as two delivery groups, each in its own bounded, scrollable panel.
        /// </summary>
        /// <remarks>
        ///     Bounded on purpose. A scene with a few dozen targets turned this card into a wall that
        ///     pushed everything else off the pane, and the answer to "what will it know?" was buried
        ///     in it. Each group now keeps a fixed maximum height and scrolls inside itself, so the
        ///     card stays the same size whether the character knows five things or five hundred —
        ///     and nothing is elided to achieve that.
        /// </remarks>
        private void DrawPreviewGroups()
        {
            if (_previewConnectRows.Count > 0)
            {
                Theme.BeginPanel(Theme.Accent, 2f);
                DrawPreviewGroupHeader(
                    ConvaiActionsEditorStrings.BuildSentAtConnectHeading(_previewConnectCount),
                    Theme.Accent);

                bool scrolls = BeginPreviewGroupBody(_previewConnectRows.Count, ref _previewConnectScroll);
                for (int i = 0; i < _previewConnectRows.Count; i++)
                    DrawPreviewRow(_previewConnectRows[i]);
                EndPreviewGroupBody(scrolls);
                Theme.EndPanel(8f);
            }

            if (_previewSceneTargets.Count == 0)
                return;

            Theme.BeginPanel(Theme.TextMuted, 2f);
            DrawPreviewGroupHeader(
                ConvaiActionsEditorStrings.BuildSentAtConversationStartHeading(_previewSceneTargets.Count),
                Theme.TextMuted);

            bool targetsScroll = BeginPreviewGroupBody(_previewSceneTargets.Count, ref _previewSceneTargetScroll);
            for (int i = 0; i < _previewSceneTargets.Count; i++)
                DrawPreviewRow(PreviewRow.Entry(_previewSceneTargets[i], null));
            EndPreviewGroupBody(targetsScroll);
            Theme.EndPanel(8f);
        }

        /// <summary>
        ///     Draws a delivery group's title and the rule that separates it from its contents.
        /// </summary>
        /// <remarks>
        ///     The rule is doing real work, not decoration. This pane's bold header style is 11px and
        ///     an entry name is 12px in the same colour, so the title was the *smallest* strong text
        ///     in its own group and read as just another row. Weight and a ruled edge separate the
        ///     levels here rather than size, which is how the rest of this design system does it.
        /// </remarks>
        private static void DrawPreviewGroupHeader(string title, Color tint)
        {
            GUILayout.Label(title, Theme.ListGroupHeader);
            GUILayout.Space(3f);

            Rect rule = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            Theme.DividerLine(rule, Theme.Fade(tint, 0.35f));
            GUILayout.Space(6f);
        }

        /// <summary>
        ///     Opens a delivery group's body, scrolling it only once it is long enough to be worth
        ///     scrolling. Returns whether a scroll view was opened, which the caller must hand back
        ///     to <see cref="EndPreviewGroupBody" />.
        /// </summary>
        /// <remarks>
        ///     The row count comes from the preview rebuild, which happens on the Layout event only,
        ///     so this decision cannot change between a frame's two passes and take a scroll view
        ///     with it.
        /// </remarks>
        private static bool BeginPreviewGroupBody(int rowCount, ref Vector2 scroll)
        {
            if (rowCount <= PreviewGroupScrollThreshold)
                return false;

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(PreviewGroupScrollHeight));
            return true;
        }

        private static void EndPreviewGroupBody(bool scrolling)
        {
            if (scrolling)
                EditorGUILayout.EndScrollView();
        }

        /// <summary>
        ///     Draws one preview row. An entry puts its name on its own line and its description
        ///     underneath in the muted style — the two used to share one line, which is why a list of
        ///     eighteen read as one paragraph.
        /// </summary>
        /// <remarks>
        ///     Every row is laid out at the full width of its group, with no horizontal scope around
        ///     it. A wrapping paragraph beside anything else cannot grow into the width it needs and
        ///     silently clips (see <c>ConvaiEditorFrame.Paragraph</c>), so the visual hierarchy here
        ///     comes from type weight rather than from indentation.
        /// </remarks>
        private static void DrawPreviewRow(PreviewRow row)
        {
            switch (row.Kind)
            {
                case PreviewRowKind.SubHeading:
                    // Through the design system's own caption control, which owns both the left edge
                    // and the rhythm around it. Drawing the caption style by hand — which is what
                    // this did — is the thing that set the divider a few pixels in from the names
                    // below it. No Space() either side: the style carries equal margins, and adding
                    // more is how the same caption ends up tight against one list and floating above
                    // the next. Small caps do the "label, not content" work.
                    Theme.GroupCaption(row.Text);
                    break;

                case PreviewRowKind.Note:
                    GUILayout.Space(8f);
                    Theme.Paragraph(row.Text, Theme.MutedWrapped);
                    break;

                default:
                    GUILayout.Label(row.Text, Theme.CardName);

                    // Muted against the name above it. Both were drawn in the primary colour, which
                    // left a name and its description looking like two halves of one sentence.
                    if (!string.IsNullOrEmpty(row.Detail))
                        Theme.Paragraph(row.Detail, Theme.MutedWrapped);

                    GUILayout.Space(7f);
                    break;
            }
        }

        /// <summary>
        ///     Reduces the cached scan rows to the two fields the reach calculation needs.
        ///     Empty before the first scan, which is honest: nothing has looked yet.
        /// </summary>
        private IReadOnlyList<ConvaiScannedTargetName> BuildScannedTargetNames()
        {
            if (_scanRows == null || _scanRows.Count == 0)
                return System.Array.Empty<ConvaiScannedTargetName>();

            var names = new List<ConvaiScannedTargetName>(_scanRows.Count);
            for (int i = 0; i < _scanRows.Count; i++)
                names.Add(new ConvaiScannedTargetName(_scanRows[i].DisplayName, _scanRows[i].Status));

            return names;
        }

        #endregion
    }
}
