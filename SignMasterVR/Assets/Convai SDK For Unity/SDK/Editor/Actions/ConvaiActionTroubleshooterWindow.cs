using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Editor window that diagnoses a character's action authoring setup and offers a one-click,
    ///     Undo-recorded fix per mechanical finding, plus a Fix All button that runs every safe fix
    ///     in order under a single Undo group.
    /// </summary>
    /// <remarks>
    ///     The checks themselves live in <see cref="ConvaiActionSetupReport" />, which every surface
    ///     that shows an action-setup count reads from — this window, the Convai Action Config
    ///     Source inspector, and the Actions Editor hero. This class is the view: pick a character,
    ///     draw the findings, run the fixes, re-run the checks.
    /// </remarks>
    internal sealed class ConvaiActionTroubleshooterWindow : EditorWindow
    {
        #region Cached content

        private static readonly GUIContent HeroTitleContent = new("Action Troubleshooter");

        private static readonly GUIContent HeroSubtitleContent = new(
            "Checks this Convai Character's action setup and offers a one-click fix for anything mechanical.");

        private static readonly GUIContent CharacterLabel = new(
            "Character", "The Convai Character whose action setup is being checked.");

        private static readonly GUIContent RerunContent = new(
            ConvaiActionsEditorStrings.TroubleshooterRerunButton);

        private static readonly GUIContent FixAllContent = new(
            ConvaiActionsEditorStrings.TroubleshooterFixAllButton);

        private const string SelectCharacterTitle = "Pick A Convai Character";
        private const string FixAllSummaryTitle = "Fixes Applied";

        // Reused per draw: their text is computed from live counts and per-finding labels.
        private static readonly GUIContent ScratchHealthPill = new();
        private static readonly GUIContent ScratchFixButton = new();

        #endregion

        private ConvaiCharacter _character;
        private ConvaiActionSetupReport _report = ConvaiActionSetupReport.Empty;
        private Vector2 _scroll;
        private GUIContent _fixAllSummary;

        /// <summary>
        ///     Opens the Convai Troubleshooter focused on Actions.
        /// </summary>
        /// <remarks>
        ///     There used to be an Actions-only window here, and then a menu entry that kept the old
        ///     path alive as an alias. Both are gone. Two troubleshooters was the original defect —
        ///     a user whose character did nothing had to already know which of them answered their
        ///     question — and a second menu row onto the one surviving window rebuilt exactly that
        ///     confusion in the menu bar. The Actions Editor and the component inspectors call this
        ///     method directly, which is how a user reaches Actions findings; the shared
        ///     <c>Convai &gt; Troubleshooter</c> entry is the only menu path to the window itself.
        /// </remarks>
        public static void ShowWindow() => ShowWindow(null);

        /// <summary>
        ///     Opens on the character explicitly selected by the calling authoring surface. The
        ///     Hierarchy selection is only a fallback for Inspector and menu callers that do not
        ///     own a character picker of their own.
        /// </summary>
        internal static void ShowWindow(ConvaiCharacter character) =>
            Convai.Editor.Diagnostics.ConvaiTroubleshooterWindow.ShowFor(
                ResolveOpeningSubject(character, Selection.activeGameObject), "convai.actions");

        /// <summary>Pure selection-precedence seam used by the Editor regression test.</summary>
        internal static GameObject ResolveOpeningSubject(
            ConvaiCharacter explicitCharacter, GameObject hierarchySelection) =>
            explicitCharacter != null ? explicitCharacter.gameObject : hierarchySelection;

        /// <summary>Findings from the most recent evaluation (test seam; the UI reads the same list).</summary>
        internal IReadOnlyList<ConvaiActionTroubleshooterFinding> Findings => _report.Findings;

        /// <summary>Evaluates a specific character (test seam; the picker uses the same path).</summary>
        internal void EvaluateFor(ConvaiCharacter character)
        {
            _character = character;
            Evaluate();
        }

        /// <summary>Runs every available fix in order under one Undo group (test seam for Fix All).</summary>
        internal int RunAllFixes() => RunAllFixesCore();

        private void OnEnable()
        {
            if (_character == null)
                AutoSelectCharacter();

            Evaluate();
        }

        private void OnGUI()
        {
            Theme.EnsureStyles();
            Theme.Fill(new Rect(0f, 0f, position.width, position.height), Theme.WindowBg);

            DrawHeroBand();

            using (new EditorGUILayout.VerticalScope(ConvaiEditorStyles.PaneContent))
            {
                DrawCharacterPicker();

                if (_character == null)
                {
                    Frame.InfoBox(
                        SelectCharacterTitle, ConvaiActionsEditorStrings.TroubleshooterSelectCharacterPrompt);
                    return;
                }

                if (_fixAllSummary != null)
                    Frame.InfoBox(FixAllSummaryTitle, _fixAllSummary.text);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                IReadOnlyList<ConvaiActionTroubleshooterFinding> findings = _report.Findings;
                for (int i = 0; i < findings.Count; i++)
                {
                    if (DrawFinding(findings[i]))
                        break; // A fix ran and Evaluate() rebuilt the list — redraw next frame.
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        ///     Convai hero band with a live health pill and the window's two actions — the same
        ///     opening language the Actions Editor window uses, so the two read as one product.
        /// </summary>
        private void DrawHeroBand()
        {
            // Its two actions are buttons rather than a status pill, so the shared hero is asked for
            // no chip and this window fills the right-hand side itself.
            Rect band = Theme.WindowHero(
                position.width, HeroTitleContent, HeroSubtitleContent);

            float rightEdge = band.xMax - 18f;
            float buttonY = band.y + ((band.height - 22f) * 0.5f);

            if (_report.FixableCount > 0)
            {
                var fixAll = new Rect(rightEdge - 110f, buttonY, 110f, 22f);
                if (Controls.GhostButton(fixAll, FixAllContent))
                    RunAllFixesCore();
                rightEdge = fixAll.x - 6f;
            }

            var rerun = new Rect(rightEdge - 100f, buttonY, 100f, 22f);
            if (Controls.GhostButton(rerun, RerunContent))
            {
                _fixAllSummary = null;
                Evaluate();
            }

            DrawHealthPill(new Rect(band.x + 18f, band.y, rerun.x - band.x - 30f, band.height));
        }

        /// <summary>
        ///     Right-aligned health pill: how many findings still need attention, at a glance. The
        ///     count is <see cref="ConvaiActionSetupReport.IssueCount" /> — the same number the
        ///     Config Source inspector and the Actions Editor hero show for this character.
        /// </summary>
        private void DrawHealthPill(Rect row)
        {
            if (_character == null) return;

            Color tint = _report.ErrorCount > 0
                ? Theme.StatusError
                : _report.WarningCount > 0
                    ? Theme.StatusWarn
                    : Theme.StatusReady;

            ScratchHealthPill.text = _report.IsHealthy
                ? "No Issues Found"
                : ConvaiActionsEditorStrings.BuildTroubleshooterHealthPill(_report.IssueCount);
            float width = Controls.PillWidth(ScratchHealthPill, true);
            Controls.Pill(
                new Rect(row.xMax - width, row.y + ((row.height - 20f) * 0.5f), width, 20f),
                ScratchHealthPill, tint, true);
        }

        private void DrawCharacterPicker()
        {
            var picked = (ConvaiCharacter)EditorGUILayout.ObjectField(
                CharacterLabel, _character, typeof(ConvaiCharacter), true);
            if (picked != _character)
            {
                _character = picked;
                _fixAllSummary = null;
                Evaluate();
            }

            GUILayout.Space(6f);
        }

        private void AutoSelectCharacter()
        {
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            if (characters.Length == 1)
                _character = characters[0];
        }

        /// <summary>Draws one finding row. Returns true when its fix ran (the findings list is stale).</summary>
        private bool DrawFinding(ConvaiActionTroubleshooterFinding finding)
        {
            Color severity = finding.Severity switch
            {
                ConvaiActionTroubleshooterSeverity.Error => Theme.StatusError,
                ConvaiActionTroubleshooterSeverity.Warning => Theme.StatusWarn,
                ConvaiActionTroubleshooterSeverity.Info => Theme.StatusInfo,
                _ => Theme.StatusReady
            };

            bool fixRan = false;
            using (Frame.Panel(severity, 6f, 2f))
            using (new EditorGUILayout.HorizontalScope())
            {
                // The panel's left accent bar already carries the severity colour, so the row needs
                // no second status mark — just the sentence and its fix.
                GUILayout.Label(finding.DisplayText, ConvaiEditorStyles.BodyWrapped);

                if (finding.Fix != null)
                {
                    GUILayout.Space(8f);
                    ScratchFixButton.text = finding.FixLabel;
                    Rect fix = GUILayoutUtility.GetRect(160f, 22f, GUILayout.Width(160f));
                    if (Controls.GhostButton(fix, ScratchFixButton))
                    {
                        RunSingleFix(finding);
                        fixRan = true;
                    }
                }
            }

            return fixRan;
        }

        #region Fix execution (Undo grouping)

        private void RunSingleFix(ConvaiActionTroubleshooterFinding finding)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(finding.FixLabel ?? "Fix Action Setup Issue");
            finding.Fix();
            Undo.CollapseUndoOperations(group);
            Evaluate();
        }

        private int RunAllFixesCore()
        {
            // Snapshot first: fixes mutate the very state the findings were computed from, and the
            // list is rebuilt once at the end instead of after every fix.
            IReadOnlyList<ConvaiActionTroubleshooterFinding> findings = _report.Findings;
            var fixes = new List<ConvaiActionTroubleshooterFinding>(_report.FixableCount);
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Fix != null)
                    fixes.Add(findings[i]);
            }

            if (fixes.Count == 0)
                return 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix All Action Setup Issues");
            for (int i = 0; i < fixes.Count; i++)
                fixes[i].Fix();
            Undo.CollapseUndoOperations(group);

            Evaluate();
            _fixAllSummary = ConvaiActionsEditorStrings.BuildTroubleshooterFixAllSummary(fixes.Count);
            ConvaiLogger.Info(
                $"[ConvaiActionTroubleshooterWindow] Fix All applied {fixes.Count} fix(es) on " +
                $"'{(_character != null ? _character.name : "<none>")}' as one Undo step.",
                LogCategory.Editor);
            Repaint();
            return fixes.Count;
        }

        #endregion

        private void Evaluate() => _report = ConvaiActionSetupReport.Run(_character);
    }
}
