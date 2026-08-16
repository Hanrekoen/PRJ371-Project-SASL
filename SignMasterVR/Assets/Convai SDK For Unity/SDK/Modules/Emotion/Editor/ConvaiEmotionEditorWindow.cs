using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.Emotion.Components;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     The Emotion editor window's top-level modes. Setup mirrors the component inspector's
    ///     checklist; Feel is the complete settings surface behind the inspector's handful of
    ///     controls; Expressions is the content surface plus the resolved face mapping; Live is a
    ///     deeper Play-Mode monitor.
    /// </summary>
    internal enum EmotionEditorMode
    {
        Setup = 0,
        Feel = 1,
        Expressions = 2,
        Live = 3
    }

    /// <summary>
    ///     Depth-only workshop for Emotions, opened from the component inspector's footer link —
    ///     never a required step. The inspector carries the common path (is it ready → what kind of
    ///     person is it → what is it feeling); this window carries everything that cannot fit
    ///     there.
    /// </summary>
    /// <remarks>
    ///     Deliberately mirrors <c>ConvaiGazeEditorWindow</c> and <c>ConvaiBodyAnimationEditorWindow</c>: the
    ///     same mode enum + <see cref="SessionState" /> persistence, a hero header, a two-pane split
    ///     whose left pane is the character picker, the shared <see cref="ConvaiEditorTheme" />,
    ///     and the same <c>SerializedObject</c> → mutate → <c>SetDirty</c> idiom for every field
    ///     write. Grey (not-set-up) cards carry no "Set Up" button — setup belongs on the component
    ///     inspector, where the checklist explaining it lives; the card says so and pings the
    ///     GameObject.
    /// </remarks>
    internal sealed partial class ConvaiEmotionEditorWindow : EditorWindow
    {
        private const string ModeSessionKey = "Convai.EmotionEditor.Mode";
        private const float LeftPaneWidth = 220f;

        private ConvaiEmotionController _controller;
        private EmotionEditorMode _mode;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        // Findings for the currently selected character — shared by the left pane's status colour
        // and the Setup mode's full-width mirror, so the two can never disagree.
        private readonly List<EmotionFinding> _findings = new(8);
        private EmotionPreflight _preflight;

        [MenuItem("Convai/Emotion Editor", false, ConvaiEditorMenu.FeatureEditors + 2)]
        internal static void Open()
        {
            ConvaiEmotionEditorWindow window =
                GetWindow<ConvaiEmotionEditorWindow>(false, EmotionEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            window.Show();
        }

        /// <summary>
        ///     Opens the window already targeting <paramref name="controller" /> — the entry point
        ///     the component inspector's footer link uses. The menu item above exists only for
        ///     people who already know the window; no documented flow starts there.
        /// </summary>
        internal static void ShowFor(
            ConvaiEmotionController controller, EmotionEditorMode mode = EmotionEditorMode.Feel)
        {
            ConvaiEmotionEditorWindow window =
                GetWindow<ConvaiEmotionEditorWindow>(false, EmotionEditorStrings.WindowTitle, true);
            window.ApplyWindowChrome();
            if (controller != null) window.SetController(controller);
            window.SetMode(mode);
            window.Show();
            window.Focus();
        }

        private void ApplyWindowChrome()
        {
            titleContent = new GUIContent(
                EmotionEditorStrings.WindowTitle, ConvaiEditorIcons.Emblem());
            minSize = new Vector2(820f, 560f);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _mode = (EmotionEditorMode)SessionState.GetInt(ModeSessionKey, (int)EmotionEditorMode.Feel);
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            Selection.selectionChanged += Repaint;

            if (_controller == null) AutoSelectController();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            Selection.selectionChanged -= Repaint;
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change) => Repaint();

        private void OnGUI()
        {
            ConvaiEditorTheme.EnsureStyles();
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height),
                    ConvaiEditorTheme.WindowBg);

            ConvaiEmotionController[] controllers = FindAllControllers();
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

            if (UnityEngine.Application.isPlaying && _mode == EmotionEditorMode.Live) Repaint();
        }

        private void DrawModeBody()
        {
            switch (_mode)
            {
                case EmotionEditorMode.Setup:
                    DrawSetupMode();
                    break;
                case EmotionEditorMode.Expressions:
                    DrawExpressionsMode();
                    break;
                case EmotionEditorMode.Live:
                    DrawLiveMode();
                    break;
                default:
                    DrawFeelMode();
                    break;
            }
        }

        // ------------------------------------------------------------------ target selection

        private static ConvaiEmotionController[] FindAllControllers() =>
            ConvaiObjectFind.All<ConvaiEmotionController>(FindObjectsInactive.Include);

        private void AutoSelectController()
        {
            ConvaiEmotionController[] controllers = FindAllControllers();
            if (controllers.Length > 0) SetController(controllers[0]);
        }

        private void SetController(ConvaiEmotionController controller)
        {
            if (_controller == controller) return;
            _controller = controller;
            Repaint();
        }

        private void SetMode(EmotionEditorMode mode)
        {
            _mode = mode;
            SessionState.SetInt(ModeSessionKey, (int)mode);
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        /// <summary>
        ///     Re-evaluates the shared finding model for the current character, so the left pane's
        ///     card colour and the Setup mode's mirror are fed from one source.
        /// </summary>
        private void RefreshFindings()
        {
            _findings.Clear();
            if (_controller == null) return;

            _preflight = EmotionSetupService.Inspect(_controller);
            EmotionTroubleshooter.Evaluate(_controller, in _preflight, _findings);
        }

        // ------------------------------------------------------------------ hero

        private void DrawHero()
        {
            GUIContent chip = null;
            Color chipTint = default;
            if (_controller != null)
            {
                (string label, Color color) = StatusFor(_controller, in _preflight, _findings);
                chip = new GUIContent($"{_controller.name} — {label}");
                chipTint = color;
            }

            ConvaiEditorTheme.WindowHero(
                position.width, EmotionEditorStrings.HeroTitle, EmotionEditorStrings.HeroSubtitle, chip, chipTint);
        }

        private static (string label, Color color) StatusFor(
            ConvaiEmotionController controller, in EmotionPreflight preflight, List<EmotionFinding> findings)
        {
            if (!preflight.IsConfigured)
                return (EmotionEditorStrings.CardNotSetUp, ConvaiEditorTheme.TextMuted);

            return EmotionTroubleshooter.WorstSeverity(findings) switch
            {
                EmotionSeverity.Error =>
                    (EmotionEditorStrings.CardNeedsAttention, ConvaiEditorTheme.Error),
                EmotionSeverity.Warning =>
                    (EmotionEditorStrings.CardNeedsAttention, ConvaiEditorTheme.Warning),
                _ => (EmotionEditorStrings.CardReady, ConvaiEditorTheme.AccentBright)
            };
        }

        // ------------------------------------------------------------------ mode switcher

        /// <summary>Tab labels in <see cref="EmotionEditorMode" /> order, so the mode bar's index is the mode.</summary>
        private static readonly GUIContent[] ModeTabs =
        {
            EmotionEditorStrings.ModeSetup,
            EmotionEditorStrings.ModeFeel,
            EmotionEditorStrings.ModeExpressions,
            EmotionEditorStrings.ModeLive
        };

        private void DrawModeSwitcher()
        {
            int clicked = ConvaiEditorTheme.ModeBar(ModeTabs, (int)_mode);
            if (clicked >= 0)
                SetMode((EmotionEditorMode)clicked);
        }

        // ------------------------------------------------------------------ left pane

        private void DrawLeftPane(ConvaiEmotionController[] controllers)
        {
            Rect paneRect = EditorGUILayout.BeginVertical(
                GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(paneRect, ConvaiEditorTheme.PaneBg);

            ConvaiEditorControls.GroupCaption(EmotionEditorStrings.LeftPaneTitle);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

            if (controllers.Length == 0)
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        ConvaiEditorControls.GroupCaption(EmotionEditorStrings.NoControllersTitle);
                        GUILayout.Label(EmotionEditorStrings.NoControllersBody,
                            ConvaiEditorTheme.CaptionWrapped);
                    }
                    GUILayout.Space(10f);
                }
            }
            else
            {
                for (int i = 0; i < controllers.Length; i++) DrawControllerCard(controllers[i]);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawControllerCard(ConvaiEmotionController controller)
        {
            bool selected = controller == _controller;
            (string statusText, Color dotColor) = CardStatus(controller, selected);

            // Grey cards never grow a "Set Up" affordance — setup belongs on the component
            // inspector, where the checklist that explains it lives.
            string tooltip = statusText == EmotionEditorStrings.CardNotSetUp
                ? EmotionEditorStrings.GreyCardHint
                : null;

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

        /// <summary>
        ///     Status for a card. The selected character reuses the already-computed findings; the
        ///     others are evaluated on the spot, which is cheap for a handful of cards.
        /// </summary>
        private (string text, Color dot) CardStatus(ConvaiEmotionController controller, bool selected)
        {
            EmotionPreflight preflight = selected ? _preflight : EmotionSetupService.Inspect(controller);
            if (!preflight.IsConfigured)
                return (EmotionEditorStrings.CardNotSetUp, ConvaiEditorTheme.TextMuted);

            EmotionSeverity worst;
            if (selected)
            {
                worst = EmotionTroubleshooter.WorstSeverity(_findings);
            }
            else
            {
                var findings = new List<EmotionFinding>(4);
                EmotionTroubleshooter.Evaluate(controller, in preflight, findings);
                worst = EmotionTroubleshooter.WorstSeverity(findings);
            }

            return worst >= EmotionSeverity.Warning
                ? (EmotionEditorStrings.CardNeedsAttention, ConvaiEditorTheme.Warning)
                : (EmotionEditorStrings.CardReady, ConvaiEditorTheme.AccentBright);
        }

        private static void DrawVerticalDivider()
        {
            Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(divider, ConvaiEditorTheme.Divider);
        }
    }
}
