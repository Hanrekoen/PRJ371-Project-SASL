using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Editor.Embodiment.Setup;
using Convai.Editor.UI;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Embodiment
{
    /// <summary>Which view the embodiment window is showing.</summary>
    internal enum ConvaiEmbodimentWindowMode
    {
        /// <summary>Get a character working: rig, features, one-click setup.</summary>
        Setup = 0,

        /// <summary>Which preset assets exist and whether they are valid.</summary>
        Presets = 1,

        /// <summary>What the character is doing right now, in Play Mode.</summary>
        Live = 2
    }

    /// <summary>
    ///     One window for setting up a Convai character's expressive features and watching them run.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Replaces the need to know that gaze, emotion and body animation are three separate
    ///         products with three separate windows, and supersedes the old Embodiment Live Inspector —
    ///         which was filed under a Developer menu <em>and</em> compiled out behind a define that
    ///         was set nowhere, so it had no entry point at all.
    ///     </para>
    ///     <para>
    ///         The per-feature windows stay: this one answers "is my character set up and what is it
    ///         doing", and links out to them for deep tuning.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiEmbodimentWindow : EditorWindow
    {
        private const string ModeSessionKey = "Convai.Embodiment.Window.Mode";
        private const float RepaintInterval = 0.1f;

        #region Cached content

        /// <summary>Tab labels, in <see cref="ConvaiEmbodimentWindowMode" /> order.</summary>
        private static readonly GUIContent[] ModeTabLabels =
        {
            new("Setup", "Get this character's expressive features working."),
            new("Presets", "Preset assets in this project and whether they are valid."),
            new("Live", "What the character is doing right now, in Play Mode.")
        };

        private static readonly GUIContent FollowSelectionLabel = new(
            "Follow Selection", "Track whatever character is selected in the scene.");

        private static readonly GUIContent FeaturesHeaderContent = new("Features");
        private static readonly GUIContent PresetsHeaderContent = new("Presets");
        private static readonly GUIContent ConversationHeaderContent = new("Conversation");
        private static readonly GUIContent EmotionHeaderContent = new("Emotion");

        private static readonly GUIContent WindowTitleContent = new("Convai Embodiment");
        private static readonly GUIContent WindowSubtitleContent = new("Set up a character's gaze, emotion and body");

        private static readonly GUIContent SetUpCharacterContent = new("Set Up This Character");
        private static readonly GUIContent CreatePresetContent = new("Create A Preset");
        private static readonly GUIContent SelectContent = new("Select");
        private static readonly GUIContent AddContent = new("Add");
        private static readonly GUIContent OpenContent = new("Open");

        // Reused per draw: their text comes from live reports and per-finding labels.
        private static readonly GUIContent ScratchRigTitle = new();
        private static readonly GUIContent ScratchStatus = new();
        private static readonly GUIContent ScratchFindingFix = new();

        #endregion

        private ConvaiEmbodimentWindowMode _mode;
        private GameObject _character;
        private bool _followSelection = true;
        private Vector2 _scroll;
        private double _lastRepaint;

        /// <summary>
        ///     The rig report for the current pass, recomputed when the character changes or a fix is
        ///     applied — not on every repaint.
        /// </summary>
        /// <remarks>
        ///     This field was previously written in three places and read in none, while
        ///     <see cref="EmbodimentRigSetupService.Inspect" /> ran fresh on every OnGUI, allocating a
        ///     finding list and a handful of formatted strings each time. Inspect is still safe to
        ///     call per repaint; it just no longer needs to be.
        /// </remarks>
        private EmbodimentSetupReport _rigReport;

        private GameObject _reportedCharacter;
        private bool _rigReportValid;

        // "Embodiment Editor", not "Embodiment": its four siblings in this band are all named
        // "<Feature> Editor", and the odd one out read like a concept rather than a window.
        [MenuItem("Convai/Embodiment Editor", false, ConvaiEditorMenu.FeatureEditors + 4)]
        internal static void Open()
        {
            ConvaiEmbodimentWindow window = GetWindow<ConvaiEmbodimentWindow>(false, "Convai Embodiment", true);
            window.minSize = new Vector2(420f, 480f);
        }

        private void OnEnable()
        {
            _mode = (ConvaiEmbodimentWindowMode)SessionState.GetInt(
                ModeSessionKey, (int)ConvaiEmbodimentWindowMode.Setup);

            // The report is cached per character, so anything that can change what it would say —
            // adding an Animator, adding the rig component, swapping the model — has to drop it.
            // Without this the cache would trade a per-repaint cost for a stale answer, which is the
            // worse of the two.
            EditorApplication.hierarchyChanged += InvalidateRigReport;
            Undo.undoRedoPerformed += InvalidateRigReport;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= InvalidateRigReport;
            Undo.undoRedoPerformed -= InvalidateRigReport;
        }

        private void OnGUI()
        {
            Theme.WindowHero(position.width, WindowTitleContent, WindowSubtitleContent);

            DrawCharacterPicker();
            DrawModeTabs();
            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_mode)
            {
                case ConvaiEmbodimentWindowMode.Setup: DrawSetup(); break;
                case ConvaiEmbodimentWindowMode.Presets: DrawPresets(); break;
                case ConvaiEmbodimentWindowMode.Live: DrawLive(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        // ── frame ──────────────────────────────────────────────────────────────────

        private void DrawCharacterPicker()
        {
            using (Theme.PanelScope())
            using (new EditorGUILayout.HorizontalScope())
            {
                _followSelection = EditorGUILayout.ToggleLeft(
                    FollowSelectionLabel, _followSelection, GUILayout.Width(130f));

                using (new EditorGUI.DisabledScope(_followSelection))
                {
                    _character = (GameObject)EditorGUILayout.ObjectField(
                        ResolveCharacter(), typeof(GameObject), true);
                }
            }
        }

        private void DrawModeTabs()
        {
            int clicked = Controls.SegmentedPicker(ModeTabLabels, (int)_mode);
            if (clicked < 0 || clicked == (int)_mode) return;

            _mode = (ConvaiEmbodimentWindowMode)clicked;
            SessionState.SetInt(ModeSessionKey, clicked);
            InvalidateRigReport();
        }

        /// <summary>The character the window is looking at, honouring Follow Selection.</summary>
        private GameObject ResolveCharacter()
        {
            if (!_followSelection) return _character;

            GameObject selected = Selection.activeGameObject;
            if (selected == null) return _character;

            ConvaiCharacter character = selected.GetComponentInParent<ConvaiCharacter>(true);
            _character = character != null ? character.gameObject : selected;
            return _character;
        }

        // ── Setup ───────────────────────────────────────────────────────────────────

        private void DrawSetup()
        {
            GameObject character = ResolveCharacter();
            if (character == null)
            {
                Theme.InfoBox(
                    "Pick A Character",
                    "Select a character in the scene to set up its expressive features.");
                return;
            }

            EmbodimentSetupReport report = ResolveRigReport(character);

            using (Theme.CardScope())
            {
                ScratchRigTitle.text = $"Rig — {report.HeaderStatus}";
                Theme.SectionHeader(ConvaiEditorGlyphs.Validation, ScratchRigTitle);
                DrawFindings(report, InvalidateRigReport);

                if (Controls.PrimaryButtonLayout(SetUpCharacterContent))
                    RunRigSetup(character);
            }

            using (Theme.CardScope())
            {
                Theme.SectionHeader(ConvaiEditorGlyphs.Profile, FeaturesHeaderContent);
                DrawFeatureList(character);
            }
        }

        /// <summary>
        ///     The cached rig report, rebuilt when the inspected character changes or a fix lands.
        /// </summary>
        private EmbodimentSetupReport ResolveRigReport(GameObject character)
        {
            if (_rigReportValid && ReferenceEquals(_reportedCharacter, character)) return _rigReport;

            _rigReport = EmbodimentRigSetupService.Inspect(character);
            _reportedCharacter = character;
            _rigReportValid = true;
            return _rigReport;
        }

        /// <summary>Drops the cached report so the next draw reads the scene again.</summary>
        private void InvalidateRigReport()
        {
            _rigReport = default;
            _reportedCharacter = null;
            _rigReportValid = false;
        }

        /// <summary>
        ///     Applies rig setup after the current IMGUI pass. Setup mutates the scene and can surface
        ///     dialogs; doing that mid-pass corrupts the layout state the enclosing scope will close.
        /// </summary>
        private void RunRigSetup(GameObject character)
        {
            EditorApplication.delayCall += () =>
            {
                if (character == null) return;

                EmbodimentRigSetupResult result = EmbodimentRigSetupService.Apply(character);
                ShowNotification(new GUIContent(result.Summary));
                InvalidateRigReport();
                Repaint();
            };
        }

        private void DrawFeatureList(GameObject character)
        {
            IReadOnlyList<EmbodimentModuleDescriptor> all = EmbodimentModuleCatalog.Modules;

            for (int i = 0; i < all.Count; i++)
            {
                EmbodimentModuleDescriptor module = all[i];
                Component component = character.GetComponentInChildren(module.ControllerType, true);
                bool present = component != null;

                using (Theme.PanelScope(null, 4f))
                {
                    Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                    Theme.StatusDot(
                        new Vector2(row.x + 6f, row.y + (row.height * 0.5f)),
                        present ? Theme.StatusReady : Theme.StatusIdle);

                    var button = new Rect(row.xMax - 64f, row.y, 64f, 18f);
                    GUI.Label(
                        new Rect(row.x + 18f, row.y, Mathf.Max(40f, button.x - row.x - 24f), row.height),
                        module.DisplayName, ConvaiEditorStyles.CardTitle);

                    if (present)
                    {
                        if (Controls.GhostButton(button, SelectContent))
                            Selection.activeObject = component;
                    }
                    else if (Controls.GhostButton(button, AddContent))
                    {
                        Undo.AddComponent(character, module.ControllerType);
                    }

                    if (!string.IsNullOrEmpty(module.Description))
                        GUILayout.Label(module.Description, ConvaiEditorStyles.CaptionWrapped);
                }
            }
        }

        // ── Presets ─────────────────────────────────────────────────────────────────

        private void DrawPresets()
        {
            Theme.InfoBox(
                "Presets Are Optional",
                "A preset hands one set of settings to each feature at once. Every feature also works " +
                "on its own, so presets are optional.");

            string[] guids = AssetDatabase.FindAssets("t:ConvaiEmbodimentPreset");
            if (guids.Length == 0)
            {
                using (Theme.CardScope())
                {
                    Theme.SectionHeader(ConvaiEditorGlyphs.Content, PresetsHeaderContent);
                    GUILayout.Label("No presets in this project.", ConvaiEditorStyles.CaptionWrapped);
                    GUILayout.Space(4f);
                    if (Controls.GhostButtonLayout(CreatePresetContent))
                        CreatePresetAsset();
                }

                return;
            }

            using (Theme.CardScope())
            {
                Theme.SectionHeader(ConvaiEditorGlyphs.Content, PresetsHeaderContent);

                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var preset = AssetDatabase.LoadAssetAtPath<Modules.Embodiment.Presets.ConvaiEmbodimentPreset>(path);
                    if (preset == null) continue;

                    EmbodimentSetupReport report = EmbodimentPresetTroubleshooter.Evaluate(preset);
                    Color tint = report.HasBlocker
                        ? Theme.StatusError
                        : report.WorstSeverity == EmbodimentFindingSeverity.Warning
                            ? Theme.StatusWarn
                            : Theme.StatusReady;

                    using (Theme.PanelScope(null, 4f))
                    {
                        Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                        Theme.StatusDot(new Vector2(row.x + 6f, row.y + (row.height * 0.5f)), tint);

                        var open = new Rect(row.xMax - 60f, row.y, 60f, 18f);

                        ScratchStatus.text = report.HeaderStatus;
                        float statusWidth = Mathf.Min(130f, Controls.PillWidth(ScratchStatus));
                        Controls.Pill(
                            new Rect(open.x - statusWidth - 8f, row.y + 1f, statusWidth, 18f),
                            ScratchStatus, tint);

                        GUI.Label(
                            new Rect(row.x + 18f, row.y, Mathf.Max(40f, open.x - statusWidth - row.x - 30f), row.height),
                            preset.name, ConvaiEditorStyles.CardTitle);

                        if (Controls.GhostButton(open, OpenContent))
                            Selection.activeObject = preset;
                    }
                }
            }
        }

        /// <summary>
        ///     Opens the save panel after the current IMGUI pass — a modal raised from inside a layout
        ///     scope discards the layout state the enclosing scope is about to close.
        /// </summary>
        private static void CreatePresetAsset()
        {
            EditorApplication.delayCall += () =>
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Create Embodiment Preset", "ConvaiEmbodimentPreset", "asset",
                    "Where should the preset be saved?");
                if (string.IsNullOrEmpty(path)) return;

                var preset = CreateInstance<Modules.Embodiment.Presets.ConvaiEmbodimentPreset>();
                AssetDatabase.CreateAsset(preset, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = preset;
            };
        }

        // ── Live ────────────────────────────────────────────────────────────────────

        private void DrawLive()
        {
            if (!UnityEngine.Application.isPlaying)
            {
                Theme.InfoBox("Not Running", "Enter Play Mode to watch the character's live state.");
                return;
            }

            GameObject character = ResolveCharacter();
            EmbodimentContext context = character == null
                ? null
                : character.GetComponentInChildren<EmbodimentContext>(true);

            if (context == null)
            {
                Theme.WarningBox(
                    "No Running Character Selected",
                    "Select a running Convai character to see what it is doing.");
                return;
            }

            EmbodimentLiveState live = EmbodimentLiveStateService.Read(context);
            DrawDialogueState(live);
            DrawEmotionScores(live);
        }

        private static void DrawDialogueState(in EmbodimentLiveState live)
        {
            using (Theme.CardScope())
            {
                Theme.SectionHeader(ConvaiEditorGlyphs.Live, ConversationHeaderContent);

                if (!live.HasConversationFlow)
                {
                    GUILayout.Label("Conversation Flow is not on this character.", ConvaiEditorStyles.CaptionWrapped);
                    return;
                }

                DialogueStateReading reading = live.Conversation;
                EditorGUILayout.LabelField("State", reading.Primary.ToString());
                EditorGUILayout.LabelField("Blending To", reading.BlendTo.ToString());

                Rect blendRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(blendRect, reading.BlendWeight, $"Blend {reading.BlendWeight:F2}");

                EditorGUILayout.LabelField("Time In State", $"{reading.TimeInState:F1}s");
                EditorGUILayout.LabelField("Energy", $"{reading.EnergyLevel:F2}");
            }
        }

        private static void DrawEmotionScores(in EmbodimentLiveState live)
        {
            using (Theme.CardScope())
            {
                Theme.SectionHeader(ConvaiEditorGlyphs.Blink, EmotionHeaderContent);

                if (!live.HasEmotion)
                {
                    GUILayout.Label("Emotion is not on this character.", ConvaiEditorStyles.CaptionWrapped);
                    return;
                }

                IReadOnlyList<EmbodimentEmotionScore> ordered = live.Emotions;
                if (ordered.Count == 0)
                {
                    GUILayout.Label("No emotion detected yet.", ConvaiEditorStyles.CaptionWrapped);
                    return;
                }

                int shown = Mathf.Min(5, ordered.Count);
                for (int i = 0; i < shown; i++)
                {
                    Rect rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(rect, ordered[i].Score, $"{ordered[i].Label}  {ordered[i].Score:F2}");
                }
            }
        }

        // ── shared ──────────────────────────────────────────────────────────────────

        private static void DrawFindings(EmbodimentSetupReport report, System.Action onFixed)
        {
            for (int i = 0; i < report.Findings.Count; i++)
            {
                EmbodimentFinding finding = report.Findings[i];

                Color severity = finding.Severity switch
                {
                    EmbodimentFindingSeverity.Error => Theme.StatusError,
                    EmbodimentFindingSeverity.Warning => Theme.StatusWarn,
                    EmbodimentFindingSeverity.Info => Theme.StatusInfo,
                    _ => Theme.StatusReady
                };

                using (Theme.PanelScope(severity, 6f, 2f))
                {
                    GUILayout.Label(finding.Title, ConvaiEditorStyles.CardTitle);
                    if (!string.IsNullOrEmpty(finding.Message))
                        GUILayout.Label(finding.Message, ConvaiEditorStyles.MutedWrapped);

                    if (!finding.CanFix) continue;

                    GUILayout.Space(4f);
                    ScratchFindingFix.text = finding.FixLabel;
                    if (Controls.GhostButtonLayout(ScratchFindingFix, 20f))
                    {
                        finding.Fix();
                        onFixed?.Invoke();
                    }
                }
            }
        }

        private void OnInspectorUpdate()
        {
            if (!UnityEngine.Application.isPlaying || _mode != ConvaiEmbodimentWindowMode.Live) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < RepaintInterval) return;

            _lastRepaint = now;
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (_followSelection) Repaint();
        }
    }
}
