using System;
using System.Collections.Generic;
using Convai.Editor.AI;
using UnityEngine;

namespace Convai.Editor.Diagnostics
{
    /// <summary>
    ///     One thing a module noticed about a character, everything needed to act on it, and nothing
    ///     about how it is drawn.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> Every Convai capability that can check a character — Actions,
    ///         Embodiment, Gaze, Emotion, Body Animation, profile validation and the module survey —
    ///         reports what it finds in this one shape. A shared surface can therefore show any
    ///         module's finding without knowing the module exists, which is what lets a status chip
    ///         lead somewhere instead of stating a problem the user has no way to read.
    ///     </para>
    ///     <para>
    ///         <b>The contract this struct is judged by.</b> An <see cref="ConvaiModuleFindingSeverity.Error" />
    ///         finding must carry a <see cref="Fix" /> or a <see cref="Locate" /> target — an error a
    ///         user cannot act on is a defect in the finding, not a finding. <see cref="Message" />
    ///         never ends at the fact: it says what is wrong, what it costs, and what to do, in the
    ///         words the Inspector uses.
    ///     </para>
    ///     <para>
    ///         <b><see cref="Fix" /> is a pure mutation.</b> It records its own Undo and never
    ///         re-evaluates — re-running the checks afterwards belongs to the caller, so a Fix All can
    ///         apply a batch inside one Undo group and re-check exactly once. This is the discipline
    ///         the Action Troubleshooter already had; it is now the rule for every module.
    ///     </para>
    /// </remarks>
    public readonly struct ConvaiSetupFinding
    {
        public ConvaiSetupFinding(
            string id,
            ConvaiModuleFindingSeverity severity,
            string title,
            string message,
            string fixLabel = null,
            Action fix = null,
            string fixPreview = null,
            UnityEngine.Object locate = null,
            string locateHint = null,
            string docsUrl = null,
            string openLabel = null,
            Action open = null,
            string openHint = null)
        {
            Id = id;
            Severity = severity;
            Title = title;
            Message = message;
            FixLabel = fixLabel;
            Fix = fix;
            FixPreview = fixPreview;
            Locate = locate;
            LocateHint = locateHint;
            DocsUrl = docsUrl;
            OpenLabel = openLabel;
            Open = open;
            OpenHint = openHint;
        }

        /// <summary>
        ///     Stable dotted id, e.g. <c>convai.actions.dispatcher.missing</c>. Never localised and
        ///     never renamed: it is what a fix, a test, an MCP response and a support thread all name
        ///     when they mean the same problem.
        /// </summary>
        public string Id { get; }

        /// <summary>How serious it is, on the one ladder every module now reports on.</summary>
        public ConvaiModuleFindingSeverity Severity { get; }

        /// <summary>Short label — "No Action Runner". Three words is the budget.</summary>
        public string Title { get; }

        /// <summary>What is wrong, what it costs, and what to do. Two sentences is the budget.</summary>
        public string Message { get; }

        /// <summary>Verb phrase for the fix button — "Add Action Runner". Null when there is no fix.</summary>
        public string FixLabel { get; }

        /// <summary>
        ///     What the fix will do, in one sentence, shown before it runs. Null when the change is
        ///     small enough that seeing it happen is explanation enough.
        /// </summary>
        public string FixPreview { get; }

        /// <summary>The one-click repair, or null. Undo-recorded, idempotent, never destructive.</summary>
        public Action Fix { get; }

        /// <summary>
        ///     What "Show Me" selects and pings — <b>only</b> when this object genuinely is the subject
        ///     of the finding.
        /// </summary>
        /// <remarks>
        ///     Never a fallback. Pointing at the character because there was nothing better to point at
        ///     is worse than offering nothing: the user is sent to an object that looks correct, and
        ///     concludes the report is wrong about it. A finding whose answer lives in an authoring
        ///     window uses <see cref="Open" /> instead, and a finding with neither simply says what to
        ///     do in its message.
        /// </remarks>
        public UnityEngine.Object Locate { get; }

        /// <summary>Where inside that object to look — "Character Rig → Head Bone".</summary>
        public string LocateHint { get; }

        /// <summary>
        ///     Label for the authoring surface that answers this finding — "Open Actions Editor".
        /// </summary>
        /// <remarks>
        ///     The counterpart to <see cref="Locate" />, for everything a scene object cannot answer.
        ///     "This action has no behavior" is not a question about a GameObject; it is a question
        ///     about what was authored, and the only useful destination is the editor where authoring
        ///     happens, opened at that action.
        /// </remarks>
        public string OpenLabel { get; }

        /// <summary>Opens that surface, focused on this finding's subject. Read-only: it never edits.</summary>
        public Action Open { get; }

        /// <summary>One line naming what the user will find there.</summary>
        public string OpenHint { get; }

        /// <summary>Deep link to the paragraph that explains the concept. Null falls back to the module page.</summary>
        public string DocsUrl { get; }

        /// <summary>Whether this finding counts as work: errors and warnings do, Ok and Info do not.</summary>
        public bool IsIssue =>
            Severity == ConvaiModuleFindingSeverity.Error || Severity == ConvaiModuleFindingSeverity.Warning;

        /// <summary>Whether pressing one button would resolve it.</summary>
        public bool IsFixable => Fix != null;

        /// <summary>Whether the user can be shown where this lives.</summary>
        public bool CanLocate => Locate != null;

        /// <summary>Whether there is an authoring surface to open at this finding's subject.</summary>
        public bool CanOpen => Open != null && !string.IsNullOrEmpty(OpenLabel);
    }

    /// <summary>What one module has to say about one character, in a form every surface can draw.</summary>
    /// <remarks>
    ///     A superset of <see cref="ConvaiModuleSurveyResult" />: same module identity, same
    ///     <see cref="ConvaiCapabilityReadiness" />, same summary and blocker sentence — plus findings
    ///     that carry fixes and navigation. The survey result is derived from this one
    ///     (<see cref="ToSurveyResult" />) so the MCP tools and the editor can never disagree.
    /// </remarks>
    public sealed class ConvaiSetupHealthResult
    {
        private static readonly ConvaiSetupFinding[] NoFindings = Array.Empty<ConvaiSetupFinding>();

        public ConvaiSetupHealthResult(
            string moduleId,
            string displayName,
            ConvaiCapabilityReadiness readiness,
            string summary,
            string blocker = null,
            IReadOnlyList<ConvaiSetupFinding> findings = null,
            int order = 100)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            Readiness = readiness;
            Summary = summary ?? string.Empty;
            Blocker = blocker ?? string.Empty;
            Findings = findings ?? NoFindings;
            Order = order;

            for (int i = 0; i < Findings.Count; i++)
            {
                ConvaiSetupFinding finding = Findings[i];
                if (finding.Severity == ConvaiModuleFindingSeverity.Error) ErrorCount++;
                else if (finding.Severity == ConvaiModuleFindingSeverity.Warning) WarningCount++;
                if (finding.IsFixable) FixableCount++;
            }
        }

        /// <summary>Stable module id, e.g. <c>convai.actions</c>.</summary>
        public string ModuleId { get; }

        /// <summary>What a user calls it, e.g. "Actions".</summary>
        public string DisplayName { get; }

        /// <summary>Where this module sits in a report. Setup-critical modules come first.</summary>
        public int Order { get; }

        /// <summary>How far this character is from actually getting the capability's behaviour.</summary>
        public ConvaiCapabilityReadiness Readiness { get; }

        /// <summary>One line a report can show without expanding anything.</summary>
        public string Summary { get; }

        /// <summary>One sentence naming what stops this working, or empty when nothing does.</summary>
        public string Blocker { get; }

        /// <summary>Everything worth acting on — plus what passed, so a healthy module can prove it.</summary>
        public IReadOnlyList<ConvaiSetupFinding> Findings { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        /// <summary>How many findings carry a one-click fix — what a Fix All would apply.</summary>
        public int FixableCount { get; }

        /// <summary>
        ///     The single number every surface shows as "N to fix": errors plus warnings. Info and Ok
        ///     findings are explanation, not work, and must never inflate it.
        /// </summary>
        public int IssueCount => ErrorCount + WarningCount;

        public bool IsHealthy => IssueCount == 0;

        /// <summary>The empty answer for "this module has nothing to say about this character".</summary>
        public static ConvaiSetupHealthResult None(string moduleId, string displayName) =>
            new(moduleId, displayName, ConvaiCapabilityReadiness.NotInstalled, string.Empty);

        /// <summary>
        ///     The same verdict in the shape the MCP tools already consume, so
        ///     <c>InspectScene</c> and <c>ValidateSetup</c> keep working unchanged and keep agreeing
        ///     with the editor by construction.
        /// </summary>
        public ConvaiModuleSurveyResult ToSurveyResult()
        {
            var findings = new ConvaiModuleSurveyFinding[Findings.Count];
            for (int i = 0; i < Findings.Count; i++)
            {
                ConvaiSetupFinding finding = Findings[i];
                findings[i] = new ConvaiModuleSurveyFinding(finding.Severity, finding.Title, finding.Message);
            }

            return new ConvaiModuleSurveyResult(ModuleId, DisplayName, Readiness, Summary, Blocker, findings);
        }
    }

    /// <summary>
    ///     Lets a module report what it makes of a character — with fixes and navigation — without
    ///     any shared surface having to know the module exists.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The successor to <see cref="IConvaiModuleSurveyor" />, and a superset of it: a provider
    ///         registered here is also reported to every existing survey consumer
    ///         (<see cref="ConvaiSetupHealthRegistry.Register" /> does both), so a module implements
    ///         one interface rather than two.
    ///     </para>
    ///     <para>
    ///         <b>An implementation must be a projection of the module's own setup service, not a
    ///         second opinion about the same character.</b> If a provider and its module's own
    ///         troubleshooter can disagree, the provider is wrong — that disagreement is exactly the
    ///         defect this whole system was built to end.
    ///     </para>
    /// </remarks>
    public interface IConvaiSetupHealthProvider
    {
        /// <summary>Stable module id, e.g. <c>convai.gaze</c>. Identity for registration.</summary>
        string ModuleId { get; }

        /// <summary>What a user calls this module.</summary>
        string DisplayName { get; }

        /// <summary>Display order in a report. Lower comes first; setup-critical modules use 0–20.</summary>
        int Order { get; }

        /// <summary>
        ///     Whether this module has anything to say about <paramref name="characterRoot" />. Must be
        ///     cheap — it runs before the real inspection to keep an untouched character free.
        /// </summary>
        bool AppliesTo(GameObject characterRoot);

        /// <summary>
        ///     Reports what this module makes of <paramref name="characterRoot" />. Read-only: this
        ///     runs inside diagnostic surfaces and must never change a scene.
        /// </summary>
        ConvaiSetupHealthResult Inspect(GameObject characterRoot);
    }
}
