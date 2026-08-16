using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Editor.Embodiment.Setup;
using Convai.Editor.Inspectors;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Embodiment.Components;
using Convai.Modules.Embodiment.Presets;
using Convai.Modules.ConversationFlow.Components;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Convai.Editor.UI;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Embodiment.Inspectors
{
    [CustomEditor(typeof(ConvaiEmbodimentPresetBinding))]
    internal sealed class ConvaiEmbodimentPresetBindingEditor : ConvaiInspectorEditor
    {
        private const string SectionPreset = "Preset";
        private const string SectionAdvanced = "Advanced";

        private SerializedProperty _preset;
        private SerializedProperty _library;
        private SerializedProperty _preserveMissingSlots;
        private int _selectedPresetIndex;

        protected override void OnEnable()
        {
            base.OnEnable();
            _preset = serializedObject.FindProperty("preset");
            _library = serializedObject.FindProperty("library");
            _preserveMissingSlots = serializedObject.FindProperty("preserveMissingSlots");
        }

        // The name in Add Component is "Preset", so that is the name here too. The header used to
        // read "Character Embodiment Binding", which meant the component a user added under one name
        // introduced itself under another — and led with the implementation word the menu grammar
        // guard already bans from the menu leaf.
        protected override string Title => "Embodiment Preset";

        protected override string Subtitle => "One set of settings for every feature";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private static ConvaiEditorChip CurrentChip =>
            EditorApplication.isPlaying ? ConvaiEditorChips.Live : ConvaiEditorChips.Ready;

        protected override void DrawBody()
        {
            ConvaiCharacterScopeNotice.DrawIfMisplaced((Component)target, Title);

            InfoBox("What this does", DescribeReach());
            DrawPresetSection();
            DrawAdvancedSection();
        }

        /// <summary>
        ///     Names the features this preset reaches, read from the catalog.
        /// </summary>
        /// <remarks>
        ///     This sentence used to list the five features by hand — the exact duplication the module
        ///     catalog exists to remove, and one that would have gone quietly stale the first time a
        ///     feature was added or renamed.
        /// </remarks>
        private static string DescribeReach()
        {
            IReadOnlyList<EmbodimentModuleDescriptor> modules = EmbodimentModuleCatalog.Modules;
            if (modules.Count == 0)
                return "Hands one set of settings to each of this character's features in a single step.";

            var names = new string[modules.Count];
            for (int i = 0; i < modules.Count; i++) names[i] = modules[i].DisplayName;

            string list = names.Length == 1
                ? names[0]
                : string.Join(", ", names, 0, names.Length - 1) + " and " + names[names.Length - 1];

            return $"Hands one set of settings to each of this character's features — {list} — in a " +
                   "single step. A feature with no slot in the preset keeps whatever is on its own " +
                   "component.";
        }

        private void DrawPresetSection()
        {
            if (!DrawSection(SectionPreset, "Preset", ConvaiEditorGlyphs.Profile)) return;

            DrawSectionBody(() => { EditorGUILayout.PropertyField(_preset, new GUIContent("Embodiment Preset")); });
        }

        private void DrawAdvancedSection()
        {
            if (!DrawSection(SectionAdvanced, "Advanced", ConvaiEditorGlyphs.Routing, defaultExpanded: false)) return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_library, new GUIContent("Preset Library"));
                EditorGUILayout.PropertyField(_preserveMissingSlots, new GUIContent("Keep Unmatched Module Profiles"));
                DrawRuntimePresetSwap();
            });
        }

        private void DrawRuntimePresetSwap()
        {
            var binding = (ConvaiEmbodimentPresetBinding)target;
            ConvaiEmbodimentPresetLibrary library = binding.Library;
            if (library == null)
            {
                InfoBox("Runtime Preset Swap", "Assign a preset library here only if this character needs to switch embodiment presets during Play Mode.");
                return;
            }

            IReadOnlyList<ConvaiEmbodimentPreset> presets = library.Presets;
            if (presets == null || presets.Count == 0)
            {
                WarningBox("Runtime Preset Swap", "The assigned preset library has no presets to apply.");
                return;
            }

            UnityEngine.GUILayout.Space(4f);
            EditorGUILayout.LabelField("Runtime Preset Swap", ConvaiEditorStyles.SectionTitle);
            if (!EditorApplication.isPlaying)
            {
                InfoBox("Play Mode Only", "Enter Play Mode to switch presets at runtime.");
                return;
            }

            string[] presetIds = BuildPresetIdLabels(presets);
            _selectedPresetIndex = UnityEngine.Mathf.Clamp(_selectedPresetIndex, 0, presets.Count - 1);
            _selectedPresetIndex = EditorGUILayout.Popup("Preset", _selectedPresetIndex, presetIds);

            if (UnityEngine.GUILayout.Button("Apply Selected Preset", UnityEngine.GUILayout.Height(26f)))
            {
                ConvaiEmbodimentPreset selected = presets[_selectedPresetIndex];
                if (selected != null)
                {
                    binding.ApplyPresetById(selected.PresetId);
                }
            }
        }

        private static string[] BuildPresetIdLabels(IReadOnlyList<ConvaiEmbodimentPreset> presets)
        {
            string[] labels = new string[presets.Count];
            for (int i = 0; i < presets.Count; i++)
            {
                ConvaiEmbodimentPreset p = presets[i];
                labels[i] = p != null && !string.IsNullOrWhiteSpace(p.PresetId)
                    ? p.PresetId
                    : $"<null slot {i}>";
            }
            return labels;
        }
    }

    [CustomEditor(typeof(ConvaiConversationFlowController))]
    internal sealed class ConvaiConversationFlowControllerEditor : ConvaiInspectorEditor
    {
        private const string SectionProfile = "Profile";
        private const string SectionLive = "LiveFlow";

        private const string ModuleId = Convai.Domain.Embodiment.Modules.ModuleIds.ConversationFlow;

        private SerializedProperty _profile;
        private bool _onCharacter;

        protected override void OnEnable()
        {
            base.OnEnable();
            _profile = serializedObject.FindProperty("profile");
        }

        protected override string Title => EmbodimentModuleCatalog.DescribeModule(ModuleId);

        protected override string Subtitle => "Realtime dialogue state";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private ConvaiEditorChip CurrentChip
        {
            get
            {
                if (!_onCharacter) return ConvaiEditorChips.NeedsAttention;
                return EditorApplication.isPlaying ? ConvaiEditorChips.Live : ConvaiEditorChips.Ready;
            }
        }

        /// <summary>Keeps the live dialogue-state readout updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        /// <summary>
        ///     Resolves placement once per pass, before the header reads <see cref="StatusChip" />.
        /// </summary>
        protected override void OnBeforeInspectorGUI() =>
            _onCharacter = ConvaiCharacterScopeNotice.IsOnConvaiCharacter((Component)target);

        protected override void DrawBody()
        {
            var driver = (ConvaiConversationFlowController)target;

            // Once, at the top. This notice used to be drawn here *and* again inside a Validation
            // section, so a misplaced component reported the same problem to the user twice.
            ConvaiCharacterScopeNotice.DrawIfMisplaced((Component)target, Title);

            InfoBox("What this does", DescribeModule());
            DrawProfileSection();
            DrawLiveStateSection(driver);
        }

        /// <summary>
        ///     The module's own one-line description, so this inspector cannot drift from the label
        ///     the preset editor and the character map show. It previously carried its own copy,
        ///     which named three of the eight dialogue states.
        /// </summary>
        private static string DescribeModule() =>
            EmbodimentModuleCatalog.TryGet(ModuleId, out EmbodimentModuleDescriptor module)
            && !string.IsNullOrEmpty(module.Description)
                ? module.Description +
                  " Every other feature reads this state to decide how to behave."
                : "Tracks the character's dialogue state so the other features can respond to it.";

        /// <summary>The profile slot, drawn as the first row whether or not one is assigned.</summary>
        private static readonly GUIContent FlowProfileField = new(
            "Flow Profile",
            "The asset holding this character's dialogue-state timing. Can be shared with other " +
            "characters.");

        /// <summary>
        ///     Which flow profile this character runs on, and the field that changes it.
        /// </summary>
        /// <remarks>
        ///     No ownership notice, deliberately: nothing on this inspector writes into the profile,
        ///     so a shared-asset warning here would be an alarm about a write that cannot happen.
        /// </remarks>
        private void DrawProfileSection()
        {
            if (!DrawSection(
                    SectionProfile, "Profile", ConvaiEditorGlyphs.Profile,
                    summary: ConvaiEditorProfileField.Summarize(_profile.objectReferenceValue))) return;

            DrawSectionBody(() =>
            {
                ConvaiEditorProfileField.Draw(_profile, FlowProfileField);
                if (_profile.objectReferenceValue == null)
                    InfoBox(
                        "Using SDK Defaults",
                        "This character has no flow profile asset, so it uses the built-in " +
                        "defaults — which work. Assign one above to change its dialogue-state timing.");
            });
        }

        private void DrawLiveStateSection(ConvaiConversationFlowController driver)
        {
            if (!DrawSection(SectionLive, "Live", ConvaiEditorGlyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying)
                {
                    OfflinePlaceholder();
                    return;
                }

                DialogueStateReading reading = driver.Current;
                EditorGUILayout.BeginHorizontal();
                LiveCell("State", reading.Primary.ToString(), Theme.AccentBright, 110f);
                LiveCell("Blend To", reading.BlendTo.ToString(), Theme.TextPrimary, 110f);
                LiveCell("Blend", reading.BlendWeight.ToString("0.00"), Theme.StatusInfo, 110f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Time In State", $"{reading.TimeInState:F1}s");
                EditorGUILayout.LabelField("Energy Level", reading.EnergyLevel.ToString("0.00"));
            });
        }
    }
}
