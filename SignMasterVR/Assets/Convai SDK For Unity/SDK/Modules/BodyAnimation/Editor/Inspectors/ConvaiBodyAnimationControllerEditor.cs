using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Data;
using Convai.Modules.BodyAnimation.Editor;
using UnityEditor;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.BodyAnimation.Editor.Inspectors
{
    /// <summary>
    ///     The Body Animation component's inspector, and the module's primary product surface.
    ///     It has three states:
    ///     <list type="bullet">
    ///         <item><b>Setup</b> — freshly added. A preflight checklist and one button.</item>
    ///         <item><b>Needs Attention</b> — configured, but something regressed. Findings with fixes.</item>
    ///         <item><b>Ready</b> — the personality controls, a content summary, and live state.</item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately narrow: this carries the common path only — is the character ready, and how
    ///         does it feel. Content authoring (pools, the 26 locomotion slots, pointing directions, clip
    ///         preview) and the full ~100-field tuning surface live in the Body Animation editor window,
    ///         reached from the footer link. Cramming those in here is what makes component inspectors
    ///         unusable.
    ///     </para>
    ///     <para>
    ///         Drawn entirely with the shared Convai editor design system, so it reads as the same
    ///         product as the Actions inspectors and adapts to the Light editor skin — the flat renderer
    ///         this replaced hardcoded dark greys and drew dark blocks on a light background.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(ConvaiBodyAnimationController))]
    internal sealed class ConvaiBodyAnimationControllerEditor : ConvaiInspectorEditor
    {
        private const string SectionPersonality = "Personality";
        private const string SectionContent = "Content";
        private const string SectionLive = "LiveAnimation";
        private const string SectionAdvanced = "Advanced";
        private const int TraceTailLength = 4;

        private static readonly GUIContent PersonalityTitle = new("Personality");
        private static readonly GUIContent ContentTitle = new("Content");
        private static readonly GUIContent LiveTitle = new("Live");
        private static readonly GUIContent AdvancedTitle = new("Advanced");
        private static readonly GUIContent BeforeSetupTitle = new("Before setup");

        private static readonly GUIContent TileIdles = new("IDLE", "Idle variants this character can rest in.");
        private static readonly GUIContent TileTalks = new("TALK", "Gesture clips played while the character speaks.");
        private static readonly GUIContent TileActions = new("ACTIONS", "Backend-triggered animations this set provides.");

        private static readonly GUIContent SetUpButton = new(
            "Set Up This Character",
            "Assigns default content and optional movement so this character animates out of the box.");

        private static readonly GUIContent BlockedButton = new(
            "Resolve the blocked item above first",
            "Setup can assign content and add movement, but it cannot author a rig.");

        private static readonly GUIContent IncludeMovementToggle = new(
            "Include movement (walking, turns, stops)",
            "Adds NavMesh movement so the character can walk to places. Leave off for a stationary " +
            "character — it will still idle, talk, gesture, and point.");

        private static readonly GUIContent CreateConfigButton = new(
            "Give This Character Its Own Settings",
            "Creates a Body Animation Config in your project, on the SDK's defaults, and points this " +
            "character at it so the controls below become available.");

        /// <summary>The profile slot, drawn as the first row of the Content section.</summary>
        private static readonly GUIContent ProfileField = new(
            "Profile",
            "Routes an Animation Set + Config pair to this character. When assigned (or delivered " +
            "by a Character Embodiment Preset) it overrides the two fields below.");

        private static readonly GUIContent AdvancedSettingsButton = new(
            "Advanced settings & content  →",
            "Opens the Body Animation editor window: clip pools, locomotion slots, pointing directions " +
            "and the full tuning surface.");

        private SerializedProperty _profile;
        private SerializedProperty _animationSet;
        private SerializedProperty _config;
        private SerializedProperty _animatorOverride;
        private SerializedProperty _autoCreateConversationFlow;
        private SerializedProperty _locomotionProviderOverride;

        private bool _includeMovement = true;

        private readonly BodyAnimationSnapshot _snapshot = new();
        private readonly List<BodyAnimationTroubleshooterFinding> _findings = new();
        private readonly List<string> _issuesScratch = new();

        // Preflight and findings are evaluated once per inspector pass in OnBeforeInspectorGUI, never
        // per section draw — the status chip and three sections all read the same snapshot.
        private BodyAnimationPreflight _preflight;
        private bool _healthy;

        protected override string Title => "Body Animation";

        protected override string Subtitle => "Idle, talk, locomotion & gestures";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private ConvaiEditorChip CurrentChip
        {
            get
            {
                if (!_preflight.IsConfigured)
                    return _preflight.HasBlocker ? ConvaiEditorChips.ActionNeeded : ConvaiEditorChips.NotSetUp;
                if (EditorApplication.isPlaying)
                    return ((ConvaiBodyAnimationController)target).IsRuntimeBuilt
                        ? ConvaiEditorChips.Live
                        : ConvaiEditorChips.Inactive;
                return _healthy ? ConvaiEditorChips.Ready : ConvaiEditorChips.NeedsAttention;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _profile = serializedObject.FindProperty("profile");
            _animationSet = serializedObject.FindProperty("_animationSet");
            _config = serializedObject.FindProperty("_config");
            _animatorOverride = serializedObject.FindProperty("_animatorOverride");
            _autoCreateConversationFlow = serializedObject.FindProperty("_autoCreateConversationFlow");
            _locomotionProviderOverride = serializedObject.FindProperty("_locomotionProviderOverride");
        }

        /// <summary>Keeps the Live section updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void OnBeforeInspectorGUI()
        {
            var controller = (ConvaiBodyAnimationController)target;
            _preflight = BodyAnimationSetupService.Inspect(controller);
            if (!_preflight.IsConfigured)
            {
                _healthy = false;
                return;
            }

            RefreshFindings(controller);
            _healthy = BodyAnimationTroubleshooter.WorstSeverity(_findings) < BodyAnimationTroubleshooterSeverity.Warning;
        }

        /// <summary>
        ///     Findings sit above the section stack, not inside it. The house order for sections is
        ///     configuration, then live telemetry, then advanced — and a problem is none of those: it is
        ///     something to read before touching any of them, which is exactly what this hook is for.
        /// </summary>
        protected override void DrawHeaderExtras()
        {
            if (!_preflight.IsConfigured || _healthy) return;

            DrawFindings((ConvaiBodyAnimationController)target);
        }

        protected override void DrawBody()
        {
            var controller = (ConvaiBodyAnimationController)target;

            if (!_preflight.IsConfigured)
            {
                DrawSetupState(controller);
                return;
            }

            DrawPersonalitySection(controller);
            DrawContentSection(controller);
            DrawLiveStateSection(controller);
            DrawAdvancedSection();
            DrawFooterLink(controller);
        }

        // ------------------------------------------------------------------ setup state

        /// <summary>
        ///     What a user sees the moment they add the component. Not a wall of empty object
        ///     fields — a checklist that states what is and is not ready before they press anything,
        ///     and one button.
        /// </summary>
        private void DrawSetupState(ConvaiBodyAnimationController controller)
        {
            bool blocked = _preflight.HasBlocker;

            InfoBox(
                "What this does",
                "Animates the whole character from code — idle variants, talk gestures while speaking, " +
                "walking and jogging with animated turns and stops, backend-triggered actions, and " +
                "pointing. No Animator Controller asset is needed.");

            using (Frame.Card())
            {
                Frame.SectionHeader(Glyphs.Validation, BeforeSetupTitle);
                for (int i = 0; i < _preflight.Checks.Count; i++)
                    DrawCheckRow(_preflight.Checks[i]);

                EditorGUILayout.Space(6f);
                _includeMovement = EditorGUILayout.ToggleLeft(IncludeMovementToggle, _includeMovement);
                EditorGUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(blocked))
                {
                    if (PrimaryButton(blocked ? BlockedButton : SetUpButton))
                        RunSetup(controller);
                }
            }

            if (blocked)
            {
                WarningBox(
                    "One thing needs you first",
                    "Setup can assign content and add movement, but it cannot author a rig. Resolve the " +
                    "item marked above, then run setup.");
                return;
            }

            // Without this the content row is the only place the next step appears, and a row detail
            // is a statement of fact, not an instruction. A project with no clips is the normal
            // starting point, so it gets a route out rather than a red mark.
            if (_preflight.NeedsContent)
            {
                WarningBox(
                    "No animation clips in this project yet",
                    "Setup will finish everything else on this character, but it needs clips before it " +
                    "can move. Import the Convai samples from the Package Manager to get the ones the " +
                    "SDK provides, or build a set from a folder of your own clips.",
                    "Create Animation Set…",
                    ConvaiBodyAnimationSetBuilderWindow.Open);
            }
        }

        /// <summary>
        ///     One preflight row as a status dot plus label and detail — the same row vocabulary the
        ///     Actions inspector uses for its action previews.
        /// </summary>
        private static void DrawCheckRow(BodyAnimationCheck check)
        {
            Color color = check.State switch
            {
                BodyAnimationCheckState.Ok => Theme.StatusReady,
                BodyAnimationCheckState.Fixable => Theme.StatusInfo,
                BodyAnimationCheckState.Blocked => Theme.StatusError,
                BodyAnimationCheckState.NeedsContent => Theme.StatusWarn,
                _ => Theme.StatusIdle
            };

            Rect slot = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            Theme.StatusDot(new Vector2(slot.x + 9f, slot.y + (slot.height * 0.5f)), color);

            var labelRect = new Rect(slot.x + 20f, slot.y, 118f, slot.height);
            var detailRect = new Rect(labelRect.xMax + 4f, slot.y, Mathf.Max(40f, slot.width - 146f), slot.height);
            GUI.Label(labelRect, check.Label, ConvaiEditorStyles.MicroLabel);
            GUI.Label(detailRect, check.Detail, ConvaiEditorStyles.CaptionWrapped);
        }

        /// <summary>
        ///     Runs setup after the current IMGUI pass, never inside it.
        /// </summary>
        /// <remarks>
        ///     <see cref="EditorUtility.DisplayDialog(string, string, string)" /> pumps the event loop
        ///     from where it is called. Called from a button inside a layout group, it therefore
        ///     destroys the layout state that the enclosing group is about to close, which left the
        ///     inspector permanently throwing "Getting control 0's position in a group with only 0
        ///     controls" on every later repaint — the group stack never recovered. Deferring through
        ///     <see cref="EditorApplication.delayCall" /> runs the same work with no GUI pass on the
        ///     stack. Setup also mutates the scene and reimports assets, which is unsafe mid-pass for
        ///     the same reason.
        /// </remarks>
        private void RunSetup(ConvaiBodyAnimationController controller)
        {
            bool includeMovement = _includeMovement;
            EditorApplication.delayCall += () =>
            {
                if (controller == null)
                    return;

                BodyAnimationSetupResult result = BodyAnimationSetupService.Apply(
                    controller, new BodyAnimationSetupOptions { IncludeMovement = includeMovement });

                var message = new System.Text.StringBuilder(result.Summary);
                for (int i = 0; i < result.Notes.Count; i++)
                    message.Append("\n\n• ").Append(result.Notes[i]);

                EditorUtility.DisplayDialog("Body Animation", message.ToString(), "OK");
            };
        }

        // ------------------------------------------------------------------ findings

        private void RefreshFindings(ConvaiBodyAnimationController controller)
        {
            BodyAnimationTroubleshooterInput input = BodyAnimationTroubleshooter.GatherFrom(
                controller, _animationSet, _config, _profile, _animatorOverride,
                _locomotionProviderOverride, _issuesScratch, out _, out _);
            BodyAnimationTroubleshooter.Evaluate(in input, _findings);
        }

        /// <summary>
        ///     Renders findings worth acting on. A finding that carries a mechanical repair gets its
        ///     Fix button inline, so the user never leaves the inspector to resolve it.
        /// </summary>
        private void DrawFindings(ConvaiBodyAnimationController controller)
        {
            ConvaiBodyAnimationSet set = BodyAnimationSetupService.ResolveAssignedSet(controller);

            for (int i = 0; i < _findings.Count; i++)
            {
                BodyAnimationTroubleshooterFinding finding = _findings[i];
                if (finding.Severity < BodyAnimationTroubleshooterSeverity.Warning) continue;

                BodyAnimationFixId fixId = finding.Fix;

                string characterFix = BodyAnimationSetupService.DescribeFix(fixId);
                if (characterFix != null)
                {
                    WarningBox(finding.Title, finding.Message, characterFix,
                        () => BodyAnimationSetupService.ApplyFix(controller, fixId));
                    continue;
                }

                string setFix = BodyAnimationFixes.DescribeSetFix(fixId);
                if (setFix != null && set != null)
                {
                    WarningBox(finding.Title, finding.Message, setFix,
                        () => BodyAnimationFixes.ApplyToSet(set, fixId));
                    continue;
                }

                string configFix = BodyAnimationFixes.DescribeConfigFix(fixId);
                ConvaiBodyAnimationConfig configAsset = configFix != null
                    ? BodyAnimationSetupService.ResolveAssignedConfig(controller)
                    : null;
                if (configFix != null && configAsset != null)
                {
                    WarningBox(finding.Title, finding.Message, configFix,
                        () => BodyAnimationFixes.ApplyToConfig(configAsset, fixId, controller));
                    continue;
                }

                if (finding.Severity == BodyAnimationTroubleshooterSeverity.Error)
                    ErrorBox(finding.Title, finding.Message);
                else
                    WarningBox(finding.Title, finding.Message);
            }
        }

        // ------------------------------------------------------------------ personality

        /// <summary>
        ///     The three controls that make one animation set read as different people, plus the
        ///     archetype presets. Everything here writes to the config asset, which may be shared —
        ///     see the sharing notice.
        /// </summary>
        private void DrawPersonalitySection(ConvaiBodyAnimationController controller)
        {
            // Resolved once and reused by the header and the body: ResolveConfigAsset builds a
            // SerializedObject, and this runs on every repaint.
            ConvaiBodyAnimationConfig config = ResolveConfigAsset(controller);

            // The summary names the asset this section's controls write — the config — which is
            // not the same asset as the Profile that routes it. Naming the Profile here would put
            // the wrong file's name over the sliders that change a different one.
            if (!DrawSection(
                    SectionPersonality, PersonalityTitle.text, Glyphs.Profile,
                    summary: ConvaiEditorProfileField.Summarize(
                        config, BodyAnimationPersonality.IsCustomized(config)))) return;

            DrawSectionBody(() =>
            {
                if (config == null)
                {
                    // Setup deliberately leaves the config empty so no character is born owning an
                    // asset it never changes. That is right, but it left nothing here to press:
                    // the controls need an asset, and nothing offered to make one. This button is
                    // that offer — a person clicking is exactly when creating an asset is correct.
                    InfoBox(
                        "Using SDK Defaults",
                        "This character has no settings of its own, so it runs on the SDK's built-in " +
                        "defaults — which work. Give it its own settings to make it more expressive, " +
                        "calmer, or busier when nobody is talking to it.");
                    if (GhostButton(CreateConfigButton))
                        CreateConfigFor(controller);
                    return;
                }

                BodyAnimationPersonality.DrawPersonalitySection(
                    config, BodyAnimationSetupService.ResolveAssignedSet(controller), controller);
            });
        }

        /// <summary>
        ///     Creates a Body Animation Config for this character and points it at the copy, through
        ///     the same copy-on-write path and the same receipt every other Convai module uses.
        /// </summary>
        /// <remarks>
        ///     Deferred for the reason <see cref="RunSetup" /> documents: creating an asset
        ///     reimports, which is unsafe with a GUI pass on the stack.
        /// </remarks>
        private static void CreateConfigFor(ConvaiBodyAnimationController controller)
        {
            EditorApplication.delayCall += () =>
            {
                if (controller == null) return;

                ConvaiCopyOnWriteResult created = ConvaiCopyOnWrite.CreateAndAssign(
                    ConvaiBodyAnimationConfig.CreateDefault(), controller,
                    "BodyAnimation", "_BodyAnimationSettings", "_config");

                if (!created.Succeeded)
                {
                    EditorUtility.DisplayDialog("Body Animation", created.FailureReason, "OK");
                    return;
                }

                ConvaiCopyReceipts.Record(controller, created.AssetPath, created.Target);
            };
        }

        // ------------------------------------------------------------------ content

        private void DrawContentSection(ConvaiBodyAnimationController controller)
        {
            ConvaiBodyAnimationSet set = BodyAnimationSetupService.ResolveAssignedSet(controller);

            // The set's name rides in the header summary — a collapsed section still says which
            // content this character is on. It used to be concatenated into the title instead,
            // which is the framework's summary slot done by hand.
            if (!DrawSection(
                SectionContent, ContentTitle.text, Glyphs.Content,
                summary: set != null ? set.DisplayName : ConvaiEditorProfileField.BuiltInDefaultsSummary))
                return;

            DrawSectionBody(() =>
            {
                // The three routing fields come first, above the clip counts they explain. They
                // used to sit under the tiles, which put the fact that identifies this character
                // below a readout of what that fact produced.
                ConvaiEditorProfileField.Draw(_profile, ProfileField);
                EditorGUILayout.PropertyField(_animationSet, new GUIContent(
                    "Animation Set",
                    "The clips this character uses for idles, talking, and actions. Ignored when " +
                    "the Profile above is assigned."));
                EditorGUILayout.PropertyField(_config, new GUIContent(
                    "Behavior Config",
                    "Tuning for how the character moves and reacts — speeds, transitions, and " +
                    "diagnostics. Ignored when the Profile above is assigned."));

                if (set == null)
                    return;

                EditorGUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    StatTile(TileIdles, set.Idles.Count.ToString());
                    GUILayout.Space(6f);
                    StatTile(TileTalks, set.Talks.Count.ToString());
                    GUILayout.Space(6f);
                    StatTile(TileActions, set.Actions.Count.ToString());
                }

                EditorGUILayout.Space(6f);
                GUILayout.Label($"{set.Listens.Count} listen · {set.Thinks.Count} think",
                    ConvaiEditorStyles.MicroLabel);
                GUILayout.Label(BodyAnimationFixes.DescribeLocomotionCoverage(set),
                    ConvaiEditorStyles.CaptionWrapped);
            });
        }

        // ------------------------------------------------------------------ live

        private void DrawLiveStateSection(ConvaiBodyAnimationController controller)
        {
            if (!DrawSection(SectionLive, LiveTitle.text, Glyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying || !controller.IsRuntimeBuilt)
                {
                    OfflinePlaceholder();
                    return;
                }

                controller.CaptureSnapshot(_snapshot);

                using (new EditorGUILayout.HorizontalScope())
                {
                    LiveCell("Dialogue", _snapshot.DialogueState.ToString(), Theme.AccentBright);
                    LiveCell("Movement",
                        string.IsNullOrEmpty(_snapshot.LocomotionState) ? "—" : _snapshot.LocomotionState,
                        Theme.StatusInfo);

                    float slide = Mathf.Abs(_snapshot.AgentSpeed - _snapshot.AnimationSpeed);
                    LiveCell("Foot Slide", $"{slide:0.00} m/s",
                        slide > 0.35f ? Theme.StatusWarn : Theme.TextPrimary);
                }

                if (_snapshot.RecentTrace.Count == 0)
                    return;

                EditorGUILayout.Space(4f);
                using (Frame.Panel(null, 0f))
                {
                    int first = Mathf.Max(0, _snapshot.RecentTrace.Count - TraceTailLength);
                    for (int i = first; i < _snapshot.RecentTrace.Count; i++)
                    {
                        AnimTraceEntry entry = _snapshot.RecentTrace[i];
                        GUILayout.Label($"[{entry.Time:0.0}s] {entry.Message}", ConvaiEditorStyles.MicroLabel);
                    }
                }
            });
        }

        // ------------------------------------------------------------------ advanced

        private void DrawAdvancedSection()
        {
            if (!DrawSection(SectionAdvanced, AdvancedTitle.text, Glyphs.Contract, defaultExpanded: false)) return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_animatorOverride, new GUIContent(
                    "Animator Override",
                    "Explicit Animator target. Empty = the first Animator found in children."));
                EditorGUILayout.PropertyField(_autoCreateConversationFlow, new GUIContent(
                    "Auto Create Conversation Flow",
                    "Automatically adds the component that detects when the character is speaking, " +
                    "so talking animations play with no extra setup."));
                EditorGUILayout.PropertyField(_locomotionProviderOverride, new GUIContent(
                    "Custom Movement Provider",
                    "Use your own movement script instead of Convai's built-in NavMesh movement. " +
                    "Assign a script that implements IConvaiLocomotionSource to take over."));
            });
        }

        private static void DrawFooterLink(ConvaiBodyAnimationController controller)
        {
            // The only documented route to the deeper surface. Content authoring and the full
            // tuning surface live there, never here (see the class remarks).
            if (GhostButton(AdvancedSettingsButton))
                ConvaiBodyAnimationEditorWindow.ShowFor(controller);
        }

        private static ConvaiBodyAnimationConfig ResolveConfigAsset(ConvaiBodyAnimationController controller)
        {
            var serialized = new SerializedObject(controller);
            var profile = serialized.FindProperty("profile")?.objectReferenceValue as ConvaiBodyAnimationProfile;
            if (profile != null && profile.Config != null) return profile.Config;
            return serialized.FindProperty("_config")?.objectReferenceValue as ConvaiBodyAnimationConfig;
        }
    }
}
