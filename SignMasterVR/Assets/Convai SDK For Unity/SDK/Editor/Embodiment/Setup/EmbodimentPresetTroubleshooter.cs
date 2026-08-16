using System;
using System.Collections.Generic;
using Convai.Modules.Embodiment.Components;
using Convai.Modules.Embodiment.Presets;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Setup
{
    /// <summary>
    ///     Checks an embodiment preset — and, when there is one, the character it is applied to — and
    ///     reports what is wrong in plain language, with a one-click fix wherever one exists.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Replaces two separate diagnostic engines that did not know about each other: an
    ///         editor-side slot validator that rendered inline, and a runtime report the preset
    ///         binding emitted only as a console warning where nobody looked. One model, one place,
    ///         and the findings now carry fixes instead of just descriptions.
    ///     </para>
    ///     <para>
    ///         Every "which modules exist" question goes to <see cref="EmbodimentModuleCatalog" />.
    ///         The hand-written map this used to validate against was missing two entries, which is
    ///         why the SDK's own sample preset shipped flagging its own correct configuration as an
    ///         unrecognized module.
    ///     </para>
    /// </remarks>
    internal static class EmbodimentPresetTroubleshooter
    {
        /// <summary>
        ///     Inspects <paramref name="preset" /> on its own — asset-level checks only, valid with no
        ///     character in the picture.
        /// </summary>
        internal static EmbodimentSetupReport Evaluate(ConvaiEmbodimentPreset preset) =>
            Evaluate(preset, null);

        /// <summary>
        ///     Inspects <paramref name="preset" />, and cross-checks it against the modules actually
        ///     present on <paramref name="characterRoot" /> when one is supplied.
        /// </summary>
        /// <remarks>
        ///     Safe to call every repaint: it allocates a findings list and reads serialized data, and
        ///     does not touch the asset. Callers should still cache it per repaint rather than calling
        ///     it once per header field — the previous inspector ran its analysis three times to answer
        ///     two halves of one question.
        /// </remarks>
        internal static EmbodimentSetupReport Evaluate(ConvaiEmbodimentPreset preset, GameObject characterRoot)
        {
            var findings = new List<EmbodimentFinding>();

            if (preset == null)
            {
                findings.Add(EmbodimentFinding.Error(
                    "preset.missing", "No preset assigned",
                    "Assign an Embodiment Preset asset, or remove this component — each feature works " +
                    "on its own with the profile set on its own component."));
                return new EmbodimentSetupReport(findings);
            }

            IReadOnlyList<EmbodimentProfileSlot> slots = preset.ProfileSlots;

            if (slots == null || slots.Count == 0)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "preset.no-slots", "No features configured",
                    "This preset assigns nothing yet. Add an entry for each feature whose settings you " +
                    "want it to carry."));
                return new EmbodimentSetupReport(findings);
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < slots.Count; i++)
            {
                EmbodimentProfileSlot slot = slots[i];
                int index = i;

                if (slot == null)
                {
                    findings.Add(EmbodimentFinding.Error(
                        "preset.slot-null", $"Entry {index + 1} is empty",
                        "This entry has no data at all. Remove it.",
                        "Remove Entry", () => RemoveSlot(preset, index)));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(slot.ModuleId))
                {
                    findings.Add(EmbodimentFinding.Error(
                        "preset.slot-no-module", $"Entry {index + 1} has no feature",
                        "Pick which feature this entry configures.",
                        "Remove Entry", () => RemoveSlot(preset, index)));
                    continue;
                }

                bool known = EmbodimentModuleCatalog.TryGet(slot.ModuleId, out EmbodimentModuleDescriptor module);
                string label = known ? module.DisplayName : slot.ModuleId;

                if (!seenIds.Add(slot.ModuleId))
                {
                    findings.Add(EmbodimentFinding.Error(
                        "preset.slot-duplicate", $"{label} is listed twice",
                        "Only the first entry is used. Remove the duplicate so the preset says what it means.",
                        "Remove Duplicate", () => RemoveSlot(preset, index)));
                    continue;
                }

                if (!known)
                {
                    // This asks the installed catalog, which cannot tell a typo from a feature the
                    // project simply does not have — so the message must not call a valid entry
                    // wrong. Removing is offered last and named as the answer for a typo, because
                    // for an uninstalled feature it silently discards a setting the user chose and
                    // installing the module would have restored.
                    findings.Add(EmbodimentFinding.Warning(
                        "preset.slot-unknown-module",
                        $"'{slot.ModuleId}' is not a feature this project has installed",
                        "Nothing on this character will read the entry while that is true. Leave it " +
                        "if the feature is one you plan to install or one of your own, or remove it " +
                        "if the name is a typo.",
                        "Remove Entry", () => RemoveSlot(preset, index)));
                    continue;
                }

                if (slot.Profile == null)
                {
                    findings.Add(EmbodimentFinding.Info(
                        "preset.slot-no-profile", $"{label} has no settings assigned",
                        $"{label} will use its built-in defaults. That is fine — assign a " +
                        $"{ObjectNames.NicifyVariableName(module.ProfileType?.Name ?? "profile")} only if you " +
                        "want to change them."));
                    continue;
                }

                if (module.ProfileType != null && !module.ProfileType.IsInstanceOfType(slot.Profile))
                {
                    findings.Add(EmbodimentFinding.Error(
                        "preset.slot-wrong-type", $"{label} has the wrong kind of settings",
                        $"{label} needs a {ObjectNames.NicifyVariableName(module.ProfileType.Name)}, but " +
                        $"'{slot.Profile.name}' is a {ObjectNames.NicifyVariableName(slot.Profile.GetType().Name)}. " +
                        "It will be rejected when the character starts.",
                        "Clear Settings", () => ClearSlotProfile(preset, index)));
                }
            }

            if (characterRoot != null)
                AddCharacterCrossChecks(preset, characterRoot, seenIds, findings);

            if (findings.Count == 0)
            {
                findings.Add(EmbodimentFinding.Ok(
                    "preset.ready", "Ready",
                    $"All {slots.Count} entries name a real feature and carry compatible settings."));
            }

            return new EmbodimentSetupReport(findings);
        }

        /// <summary>
        ///     Compares the preset against what is actually on the character: features configured but
        ///     not present, and features present but unconfigured.
        /// </summary>
        private static void AddCharacterCrossChecks(
            ConvaiEmbodimentPreset preset,
            GameObject characterRoot,
            HashSet<string> slotIds,
            List<EmbodimentFinding> findings)
        {
            List<EmbodimentModuleDescriptor> present = EmbodimentModuleCatalog.ModulesOn(characterRoot);
            var presentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < present.Count; i++)
            {
                presentIds.Add(present[i].ModuleId);

                if (slotIds.Contains(present[i].ModuleId)) continue;

                EmbodimentModuleDescriptor module = present[i];
                findings.Add(EmbodimentFinding.Info(
                    "preset.character-module-unconfigured",
                    $"{module.DisplayName} is on this character but not in the preset",
                    $"{module.DisplayName} will keep whatever settings are on its own component. Add an " +
                    "entry if you want this preset to control it.",
                    $"Add {module.DisplayName} Entry",
                    () => AddSlot(preset, module.ModuleId)));
            }

            foreach (string slotId in slotIds)
            {
                if (presentIds.Contains(slotId)) continue;

                string label = EmbodimentModuleCatalog.DescribeModule(slotId);
                findings.Add(EmbodimentFinding.Warning(
                    "preset.slot-module-absent",
                    $"{label} is in the preset but not on this character",
                    $"This entry does nothing here. Add the {label} component to the character, or remove " +
                    "the entry."));
            }
        }

        // ── one-click fixes ─────────────────────────────────────────────────────────
        // Each is a single undo group so a user can take one fix back without unpicking the rest.

        private static void RemoveSlot(ConvaiEmbodimentPreset preset, int index)
        {
            var slots = new List<EmbodimentProfileSlot>(preset.ProfileSlots);
            if (index < 0 || index >= slots.Count) return;

            Undo.RecordObject(preset, "Remove Preset Entry");
            slots.RemoveAt(index);
            preset.SetProfileSlots(slots);
            EditorUtility.SetDirty(preset);
        }

        private static void ClearSlotProfile(ConvaiEmbodimentPreset preset, int index)
        {
            var slots = new List<EmbodimentProfileSlot>(preset.ProfileSlots);
            if (index < 0 || index >= slots.Count) return;

            Undo.RecordObject(preset, "Clear Preset Entry Settings");
            slots[index] = new EmbodimentProfileSlot(slots[index].ModuleId, null);
            preset.SetProfileSlots(slots);
            EditorUtility.SetDirty(preset);
        }

        private static void AddSlot(ConvaiEmbodimentPreset preset, string moduleId)
        {
            var slots = new List<EmbodimentProfileSlot>(preset.ProfileSlots);

            Undo.RecordObject(preset, "Add Preset Entry");
            slots.Add(new EmbodimentProfileSlot(moduleId, null));
            preset.SetProfileSlots(slots);
            EditorUtility.SetDirty(preset);
        }

        /// <summary>
        ///     Applies every finding that offers a fix, in one undo group.
        /// </summary>
        /// <remarks>
        ///     Fixes run newest-index-first because several of them remove entries by index; running
        ///     forward would shift the indices of the fixes still queued.
        /// </remarks>
        internal static int ApplyAllFixes(EmbodimentSetupReport report)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Embodiment Preset");

            int applied = 0;
            for (int i = report.Findings.Count - 1; i >= 0; i--)
            {
                EmbodimentFinding finding = report.Findings[i];
                if (!finding.CanFix) continue;

                finding.Fix();
                applied++;
            }

            Undo.CollapseUndoOperations(group);
            return applied;
        }
    }
}
