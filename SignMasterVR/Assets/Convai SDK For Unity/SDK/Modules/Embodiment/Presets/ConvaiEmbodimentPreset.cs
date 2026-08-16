using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.Embodiment.Presets
{
    /// <summary>
    ///     Optional bundle of embodiment-module profiles. Modules remain independently usable;
    ///     this asset only maps stable module ids to ScriptableObject profiles.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConvaiEmbodimentPreset",
        menuName = "Convai/Embodiment/Preset",
        order = 100)]
    public sealed class ConvaiEmbodimentPreset : ScriptableObject
    {
        // No [Header] on serialized fields: ConvaiEmbodimentPresetInspector draws each of them into
        // its own named sections and never falls back to the default inspector, so a header here
        // renders nowhere and can only drift away from the section it was meant to name.
        [SerializeField]
        [Tooltip("Short identifier referenced from a ConvaiEmbodimentPresetLibrary.")]
        private string presetId = "default";

        [SerializeField, TextArea]
        [Tooltip("Human-readable description for the library picker.")]
        private string description;

        [SerializeField]
        [Tooltip("Profile entries keyed by stable embodiment module id.")]
        private List<EmbodimentProfileSlot> profileSlots = new();

        public string PresetId => presetId;
        public string Description => description;
        public IReadOnlyList<EmbodimentProfileSlot> ProfileSlots => profileSlots;

        private void OnValidate()
        {
            if (HasDuplicateModuleIds(out string message))
                ConvaiLogger.Warning($"{message}", LogCategory.Character);
        }

        public bool TryGetProfile(string moduleId, out ScriptableObject profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(moduleId) || profileSlots == null) return false;

            for (int i = 0; i < profileSlots.Count; i++)
            {
                EmbodimentProfileSlot slot = profileSlots[i];
                if (slot == null) continue;
                if (!string.Equals(slot.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)) continue;

                profile = slot.Profile;
                return true;
            }

            return false;
        }

        public bool HasDuplicateModuleIds(out string message)
        {
            if (!DuplicateDetector.HasDuplicates(
                    profileSlots,
                    slot => slot?.ModuleId,
                    out string duplicateKeys))
            {
                message = null;
                return false;
            }

            message = $"Duplicate module profile slots: {duplicateKeys}. First matching slot is used.";
            return true;
        }

        public bool TryBuildDiagnosticReport(
            IReadOnlyCollection<string> receiverModuleIds,
            bool preserveMissingSlots,
            out string report)
        {
            report = null;
            var lines = new List<string>();

            if (HasDuplicateModuleIds(out string duplicateMessage))
                lines.Add(duplicateMessage);

            HashSet<string> receiverIds = receiverModuleIds != null
                ? new HashSet<string>(receiverModuleIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> slotIds = new(StringComparer.OrdinalIgnoreCase);
            var unknownSlots = new List<string>();
            var nullProfiles = new List<string>();
            var blankSlots = new List<int>();

            if (profileSlots != null)
            {
                for (int i = 0; i < profileSlots.Count; i++)
                {
                    EmbodimentProfileSlot slot = profileSlots[i];
                    if (slot == null || string.IsNullOrWhiteSpace(slot.ModuleId))
                    {
                        blankSlots.Add(i);
                        continue;
                    }

                    slotIds.Add(slot.ModuleId);
                    if (slot.Profile == null)
                        nullProfiles.Add(slot.ModuleId);
                    if (receiverIds.Count > 0 && !receiverIds.Contains(slot.ModuleId))
                        unknownSlots.Add(slot.ModuleId);
                }
            }

            var missingSlots = new List<string>();
            foreach (string receiverId in receiverIds)
            {
                if (!slotIds.Contains(receiverId))
                    missingSlots.Add(receiverId);
            }

            AddSortedLine(lines, unknownSlots, "Profile slots without an active receiver");
            AddSortedLine(lines, nullProfiles, "Profile slots with null profiles");
            AddSortedLine(lines, missingSlots,
                preserveMissingSlots
                    ? "Active receivers without a preset slot; inspector profiles will be preserved"
                    : "Active receivers without a preset slot; null profiles will be applied");

            if (blankSlots.Count > 0)
                lines.Add($"Blank profile slot indices: {string.Join(", ", blankSlots)}.");

            if (lines.Count == 0) return false;

            report = string.Join(" ", lines);
            return true;
        }

        public void SetProfileSlots(IReadOnlyList<EmbodimentProfileSlot> slots)
        {
            profileSlots.Clear();
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                EmbodimentProfileSlot slot = slots[i];
                if (slot != null) profileSlots.Add(slot);
            }
        }

        private static void AddSortedLine(List<string> lines, List<string> values, string label)
        {
            if (values == null || values.Count == 0) return;
            values.Sort(StringComparer.OrdinalIgnoreCase);
            lines.Add($"{label}: {string.Join(", ", values)}.");
        }
    }

    [Serializable]
    public sealed class EmbodimentProfileSlot
    {
        [SerializeField]
        [Tooltip("Stable id exposed by the target embodiment module.")]
        private string moduleId;

        [SerializeField]
        [Tooltip("Profile asset consumed by the target embodiment module.")]
        private ScriptableObject profile;

        public EmbodimentProfileSlot(string moduleId, ScriptableObject profile)
        {
            this.moduleId = moduleId;
            this.profile = profile;
        }

        public string ModuleId => moduleId;
        public ScriptableObject Profile => profile;
    }
}
