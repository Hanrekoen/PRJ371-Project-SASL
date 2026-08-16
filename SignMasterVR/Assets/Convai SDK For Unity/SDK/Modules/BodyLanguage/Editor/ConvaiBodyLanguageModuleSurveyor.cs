using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Editor.AI;
using UnityEditor;
using UnityEngine;

// This file lives in the module's core editor assembly, not in its Editor.AI assembly where it
// was written, because that assembly is gated behind the CONVAI_UNITY_MCP define constraint and
// does not exist unless the Unity AI Assistant package is installed. The scene-wide Convai
// tools discover a module through the surveyor it registers, so leaving it there made Body
// Language invisible to them for every user without that package — the exact failure the
// survey vocabulary was moved into the core editor assembly to prevent. The namespace is
// deliberately unchanged, matching that move.
namespace Convai.Modules.BodyLanguage.Editor.AI
{
    /// <summary>
    ///     Tells the scene-wide Convai tools what Body Language makes of a character, so an assistant
    ///     inspecting a scene can see the capability exists before it knows to reach for
    ///     <c>Convai.DiagnoseBodyLanguage</c>.
    /// </summary>
    /// <remarks>
    ///     A projection of <see cref="BodyLanguageSetupService" />, never a second opinion: the
    ///     survey, the three tools and the component inspector all read the same preflight.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiBodyLanguageModuleSurveyor : IConvaiModuleSurveyor
    {
        static ConvaiBodyLanguageModuleSurveyor() =>
            ConvaiModuleSurveyRegistry.Register(new ConvaiBodyLanguageModuleSurveyor());

        public string ModuleId => ModuleIds.BodyLanguage;

        public string DisplayName => "Body Language";

        public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
        {
            ConvaiBodyLanguageReport report = ConvaiBodyLanguageReport.For(characterRoot);

            if (!report.IsPresent)
            {
                return new ConvaiModuleSurveyResult(
                    ModuleId, DisplayName, ConvaiCapabilityReadiness.NotInstalled,
                    report.Summary, string.Empty,
                    new[]
                    {
                        new ConvaiModuleSurveyFinding(
                            ConvaiModuleFindingSeverity.Info, "Body Language",
                            "This character has no Body Language component. Add it with " +
                            "Add Component → Convai → Embodiment → Body Language, and it will breathe, " +
                            "shift its weight and gesture as it talks as soon as you press Play. It " +
                            "needs no profile and no animation clips.")
                    });
            }

            IReadOnlyList<BodyLanguageCheck> checks = report.Preflight.Checks;
            var findings = new List<ConvaiModuleSurveyFinding>(checks.Count);
            for (int i = 0; i < checks.Count; i++)
            {
                BodyLanguageCheck check = checks[i];

                // Ok rows are working, and Optional rows are a legitimate rig — surfacing either as a
                // survey finding would report a healthy character as having five problems.
                if (check.State is BodyLanguageCheckState.Ok or BodyLanguageCheckState.Optional) continue;

                findings.Add(new ConvaiModuleSurveyFinding(
                    check.State == BodyLanguageCheckState.Blocked
                        ? ConvaiModuleFindingSeverity.Error
                        : ConvaiModuleFindingSeverity.Info,
                    check.Label,
                    check.Detail));
            }

            return new ConvaiModuleSurveyResult(
                ModuleId, DisplayName, Translate(report.State), report.Summary,
                report.State == BodyLanguageReadiness.Blocked ? report.Blocker : string.Empty,
                findings);
        }

        /// <summary>
        ///     This module's own readiness in the layer-wide vocabulary. There is deliberately no
        ///     inert case: Body Language needs no clips and no profile, so a character that is
        ///     present and unblocked is working.
        /// </summary>
        private static ConvaiCapabilityReadiness Translate(BodyLanguageReadiness readiness) => readiness switch
        {
            BodyLanguageReadiness.NotInstalled => ConvaiCapabilityReadiness.NotInstalled,
            BodyLanguageReadiness.Blocked => ConvaiCapabilityReadiness.Blocked,
            _ => ConvaiCapabilityReadiness.Working
        };
    }
}
