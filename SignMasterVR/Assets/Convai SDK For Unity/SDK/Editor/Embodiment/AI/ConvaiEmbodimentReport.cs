using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.AI;
using Convai.Editor.Embodiment.Setup;
using Convai.Modules.Embodiment.Components;
using Convai.Modules.Embodiment.Presets;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.AI
{
    /// <summary>
    ///     One of this character's expressive features, as the Embodiment layer sees it.
    /// </summary>
    /// <remarks>
    ///     Identity, description and menu paths come from <see cref="EmbodimentModuleCatalog" />;
    ///     readiness, summary, blocker and findings come from the feature's own registered surveyor,
    ///     which is a projection of that feature's own setup service. Nothing here evaluates a
    ///     feature itself.
    /// </remarks>
    internal readonly struct ConvaiEmbodimentCapability
    {
        internal ConvaiEmbodimentCapability(
            EmbodimentModuleDescriptor descriptor,
            Component component,
            string settingsAssetPath,
            bool hasSurveyor,
            ConvaiCapabilityReadiness readiness,
            string summary,
            string blocker,
            IReadOnlyList<ConvaiModuleSurveyFinding> findings)
        {
            Descriptor = descriptor;
            Component = component;
            SettingsAssetPath = settingsAssetPath;
            HasSurveyor = hasSurveyor;
            Readiness = readiness;
            Summary = summary;
            Blocker = blocker ?? string.Empty;
            Findings = findings ?? System.Array.Empty<ConvaiModuleSurveyFinding>();
        }

        internal EmbodimentModuleDescriptor Descriptor { get; }

        /// <summary>The feature's component on this character, or <c>null</c>.</summary>
        internal Component Component { get; }

        /// <summary>Project path of the settings asset assigned to it, or empty for built-in defaults.</summary>
        internal string SettingsAssetPath { get; }

        /// <summary>
        ///     Whether the feature reports into the survey seam. Conversation Flow does not, and does
        ///     not need to: it has no rig dependency and no content, so present means working.
        /// </summary>
        internal bool HasSurveyor { get; }

        internal ConvaiCapabilityReadiness Readiness { get; }
        internal string Summary { get; }
        internal string Blocker { get; }
        internal IReadOnlyList<ConvaiModuleSurveyFinding> Findings { get; }

        internal bool IsPresent => Component != null;
        internal bool IsWorking => Readiness == ConvaiCapabilityReadiness.Working;

        /// <summary>The menu path that adds this feature, exactly as a user reads it.</summary>
        internal string AddComponentMenuPath =>
            $"Add Component → Convai → Embodiment → {Descriptor.DisplayName}";

        /// <summary>The feature's own diagnosis tool, or empty when it has none.</summary>
        internal string DiagnoseTool => ConvaiEmbodimentCapabilityTools.Diagnose(Descriptor.ModuleId);

        /// <summary>The feature's own configuration tool, or empty when it has none.</summary>
        internal string ConfigureTool => ConvaiEmbodimentCapabilityTools.Configure(Descriptor.ModuleId);
    }

    /// <summary>
    ///     Which MCP tool owns which feature, so a survey can hand an assistant the next call instead
    ///     of leaving it to guess from a display name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately a small explicit table rather than a name derived from the display label.
    ///         Deriving it looks tidier and is wrong: Conversation Flow would resolve to
    ///         <c>Convai.DiagnoseConversation</c>, which diagnoses a running conversation session and
    ///         has nothing to do with the feature.
    ///     </para>
    ///     <para>
    ///         This is routing, not knowledge about a character, so it is not a second check engine.
    ///         The table names every feature's tools, including features an SDK build may not
    ///         install — an entry for an absent feature is never looked up, because the lookup is
    ///         keyed on the installed capability catalog rather than on this table.
    ///         <c>EveryCapabilityToolThisLayerRoutesToExistsInTheCatalog</c> asserts that every
    ///         capability actually installed routes to ids that are in
    ///         <see cref="ConvaiMcpToolCatalog" />, which is what stops the table pointing at a tool
    ///         that was renamed or removed.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEmbodimentCapabilityTools
    {
        private static readonly Dictionary<string, (string Configure, string Diagnose)> ByModuleId =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                [ModuleIds.Gaze] =
                    ("Convai.ConfigureGaze", "Convai.DiagnoseGaze"),
                [ModuleIds.Emotion] =
                    ("Convai.ConfigureEmotion", "Convai.DiagnoseEmotion"),
                [ModuleIds.BodyAnimation] =
                    ("Convai.ConfigureBodyAnimation", "Convai.DiagnoseBodyAnimation"),
                [ModuleIds.BodyLanguage] =
                    ("Convai.ConfigureBodyLanguage", "Convai.DiagnoseBodyLanguage")

                // Conversation Flow is absent on purpose: it has no tools of its own, and pointing at
                // an unrelated one would be worse than saying nothing.
            };

        /// <summary>Every tool id this table routes to, for the guard test.</summary>
        internal static IEnumerable<string> AllRoutedToolIds
        {
            get
            {
                foreach (KeyValuePair<string, (string Configure, string Diagnose)> entry in ByModuleId)
                {
                    yield return entry.Value.Configure;
                    yield return entry.Value.Diagnose;
                }
            }
        }

        internal static string Configure(string moduleId) =>
            moduleId != null && ByModuleId.TryGetValue(moduleId, out (string Configure, string Diagnose) tools)
                ? tools.Configure
                : string.Empty;

        internal static string Diagnose(string moduleId) =>
            moduleId != null && ByModuleId.TryGetValue(moduleId, out (string Configure, string Diagnose) tools)
                ? tools.Diagnose
                : string.Empty;
    }

    /// <summary>
    ///     Everything the Convai Embodiment tools know about one character, gathered once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict is a projection of <see cref="EmbodimentRigSetupService" />,
    ///         <see cref="EmbodimentModuleCatalog" />, <see cref="EmbodimentPresetTroubleshooter" />
    ///         and each feature's own registered surveyor — the same code the Convai Embodiment
    ///         window draws. This type performs no check of its own, so an assistant and the editor
    ///         cannot describe the same character differently.
    ///     </para>
    ///     <para>
    ///         Shared by all three tools and by the layer's own surveyor so those cannot drift apart
    ///         either.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiEmbodimentReport
    {
        /// <summary>
        ///     The layer's own id in the survey seam. Excluded from <see cref="Capabilities" />: the
        ///     Embodiment survey reports the rig and the preset, and listing itself as one of the
        ///     character's features would be both wrong and recursive.
        /// </summary>
        internal const string LayerModuleId = "convai.embodiment";

        internal const string CreatePresetMenuPath =
            "Assets → Create → Convai → Embodiment → Preset";

        internal const string AddPresetComponentMenuPath =
            "Add Component → Convai → Embodiment → Preset";

        private ConvaiEmbodimentReport(
            ConvaiCharacter character,
            EmbodimentSetupReport rig,
            StandardRigBinding rigBinding,
            Animator animator,
            IReadOnlyList<ConvaiEmbodimentCapability> capabilities,
            ConvaiEmbodimentPresetBinding presetBinding,
            ConvaiEmbodimentPreset preset,
            EmbodimentSetupReport presetReport)
        {
            Character = character;
            Rig = rig;
            RigBinding = rigBinding;
            Animator = animator;
            Capabilities = capabilities;
            PresetBinding = presetBinding;
            Preset = preset;
            PresetReport = presetReport;
        }

        internal ConvaiCharacter Character { get; }

        /// <summary>The rig service's own report, verbatim.</summary>
        internal EmbodimentSetupReport Rig { get; }

        /// <summary>The Character Rig component, or <c>null</c> when it has not been added yet.</summary>
        internal StandardRigBinding RigBinding { get; }

        internal Animator Animator { get; }

        /// <summary>Every feature the catalog declares, present or not, in display order.</summary>
        internal IReadOnlyList<ConvaiEmbodimentCapability> Capabilities { get; }

        internal ConvaiEmbodimentPresetBinding PresetBinding { get; }
        internal ConvaiEmbodimentPreset Preset { get; }

        /// <summary>
        ///     The preset troubleshooter's report, cross-checked against this character. Empty when
        ///     the character has no Preset component — presets are optional.
        /// </summary>
        internal EmbodimentSetupReport PresetReport { get; }

        internal GameObject Root => Character != null ? Character.gameObject : null;

        internal bool HasPreset => Preset != null;

        /// <summary>
        ///     How far this character as a whole is from being alive, taken from the worst thing
        ///     standing in its way.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Read from the rig first, because every feature depends on it and a blocked rig is a
        ///         different conversation from a feature that simply is not added yet.
        ///     </para>
        ///     <para>
        ///         <b>Reports the worst present feature, not the best.</b> A character whose face
        ///         cannot move has a real problem even when gaze and posture are fine, and calling
        ///         that character <c>Working</c> — as an any-feature-works roll-up would — buries the
        ///         one thing the user needs to act on under four that are healthy. A feature that is
        ///         simply not added never counts against the character: leaving Body Animation off is
        ///         a design choice, not a fault.
        ///     </para>
        ///     <para>
        ///         A character with no features at all is <see cref="ConvaiCapabilityReadiness.Inert" />
        ///         rather than broken: nothing is wrong with it, and it will still not do anything.
        ///     </para>
        /// </remarks>
        internal ConvaiCapabilityReadiness Readiness
        {
            get
            {
                if (Character == null) return ConvaiCapabilityReadiness.NotInstalled;
                if (Rig.HasBlocker) return ConvaiCapabilityReadiness.Blocked;

                bool anyBlocked = false;
                bool anyInert = false;
                bool anyWorking = false;
                for (int i = 0; i < Capabilities.Count; i++)
                {
                    if (!Capabilities[i].IsPresent) continue;
                    switch (Capabilities[i].Readiness)
                    {
                        case ConvaiCapabilityReadiness.Blocked: anyBlocked = true; break;
                        case ConvaiCapabilityReadiness.Inert: anyInert = true; break;
                        case ConvaiCapabilityReadiness.Working: anyWorking = true; break;
                    }
                }

                if (anyBlocked) return ConvaiCapabilityReadiness.Blocked;
                if (anyInert) return ConvaiCapabilityReadiness.Inert;
                return anyWorking
                    ? ConvaiCapabilityReadiness.Working
                    : ConvaiCapabilityReadiness.Inert;
            }
        }

        /// <summary>One line an assistant can show without expanding anything.</summary>
        internal string Summary
        {
            get
            {
                if (Character == null)
                    return "Not a Convai character. Add a Convai Character component to this "
                           + "object, or call Convai.ConfigureCharacter to set one up.";

                int present = 0;
                int working = 0;
                int inert = 0;
                int blocked = 0;
                for (int i = 0; i < Capabilities.Count; i++)
                {
                    ConvaiEmbodimentCapability capability = Capabilities[i];
                    if (!capability.IsPresent) continue;
                    present++;
                    switch (capability.Readiness)
                    {
                        case ConvaiCapabilityReadiness.Working: working++; break;
                        case ConvaiCapabilityReadiness.Inert: inert++; break;
                        case ConvaiCapabilityReadiness.Blocked: blocked++; break;
                    }
                }

                if (present == 0)
                {
                    return $"{DescribeRig()} No expressive features are on this character yet, so it " +
                           "stays still and neutral.";
                }

                string tail = inert == 0 && blocked == 0
                    ? string.Empty
                    : blocked == 0
                        ? $" — {inert} inert"
                        : inert == 0
                            ? $" — {blocked} blocked"
                            : $" — {inert} inert and {blocked} blocked";

                // Both counts, because they answer different questions: how much of the layer this
                // character uses, and how much of what it uses actually runs.
                return $"{DescribeRig()} {present} of {Capabilities.Count} features added, " +
                       $"{working} working{tail}.";
            }
        }

        /// <summary>The rig in one clause, since it is what every feature depends on.</summary>
        private string DescribeRig()
        {
            if (Rig.HasBlocker) return "Rig not set up — see the rig blocker below for what is missing.";
            if (RigBinding == null) return "Rig will be worked out automatically.";

            string face = RigBinding.DetectedConvention == RigConvention.Unknown
                ? "face rig not recognized"
                : $"{RigBinding.DetectedConvention} face";
            string body = Animator != null && Animator.isHuman ? "Humanoid rig" : "non-Humanoid rig";
            return $"{body}, {face}.";
        }

        /// <summary>
        ///     Gathers the report for <paramref name="character" />. Read-only and safe to call from
        ///     any diagnostic path.
        /// </summary>
        internal static ConvaiEmbodimentReport For(ConvaiCharacter character)
        {
            if (character == null)
            {
                return new ConvaiEmbodimentReport(
                    null, default, null, null,
                    System.Array.Empty<ConvaiEmbodimentCapability>(), null, null, default);
            }

            GameObject root = character.gameObject;
            var presetBinding = root.GetComponentInChildren<ConvaiEmbodimentPresetBinding>(true);
            ConvaiEmbodimentPreset preset = presetBinding != null ? presetBinding.Preset : null;

            return new ConvaiEmbodimentReport(
                character,
                EmbodimentRigSetupService.Inspect(root),
                root.GetComponentInChildren<StandardRigBinding>(true),
                root.GetComponentInChildren<Animator>(true),
                BuildCapabilities(root),
                presetBinding,
                preset,
                presetBinding == null
                    ? default
                    : EmbodimentPresetTroubleshooter.Evaluate(preset, root));
        }

        /// <summary>
        ///     Pairs every catalog-declared feature with what its own surveyor says about this
        ///     character. The catalog decides which features exist; the surveyor decides how each one
        ///     is doing. Neither question is answered here.
        /// </summary>
        private static IReadOnlyList<ConvaiEmbodimentCapability> BuildCapabilities(GameObject root)
        {
            IReadOnlyList<EmbodimentModuleDescriptor> declared = EmbodimentModuleCatalog.Modules;
            IReadOnlyList<ConvaiModuleSurveyResult> surveys = ConvaiModuleSurveyRegistry.SurveyAll(root);

            var capabilities = new List<ConvaiEmbodimentCapability>(declared.Count);
            for (int i = 0; i < declared.Count; i++)
            {
                EmbodimentModuleDescriptor descriptor = declared[i];
                if (string.Equals(descriptor.ModuleId, LayerModuleId, System.StringComparison.Ordinal))
                    continue;

                Component component = root.GetComponentInChildren(descriptor.ControllerType, true);

                if (TryFindSurvey(surveys, descriptor.ModuleId, out ConvaiModuleSurveyResult survey))
                {
                    capabilities.Add(new ConvaiEmbodimentCapability(
                        descriptor, component, ResolveSettingsAssetPath(component), true,
                        survey.Readiness, survey.Summary, survey.Blocker, survey.Findings));
                    continue;
                }

                // No surveyor. The honest answer is presence, and saying so is better than inventing
                // a verdict the feature never gave — Conversation Flow needs no rig and no content,
                // so on this character present really is working.
                capabilities.Add(new ConvaiEmbodimentCapability(
                    descriptor, component, ResolveSettingsAssetPath(component), false,
                    component != null
                        ? ConvaiCapabilityReadiness.Working
                        : ConvaiCapabilityReadiness.NotInstalled,
                    component != null
                        ? $"{descriptor.DisplayName} is on this character."
                        : DescribeAbsence(descriptor),
                    string.Empty,
                    System.Array.Empty<ConvaiModuleSurveyFinding>()));
            }

            return capabilities;
        }

        private static bool TryFindSurvey(
            IReadOnlyList<ConvaiModuleSurveyResult> surveys, string moduleId,
            out ConvaiModuleSurveyResult survey)
        {
            for (int i = 0; i < surveys.Count; i++)
            {
                if (!string.Equals(surveys[i].ModuleId, moduleId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                survey = surveys[i];
                return true;
            }

            survey = default;
            return false;
        }

        /// <summary>
        ///     The feature's own <c>Absence</c> sentence, said the way the setup surfaces say it, so
        ///     one wording reaches the window, the Inspector and an assistant.
        /// </summary>
        private static string DescribeAbsence(in EmbodimentModuleDescriptor descriptor) =>
            string.IsNullOrWhiteSpace(descriptor.Absence)
                ? $"Not on this character. {descriptor.Description}"
                : $"Not on this character — without it, {descriptor.Absence}";

        /// <summary>
        ///     The settings asset assigned to a feature, read generically from the profile field every
        ///     feature component inherits, so no feature needs its own branch here.
        /// </summary>
        private static string ResolveSettingsAssetPath(Component component)
        {
            if (component == null) return string.Empty;

            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty("profile");
            Object asset = property != null && property.propertyType == SerializedPropertyType.ObjectReference
                ? property.objectReferenceValue
                : null;

            return asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
        }
    }
}
