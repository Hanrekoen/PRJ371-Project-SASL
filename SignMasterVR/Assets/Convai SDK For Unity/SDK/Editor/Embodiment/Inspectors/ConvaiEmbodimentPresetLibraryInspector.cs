using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.Embodiment.Presets;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Inspector for <see cref="ConvaiEmbodimentPresetLibrary" />: the presets it holds, the id
    ///     each one answers to, and whether any two answer to the same id.
    /// </summary>
    /// <remarks>
    ///     The preset asset has a full inspector; its library had none, so the one asset in the pair
    ///     whose whole purpose is lookup-by-id showed a bare list that never displayed a single id.
    ///     A duplicate id — where the first entry silently wins and the second is unreachable — was
    ///     reported only as an <c>OnValidate</c> line in the console.
    /// </remarks>
    [CustomEditor(typeof(ConvaiEmbodimentPresetLibrary))]
    internal sealed class ConvaiEmbodimentPresetLibraryInspector : ConvaiInspectorEditor
    {
        private const string SectionPresets = "Presets";

        private static readonly GUIContent PresetsLabel = new("Presets");

        private SerializedProperty _presets;

        protected override void OnEnable()
        {
            base.OnEnable();
            _presets = serializedObject.FindProperty("presets");
        }

        protected override string Title => "Embodiment Preset Library";

        protected override string Subtitle => "Swap a character's whole personality by id";

        protected override GUIContent StatusChip => HasDuplicates(out _)
            ? ConvaiEditorChips.NeedsAttention.Content
            : ConvaiEditorChips.Ready.Content;

        protected override Color StatusChipTint => HasDuplicates(out _)
            ? ConvaiEditorTheme.StatusWarn
            : ConvaiEditorTheme.StatusReady;

        protected override void DrawBody()
        {
            InfoBox(
                "What this does",
                "A list of presets that can be applied by id at runtime, for a character that changes " +
                "persona mid-session. Assign it to the character's Embodiment Preset component and " +
                "call Apply Preset By Id. It applies nothing on its own.");

            if (!DrawSection(SectionPresets, "Presets", ConvaiEditorGlyphs.Content)) return;

            DrawSectionBody(() =>
            {
                if (_presets != null) EditorGUILayout.PropertyField(_presets, PresetsLabel, true);

                DrawIdTable();

                if (HasDuplicates(out string message))
                    WarningBox("Duplicate Ids", message);
            });
        }

        /// <summary>
        ///     The id each entry answers to — the thing the list itself never shows, and the only
        ///     value a runtime swap actually uses.
        /// </summary>
        private void DrawIdTable()
        {
            var library = (ConvaiEmbodimentPresetLibrary)target;
            IReadOnlyList<ConvaiEmbodimentPreset> presets = library.Presets;
            if (presets == null || presets.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No presets yet. Add one above to make it reachable by id.",
                    ConvaiEditorStyles.CaptionWrapped);
                return;
            }

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Ids In This Library", ConvaiEditorStyles.SectionTitle);

            for (int i = 0; i < presets.Count; i++)
            {
                ConvaiEmbodimentPreset preset = presets[i];
                if (preset == null)
                {
                    EditorGUILayout.LabelField($"{i}", "— empty slot —", ConvaiEditorStyles.CaptionWrapped);
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(preset.PresetId)
                    ? "— no id, so it cannot be applied —"
                    : preset.PresetId;
                EditorGUILayout.LabelField(preset.name, id);
            }
        }

        private bool HasDuplicates(out string message)
        {
            var library = (ConvaiEmbodimentPresetLibrary)target;
            return library.HasDuplicatePresetIds(out message);
        }
    }
}
