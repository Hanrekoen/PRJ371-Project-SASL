using Convai.Domain.Embodiment.Semantics;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Emotion;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using UnityEditor;
using UnityEngine;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     The Emotion component's inspector, and the module's primary product surface.
    ///     It has three states:
    ///     <list type="bullet">
    ///         <item><b>Not Set Up</b> — freshly added. A preflight checklist and one button.</item>
    ///         <item><b>Needs Attention</b> — configured, but something is wrong. Findings with fixes.</item>
    ///         <item><b>Ready</b> — the personality controls, and live state.</item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     Deliberately narrow: this carries the common path only — is the character ready, and what
    ///     kind of person is it. The full settings surface, the expression content and the deeper
    ///     live monitor live in the Emotion editor window, reached from the footer link. Cramming
    ///     those in here is what makes component inspectors unusable.
    /// </remarks>
    [CustomEditor(typeof(ConvaiEmotionController))]
    internal sealed class ConvaiEmotionControllerEditor : ConvaiInspectorEditor
    {
        private const string SectionPersonality = "Personality";
        private const string SectionLive = "LiveEmotion";
        private const string SectionAdvanced = "Advanced";

        private static readonly GUIContent ChipOff = new("Off", "Emotion detection is switched off for this character.");

        private static readonly GUIContent SetUpButton = new(
            "Set Up Emotions", "Creates and assigns an emotion personality for this character.");

        private static readonly GUIContent BlockedButton = new(
            "Resolve the blocked item above first",
            "Setup cannot give the character a face or move it onto a Convai character.");

        private static readonly GUIContent TryItButton = new(
            "Try It", "Holds this expression on the live character so you can see it.");

        private static readonly GUIContent StopButton = new(
            "Stop", "Releases the held expression and returns to normal reactions.");

        /// <summary>
        ///     The personality slot, drawn as the first row of the Personality section. It used to
        ///     live under <c>Advanced</c>, a section that starts collapsed — so the one fact that
        ///     identifies this component was the one thing a user had to go looking for.
        /// </summary>
        private static readonly GUIContent PersonalityField = new(
            "Personality",
            "The asset holding this character's emotional temperament. Can be shared with other " +
            "characters.");

        private static readonly GUIContent AdvancedSettingsButton = new(
            "Advanced settings & expressions  →",
            "Opens the Emotion editor window: the full settings surface, expression content and the " +
            "deeper live monitor.");

        private const string InitialMoodUseProfileOption = "(Use the personality's resting mood)";
        private const string InitialMoodNoneOption = "Neutral — no resting mood";


        private SerializedProperty _detectionMode;
        private SerializedProperty _profile;
        private SerializedProperty _lockEmotion;
        private SerializedProperty _lockedEmotionLabel;
        private SerializedProperty _lockedIntensity;
        private SerializedProperty _initialMoodLabel;
        private SerializedProperty _initialMoodIntensity;
        private SerializedProperty _actionSuccessMoodLabel;
        private SerializedProperty _actionFailureMoodLabel;
        private SerializedProperty _actionMoodIntensity;
        private SerializedProperty _actionMoodHoldSeconds;
        private SerializedProperty _actionMoodTransitionSeconds;

        private CharacterDemeanor _setupCharacterType = CharacterDemeanor.Warm;

        // Transient authoring aid, deliberately not serialized.
        private string _previewEmotionLabel;
        private float _previewIntensity = 1f;

        protected override void OnEnable()
        {
            base.OnEnable();
            _detectionMode = serializedObject.FindProperty("detectionMode");
            _profile = serializedObject.FindProperty("profile");
            _lockEmotion = serializedObject.FindProperty("lockEmotion");
            _lockedEmotionLabel = serializedObject.FindProperty("lockedEmotionLabel");
            _lockedIntensity = serializedObject.FindProperty("lockedIntensity");
            _initialMoodLabel = serializedObject.FindProperty("initialMoodLabel");
            _initialMoodIntensity = serializedObject.FindProperty("initialMoodIntensity");
            _actionSuccessMoodLabel = serializedObject.FindProperty("actionSuccessMoodLabel");
            _actionFailureMoodLabel = serializedObject.FindProperty("actionFailureMoodLabel");
            _actionMoodIntensity = serializedObject.FindProperty("actionMoodIntensity");
            _actionMoodHoldSeconds = serializedObject.FindProperty("actionMoodHoldSeconds");
            _actionMoodTransitionSeconds = serializedObject.FindProperty("actionMoodTransitionSeconds");
        }

        // Preflight and findings are evaluated once per pass, so the status chip and every section
        // read the same snapshot. The findings list is reused rather than reallocated per repaint.
        private EmotionPreflight _preflight;
        private readonly List<EmotionFinding> _findings = new(4);
        private bool _healthy;

        /// <summary>
        ///     Whether this character has emotion detection switched off. Reads the serialized
        ///     <em>value</em>, never the enum's declaration index — see <see cref="EmotionDetectionModes" />.
        /// </summary>
        private bool DetectionIsOff =>
            (EmotionDetectionMode)_detectionMode.intValue == EmotionDetectionMode.Off;

        protected override string Title => "Emotions";

        protected override string Subtitle => "Facial expression, mood and reactions";

        protected override GUIContent StatusChip
        {
            get
            {
                if (!_preflight.IsConfigured)
                    return (_preflight.HasBlocker ? ConvaiEditorChips.ActionNeeded : ConvaiEditorChips.NotSetUp).Content;
                if (DetectionIsOff) return ChipOff;
                if (EditorApplication.isPlaying) return ConvaiEditorChips.Live.Content;
                return (_healthy ? ConvaiEditorChips.Ready : ConvaiEditorChips.NeedsAttention).Content;
            }
        }

        protected override Color StatusChipTint
        {
            get
            {
                if (!_preflight.IsConfigured)
                    return (_preflight.HasBlocker ? ConvaiEditorChips.ActionNeeded : ConvaiEditorChips.NotSetUp).Tint;
                if (DetectionIsOff) return Theme.StatusIdle;
                if (EditorApplication.isPlaying) return ConvaiEditorChips.Live.Tint;
                return (_healthy ? ConvaiEditorChips.Ready : ConvaiEditorChips.NeedsAttention).Tint;
            }
        }

        /// <summary>Keeps the live expression readout updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void OnBeforeInspectorGUI()
        {
            var controller = (ConvaiEmotionController)target;
            _preflight = EmotionSetupService.Inspect(controller);
            if (!_preflight.IsConfigured)
            {
                _healthy = false;
                return;
            }

            _findings.Clear();
            EmotionTroubleshooter.Evaluate(controller, in _preflight, _findings);
            _healthy = EmotionTroubleshooter.WorstSeverity(_findings) < EmotionSeverity.Warning;
        }

        /// <summary>
        ///     Findings sit above the section stack, not inside it. The house order for sections is
        ///     configuration, then live telemetry, then advanced — and a problem is none of those: it is
        ///     something to read before touching any of them, which is exactly what this hook is for.
        /// </summary>
        protected override void DrawHeaderExtras()
        {
            if (!_preflight.IsConfigured || _healthy) return;

            DrawFindings((ConvaiEmotionController)target, _findings);
        }

        protected override void DrawBody()
        {
            var controller = (ConvaiEmotionController)target;

            if (!_preflight.IsConfigured)
            {
                DrawSetupState(controller, in _preflight);
                return;
            }

            DrawPersonalitySection(controller);
            DrawLiveEmotionSection(controller);
            DrawAdvancedSection(controller, in _preflight);
            DrawFooterLink(controller);
        }

        // ------------------------------------------------------------------ setup state

        /// <summary>
        ///     What a user sees the moment they add the component. Not a wall of empty object
        ///     fields — a checklist that states what is and is not ready before they press
        ///     anything, and one button.
        /// </summary>
        private void DrawSetupState(ConvaiEmotionController controller, in EmotionPreflight preflight)
        {
            bool blocked = preflight.HasBlocker;

            InfoBox(
                "What this does",
                "Gives the character a face that reacts to the conversation — expressions as it " +
                "speaks and listens, a resting mood it settles back to, and small movements so it " +
                "never looks frozen.");

            EditorGUILayout.LabelField("Before setup", ConvaiEditorStyles.SectionTitle);
            for (int i = 0; i < preflight.Checks.Count; i++)
                DrawCheckRow(preflight.Checks[i]);

            EditorGUILayout.Space(6f);

            _setupCharacterType = (CharacterDemeanor)EditorGUILayout.EnumPopup(
                new GUIContent("Character type",
                    "A starting temperament. You can change it, or tune anything, afterwards."),
                _setupCharacterType);

            using (new EditorGUI.DisabledScope(blocked))
            {
                if (PrimaryButton(blocked ? BlockedButton : SetUpButton))
                    RunSetup(controller);
            }

            if (blocked)
            {
                WarningBox(
                    "One thing needs you first",
                    "Setup can create and assign a personality, but it cannot give the character a " +
                    "face or move it onto a Convai character. Resolve the item marked above, then " +
                    "run setup.");
            }
        }

        /// <summary>One preflight row as a status dot plus label and detail.</summary>
        private static void DrawCheckRow(EmotionCheck check)
        {
            Color color = check.State switch
            {
                EmotionCheckState.Ok => Theme.StatusReady,
                EmotionCheckState.Fixable => Theme.StatusInfo,
                EmotionCheckState.Blocked => Theme.StatusError,
                _ => Theme.StatusIdle
            };

            Rect slot = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            Theme.StatusDot(new Vector2(slot.x + 9f, slot.y + (slot.height * 0.5f)), color);

            var labelRect = new Rect(slot.x + 20f, slot.y, 90f, slot.height);
            var detailRect = new Rect(labelRect.xMax + 4f, slot.y, Mathf.Max(40f, slot.width - 118f), slot.height);
            GUI.Label(labelRect, check.Label, ConvaiEditorStyles.MicroLabel);
            GUI.Label(detailRect, check.Detail, ConvaiEditorStyles.CaptionWrapped);
        }

        /// <summary>
        ///     Runs setup after the current IMGUI pass, never inside it. See the same method on
        ///     <see cref="ConvaiBodyAnimationControllerEditor" /> for why a modal dialog raised from
        ///     inside a layout group corrupts the group stack for every later repaint.
        /// </summary>
        private void RunSetup(ConvaiEmotionController controller)
        {
            CharacterDemeanor characterType = _setupCharacterType;
            EditorApplication.delayCall += () =>
            {
                if (controller == null)
                    return;

                EmotionSetupResult result = EmotionSetupService.Apply(
                    controller, new EmotionSetupOptions { CharacterType = characterType });

                var message = new System.Text.StringBuilder(result.Summary);
                for (int i = 0; i < result.Notes.Count; i++)
                    message.Append("\n\n• ").Append(result.Notes[i]);

                EditorUtility.DisplayDialog("Emotions", message.ToString(), "OK");
            };
        }

        // ------------------------------------------------------------------ ready state

        private static void DrawFindings(ConvaiEmotionController controller, List<EmotionFinding> findings)
        {
            for (int i = 0; i < findings.Count; i++)
            {
                EmotionFinding finding = findings[i];
                if (finding.Severity < EmotionSeverity.Warning) continue;

                string fixLabel = EmotionTroubleshooter.DescribeFix(finding.Fix);
                if (fixLabel != null)
                {
                    EmotionFixId fixId = finding.Fix;
                    WarningBox(finding.Title, finding.Message, fixLabel,
                        () => EmotionTroubleshooter.ApplyFix(controller, fixId));
                    continue;
                }

                if (finding.Severity == EmotionSeverity.Error)
                    ErrorBox(finding.Title, finding.Message);
                else
                    WarningBox(finding.Title, finding.Message);
            }
        }

        // ------------------------------------------------------------------ personality

        /// <summary>
        ///     The controls that make one emotion system read as different people. Everything here
        ///     writes to the profile asset, which may be shared — see the sharing notice.
        /// </summary>
        private void DrawPersonalitySection(ConvaiEmotionController controller)
        {
            ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);

            if (!DrawSection(
                    SectionPersonality, "Personality", ConvaiEditorGlyphs.Profile,
                    summary: ConvaiEditorProfileField.Summarize(
                        profile, EmotionPersonality.IsCustomized(profile)))) return;

            DrawSectionBody(() =>
            {
                ConvaiEditorProfileField.Draw(_profile, PersonalityField);

                if (profile == null)
                {
                    InfoBox(
                        "Using SDK Defaults",
                        "This character has no personality asset, so the built-in defaults are used " +
                        "and there is nothing to tune here yet. Assign one above, or remove and " +
                        "re-add this component to run setup again.");
                    return;
                }

                EmotionPersonality.DrawSharingNotice(profile, controller);
                EmotionPersonality.DrawArchetypeRow(profile, controller);
                EditorGUILayout.Space(4f);
                EmotionPersonality.DrawControls(
                    profile, EmotionPersonality.HasOtherCharacters(controller), controller);
                ConvaiCopyReceipts.Draw(controller);
            });
        }

        // ------------------------------------------------------------------ live

        private void DrawLiveEmotionSection(ConvaiEmotionController controller)
        {
            if (!DrawSection(SectionLive, "Live", ConvaiEditorGlyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying)
                {
                    OfflinePlaceholder();
                    DrawPreviewRow(controller);
                    return;
                }

                EmotionReading current = controller.Current;
                using (new EditorGUILayout.HorizontalScope())
                {
                    LiveCell("Feeling", current.DominantLabel,
                        current.IsNeutral ? Theme.StatusIdle : Theme.AccentBright, 110f, !current.IsNeutral);
                    LiveCell("Strength", current.DominantScore.ToString("0.00"),
                        current.DominantScore > 0.1f ? Theme.AccentBright : Theme.StatusIdle, 90f);
                    LiveCell("Mood", current.MoodLabel,
                        current.MoodScore > 0.01f ? Theme.StatusInfo : Theme.StatusIdle, 110f);
                    LiveCell("Held For", $"{current.DominantHoldSeconds:0.0}s", Theme.TextPrimary, 80f);
                }

                DrawPreviewRow(controller);
            });
        }

        /// <summary>
        ///     A one-off preview row calling the controller's public
        ///     <see cref="ConvaiEmotionController.LockEmotion" />/
        ///     <see cref="ConvaiEmotionController.UnlockEmotion" /> on the live instance. Disabled
        ///     (not hidden) outside Play Mode, with a one-line hint.
        /// </summary>
        private void DrawPreviewRow(ConvaiEmotionController controller)
        {
            bool playing = EditorApplication.isPlaying;

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!playing))
            using (new EditorGUILayout.HorizontalScope())
            {
                ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);
                string[] labels = BuildEmotionLabels(profile, _previewEmotionLabel);
                int currentIndex = IndexOfLabel(labels, _previewEmotionLabel);
                if (currentIndex < 0) currentIndex = 0;

                int nextIndex = EditorGUILayout.Popup(
                    currentIndex, EmotionLabelCatalog.DisplayNames(labels), GUILayout.MinWidth(100));
                if (nextIndex >= 0 && nextIndex < labels.Length) _previewEmotionLabel = labels[nextIndex];

                _previewIntensity = EditorGUILayout.Slider(_previewIntensity, 0f, 1f, GUILayout.MinWidth(90));

                Rect tryRect = GUILayoutUtility.GetRect(70f, 20f, GUILayout.Width(70f));
                if (ConvaiEditorControls.GhostButton(tryRect, TryItButton))
                {
                    string label = string.IsNullOrWhiteSpace(_previewEmotionLabel)
                        ? "neutral"
                        : _previewEmotionLabel;
                    controller.LockEmotion(label, _previewIntensity);
                }

                using (new EditorGUI.DisabledScope(!_lockEmotion.boolValue))
                {
                    Rect stopRect = GUILayoutUtility.GetRect(60f, 20f, GUILayout.Width(60f));
                    if (ConvaiEditorControls.GhostButton(stopRect, StopButton))
                        controller.UnlockEmotion();
                }
            }

            if (!playing)
                OfflinePlaceholder("Enter Play Mode to try an expression.");
        }

        // ------------------------------------------------------------------ advanced

        private void DrawAdvancedSection(ConvaiEmotionController controller, in EmotionPreflight preflight)
        {
            if (!DrawSection(SectionAdvanced, "Advanced", ConvaiEditorGlyphs.Contract, defaultExpanded: false)) return;

            RigConventionDescription rig = Describe(in preflight);

            DrawSectionBody(() =>
            {
                DrawDetectionMode();

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Face rig", rig.Text);
                if (rig.Warn)
                    InfoBox("Unrecognised face rig",
                        "Convai could not match this character's blendshape names to a convention it " +
                        "knows, so expressions will be limited. Assign a Custom Rig Convention Map " +
                        "to map them yourself.");

                EditorGUILayout.Space(4f);
                DrawInitialMood(controller);

                EditorGUILayout.Space(4f);
                DrawHoldExpression(controller);

                EditorGUILayout.Space(4f);
                DrawMoodAfterActions(controller);
            });
        }

        private readonly struct RigConventionDescription
        {
            internal RigConventionDescription(string text, bool warn)
            {
                Text = text;
                Warn = warn;
            }

            internal string Text { get; }
            internal bool Warn { get; }
        }

        private static RigConventionDescription Describe(in EmotionPreflight preflight)
        {
            if (preflight.Convention == Domain.Embodiment.Semantics.RigConvention.Unknown)
                return new RigConventionDescription("Not recognised", true);

            return new RigConventionDescription(
                $"{EmotionSetupService.DescribeConvention(preflight.Convention)} " +
                $"({Mathf.RoundToInt(preflight.ConventionConfidence * 100f)}% match)",
                false);
        }

        /// <summary>
        ///     Detection as three plain choices, followed by what the selected one actually does.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The enum's own names — <c>Nrclex</c>, <c>Llm</c> — are the wire contract and stay
        ///         exactly as they are; neither means anything to the person choosing. The mapping
        ///         between what is shown and what is written lives in
        ///         <see cref="EmotionDetectionModes" />, never inferred from the enum's declaration
        ///         order: doing that shipped the two providers swapped.
        ///     </para>
        ///     <para>
        ///         Both live modes are a genuine choice rather than a good one and a lesser one, so
        ///         the description below the dropdown states what the selected mode is better at
        ///         <em>and</em> what it gives up, and it is always visible — this is a decision a
        ///         user should be able to revisit without leaving the Inspector.
        ///     </para>
        /// </remarks>
        private void DrawDetectionMode()
        {
            var currentMode = (EmotionDetectionMode)_detectionMode.intValue;
            int currentIndex = EmotionDetectionModes.IndexOf(currentMode);

            int nextIndex = EditorGUILayout.Popup(
                new GUIContent("Emotion detection",
                    "How the character's feelings are worked out from what is being said."),
                currentIndex, EmotionDetectionModes.Options);

            EmotionDetectionMode nextMode = EmotionDetectionModes.ValueAt(nextIndex);
            if (nextMode != currentMode) _detectionMode.intValue = (int)nextMode;

            if (nextMode == EmotionDetectionMode.Off)
            {
                InfoBox("Emotions Off", EmotionDetectionModes.DescriptionFor(nextMode));
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                GUILayout.Label(
                    EmotionDetectionModes.DescriptionFor(nextMode), ConvaiEditorStyles.CaptionWrapped);
            }
        }

        /// <summary>
        ///     Per-character resting-mood override, shown as a dropdown with the two meaningful
        ///     special cases spelled out, plus an always-visible line resolving which one actually
        ///     wins.
        /// </summary>
        private void DrawInitialMood(ConvaiEmotionController controller)
        {
            ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);
            string currentLabel = _initialMoodLabel.stringValue;
            string[] labels = BuildEmotionLabels(profile, currentLabel);

            var options = new List<string>(labels.Length + 2)
                { InitialMoodUseProfileOption, InitialMoodNoneOption };
            for (int i = 0; i < labels.Length; i++)
            {
                if (string.Equals(labels[i], "neutral", System.StringComparison.OrdinalIgnoreCase)) continue;
                options.Add(labels[i]);
            }

            bool forcedNeutral = !string.IsNullOrWhiteSpace(currentLabel) &&
                string.Equals(currentLabel, "neutral", System.StringComparison.OrdinalIgnoreCase);

            int currentIndex = 0;
            if (forcedNeutral) currentIndex = 1;
            else if (!string.IsNullOrWhiteSpace(currentLabel))
            {
                int found = IndexOfLabel(options, currentLabel);
                if (found > 0) currentIndex = found;
            }

            // The two leading entries are this dropdown's own wording, not emotion names, so only
            // the emotions themselves are capitalized for display.
            string[] shown = options.ToArray();
            for (int i = 2; i < shown.Length; i++) shown[i] = EmotionLabelCatalog.DisplayName(shown[i]);

            int nextIndex = EditorGUILayout.Popup(
                new GUIContent("This character rests at",
                    "Overrides the personality's resting mood for this character alone, so two " +
                    "characters can share one personality and still rest differently."),
                currentIndex, shown);

            if (nextIndex == 0) _initialMoodLabel.stringValue = string.Empty;
            else if (nextIndex == 1) _initialMoodLabel.stringValue = "neutral";
            else if (nextIndex > 1 && nextIndex < options.Count)
            {
                _initialMoodLabel.stringValue = options[nextIndex];
                if (_initialMoodIntensity.floatValue <= 0f)
                    _initialMoodIntensity.floatValue =
                        EmotionPersonalityTable.DefaultRestingMoodIntensity;
            }

            forcedNeutral = string.Equals(_initialMoodLabel.stringValue, "neutral",
                System.StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_initialMoodLabel.stringValue) && !forcedNeutral)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _initialMoodIntensity.floatValue = EditorGUILayout.Slider(
                        new GUIContent("How strong"), _initialMoodIntensity.floatValue, 0.05f, 1f);
                }
            }

            DrawEffectiveRestingMoodLine(controller);
        }

        /// <summary>
        ///     Always-visible one-line summary of the resolved resting mood, so the precedence chain
        ///     is legible without cross-referencing the personality asset.
        /// </summary>
        /// <remarks>
        ///     Reads <see cref="EmotionSetupService.ResolveEffectiveRestingMood" /> rather than
        ///     resolving the chain again here. This line used to compare the override label to the
        ///     literal string "neutral" and treat any non-empty label as winning, which disagreed
        ///     with the runtime on a custom vocabulary and reported a typo as though it had taken
        ///     effect.
        /// </remarks>
        private void DrawEffectiveRestingMoodLine(ConvaiEmotionController controller)
        {
            // The dropdown writes through the SerializedObject, so the controller's own fields are
            // still one apply behind while the user is interacting with it.
            serializedObject.ApplyModifiedProperties();

            EmotionRestingMood resting = EmotionSetupService.ResolveEffectiveRestingMood(controller);
            GUILayout.Label(resting.Explanation, ConvaiEditorStyles.CaptionWrapped);

            if (!string.IsNullOrEmpty(resting.Suppressed))
                GUILayout.Label(resting.Suppressed, ConvaiEditorStyles.CaptionWrapped);

            if (!resting.LabelResolves)
            {
                GUILayout.Label(
                    $"This character's vocabulary has no emotion called " +
                    $"'{_initialMoodLabel.stringValue}', so the setting is ignored.",
                    ConvaiEditorStyles.CaptionWrapped);
            }
        }

        private void DrawHoldExpression(ConvaiEmotionController controller)
        {
            EditorGUILayout.PropertyField(_lockEmotion, new GUIContent(
                "Hold one expression",
                "Freezes the face on a chosen expression and ignores everything the character " +
                "feels. For testing and cutscenes."));

            if (!_lockEmotion.boolValue) return;

            using (new EditorGUI.IndentLevelScope())
            {
                ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);
                string[] labels = BuildEmotionLabels(profile, _lockedEmotionLabel.stringValue);
                int currentIndex = IndexOfLabel(labels, _lockedEmotionLabel.stringValue);
                if (currentIndex < 0) currentIndex = IndexOfLabel(labels, "neutral");
                if (currentIndex < 0) currentIndex = 0;

                int nextIndex = EditorGUILayout.Popup(
                    new GUIContent("Expression"), currentIndex, EmotionLabelCatalog.DisplayNames(labels));
                if (nextIndex >= 0 && nextIndex < labels.Length)
                    _lockedEmotionLabel.stringValue = labels[nextIndex];

                EditorGUILayout.PropertyField(_lockedIntensity, new GUIContent("How strong"));
            }
        }

        /// <summary>
        ///     The brief mood reaction after a Convai action succeeds or fails.
        /// </summary>
        /// <remarks>
        ///     These five fields were serialized but drawn by no inspector and reachable through no
        ///     fallback, so the feature they configure could not be used or discovered. They are
        ///     shown as inert with the reason when the character has no Action Runner, rather
        ///     than as controls that silently do nothing.
        /// </remarks>
        private void DrawMoodAfterActions(ConvaiEmotionController controller)
        {
            bool hasDispatcher = EmotionTroubleshooter.HasActionDispatcher(controller);

            ConvaiEditorControls.GroupCaption("Mood after actions");

            using (new EditorGUI.DisabledScope(!hasDispatcher))
            {
                EditorGUILayout.PropertyField(_actionSuccessMoodLabel, new GUIContent(
                    "When it succeeds", "Mood the character briefly moves toward after an action works."));
                EditorGUILayout.PropertyField(_actionFailureMoodLabel, new GUIContent(
                    "When it fails", "Mood the character briefly moves toward after an action fails."));
                EditorGUILayout.PropertyField(_actionMoodIntensity, new GUIContent("How strong"));
                EditorGUILayout.PropertyField(_actionMoodHoldSeconds, new GUIContent(
                    "How long it lasts", "Seconds the reaction holds before it lifts off on its own."));
                EditorGUILayout.PropertyField(_actionMoodTransitionSeconds, new GUIContent("Ease in and out over"));
            }

            if (!hasDispatcher)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    GUILayout.Label(
                        "This character has no Action Runner, so there are no action outcomes to " +
                        "react to yet.", ConvaiEditorStyles.CaptionWrapped);
                }
            }
        }

        private static void DrawFooterLink(ConvaiEmotionController controller)
        {
            EditorGUILayout.Space(6f);
            // The only documented route to the deeper surface. Full settings and expression content
            // live there, never here (see the class remarks).
            if (GhostButton(AdvancedSettingsButton))
                ConvaiEmotionEditorWindow.ShowFor(controller);
        }

        // ------------------------------------------------------------------ label helpers

        /// <summary>
        ///     The emotions this character's vocabulary defines, with <paramref name="currentLabel" />
        ///     kept selectable if the vocabulary no longer defines it. Resolved through
        ///     <see cref="EmotionLabelCatalog" />, which caches — this is called from three draw
        ///     paths on an inspector that repaints every frame in Play Mode, and it used to
        ///     synthesize and destroy a taxonomy asset on each one.
        /// </summary>
        private static string[] BuildEmotionLabels(ConvaiEmotionProfile profile, string currentLabel) =>
            EmotionLabelCatalog.LabelsFor(profile, currentLabel);

        private static int IndexOfLabel(IReadOnlyList<string> labels, string label) =>
            EmotionLabelCatalog.IndexOf(labels, label);
    }
}
