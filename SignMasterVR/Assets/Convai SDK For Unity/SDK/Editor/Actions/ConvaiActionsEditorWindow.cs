using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.Inspectors;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Convai.Editor.UI;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The Actions Editor window's top-level modes : the action list stays
    ///     the default spine; Scene Knowledge and Character Settings are sibling views over the same
    ///     picked character.
    /// </summary>
    internal enum ConvaiActionsEditorMode
    {
        /// <summary>The original action list + detail panes.</summary>
        Actions = 0,

        /// <summary>Known objects/characters, initial attention, scene scan, and the sent-to-Convai preview.</summary>
        SceneKnowledge = 1,

        /// <summary>Dispatcher and feedback relay settings for the picked character.</summary>
        CharacterSettings = 2,

        /// <summary>Play-mode-only live monitor: current activity, timeline, live registry, feedback log.</summary>
        Live = 3
    }

    /// <summary>
    ///     Two-pane, beginner-first authoring window for a Convai Character's actions.
    ///     The left pane lists every action (editable inline
    ///     actions grouped as "This Character", then one read-only group per assigned
    ///     <see cref="ConvaiActionSet" />) as hoverable status cards; the right pane edits the
    ///     selected action's Command, Scene Behavior, and (collapsed by default) Advanced settings.
    ///     Every user-facing string lives in <see cref="ConvaiActionsEditorStrings" />; every visual
    ///     primitive lives in <see cref="ConvaiEditorTheme" />.
    /// </summary>
    /// <remarks>
    ///     Mutation strategy: this window is not a <c>UnityEditor.Editor</c> bound to one target's
    ///     <c>SerializedObject</c> — it edits whichever character is currently picked, and its
    ///     selection can outlive any one inspector's lifecycle. Rather than juggling a
    ///     <c>SerializedObject</c>/<c>SerializedProperty</c> tree that would need rebuilding on every
    ///     character switch, every field write here follows the same
    ///     <c>Undo.RecordObject(source, ...)</c> → direct field mutation →
    ///     <c>EditorUtility.SetDirty(source)</c> idiom the Live mode Advanced card's template-apply
    ///     path (<c>ApplyLivePresetTemplates</c>) also uses. Undo
    ///     correctly restores nested <see cref="ConvaiActionDefinition" />/parameter field edits
    ///     because Unity's object-level Undo snapshot serializes the whole
    ///     <see cref="ConvaiActionConfigSource" /> (including its nested serializable lists), not just
    ///     the top-level fields that changed.
    /// </remarks>
    internal sealed partial class ConvaiActionsEditorWindow : EditorWindow
    {
        /// <summary>Extra band height for the character-picker row under the hero's opening line.</summary>
        private const float HeroPickerRowHeight = 32f;

        /// <summary>
        ///     Height of a group heading in the action list. Close to the 34px action card on purpose:
        ///     a heading has to hold its own against the rows it heads, and a list of folded groups is
        ///     the whole window until one is opened.
        /// </summary>
        private const float GroupHeaderHeight = 30f;

        /// <summary>
        ///     Where the picker row starts: the band's own left inset, the same edge the emblem and
        ///     the mode-bar tabs below sit against. Deliberately not the title's text column — the
        ///     emblem only occupies the opening line, so indenting to it left this row with a wide
        ///     empty margin and nothing above it to explain the indent.
        /// </summary>
        private const float HeroPickerRowX = 14f;

        /// <summary>Right-hand space the clickable health pill needs.</summary>
        private const float HeroRightReserve = 200f;

        /// <summary>
        ///     How many ready-made starters the empty state offers. Four teach the core journey —
        ///     move, follow, direct attention, and express — without turning onboarding into a catalog.
        /// </summary>
        private const int MaximumStarterCards = 4;

        private const float LeftPaneWidth = 286f;
        private const float FooterHeight = 32f;
        private const string SectionStateHostId = "ActionsEditorWindow";
        private const string AdvancedSectionId = "Advanced";
        private const string ModeSessionKey = "Convai.ActionsEditor.Mode";

        /// <summary>
        ///     Focus-tracking name for this window's search field. Must be unique per field so the
        ///     placeholder hides only when this field actually holds focus.
        /// </summary>
        private const string SearchControlName = "ConvaiActionsEditor.Search";

        private const string CommandGlyph = ConvaiEditorGlyphs.Command;
        private const string SceneBehaviorGlyph = ConvaiEditorGlyphs.Run;
        private const string AdvancedGlyph = ConvaiEditorGlyphs.Contract;

        /// <summary>
        ///     Deliberately labelless wrapper for the row's enable checkbox (the action name sits
        ///     right next to it); the tooltip text stays owned by the string table.
        /// </summary>
        private static readonly GUIContent RowEnabledToggle =
            new(string.Empty, ConvaiActionsEditorStrings.ActionEnabledField.tooltip);

        private ConvaiCharacter _character;

        /// <summary>Backing state for <see cref="GetSceneCharacters" />.</summary>
        private ConvaiCharacter[] _sceneCharacters = Array.Empty<ConvaiCharacter>();

        private ConvaiEditorRefreshTimer _sceneCharacterTimer;
        private bool _sceneCharactersResolved;
        private ConvaiActionDefinition _selectedDefinition;
        private string _searchFilter = string.Empty;
        private bool _advancedExpanded;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private ConvaiActionsEditorMode _mode;

        /// <summary>Backing state for <see cref="FindAllActionConfigSources" />, valid for one draw pass.</summary>
        private List<ConvaiActionConfigSource> _actionConfigSources;

        private bool _actionConfigSourcesScanned;

        /// <summary>
        ///     Opens the window from the Convai menu, on whichever character the window last showed.
        /// </summary>
        [MenuItem("Convai/Actions Editor", false, ConvaiEditorMenu.FeatureEditors + 0)]
        internal static void ShowWindow()
        {
            ConvaiActionsEditorWindow window = GetWindow<ConvaiActionsEditorWindow>();
            window.ApplyWindowChrome();
            window.Show();
        }

        /// <summary>
        ///     Opens the window focused on the character owning <paramref name="source" /> (the shrunk
        ///     inspector's entry point). Pass <paramref name="definition" /> to deep-link straight to
        ///     one action's editing pane.
        /// </summary>
        internal static void ShowWindowFor(ConvaiActionConfigSource source, ConvaiActionDefinition definition = null)
        {
            ConvaiActionsEditorWindow window = GetWindow<ConvaiActionsEditorWindow>();
            window.ApplyWindowChrome();
            if (source != null)
                window._character = source.GetComponent<ConvaiCharacter>();
            if (definition != null)
            {
                window._selectedDefinition = definition;
                // A deep link targets one action's detail pane, which only the Actions mode shows.
                window.SetMode(ConvaiActionsEditorMode.Actions);
            }

            window.Show();
            window.Focus();
        }

        private void ApplyWindowChrome()
        {
            titleContent = new GUIContent(
                ConvaiActionsEditorStrings.WindowTabTitle.text,
                UI.ConvaiEditorIcons.Emblem());
            minSize = new Vector2(860f, 540f);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;

            // Also the after-assembly-reload hook, which is the only chance the Scene Knowledge pane
            // gets to throw away caches that did not survive the reload intact.
            ResetSceneKnowledgeDerivedState();

            _advancedExpanded = ConvaiEditorSectionState.Get(SectionStateHostId, AdvancedSectionId, false);
            _mode = (ConvaiActionsEditorMode)SessionState.GetInt(ModeSessionKey, (int)ConvaiActionsEditorMode.Actions);
            LoadProductivitySessionState();
            LoadGroupingState();

            // Live only exists during Play mode (or as Session Review while the last session's
            // recording is still around); a stale persisted Live selection falls back.
            if (_mode == ConvaiActionsEditorMode.Live && !EditorApplication.isPlaying &&
                ConvaiActionsSessionCollector.Log.Batches.Count == 0)
                _mode = ConvaiActionsEditorMode.Actions;

            Undo.undoRedoPerformed += HandleUndoRedoPerformed;
            EditorApplication.hierarchyChanged += MarkSettingsBindingsStale;
            EditorApplication.hierarchyChanged += MarkSceneCharactersStale;
            EditorApplication.hierarchyChanged += MarkSceneScanStale;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.update += HandleEditorUpdateForLive;
            ConvaiActionsSessionCollector.AddConsumer(this);
            ConvaiActionsSessionCollector.Changed += HandleCollectorChanged;
            if (_character == null)
                AutoSelectCharacter();
        }

        private void OnDisable()
        {
            ConvaiEditorSectionState.Set(SectionStateHostId, AdvancedSectionId, _advancedExpanded);
            Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
            EditorApplication.hierarchyChanged -= MarkSettingsBindingsStale;
            EditorApplication.hierarchyChanged -= MarkSceneCharactersStale;
            EditorApplication.hierarchyChanged -= MarkSceneScanStale;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.update -= HandleEditorUpdateForLive;
            ConvaiActionsSessionCollector.Changed -= HandleCollectorChanged;
            ConvaiActionsSessionCollector.RemoveConsumer(this);
            DisposeCharacterSettingsBindings();
            UnsubscribeLiveRuntimeDiagnostics();
        }

        /// <summary>
        ///     Undo/redo can rewrite the scene-knowledge lists behind the caches this window keeps
        ///     (the sent-to-Convai preview and scan-row classifications), so both are refreshed.
        /// </summary>
        private void HandleUndoRedoPerformed()
        {
            MarkSceneKnowledgeCachesStale();
            Repaint();
        }

        private void AutoSelectCharacter()
        {
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            if (characters.Length == 1)
                _character = characters[0];
        }

        /// <summary>
        ///     The Convai Characters in the open scenes, rebuilt on a timer rather than per event.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This window sets <see cref="EditorWindow.wantsMouseMove" /> and repaints on every
        ///         mouse-move event, and <see cref="OnGUI" /> needs this list on every pass to draw the
        ///         character picker. Scanning for it inline therefore walked every open scene each time
        ///         the pointer moved a pixel across the window — the heaviest per-event cost anywhere
        ///         in Convai's editor UI, and one that grew with the size of the user's scene rather
        ///         than with anything on screen.
        ///     </para>
        ///     <para>
        ///         Refreshed two ways on purpose: <see cref="EditorApplication.hierarchyChanged" />
        ///         marks it stale the moment a character is added, removed or renamed, and the timer
        ///         is the safety net for everything that changes the answer without raising that event.
        ///     </para>
        /// </remarks>
        private ConvaiCharacter[] GetSceneCharacters()
        {
            if (_sceneCharacterTimer.ShouldRefresh(_sceneCharactersResolved))
            {
                _sceneCharactersResolved = true;
                _sceneCharacters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            }

            return _sceneCharacters;
        }

        /// <summary>Refreshes the cached character list at the top of the next pass.</summary>
        private void MarkSceneCharactersStale() => _sceneCharacterTimer.Invalidate(true);

        /// <summary>
        ///     The first character in <paramref name="characters" /> that still exists.
        /// </summary>
        /// <remarks>
        ///     The list is rebuilt on a timer, so between a deletion and the next refresh it can name
        ///     an object that is gone. Reading <c>name</c> off a destroyed object throws, which would
        ///     take the window down over a state that resolves itself a frame later.
        /// </remarks>
        private static ConvaiCharacter FirstLiveCharacter(ConvaiCharacter[] characters)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] != null)
                    return characters[i];
            }

            return null;
        }

        private void OnGUI()
        {
            Theme.EnsureStyles();

            // One scene scan per draw pass at most — see FindAllActionConfigSources.
            _actionConfigSourcesScanned = false;

            if (Event.current.type == EventType.MouseMove)
                Repaint();

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), Theme.WindowBg);

            ConvaiCharacter[] characters = GetSceneCharacters();
            if (_character == null && characters.Length == 1)
                _character = characters[0];

            ConvaiActionConfigSource source = _character != null ? _character.GetActionConfigSource() : null;
            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics =
                source != null ? ConvaiActionSetupReport.ValidateCached(source) : null;

            DrawHero(characters, source != null);

            if (_character == null)
            {
                DrawCenteredState(
                    ConvaiActionsEditorStrings.NoCharacterStateTitle,
                    ConvaiActionsEditorStrings.NoCharacterStateBody,
                    null,
                    null);
                return;
            }

            if (source == null)
            {
                DrawCenteredState(
                    ConvaiActionsEditorStrings.EnableActionsTitle,
                    ConvaiActionsEditorStrings.EnableActionsBody,
                    ConvaiActionsEditorStrings.EnableActionsButton,
                    EnableActions);
                return;
            }

            if (EditorApplication.isPlaying)
                EnsureCollectorSubject();

            DrawModeSwitcher();

            // Live needs something to show: Play mode, or the last session's surviving recording
            // (Session Review). With neither, the selection quietly falls back to Actions (the
            // chip is not drawn either) without touching the persisted mode.
            ConvaiActionsEditorMode effectiveMode =
                _mode == ConvaiActionsEditorMode.Live && !EditorApplication.isPlaying &&
                ConvaiActionsSessionCollector.Log.Batches.Count == 0
                    ? ConvaiActionsEditorMode.Actions
                    : _mode;

            switch (effectiveMode)
            {
                case ConvaiActionsEditorMode.SceneKnowledge:
                    DrawSceneKnowledgeMode(source);
                    break;

                case ConvaiActionsEditorMode.CharacterSettings:
                    DrawCharacterSettingsMode(source);
                    break;

                case ConvaiActionsEditorMode.Live:
                    DrawLiveMode();
                    break;

                default:
                    DrawActionsMode(source, diagnostics);
                    break;
            }

            DrawFooter(diagnostics);

            // A paragraph that settled its width during this pass was laid out against a width the
            // pass did not have — one more draw and every paragraph is at its right height. Costs a
            // single extra pass after a domain reload or a window resize, and nothing otherwise.
            if (Theme.ConsumeMeasurementChange())
                Repaint();
        }

        /// <summary>The original action-list mode, unchanged: toolbar, empty-state hero, or the two panes.</summary>
        private void DrawActionsMode(ConvaiActionConfigSource source, IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics)
        {
            DrawToolbarRow(source);

            if (!ConvaiActionsEditorModel.HasAuthoredContent(source))
            {
                DrawEmptyState(source);
            }
            else
            {
                ApplyAutomaticGroupAxis(source);

                // Built once along the Source axis — row status, filtering and authored indices are
                // decided there — then rearranged for display. Everything downstream (selection,
                // bulk operations, category commands) reads the arranged list, so what the user sees
                // and what an edit acts on are the same order.
                List<ConvaiActionGroup> groups = BuildDisplayGroups(
                    ConvaiActionsEditorModel.BuildGroups(source, diagnostics, _searchFilter, _listFilter));
                _lastGroups = groups;
                RestoreOrPruneMultiSelection(source);
                HandleListKeyboard(groups);
                ConvaiActionRow? selectedRow = ResolveSelectedRow(groups);

                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawLeftPane(source, groups);
                    DrawVerticalDivider();
                    if (HasMultiSelection)
                        DrawMultiSelectionPane(source);
                    else
                        DrawRightPane(source, selectedRow, groups, diagnostics);
                }
            }
        }

        #region Mode switcher

        /// <summary>
        ///     Slim segmented mode row directly under the hero: the selected segment is the accent
        ///     primary button, siblings are ghost buttons. Kept deliberately small so the action list
        ///     remains the window's visual spine in the default mode.
        /// </summary>
        /// <summary>
        ///     Tab labels in <see cref="ConvaiActionsEditorMode" /> order, so the index the mode bar
        ///     returns is the mode itself. Three sets rather than one because the fourth tab is
        ///     conditional and changes its own label — see <see cref="ModeTabs" />.
        /// </summary>
        private static readonly GUIContent[] ModeTabsEditing =
        {
            ConvaiActionsEditorStrings.ModeActions,
            ConvaiActionsEditorStrings.ModeSceneKnowledge,
            ConvaiActionsEditorStrings.ModeCharacterSettings
        };

        private static readonly GUIContent[] ModeTabsPlaying =
        {
            ConvaiActionsEditorStrings.ModeActions,
            ConvaiActionsEditorStrings.ModeSceneKnowledge,
            ConvaiActionsEditorStrings.ModeCharacterSettings,
            ConvaiActionsEditorStrings.ModeLive
        };

        private static readonly GUIContent[] ModeTabsSessionReview =
        {
            ConvaiActionsEditorStrings.ModeActions,
            ConvaiActionsEditorStrings.ModeSceneKnowledge,
            ConvaiActionsEditorStrings.ModeCharacterSettings,
            ConvaiActionsEditorStrings.ModeSessionReview
        };

        /// <summary>
        ///     Live is a Play-mode sibling: it appears when there is something live to watch. After
        ///     Play mode ends it lingers as "Session Review" while the recorded session is still around
        ///     (usage insights and the timeline stay reviewable), then disappears.
        /// </summary>
        private static GUIContent[] ModeTabs =>
            EditorApplication.isPlaying ? ModeTabsPlaying
            : ConvaiActionsSessionCollector.Log.Batches.Count > 0 ? ModeTabsSessionReview
            : ModeTabsEditing;

        private void DrawModeSwitcher()
        {
            int clicked = Theme.ModeBar(ModeTabs, (int)_mode);
            if (clicked >= 0)
                SetMode((ConvaiActionsEditorMode)clicked);
        }

        private void SetMode(ConvaiActionsEditorMode mode)
        {
            _mode = mode;
            SessionState.SetInt(ModeSessionKey, (int)mode);
            GUIUtility.keyboardControl = 0;

            // Definitions edited in Actions mode change what the connect-time preview shows,
            // so entering Scene Knowledge always re-renders it from current data.
            if (mode == ConvaiActionsEditorMode.SceneKnowledge)
                MarkSceneKnowledgeCachesStale();

            Repaint();
        }

        #endregion

        #region Hero header

        /// <summary>
        ///     This window's band carries two things the shared hero does not draw itself: a character
        ///     picker on a second row, and a health pill that is a <em>button</em> (it opens the
        ///     Troubleshooter). So the design system draws the opening line and asks for the extra row,
        ///     and this window places both affordances into the band rect it gets back.
        /// </summary>
        private void DrawHero(ConvaiCharacter[] characters, bool hasSource)
        {
            Rect hero = Theme.WindowHero(
                position.width,
                ConvaiActionsEditorStrings.HeroTitle,
                ConvaiActionsEditorStrings.HeroSubtitle,
                extraHeight: HeroPickerRowHeight);

            DrawCharacterPicker(
                characters,
                new Rect(
                    HeroPickerRowX, hero.yMax - 34f,
                    hero.width - HeroPickerRowX - HeroRightReserve, 24f));

            if (hasSource)
                DrawHealthChip(_character, hero);
        }

        private void DrawCharacterPicker(ConvaiCharacter[] characters, Rect rowRect)
        {
            // The label is measured, not boxed into a fixed column: "Character" is one word in a
            // known style, and a padded fixed width is how a gap opens up between it and the picker.
            float labelWidth = ConvaiEditorTextMetrics.Width(
                Theme.HeroSubtitle, ConvaiActionsEditorStrings.HeroCharacterLabel);
            GUI.Label(new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height),
                ConvaiActionsEditorStrings.HeroCharacterLabel, Theme.HeroSubtitle);

            float valueX = rowRect.x + labelWidth + 8f;
            float valueWidth = rowRect.xMax - valueX;

            if (characters.Length == 0)
            {
                GUI.Label(new Rect(valueX, rowRect.y, valueWidth, rowRect.height),
                    ConvaiActionsEditorStrings.HeroNoCharacters, Theme.MicroLabel);
                return;
            }

            ConvaiCharacter picked = _character != null ? _character : FirstLiveCharacter(characters);
            if (picked == null)
            {
                GUI.Label(new Rect(valueX, rowRect.y, valueWidth, rowRect.height),
                    ConvaiActionsEditorStrings.HeroNoCharacters, Theme.MicroLabel);
                return;
            }

            GUIContent pill = ConvaiActionsEditorStrings.BuildCharacterPill(picked.name);
            float width = Mathf.Clamp(
                ConvaiEditorTextMetrics.Width(Theme.HeroSubtitle, pill) + 28f, 120f, 260f);
            var pillRect = new Rect(valueX, rowRect.y, width, rowRect.height);
            if (Theme.GhostButton(pillRect, pill))
                ShowCharacterMenu(characters);
        }

        private void ShowCharacterMenu(ConvaiCharacter[] characters)
        {
            var menu = new GenericMenu();
            for (int i = 0; i < characters.Length; i++)
            {
                ConvaiCharacter candidate = characters[i];
                menu.AddItem(new GUIContent(candidate.name), candidate == _character, () =>
                {
                    if (_character == candidate)
                        return;

                    _character = candidate;
                    _selectedDefinition = null;
                    GUIUtility.keyboardControl = 0;
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        /// <summary>
        ///     The hero's health chip. It counts the same things the Action Troubleshooter it opens
        ///     counts — it used to summarize the validator alone, so clicking a chip that said
        ///     "1 to fix" opened a window headed "5 To Fix", which reads as one of them being broken.
        /// </summary>
        private static void DrawHealthChip(ConvaiCharacter character, Rect hero)
        {
            ConvaiActionSetupReport report = ConvaiActionSetupReport.Cached(character);
            bool healthy = report.IsHealthy;
            GUIContent content = healthy
                ? ConvaiActionsEditorStrings.HealthChipReady
                : ConvaiActionsEditorStrings.BuildHealthChipIssues(report.IssueCount);
            Color tint = healthy
                ? Theme.StatusReady
                : report.ErrorCount > 0 ? Theme.StatusError : Theme.StatusWarn;

            float width = Theme.PillWidth(content) + 8f;
            var chipRect = new Rect(hero.width - width - 16f, 16f, width, 22f);
            if (Theme.PillButton(chipRect, content, tint))
                ConvaiActionTroubleshooterWindow.ShowWindow(character);
        }

        #endregion

        #region Toolbar row

        private void DrawToolbarRow(ConvaiActionConfigSource source)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            row.xMin += 16f;
            row.xMax -= 16f;
            float y = Theme.CenteredSlice(row, 26f).y;

            const float addWidth = 132f;
            const float overflowWidth = 28f;
            GUIContent filterChoice = FilterChoiceContent(_listFilter);
            float filterWidth = Theme.PillWidth(filterChoice) + 12f;
            GUIContent groupChoice = GroupChoiceContent(_groupAxis);
            float groupWidth = Theme.PillWidth(groupChoice) + 12f;
            var searchRect = new Rect(row.x, y,
                Mathf.Min(280f, row.width - addWidth - overflowWidth - filterWidth - groupWidth - 40f), 26f);
            _searchFilter = Theme.SearchField(
                searchRect,
                _searchFilter,
                ConvaiActionsEditorStrings.SearchPlaceholder,
                ConvaiActionsEditorStrings.SearchFieldHelp,
                ConvaiActionsEditorStrings.ClearSearchButton,
                SearchControlName);

            var filterRect = new Rect(searchRect.xMax + 8f, y, filterWidth, 26f);
            if (Theme.GhostButton(filterRect, filterChoice))
                ShowListFilterMenu();

            // Next to the scope filter, because the two answer the neighbouring questions "what is
            // shown?" and "how is it arranged?" — and both are the same kind of control.
            var groupRect = new Rect(filterRect.xMax + 8f, y, groupWidth, 26f);
            if (Theme.GhostButton(groupRect, groupChoice))
                ShowGroupByMenu();

            var addRect = new Rect(row.xMax - addWidth, y, addWidth, 26f);
            if (Theme.PrimaryButton(addRect, ConvaiActionsEditorStrings.AddActionButton))
                ShowAddActionMenu(source);

            var overflowRect = new Rect(addRect.x - overflowWidth - 8f, y, overflowWidth, 26f);
            if (Theme.GhostButton(overflowRect, ConvaiActionsEditorStrings.ToolbarOverflowButton))
                ShowToolbarOverflowMenu(source);
        }

        #endregion

        #region Left pane

        private void DrawLeftPane(ConvaiActionConfigSource source, List<ConvaiActionGroup> groups)
        {
            Rect paneRect = EditorGUILayout.BeginVertical(GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(paneRect, Theme.PaneBg);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
            GUILayout.Space(6f);

            if (ShouldOfferCategorySuggestion(source, groups))
            {
                DrawCategorySuggestionBanner(source, groups);
                GUILayout.Space(4f);
            }

            int visibleRows = 0;
            bool drewGroup = false;
            for (int g = 0; g < groups.Count; g++)
            {
                ConvaiActionGroup group = groups[g];

                // While narrowing (search text or scope filter), a group with no surviving rows is
                // noise — hide its header too. Unnarrowed, an empty group still draws: "This
                // Character 0" tells you where inline actions go, and an empty set's header is the
                // only way to reach or remove it.
                if (group.Rows.Count == 0 && HasActiveListNarrowing)
                    continue;

                // A rule between groups, never above the first one: with every group folded the pane
                // is nothing but headings, and headings with no separation read as one list of rows.
                if (drewGroup)
                    DrawGroupSeparator();

                drewGroup = true;
                DrawGroupHeader(source, group);

                if (IsGroupCollapsed(group.Key))
                    continue;

                for (int r = 0; r < group.Rows.Count; r++)
                {
                    DrawActionCard(source, group.Rows[r]);
                    visibleRows++;
                }

                if (group.Set != null && group.Rows.Count == 0)
                    DrawEmptySetHint(group.Set);

                GUILayout.Space(8f);
            }

            // Action Sets are a property of the character, not of one arrangement of its list — the
            // footer that assigns them only makes sense while the list is arranged by source.
            if (_groupAxis == ConvaiActionsGroupAxis.Source)
                DrawAddActionSetRow(source);

            if (visibleRows == 0 && HasActiveListNarrowing)
            {
                GUILayout.Space(12f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(12f);
                    GUILayout.Label(
                        !string.IsNullOrWhiteSpace(_searchFilter)
                            ? ConvaiActionsEditorStrings.NoSearchResults
                            : ConvaiActionsEditorStrings.NoFilterResults,
                        Theme.MutedWrapped);
                    GUILayout.Space(12f);
                }
            }

            DrawListDeselectZone();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        ///     The empty space under the last card, claimed so a click there means what it looks like
        ///     it means: nothing selected, back to the overview. Without it the blank area silently
        ///     swallowed clicks and the pane kept showing whichever action was last opened.
        /// </summary>
        private void DrawListDeselectZone()
        {
            Rect empty = GUILayoutUtility.GetRect(
                0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !empty.Contains(current.mousePosition))
                return;

            _selectedDefinition = null;
            ClearMultiSelection();
            GUIUtility.keyboardControl = 0;
            current.Use();
            Repaint();
        }

        private void DrawGroupHeader(ConvaiActionConfigSource source, ConvaiActionGroup group)
        {
            Rect row = GUILayoutUtility.GetRect(0f, GroupHeaderHeight, GUILayout.ExpandWidth(true));
            row.xMin += 12f;
            row.xMax -= 12f;

            GUIContent countPill = ConvaiActionsEditorStrings.BuildCountPill(group.Rows.Count);
            float pillWidth = Theme.PillWidth(countPill);

            // Category/status/behavior groups are computed, so their header carries no asset
            // affordances — it collapses, reports health, and (for a category) offers the commands
            // that act on the whole group.
            if (group.Kind != ConvaiActionGroupKind.ThisCharacter && group.Kind != ConvaiActionGroupKind.ActionSet)
            {
                DrawComputedGroupHeader(source, group, row, countPill, pillWidth);
                return;
            }

            if (group.Set == null)
            {
                GUI.Label(new Rect(row.x, row.y, row.width - pillWidth - 4f, row.height),
                    ConvaiActionsEditorStrings.ThisCharacterGroup, Theme.ListGroupHeader);
                Theme.Pill(
                    PillSlot(
                        row,
                        row.x + ConvaiEditorTextMetrics.Width(
                            Theme.ListGroupHeader, ConvaiActionsEditorStrings.ThisCharacterGroup) + 8f,
                        pillWidth),
                    countPill, Theme.TextMuted);
                return;
            }

            // Custom trailing icon buttons (select asset / stop using / add) sit outside the shared
            // collapsible header row's layout, so this header stays hand-drawn — only the chevron
            // literal is swapped for the design system's disclosure marks.
            bool expanded = !IsGroupCollapsed(group.Key);
            Rect chevronRect = CenteredSlot(row, row.x, 16f, 16f);
            GUI.Label(
                chevronRect,
                expanded ? Glyphs.Affordance.DisclosureOpen : Glyphs.Affordance.DisclosureClosed,
                Theme.MicroLabel);

            // Three trailing icon buttons (add / stop using / select asset) plus the count pill.
            const float trailingReserve = 70f;
            GUIContent setTitle = ConvaiActionsEditorStrings.BuildActionSetGroupLabel(group.Title);
            float titleWidth = Mathf.Max(24f, Mathf.Min(
                ConvaiEditorTextMetrics.Width(Theme.ListGroupHeader, setTitle) + 4f,
                row.width - 16f - pillWidth - trailingReserve));
            var titleRect = new Rect(chevronRect.xMax, row.y, titleWidth, row.height);
            EditorGUIUtility.AddCursorRect(new Rect(row.x, row.y, titleRect.xMax - row.x, row.height), MouseCursor.Link);
            bool titleClicked = GUI.Button(titleRect, setTitle, Theme.ListGroupHeader);
            bool chevronClicked = GUI.Button(chevronRect, GUIContent.none, Theme.InvisibleButton);
            if (titleClicked || chevronClicked)
                SetGroupCollapsed(group.Key, expanded);

            Theme.Pill(PillSlot(row, titleRect.xMax + 4f, pillWidth), countPill, Theme.TextMuted);

            Rect pingRect = CenteredSlot(row, row.xMax - 20f, 18f, 18f);
            if (Theme.IconButton(pingRect, ConvaiActionsEditorStrings.SelectAssetIcon))
            {
                Selection.activeObject = group.Set;
                EditorGUIUtility.PingObject(group.Set);
            }

            Rect removeRect = CenteredSlot(row, pingRect.x - 20f, 18f, 18f);
            if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.RemoveActionSetIcon))
                RemoveActionSet(source, group.Set);

            Rect addRect = CenteredSlot(row, removeRect.x - 20f, 18f, 18f);
            if (Theme.IconButton(addRect, ConvaiActionsEditorStrings.AddToSetIcon))
                ShowAddActionToSetMenu(group.Set);
        }

        /// <summary>The hairline that separates one group of the action list from the previous one.</summary>
        private static void DrawGroupSeparator()
        {
            Rect rule = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            rule.xMin += 12f;
            rule.xMax -= 12f;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rule, Theme.Divider);
        }

        /// <summary>A fixed-size affordance inside a header row, centred on the row's own height.</summary>
        private static Rect CenteredSlot(Rect row, float x, float width, float height) =>
            new(x, row.y + ((row.height - height) * 0.5f), width, height);

        /// <summary>The 16px pill slot inside a header row, centred the same way.</summary>
        private static Rect PillSlot(Rect row, float x, float width) =>
            CenteredSlot(row, x, width, 16f);

        /// <summary>
        ///     Header for a computed group — one category, one status bucket, one behavior family.
        ///     Collapses like a set's header, reports the group's worst status so a folded group still
        ///     admits what is wrong inside it, and (categories only) opens the commands that act on
        ///     every action in it.
        /// </summary>
        private void DrawComputedGroupHeader(
            ConvaiActionConfigSource source, ConvaiActionGroup group, Rect row, GUIContent countPill, float pillWidth)
        {
            bool expanded = !IsGroupCollapsed(group.Key);
            bool isCategory = group.Kind == ConvaiActionGroupKind.Category;

            HandleHeaderDropTarget(source, group, row);

            Rect chevronRect = CenteredSlot(row, row.x, 16f, 16f);
            GUI.Label(
                chevronRect,
                expanded ? Glyphs.Affordance.DisclosureOpen : Glyphs.Affordance.DisclosureClosed,
                Theme.MicroLabel);

            // Reserve: the count pill, the attention pill when there is one, and the category menu.
            GUIContent attentionPill = group.UnhealthyCount > 0
                ? ConvaiActionsEditorStrings.BuildGroupAttentionPill(group.UnhealthyCount)
                : null;
            float attentionWidth = attentionPill == null ? 0f : Theme.PillWidth(attentionPill) + 6f;
            float menuReserve = isCategory ? 22f : 0f;

            GUIContent title = isCategory && group.CategoryName.Length > 0
                ? ConvaiActionsEditorStrings.BuildCategoryGroupLabel(group.Title)
                : GroupTitleContent(group);

            // A category header used to carry a colour derived from its own name. It meant nothing —
            // and it said it in the list's one loaded dialect, the coloured dot, which on every row
            // below means ready/needs attention/broken. Two meanings for one mark leaves neither
            // trustworthy, so the heading is typographic and colour in this pane stays status only.
            float titleX = chevronRect.xMax;

            float titleWidth = Mathf.Max(24f, Mathf.Min(
                ConvaiEditorTextMetrics.Width(Theme.ListGroupHeader, title) + 4f,
                row.xMax - titleX - pillWidth - attentionWidth - menuReserve - 8f));
            var titleRect = new Rect(titleX, row.y, titleWidth, row.height);

            var hitRect = new Rect(row.x, row.y, titleRect.xMax - row.x, row.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.Link);

            // Both buttons are drawn every pass — short-circuiting the second one on the frame the
            // first is clicked would change the control count mid-pass.
            bool titleClicked = GUI.Button(titleRect, title, Theme.ListGroupHeader);
            bool chevronClicked = GUI.Button(chevronRect, GUIContent.none, Theme.InvisibleButton);
            if (titleClicked || chevronClicked)
                SetGroupCollapsed(group.Key, expanded);

            Theme.Pill(PillSlot(row, titleRect.xMax + 4f, pillWidth), countPill, Theme.TextMuted);

            if (attentionPill != null)
            {
                Color tint = group.WorstStatus == ConvaiActionRowStatus.Broken ? Theme.StatusError : Theme.StatusWarn;
                Rect attentionRect = PillSlot(row, titleRect.xMax + 8f + pillWidth, attentionWidth - 6f);
                Theme.Pill(attentionRect, attentionPill, tint, true);
            }

            if (!isCategory)
                return;

            Rect menuRect = CenteredSlot(row, row.xMax - 20f, 18f, 18f);
            if (Theme.IconButton(menuRect, ConvaiActionsEditorStrings.ToolbarOverflowButton))
                ShowCategoryHeaderMenu(source, group);

            if (Event.current.type == EventType.ContextClick && row.Contains(Event.current.mousePosition))
            {
                ShowCategoryHeaderMenu(source, group);
                Event.current.Use();
            }
        }

        /// <summary>
        ///     Cached header content for computed groups whose titles come from the model rather than
        ///     the string table (status buckets, behavior families, the uncategorized pile).
        /// </summary>
        private GUIContent GroupTitleContent(ConvaiActionGroup group)
        {
            if (group.Kind == ConvaiActionGroupKind.Category && group.CategoryName.Length == 0)
                return ConvaiActionsEditorStrings.CategoryUncategorizedHeader;

            // Keyed by the group's identity, not by its title: a header is drawn on every repaint, and
            // a one-slot cache would allocate a fresh GUIContent per group per pass.
            if (!_groupTitleContents.TryGetValue(group.Key, out GUIContent content))
            {
                content = new GUIContent(group.Title, GroupTitleTooltip(group.Kind));
                _groupTitleContents[group.Key] = content;
            }

            return content;
        }

        private static string GroupTitleTooltip(ConvaiActionGroupKind kind) =>
            kind == ConvaiActionGroupKind.Behavior
                ? ConvaiActionsEditorStrings.GroupMenuBehavior.tooltip
                : ConvaiActionsEditorStrings.GroupMenuStatus.tooltip;

        /// <summary>
        ///     An assigned set holding no actions would otherwise render as a bare header over empty
        ///     space, with nothing explaining why it contributes nothing — the exact dead end a freshly
        ///     created set lands in. This says what happened and points at the "+" that fixes it.
        /// </summary>
        private void DrawEmptySetHint(ConvaiActionSet set)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22f);
                GUILayout.Label(ConvaiActionsEditorStrings.EmptySetHint, Theme.MutedWrapped);
                GUILayout.Space(12f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22f);
                Rect addRect = GUILayoutUtility.GetRect(150f, 22f, GUILayout.Width(150f), GUILayout.Height(22f));
                if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.AddFirstSetActionButton))
                    ShowAddActionToSetMenu(set);
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(4f);
        }

        /// <summary>
        ///     The left pane's footer control for Action Sets. When the character uses none, it also
        ///     carries the one-paragraph "what is an Action Set?" explainer — this is the point of
        ///     decision, and the feature is otherwise undiscoverable: nothing else in the UI says what
        ///     a set is or how to get one. Once at least one set is in use the concept is self-evident
        ///     from the groups above, so the explainer retires and only the action remains.
        /// </summary>
        private void DrawAddActionSetRow(ConvaiActionConfigSource source)
        {
            bool usesAnySet = ConvaiActionsEditorModel.CountAssignedSets(source) > 0;

            GUILayout.Space(usesAnySet ? 2f : 10f);

            if (!usesAnySet)
            {
                Rect line = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
                line.xMin += 12f;
                line.xMax -= 12f;
                Theme.DividerLine(line, Theme.Divider);
                GUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(12f);
                    GUILayout.Label(ConvaiActionsEditorStrings.ActionSetsExplainer, Theme.MutedWrapped);
                    GUILayout.Space(12f);
                }

                GUILayout.Space(6f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                if (GUILayout.Button(ConvaiActionsEditorStrings.AddActionSetLink, Theme.Link))
                    ShowAddActionSetMenu(source);
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(8f);
        }

        private void DrawActionCard(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            Rect slot = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
            var card = new Rect(slot.x + 10f, slot.y, slot.width - 20f, 34f);

            // Right-click anywhere on the card opens the row operations menu
            // (duplicate/copy/paste/extract/delete, or the shared-row variants).
            if (Event.current.type == EventType.ContextClick && card.Contains(Event.current.mousePosition))
            {
                ShowRowOperationsMenu(source, row);
                Event.current.Use();
            }

            HandleRowDragSource(row, card);
            HandleRowDropTarget(source, row, card);

            bool selected = IsRowSelected(row.Definition);
            bool hover = card.Contains(Event.current.mousePosition);

            Theme.FillRounded(card, selected ? Theme.CardBgSelected : hover ? Theme.CardBgHover : Theme.CardBg, 6f);
            Theme.StrokeRounded(card,
                selected ? Theme.Fade(Theme.Accent, 0.85f) : Theme.CardBorder, 6f);

            // Availability checkbox first: unticked actions are withheld from the Convai
            // Character entirely, so the state deserves row-level visibility and a one-click flip.
            bool rowEnabled = row.Definition == null || row.Definition.Enabled;
            var enabledRect = new Rect(card.x + 7f, card.y + ((card.height - 16f) * 0.5f), 16f, 16f);
            bool newRowEnabled = GUI.Toggle(enabledRect, rowEnabled, RowEnabledToggle);
            if (newRowEnabled != rowEnabled && row.Definition != null)
            {
                UnityEngine.Object toggleOwner = OwnerOf(source, row);
                Undo.RecordObject(toggleOwner, newRowEnabled ? "Enable Action" : "Disable Action");
                row.Definition.Enabled = newRowEnabled;
                MarkDirty(toggleOwner);
                rowEnabled = newRowEnabled;
            }

            Color previousGuiColor = GUI.color;
            if (!rowEnabled)
                GUI.color = Theme.Fade(previousGuiColor, previousGuiColor.a * 0.45f);

            Color statusColor = StatusColor(row.Status);
            Theme.StatusDot(new Vector2(card.x + 36f, card.y + (card.height * 0.5f)), statusColor, selected || hover);
            GUI.Label(new Rect(card.x + 26f, card.y + 7f, 20f, 20f), StatusDotContent(row.Status), Theme.InvisibleButton);

            bool showRowButtons = hover || selected;
            float buttonsWidth = showRowButtons ? 66f : 0f;
            var nameRect = new Rect(card.x + 50f, card.y, card.width - 54f - buttonsWidth, card.height);
            EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
            if (GUI.Button(nameRect, ConvaiActionsEditorStrings.BuildActionRowLabel(row.DisplayName),
                    selected ? Theme.CardNameSelected : Theme.CardName))
                HandleRowClick(row);

            GUI.color = previousGuiColor;

            if (!showRowButtons)
                return;

            // Reorder/remove act on whichever list actually owns this row — the character's inline
            // definitions, or the shared set's. Both are authored lists the user can rearrange.
            int ownerCount = row.OwningSet != null ? row.OwningSet.Definitions.Count : source.Definitions.Count;
            float buttonY = card.y + ((card.height - 18f) * 0.5f);

            using (new EditorGUI.DisabledScope(row.OwnerIndex <= 0))
            {
                if (Theme.IconButton(new Rect(card.xMax - 66f, buttonY, 18f, 18f), ConvaiActionsEditorStrings.MoveActionUpButton))
                    MoveAction(source, row, -1);
            }

            using (new EditorGUI.DisabledScope(row.OwnerIndex >= ownerCount - 1))
            {
                if (Theme.IconButton(new Rect(card.xMax - 46f, buttonY, 18f, 18f), ConvaiActionsEditorStrings.MoveActionDownButton))
                    MoveAction(source, row, 1);
            }

            if (Theme.IconButton(new Rect(card.xMax - 24f, buttonY, 18f, 18f), BuildRemoveActionContent(row)))
                RemoveAction(source, row);
        }

        /// <summary>
        ///     Removing a shared action hits every character using the set, so its tooltip must say so
        ///     rather than reuse the inline row's "remove from this character" wording.
        /// </summary>
        private static GUIContent BuildRemoveActionContent(ConvaiActionRow row) =>
            row.IsShared
                ? ConvaiActionsEditorStrings.BuildRemoveSharedActionButton(row.OwningSet.name)
                : ConvaiActionsEditorStrings.RemoveActionButton;

        private static void DrawVerticalDivider()
        {
            Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(divider, Theme.Divider);
        }

        private static Color StatusColor(ConvaiActionRowStatus status) => status switch
        {
            ConvaiActionRowStatus.Ready => Theme.StatusReady,
            ConvaiActionRowStatus.NeedsAttention => Theme.StatusWarn,
            _ => Theme.StatusError
        };

        private static GUIContent StatusDotContent(ConvaiActionRowStatus status) => status switch
        {
            ConvaiActionRowStatus.Ready => ConvaiActionsEditorStrings.StatusDotReady,
            ConvaiActionRowStatus.NeedsAttention => ConvaiActionsEditorStrings.StatusDotNeedsAttention,
            _ => ConvaiActionsEditorStrings.StatusDotBroken
        };

        private static GUIContent StatusChipContent(ConvaiActionRowStatus status) => status switch
        {
            ConvaiActionRowStatus.Ready => ConvaiActionsEditorStrings.StatusChipReady,
            ConvaiActionRowStatus.NeedsAttention => ConvaiActionsEditorStrings.StatusChipNeedsAttention,
            _ => ConvaiActionsEditorStrings.StatusChipBroken
        };

        private ConvaiActionRow? ResolveSelectedRow(List<ConvaiActionGroup> groups)
        {
            if (_selectedDefinition != null)
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    List<ConvaiActionRow> rows = groups[g].Rows;
                    for (int r = 0; r < rows.Count; r++)
                    {
                        if (ReferenceEquals(rows[r].Definition, _selectedDefinition))
                            return rows[r];
                    }
                }
            }

            // No fallback to "the first row": opening the window, or clicking the empty space under
            // the list, means nothing is selected, and the right pane says so with the overview.
            // Auto-selecting made every fresh open look like the author had picked that action.
            _selectedDefinition = null;
            return null;
        }

        /// <summary>Up/Down arrow selection across the visible (non-collapsed, filtered) rows when no field has focus.</summary>
        private void HandleListKeyboard(List<ConvaiActionGroup> groups)
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
                return;

            int direction = current.keyCode switch
            {
                KeyCode.DownArrow => 1,
                KeyCode.UpArrow => -1,
                _ => 0
            };
            if (direction == 0)
                return;

            var visible = new List<ConvaiActionRow>();
            for (int g = 0; g < groups.Count; g++)
            {
                if (IsGroupCollapsed(groups[g].Key))
                    continue;

                visible.AddRange(groups[g].Rows);
            }

            if (visible.Count == 0)
                return;

            int index = -1;
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i].Definition, _selectedDefinition))
                {
                    index = i;
                    break;
                }
            }

            int next = Mathf.Clamp(index + direction, 0, visible.Count - 1);
            _selectedDefinition = visible[next].Definition;
            ClearMultiSelection();
            current.Use();
            Repaint();
        }

        #endregion

        #region Right pane

        private void DrawRightPane(
            ConvaiActionConfigSource source,
            ConvaiActionRow? selectedRowNullable,
            List<ConvaiActionGroup> groups,
            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
                using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
                {
                    float previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 120f;

                    if (selectedRowNullable == null)
                    {
                        DrawOverviewPane(source, groups);
                    }
                    else
                    {
                        ConvaiActionRow row = selectedRowNullable.Value;
                        DrawSelectedHeader(source, row);
                        DrawDetailHistoryStrip(row);
                        DrawSharedSetBanner(row);
                        DrawCommandCard(source, row);
                        DrawSceneBehaviorCard(source, row);
                        DrawTestRunCard(source, row);
                        DrawAdvancedCard(source, row);
                    }

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        ///     What the right pane shows when no action is selected — which is now the state the
        ///     window opens in. Rather than an instruction to go click something, it answers the
        ///     question an author actually has on arrival: what can this character already do, how
        ///     much of it works, and where do I go from here.
        /// </summary>
        private void DrawOverviewPane(ConvaiActionConfigSource source, List<ConvaiActionGroup> groups)
        {
            int total = 0;
            int ready = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                List<ConvaiActionRow> rows = groups[g].Rows;
                total += rows.Count;
                for (int r = 0; r < rows.Count; r++)
                {
                    if (rows[r].Status == ConvaiActionRowStatus.Ready)
                        ready++;
                }
            }

            int needsWork = total - ready;

            Theme.BeginCard();
            Theme.SectionHeader(
                ConvaiEditorGlyphs.Identity,
                ConvaiActionsEditorStrings.BuildOverviewTitle(_character != null ? _character.name : string.Empty));

            GUILayout.Label(ConvaiActionsEditorStrings.RightPaneNoSelection, Theme.MutedWrapped);
            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                Theme.StatTile(ConvaiActionsEditorStrings.OverviewTileActions, total.ToString());
                GUILayout.Space(6f);
                Theme.StatTile(ConvaiActionsEditorStrings.OverviewTileReady, ready.ToString(),
                    ready > 0 ? Theme.StatusReady : (Color?)null);
                GUILayout.Space(6f);
                Theme.StatTile(ConvaiActionsEditorStrings.OverviewTileNeedsWork, needsWork.ToString(),
                    needsWork > 0 ? Theme.StatusWarn : (Color?)null);
            }

            Theme.EndCard();

            // The breakdown mirrors whatever axis the list is grouped by, so the summary and the
            // list on the left never tell two different stories about the same actions.
            if (total > 0)
            {
                Theme.BeginCard();
                Theme.SectionHeader(ConvaiEditorGlyphs.Section, ConvaiActionsEditorStrings.OverviewBreakdownTitle);

                for (int g = 0; g < groups.Count; g++)
                {
                    ConvaiActionGroup group = groups[g];
                    if (group.Rows.Count == 0)
                        continue;

                    Theme.KeyValueRow(
                        ConvaiActionsEditorStrings.BuildOverviewGroupLabel(group.Title),
                        ConvaiActionsEditorStrings.BuildOverviewGroupCount(group.Rows.Count),
                        group.UnhealthyCount > 0 ? StatusColor(group.WorstStatus) : (Color?)null);
                }

                Theme.EndCard();
            }

            Theme.BeginCard();
            Theme.SectionHeader(ConvaiEditorGlyphs.Blink, ConvaiActionsEditorStrings.OverviewNextStepsTitle);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (Theme.GhostButtonLayout(ConvaiActionsEditorStrings.OverviewAddActionButton, 24f))
                    ShowAddActionMenu(source);

                GUILayout.Space(6f);
                if (Theme.GhostButtonLayout(ConvaiActionsEditorStrings.OverviewSceneKnowledgeButton, 24f))
                    SetMode(ConvaiActionsEditorMode.SceneKnowledge);

                GUILayout.Space(6f);
                if (Theme.GhostButtonLayout(ConvaiActionsEditorStrings.OverviewTroubleshooterButton, 24f))
                    ConvaiActionTroubleshooterWindow.ShowWindow(_character);
            }

            Theme.EndCard();
        }

        private void DrawSelectedHeader(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(row.DisplayName, Theme.SelectedTitle, GUILayout.Height(24f));
                GUILayout.FlexibleSpace();

                // Same operations menu the list row's right-click opens, surfaced as a visible
                // affordance so the feature is discoverable without knowing about context clicks.
                Rect overflowRect = GUILayoutUtility.GetRect(26f, 22f, GUILayout.Width(26f), GUILayout.Height(22f));
                if (Theme.GhostButton(overflowRect, ConvaiActionsEditorStrings.RowOverflowButton))
                    ShowRowOperationsMenu(source, row);
            }

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent chip = StatusChipContent(row.Status);
                float chipWidth = Theme.PillWidth(chip);
                Rect chipRect = GUILayoutUtility.GetRect(chipWidth, 18f, GUILayout.Width(chipWidth), GUILayout.Height(18f));
                Theme.Pill(chipRect, chip, StatusColor(row.Status));

                if (row.IsShared && row.OwningSet != null)
                {
                    GUILayout.Space(6f);
                    GUIContent shared = ConvaiActionsEditorStrings.BuildSharedFromSet(row.OwningSet.name);
                    float sharedWidth = Theme.PillWidth(shared);
                    Rect sharedRect = GUILayoutUtility.GetRect(sharedWidth, 18f, GUILayout.Width(sharedWidth), GUILayout.Height(18f));
                    Theme.Pill(sharedRect, shared, Theme.TextSecondary);

                    GUILayout.Space(6f);
                    Rect selectRect = GUILayoutUtility.GetRect(88f, 18f, GUILayout.Width(88f), GUILayout.Height(18f));
                    if (Theme.GhostButton(selectRect, ConvaiActionsEditorStrings.SelectAssetButton))
                    {
                        Selection.activeObject = row.OwningSet;
                        EditorGUIUtility.PingObject(row.OwningSet);
                    }
                }

                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(10f);
        }

        /// <summary>
        ///     States, before any editable field, that this action is shared and exactly how far an edit
        ///     reaches. This banner is what makes editing a shared asset in place honest: without it the
        ///     right pane looks identical to an inline action, and a user would reasonably assume their
        ///     rename only touched the character they have open.
        /// </summary>
        private void DrawSharedSetBanner(ConvaiActionRow row)
        {
            if (!row.IsShared || row.OwningSet == null)
                return;

            int otherUsers = ConvaiActionsEditorModel.CountCharactersUsingSet(FindAllActionConfigSources(), row.OwningSet) - 1;
            GUIContent banner = otherUsers > 0
                ? ConvaiActionsEditorStrings.BuildSharedSetBanner(row.OwningSet.name, otherUsers)
                : ConvaiActionsEditorStrings.SharedSetBannerSoleUser;

            Theme.BeginPanel(otherUsers > 0 ? Theme.StatusWarn : Theme.TextSecondary);
            GUILayout.Label(banner, Theme.BodyWrapped);
            Theme.EndPanel(10f);
        }

        /// <summary>
        ///     Every <see cref="ConvaiActionConfigSource" /> in the open scenes, used to count a set's
        ///     other users. Scoped to open scenes only — the banner deliberately promises nothing about
        ///     characters in unopened scenes or prefabs, which this window cannot see.
        /// </summary>
        /// <remarks>
        ///     Scanned at most once per <see cref="OnGUI" /> pass. The window repaints on every mouse
        ///     move and every shared row draws this banner, so an unscanned call site meant a full
        ///     scene scan per shared row per repaint.
        /// </remarks>
        private List<ConvaiActionConfigSource> FindAllActionConfigSources()
        {
            if (_actionConfigSourcesScanned)
                return _actionConfigSources;

            ConvaiActionConfigSource[] found =
                ConvaiObjectFind.All<ConvaiActionConfigSource>(FindObjectsInactive.Include);
            _actionConfigSources = new List<ConvaiActionConfigSource>(found);
            _actionConfigSourcesScanned = true;
            return _actionConfigSources;
        }

        private void DrawCommandCard(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            ConvaiActionDefinition definition = row.Definition;
            Theme.BeginCard();
            Theme.SectionHeader(CommandGlyph, ConvaiActionsEditorStrings.CommandBoxTitle);

            UnityEngine.Object owner = OwnerOf(source, row);

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(ConvaiActionsEditorStrings.NameField, definition.ActionName);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Rename Action");
                definition.ActionName = newName;
                MarkDirty(owner);
            }

            EditorGUILayout.Space(2f);
            EditorGUI.BeginChangeCheck();
            // Prose, not a name: a description worth writing runs past one line, and a single-line
            // field hides everything but the fragment under the caret. Grows to fit what is written.
            string newDescription = Theme.ProseField(
                ConvaiActionsEditorStrings.DescriptionField,
                definition.Description,
                "Action.Description:" + (row.OwningSet != null ? row.OwningSet.name : "inline") + ":" + row.OwnerIndex,
                ConvaiActionsEditorStrings.DescriptionPlaceholder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Edit Action Description");
                definition.Description = newDescription;
                MarkDirty(owner);
            }

            EditorGUILayout.Space(2f);
            DrawTargetRequirementRow(source, definition, owner);

            EditorGUILayout.Space(2f);
            DrawAnswerDeliveryRow(source, definition, owner);

            EditorGUILayout.Space(2f);
            EditorGUI.BeginChangeCheck();
            bool newEnabled = EditorGUILayout.Toggle(ConvaiActionsEditorStrings.ActionEnabledField, definition.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, newEnabled ? "Enable Action" : "Disable Action");
                definition.Enabled = newEnabled;
                MarkDirty(owner);
            }

            EditorGUILayout.Space(10f);
            GUILayout.Label(ConvaiActionsEditorStrings.CommandPreviewLabel, Theme.MicroLabel);
            GUILayout.Space(2f);
            Theme.BeginPanel(null, 3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6f);
                string rendered = definition.ToActionConfigString();
                GUILayout.Label(ConvaiActionsEditorStrings.BuildCommandPreviewValue(rendered), Theme.PreviewQuote);
            }

            Theme.EndPanel(0f);
            Theme.EndCard();
        }

        /// <summary>
        ///     The target is part of the spoken command contract, so it lives beside Name and
        ///     Description rather than behind Advanced. When the character has nothing of the
        ///     accepted kind, the same row explains the impact and starts the exact setup task.
        /// </summary>
        private void DrawTargetRequirementRow(
            ConvaiActionConfigSource source,
            ConvaiActionDefinition definition,
            UnityEngine.Object owner)
        {
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(
                ConvaiActionsEditorStrings.ValidTargetsField,
                (int)definition.TargetRequirement,
                ConvaiActionsEditorStrings.ValidTargetOptions);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Change Action Target");
                definition.TargetRequirement = (ConvaiActionTargetRequirement)selected;
                MarkDirty(owner);
                ConvaiActionSetupReport.Invalidate();
            }

            ConvaiActionTargetRequirement requirement = definition.TargetRequirement;
            if (requirement == ConvaiActionTargetRequirement.None ||
                ConvaiActionConfigValidator.HasTargetForRequirement(
                    source, requirement, ConvaiActionSetupReport.CachedSceneTargets()))
                return;

            string actionName = definition.ActionName;
            string characterName = _character != null ? _character.name : source != null ? source.name : null;
            bool canEdit = !EditorApplication.isPlaying;
            Theme.WarningBox(
                ConvaiActionsEditorStrings.BuildMissingTargetTitle(actionName),
                ConvaiActionsEditorStrings.BuildMissingTargetMessage(actionName, characterName, requirement),
                canEdit ? ConvaiActionsEditorStrings.BuildAddTargetButton(requirement) : null,
                canEdit ? () => BeginTargetSetup(source, requirement) : null);
        }

        /// <summary>
        ///     "When it finishes" — whether the Convai Character says what this action found.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         It lives on the Command card rather than under Advanced on purpose. Whether an
        ///         action answers a question is part of what the action <em>is</em>, decided in the
        ///         same breath as its name and description — and it sits directly under Description
        ///         because that is where the author has just written the sentence this control is
        ///         asking about. Buried under Advanced it would reproduce the very defect it exists
        ///         to remove: a capability that is present and that a first-time user never finds.
        ///     </para>
        ///     <para>
        ///         The resolved line underneath is the point. Two settings decide this between them,
        ///         and asking anybody to hold that precedence in their head is how a Convai Character
        ///         ends up silent for a reason nobody can see.
        ///     </para>
        /// </remarks>
        private void DrawAnswerDeliveryRow(
            ConvaiActionConfigSource source,
            ConvaiActionDefinition definition,
            UnityEngine.Object owner)
        {
            EditorGUI.BeginChangeCheck();
            var newDelivery = (ConvaiActionAnswerDelivery)EditorGUILayout.EnumPopup(
                ConvaiActionsEditorStrings.AnswerDeliveryField, definition.AnswerDelivery);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Change Action Answer Delivery");
                definition.AnswerDelivery = newDelivery;
                MarkDirty(owner);
            }

            DrawSettingsFieldHint(ConvaiActionAnswerDeliveryExplanations.Explain(definition.AnswerDelivery));

            if (definition.AnswerDelivery == ConvaiActionAnswerDelivery.TellThePlayer)
                DrawSettingsFieldHint(ConvaiActionAnswerDeliveryExplanations.TellThePlayerFootnote);

            ConvaiActionFeedbackMode? characterMode = ResolveCharacterSuccessMode(source, out string characterName);

            DrawSettingsFieldHint(ConvaiActionAnswerDeliveryExplanations.DescribeEffect(
                definition.AnswerDelivery, characterMode, characterName));

            ConvaiActionAnswerAdvisory advisory = ConvaiActionAnswerDeliveryExplanations.FindAdvisory(
                definition.AnswerDelivery, characterMode, characterName);
            if (!advisory.Exists) return;

            // A missing relay is an optional enhancement, not a broken action. Say so in the
            // neutral information treatment and put the safe mechanical next step beside the
            // explanation. Scene components are authored only outside Play Mode.
            if (characterMode == null)
            {
                bool canAdd = !EditorApplication.isPlaying;
                string message = canAdd
                    ? advisory.Message
                    : advisory.Message + " Exit Play Mode to add it.";
                Theme.InfoBox(
                    advisory.Title,
                    message,
                    canAdd ? ConvaiActionsEditorStrings.SettingsAddRelayButton.text : null,
                    canAdd ? AddRelayComponent : null);
                return;
            }

            if (advisory.IsWarning)
                Theme.WarningBox(advisory.Title, advisory.Message);
            else
                Theme.InfoBox(advisory.Title, advisory.Message);
        }

        /// <summary>
        ///     The success feedback mode of the character this action belongs to, or <c>null</c> when
        ///     there is no relay to deliver anything at all.
        /// </summary>
        private static ConvaiActionFeedbackMode? ResolveCharacterSuccessMode(
            ConvaiActionConfigSource source,
            out string characterName)
        {
            characterName = null;
            if (source == null) return null;

            ConvaiCharacter character = source.GetComponentInParent<ConvaiCharacter>(true);
            characterName = character != null ? character.name : source.gameObject.name;

            ConvaiActionFeedbackRelay relay = source.GetComponentInParent<ConvaiActionFeedbackRelay>(true);
            return relay == null ? null : relay.SuccessFeedbackMode;
        }

        private void DrawSceneBehaviorCard(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            ConvaiActionDefinition definition = row.Definition;
            Theme.BeginCard();
            Theme.SectionHeader(SceneBehaviorGlyph, ConvaiActionsEditorStrings.SceneBehaviorBoxTitle);

            if (row.IsShared)
            {
                DrawSharedBehaviorBody(source, row);
                Theme.EndCard();
                return;
            }

            if (definition.Executor is IConvaiActionExecutor)
            {
                DrawBoundBehaviorStatus(definition.Executor.GetType(), definition.Executor.gameObject.name);
                DrawBehaviorEditRow(source, definition);
                Theme.EndCard();
                return;
            }

            string hint = definition.ExecutorTypeHint;
            Type resolvedType = null;
            bool hintResolves = !string.IsNullOrWhiteSpace(hint) && ConvaiActionExecutorBinder.TryResolveType(hint, out resolvedType);
            if (!hintResolves)
            {
                Theme.BeginPanel(Theme.StatusError);
                GUILayout.Label(ConvaiActionsEditorStrings.ActionBehaviorMissing, Theme.BodyWrapped);
                Theme.EndPanel();
                DrawBehaviorEditRow(source, definition);
                Theme.EndCard();
                return;
            }

            var candidate = source.GetComponentInChildren(resolvedType, true) as MonoBehaviour;
            if (candidate != null)
            {
                // Read-only info only: this mirrors runtime auto-binding
                // (ConvaiActionExecutorBinder.TryBind, invoked from
                // ConvaiActionConfigSource.GetEffectiveDefinitions) without silently writing to
                // the authored Executor field on every repaint — a real field assignment only
                // ever happens from an explicit user action (Add & Bind, the dropdown, or the
                // object field), never as a side effect of drawing this status line.
                DrawBoundBehaviorStatus(resolvedType, candidate.gameObject.name);
                Theme.EndCard();
                return;
            }

            string archetypeName = ResolveArchetypeDisplayName(resolvedType);
            Theme.BeginPanel(Theme.StatusWarn);
            GUILayout.Label(ConvaiActionsEditorStrings.BuildBehaviorResolvableBody(archetypeName), Theme.BodyWrapped);
            GUILayout.Space(8f);
            GUIContent addAndBind = ConvaiActionsEditorStrings.BuildAddAndBindButton(archetypeName);
            Rect buttonRect = GUILayoutUtility.GetRect(240f, 26f, GUILayout.Width(240f), GUILayout.Height(26f));
            if (Theme.PrimaryButton(buttonRect, addAndBind))
                AddAndBindBehavior(source, definition, resolvedType);
            Theme.EndPanel(0f);
            Theme.EndCard();
        }

        /// <summary>
        ///     Scene Behavior for an action owned by a shared <see cref="ConvaiActionSet" />. An asset
        ///     cannot reference a scene component, so the set stores the behavior's <em>type</em>
        ///     (<see cref="ConvaiActionDefinition.ExecutorTypeHint" />) and each character using the set
        ///     supplies its own matching component at connect time. This body therefore splits cleanly
        ///     in two: the type choice (shared — edits the asset) and how that type currently resolves
        ///     against the character on screen (local — only ever adds a component to this character).
        /// </summary>
        private void DrawSharedBehaviorBody(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            ConvaiActionDefinition definition = row.Definition;
            string hint = definition.ExecutorTypeHint;
            Type resolvedType = null;
            bool hintResolves = !string.IsNullOrWhiteSpace(hint) && ConvaiActionExecutorBinder.TryResolveType(hint, out resolvedType);

            GUILayout.Label(ConvaiActionsEditorStrings.SetActionBehaviorLabel, Theme.MicroLabel);
            GUILayout.Space(2f);

            GUIContent choice = hintResolves
                ? ConvaiActionsEditorStrings.BuildSetBehaviorChoice(ResolveArchetypeDisplayName(resolvedType))
                : ConvaiActionsEditorStrings.ChooseSetBehaviorButton;
            Rect choiceRect = GUILayoutUtility.GetRect(260f, 24f, GUILayout.Width(260f), GUILayout.Height(24f));
            if (Theme.GhostButton(choiceRect, choice))
                ShowSetBehaviorDropdown(row);

            GUILayout.Space(8f);

            if (string.IsNullOrWhiteSpace(hint))
            {
                Theme.BeginPanel(Theme.StatusError);
                GUILayout.Label(ConvaiActionsEditorStrings.SetBehaviorUnset, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            if (!hintResolves)
            {
                Theme.BeginPanel(Theme.StatusError);
                GUILayout.Label(ConvaiActionsEditorStrings.SetBehaviorUnresolvable, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            string archetypeName = ResolveArchetypeDisplayName(resolvedType);
            var candidate = source.GetComponentInChildren(resolvedType, true) as MonoBehaviour;
            if (candidate != null)
            {
                Theme.BeginPanel(Theme.StatusReady);
                GUILayout.Label(
                    ConvaiActionsEditorStrings.BuildSetBehaviorResolved(archetypeName, candidate.gameObject.name),
                    Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            Theme.BeginPanel(Theme.StatusWarn);
            GUILayout.Label(
                ConvaiActionsEditorStrings.BuildSetBehaviorMissingOnCharacter(archetypeName), Theme.BodyWrapped);
            GUILayout.Space(8f);
            GUIContent addButton = ConvaiActionsEditorStrings.BuildAddBehaviorToCharacterButton(archetypeName);
            Rect addRect = GUILayoutUtility.GetRect(280f, 26f, GUILayout.Width(280f), GUILayout.Height(26f));
            if (Theme.PrimaryButton(addRect, addButton))
            {
                // Scene-side only: adds the component to the character so it can perform the shared
                // action. The set asset is untouched — its type hint already names this behavior.
                ConvaiActionBehaviorHosting.AddBehavior(source, resolvedType);
            }

            Theme.EndPanel(0f);
        }

        /// <summary>
        ///     Behavior picker for a shared action: offers the shipped archetype catalog by friendly
        ///     name and writes the chosen type's name into the set asset's type hint. Deliberately not
        ///     the scene-hierarchy picker used for inline actions — an asset must never hold a scene
        ///     reference, and the choice here is meant to apply to every character using the set.
        /// </summary>
        private void ShowSetBehaviorDropdown(ConvaiActionRow row)
        {
            ConvaiActionDefinition definition = row.Definition;
            IReadOnlyList<ConvaiActionArchetypeCatalogEntry> entries = ConvaiActionArchetypeCatalog.Entries;
            var menu = new GenericMenu();
            if (entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(ConvaiActionsEditorStrings.NoBehaviorCandidates.text));
                menu.ShowAsContext();
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                ConvaiActionArchetypeCatalogEntry entry = entries[i];
                bool isCurrent = string.Equals(definition.ExecutorTypeHint, entry.ExecutorType.Name, StringComparison.Ordinal);
                menu.AddItem(new GUIContent(entry.DisplayName), isCurrent, () =>
                {
                    Undo.RecordObject(row.OwningSet, "Choose Action Behavior");
                    definition.ExecutorTypeHint = entry.ExecutorType.Name;
                    MarkDirty(row.OwningSet);
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        private static void DrawBoundBehaviorStatus(Type executorType, string gameObjectName)
        {
            string archetypeName = ResolveArchetypeDisplayName(executorType);
            Theme.BeginPanel(Theme.StatusReady);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect dotRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
                Theme.StatusDot(dotRect, Theme.StatusReady, true);
                GUILayout.Space(2f);
                GUILayout.Label(
                    ConvaiActionsEditorStrings.BuildBehaviorBoundStatus(archetypeName, executorType.Name, gameObjectName),
                    Theme.BodyWrapped);
            }

            Theme.EndPanel();
        }

        private static string ResolveArchetypeDisplayName(Type executorType)
        {
            ConvaiActionArchetypeCatalogEntry entry = ConvaiActionArchetypeCatalog.FindByExecutorType(executorType);
            return string.IsNullOrEmpty(entry?.DisplayName) ? executorType.Name : entry.DisplayName;
        }

        private static void AddAndBindBehavior(ConvaiActionConfigSource source, ConvaiActionDefinition definition, Type resolvedType)
        {
            Undo.RecordObject(source, "Add Action Behavior");
            MonoBehaviour component = ConvaiActionBehaviorHosting.AddBehavior(source, resolvedType);
            if (component != null)
                definition.Executor = component;
            MarkDirty(source);
        }

        private void DrawBehaviorEditRow(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var picked = (MonoBehaviour)EditorGUILayout.ObjectField(
                    ConvaiActionsEditorStrings.BehaviorObjectFieldFallback, definition.Executor, typeof(MonoBehaviour), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (picked == null || picked is IConvaiActionExecutor)
                    {
                        Undo.RecordObject(source, "Assign Action Behavior");
                        definition.Executor = picked;
                        MarkDirty(source);
                    }
                    else
                    {
                        ConvaiLogger.Warning(
                            $"[ConvaiActionsEditorWindow] '{picked.GetType().Name}' does not implement the Action Behavior contract (IConvaiActionExecutor).",
                            LogCategory.Editor);
                    }
                }

                GUILayout.Space(4f);
                Rect chooseRect = GUILayoutUtility.GetRect(22f, 19f, GUILayout.Width(22f));
                if (Theme.IconButton(chooseRect, ConvaiActionsEditorStrings.ChooseBehaviorButton))
                    ShowBehaviorDropdown(source, definition);
            }
        }

        private static void ShowBehaviorDropdown(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            List<MonoBehaviour> candidates = FindExecutorsInHierarchy(source.gameObject);
            var menu = new GenericMenu();
            if (candidates.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(ConvaiActionsEditorStrings.NoBehaviorCandidates.text));
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    MonoBehaviour candidate = candidates[i];
                    string label = $"{ResolveArchetypeDisplayName(candidate.GetType())} ({candidate.gameObject.name})";
                    bool isCurrent = definition.Executor == candidate;
                    menu.AddItem(new GUIContent(label), isCurrent, () =>
                    {
                        Undo.RecordObject(source, "Assign Action Behavior");
                        definition.Executor = candidate;
                        MarkDirty(source);
                    });
                }
            }

            menu.ShowAsContext();
        }

        private static List<MonoBehaviour> FindExecutorsInHierarchy(GameObject root)
        {
            var results = new List<MonoBehaviour>();
            if (root == null)
                return results;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IConvaiActionExecutor)
                    results.Add(behaviours[i]);

            return results;
        }

        #endregion

        #region Advanced

        private void DrawAdvancedCard(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            ConvaiActionDefinition definition = row.Definition;
            Theme.BeginCard();

            _advancedExpanded = Theme.CollapsibleSectionHeader(
                AdvancedGlyph, ConvaiActionsEditorStrings.AdvancedFoldout, _advancedExpanded);

            if (!_advancedExpanded)
            {
                Theme.EndCard();
                return;
            }

            GUILayout.Space(8f);

            UnityEngine.Object owner = OwnerOf(source, row);

            DrawParameters(owner, definition);
            EditorGUILayout.Space(8f);

            EditorGUI.BeginChangeCheck();
            float newTimeout = EditorGUILayout.FloatField(ConvaiActionsEditorStrings.TimeoutField, definition.TimeoutSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Edit Action Timeout");
                definition.TimeoutSeconds = Mathf.Max(0f, newTimeout);
                MarkDirty(owner);
            }

            EditorGUI.BeginChangeCheck();
            var newPolicy = (ConvaiActionFailurePolicyOverride)EditorGUILayout.EnumPopup(
                ConvaiActionsEditorStrings.FailurePolicyField, definition.FailurePolicyOverride);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Edit Failure Policy");
                definition.FailurePolicyOverride = newPolicy;
                MarkDirty(owner);
            }

            EditorGUI.BeginChangeCheck();
            bool newWait = EditorGUILayout.Toggle(ConvaiActionsEditorStrings.SpeechGateField, definition.WaitForBotSpeech);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Edit Speech Gate");
                definition.WaitForBotSpeech = newWait;
                MarkDirty(owner);
            }

            EditorGUI.BeginChangeCheck();
            float newDelay = EditorGUILayout.FloatField(
                ConvaiActionsEditorStrings.SpeechGateDelayField, definition.DelayAfterBotSpeechSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(owner, "Edit Speech Gate Delay");
                definition.DelayAfterBotSpeechSeconds = Mathf.Max(0f, newDelay);
                MarkDirty(owner);
            }

            Theme.EndCard();
        }

        /// <summary>Stand-in drawn when a definition has no parameter list yet, so drawing never creates one.</summary>
        private static readonly List<ConvaiActionParameterDefinition> NoParameters = new();

        private void DrawParameters(UnityEngine.Object owner, ConvaiActionDefinition definition)
        {
            GUILayout.Label(ConvaiActionsEditorStrings.ParametersLabel, Theme.MicroLabel);
            GUILayout.Space(4f);

            // Read only: a missing list is drawn as empty and is created below, inside the
            // Undo-recorded add, so a repaint never writes to the serialized owner.
            List<ConvaiActionParameterDefinition> parameters = definition.Parameters ?? NoParameters;

            if (parameters.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(12f);
                    GUILayout.Label(ConvaiActionsEditorStrings.ParameterNameField, Theme.MicroLabel, GUILayout.MinWidth(60f));
                    GUILayout.Label(ConvaiActionsEditorStrings.ParameterTypeField, Theme.MicroLabel, GUILayout.Width(90f));
                    GUILayout.Label(ConvaiActionsEditorStrings.ParameterConnectorField, Theme.MicroLabel, GUILayout.Width(80f));
                    GUILayout.Space(76f);
                }
            }

            for (int p = 0; p < parameters.Count; p++)
            {
                ConvaiActionParameterDefinition parameter = parameters[p];
                Theme.BeginPanel(null);
                if (DrawParameterRow(owner, parameters, p))
                {
                    Theme.EndPanel();
                    break; // The list shape changed (removed); redraw next frame.
                }

                EditorGUI.BeginChangeCheck();
                string newDescription = Theme.ProseField(
                    ConvaiActionsEditorStrings.ParameterDescriptionField,
                    parameter.Description,
                    "Action.Parameter.Description:" + p,
                    ConvaiActionsEditorStrings.ParameterDescriptionPlaceholder);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(owner, "Edit Parameter Description");
                    parameter.Description = newDescription;
                    MarkDirty(owner);
                }

                if (parameter.Type == ConvaiActionParameterType.Choice)
                    DrawChoicesEditor(owner, parameter);

                Theme.EndPanel();
            }

            Rect addRect = GUILayoutUtility.GetRect(130f, 24f, GUILayout.Width(130f), GUILayout.Height(24f));
            if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.AddParameterButton))
            {
                Undo.RecordObject(owner, "Add Parameter");
                parameters = definition.Parameters ??= new List<ConvaiActionParameterDefinition>();
                parameters.Add(new ConvaiActionParameterDefinition
                {
                    Name = string.Empty,
                    Description = string.Empty,
                    Type = ConvaiActionParameterType.Auto,
                    Connector = string.Empty,
                    Choices = new List<string>()
                });
                MarkDirty(owner);
            }
        }

        /// <summary>Draws one parameter's name/type/connector/reorder/remove row. Returns true when the parameter was just removed.</summary>
        private bool DrawParameterRow(UnityEngine.Object owner, List<ConvaiActionParameterDefinition> parameters, int p)
        {
            ConvaiActionParameterDefinition parameter = parameters[p];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(parameter.Name, GUILayout.MinWidth(60f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(owner, "Edit Parameter Name");
                    parameter.Name = newName;
                    MarkDirty(owner);
                }

                EditorGUI.BeginChangeCheck();
                var newType = (ConvaiActionParameterType)EditorGUILayout.EnumPopup(parameter.Type, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(owner, "Edit Parameter Type");
                    parameter.Type = newType;
                    MarkDirty(owner);
                }

                using (new EditorGUI.DisabledScope(p == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    string newConnector = EditorGUILayout.TextField(parameter.Connector, GUILayout.Width(80f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(owner, "Edit Parameter Connector");
                        parameter.Connector = newConnector;
                        MarkDirty(owner);
                    }
                }

                using (new EditorGUI.DisabledScope(p == 0))
                {
                    Rect upRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                    if (Theme.IconButton(upRect, ConvaiActionsEditorStrings.MoveActionUpButton))
                    {
                        Undo.RecordObject(owner, "Reorder Parameters");
                        (parameters[p], parameters[p - 1]) = (parameters[p - 1], parameters[p]);
                        MarkDirty(owner);
                    }
                }

                using (new EditorGUI.DisabledScope(p == parameters.Count - 1))
                {
                    Rect downRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                    if (Theme.IconButton(downRect, ConvaiActionsEditorStrings.MoveActionDownButton))
                    {
                        Undo.RecordObject(owner, "Reorder Parameters");
                        (parameters[p], parameters[p + 1]) = (parameters[p + 1], parameters[p]);
                        MarkDirty(owner);
                    }
                }

                Rect removeRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.RemoveParameterButton))
                {
                    Undo.RecordObject(owner, "Remove Parameter");
                    parameters.RemoveAt(p);
                    MarkDirty(owner);
                    return true;
                }
            }

            return false;
        }

        private static void DrawChoicesEditor(UnityEngine.Object owner, ConvaiActionParameterDefinition parameter)
        {
            List<string> choices = parameter.Choices ??= new List<string>();
            GUILayout.Space(4f);
            GUILayout.Label(ConvaiActionsEditorStrings.ParameterChoicesField, Theme.MicroLabel);

            for (int c = 0; c < choices.Count; c++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string newChoice = EditorGUILayout.TextField(choices[c]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(owner, "Edit Choice");
                        choices[c] = newChoice;
                        MarkDirty(owner);
                    }

                    Rect removeRect = GUILayoutUtility.GetRect(20f, 18f, GUILayout.Width(20f));
                    if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.RemoveParameterButton))
                    {
                        Undo.RecordObject(owner, "Remove Choice");
                        choices.RemoveAt(c);
                        MarkDirty(owner);
                        break;
                    }
                }
            }

            Rect addRect = GUILayoutUtility.GetRect(100f, 20f, GUILayout.Width(100f), GUILayout.Height(20f));
            if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.AddChoiceButton))
            {
                Undo.RecordObject(owner, "Add Choice");
                choices.Add(string.Empty);
                MarkDirty(owner);
            }
        }

        #endregion

        #region Footer

        private void DrawFooter(IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics)
        {
            (int errors, int warnings) = ConvaiActionsEditorModel.Summarize(diagnostics);
            bool healthy = errors == 0 && warnings == 0;
            Color statusColor = healthy ? Theme.StatusReady : errors > 0 ? Theme.StatusError : Theme.StatusWarn;

            Rect reserved = GUILayoutUtility.GetRect(0f, FooterHeight, GUILayout.ExpandWidth(true));
            var bar = new Rect(0f, reserved.y, position.width, position.height - reserved.y);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bar, Theme.HeroBg);
                EditorGUI.DrawRect(bar, Theme.Fade(statusColor, 0.05f));
                EditorGUI.DrawRect(new Rect(0f, bar.y, bar.width, 1f), Theme.Divider);
            }

            float centerY = bar.y + (FooterHeight * 0.5f);
            Theme.StatusDot(new Vector2(20f, centerY), statusColor, !healthy);

            GUIContent summary = healthy
                ? ConvaiActionsEditorStrings.StatusStripAllReady
                : ConvaiActionsEditorStrings.BuildFooterIssueSummary(warnings, errors);
            GUI.Label(new Rect(32f, bar.y, bar.width - 160f, FooterHeight), summary, Theme.FooterLabel);

            var troubleshooterRect = new Rect(bar.width - 118f, centerY - 10f, 104f, 20f);
            if (Theme.GhostButton(troubleshooterRect, ConvaiActionsEditorStrings.OpenTroubleshooterButton))
                ConvaiActionTroubleshooterWindow.ShowWindow(_character);
        }

        #endregion

        #region Empty / onboarding states

        private void DrawCenteredState(GUIContent title, GUIContent body, GUIContent buttonContent, Action buttonAction)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                GUILayout.FlexibleSpace();
                DrawCenteredBrandIcon();
                GUILayout.Space(14f);
                GUILayout.Label(title, Theme.CenteredTitle);
                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(body, Theme.CenteredBody, GUILayout.MaxWidth(440f));
                    GUILayout.FlexibleSpace();
                }

                if (buttonContent != null && buttonAction != null)
                {
                    GUILayout.Space(18f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        Rect buttonRect = GUILayoutUtility.GetRect(190f, 30f, GUILayout.Width(190f), GUILayout.Height(30f));
                        if (Theme.PrimaryButton(buttonRect, buttonContent))
                            buttonAction();
                        GUILayout.FlexibleSpace();
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawCenteredBrandIcon()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect iconRect = GUILayoutUtility.GetRect(44f, 44f, GUILayout.Width(44f), GUILayout.Height(44f));
                Texture2D icon = UI.ConvaiEditorIcons.Emblem();
                if (icon != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUILayout.FlexibleSpace();
            }
        }

        private void EnableActions()
        {
            Undo.AddComponent<ConvaiActionConfigSource>(_character.gameObject);
            EditorUtility.SetDirty(_character.gameObject);
        }

        private void DrawEmptyState(ConvaiActionConfigSource source)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                GUILayout.FlexibleSpace();
                DrawCenteredBrandIcon();
                GUILayout.Space(14f);
                GUILayout.Label(ConvaiActionsEditorStrings.EmptyStateTitle, Theme.CenteredTitle);
                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(ConvaiActionsEditorStrings.EmptyStateSubtitle, Theme.CenteredBody, GUILayout.MaxWidth(440f));
                    GUILayout.FlexibleSpace();
                }

                // Which behaviors appear here is declared by the behaviors themselves
                // (ConvaiActionArchetypeAttribute.FeaturedOrder), not decided in this window — see
                // DrawStarterCard. A library with no declared starters simply shows none, and the
                // "browse the full catalog" link below still gets the author where they are going.
                List<ConvaiActionArchetypeCatalogEntry> starters =
                    ConvaiActionArchetypeCatalog.FeaturedEntries(MaximumStarterCards);
                if (starters.Count > 0)
                {
                    GUILayout.Space(24f);
                    for (int rowStart = 0; rowStart < starters.Count; rowStart += 2)
                    {
                        if (rowStart > 0)
                            GUILayout.Space(14f);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            int rowEnd = Mathf.Min(rowStart + 2, starters.Count);
                            for (int i = rowStart; i < rowEnd; i++)
                            {
                                if (i > rowStart)
                                    GUILayout.Space(14f);

                                // The same decorative mark on every card: colour and status glyphs in this
                                // window mean ready/needs attention/broken, and none of these is added yet.
                                DrawStarterCard(source, starters[i], Glyphs.Blink);
                            }

                            GUILayout.FlexibleSpace();
                        }
                    }
                }

                GUILayout.Space(20f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(ConvaiActionsEditorStrings.BrowseAllActionsLink, Theme.Link))
                        ShowAddActionMenu(source);
                    GUILayout.Space(24f);
                    if (GUILayout.Button(ConvaiActionsEditorStrings.EmptyStateTutorialLink, Theme.Link))
                        OpenTutorialDoc();
                    GUILayout.FlexibleSpace();
                }

                // The Action Set path must exist here too: a character with no actions is exactly the
                // one most likely to want a ready-made shared set, and this hero replaces the whole
                // left pane — without this, "+ Use an Action Set" is unreachable from the empty state.
                GUILayout.Space(26f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(ConvaiActionsEditorStrings.ActionSetsExplainer, Theme.CenteredBody, GUILayout.MaxWidth(440f));
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(ConvaiActionsEditorStrings.AddActionSetLink, Theme.Link))
                        ShowAddActionSetMenu(source);
                    GUILayout.FlexibleSpace();
                }

                GUILayout.FlexibleSpace();
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>
        ///     Draws one ready-made starter, described entirely by the archetype itself: its display
        ///     name, its compact featured description, and the fact that it wanted to be a starter at all.
        ///     This window names no behavior — reshaping the shipped library never edits this file.
        /// </summary>
        private void DrawStarterCard(ConvaiActionConfigSource source, ConvaiActionArchetypeCatalogEntry entry, string glyph)
        {
            Rect card = GUILayoutUtility.GetRect(168f, 132f, GUILayout.Width(168f), GUILayout.Height(132f));
            bool hover = card.Contains(Event.current.mousePosition);

            Theme.FillRounded(card, hover ? Theme.CardBgHover : Theme.CardBg, 8f);
            Theme.StrokeRounded(card,
                hover ? Theme.Fade(Theme.Accent, 0.85f) : Theme.CardBorder, 8f);

            var name = new GUIContent(entry.DisplayName, entry.Description);
            GUI.Label(new Rect(card.x, card.y + 12f, card.width, 26f), glyph, Theme.StarterGlyph);
            GUI.Label(new Rect(card.x + 8f, card.y + 44f, card.width - 16f, 18f), entry.DisplayName, Theme.StarterName);
            GUI.Label(new Rect(card.x + 12f, card.y + 66f, card.width - 24f, 58f),
                entry.FeaturedDescription, Theme.StarterDesc);

            EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
            if (GUI.Button(card, name, Theme.InvisibleButton))
                _selectedDefinition = AddArchetypeAction(source, entry);
        }

        /// <summary>
        ///     Opens the published tutorial in the browser, like every other documentation link in
        ///     the SDK's editor surfaces. Deliberately not the packaged Markdown file: revealing a
        ///     <c>.md</c> in the OS file browser hands someone who has never opened this SDK before a
        ///     file to figure out how to read, which is the opposite of what "open the tutorial"
        ///     promises.
        /// </summary>
        private static void OpenTutorialDoc() =>
            UnityEngine.Application.OpenURL(Utilities.ConvaiEditorLinks.DocsUnitySdkUrl);

        #endregion

        #region Add Action / mutation helpers

        private static void ShowAddActionMenu(ConvaiActionConfigSource source)
        {
            var menu = new GenericMenu();
            PopulateArchetypeMenu(menu, entry => AddArchetypeAction(source, entry));

            menu.AddItem(ConvaiActionsEditorStrings.CustomActionMenuItem, false, () => AddCustomAction(source));
            menu.ShowAsContext();
        }

        private static void PopulateArchetypeMenu(
            GenericMenu menu,
            Action<ConvaiActionArchetypeCatalogEntry> onSelected)
        {
            List<ConvaiActionArchetypeMenuItem> items = ConvaiActionArchetypeCatalog.BuildMenuItems();
            bool hasPreviousSection = false;
            for (int i = 0; i < items.Count; i++)
            {
                ConvaiActionArchetypeMenuItem item = items[i];
                if (item.StartsSection)
                {
                    if (hasPreviousSection)
                        menu.AddSeparator(string.Empty);
                    if (!string.IsNullOrWhiteSpace(item.SectionHeader))
                        menu.AddDisabledItem(new GUIContent(item.SectionHeader));
                    hasPreviousSection = true;
                }

                ConvaiActionArchetypeCatalogEntry entry = item.Entry;
                menu.AddItem(new GUIContent(item.MenuPath), false, () => onSelected(entry));
            }

            if (items.Count > 0)
                menu.AddSeparator(string.Empty);
        }

        private static ConvaiActionDefinition AddArchetypeAction(ConvaiActionConfigSource source, ConvaiActionArchetypeCatalogEntry entry)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Convai Action");

            try
            {
                Undo.RecordObject(source, "Add Convai Action");
                ConvaiActionDefinition definition = entry.BuildDefinition();

                // Searches the whole character, not just the root, so a behavior the author has moved
                // onto the action behaviors object is reused rather than duplicated beside it.
                var existingExecutor = source.GetComponentInChildren(entry.ExecutorType, true) as MonoBehaviour;
                existingExecutor ??= ConvaiActionBehaviorHosting.AddBehavior(source, entry.ExecutorType);
                if (existingExecutor == null)
                    throw new InvalidOperationException($"Could not add {entry.ExecutorType.Name}.");

                // Character-side requirements are deterministic: the archetype names the exact
                // component and there is only one character being authored. Provision them now so a
                // catalog action does not begin life knowingly broken. Target-side requirements stay
                // in Setup Health because choosing which scene object owns them is an authoring choice.
                Type peerType = ConvaiComponentTypeResolver.Resolve(entry.RequiredPeerHint);
                if (peerType != null && source.GetComponentInChildren(peerType, true) == null)
                {
                    Component peer = Undo.AddComponent(source.gameObject, peerType);
                    if (peer == null)
                        throw new InvalidOperationException($"Could not add required component {peerType.Name}.");
                    EditorUtility.SetDirty(source.gameObject);
                }

                definition.Executor = existingExecutor;
                definition.ExecutorTypeHint = entry.ExecutorType.Name;

                var definitions = new List<ConvaiActionDefinition>(source.Definitions) { definition };
                source.ReplaceDefinitions(definitions);
                MarkDirty(source);
                Undo.CollapseUndoOperations(undoGroup);
                return definition;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogError($"[Convai Actions] The action was not added: {exception.Message}", source);
                return null;
            }
        }

        /// <summary>
        ///     "Add action" catalog for a shared set. Mirrors <see cref="ShowAddActionMenu" />, but every
        ///     entry lands in the set asset and carries only an <see cref="ConvaiActionDefinition.ExecutorTypeHint" />:
        ///     an asset cannot reference a scene component, and adding an executor component to the
        ///     currently open character would be a silent, one-character-only side effect of authoring
        ///     something meant to be shared.
        /// </summary>
        private void ShowAddActionToSetMenu(ConvaiActionSet set)
        {
            if (set == null)
                return;

            var menu = new GenericMenu();
            PopulateArchetypeMenu(menu, entry => AddArchetypeActionToSet(set, entry));

            menu.AddItem(ConvaiActionsEditorStrings.CustomActionMenuItem, false, () => AddCustomActionToSet(set));
            menu.ShowAsContext();
        }

        private void AddArchetypeActionToSet(ConvaiActionSet set, ConvaiActionArchetypeCatalogEntry entry)
        {
            Undo.RecordObject(set, "Add Action To Set");
            ConvaiActionDefinition definition = entry.BuildDefinition();
            definition.Executor = null;
            definition.ExecutorTypeHint = entry.ExecutorType.Name;

            var definitions = new List<ConvaiActionDefinition>(set.Definitions) { definition };
            set.ReplaceDefinitions(definitions);
            MarkDirty(set);
            ExpandGroup(ConvaiActionsEditorModel.BuildActionSetGroupKey(set));
            _selectedDefinition = definition;
            Repaint();
        }

        private void AddCustomActionToSet(ConvaiActionSet set)
        {
            Undo.RecordObject(set, "Add Action To Set");
            var definition = new ConvaiActionDefinition
            {
                ActionName = "New Action",
                Parameters = new List<ConvaiActionParameterDefinition>()
            };

            var definitions = new List<ConvaiActionDefinition>(set.Definitions) { definition };
            set.ReplaceDefinitions(definitions);
            MarkDirty(set);
            ExpandGroup(ConvaiActionsEditorModel.BuildActionSetGroupKey(set));
            _selectedDefinition = definition;
            Repaint();
        }

        private static ConvaiActionDefinition AddCustomAction(ConvaiActionConfigSource source)
        {
            Undo.RecordObject(source, "Add Action");
            var definition = new ConvaiActionDefinition
            {
                ActionName = "New Action",
                Parameters = new List<ConvaiActionParameterDefinition>()
            };

            var definitions = new List<ConvaiActionDefinition>(source.Definitions) { definition };
            source.ReplaceDefinitions(definitions);
            MarkDirty(source);
            return definition;
        }

        /// <summary>
        ///     Removes a row's definition from whichever list owns it. A shared row's removal is
        ///     confirmed first — it silently changes every other character using the set, which is not
        ///     something a hover-revealed icon button should do unannounced.
        /// </summary>
        private void RemoveAction(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            if (row.IsShared &&
                !EditorUtility.DisplayDialog(
                    "Remove this shared action?",
                    $"'{row.DisplayName}' will be removed from the Action Set '{row.OwningSet.name}', so " +
                    "every Convai Character using that set loses it.\n\nThis can be undone.",
                    "Remove From Set",
                    "Cancel"))
                return;

            IReadOnlyList<ConvaiActionDefinition> owned = row.OwningSet != null ? row.OwningSet.Definitions : source.Definitions;
            if (row.OwnerIndex < 0 || row.OwnerIndex >= owned.Count)
                return;

            UnityEngine.Object owner = OwnerOf(source, row);
            Undo.RecordObject(owner, "Remove Action");
            var definitions = new List<ConvaiActionDefinition>(owned);
            ConvaiActionDefinition removed = definitions[row.OwnerIndex];
            definitions.RemoveAt(row.OwnerIndex);
            ReplaceOwnedDefinitions(source, row.OwningSet, definitions);
            MarkDirty(owner);

            if (ReferenceEquals(_selectedDefinition, removed))
                _selectedDefinition = null;
        }

        private static void MoveAction(ConvaiActionConfigSource source, ConvaiActionRow row, int delta)
        {
            IReadOnlyList<ConvaiActionDefinition> owned = row.OwningSet != null ? row.OwningSet.Definitions : source.Definitions;
            var definitions = new List<ConvaiActionDefinition>(owned);
            int index = row.OwnerIndex;
            int target = index + delta;
            if (index < 0 || index >= definitions.Count || target < 0 || target >= definitions.Count)
                return;

            UnityEngine.Object owner = OwnerOf(source, row);
            Undo.RecordObject(owner, "Reorder Actions");
            (definitions[index], definitions[target]) = (definitions[target], definitions[index]);
            ReplaceOwnedDefinitions(source, row.OwningSet, definitions);
            MarkDirty(owner);
        }

        /// <summary>Writes a rebuilt definition list back to whichever container owns it.</summary>
        private static void ReplaceOwnedDefinitions(
            ConvaiActionConfigSource source,
            ConvaiActionSet owningSet,
            List<ConvaiActionDefinition> definitions)
        {
            if (owningSet != null)
                owningSet.ReplaceDefinitions(definitions);
            else
                source.ReplaceDefinitions(definitions);
        }

        /// <summary>
        ///     The Unity object that actually owns a row's definition, and therefore the object every
        ///     edit to that row must record Undo against and mark dirty: the owning
        ///     <see cref="ConvaiActionSet" /> asset for a shared row, or the scene
        ///     <see cref="ConvaiActionConfigSource" /> component for an inline row. Getting this wrong
        ///     is silent data loss — recording Undo against the scene component while mutating an
        ///     asset's definition leaves the asset dirty-but-unrecorded, so the edit survives Undo and
        ///     never round-trips.
        /// </summary>
        private static UnityEngine.Object OwnerOf(ConvaiActionConfigSource source, ConvaiActionRow row) =>
            // Explicit cast, not a bare ternary: ConvaiActionSet and ConvaiActionConfigSource have no
            // conversion between them, so an uncast conditional would lean on C# 9 target typing to
            // find UnityEngine.Object. Spelling it out keeps this readable and language-version-proof.
            row.OwningSet != null ? (UnityEngine.Object)row.OwningSet : source;

        /// <summary>
        ///     Marks a definition owner dirty. A <see cref="ConvaiActionSet" /> is a project asset, so
        ///     <see cref="EditorUtility.SetDirty" /> alone is enough (Unity persists it on the next
        ///     asset save); a scene component additionally needs its scene marked so the change is
        ///     offered for saving.
        /// </summary>
        private static void MarkDirty(UnityEngine.Object owner)
        {
            if (owner == null)
                return;

            EditorUtility.SetDirty(owner);
            if (owner is Component component && component.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        #endregion

        #region Action Set assignment

        private void ShowAddActionSetMenu(ConvaiActionConfigSource source)
        {
            var menu = new GenericMenu();
            List<ConvaiActionSet> candidates = FindAssignableActionSets(source);
            if (candidates.Count == 0)
            {
                menu.AddDisabledItem(ConvaiActionsEditorStrings.NoAssignableActionSetsMenuItem);
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ConvaiActionSet candidate = candidates[i];
                    menu.AddItem(new GUIContent(candidate.name), false, () =>
                    {
                        AddActionSet(source, candidate);
                        Repaint();
                    });
                }
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(ConvaiActionsEditorStrings.CreateNewActionSetMenuItem, false, () =>
            {
                CreateAndAssignActionSet(source);
                Repaint();
            });
            menu.ShowAsContext();
        }

        private static List<ConvaiActionSet> FindAssignableActionSets(ConvaiActionConfigSource source)
        {
            var results = new List<ConvaiActionSet>();
            string[] guids = AssetDatabase.FindAssets("t:ConvaiActionSet");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ConvaiActionSet>(path);
                if (asset != null && !ConvaiActionsEditorModel.IsSetAssigned(source, asset))
                    results.Add(asset);
            }

            return results;
        }

        private static void CreateAndAssignActionSet(ConvaiActionConfigSource source)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Action Set",
                "ConvaiActionSet",
                "asset",
                "Create a reusable Action Set asset that can be shared across Convai Characters.");
            if (string.IsNullOrWhiteSpace(path))
                return;

            ConvaiActionSet asset = ConvaiActionSet.CreateDefault();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AddActionSet(source, asset);
            EditorGUIUtility.PingObject(asset);
        }

        private static void AddActionSet(ConvaiActionConfigSource source, ConvaiActionSet set)
        {
            if (set == null || ConvaiActionsEditorModel.IsSetAssigned(source, set))
                return;

            Undo.RecordObject(source, "Use Action Set");
            var sets = new List<ConvaiActionSet>(source.ActionSets) { set };
            source.ReplaceActionSets(sets);
            MarkDirty(source);
        }

        /// <summary>
        ///     Stops using a set on this character. Confirmed first: the set may carry many actions and
        ///     the button is a small icon next to other icons, so an accidental click would silently
        ///     strip a chunk of the character's behavior. The dialog also states plainly that the asset
        ///     survives — the destructive-looking "✕" otherwise reads as "delete the shared asset".
        /// </summary>
        private void RemoveActionSet(ConvaiActionConfigSource source, ConvaiActionSet set)
        {
            if (set == null)
                return;

            int actionCount = set.Definitions.Count;
            string body = actionCount == 0
                ? $"'{set.name}' will no longer be used by '{source.name}'.\n\nThe Action Set asset itself is not deleted."
                : $"'{source.name}' will lose the {actionCount} action{(actionCount == 1 ? string.Empty : "s")} " +
                  $"it gets from '{set.name}'.\n\nThe Action Set asset itself is not deleted, and every other " +
                  "character using it keeps working.";

            if (!EditorUtility.DisplayDialog("Stop using this Action Set?", body, "Stop Using", "Cancel"))
                return;

            Undo.RecordObject(source, "Stop Using Action Set");
            var sets = new List<ConvaiActionSet>(source.ActionSets);
            sets.Remove(set);
            source.ReplaceActionSets(sets);
            MarkDirty(source);
            ExpandGroup(ConvaiActionsEditorModel.BuildActionSetGroupKey(set));

            if (_selectedDefinition != null && IndexOfDefinition(set.Definitions, _selectedDefinition) >= 0)
                _selectedDefinition = null;
        }

        /// <summary>Reference-identity index lookup (<see cref="IReadOnlyList{T}" /> carries no <c>IndexOf</c>).</summary>
        private static int IndexOfDefinition(IReadOnlyList<ConvaiActionDefinition> definitions, ConvaiActionDefinition definition)
        {
            if (definitions == null || definition == null)
                return -1;

            for (int i = 0; i < definitions.Count; i++)
            {
                if (ReferenceEquals(definitions[i], definition))
                    return i;
            }

            return -1;
        }

        #endregion
    }
}
