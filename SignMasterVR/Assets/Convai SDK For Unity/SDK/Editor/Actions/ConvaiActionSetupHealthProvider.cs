using System.Collections.Generic;
using Convai.Editor.AI;
using Convai.Editor.Diagnostics;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Reports what Actions makes of a character to every shared Convai surface — the
    ///     Troubleshooter window, the inspector status chips, and the MCP tools.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A projection of <see cref="ConvaiActionSetupReport" /> and nothing else. It runs no
    ///         check of its own: if this provider and the Action Troubleshooter could disagree about a
    ///         character, this class would be the bug. That is not a style preference — the same
    ///         character once reported "1 to fix" in its inspector and "5 To Fix" in the
    ///         Troubleshooter, and one engine behind every surface is what ended it.
    ///     </para>
    ///     <para>
    ///         Actions was the one capability with a real check engine and no registration at all, so
    ///         until now every scene-wide tool was blind to the most common way a character fails to
    ///         do anything: no dispatcher, so the backend's actions arrive and nothing runs them.
    ///     </para>
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class ConvaiActionSetupHealthProvider : IConvaiSetupHealthProvider
    {
        /// <summary>
        ///     Actions comes first in a report. When a character does nothing at all, this is the
        ///     module that most often explains why, and a beginner reads a report top-down.
        /// </summary>
        private const int DisplayOrder = 10;

        static ConvaiActionSetupHealthProvider() =>
            ConvaiSetupHealthRegistry.Register(new ConvaiActionSetupHealthProvider());

        public string ModuleId => "convai.actions";

        public string DisplayName => "Actions";

        public int Order => DisplayOrder;

        /// <summary>
        ///     Any Convai Character. Actions is not an opt-in module: a character with no action
        ///     components has a real, reportable answer ("this character cannot do anything yet"),
        ///     which is exactly what a beginner needs to be told.
        /// </summary>
        public bool AppliesTo(GameObject characterRoot) =>
            characterRoot != null && characterRoot.GetComponent<ConvaiCharacter>() != null;

        public ConvaiSetupHealthResult Inspect(GameObject characterRoot)
        {
            var character = characterRoot != null ? characterRoot.GetComponent<ConvaiCharacter>() : null;
            if (character == null)
                return ConvaiSetupHealthResult.None(ModuleId, DisplayName);

            ConvaiActionSetupReport report = ConvaiActionSetupReport.Run(character);
            var source = characterRoot.GetComponent<ConvaiActionConfigSource>();

            var findings = new List<ConvaiSetupFinding>(report.Findings.Count);
            for (int i = 0; i < report.Findings.Count; i++)
                findings.Add(ToFinding(report.Findings[i]));

            int actionCount = CountActions(source);
            ConvaiCapabilityReadiness readiness = ResolveReadiness(source, report, actionCount, out string blocker);

            return new ConvaiSetupHealthResult(
                ModuleId, DisplayName, readiness, BuildSummary(actionCount, report), blocker, findings, DisplayOrder);
        }

        private static ConvaiSetupFinding ToFinding(ConvaiActionTroubleshooterFinding finding) =>
            new(
                finding.Id,
                finding.Severity switch
                {
                    ConvaiActionTroubleshooterSeverity.Error => ConvaiModuleFindingSeverity.Error,
                    ConvaiActionTroubleshooterSeverity.Warning => ConvaiModuleFindingSeverity.Warning,
                    ConvaiActionTroubleshooterSeverity.Info => ConvaiModuleFindingSeverity.Info,
                    _ => ConvaiModuleFindingSeverity.Ok
                },
                finding.Title,
                finding.Message,
                finding.FixLabel,
                finding.Fix,
                locate: finding.Locate,
                openLabel: finding.OpenLabel,
                open: finding.Open);

        /// <summary>
        ///     Which of the four readiness states this character is in, and the one sentence naming
        ///     what stops it. Authored-but-broken and never-authored are deliberately different
        ///     answers: the first needs a fix, the second needs the Actions Editor.
        /// </summary>
        private static ConvaiCapabilityReadiness ResolveReadiness(
            ConvaiActionConfigSource source, ConvaiActionSetupReport report, int actionCount, out string blocker)
        {
            if (source == null)
            {
                blocker = "This character has no action setup, so it cannot do anything the " +
                          "conversation asks for.";
                return ConvaiCapabilityReadiness.NotInstalled;
            }

            if (report.ErrorCount > 0)
            {
                // No "the Troubleshooter lists what" here: this sentence is read inside the
                // Troubleshooter, with the list directly beneath it. A blocker that points at the page
                // it is printed on tells the user nothing and costs them a line of reading.
                blocker = report.ErrorCount == 1
                    ? "One thing below stops this character running its actions."
                    : $"{report.ErrorCount} things below stop this character running its actions.";
                return ConvaiCapabilityReadiness.Blocked;
            }

            if (actionCount == 0)
            {
                blocker = "No actions are set up yet, so the character will talk but never do " +
                          "anything. Add one in the Actions Editor.";
                return ConvaiCapabilityReadiness.Inert;
            }

            blocker = string.Empty;
            return ConvaiCapabilityReadiness.Working;
        }

        private static string BuildSummary(int actionCount, ConvaiActionSetupReport report)
        {
            string actions = actionCount == 1 ? "1 action" : $"{actionCount} actions";
            return report.IsHealthy ? actions : $"{actions} · {report.IssueCount} to fix";
        }

        private static int CountActions(ConvaiActionConfigSource source)
        {
            if (source == null)
                return 0;

            IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions();
            return definitions?.Count ?? 0;
        }
    }
}
