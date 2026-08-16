using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Providers;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    internal sealed partial class ConvaiGazeEditorWindow
    {
        private readonly GazeSnapshot _snapshot = new();
        private UnityEditor.Editor _profileEditor;
        private ConvaiGazeProfile _profileEditorTarget;

        // ------------------------------------------------------------------ feel

        /// <summary>
        ///     The complete personality surface. The inspector shows three dials; this shows the
        ///     whole profile behind them, by hosting the profile's own inspector rather than
        ///     re-implementing it — one authored surface, no chance of the two drifting.
        /// </summary>
        private void DrawFeelMode()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(10f);

                    SerializedObject serialized = ControllerSerialized;
                    SerializedProperty profileProperty = serialized.FindProperty("profile");

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(profileProperty, new GUIContent(
                        "Personality",
                        "The Gaze Profile this character uses. Leave empty to use the SDK defaults."));
                    if (EditorGUI.EndChangeCheck())
                    {
                        serialized.ApplyModifiedProperties();
                        InvalidateModels();
                    }

                    var profile = profileProperty?.objectReferenceValue as ConvaiGazeProfile;
                    if (profile == null)
                    {
                        GUILayout.Space(8f);
                        using (ConvaiEditorFrame.Panel())
                        {
                            ConvaiEditorControls.GroupCaption(GazeEditorStrings.FeelNoProfileTitle);
                            GUILayout.Label(GazeEditorStrings.FeelNoProfileBody, ConvaiEditorTheme.CaptionWrapped);
                            if (GUILayout.Button("Add a Personality", GUILayout.Height(22f)))
                            {
                                GazeSetupService.ApplyFix(_controller, GazeFixId.AssignDefaultProfile);
                                InvalidateModels();
                            }
                        }
                        GUILayout.Space(14f);
                        return;
                    }

                    DrawSharedProfileNotice(profile);

                    GUILayout.Space(8f);
                    EnsureProfileEditor(profile);
                    _profileEditor?.OnInspectorGUI();

                    GUILayout.Space(16f);
                }
                GUILayout.Space(14f);
            }
        }

        private void EnsureProfileEditor(ConvaiGazeProfile profile)
        {
            if (_profileEditor != null && _profileEditorTarget == profile) return;

            ReleaseProfileEditor();
            _profileEditorTarget = profile;
            _profileEditor = UnityEditor.Editor.CreateEditor(profile);
        }

        /// <summary>
        ///     Destroys the hosted profile editor. A <see cref="UnityEditor.Editor" /> is a
        ///     <see cref="Object" />, so leaving one behind when the window closes or the selection
        ///     changes leaks it for the rest of the session.
        /// </summary>
        private void ReleaseProfileEditor()
        {
            if (_profileEditor != null) DestroyImmediate(_profileEditor);
            _profileEditor = null;
            _profileEditorTarget = null;
        }

        /// <summary>
        ///     Profiles are assets, so two characters can share one. Editing a shared profile
        ///     changes both, and finding that out by surprise is the trap this notice exists to
        ///     close.
        /// </summary>
        private void DrawSharedProfileNotice(ConvaiGazeProfile profile)
        {
            // Counted during the model refresh, not per repaint — it used to be a full scene scan
            // inside the draw call.
            int users = _sharedProfileUsers;
            if (users <= 1) return;

            using (ConvaiEditorFrame.Panel())
            {
                GUILayout.Label($"{GazeEditorStrings.FeelSharedNotice} ({users} characters)",
                    ConvaiEditorTheme.CaptionWrapped);
                if (GUILayout.Button("Give this character its own copy", GUILayout.Height(20f)))
                    DuplicateProfileForController(profile);
            }
        }

        private void DuplicateProfileForController(ConvaiGazeProfile profile)
        {
            string sourcePath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(sourcePath))
            {
                EditorUtility.DisplayDialog("Convai Gaze",
                    "This personality is not a saved asset, so it cannot be copied.", "OK");
                return;
            }

            string destination = EditorUtility.SaveFilePanelInProject(
                "Save a copy of this personality",
                $"{_controller.name}_GazeProfile", "asset",
                "Choose where the copy lives. The character will use the copy from now on.");
            if (string.IsNullOrEmpty(destination)) return;

            if (!AssetDatabase.CopyAsset(sourcePath, destination))
            {
                EditorUtility.DisplayDialog("Convai Gaze", "The copy could not be created.", "OK");
                return;
            }

            AssetDatabase.Refresh();
            var copy = AssetDatabase.LoadAssetAtPath<ConvaiGazeProfile>(destination);
            if (copy == null) return;

            SerializedObject serialized = ControllerSerialized;
            serialized.FindProperty("profile").objectReferenceValue = copy;
            serialized.ApplyModifiedProperties();
            InvalidateModels();
        }

        // ------------------------------------------------------------------ targets

        /// <summary>
        ///     Scene-wide: everything a character can decide to look at, plus the advanced
        ///     targeting fields evicted from the component inspector so its common path stays two
        ///     controls.
        /// </summary>
        private void DrawTargetsMode()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawSectionTitle(GazeEditorStrings.TargetsTitle);
                    DrawBody(GazeEditorStrings.TargetsBody);
                    GUILayout.Space(6f);

                    if (_targetRows.Count == 0) DrawBody(GazeEditorStrings.TargetsEmpty);
                    else
                        for (int i = 0; i < _targetRows.Count; i++)
                            DrawTargetRow(_targetRows[i]);

                    GUILayout.Space(8f);
                    DrawAddTargetButton();

                    DrawAdvancedTargeting();
                    GUILayout.Space(16f);
                }
                GUILayout.Space(14f);
            }
        }

        /// <summary>One row of the Targets list: anything in the scene a character can decide to look at.</summary>
        private readonly struct GazeTargetRow
        {
            public GazeTargetRow(GameObject owner, string state)
            {
                Owner = owner;
                State = state;
            }

            public readonly GameObject Owner;

            /// <summary>Plain-English status, including which of the two target components it is.</summary>
            public readonly string State;
        }

        private readonly List<GazeTargetRow> _targetRows = new(8);

        /// <summary>
        ///     Rebuilds the scene's target list during the model refresh rather than inside the draw
        ///     call.
        /// </summary>
        /// <remarks>
        ///     Two components can make an object a gaze candidate, and this list used to show only
        ///     one of them. A scene whose objects were marked with World Object Target — the
        ///     Actions-metadata variant — read as "nothing here yet", which is the worst possible
        ///     answer for a page whose whole job is to say what the character can look at.
        /// </remarks>
        private void RefreshTargetRows()
        {
            _targetRows.Clear();

            ConvaiGazeTarget[] targets = ConvaiObjectFind.All<ConvaiGazeTarget>(FindObjectsInactive.Include);
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                _targetRows.Add(new GazeTargetRow(
                    targets[i].gameObject,
                    targets[i].isActiveAndEnabled ? "noticed while idle" : "disabled"));
            }

            WorldObjectGazeTargetProvider[] worldObjects = ConvaiObjectFind.All<WorldObjectGazeTargetProvider>(FindObjectsInactive.Include);
            for (int i = 0; i < worldObjects.Length; i++)
            {
                if (worldObjects[i] == null) continue;
                _targetRows.Add(new GazeTargetRow(
                    worldObjects[i].gameObject,
                    worldObjects[i].isActiveAndEnabled
                        ? "noticed while idle (world object)"
                        : "disabled (world object)"));
            }
        }

        private static void DrawTargetRow(GazeTargetRow row)
        {
            if (row.Owner == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(row.Owner.name, EditorStyles.miniButton, GUILayout.Width(220f)))
                {
                    EditorGUIUtility.PingObject(row.Owner);
                    Selection.activeGameObject = row.Owner;
                }

                GUILayout.Label(row.State, ConvaiEditorTheme.CaptionWrapped);
            }
        }

        private void DrawAddTargetButton()
        {
            GameObject selected = Selection.activeGameObject;
            bool canAdd = selected != null && selected.GetComponent<ConvaiGazeTarget>() == null;

            using (new EditorGUI.DisabledScope(!canAdd))
            {
                string label = selected == null
                    ? "Select an object in the scene first"
                    : canAdd
                        ? $"{GazeEditorStrings.TargetsAddButton} ({selected.name})"
                        : $"'{selected.name}' is already marked";

                if (GUILayout.Button(label, GUILayout.Height(24f)))
                {
                    Undo.AddComponent<ConvaiGazeTarget>(selected);
                    InvalidateModels();
                }
            }
        }

        /// <summary>
        ///     The six controls the component inspector deliberately does not show. They exist for
        ///     split-screen, cutscene rigs, kiosks and presenters — roughly one project in ten —
        ///     and putting them on the first-run surface is what made that surface unreadable.
        /// </summary>
        private void DrawAdvancedTargeting()
        {
            DrawSectionTitle(GazeEditorStrings.TargetsAdvancedTitle);
            DrawBody(GazeEditorStrings.TargetsAdvancedBody);
            GUILayout.Space(4f);

            SerializedObject serialized = ControllerSerialized;

            DrawAdvancedField(serialized, "autoCreatePlayerAnchor", "Create A Player Anchor Automatically",
                "On by default. Turn this off only if you provide your own gaze target source.");
            DrawAdvancedField(serialized, "playerAnchorAimMode", "Aim Point",
                "Where on the player the character actually looks. Automatic suits every normal camera rig.");
            DrawAdvancedField(serialized, "playerAnchorAimOffset", "Aim Offset",
                "Used when Aim Point is set to an offset — authored in the anchor's own space.");
            DrawAdvancedField(serialized, "focusFidelity", "Focus Style",
                "Relaxed keeps subtle eye life while focused. Locked suppresses look-aways entirely, " +
                "for presenter and kiosk characters.");
            DrawAdvancedField(serialized, "allowScriptedOverridesDuringExactFocus", "Let Scripts Override A Locked Focus",
                "Whether a GazeAt() call from your code can pull a locked character's gaze away.");
            DrawAdvancedField(serialized, "lockBlocksGlances", "Ignore Glances While Focused",
                "While focus is held, brief glances are absorbed so nothing tugs the gaze off the player.");

            if (!serialized.ApplyModifiedProperties()) return;

            InvalidateModels();
            if (UnityEngine.Application.isPlaying)
            {
                // Play-mode edits go through the runtime properties so the live solve re-targets
                // immediately instead of on the next enable.
                _controller.PlayerAnchorAimMode = (GazeAnchorAimMode)serialized
                    .FindProperty("playerAnchorAimMode").intValue;
                _controller.PlayerAnchorAimOffset = serialized
                    .FindProperty("playerAnchorAimOffset").vector3Value;
                _controller.FocusFidelity = (GazeFocusFidelity)serialized
                    .FindProperty("focusFidelity").intValue;
                _controller.AllowScriptedOverridesDuringExactFocus = serialized
                    .FindProperty("allowScriptedOverridesDuringExactFocus").boolValue;
                _controller.LockBlocksGlances = serialized.FindProperty("lockBlocksGlances").boolValue;
            }
        }

        private static void DrawAdvancedField(
            SerializedObject serialized, string fieldName, string label, string tooltip)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null) return;
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
        }

        // ------------------------------------------------------------------ live

        private void DrawLiveMode()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    if (!UnityEngine.Application.isPlaying)
                    {
                        DrawSectionTitle(GazeEditorStrings.LiveOfflineTitle);
                        DrawBody(GazeEditorStrings.LiveOfflineBody);
                        return;
                    }

                    _controller.CaptureSnapshot(_snapshot);

                    DrawSectionTitle("Right now");
                    DrawLiveRow("Looking at", _snapshot.Reading.Target != null
                        ? _snapshot.Reading.Target.name
                        : _snapshot.Reading.TargetKind.ToString());
                    DrawLiveRow("Conversation state", _snapshot.DialogueState.ToString());
                    DrawLiveRow("How committed", _snapshot.Reading.Engagement.ToString("0.00"));
                    DrawLiveRow("Eyes", _snapshot.EyePhase);
                    DrawLiveRow("Blink", _snapshot.BlinkWeight.ToString("0.00"));
                    DrawLiveRow("Turning its body", _snapshot.IsReorienting ? "yes" : "no");

                    if (!float.IsNaN(_snapshot.ContactErrorDegrees))
                        DrawLiveRow("Off target by", $"{_snapshot.ContactErrorDegrees:0.0}°");
                    if (_snapshot.PlayerAttention >= 0f)
                        DrawLiveRow("You are", _snapshot.PlayerLooking ? "looking at it" : "looking away");

                    DrawSectionTitle("Angles");
                    DrawLiveRow("Head", $"{_snapshot.HeadAngles.x:0.0}° / {_snapshot.HeadAngles.y:0.0}°");
                    DrawLiveRow("Chest", $"{_snapshot.TorsoAngles.x:0.0}° / {_snapshot.TorsoAngles.y:0.0}°");
                    DrawLiveRow("Left eye", $"{_snapshot.LeftEyeAngles.x:0.0}° / {_snapshot.LeftEyeAngles.y:0.0}°");
                    DrawLiveRow("Right eye", $"{_snapshot.RightEyeAngles.x:0.0}° / {_snapshot.RightEyeAngles.y:0.0}°");

                    if (_snapshot.RecentTrace.Count > 0)
                    {
                        DrawSectionTitle("What just happened");
                        for (int i = 0; i < _snapshot.RecentTrace.Count; i++)
                        {
                            GazeTraceEntry entry = _snapshot.RecentTrace[i];
                            GUILayout.Label($"[{entry.Time:0.0}s] {entry.Message}", ConvaiEditorStyles.MicroLabel);
                        }
                    }

                    GUILayout.Space(16f);
                }
                GUILayout.Space(14f);
            }
        }

        private static void DrawLiveRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, ConvaiEditorStyles.RowLabel, GUILayout.Width(150f));
                GUILayout.Label(value, ConvaiEditorStyles.MicroLabel);
            }
        }
    }
}
