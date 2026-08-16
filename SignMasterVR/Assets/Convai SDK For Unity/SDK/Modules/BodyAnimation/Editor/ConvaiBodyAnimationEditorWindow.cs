using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Components;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     The Body Animation Editor window's top-level modes: Setup
    ///     mirrors the component inspector's checklist; Content is the window's actual reason to
    ///     exist (pools, locomotion, actions, pointing); Feel exposes the complete behavior config
    ///     behind the inspector's three sliders; Live is a deeper Play-Mode monitor.
    /// </summary>
    internal enum BodyAnimationEditorMode
    {
        Setup = 0,
        Content = 1,
        Feel = 2,
        Live = 3
    }

    /// <summary>
    ///     Depth-only workshop for Body Animation, opened from the component inspector's footer
    ///     link, and never a required step. The
    ///     inspector carries the common path (state → setup → personality → live summary); this
    ///     window carries everything that cannot fit there: full content authoring, the complete
    ///     ~100-field config surface, and a deeper live monitor.
    /// </summary>
    /// <remarks>
    ///     Deliberately mirrors <c>ConvaiActionsEditorWindow</c>: the same mode enum +
    ///     <see cref="SessionState" /> persistence, a hero header, a two-pane split (this window's
    ///     left pane is the character picker itself — every <see cref="ConvaiBodyAnimationController" />
    ///     in the open scenes as a status card), a strings/theme separation
    ///     (<see cref="BodyAnimationEditorStrings" />), a shared window frame, and
    ///     the same <c>Undo.RecordObject</c>/<c>SerializedObject</c> → mutate →
    ///     <c>EditorUtility.SetDirty</c> idiom every field write in the partial mode files follows.
    ///     Grey (unconfigured) cards deliberately carry no "Add" button — adding the component is
    ///     Unity's own Add Component gesture; the card only explains that and pings
    ///     the GameObject.
    /// </remarks>
    internal sealed partial class ConvaiBodyAnimationEditorWindow : EditorWindow
    {
        private const string ModeSessionKey = "Convai.BodyAnimationEditor.Mode";
        private const float LeftPaneWidth = 220f;

        private static readonly GUIContent HeroTitleContent = new(BodyAnimationEditorStrings.HeroTitle);
        private static readonly GUIContent HeroSubtitleContent = new(BodyAnimationEditorStrings.HeroSubtitle);

        private ConvaiBodyAnimationController _controller;
        private BodyAnimationEditorMode _mode;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        // Findings gathered for the currently selected controller — shared by the left pane's
        // status colour and the Setup mode's full-width mirror.
        private readonly List<BodyAnimationTroubleshooterFinding> _findings = new(16);
        private readonly List<string> _issuesScratch = new(16);
        private SerializedObject _serializedController;
        private SerializedProperty _setProp;
        private SerializedProperty _configProp;
        private SerializedProperty _profileProp;
        private SerializedProperty _animatorOverrideProp;
        private SerializedProperty _locomotionProviderProp;

        [MenuItem("Convai/Body Animation Editor", false, ConvaiEditorMenu.FeatureEditors + 1)]
        internal static void Open()
        {
            ConvaiBodyAnimationEditorWindow window = GetWindow<ConvaiBodyAnimationEditorWindow>(false, BodyAnimationEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            window.Show();
        }

        /// <summary>
        ///     Opens the window already targeting <paramref name="controller" /> in
        ///     <paramref name="mode" /> — the entry point the component inspector's footer link
        ///     uses. The menu item above exists only for people who already know the window; no
        ///     documented flow starts there (entry-point rule §0.1.3).
        /// </summary>
        internal static void ShowFor(ConvaiBodyAnimationController controller, BodyAnimationEditorMode mode = BodyAnimationEditorMode.Content)
        {
            ConvaiBodyAnimationEditorWindow window = GetWindow<ConvaiBodyAnimationEditorWindow>(false, BodyAnimationEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            if (controller != null) window.SetController(controller);
            window.SetMode(mode);
            window.Show();
            window.Focus();
        }

        private void ApplyWindowChrome()
        {
            titleContent = new GUIContent(BodyAnimationEditorStrings.WindowTitle, ConvaiEditorIcons.Emblem());
            minSize = new Vector2(820f, 560f);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _mode = (BodyAnimationEditorMode)SessionState.GetInt(ModeSessionKey, (int)BodyAnimationEditorMode.Content);
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            Selection.selectionChanged += Repaint;

            if (_controller == null) AutoSelectController();
        }

        private void OnDisable()
        {
            StopPreview();
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            Selection.selectionChanged -= Repaint;
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode)
                StopPreview();
            Repaint();
        }

        private void OnGUI()
        {
            ConvaiEditorTheme.EnsureStyles();
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), ConvaiEditorTheme.WindowBg);

            ConvaiBodyAnimationController[] controllers = FindAllControllers();
            if (_controller == null && controllers.Length > 0) SetController(controllers[0]);

            RefreshFindings();

            DrawHero();

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawLeftPane(controllers);
                DrawVerticalDivider();

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawModeSwitcher();
                    _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
                    DrawModeBody();
                    EditorGUILayout.EndScrollView();
                }
            }

            if (UnityEngine.Application.isPlaying && _mode == BodyAnimationEditorMode.Live) Repaint();
        }

        private void DrawModeBody()
        {
            switch (_mode)
            {
                case BodyAnimationEditorMode.Setup:
                    DrawSetupMode();
                    break;
                case BodyAnimationEditorMode.Feel:
                    DrawFeelMode();
                    break;
                case BodyAnimationEditorMode.Live:
                    DrawLiveMode();
                    break;
                default:
                    DrawContentMode();
                    break;
            }
        }

        // ------------------------------------------------------------------ target selection

        private static ConvaiBodyAnimationController[] FindAllControllers() =>
            ConvaiObjectFind.All<ConvaiBodyAnimationController>(FindObjectsInactive.Include);

        private void AutoSelectController()
        {
            ConvaiBodyAnimationController[] controllers = FindAllControllers();
            if (controllers.Length > 0) SetController(controllers[0]);
        }

        private void SetController(ConvaiBodyAnimationController controller)
        {
            if (_controller == controller) return;

            StopPreview();
            _controller = controller;
            _serializedController = null;
            _setProp = _configProp = _profileProp = _animatorOverrideProp = _locomotionProviderProp = null;

            if (_controller != null)
            {
                _serializedController = new SerializedObject(_controller);
                _setProp = _serializedController.FindProperty("_animationSet");
                _configProp = _serializedController.FindProperty("_config");
                _profileProp = _serializedController.FindProperty("profile");
                _animatorOverrideProp = _serializedController.FindProperty("_animatorOverride");
                _locomotionProviderProp = _serializedController.FindProperty("_locomotionProviderOverride");
            }

            Repaint();
        }

        private void SetMode(BodyAnimationEditorMode mode)
        {
            _mode = mode;
            SessionState.SetInt(ModeSessionKey, (int)mode);
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        /// <summary>
        ///     Re-evaluates the shared finding model for the current controller — feeds the left
        ///     pane's card colour and the Setup mode's full-width mirror from one source, so the
        ///     two can never disagree.
        /// </summary>
        private void RefreshFindings()
        {
            _findings.Clear();
            if (_controller == null || _serializedController == null) return;

            _serializedController.Update();
            BodyAnimationTroubleshooterInput input = BodyAnimationTroubleshooter.GatherFrom(
                _controller, _setProp, _configProp, _profileProp, _animatorOverrideProp,
                _locomotionProviderProp, _issuesScratch, out _, out _);
            BodyAnimationTroubleshooter.Evaluate(in input, _findings);
        }

        // ------------------------------------------------------------------ hero

        private void DrawHero()
        {
            GUIContent chip = null;
            Color chipTint = default;
            if (_controller != null)
            {
                BodyAnimationTroubleshooterSeverity worst = BodyAnimationTroubleshooter.WorstSeverity(_findings);
                (string label, Color color) = StatusFor(worst);
                chip = new GUIContent($"{_controller.name} — {label}");
                chipTint = color;
            }

            ConvaiEditorTheme.WindowHero(position.width, HeroTitleContent, HeroSubtitleContent, chip, chipTint);
        }

        private static (string label, Color color) StatusFor(BodyAnimationTroubleshooterSeverity worst) => worst switch
        {
            BodyAnimationTroubleshooterSeverity.Error => (BodyAnimationEditorStrings.CardNeedsAttention, ConvaiEditorTheme.Error),
            BodyAnimationTroubleshooterSeverity.Warning => (BodyAnimationEditorStrings.CardNeedsAttention, ConvaiEditorTheme.Warning),
            _ => (BodyAnimationEditorStrings.CardReady, ConvaiEditorTheme.AccentBright)
        };

        // ------------------------------------------------------------------ mode switcher

        /// <summary>
        ///     Tab labels in <see cref="BodyAnimationEditorMode" /> order, so the index the mode bar
        ///     returns is the mode itself. Live is a Play-mode view, so the editing set stops short of it.
        /// </summary>
        private static readonly GUIContent[] ModeTabsPlaying =
        {
            BodyAnimationEditorStrings.ModeSetup,
            BodyAnimationEditorStrings.ModeContent,
            BodyAnimationEditorStrings.ModeFeel,
            BodyAnimationEditorStrings.ModeLive
        };

        private static readonly GUIContent[] ModeTabsEditing =
        {
            BodyAnimationEditorStrings.ModeSetup,
            BodyAnimationEditorStrings.ModeContent,
            BodyAnimationEditorStrings.ModeFeel
        };

        private void DrawModeSwitcher()
        {
            GUIContent[] tabs = UnityEngine.Application.isPlaying ? ModeTabsPlaying : ModeTabsEditing;
            int clicked = ConvaiEditorTheme.ModeBar(tabs, (int)_mode);
            if (clicked >= 0)
                SetMode((BodyAnimationEditorMode)clicked);
        }

        // ------------------------------------------------------------------ left pane

        private void DrawLeftPane(ConvaiBodyAnimationController[] controllers)
        {
            Rect paneRect = EditorGUILayout.BeginVertical(GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(paneRect, ConvaiEditorTheme.PaneBg);

            ConvaiEditorControls.GroupCaption(BodyAnimationEditorStrings.LeftPaneTitle);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

            if (controllers.Length == 0)
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        ConvaiEditorControls.GroupCaption(BodyAnimationEditorStrings.NoControllersTitle);
                        GUILayout.Label(BodyAnimationEditorStrings.NoControllersBody, ConvaiEditorTheme.CaptionWrapped);
                    }
                    GUILayout.Space(10f);
                }
            }
            else
            {
                for (int i = 0; i < controllers.Length; i++)
                    DrawControllerCard(controllers[i]);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawControllerCard(ConvaiBodyAnimationController controller)
        {
            bool selected = controller == _controller;

            BodyAnimationPreflight preflight = BodyAnimationSetupService.Inspect(controller);
            BodyAnimationTroubleshooterSeverity worst = selected
                ? BodyAnimationTroubleshooter.WorstSeverity(_findings)
                : ControllerSeverity(controller, preflight);
            Color dotColor = worst >= BodyAnimationTroubleshooterSeverity.Warning || !preflight.IsConfigured
                ? (preflight.IsConfigured ? ConvaiEditorTheme.Warning : ConvaiEditorTheme.TextMuted)
                : ConvaiEditorTheme.AccentBright;

            string statusText = !preflight.IsConfigured
                ? BodyAnimationEditorStrings.CardNotSetUp
                : worst >= BodyAnimationTroubleshooterSeverity.Warning
                    ? BodyAnimationEditorStrings.CardNeedsAttention
                    : BodyAnimationEditorStrings.CardReady;

            // Grey (unconfigured) cards never grow an "Add" affordance — adding the component is
            // Unity's own Add Component gesture. Selecting the card still pings the
            // GameObject and its tooltip explains why there is nothing else to click here.
            string tooltip = !preflight.IsConfigured ? BodyAnimationEditorStrings.GreyCardHint : null;

            _cardTitleScratch.text = controller.name;
            bool clicked = ConvaiEditorTheme.SelectableCard(
                _cardTitleScratch, statusText, dotColor, selected, out _, tooltip);
            if (clicked)
            {
                SetController(controller);
                Selection.activeGameObject = controller.gameObject;
            }
        }

        /// <summary>Reused per-card title content so the card list allocates nothing per repaint.</summary>
        private readonly GUIContent _cardTitleScratch = new();

        /// <summary>Worst finding severity for a controller other than the currently selected one (cheap, no caching needed for a handful of cards).</summary>
        private static BodyAnimationTroubleshooterSeverity ControllerSeverity(ConvaiBodyAnimationController controller, BodyAnimationPreflight preflight)
        {
            if (!preflight.IsConfigured) return BodyAnimationTroubleshooterSeverity.Warning;

            var serialized = new SerializedObject(controller);
            var scratch = new List<string>(8);
            var findings = new List<BodyAnimationTroubleshooterFinding>(8);
            BodyAnimationTroubleshooterInput input = BodyAnimationTroubleshooter.GatherFrom(
                controller,
                serialized.FindProperty("_animationSet"),
                serialized.FindProperty("_config"),
                serialized.FindProperty("profile"),
                serialized.FindProperty("_animatorOverride"),
                serialized.FindProperty("_locomotionProviderOverride"),
                scratch, out _, out _);
            BodyAnimationTroubleshooter.Evaluate(in input, findings);
            return BodyAnimationTroubleshooter.WorstSeverity(findings);
        }

        private static void DrawVerticalDivider()
        {
            Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(divider, ConvaiEditorTheme.Divider);
        }
    }
}
