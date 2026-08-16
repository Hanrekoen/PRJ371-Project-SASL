using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Setup mode: the same preflight checklist and finding model the component inspector
    ///     renders, in full width. This is a mirror, not the primary surface — a user who never
    ///     opens this window must still be able to complete setup entirely from the inspector
    ///    . Fixes route through the same two services every other surface
    ///     uses (<see cref="BodyAnimationSetupService" /> for character-scoped repairs,
    ///     <see cref="BodyAnimationFixes" /> for set-scoped ones), so a fix behaves identically
    ///     wherever it is pressed.
    /// </summary>
    internal sealed partial class ConvaiBodyAnimationEditorWindow
    {
        private static readonly GUIContent PreflightHeaderContent =
            new(BodyAnimationEditorStrings.SetupPreflightHeader);

        private static readonly GUIContent FindingsHeaderContent =
            new(BodyAnimationEditorStrings.SetupFindingsHeader);

        private static readonly GUIContent IncludeMovementContent = new(
            BodyAnimationEditorStrings.SetupIncludeMovementLabel,
            BodyAnimationEditorStrings.SetupIncludeMovementTooltip);

        private static readonly GUIContent SetupRunContent = new(BodyAnimationEditorStrings.SetupRunButton);

        private static readonly GUIContent SetupBlockedContent = new(BodyAnimationEditorStrings.SetupBlockedButton);

        /// <summary>Reused for per-finding fix buttons, whose labels are computed per draw.</summary>
        private static readonly GUIContent ScratchFixButton = new();

        private bool _setupIncludeMovement = true;

        private void DrawSetupMode()
        {
            if (_controller == null)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.SetupNoControllerMessage);
                return;
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Min(720f, position.width - LeftPaneWidth - 40f))))
            {
                GUILayout.Space(6f);
                GUILayout.Label(BodyAnimationEditorStrings.SetupModeTitle, ConvaiEditorStyles.SelectedTitle);
                GUILayout.Label(BodyAnimationEditorStrings.SetupModeIntro, ConvaiEditorStyles.MutedWrapped);
                GUILayout.Space(10f);

                DrawPreflightSection();
                DrawFindingsSection();
            }
        }

        private void DrawPreflightSection()
        {
            BodyAnimationPreflight preflight = BodyAnimationSetupService.Inspect(_controller);

            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(
                    ConvaiEditorGlyphs.Validation, PreflightHeaderContent);

                for (int i = 0; i < preflight.Checks.Count; i++)
                    DrawPreflightRow(preflight.Checks[i]);

                GUILayout.Space(6f);
                _setupIncludeMovement = EditorGUILayout.ToggleLeft(IncludeMovementContent, _setupIncludeMovement);
                GUILayout.Space(6f);

                bool blocked = preflight.HasBlocker;
                using (new EditorGUI.DisabledScope(blocked || preflight.IsConfigured))
                {
                    if (ConvaiEditorControls.PrimaryButtonLayout(
                            blocked ? SetupBlockedContent : SetupRunContent, 26f))
                        RunSetup();
                }
            }
        }

        /// <summary>
        ///     Runs setup after the current IMGUI pass, never inside it — a modal raised from within a
        ///     layout scope discards the layout state the enclosing scope is about to close, which
        ///     leaves the surface throwing on every later repaint with no way to recover.
        /// </summary>
        private void RunSetup()
        {
            ConvaiBodyAnimationController controller = _controller;
            bool includeMovement = _setupIncludeMovement;

            EditorApplication.delayCall += () =>
            {
                if (controller == null) return;

                BodyAnimationSetupResult result = BodyAnimationSetupService.Apply(
                    controller, new BodyAnimationSetupOptions { IncludeMovement = includeMovement });

                var message = new System.Text.StringBuilder(result.Summary);
                for (int i = 0; i < result.Notes.Count; i++)
                    message.Append("\n\n• ").Append(result.Notes[i]);

                EditorUtility.DisplayDialog(BodyAnimationEditorStrings.WindowTitle, message.ToString(), "OK");
                RefreshFindings();
            };
        }

        private static void DrawPreflightRow(BodyAnimationCheck check)
        {
            Color color = check.State switch
            {
                BodyAnimationCheckState.Ok => ConvaiEditorTheme.StatusReady,
                BodyAnimationCheckState.Fixable => ConvaiEditorTheme.StatusInfo,
                BodyAnimationCheckState.Blocked => ConvaiEditorTheme.StatusError,
                BodyAnimationCheckState.NeedsContent => ConvaiEditorTheme.StatusWarn,
                _ => ConvaiEditorTheme.StatusIdle
            };

            // Same row geometry as the component inspector's checklist, so the two surfaces read as
            // one checklist rendered at two widths.
            Rect slot = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            ConvaiEditorTheme.StatusDot(new Vector2(slot.x + 9f, slot.y + (slot.height * 0.5f)), color);

            var labelRect = new Rect(slot.x + 20f, slot.y, 130f, slot.height);
            var detailRect = new Rect(labelRect.xMax + 4f, slot.y, Mathf.Max(40f, slot.width - 158f), slot.height);
            GUI.Label(labelRect, check.Label, ConvaiEditorStyles.MicroLabel);
            GUI.Label(detailRect, check.Detail, ConvaiEditorStyles.CaptionWrapped);
        }

        private void DrawFindingsSection()
        {
            bool anyActionable = false;
            for (int i = 0; i < _findings.Count; i++)
            {
                if (_findings[i].Severity > BodyAnimationTroubleshooterSeverity.Ok) anyActionable = true;
            }

            if (!anyActionable)
            {
                ConvaiEditorFrame.InfoBox(
                    BodyAnimationEditorStrings.SetupFindingsHeader,
                    BodyAnimationEditorStrings.SetupAllGoodMessage);
                return;
            }

            ConvaiBodyAnimationSet set = BodyAnimationSetupService.ResolveAssignedSet(_controller);

            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Discovery, FindingsHeaderContent);

                for (int i = 0; i < _findings.Count; i++)
                {
                    BodyAnimationTroubleshooterFinding finding = _findings[i];
                    if (finding.Severity == BodyAnimationTroubleshooterSeverity.Ok)
                    {
                        GUILayout.Label($"{finding.Title}: {finding.Message}", ConvaiEditorStyles.CaptionWrapped);
                        continue;
                    }

                    DrawFindingPanel(finding, set);
                }
            }
        }

        /// <summary>One finding as a severity-tinted panel with its own one-click fix, when it has one.</summary>
        private void DrawFindingPanel(BodyAnimationTroubleshooterFinding finding, ConvaiBodyAnimationSet set)
        {
            Color severity = finding.Severity switch
            {
                BodyAnimationTroubleshooterSeverity.Error => ConvaiEditorTheme.StatusError,
                BodyAnimationTroubleshooterSeverity.Warning => ConvaiEditorTheme.StatusWarn,
                _ => ConvaiEditorTheme.StatusInfo
            };

            using (ConvaiEditorFrame.Panel(severity, 6f, 2f))
            {
                GUILayout.Label(finding.Title, ConvaiEditorStyles.CardTitle);
                GUILayout.Label(finding.Message, ConvaiEditorStyles.MutedWrapped);

                string characterFix = BodyAnimationSetupService.DescribeFix(finding.Fix);
                if (characterFix != null)
                {
                    DrawFixButton(characterFix, () =>
                    {
                        BodyAnimationSetupService.ApplyFix(_controller, finding.Fix);
                        RefreshFindings();
                    });
                    return;
                }

                string setFix = BodyAnimationFixes.DescribeSetFix(finding.Fix);
                if (setFix != null && set != null)
                {
                    DrawFixButton(setFix, () =>
                    {
                        BodyAnimationFixes.ApplyToSet(set, finding.Fix);
                        RefreshFindings();
                    });
                    return;
                }

                string configFix = BodyAnimationFixes.DescribeConfigFix(finding.Fix);
                if (configFix == null) return;

                ConvaiBodyAnimationConfig configAsset =
                    BodyAnimationSetupService.ResolveAssignedConfig(_controller);
                if (configAsset == null) return;

                DrawFixButton(configFix, () =>
                {
                    BodyAnimationFixes.ApplyToConfig(configAsset, finding.Fix, _controller);
                    RefreshFindings();
                });
            }
        }

        private static void DrawFixButton(string label, System.Action apply)
        {
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect rect = GUILayoutUtility.GetRect(140f, 20f, GUILayout.Width(140f));
                ScratchFixButton.text = label;
                if (ConvaiEditorControls.GhostButton(rect, ScratchFixButton)) apply();
            }
        }

        private static void DrawCenteredMessage(string message)
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(message, ConvaiEditorStyles.CenteredBody, GUILayout.MaxWidth(360f));
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }
    }
}
