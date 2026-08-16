using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Editor.AI;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor.AI
{
    /// <summary>
    ///     Tells the scene-wide Convai tools what Emotions makes of a character, so an assistant
    ///     inspecting a scene can see the capability exists before it knows to reach for
    ///     <c>Convai.DiagnoseEmotion</c>.
    /// </summary>
    /// <remarks>
    ///     A projection of <see cref="EmotionSetupService" /> and
    ///     <see cref="EmotionTroubleshooter" />, never a second opinion: the survey, the four tools
    ///     and the component inspector all read the same checks.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiEmotionModuleSurveyor : IConvaiModuleSurveyor
    {
        static ConvaiEmotionModuleSurveyor() =>
            ConvaiModuleSurveyRegistry.Register(new ConvaiEmotionModuleSurveyor());

        public string ModuleId => ModuleIds.Emotion;

        public string DisplayName => "Emotions";

        public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
        {
            ConvaiEmotionReport report = ConvaiEmotionReport.For(characterRoot);

            if (!report.IsPresent)
            {
                return new ConvaiModuleSurveyResult(
                    ModuleId, DisplayName, ConvaiCapabilityReadiness.NotInstalled,
                    report.Summary, string.Empty,
                    new[]
                    {
                        new ConvaiModuleSurveyFinding(
                            ConvaiModuleFindingSeverity.Info, "Emotions",
                            "This character has no Emotion component, so its face never reacts to " +
                            "what is said. Add it with Add Component → Convai → Embodiment → Emotion; " +
                            "it starts on Responsive detection, so it works as soon as it is added.")
                    });
            }

            var findings = new List<ConvaiModuleSurveyFinding>(6);

            IReadOnlyList<EmotionCheck> checks = report.Preflight.Checks;
            for (int i = 0; i < checks.Count; i++)
            {
                // Ok rows are working and Optional rows are a legitimate character — surfacing
                // either would report a healthy character as having four problems.
                if (checks[i].State is EmotionCheckState.Ok or EmotionCheckState.Optional) continue;

                findings.Add(new ConvaiModuleSurveyFinding(
                    checks[i].State == EmotionCheckState.Blocked
                        ? ConvaiModuleFindingSeverity.Error
                        : ConvaiModuleFindingSeverity.Info,
                    checks[i].Label,
                    checks[i].Detail));
            }

            // Detection Off used to be reported here in this type's own words, because the
            // troubleshooter only raised it alongside an assigned personality. It raises it in
            // every case now, so this surface adds nothing of its own: the assistant and the
            // inspector describe a character from one list, which is the whole point of the seam.
            IReadOnlyList<EmotionFinding> troubleshooterFindings = report.Findings;
            for (int i = 0; i < troubleshooterFindings.Count; i++)
            {
                EmotionFinding finding = troubleshooterFindings[i];

                findings.Add(new ConvaiModuleSurveyFinding(
                    finding.Severity switch
                    {
                        EmotionSeverity.Error => ConvaiModuleFindingSeverity.Error,
                        EmotionSeverity.Warning => ConvaiModuleFindingSeverity.Warning,
                        _ => ConvaiModuleFindingSeverity.Info
                    },
                    finding.Title,
                    finding.Message));
            }

            return new ConvaiModuleSurveyResult(
                ModuleId, DisplayName, Translate(report.State), report.Summary,
                DescribeBlocker(report), findings);
        }

        /// <summary>This module's own readiness in the layer-wide vocabulary.</summary>
        private static ConvaiCapabilityReadiness Translate(EmotionReadiness readiness) => readiness switch
        {
            EmotionReadiness.NotInstalled => ConvaiCapabilityReadiness.NotInstalled,
            EmotionReadiness.Blocked => ConvaiCapabilityReadiness.Blocked,
            EmotionReadiness.Inert => ConvaiCapabilityReadiness.Inert,
            _ => ConvaiCapabilityReadiness.Working
        };

        private static string DescribeBlocker(in ConvaiEmotionReport report) => report.State switch
        {
            EmotionReadiness.Blocked => report.Blocker,
            EmotionReadiness.Inert =>
                "Emotion detection is Off on this character, so it never receives anything to feel " +
                "and its face will not move. Set it to Responsive on the Emotion component's " +
                "Advanced section.",
            _ => string.Empty
        };
    }
}
