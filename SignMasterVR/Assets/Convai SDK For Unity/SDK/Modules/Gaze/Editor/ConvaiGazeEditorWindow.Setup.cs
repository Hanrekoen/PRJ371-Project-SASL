using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Core.Solvers;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>One capability row on the Setup tab: the model plus the checkbox's live state.</summary>
    internal struct GazeCapabilityInfoRow
    {
        public GazeCapabilityId Id;
        public string DisplayName;
        public string Description;
        public bool Present;
    }

    internal sealed partial class ConvaiGazeEditorWindow
    {
        private void RefreshCapabilityRows()
        {
            Transform root = GazeSetupService.ResolveRoot(_controller);
            // Reused scratch list: this used to allocate a fresh one inside OnGUI.
            List<GazeCapabilityInfo> infos = _capabilityScratch;
            GazeCapabilities.Evaluate(root, infos);

            for (int i = 0; i < infos.Count; i++)
            {
                _capabilityRows.Add(new GazeCapabilityInfoRow
                {
                    Id = infos[i].Id,
                    DisplayName = infos[i].DisplayName,
                    Description = infos[i].Description,
                    Present = infos[i].IsPresent
                });
            }
        }

        private void DrawSetupMode()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawChecklist();
                    DrawBlockerCallout();
                    DrawCapabilityRows();
                    DrawRigReport();
                    DrawTroubleshooterFindings();
                    GUILayout.Space(16f);
                }
                GUILayout.Space(14f);
            }
        }

        // ------------------------------------------------------------------ checklist

        private void DrawChecklist()
        {
            DrawSectionTitle(GazeEditorStrings.SetupChecklistTitle);

            if (_preflight.Checks == null) return;

            for (int i = 0; i < _preflight.Checks.Count; i++)
            {
                GazeCheck check = _preflight.Checks[i];
                (string glyph, Color color) = GlyphFor(check.State);

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.color;
                    GUI.color = color;
                    GUILayout.Label(glyph, GUILayout.Width(16f));
                    GUI.color = previous;

                    GUILayout.Label(check.Label, ConvaiEditorStyles.RowLabel, GUILayout.Width(130f));
                    GUILayout.Label(check.Detail, ConvaiEditorTheme.CaptionWrapped);

                    string fixLabel = GazeSetupService.DescribeFix(check.Fix);
                    if (fixLabel != null && GUILayout.Button(fixLabel, EditorStyles.miniButton, GUILayout.Width(140f)))
                    {
                        GazeSetupService.ApplyFix(_controller, check.Fix);
                        InvalidateModels();
                    }
                }
            }
        }

        private static (string glyph, Color color) GlyphFor(GazeCheckState state) => state switch
        {
            GazeCheckState.Ok => (ConvaiEditorGlyphs.Status.Ok, ConvaiEditorTheme.AccentBright),
            GazeCheckState.Fixable => ("•", ConvaiEditorTheme.Info),
            GazeCheckState.Blocked => (ConvaiEditorGlyphs.Status.Fail, ConvaiEditorTheme.Error),
            _ => (ConvaiEditorGlyphs.Status.Neutral, ConvaiEditorTheme.TextMuted)
        };

        private void DrawBlockerCallout()
        {
            if (!_preflight.TryGetBlocker(out GazeCheck blocker)) return;

            GUILayout.Space(6f);
            using (ConvaiEditorFrame.Panel())
            {
                ConvaiEditorControls.GroupCaption("One thing needs you first");
                GUILayout.Label(
                    "Gaze rotates the character's head and eye bones, so it cannot run until it can " +
                    "find them. Everything else on this page works once this is resolved.",
                    ConvaiEditorTheme.CaptionWrapped);
                GUILayout.Label(blocker.Detail, ConvaiEditorTheme.CaptionWrapped);
            }
        }

        // ------------------------------------------------------------------ capabilities

        /// <summary>
        ///     The direct answer to "what are the other components, and do I need them?" — every
        ///     optional capability as a checkbox with one sentence, and never a class name.
        /// </summary>
        private void DrawCapabilityRows()
        {
            DrawSectionTitle(GazeEditorStrings.SetupExtrasTitle);
            DrawBody(GazeEditorStrings.SetupExtrasBody);
            GUILayout.Space(4f);

            for (int i = 0; i < _capabilityRows.Count; i++)
            {
                GazeCapabilityInfoRow row = _capabilityRows[i];

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool next = EditorGUILayout.ToggleLeft(
                        new GUIContent(row.DisplayName, row.Description), row.Present);
                    if (EditorGUI.EndChangeCheck()) ToggleCapability(row.Id, next);

                    Rect indented = EditorGUILayout.GetControlRect(false, 14f);
                    indented.xMin += 18f;
                    GUI.Label(indented, row.Description, ConvaiEditorTheme.CaptionWrapped);
                }
            }
        }

        private void ToggleCapability(GazeCapabilityId id, bool wanted)
        {
            var selection = new List<GazeCapabilityId>(_capabilityRows.Count);
            for (int i = 0; i < _capabilityRows.Count; i++)
            {
                GazeCapabilityInfoRow row = _capabilityRows[i];
                bool keep = row.Id == id ? wanted : row.Present;
                if (keep) selection.Add(row.Id);
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(wanted ? "Add Gaze Capability" : "Remove Gaze Capability");
            GazeSetupService.ApplyCapabilities(_controller, selection, null);
            Undo.CollapseUndoOperations(undoGroup);

            InvalidateModels();
            Repaint();
        }

        // ------------------------------------------------------------------ rig report

        /// <summary>
        ///     What the gaze stack actually resolved on this character, and the facing-direction
        ///     check — the module's hardest-to-discover requirement, shown as a measured angle
        ///     rather than a paragraph of prose.
        /// </summary>
        /// <remarks>
        ///     Both answers come from <see cref="GazeSetupService" /> rather than being measured
        ///     here, so this report, the component inspector and an AI assistant cannot describe the
        ///     same rig differently. It is also why the report now works before Play Mode: the
        ///     service resolves the bones the way the runtime will, instead of waiting for the gaze
        ///     chain to bind.
        /// </remarks>
        private void DrawRigReport()
        {
            DrawSectionTitle(GazeEditorStrings.SetupRigReportTitle);

            // Once bound, the chain is authoritative — it is the rig gaze is actually driving.
            GazeChainCalibration chain = _controller.Chain;
            if (chain != null && chain.IsBound)
            {
                DrawRigRow("Head", chain.Head);
                DrawRigRow("Neck", chain.Neck);
                DrawRigRow("Left eye", chain.LeftEye);
                DrawRigRow("Right eye", chain.RightEye);
            }
            else
            {
                GazeBoneReport bones = GazeSetupService.ResolveBones(_controller);
                DrawRigRow("Head", bones.Head);
                DrawRigRow("Neck", bones.Neck);
                DrawRigRow("Left eye", bones.LeftEye);
                DrawRigRow("Right eye", bones.RightEye);
            }

            ConvaiEditorControls.GroupCaption(GazeEditorStrings.ForwardAxisTitle);

            GazeFacingReport facing = GazeSetupService.InspectFacing(_controller);
            if (facing.Measured)
            {
                bool pass = facing.State == GazeFacingState.Pass;
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.color;
                    GUI.color = pass ? ConvaiEditorTheme.AccentBright : ConvaiEditorTheme.Error;
                    GUILayout.Label(
                        pass ? ConvaiEditorGlyphs.Status.Ok : ConvaiEditorGlyphs.Status.Fail,
                        GUILayout.Width(16f));
                    GUI.color = previous;
                    GUILayout.Label(
                        $"{facing.AngleDegrees:0.0}° from the character's forward",
                        ConvaiEditorStyles.MicroLabel);
                }
            }

            DrawBody(facing.Detail);
        }

        private static void DrawRigRow(string label, Transform bone)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, ConvaiEditorStyles.RowLabel, GUILayout.Width(90f));
                if (bone == null)
                {
                    GUILayout.Label("not found", ConvaiEditorTheme.CaptionWrapped);
                    return;
                }

                if (GUILayout.Button(bone.name, EditorStyles.miniButton, GUILayout.Width(180f)))
                {
                    EditorGUIUtility.PingObject(bone.gameObject);
                    Selection.activeGameObject = bone.gameObject;
                }

                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------------ findings

        private void DrawTroubleshooterFindings()
        {
            bool any = false;
            for (int i = 0; i < _findings.Count; i++)
                if (_findings[i].Severity >= GazeSetupSeverity.Warning) { any = true; break; }

            if (!any) return;

            DrawSectionTitle("Worth knowing");
            for (int i = 0; i < _findings.Count; i++)
            {
                GazeSetupFinding finding = _findings[i];
                if (finding.Severity < GazeSetupSeverity.Warning) continue;

                using (ConvaiEditorFrame.Panel())
                {
                    ConvaiEditorControls.GroupCaption(finding.Title);
                    GUILayout.Label(finding.Message, ConvaiEditorTheme.CaptionWrapped);
                }
            }
        }
    }
}
