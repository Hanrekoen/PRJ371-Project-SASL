using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Editor.AI;
using Convai.Modules.Gaze.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor.AI
{
    /// <summary>
    ///     Tells the scene-wide Convai tools what Gaze makes of a character, so an assistant
    ///     inspecting a scene can see the capability exists before it knows to reach for
    ///     <c>Convai.DiagnoseGaze</c>.
    /// </summary>
    /// <remarks>
    ///     A projection of <see cref="GazeSetupService" /> and <see cref="GazeSetupTroubleshooter" />,
    ///     never a second opinion: the survey, the diagnose tool, the component inspector and the
    ///     Gaze editor window all read the same preflight and the same findings.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiGazeModuleSurveyor : IConvaiModuleSurveyor
    {
        static ConvaiGazeModuleSurveyor() =>
            ConvaiModuleSurveyRegistry.Register(new ConvaiGazeModuleSurveyor());

        public string ModuleId => ModuleIds.Gaze;

        public string DisplayName => "Gaze";

        public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
        {
            var controller = characterRoot != null
                ? characterRoot.GetComponentInChildren<ConvaiGazeController>(true)
                : null;

            if (controller == null)
            {
                return new ConvaiModuleSurveyResult(
                    ModuleId, DisplayName, ConvaiCapabilityReadiness.NotInstalled,
                    "Not on this character — it will never make eye contact.",
                    string.Empty,
                    new[]
                    {
                        new ConvaiModuleSurveyFinding(
                            ConvaiModuleFindingSeverity.Info, "Gaze",
                            "This character has no Gaze component. Add it with " +
                            "Add Component → Convai → Embodiment → Gaze, and it will look at the " +
                            "player as soon as you press Play.")
                    });
            }

            var serialized = new SerializedObject(controller);
            bool autoCreateAnchor = serialized.FindProperty("autoCreatePlayerAnchor")?.boolValue ?? true;

            GazePreflight preflight = GazeSetupService.Inspect(controller);
            GazeSetupInput input = GazeSetupTroubleshooter.GatherFrom(
                controller, serialized.FindProperty("profile"), autoCreateAnchor);
            var findings = new List<GazeSetupFinding>(8);
            GazeSetupTroubleshooter.Evaluate(in input, findings);

            var surveyed = new List<ConvaiModuleSurveyFinding>(findings.Count);
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity == GazeSetupSeverity.Ok) continue;
                surveyed.Add(new ConvaiModuleSurveyFinding(
                    Translate(findings[i].Severity), findings[i].Title, findings[i].Message));
            }

            GazeAnchorReport anchor = GazeSetupService.InspectAnchor(controller);

            // Readiness is exactly the preflight's own verdict, with no inference layered on top.
            // An earlier revision reported "functional, but nothing resolves as the player" as
            // Inert, which reads well and is wrong: Gaze's own setup service calls that character
            // functional, so inventing a second verdict here made this survey and
            // Convai.DiagnoseGaze disagree about the same character.
            //
            // The anchorless case is not lost — it is what the summary says, and the module's own
            // findings carry it. It is a scene that has nothing to look at yet, not a broken
            // character.
            bool blocked = !preflight.IsFunctional;

            string blockerDetail = blocked
                ? preflight.TryGetBlocker(out GazeCheck blocker)
                    ? blocker.Detail
                    : "Cannot run on this character yet."
                : string.Empty;

            string summary = blocked
                ? blockerDetail
                : anchor.Anchor != null
                    ? $"Watching '{anchor.Anchor.name}'."
                    : "Working, but nothing in this scene resolves as the player yet.";

            return new ConvaiModuleSurveyResult(
                ModuleId, DisplayName,
                blocked ? ConvaiCapabilityReadiness.Blocked : ConvaiCapabilityReadiness.Working,
                summary, blockerDetail, surveyed);
        }

        private static ConvaiModuleFindingSeverity Translate(GazeSetupSeverity severity) => severity switch
        {
            GazeSetupSeverity.Error => ConvaiModuleFindingSeverity.Error,
            GazeSetupSeverity.Warning => ConvaiModuleFindingSeverity.Warning,
            GazeSetupSeverity.Info => ConvaiModuleFindingSeverity.Info,
            _ => ConvaiModuleFindingSeverity.Ok
        };
    }
}
