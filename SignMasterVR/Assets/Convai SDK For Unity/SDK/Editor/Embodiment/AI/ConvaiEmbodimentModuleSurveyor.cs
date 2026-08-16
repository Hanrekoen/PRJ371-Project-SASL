using System.Collections.Generic;
using Convai.Editor.AI;
using Convai.Editor.Embodiment.Setup;
using Convai.Modules.Embodiment.Components;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.AI
{
    /// <summary>
    ///     Tells the scene-wide Convai tools what the Embodiment layer makes of a character — the rig
    ///     every feature depends on, and the preset that hands them their settings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is how <c>Convai.InspectScene</c> and <c>Convai.ValidateSetup</c> gained the rig
    ///         and the preset without either tool learning anything about Embodiment: they already
    ///         walk the survey seam, so registering here was the whole change. A mis-detected face
    ///         rig is the documented usual cause of "expression does nothing", and it was previously
    ///         invisible to every scene-wide tool.
    ///     </para>
    ///     <para>
    ///         <b>Deliberately does not build a <see cref="ConvaiEmbodimentReport" />.</b> That report
    ///         surveys every feature, which means calling
    ///         <see cref="ConvaiModuleSurveyRegistry.SurveyAll" /> — and this runs inside it. Reading
    ///         the two services directly is both the non-recursive answer and the correct one: this
    ///         surveyor's subject is the layer, not the features, which report themselves.
    ///     </para>
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiEmbodimentModuleSurveyor : IConvaiModuleSurveyor
    {
        static ConvaiEmbodimentModuleSurveyor() =>
            ConvaiModuleSurveyRegistry.Register(new ConvaiEmbodimentModuleSurveyor());

        public string ModuleId => ConvaiEmbodimentReport.LayerModuleId;

        public string DisplayName => "Embodiment";

        public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
        {
            if (characterRoot == null || characterRoot.GetComponentInParent<ConvaiCharacter>(true) == null)
            {
                return new ConvaiModuleSurveyResult(
                    ModuleId, DisplayName, ConvaiCapabilityReadiness.NotInstalled,
                    "Not a Convai character.", string.Empty, null);
            }

            EmbodimentSetupReport rig = EmbodimentRigSetupService.Inspect(characterRoot);
            var findings = new List<ConvaiModuleSurveyFinding>(8);
            Collect(rig, findings);

            var presetBinding = characterRoot.GetComponentInChildren<ConvaiEmbodimentPresetBinding>(true);
            if (presetBinding != null)
                Collect(EmbodimentPresetTroubleshooter.Evaluate(presetBinding.Preset, characterRoot), findings);

            List<EmbodimentModuleDescriptor> present = EmbodimentModuleCatalog.ModulesOn(characterRoot);
            int declared = EmbodimentModuleCatalog.Modules.Count;

            string blocker = rig.HasBlocker ? FirstErrorMessage(rig) : string.Empty;

            return new ConvaiModuleSurveyResult(
                ModuleId, DisplayName,
                rig.HasBlocker
                    ? ConvaiCapabilityReadiness.Blocked
                    : present.Count == 0
                        ? ConvaiCapabilityReadiness.Inert
                        : ConvaiCapabilityReadiness.Working,
                rig.HasBlocker
                    ? blocker
                    : present.Count == 0
                        ? $"Rig {rig.HeaderStatus.ToLowerInvariant()}, but no expressive features are " +
                          "on this character yet, so it stays still and neutral."
                        : $"Rig {rig.HeaderStatus.ToLowerInvariant()}. " +
                          $"{present.Count} of {declared} features on this character.",
                blocker,
                findings);
        }

        /// <summary>
        ///     Carries a setup report's findings across, in the service's own words and severities.
        ///     <c>Ok</c> rows are dropped: they are confirmations, and a survey that lists them
        ///     reports a healthy character as having a page of problems.
        /// </summary>
        private static void Collect(
            in EmbodimentSetupReport report, List<ConvaiModuleSurveyFinding> findings)
        {
            IReadOnlyList<EmbodimentFinding> source = report.Findings;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Severity == EmbodimentFindingSeverity.Ok) continue;

                findings.Add(new ConvaiModuleSurveyFinding(
                    Translate(source[i].Severity), source[i].Title, source[i].Message));
            }
        }

        private static string FirstErrorMessage(in EmbodimentSetupReport report)
        {
            IReadOnlyList<EmbodimentFinding> findings = report.Findings;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity == EmbodimentFindingSeverity.Error)
                    return findings[i].Message;

            return string.Empty;
        }

        private static ConvaiModuleFindingSeverity Translate(EmbodimentFindingSeverity severity) =>
            severity switch
            {
                EmbodimentFindingSeverity.Error => ConvaiModuleFindingSeverity.Error,
                EmbodimentFindingSeverity.Warning => ConvaiModuleFindingSeverity.Warning,
                EmbodimentFindingSeverity.Info => ConvaiModuleFindingSeverity.Info,
                _ => ConvaiModuleFindingSeverity.Ok
            };
    }
}
