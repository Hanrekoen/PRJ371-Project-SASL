using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Inspector for <see cref="ConvaiBodyAnimationSet" />: a content summary as stat tiles and a
    ///     validation report above the property drawers, so authoring problems (missing clips, loop
    ///     flags, alias collisions) surface without entering play mode.
    /// </summary>
    /// <remarks>
    ///     Shares one finding model with the Troubleshooter window and the controller inspector, so an
    ///     authoring problem reads identically wherever the user happens to find it — and now shares
    ///     one design system with them too.
    /// </remarks>
    [CustomEditor(typeof(ConvaiBodyAnimationSet))]
    internal sealed class ConvaiBodyAnimationSetEditor : ConvaiInspectorEditor
    {
        private static readonly GUIContent TileIdles = new("IDLE", "Valid idle entries in this set.");
        private static readonly GUIContent TileTalks = new("TALK", "Valid talk entries in this set.");
        private static readonly GUIContent TileLocomotion = new("MOVE", "Assigned locomotion clips.");
        private static readonly GUIContent TileActions = new("ACTIONS", "Valid action entries in this set.");

        private static readonly GUIContent ChipReady = new("Ready", "No authoring problems were found.");
        private static readonly GUIContent ChipCheckContent = new("Check Content", "Something in this set needs a look.");
        private static readonly GUIContent ChipNeedsAttention = new("Needs Attention", "This set has a blocking problem.");

        // "Measure Clips", the same words the editor window's Content mode uses for the same run.
        // With the menu entry retired, these two buttons are the only ways in, so calling one of
        // them something else would read as a second, different tool.
        private static readonly GUIContent AnalyzeButton = new(
            "Measure Clips",
            "Samples every assigned locomotion clip and fills authored speed/distance/yaw curves and " +
            "foot plants (required for zero-slide NavMesh sync).");

        private static readonly GUIContent BootstrapButton = new(
            "Bootstrap Talk Motion Phrases",
            "For talk entries with no fragments, creates three editable sub-ranges. Preview and refine " +
            "the boundaries for the source performance before shipping.");

        private static readonly GUIContent ToolsTitle = new("Content Tools");

        private readonly List<string> _issues = new();
        private readonly List<BodyAnimationTroubleshooterFinding> _findings = new();
        private readonly List<(string slot, LocomotionClip clip)> _assignedLocomotion = new();

        private BodyAnimationTroubleshooterSeverity _worst;

        protected override string Title => "Body Animation Set";

        protected override string Subtitle => "Animation content";

        protected override GUIContent StatusChip => _worst switch
        {
            BodyAnimationTroubleshooterSeverity.Error => ChipNeedsAttention,
            BodyAnimationTroubleshooterSeverity.Warning => ChipCheckContent,
            _ => ChipReady
        };

        protected override Color StatusChipTint => _worst switch
        {
            BodyAnimationTroubleshooterSeverity.Error => Theme.StatusError,
            BodyAnimationTroubleshooterSeverity.Warning => Theme.StatusWarn,
            _ => Theme.StatusReady
        };

        protected override void OnBeforeInspectorGUI()
        {
            var set = (ConvaiBodyAnimationSet)target;
            BodyAnimationTroubleshooter.EvaluateSetAsset(set, _issues, _findings);
            _worst = BodyAnimationTroubleshooter.WorstSeverity(_findings);
        }

        protected override void DrawHeaderExtras()
        {
            var set = (ConvaiBodyAnimationSet)target;

            DrawSummaryTiles(set);
            DrawFindings(set);
            DrawTools(set);
        }

        private void DrawSummaryTiles(ConvaiBodyAnimationSet set)
        {
            set.Locomotion.CollectAssigned(_assignedLocomotion);

            using (new EditorGUILayout.HorizontalScope())
            {
                StatTile(TileIdles, Count(set.Idles, e => e != null && e.IsValid).ToString());
                GUILayout.Space(6f);
                StatTile(TileTalks, Count(set.Talks, e => e != null && e.IsValid).ToString());
                GUILayout.Space(6f);
                StatTile(TileLocomotion, _assignedLocomotion.Count.ToString());
                GUILayout.Space(6f);
                StatTile(TileActions, Count(set.Actions, e => e != null && e.IsValid).ToString());
            }

            EditorGUILayout.Space(8f);
        }

        /// <summary>
        ///     Renders the shared finding model. Findings that carry a mechanical repair get their
        ///     Fix button here too, so the user never has to leave the asset to resolve them.
        /// </summary>
        private void DrawFindings(ConvaiBodyAnimationSet set)
        {
            int actionable = 0;
            for (int i = 0; i < _findings.Count; i++)
            {
                if (_findings[i].Severity > BodyAnimationTroubleshooterSeverity.Ok) actionable++;
            }

            if (actionable == 0)
            {
                InfoBox("Content Validated", "No authoring problems were found in this set.");
                return;
            }

            for (int i = 0; i < _findings.Count; i++)
            {
                BodyAnimationTroubleshooterFinding finding = _findings[i];
                if (finding.Severity == BodyAnimationTroubleshooterSeverity.Ok) continue;

                string fixLabel = BodyAnimationFixes.DescribeSetFix(finding.Fix);
                if (fixLabel != null)
                {
                    ConvaiBodyAnimationSet fixTarget = set;
                    BodyAnimationFixId fixId = finding.Fix;
                    WarningBox(finding.Title, finding.Message, fixLabel,
                        () => BodyAnimationFixes.ApplyToSet(fixTarget, fixId));
                    continue;
                }

                if (finding.Severity == BodyAnimationTroubleshooterSeverity.Error)
                    ErrorBox(finding.Title, finding.Message);
                else if (finding.Severity == BodyAnimationTroubleshooterSeverity.Warning)
                    WarningBox(finding.Title, finding.Message);
                else
                    InfoBox(finding.Title, finding.Message);
            }
        }

        private static void DrawTools(ConvaiBodyAnimationSet set)
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Content, ToolsTitle);

                if (GhostButton(AnalyzeButton))
                    ClipMotionAnalyzer.AnalyzeSet(set);

                EditorGUILayout.Space(4f);

                if (GhostButton(BootstrapButton))
                {
                    int changed = TalkFragmentSuggestionUtility.Generate(set);
                    EditorUtility.DisplayDialog(
                        "Talk Motion Phrases",
                        changed > 0
                            ? $"Created editable phrase suggestions for {changed} talk clip(s). Preview and refine their safe boundaries."
                            : "No eligible talk entry was found. Entries may already have fragments.",
                        "OK");
                }
            }
        }

        private static int Count<T>(IReadOnlyList<T> list, System.Predicate<T> predicate)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (predicate(list[i])) count++;
            }

            return count;
        }
    }
}
