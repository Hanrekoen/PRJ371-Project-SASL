using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Editor.AI;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor.AI
{
    /// <summary>
    ///     Tells the scene-wide Convai tools what Body Animation makes of a character, so an
    ///     assistant inspecting a scene can see the capability exists before it knows to reach for
    ///     <c>Convai.DiagnoseBodyAnimation</c>.
    /// </summary>
    /// <remarks>
    ///     A projection of <see cref="ConvaiBodyAnimationReport" />, never a second opinion: the
    ///     survey, the diagnose tool, the component inspector and the Body Animation Editor window
    ///     all read the same preflight and the same findings.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiBodyAnimationModuleSurveyor : IConvaiModuleSurveyor
    {
        static ConvaiBodyAnimationModuleSurveyor() =>
            ConvaiModuleSurveyRegistry.Register(new ConvaiBodyAnimationModuleSurveyor());

        public string ModuleId => ModuleIds.BodyAnimation;

        public string DisplayName => "Body Animation";

        public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
        {
            ConvaiBodyAnimationReport report = ConvaiBodyAnimationReport.For(characterRoot);

            if (!report.IsPresent)
            {
                return new ConvaiModuleSurveyResult(
                    ModuleId, DisplayName, ConvaiCapabilityReadiness.NotInstalled,
                    report.Summary, string.Empty,
                    new[]
                    {
                        new ConvaiModuleSurveyFinding(
                            ConvaiModuleFindingSeverity.Info, "Body Animation",
                            "This character has no Body Animation component, so it stands perfectly " +
                            "still — no idle, no talking gestures. Add it with Add Component → " +
                            "Convai → Embodiment → Body Animation, and it idles and gestures as soon " +
                            "as you press Play.")
                    });
            }

            List<BodyAnimationTroubleshooterFinding> findings = report.Findings;
            var surveyed = new List<ConvaiModuleSurveyFinding>(findings.Count);
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity == BodyAnimationTroubleshooterSeverity.Ok) continue;
                surveyed.Add(new ConvaiModuleSurveyFinding(
                    Translate(findings[i].Severity), findings[i].Title, findings[i].Message));
            }

            return new ConvaiModuleSurveyResult(
                ModuleId, DisplayName, Translate(report.State), report.Summary,
                DescribeBlocker(report), surveyed);
        }

        /// <summary>
        ///     This module's own readiness in the layer-wide vocabulary. <c>NeedsContent</c> becomes
        ///     <c>Inert</c> rather than earning a fifth state: to the layer, "set up, unblocked, and
        ///     still nothing happens" is one answer, and <em>why</em> travels in the blocker line.
        ///     <c>Convai.DiagnoseBodyAnimation</c> still reports <c>NeedsContent</c> verbatim, which
        ///     is where the distinction matters.
        /// </summary>
        private static ConvaiCapabilityReadiness Translate(BodyAnimationReadiness readiness) => readiness switch
        {
            BodyAnimationReadiness.NotInstalled => ConvaiCapabilityReadiness.NotInstalled,
            BodyAnimationReadiness.Blocked => ConvaiCapabilityReadiness.Blocked,
            BodyAnimationReadiness.NeedsContent => ConvaiCapabilityReadiness.Inert,
            _ => ConvaiCapabilityReadiness.Working
        };

        private static string DescribeBlocker(in ConvaiBodyAnimationReport report) => report.State switch
        {
            BodyAnimationReadiness.Blocked => report.Blocker,
            BodyAnimationReadiness.NeedsContent =>
                "No animation content is assigned, so this character stays still. Assign an " +
                "Animation Set on the Body Animation component, or build one in the Body Animation " +
                "Editor window.",
            _ => string.Empty
        };

        private static ConvaiModuleFindingSeverity Translate(BodyAnimationTroubleshooterSeverity severity) =>
            severity switch
            {
                BodyAnimationTroubleshooterSeverity.Error => ConvaiModuleFindingSeverity.Error,
                BodyAnimationTroubleshooterSeverity.Warning => ConvaiModuleFindingSeverity.Warning,
                BodyAnimationTroubleshooterSeverity.Info => ConvaiModuleFindingSeverity.Info,
                _ => ConvaiModuleFindingSeverity.Ok
            };
    }
}
