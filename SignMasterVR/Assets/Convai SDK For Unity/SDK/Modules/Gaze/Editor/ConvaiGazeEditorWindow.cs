using System;
using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Data;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     The Gaze editor window's top-level modes. Setup mirrors the component inspector's
    ///     checklist and adds the rig report; Feel is the complete personality surface behind the
    ///     inspector's three dials; Targets is scene-wide authoring plus the advanced targeting
    ///     fields; Live is a deeper Play-Mode monitor.
    /// </summary>
    internal enum GazeEditorMode
    {
        Setup = 0,
        Feel = 1,
        Targets = 2,
        Live = 3
    }

    /// <summary>
    ///     Depth-only workshop for Gaze, opened from the component inspector's footer link — never
    ///     a required step. The inspector carries the common path (is it ready → who it watches →
    ///     how it feels → what it is doing); this window carries everything that cannot fit there.
    /// </summary>
    /// <remarks>
    ///     Deliberately mirrors <c>ConvaiBodyAnimationEditorWindow</c> and
    ///     <c>ConvaiActionsEditorWindow</c>: the same mode enum + <see cref="SessionState" />
    ///     persistence, a hero header, a two-pane split whose left pane is the character picker,
    ///     a strings/theme separation, and the same <c>SerializedObject</c> → mutate →
    ///     <c>SetDirty</c> idiom for every field write. Grey (non-working) cards carry no "Add"
    ///     button — adding the component is Unity's own Add Component gesture; the card explains
    ///     that and pings the GameObject.
    /// </remarks>
    internal sealed partial class ConvaiGazeEditorWindow : EditorWindow
    {
        private const string ModeSessionKey = "Convai.GazeEditor.Mode";
        private const float LeftPaneWidth = 220f;

        private static readonly GUIContent HeroTitleContent = new(GazeEditorStrings.HeroTitle);
        private static readonly GUIContent HeroSubtitleContent = new(GazeEditorStrings.HeroSubtitle);

        private ConvaiGazeController _controller;
        private GazeEditorMode _mode;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        private GazePreflight _preflight;
        private readonly List<GazeSetupFinding> _findings = new(8);
        private readonly List<GazeCapabilityInfoRow> _capabilityRows = new(8);

        /// <summary>
        ///     How long a rebuilt scene model stays good for. Everything the models are built from —
        ///     a scene scan, a preflight per character, a capability scan, a serialized read — is
        ///     cheap once and expensive sixty times a second, which is what this window used to do:
        ///     the whole set was rebuilt inside <see cref="OnGUI" />, and <c>wantsMouseMove</c> means
        ///     OnGUI runs on every mouse move across the window. A quarter second is under the
        ///     threshold where a user notices staleness and far above the repaint rate.
        /// </summary>
        private const double ModelRefreshIntervalSeconds = 0.25d;

        private ConvaiGazeController[] _controllers = Array.Empty<ConvaiGazeController>();
        private GazePreflight[] _controllerPreflights = Array.Empty<GazePreflight>();
        private readonly List<GazeCapabilityInfo> _capabilityScratch = new(GazeCapabilities.Count);
        private int _sharedProfileUsers;
        private ConvaiEditorRefreshTimer _modelTimer;
        private bool _modelsResolved;
        private SerializedObject _controllerSerialized;

        /// <summary>
        ///     The selected controller's <see cref="SerializedObject" />, rebuilt only when the
        ///     selection changes. Several mode bodies read and write controller fields; each used to
        ///     construct its own instance inside its draw call, which is an allocation and a full
        ///     serialized-property rebuild per repaint per mode.
        /// </summary>
        private SerializedObject ControllerSerialized
        {
            get
            {
                if (_controller == null) return null;
                if (_controllerSerialized == null || _controllerSerialized.targetObject != _controller)
                    _controllerSerialized = new SerializedObject(_controller);

                _controllerSerialized.Update();
                return _controllerSerialized;
            }
        }

        [MenuItem("Convai/Gaze Editor", false, ConvaiEditorMenu.FeatureEditors + 3)]
        internal static void Open()
        {
            ConvaiGazeEditorWindow window = GetWindow<ConvaiGazeEditorWindow>(false, GazeEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            window.Show();
        }

        /// <summary>
        ///     Opens the window already targeting <paramref name="controller" /> — the entry point
        ///     the component inspector's footer link uses. The menu item above exists only for
        ///     people who already know the window; no documented flow starts there.
        /// </summary>
        internal static void ShowFor(ConvaiGazeController controller, GazeEditorMode mode = GazeEditorMode.Feel)
        {
            ConvaiGazeEditorWindow window = GetWindow<ConvaiGazeEditorWindow>(false, GazeEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            if (controller != null) window.SetController(controller);
            window.SetMode(mode);
            window.Show();
            window.Focus();
        }

        private void ApplyWindowChrome()
        {
            titleContent = new GUIContent(GazeEditorStrings.WindowTitle, ConvaiEditorIcons.Emblem());
            minSize = new Vector2(820f, 560f);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _mode = (GazeEditorMode)SessionState.GetInt(ModeSessionKey, (int)GazeEditorMode.Setup);
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            Selection.selectionChanged += HandleSelectionChanged;

            if (_controller == null) AutoSelectController();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            Selection.selectionChanged -= HandleSelectionChanged;
            ReleaseProfileEditor();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            InvalidateModels();
            Repaint();
        }

        /// <summary>
        ///     Selection feeds the Targets tab's "mark the selected object" button, so a new
        ///     selection has to reach the models rather than only triggering a repaint of stale ones.
        /// </summary>
        private void HandleSelectionChanged()
        {
            InvalidateModels();
            Repaint();
        }

        private void OnGUI()
        {
            ConvaiEditorTheme.EnsureStyles();
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), ConvaiEditorTheme.WindowBg);

            RefreshModelsIfDue();
            if (_controller == null && _controllers.Length > 0) SetController(_controllers[0]);

            DrawHero();

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawLeftPane();
                DrawVerticalDivider();

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawModeSwitcher();
                    _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
                    DrawModeBody();
                    EditorGUILayout.EndScrollView();
                }
            }

            // Invalidation is deliberately per-mutation (each site calls InvalidateModels itself)
            // rather than a blanket `if (GUI.changed)` here: a scroll view sets GUI.changed too, so
            // the blanket form would rebuild every frame the user drags the scrollbar — which is the
            // exact cost this caching removed.
            if (UnityEngine.Application.isPlaying && _mode == GazeEditorMode.Live) Repaint();
        }

        private void DrawModeBody()
        {
            if (_controller == null)
            {
                DrawEmptyState();
                return;
            }

            switch (_mode)
            {
                case GazeEditorMode.Feel:
                    DrawFeelMode();
                    break;
                case GazeEditorMode.Targets:
                    DrawTargetsMode();
                    break;
                case GazeEditorMode.Live:
                    DrawLiveMode();
                    break;
                default:
                    DrawSetupMode();
                    break;
            }
        }

        private void DrawEmptyState()
        {
            GUILayout.Space(24f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(24f);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(GazeEditorStrings.NoControllersTitle, ConvaiEditorTheme.SectionTitle);
                    GUILayout.Space(4f);
                    GUILayout.Label(GazeEditorStrings.NoControllersBody, ConvaiEditorTheme.CaptionWrapped);
                }
                GUILayout.Space(24f);
            }
        }

        // ------------------------------------------------------------------ target selection

        private static ConvaiGazeController[] FindAllControllers() =>
            ConvaiObjectFind.All<ConvaiGazeController>(FindObjectsInactive.Include);

        private void AutoSelectController()
        {
            ConvaiGazeController[] controllers = FindAllControllers();
            if (controllers.Length > 0) SetController(controllers[0]);
        }

        private void SetController(ConvaiGazeController controller)
        {
            if (_controller == controller) return;
            _controller = controller;
            RefreshModels();
            Repaint();
        }

        private void SetMode(GazeEditorMode mode)
        {
            _mode = mode;
            SessionState.SetInt(ModeSessionKey, (int)mode);
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        /// <summary>Marks the cached models stale so the next repaint rebuilds them.</summary>
        private void InvalidateModels() => _modelTimer.Invalidate(true);

        /// <summary>Rebuilds the models if the cache interval has elapsed. Cheap on every other frame.</summary>
        private void RefreshModelsIfDue()
        {
            if (!_modelTimer.ShouldRefresh(_modelsResolved, ModelRefreshIntervalSeconds)) return;
            RefreshModels();
        }

        /// <summary>
        ///     Re-evaluates every model the window draws from — the scene's controllers, a preflight
        ///     for each, the selected character's findings and capabilities, and the shared-profile
        ///     count — so the left pane's status dot, the hero chip and the mode bodies are all fed
        ///     from one source and can never disagree.
        /// </summary>
        private void RefreshModels()
        {
            _modelsResolved = true;
            _modelTimer.MarkRefreshed(ModelRefreshIntervalSeconds);

            RefreshControllerList();
            RefreshTargetRows();

            _findings.Clear();
            _capabilityRows.Clear();
            _sharedProfileUsers = 0;

            if (_controller == null)
            {
                _preflight = default;
                return;
            }

            _preflight = GazeSetupService.Inspect(_controller);

            SerializedObject serialized = ControllerSerialized;
            GazeSetupInput input = GazeSetupTroubleshooter.GatherFrom(
                _controller, serialized.FindProperty("profile"),
                serialized.FindProperty("autoCreatePlayerAnchor")?.boolValue ?? true);
            GazeSetupTroubleshooter.Evaluate(in input, _findings);

            RefreshCapabilityRows();
            RefreshSharedProfileUsers();
        }

        /// <summary>
        ///     Rescans the scene and preflights every controller once. The left pane needs a status
        ///     colour per card, and computing it inside the card draw meant one full preflight per
        ///     character per repaint.
        /// </summary>
        private void RefreshControllerList()
        {
            _controllers = FindAllControllers();

            if (_controllerPreflights.Length != _controllers.Length)
                _controllerPreflights = new GazePreflight[_controllers.Length];

            for (int i = 0; i < _controllers.Length; i++)
                _controllerPreflights[i] = GazeSetupService.Inspect(_controllers[i]);
        }

        /// <summary>
        ///     How many characters share the selected character's profile — the number behind the
        ///     Feel tab's "editing this changes both" notice.
        /// </summary>
        private void RefreshSharedProfileUsers()
        {
            ConvaiGazeProfile profile = GazeSetupService.ResolveAssignedProfile(_controller);
            if (profile == null) return;

            for (int i = 0; i < _controllers.Length; i++)
                if (GazeSetupService.ResolveAssignedProfile(_controllers[i]) == profile)
                    _sharedProfileUsers++;
        }

        // ------------------------------------------------------------------ hero

        private void DrawHero()
        {
            GUIContent chip = null;
            Color chipTint = default;
            if (_controller != null)
            {
                (string label, Color color) = StatusFor(_preflight);
                chip = new GUIContent($"{_controller.name} — {label}");
                chipTint = color;
            }

            ConvaiEditorTheme.WindowHero(position.width, HeroTitleContent, HeroSubtitleContent, chip, chipTint);
        }

        /// <summary>
        ///     Status wording, and the distinction that matters: a character with nothing assigned
        ///     is <b>Ready</b>, not "not set up". Gaze works out of the box, and only a rig it
        ///     cannot drive stops it.
        /// </summary>
        private static (string label, Color color) StatusFor(GazePreflight preflight)
        {
            if (preflight.Checks == null || preflight.Checks.Count == 0)
                return (GazeEditorStrings.CardReady, ConvaiEditorTheme.TextMuted);

            // Two states, not three. A character with an unassigned profile or an untagged camera
            // is working — those rows are suggestions, and colouring them as a problem is the
            // false alarm this round removes.
            return preflight.HasBlocker
                ? (GazeEditorStrings.CardNotWorking, ConvaiEditorTheme.Error)
                : (GazeEditorStrings.CardReady, ConvaiEditorTheme.AccentBright);
        }

        // ------------------------------------------------------------------ mode switcher

        private void DrawModeSwitcher()
        {
            Rect row = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            float x = row.x + 12f;
            float y = row.y + 4f;
            DrawModeButton(ref x, y, GazeEditorMode.Setup, GazeEditorStrings.ModeSetup);
            DrawModeButton(ref x, y, GazeEditorMode.Feel, GazeEditorStrings.ModeFeel);
            DrawModeButton(ref x, y, GazeEditorMode.Targets, GazeEditorStrings.ModeTargets);
            if (UnityEngine.Application.isPlaying)
                DrawModeButton(ref x, y, GazeEditorMode.Live, GazeEditorStrings.ModeLive);

            // Underlines this tab row only. Drawn from the window's left edge it ran back across the
            // character list beside it, reading as a black seam through that card.
            ConvaiEditorTheme.DividerLine(new Rect(row.x, row.yMax - 1f, row.width, 1f), ConvaiEditorTheme.Divider);
        }

        private void DrawModeButton(ref float x, float y, GazeEditorMode mode, GUIContent content)
        {
            float width = ConvaiEditorTheme.PillWidth(content) + 30f;
            var rect = new Rect(x, y, width, 24f);
            bool selected = _mode == mode;
            bool clicked = selected
                ? ConvaiEditorTheme.PrimaryButton(rect, content)
                : ConvaiEditorTheme.GhostButton(rect, content);
            if (clicked && !selected) SetMode(mode);
            x += width + 6f;
        }

        // ------------------------------------------------------------------ left pane

        private void DrawLeftPane()
        {
            Rect paneRect = EditorGUILayout.BeginVertical(
                GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(paneRect, ConvaiEditorTheme.PaneBg);

            ConvaiEditorControls.GroupCaption(GazeEditorStrings.LeftPaneTitle);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

            if (_controllers.Length == 0)
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        ConvaiEditorControls.GroupCaption(GazeEditorStrings.NoControllersTitle);
                        GUILayout.Label(GazeEditorStrings.NoControllersBody, ConvaiEditorTheme.CaptionWrapped);
                    }
                    GUILayout.Space(10f);
                }
            }
            else
            {
                for (int i = 0; i < _controllers.Length; i++) DrawControllerCard(i);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawControllerCard(int index)
        {
            ConvaiGazeController controller = _controllers[index];
            if (controller == null) return;

            bool selected = controller == _controller;
            // Preflighted once per model refresh, not once per card per repaint.
            GazePreflight preflight = selected ? _preflight : _controllerPreflights[index];
            (string status, Color color) = StatusFor(preflight);

            // A non-working card never grows an "Add" affordance — the fix is a rig decision, and
            // its tooltip says where to make it.
            string tooltip = preflight.HasBlocker ? GazeEditorStrings.GreyCardHint : null;

            _cardTitleScratch.text = controller.name;
            bool clicked = ConvaiEditorTheme.SelectableCard(
                _cardTitleScratch, status, color, selected, out _, tooltip);
            if (clicked)
            {
                SetController(controller);
                Selection.activeGameObject = controller.gameObject;
            }
        }

        /// <summary>Reused per-card title content so the card list allocates nothing per repaint.</summary>
        private readonly GUIContent _cardTitleScratch = new();

        private static void DrawVerticalDivider()
        {
            Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(divider, ConvaiEditorTheme.Divider);
        }

        // ------------------------------------------------------------------ shared drawing helpers

        private static void DrawSectionTitle(string title)
        {
            GUILayout.Space(10f);
            GUILayout.Label(title, ConvaiEditorTheme.SectionTitle);
        }

        private static void DrawBody(string text) => GUILayout.Label(text, ConvaiEditorTheme.CaptionWrapped);
    }
}
