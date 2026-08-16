using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.Embodiment.Presets
{
    /// <summary>
    ///     Searchable collection of <see cref="ConvaiEmbodimentPreset" /> assets. Used by
    ///     tooling and character selector UIs to present a drop-down of
    ///     archetypes to the user and to resolve a preset by its <see cref="ConvaiEmbodimentPreset.PresetId" />
    ///     at runtime.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConvaiEmbodimentPresetLibrary",
        menuName = "Convai/Embodiment/Preset Library",
        order = 101)]
    public sealed class ConvaiEmbodimentPresetLibrary : ScriptableObject
    {
        [SerializeField]
        private List<ConvaiEmbodimentPreset> presets = new();

        public IReadOnlyList<ConvaiEmbodimentPreset> Presets => presets;

        private void OnValidate()
        {
            if (HasDuplicatePresetIds(out string message))
                ConvaiLogger.Warning($"{message}", LogCategory.Character);
        }

        /// <summary>
        ///     Returns the preset whose <see cref="ConvaiEmbodimentPreset.PresetId" /> matches
        ///     <paramref name="id" /> (case-insensitive). Null when no entry matches.
        /// </summary>
        public ConvaiEmbodimentPreset Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < presets.Count; i++)
            {
                ConvaiEmbodimentPreset preset = presets[i];
                if (preset != null && string.Equals(preset.PresetId, id, System.StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
            return null;
        }

        public bool HasDuplicatePresetIds(out string message)
        {
            if (!DuplicateDetector.HasDuplicates(
                    presets,
                    preset => preset?.PresetId,
                    out string duplicateKeys))
            {
                message = null;
                return false;
            }

            message = $"Duplicate preset ids: {duplicateKeys}. First matching preset is used.";
            return true;
        }
    }
}
