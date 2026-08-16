using System.Collections.Generic;

namespace Convai.Editor.Embodiment.Setup
{
    /// <summary>How much a setup finding matters.</summary>
    internal enum EmbodimentFindingSeverity
    {
        /// <summary>Confirmation that something is correctly set up. Shown, but never blocks.</summary>
        Ok = 0,

        /// <summary>Worth knowing; the character still works.</summary>
        Info = 1,

        /// <summary>The character works, but not as well as it could.</summary>
        Warning = 2,

        /// <summary>The character will not behave correctly until this is fixed.</summary>
        Error = 3
    }

    /// <summary>
    ///     One thing a setup inspector found, and — when it can be fixed automatically — how.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Gaze, Emotion, Body Animation, Body Language and the Actions troubleshooter each grew
    ///         their own structurally identical <c>Severity</c> / <c>FixId</c> / <c>Finding</c> triple
    ///         with no shared base. This is the shared one, so the embodiment layer's setup surfaces
    ///         read the same way and a new module does not add a sixth copy.
    ///     </para>
    ///     <para>
    ///         The fix is a <see cref="System.Action" /> rather than an enum id: the enum forced every
    ///         producer to also write a <c>DescribeFix</c>/<c>ApplyFix</c> switch far from the code
    ///         that detected the problem, which is where those switches drifted out of sync.
    ///     </para>
    /// </remarks>
    internal readonly struct EmbodimentFinding
    {
        private EmbodimentFinding(
            string id,
            EmbodimentFindingSeverity severity,
            string title,
            string message,
            string fixLabel,
            System.Action fix)
        {
            Id = id;
            Severity = severity;
            Title = title;
            Message = message;
            FixLabel = fixLabel;
            Fix = fix;
        }

        /// <summary>Stable dotted id, e.g. <c>preset.slot-unknown-module</c>. Used by tests, not shown.</summary>
        public string Id { get; }

        public EmbodimentFindingSeverity Severity { get; }

        /// <summary>Short plain-English headline, e.g. "No gaze target".</summary>
        public string Title { get; }

        /// <summary>What is wrong and what it costs the user, in plain product language.</summary>
        public string Message { get; }

        /// <summary>Button text for the one-click fix, or <c>null</c> when there is none.</summary>
        public string FixLabel { get; }

        /// <summary>The one-click fix, or <c>null</c>. Runs inside a single undo group.</summary>
        public System.Action Fix { get; }

        /// <summary>Whether this finding offers a one-click fix.</summary>
        public bool CanFix => Fix != null && !string.IsNullOrEmpty(FixLabel);

        public static EmbodimentFinding Ok(string id, string title, string message = null) =>
            new(id, EmbodimentFindingSeverity.Ok, title, message, null, null);

        /// <summary>
        ///     Something worth knowing. May still offer a fix — "add the entry for this feature" is
        ///     informational, not a problem, but it is still a one-click action.
        /// </summary>
        public static EmbodimentFinding Info(
            string id, string title, string message, string fixLabel = null, System.Action fix = null) =>
            new(id, EmbodimentFindingSeverity.Info, title, message, fixLabel, fix);

        public static EmbodimentFinding Warning(
            string id, string title, string message, string fixLabel = null, System.Action fix = null) =>
            new(id, EmbodimentFindingSeverity.Warning, title, message, fixLabel, fix);

        public static EmbodimentFinding Error(
            string id, string title, string message, string fixLabel = null, System.Action fix = null) =>
            new(id, EmbodimentFindingSeverity.Error, title, message, fixLabel, fix);
    }

    /// <summary>
    ///     The result of inspecting one thing's setup: an ordered list of findings plus the
    ///     three-state verdict every embodiment inspector header shows.
    /// </summary>
    internal readonly struct EmbodimentSetupReport
    {
        private readonly IReadOnlyList<EmbodimentFinding> _findings;

        public EmbodimentSetupReport(IReadOnlyList<EmbodimentFinding> findings)
        {
            _findings = findings ?? System.Array.Empty<EmbodimentFinding>();
        }

        /// <summary>
        ///     The findings, never <c>null</c>.
        /// </summary>
        /// <remarks>
        ///     Guarded through the property rather than only in the constructor, because a
        ///     <c>default(EmbodimentSetupReport)</c> — which is how callers spell "nothing to report
        ///     here", for instance a character with no preset — bypasses the constructor entirely and
        ///     would otherwise leave every member on this struct throwing.
        /// </remarks>
        public IReadOnlyList<EmbodimentFinding> Findings =>
            _findings ?? System.Array.Empty<EmbodimentFinding>();

        /// <summary>The worst severity present, or <see cref="EmbodimentFindingSeverity.Ok" />.</summary>
        public EmbodimentFindingSeverity WorstSeverity
        {
            get
            {
                EmbodimentFindingSeverity worst = EmbodimentFindingSeverity.Ok;
                for (int i = 0; i < Findings.Count; i++)
                    if (Findings[i].Severity > worst) worst = Findings[i].Severity;
                return worst;
            }
        }

        /// <summary>Whether anything here stops the character behaving correctly.</summary>
        public bool HasBlocker => WorstSeverity == EmbodimentFindingSeverity.Error;

        /// <summary>Whether any finding offers a one-click fix.</summary>
        public bool HasFixes
        {
            get
            {
                for (int i = 0; i < Findings.Count; i++)
                    if (Findings[i].CanFix) return true;
                return false;
            }
        }

        /// <summary>
        ///     The word shown in the inspector header: <c>Ready</c>, <c>Needs Attention</c>, or
        ///     <c>Not Set Up</c>.
        /// </summary>
        public string HeaderStatus => WorstSeverity switch
        {
            EmbodimentFindingSeverity.Error => "Not Set Up",
            EmbodimentFindingSeverity.Warning => "Needs Attention",
            _ => "Ready"
        };
    }
}
