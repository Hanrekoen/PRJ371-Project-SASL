using System.Collections.Generic;
using Convai.Editor.AI;
using Convai.Editor.UI;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;

namespace Convai.Editor.Diagnostics
{
    /// <summary>
    ///     Everything the Troubleshooter draws, in the form it draws it, built once per report.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this class exists.</b> An IMGUI window repaints on every mouse move — tens of
    ///         times a second while a user does nothing but hover. The report behind it changes at
    ///         human speed. Composing headlines, counts and freshness text inside the draw call
    ///         therefore produced a fresh string, and a fresh <see cref="GUIContent" /> to hold it, for
    ///         every finding on every repaint: garbage measured in kilobytes per second of hovering,
    ///         for text that had not changed since the last edit.
    ///     </para>
    ///     <para>
    ///         So the split is model → view model → view. The registry answers <em>what is true</em>
    ///         (cached, throttled, scene-aware). This answers <em>what it says on screen</em>, rebuilt
    ///         only when the report is. The window draws it and allocates nothing to do so.
    ///     </para>
    ///     <para>
    ///         The one thing deliberately left out is the freshness line, which is the only text that
    ///         changes without the report changing. It is rebuilt at most once a second by
    ///         <see cref="RefreshFreshness" /> rather than per repaint.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiTroubleshooterView
    {
        private static readonly ConvaiTroubleshooterModuleView[] NoModules = System.Array.Empty<ConvaiTroubleshooterModuleView>();
        private static readonly ConvaiFindingRowView[] NoRows = System.Array.Empty<ConvaiFindingRowView>();

        private double _freshnessBuiltAt = double.NegativeInfinity;

        private ConvaiTroubleshooterView(
            ConvaiSetupHealthSnapshot snapshot,
            IReadOnlyList<ConvaiTroubleshooterModuleView> modules,
            IReadOnlyList<ConvaiFindingRowView> passed,
            GUIContent chip,
            Color chipTint,
            GUIContent fixAllButton)
        {
            Snapshot = snapshot;
            Modules = modules;
            Passed = passed;
            Chip = chip;
            ChipTint = chipTint;
            FixAllButton = fixAllButton;
            PassedCountLabel = passed.Count.ToString();
            Freshness = new GUIContent();
        }

        internal ConvaiSetupHealthSnapshot Snapshot { get; }

        /// <summary>One entry per capability that had something to say, in report order.</summary>
        internal IReadOnlyList<ConvaiTroubleshooterModuleView> Modules { get; }

        /// <summary>
        ///     Everything that is not work — passes and informational notes — for the collapsed
        ///     "Checked And Fine" list.
        /// </summary>
        internal IReadOnlyList<ConvaiFindingRowView> Passed { get; }

        internal string PassedCountLabel { get; }

        /// <summary>The hero chip — "3 to fix", or the healthy chip when there is nothing to say.</summary>
        internal GUIContent Chip { get; }

        internal Color ChipTint { get; }

        /// <summary>The footer's Fix All button, or null when fewer than two fixes exist.</summary>
        internal GUIContent FixAllButton { get; }

        /// <summary>"Checked just now" and friends. Kept current by <see cref="RefreshFreshness" />.</summary>
        internal GUIContent Freshness { get; }

        /// <summary>The empty view, for "no character picked".</summary>
        internal static ConvaiTroubleshooterView Empty { get; } = new(
            ConvaiSetupHealthSnapshot.Empty, NoModules, NoRows, null, Color.white, null);

        internal static ConvaiTroubleshooterView Build(ConvaiSetupHealthSnapshot snapshot)
        {
            if (snapshot?.Character == null)
                return Empty;

            var modules = new List<ConvaiTroubleshooterModuleView>(snapshot.Results.Count);
            var passed = new List<ConvaiFindingRowView>();

            for (int i = 0; i < snapshot.Results.Count; i++)
            {
                ConvaiSetupHealthResult result = snapshot.Results[i];
                modules.Add(ConvaiTroubleshooterModuleView.Build(result));

                IReadOnlyList<ConvaiSetupFinding> findings = result.Findings;
                for (int f = 0; f < findings.Count; f++)
                {
                    // Everything that is not work: passes and informational notes alike. An Info
                    // finding is a suggestion rather than a fault, so it must not reach the issue
                    // count — but it is still something the module chose to say, and matching only
                    // Ok here left it drawn nowhere at all.
                    if (!findings[f].IsIssue)
                        passed.Add(ConvaiFindingRowView.Build(findings[f]));
                }
            }

            GUIContent chip = snapshot.IsHealthy
                ? new GUIContent(
                    $"{Glyphs.Status.Ok} Nothing to fix", "Every check on this character passed.")
                : new GUIContent(
                    snapshot.IssueCount == 1 ? "1 to fix" : $"{snapshot.IssueCount} to fix",
                    "Findings on this character that still need attention.");

            GUIContent fixAll = snapshot.FixableCount > 1
                ? new GUIContent(
                    $"{Glyphs.Status.Fixable} Fix All ({snapshot.FixableCount})",
                    "Applies every one-click fix on this character as a single undo step.")
                : null;

            return new ConvaiTroubleshooterView(
                snapshot, modules, passed, chip, ConvaiFindingView.Tint(snapshot), fixAll);
        }

        /// <summary>
        ///     Updates the freshness line if a second has passed since it was last written. The only
        ///     text here that ages, and the only one allowed to be rebuilt outside a report change.
        /// </summary>
        internal void RefreshFreshness(double now)
        {
            if (Snapshot.Character == null || now - _freshnessBuiltAt < 1d)
                return;

            _freshnessBuiltAt = now;
            double age = now - Snapshot.CheckedAt;
            Freshness.text = double.IsInfinity(age) || age < 0d
                ? string.Empty
                : age < 5d
                    ? "Checked just now"
                    : age < 60d
                        ? $"Checked {(int)age} seconds ago"
                        : $"Checked {(int)(age / 60d)} min ago";
        }
    }

    /// <summary>One capability's section, with every string it draws already composed.</summary>
    internal sealed class ConvaiTroubleshooterModuleView
    {
        private static readonly ConvaiFindingRowView[] NoRows = System.Array.Empty<ConvaiFindingRowView>();

        private ConvaiTroubleshooterModuleView(
            ConvaiSetupHealthResult result,
            GUIContent title,
            string summary,
            Color accent,
            IReadOnlyList<ConvaiFindingRowView> issues,
            GUIContent blocker,
            GUIContent restingState,
            GUIContent fixTheseButton,
            int fixableShown)
        {
            Result = result;
            Title = title;
            Summary = summary;
            Accent = accent;
            Issues = issues;
            Blocker = blocker;
            RestingState = restingState;
            FixTheseButton = fixTheseButton;
            FixableShown = fixableShown;
        }

        internal ConvaiSetupHealthResult Result { get; }

        internal GUIContent Title { get; }

        /// <summary>The right-aligned phrase on the section header — "3 to fix", "Ready".</summary>
        internal string Summary { get; }

        internal Color Accent { get; }

        /// <summary>The findings this section shows: errors and warnings, never the passes.</summary>
        internal IReadOnlyList<ConvaiFindingRowView> Issues { get; }

        /// <summary>One sentence naming what stops this capability, or null when nothing does.</summary>
        internal GUIContent Blocker { get; }

        /// <summary>What the section says when it has no issues to list.</summary>
        internal GUIContent RestingState { get; }

        /// <summary>The section's batch-fix button, or null when it would offer fewer than two.</summary>
        internal GUIContent FixTheseButton { get; }

        /// <summary>
        ///     How many of the findings <em>on screen</em> carry a fix — never the report's total. A
        ///     button offering three fixes above two visible findings cannot be checked before it is
        ///     pressed.
        /// </summary>
        internal int FixableShown { get; }

        internal static ConvaiTroubleshooterModuleView Build(ConvaiSetupHealthResult result)
        {
            var issues = new List<ConvaiFindingRowView>();
            var fixableShown = 0;

            IReadOnlyList<ConvaiSetupFinding> findings = result.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                ConvaiSetupFinding finding = findings[i];
                if (!finding.IsIssue)
                    continue;

                issues.Add(ConvaiFindingRowView.Build(finding));
                if (finding.IsFixable)
                    fixableShown++;
            }

            bool showBlocker =
                !string.IsNullOrEmpty(result.Blocker) &&
                result.Readiness != ConvaiCapabilityReadiness.Working;

            GUIContent resting = issues.Count > 0
                ? null
                : new GUIContent(
                    result.Readiness == ConvaiCapabilityReadiness.Working
                        ? "Everything this capability needs is in place."
                        : result.Summary);

            GUIContent fixThese = fixableShown > 1
                ? new GUIContent(
                    $"{Glyphs.Status.Fixable} Fix These ({fixableShown})",
                    "Applies every one-click fix shown here as a single undo step.")
                : null;

            return new ConvaiTroubleshooterModuleView(
                result,
                new GUIContent(result.DisplayName),
                ConvaiFindingView.Summary(result),
                ConvaiFindingView.Tint(result),
                issues.Count == 0 ? NoRows : issues,
                showBlocker ? new GUIContent(result.Blocker) : null,
                resting,
                fixThese,
                fixableShown);
        }
    }

    /// <summary>
    ///     One character in the scene list: its name, its worst finding and its colour, all resolved
    ///     when the list was built rather than while it is being drawn.
    /// </summary>
    internal readonly struct ConvaiTroubleshooterSceneRow
    {
        private ConvaiTroubleshooterSceneRow(
            GameObject character, GUIContent name, GUIContent worst, GUIContent pill, Color tint)
        {
            Character = character;
            Name = name;
            Worst = worst;
            Pill = pill;
            Tint = tint;
        }

        internal GameObject Character { get; }

        internal GUIContent Name { get; }

        internal GUIContent Worst { get; }

        internal GUIContent Pill { get; }

        internal Color Tint { get; }

        /// <summary>
        ///     Surveys every Convai Character in the open scene. A full object sweep plus a report per
        ///     character — which is exactly why the window runs this behind a throttle and never from a
        ///     repaint.
        /// </summary>
        internal static IReadOnlyList<ConvaiTroubleshooterSceneRow> BuildAll()
        {
            ConvaiCharacter[] characters =
                ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);

            var rows = new List<ConvaiTroubleshooterSceneRow>(characters.Length);
            for (int i = 0; i < characters.Length; i++)
            {
                ConvaiCharacter character = characters[i];
                if (character == null)
                    continue;

                ConvaiSetupHealthSnapshot snapshot = ConvaiSetupHealthRegistry.Get(character.gameObject);
                rows.Add(new ConvaiTroubleshooterSceneRow(
                    character.gameObject,
                    new GUIContent(character.name),
                    new GUIContent(WorstLine(snapshot)),
                    new GUIContent(
                        snapshot.IsHealthy
                            ? "Nothing to fix"
                            : snapshot.IssueCount == 1 ? "1 to fix" : $"{snapshot.IssueCount} to fix"),
                    ConvaiFindingView.Tint(snapshot)));
            }

            return rows;
        }

        /// <summary>The worst thing this character has to say, in one line, without expanding anything.</summary>
        private static string WorstLine(ConvaiSetupHealthSnapshot snapshot)
        {
            ConvaiSetupFinding worst = default;
            var found = false;

            for (int i = 0; i < snapshot.Results.Count; i++)
            {
                IReadOnlyList<ConvaiSetupFinding> findings = snapshot.Results[i].Findings;
                for (int f = 0; f < findings.Count; f++)
                {
                    ConvaiSetupFinding finding = findings[f];
                    if (!finding.IsIssue)
                        continue;

                    if (!found || finding.Severity > worst.Severity)
                    {
                        worst = finding;
                        found = true;
                    }
                }
            }

            if (found)
                return worst.Title;

            return snapshot.HasInertModule
                ? "Set up, but a capability will do nothing yet — open it to see which."
                : "Everything checked out.";
        }
    }

    /// <summary>One finding with its text already composed, ready to draw.</summary>
    internal readonly struct ConvaiFindingRowView
    {
        private ConvaiFindingRowView(
            ConvaiSetupFinding finding,
            GUIContent headline,
            GUIContent message,
            GUIContent locateHint,
            GUIContent fixButton,
            GUIContent openButton)
        {
            Finding = finding;
            Headline = headline;
            Message = message;
            LocateHint = locateHint;
            FixButton = fixButton;
            OpenButton = openButton;
        }

        internal ConvaiSetupFinding Finding { get; }

        internal GUIContent Headline { get; }

        internal GUIContent Message { get; }

        internal GUIContent LocateHint { get; }

        internal GUIContent FixButton { get; }

        internal GUIContent OpenButton { get; }

        internal static ConvaiFindingRowView Build(ConvaiSetupFinding finding) =>
            new(
                finding,
                new GUIContent($"{ConvaiFindingView.Glyph(finding.Severity)}  {finding.Title}"),
                new GUIContent(finding.Message),
                string.IsNullOrEmpty(finding.LocateHint) ? null : new GUIContent(finding.LocateHint),
                finding.IsFixable
                    ? new GUIContent(finding.FixLabel, finding.FixPreview ?? string.Empty)
                    : null,
                finding.CanOpen
                    ? new GUIContent(finding.OpenLabel, finding.OpenHint ?? string.Empty)
                    : null);
    }
}
