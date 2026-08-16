using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.AI;
using Convai.Editor.UI;
using Convai.Editor.Utilities;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.Diagnostics
{
    /// <summary>
    ///     The one place a Convai finding is read: what is stopping a character from working, module
    ///     by module, with the fix beside each one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why one window and not a panel per inspector.</b> Authoring already lives in windows
    ///         in this SDK — the Convai Actions inspector says so in its own copy ("Open the
    ///         Actions Editor to change anything"). Diagnosis follows the same shape: the Inspector
    ///         shows the count, this window holds the account. The alternative renders findings twice,
    ///         and the Inspector's column is the narrow one — a message written to fit 260 pixels is
    ///         the message that stops explaining and starts abbreviating, which is how the dead-end
    ///         status chips this window exists to fix were born in the first place.
    ///     </para>
    ///     <para>
    ///         <b>What the window owes the user for taking them out of the Inspector.</b> It arrives
    ///         with the character already selected, expanded and scrolled to whatever they clicked; it
    ///         is one reused instance that never steals focus; every finding can send them back to the
    ///         exact object it is about; and opened cold from the menu it still works, listing every
    ///         Convai Character in the scene. Those five obligations are the difference between a
    ///         destination and a detour.
    ///     </para>
    ///     <para>
    ///         <b>It diagnoses nothing itself.</b> Every finding comes from
    ///         <see cref="ConvaiSetupHealthRegistry" />, which is a projection of each module's own
    ///         check engine. This class is the view.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiTroubleshooterWindow : EditorWindow
    {
        #region Cached content

        private static readonly GUIContent WindowTitle = new("Convai Troubleshooter");

        private static readonly GUIContent HeroTitle = new("Convai Troubleshooter");

        private static readonly GUIContent HeroSubtitle = new(
            "What is stopping this character from working, and how to fix it.");

        private static readonly GUIContent CharacterLabel = new(
            "Character", "The Convai Character being checked.");

        private static readonly GUIContent SceneModeButton = new(
            "This Scene", "Check every Convai Character in the open scene.");

        private static readonly GUIContent CharacterModeButton = new(
            "This Character", "Go back to checking one character.");

        private static readonly GUIContent RecheckButton = new(
            "Re-check", "Run every check again now.");

        private static readonly GUIContent CheckedFineTitle = new("Checked And Fine");

        private static readonly GUIContent NoCharacterTitle = new("Pick A Convai Character");

        private const string NoCharacterBody =
            "Select a Convai Character in the Hierarchy, or drop one into the field above, and this " +
            "window will list anything that would stop it working.";

        private const string SceneEmptyTitle = "No Convai Characters";

        private const string SceneEmptyBody =
            "This scene has no Convai Characters yet. Add one with Convai → Welcome, then come back.";

        private const string FixAllTitle = "Fix Everything That Can Be Fixed";

        #endregion

        #region State

        private const string HostId = "ConvaiTroubleshooterWindow";
        private const string CheckedFineSectionId = "CheckedAndFine";

        private GameObject _character;
        private ConvaiSetupHealthSnapshot _snapshot = ConvaiSetupHealthSnapshot.Empty;

        /// <summary>
        ///     The report as it is drawn — every string composed once, when the report changed.
        /// </summary>
        private ConvaiTroubleshooterView _view = ConvaiTroubleshooterView.Empty;

        /// <summary>Scene-mode rows, and the throttle that keeps the scene sweep off the draw path.</summary>
        private IReadOnlyList<ConvaiTroubleshooterSceneRow> _sceneRows;

        private ConvaiEditorRefreshTimer _sceneTimer;

        /// <summary>A character the user picked from the scene list, switched to after the layout pass.</summary>
        private GameObject _pendingCharacter;
        private Vector2 _scroll;
        private bool _sceneMode;

        /// <summary>The module a deep link asked for, so its fold opens whatever the user left it as.</summary>
        private string _focusModuleId;

        private string _focusFindingId;

        /// <summary>Highlights the deep-linked finding on arrival, and keeps the window repainting until it fades.</summary>
        private ConvaiEditorFlashTimer _flash;

        private string _fixSummary;

        /// <summary>
        ///     What the user pressed this pass, executed once the layout is over.
        /// </summary>
        /// <remarks>
        ///     Running a fix from inside the draw call mutates the scene the pass is measuring, so the
        ///     Repaint event lays out against content the Layout event never saw. Deferring is not
        ///     tidiness — it is the difference between a window that reports and one that corrupts its
        ///     own layout the moment anything is fixed.
        /// </remarks>
        private ConvaiFindingCommand _pending;

        /// <summary>A per-capability Fix All the user pressed, deferred for the same reason.</summary>
        private ConvaiSetupHealthResult _pendingBatch;

        #endregion

        #region Entry points

        [MenuItem("Convai/Troubleshooter", false, ConvaiEditorMenu.Diagnostics + 0)]
        internal static void ShowWindow() => ShowFor(Selection.activeGameObject);

        /// <summary>
        ///     Opens the window on <paramref name="character" />, expanded and scrolled to
        ///     <paramref name="moduleId" /> and flashing <paramref name="findingId" /> when they are
        ///     given. This is what every status chip and issue tile in the SDK calls.
        /// </summary>
        /// <remarks>
        ///     Reuses the open window rather than creating a second one, and does not steal focus in
        ///     Play mode — a diagnostic surface that grabs the editor mid-test is worse than one that
        ///     waits its turn.
        /// </remarks>
        internal static void ShowFor(GameObject character, string moduleId = null, string findingId = null)
        {
            ConvaiTroubleshooterWindow window = GetWindow<ConvaiTroubleshooterWindow>(
                utility: false, title: WindowTitle.text, focus: !EditorApplication.isPlaying);
            window.titleContent = WindowTitle;
            window.minSize = new Vector2(520f, 380f);

            GameObject resolved = ResolveCharacter(character);
            if (resolved != null)
            {
                window._character = resolved;
                window._sceneMode = false;
            }

            window._focusModuleId = moduleId;
            window._focusFindingId = findingId;
            window._flash.Start();

            if (!string.IsNullOrEmpty(moduleId))
                ConvaiEditorSectionState.Set(HostId, moduleId, true);

            window._fixSummary = null;
            window.Evaluate();
            window.Show();
            window.Repaint();
        }

        /// <summary>Opens the window for the character owning <paramref name="component" />.</summary>
        internal static void ShowFor(Component component, string moduleId = null, string findingId = null) =>
            ShowFor(component != null ? component.gameObject : null, moduleId, findingId);

        /// <summary>
        ///     The Convai Character behind whatever the caller had — the object itself, or the nearest
        ///     ancestor carrying the component. A user who clicks a chip on a child object still means
        ///     "check this character".
        /// </summary>
        private static GameObject ResolveCharacter(GameObject candidate)
        {
            if (candidate == null)
                return null;

            var character = candidate.GetComponentInParent<ConvaiCharacter>();
            return character != null ? character.gameObject : null;
        }

        #endregion

        private void OnEnable()
        {
            if (_character == null)
                _character = ResolveCharacter(Selection.activeGameObject) ?? FindOnlyCharacter();

            Evaluate();
        }

        /// <summary>
        ///     Follows the Hierarchy selection, because a user who selects a different character and
        ///     reads a report about the previous one has been told something false. Selecting
        ///     something that is not a character leaves the window where it was, rather than emptying
        ///     it every time the user clicks a light.
        /// </summary>
        private void OnSelectionChange()
        {
            GameObject selected = ResolveCharacter(Selection.activeGameObject);
            if (selected == null || selected == _character)
                return;

            _character = selected;
            _sceneMode = false;
            _fixSummary = null;
            ClearFocus();
            Evaluate();
            Repaint();
        }

        private void OnGUI()
        {
            Theme.EnsureStyles();
            Theme.Fill(new Rect(0f, 0f, position.width, position.height), Theme.WindowBg);

            DrawHero();

            using (new EditorGUILayout.VerticalScope(Styles.PaneContent))
            {
                if (_sceneMode)
                {
                    DrawSceneList();
                    return;
                }

                if (_character == null)
                {
                    Frame.InfoBox(NoCharacterTitle.text, NoCharacterBody);
                    return;
                }

                if (_fixSummary != null)
                    Frame.InfoBox(FixAllTitle, _fixSummary);

                DrawReport();
                DrawFooter();
            }

            _view.RefreshFreshness(EditorApplication.timeSinceStartup);
            ExecutePending();

            // The flash is the only thing on this window that changes without input, so it is the only
            // reason to repaint continuously — and only while it is running.
            _flash.KeepAlive(this);
        }

        /// <summary>
        ///     Carries out whatever the user pressed, now that nothing is mid-layout.
        /// </summary>
        private void ExecutePending()
        {
            if (_pendingCharacter != null)
            {
                _character = _pendingCharacter;
                _pendingCharacter = null;
                _sceneMode = false;
                _fixSummary = null;
                ClearFocus();
                Evaluate();
                return;
            }

            if (_pendingBatch != null)
            {
                ConvaiSetupHealthResult batch = _pendingBatch;
                _pendingBatch = null;
                RunFixes(CollectFixes(batch, visibleOnly: true), batch.DisplayName);
                return;
            }

            if (!_pending.HasCommand)
                return;

            ConvaiFindingCommand command = _pending;
            _pending = default;

            switch (command.Kind)
            {
                case ConvaiFindingCommandKind.Fix:
                    RunFixes(new List<ConvaiSetupFinding> { command.Finding }, command.Finding.Title);
                    break;
                case ConvaiFindingCommandKind.Locate:
                    ShowMe(command.Finding);
                    break;
                case ConvaiFindingCommandKind.Open:
                    command.Finding.Open();
                    break;
                case ConvaiFindingCommandKind.Docs:
                    // Fully qualified: this SDK has its own Convai.Application namespace, so a bare
                    // Application inside a Convai.* namespace resolves to that and not to Unity's.
                    UnityEngine.Application.OpenURL(command.Finding.DocsUrl);
                    break;
            }
        }

        #region Hero

        private void DrawHero()
        {
            Rect band = Theme.WindowHero(
                position.width, HeroTitle, HeroSubtitle, HeaderChip(), HeaderChipTint(),
                ConvaiEditorLinks.DocsUnitySdkUrl, extraHeight: 28f);

            var row = new Rect(
                band.x + Tokens.HeaderEdgeInset,
                Tokens.WindowHeroHeight - 4f,
                band.width - (Tokens.HeaderEdgeInset * 2f),
                22f);

            float modeWidth = 108f;
            var modeRect = new Rect(row.xMax - modeWidth, row.y, modeWidth, row.height);
            if (Controls.GhostButton(modeRect, _sceneMode ? CharacterModeButton : SceneModeButton))
            {
                _sceneMode = !_sceneMode;
                _fixSummary = null;
                ClearFocus();
                Evaluate();
            }

            if (_sceneMode)
                return;

            var pickerRect = new Rect(row.x, row.y, modeRect.x - row.x - 8f, row.height);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 70f;
            var picked = (ConvaiCharacter)EditorGUI.ObjectField(
                pickerRect, CharacterLabel,
                _character != null ? _character.GetComponent<ConvaiCharacter>() : null,
                typeof(ConvaiCharacter), true);
            EditorGUIUtility.labelWidth = labelWidth;

            GameObject pickedObject = picked != null ? picked.gameObject : null;
            if (pickedObject == _character)
                return;

            _character = pickedObject;
            _fixSummary = null;
            ClearFocus();
            Evaluate();
        }

        private GUIContent HeaderChip() => _sceneMode || _character == null ? null : _view.Chip;

        private Color HeaderChipTint() => _view.ChipTint;

        #endregion

        #region One character

        private void DrawReport()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            IReadOnlyList<ConvaiTroubleshooterModuleView> modules = _view.Modules;
            if (modules.Count == 0)
            {
                Frame.InfoBox(
                    "Nothing To Check",
                    "No Convai capability reported anything about this character. That usually means " +
                    "the character has only just been created — open Convai → Welcome to finish setting it up.");
                EditorGUILayout.EndScrollView();
                return;
            }

            for (int i = 0; i < modules.Count; i++)
                DrawModule(modules[i]);

            DrawCheckedAndFine();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        ///     One module's section. Opens by default when it has an error and stays closed otherwise,
        ///     so a beginner with a broken character cannot miss it and everyone else meets a quiet
        ///     window; a deep link always wins over the persisted fold, because a user who clicked a
        ///     count must never land on a collapsed section.
        /// </summary>
        private void DrawModule(ConvaiTroubleshooterModuleView module)
        {
            string moduleId = module.Result.ModuleId;
            bool focused = string.Equals(moduleId, _focusModuleId, StringComparison.Ordinal);
            bool expanded = ConvaiEditorSectionState.Get(HostId, moduleId, module.Result.IssueCount > 0) || focused;

            Frame.BeginCard();

            bool now = Frame.SectionHeaderRow(
                Glyphs.Section, module.Title, expanded, module.Accent, module.Summary);
            if (now != expanded)
            {
                ConvaiEditorSectionState.Set(HostId, moduleId, now);
                if (!now)
                    _focusModuleId = null;
            }

            if (now)
                DrawModuleBody(module);

            Frame.EndCard(8f);
        }

        private void DrawModuleBody(ConvaiTroubleshooterModuleView module)
        {
            if (module.Blocker != null)
            {
                GUILayout.Label(module.Blocker, Styles.MutedWrapped);
                GUILayout.Space(6f);
            }

            bool flashing = _flash.IsRunning;
            IReadOnlyList<ConvaiFindingRowView> issues = module.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                bool flash = flashing &&
                             string.Equals(issues[i].Finding.Id, _focusFindingId, StringComparison.Ordinal);

                ConvaiFindingCommand command = ConvaiFindingView.Draw(issues[i], flash);
                if (command.HasCommand)
                    _pending = command;
            }

            if (module.RestingState != null)
                GUILayout.Label(module.RestingState, Styles.MutedWrapped);

            if (module.FixTheseButton == null)
                return;

            GUILayout.Space(4f);
            Rect fixAll = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (Controls.GhostButton(fixAll, module.FixTheseButton))
                _pendingBatch = module.Result;
        }

        /// <summary>
        ///     Everything that passed, collapsed. A user who is told "Ready" while nothing visibly
        ///     happens has no way to trust the word; being able to read what was actually checked is
        ///     what turns a green chip into an answer.
        /// </summary>
        private void DrawCheckedAndFine()
        {
            // The list and its text come from the view model. Gathering the passes and composing their
            // rows here meant doing it on every mouse move over the window, for content that changes
            // only when the report does.
            IReadOnlyList<ConvaiFindingRowView> passed = _view.Passed;
            if (passed.Count == 0)
                return;

            Frame.BeginCard();
            bool expanded = ConvaiEditorSectionState.Get(HostId, CheckedFineSectionId, false);
            bool now = Frame.SectionHeaderRow(
                Glyphs.Status.Ok, CheckedFineTitle, expanded, Theme.StatusReady, _view.PassedCountLabel);
            if (now != expanded)
                ConvaiEditorSectionState.Set(HostId, CheckedFineSectionId, now);

            if (now)
            {
                for (int i = 0; i < passed.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(
                            passed[i].Headline, Styles.ReadingLabel,
                            GUILayout.Width(Tokens.ReadingLabelWidth + 40f));
                        GUILayout.Label(passed[i].Message, Styles.MutedWrapped);
                    }
                }
            }

            Frame.EndCard(8f);
        }

        private void DrawFooter()
        {
            Theme.HorizontalRule(Theme.Divider, 6f, 4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(_view.Freshness, Styles.FooterLabel);
                GUILayout.FlexibleSpace();

                Rect recheck = GUILayoutUtility.GetRect(
                    RecheckButton, Styles.GhostButtonLabel, GUILayout.Height(22f), GUILayout.MinWidth(90f));
                if (Controls.GhostButton(recheck, RecheckButton))
                {
                    _fixSummary = null;
                    Evaluate(force: true);
                }

                if (_view.FixAllButton == null)
                    return;

                GUILayout.Space(6f);
                Rect fixAll = GUILayoutUtility.GetRect(
                    _view.FixAllButton, Styles.GhostButtonLabel,
                    GUILayout.Height(22f), GUILayout.MinWidth(120f));
                if (Controls.GhostButton(fixAll, _view.FixAllButton))
                    ConfirmAndRunFixAll();
            }
        }


        #endregion

        #region Whole scene

        /// <summary>
        ///     Every Convai Character in the open scene with its worst finding — the view for "I do not
        ///     know which character is broken", and the reason this window is useful when nothing is
        ///     selected at all.
        /// </summary>
        /// <remarks>
        ///     The list is built behind <see cref="ConvaiEditorRefreshTimer" />, not per repaint.
        ///     Finding every character in a scene is a full object sweep, and this method runs on every
        ///     mouse move over the window — a twenty-character scene was paying twenty snapshot lookups
        ///     and a scene sweep for the privilege of the cursor moving one pixel.
        /// </remarks>
        private void DrawSceneList()
        {
            if (_sceneTimer.ShouldRefresh(_sceneRows != null))
                _sceneRows = ConvaiTroubleshooterSceneRow.BuildAll();

            if (_sceneRows == null || _sceneRows.Count == 0)
            {
                Frame.InfoBox(SceneEmptyTitle, SceneEmptyBody);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _sceneRows.Count; i++)
            {
                ConvaiTroubleshooterSceneRow row = _sceneRows[i];
                if (row.Character == null)
                    continue;

                Rect card = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
                bool hover = card.Contains(Event.current.mousePosition);
                Theme.FillRounded(card, hover ? Theme.CardBgHover : Theme.CardBg, Tokens.CardRadius);
                Theme.StrokeRounded(
                    card, hover ? Theme.Fade(Theme.Accent, 0.7f) : Theme.CardBorder, Tokens.CardRadius);

                Theme.StatusDot(new Vector2(card.x + 16f, card.y + (card.height * 0.5f)), row.Tint, hover);

                float pillWidth = Controls.PillWidth(row.Pill);
                Controls.Pill(
                    new Rect(card.xMax - pillWidth - 12f, card.y + 12f, pillWidth, Tokens.PillHeight),
                    row.Pill, row.Tint);

                float textWidth = card.width - pillWidth - 50f;
                GUI.Label(new Rect(card.x + 30f, card.y + 6f, textWidth, 18f), row.Name, Styles.CardName);
                GUI.Label(new Rect(card.x + 30f, card.y + 22f, textWidth, 16f), row.Worst, Styles.MicroLabel);

                EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
                if (GUI.Button(card, GUIContent.none, Styles.InvisibleButton))
                    _pendingCharacter = row.Character;

                GUILayout.Space(Tokens.ListCardSpacing);
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Fixes

        /// <param name="visibleOnly">
        ///     Restricts the batch to findings the section actually shows. A "Fix These (2)" button
        ///     must not quietly apply a third fix for an Info-level suggestion the user never saw.
        /// </param>
        private static List<ConvaiSetupFinding> CollectFixes(ConvaiSetupHealthResult result, bool visibleOnly)
        {
            var fixes = new List<ConvaiSetupFinding>(result.FixableCount);
            IReadOnlyList<ConvaiSetupFinding> findings = result.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                ConvaiSetupFinding finding = findings[i];
                if (!finding.IsFixable || (visibleOnly && !finding.IsIssue))
                    continue;

                fixes.Add(finding);
            }

            return fixes;
        }

        /// <summary>
        ///     Fix All names what it is about to do before it does it. Every fix here is undoable, so
        ///     the dialog is not a safety rail — it is the difference between a user who knows what
        ///     changed on their character and one who has to go looking.
        /// </summary>
        private void ConfirmAndRunFixAll()
        {
            var fixes = new List<ConvaiSetupFinding>(_snapshot.FixableCount);
            for (int i = 0; i < _snapshot.Results.Count; i++)
                fixes.AddRange(CollectFixes(_snapshot.Results[i], visibleOnly: false));

            if (fixes.Count == 0)
                return;

            var message = new System.Text.StringBuilder();
            message.AppendLine($"This will apply {fixes.Count} fixes to '{_character.name}':");
            message.AppendLine();
            for (int i = 0; i < fixes.Count; i++)
                message.AppendLine($"• {fixes[i].FixPreview ?? fixes[i].FixLabel}");
            message.AppendLine();
            message.Append("It is one undo step, so Ctrl+Z puts everything back.");

            if (!EditorUtility.DisplayDialog(FixAllTitle, message.ToString(), "Apply", "Cancel"))
                return;

            RunFixes(fixes, "Everything");
        }

        /// <summary>
        ///     Runs a batch under one named Undo group, then rebuilds. Each fix is re-checked against a
        ///     fresh report first: a window left open while the user edited the character could
        ///     otherwise "fix" something that is already gone.
        /// </summary>
        private void RunFixes(List<ConvaiSetupFinding> fixes, string what)
        {
            if (fixes == null || fixes.Count == 0 || _character == null)
                return;

            ConvaiSetupHealthSnapshot fresh = ConvaiSetupHealthRegistry.Refresh(_character);
            var stale = 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Fix {what}");

            var applied = 0;
            for (int i = 0; i < fixes.Count; i++)
            {
                ConvaiSetupFinding finding = fixes[i];
                if (!StillPresent(fresh, finding.Id))
                {
                    stale++;
                    continue;
                }

                finding.Fix();
                applied++;
            }

            Undo.CollapseUndoOperations(group);
            ConvaiSetupHealthRegistry.Invalidate();
            Evaluate(force: true);

            if (applied > 0)
            {
                string appliedText = applied == 1 ? "1 fix" : $"{applied} fixes";
                string skippedText = stale == 1
                    ? "One finding was already resolved and was skipped."
                    : $"{stale} findings were already resolved and were skipped.";
                _fixSummary = stale > 0
                    ? $"Applied {appliedText} as one undo step. {skippedText}"
                    : $"Applied {appliedText} as one undo step.";
                ConvaiLogger.Info(
                    $"[ConvaiTroubleshooterWindow] Applied {appliedText} on " +
                    $"'{_character.name}' as one Undo step.",
                    LogCategory.Editor);
            }
            else
            {
                _fixSummary = "Nothing was applied — those findings were already resolved.";
            }

            Repaint();
        }

        private static bool StillPresent(ConvaiSetupHealthSnapshot snapshot, string findingId)
        {
            for (int i = 0; i < snapshot.Results.Count; i++)
            {
                IReadOnlyList<ConvaiSetupFinding> findings = snapshot.Results[i].Findings;
                for (int f = 0; f < findings.Count; f++)
                {
                    if (string.Equals(findings[f].Id, findingId, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Selects and pings what a finding is about. "Show Me" has to mean show me: for a beginner
        ///     who does not yet know where a setting lives, being put in front of the right object is
        ///     the whole repair.
        /// </summary>
        private static void ShowMe(ConvaiSetupFinding finding)
        {
            if (finding.Locate == null)
                return;

            Selection.activeObject = finding.Locate;
            EditorGUIUtility.PingObject(finding.Locate);
        }

        #endregion

        private void ClearFocus()
        {
            _focusModuleId = null;
            _focusFindingId = null;
            _flash.Stop();
        }

        /// <summary>
        ///     Re-reads the report and recomposes what is drawn from it. The only place the view model
        ///     is built, so "what is on screen" can never drift from "what was measured".
        /// </summary>
        private void Evaluate(bool force = false)
        {
            _snapshot = _character == null
                ? ConvaiSetupHealthSnapshot.Empty
                : force
                    ? ConvaiSetupHealthRegistry.Refresh(_character)
                    : ConvaiSetupHealthRegistry.Get(_character);

            _view = ConvaiTroubleshooterView.Build(_snapshot);
            _sceneRows = null;
        }

        private static GameObject FindOnlyCharacter()
        {
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            return characters.Length == 1 ? characters[0].gameObject : null;
        }
    }
}
