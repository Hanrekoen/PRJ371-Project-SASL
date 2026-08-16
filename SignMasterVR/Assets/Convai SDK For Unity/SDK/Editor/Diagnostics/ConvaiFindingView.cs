using Convai.Editor.AI;
using UnityEditor;
using UnityEngine;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.Diagnostics
{
    /// <summary>What the user asked a finding row to do. Nothing else about a row is interactive.</summary>
    internal enum ConvaiFindingCommandKind
    {
        None,
        Fix,
        Locate,
        Open,
        Docs
    }

    /// <summary>
    ///     A press on a finding row, returned rather than executed.
    /// </summary>
    /// <remarks>
    ///     Rows report; the window acts, after the layout pass is over. Running a fix from inside the
    ///     draw call mutates the very scene the pass is measuring, so the Repaint event lays out
    ///     against content the Layout event never saw — the classic IMGUI "layout mismatch" fault, and
    ///     the reason a hand-rolled row that fixes in place has to <c>break</c> out of its own loop.
    ///     Returning the command makes that whole class of bug unrepresentable.
    /// </remarks>
    internal readonly struct ConvaiFindingCommand
    {
        internal ConvaiFindingCommand(ConvaiFindingCommandKind kind, ConvaiSetupFinding finding)
        {
            Kind = kind;
            Finding = finding;
        }

        internal ConvaiFindingCommandKind Kind { get; }

        internal ConvaiSetupFinding Finding { get; }

        internal bool HasCommand => Kind != ConvaiFindingCommandKind.None;
    }

    /// <summary>
    ///     The one way a Convai finding is drawn, and the one place severity and readiness are turned
    ///     into a colour or a glyph.
    /// </summary>
    /// <remarks>
    ///     Every surface that reports setup health renders through this: the Troubleshooter's module
    ///     sections, its scene list, and the header chips that lead to them. When each surface mapped
    ///     severity to a colour itself, the same finding was amber in one place and red in another —
    ///     which is the specific way a status system stops being believed.
    /// </remarks>
    internal static class ConvaiFindingView
    {
        private static readonly GUIContent ShowMeContent = new(
            "Show Me", "Select this in the Hierarchy and highlight it.");

        private static readonly GUIContent LearnMoreContent = new("Learn More", "Open the documentation.");

        /// <summary>Height of a row's action buttons. One value, so a row never looks assembled.</summary>
        private const float ButtonHeight = 22f;

        #region Palette

        /// <summary>The colour a severity is allowed to be.</summary>
        internal static Color Tint(ConvaiModuleFindingSeverity severity) =>
            severity switch
            {
                ConvaiModuleFindingSeverity.Error => Theme.StatusError,
                ConvaiModuleFindingSeverity.Warning => Theme.StatusWarn,
                ConvaiModuleFindingSeverity.Info => Theme.StatusInfo,
                _ => Theme.StatusReady
            };

        /// <summary>The mark a severity is allowed to carry.</summary>
        internal static string Glyph(ConvaiModuleFindingSeverity severity) =>
            severity switch
            {
                ConvaiModuleFindingSeverity.Error => Glyphs.Status.Fail,
                ConvaiModuleFindingSeverity.Warning => Glyphs.Status.Warn,
                ConvaiModuleFindingSeverity.Info => Glyphs.Status.Info,
                _ => Glyphs.Status.Ok
            };

        /// <summary>
        ///     The colour of a whole capability, which is not the same question as the colour of a
        ///     finding.
        /// </summary>
        /// <remarks>
        ///     A capability with nothing to fix is not necessarily healthy: <b>Not set up</b> and
        ///     <b>Nothing will happen yet</b> both report zero issues, and drawing them in the same
        ///     brand green as a working one tells a beginner their character is fine when it will do
        ///     nothing. Absence is muted, inertness is amber, and only <see cref="ConvaiCapabilityReadiness.Working" />
        ///     earns the accent.
        /// </remarks>
        internal static Color Tint(ConvaiSetupHealthResult result)
        {
            if (result.ErrorCount > 0) return Theme.StatusError;
            if (result.WarningCount > 0) return Theme.StatusWarn;

            return result.Readiness switch
            {
                ConvaiCapabilityReadiness.NotInstalled => Theme.TextMuted,
                ConvaiCapabilityReadiness.Blocked => Theme.StatusError,
                ConvaiCapabilityReadiness.Inert => Theme.StatusWarn,
                _ => Theme.Accent
            };
        }

        /// <summary>The colour of a whole character, for a chip or a scene-list row.</summary>
        internal static Color Tint(ConvaiSetupHealthSnapshot snapshot)
        {
            if (snapshot.ErrorCount > 0) return Theme.StatusError;
            if (snapshot.WarningCount > 0 || snapshot.HasInertModule) return Theme.StatusWarn;
            return Theme.StatusReady;
        }

        /// <summary>
        ///     What a capability's section says on its right-hand side when collapsed — one phrase, in
        ///     the shared chip vocabulary, never a code word.
        /// </summary>
        internal static string Summary(ConvaiSetupHealthResult result)
        {
            if (result.IssueCount > 0)
                return result.IssueCount == 1 ? "1 to fix" : $"{result.IssueCount} to fix";

            return result.Readiness switch
            {
                ConvaiCapabilityReadiness.NotInstalled => "Not set up",
                ConvaiCapabilityReadiness.Blocked => "Blocked",
                ConvaiCapabilityReadiness.Inert => "Nothing will happen yet",
                _ => "Ready"
            };
        }

        #endregion

        /// <summary>
        ///     Draws one finding — severity bar, headline, message, and the things the user can do
        ///     about it — and returns what they pressed.
        /// </summary>
        /// <param name="flash">
        ///     Outlines the row, for a finding the user arrived at from a status chip. It is how a deep
        ///     link says "this one", and it is drawn over the panel rather than under it.
        /// </param>
        internal static ConvaiFindingCommand Draw(in ConvaiFindingRowView row, bool flash)
        {
            ConvaiSetupFinding finding = row.Finding;
            var command = new ConvaiFindingCommand(ConvaiFindingCommandKind.None, finding);
            Color severity = Tint(finding.Severity);

            Rect panel = Frame.BeginPanel(severity);

            // Every string here was composed when the report was built, not now: this method runs on
            // every mouse move over the window.
            GUILayout.Label(row.Headline, Styles.CardName);
            GUILayout.Label(row.Message, Styles.BodyWrapped);

            if (row.LocateHint != null)
                GUILayout.Label(row.LocateHint, Styles.MicroLabel);

            if (HasAnyAction(finding))
            {
                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (row.FixButton != null && Button(row.FixButton, 130f))
                        command = new ConvaiFindingCommand(ConvaiFindingCommandKind.Fix, finding);

                    if (row.OpenButton != null && Button(row.OpenButton, 150f))
                        command = new ConvaiFindingCommand(ConvaiFindingCommandKind.Open, finding);

                    if (finding.CanLocate && Button(ShowMeContent, 96f))
                        command = new ConvaiFindingCommand(ConvaiFindingCommandKind.Locate, finding);

                    if (!string.IsNullOrEmpty(finding.DocsUrl) && Button(LearnMoreContent, 100f))
                        command = new ConvaiFindingCommand(ConvaiFindingCommandKind.Docs, finding);
                }
            }

            Frame.EndPanel();

            if (flash && Event.current.type == EventType.Repaint)
                Theme.StrokeRounded(panel, Theme.AccentBright, Tokens.PanelRadius, 2f);

            return command;
        }

        private static bool HasAnyAction(ConvaiSetupFinding finding) =>
            finding.IsFixable || finding.CanOpen || finding.CanLocate || !string.IsNullOrEmpty(finding.DocsUrl);

        private static bool Button(GUIContent content, float minWidth)
        {
            Rect rect = GUILayoutUtility.GetRect(
                content, Styles.GhostButtonLabel,
                GUILayout.Height(ButtonHeight), GUILayout.MinWidth(minWidth));
            bool pressed = Controls.GhostButton(rect, content);
            GUILayout.Space(6f);
            return pressed;
        }
    }
}
