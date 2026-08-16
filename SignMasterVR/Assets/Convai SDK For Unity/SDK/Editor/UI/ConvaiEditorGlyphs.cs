namespace Convai.Editor.UI
{
    /// <summary>
    ///     The section glyphs of the Convai editor design system. A section that means the same thing
    ///     in two modules carries the same mark, so a user who learns the vocabulary once reads every
    ///     Convai inspector faster.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Four shape families, one visual weight.</b> Marks are grouped so a reader can tell
    ///         what kind of section they are looking at before reading the title: <em>discs</em> are
    ///         state and liveness, <em>squares</em> are authored content, <em>diamonds</em> are wiring
    ///         and instructions, <em>triangles</em> are attention and playback, and <em>arrows</em> are
    ///         flow between two things. Within a family the marks share a stroke weight, which is what
    ///         makes a stack of section headers read as one system rather than a pile of symbols.
    ///     </para>
    ///     <para>
    ///         <b>Every mark comes from Geometric Shapes (U+25A0–U+25FF) or Arrows (U+2190–U+21FF).</b>
    ///         Those two blocks are covered completely by the default fonts Unity falls back to on
    ///         Windows, macOS and Linux, and they are drawn as text — so a mark inherits its section's
    ///         accent colour, sits on the label baseline, and stays crisp at any editor DPI.
    ///         Miscellaneous Symbols (the gear at U+2699, the hammer-and-pick at U+2692) and the
    ///         technical crosshairs at U+2315/U+2316 are <b>banned</b>: they are missing from some
    ///         system fonts and, where they do exist, are often substituted by a colour emoji that
    ///         ignores the accent and the baseline. Bare typography such as <c>"!"</c> is banned for
    ///         the same reason it looked wrong — it is a letterform among shapes.
    ///         <see cref="ConvaiEditorGlyphs" /> is guarded on both rules.
    ///     </para>
    ///     <para>
    ///         Pick by <em>meaning</em>, not by shape — that is what keeps the vocabulary learnable.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorGlyphs
    {
        #region Discs — state and liveness

        /// <summary>Live runtime state, only meaningful in Play mode.</summary>
        internal const string Live = "●";

        /// <summary>Eye behaviour — blinks and saccades.</summary>
        internal const string Blink = "◉";

        /// <summary>Camera and capture — a lens the character sees through.</summary>
        internal const string Capture = "◎";

        /// <summary>Visibility and exposure — what the character can see or know about.</summary>
        internal const string Visibility = "◐";

        /// <summary>Contracts, extension points and advanced wiring — open, because it is open for extension.</summary>
        internal const string Contract = "○";

        /// <summary>Discovery, search and scanning the scene.</summary>
        internal const string Discovery = "◌";

        #endregion

        #region Squares — authored content

        /// <summary>Authored content — clips, pools, libraries.</summary>
        internal const string Content = "▣";

        /// <summary>Identity — names, aliases, descriptions.</summary>
        internal const string Identity = "▤";

        /// <summary>Generic section mark, used by the attribute-driven renderer.</summary>
        internal const string Section = "▪";

        #endregion

        #region Diamonds — wiring and instructions

        /// <summary>Tuning and personality — how a feature is configured to behave.</summary>
        internal const string Profile = "◆";

        /// <summary>Animator wiring and rig binding.</summary>
        internal const string Animator = "◈";

        /// <summary>A command the character can be asked to perform.</summary>
        internal const string Command = "◇";

        #endregion

        #region Triangles — attention and playback

        /// <summary>Validation results and things needing attention.</summary>
        internal const string Validation = "▲";

        /// <summary>Something that runs — a behavior, a step, a playback.</summary>
        internal const string Run = "▶";

        /// <summary>Spatial placement — anchors, positions, approach points.</summary>
        internal const string Placement = "▽";

        #endregion

        #region Arrows — flow between two things

        /// <summary>Routing and hand-off between two systems.</summary>
        internal const string Routing = "↔";

        /// <summary>Events and outbound notifications.</summary>
        internal const string Events = "↗";

        /// <summary>Numeric ranges and limits.</summary>
        internal const string Range = "↕";

        /// <summary>Motion and animation behaviour.</summary>
        internal const string Motion = "→";

        /// <summary>
        ///     How the character responds to something — reaction beats, per-emotion responses,
        ///     action outcomes.
        /// </summary>
        /// <remarks>
        ///     Distinct from <see cref="Blink" />, which means eyelids and nothing else. The two are
        ///     easy to confuse on the strength of both words starting with "re-", and a mark that means
        ///     two things stops being a vocabulary.
        /// </remarks>
        internal const string Reaction = "↩";

        #endregion

        /// <summary>
        ///     The four marks that report the outcome of a check — setup doctors, validation rows,
        ///     preflight lists. A separate, deliberately tiny vocabulary from the section glyphs above.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         These are allowed to reuse a shape a section glyph also uses, because they never
        ///         appear in a section header: they sit in a status column beside a check's name, where
        ///         the reader is asking "did this pass?", not "what kind of section is this?". That is
        ///         why the uniqueness guard covers section glyphs only.
        ///     </para>
        ///     <para>
        ///         <b>Warning is a solid triangle, not U+26A0.</b> The warning sign renders as a colour
        ///         emoji on Windows and recent macOS, which ignores the tint the row asks for and sits
        ///         off the text baseline — a yellow-and-black pictogram in the middle of a tinted status
        ///         column. <see cref="Validation" /> shares this shape for exactly that reason.
        ///     </para>
        ///     <para>
        ///         One spelling per outcome is the point. Spelled at the call site instead, a cross
        ///         reaches two different codepoints in files that sit side by side, and the difference
        ///         only shows up on the machine whose font covers one of them and not the other.
        ///     </para>
        /// </remarks>
        internal static class Status
        {
            /// <summary>The check passed. Tint with the brand accent.</summary>
            internal const string Ok = "✓";

            /// <summary>The check failed and blocks the feature. Tint with the error colour.</summary>
            internal const string Fail = "✕";

            /// <summary>The check passed but something should be reviewed. Tint amber.</summary>
            internal const string Warn = "▲";

            /// <summary>
            ///     The check failed but the editor can fix it in one click. Tint with the info colour —
            ///     this is an offer, not a problem.
            /// </summary>
            internal const string Fixable = "◇";

            /// <summary>Not evaluated, not applicable, or switched off. Tint muted.</summary>
            internal const string Neutral = "○";

            /// <summary>
            ///     Context rather than an outcome — the check reports something worth knowing. A plain
            ///     lower-case letter, matching the design system's own info message box, because the
            ///     information sign at U+2139 renders as a colour emoji on Windows.
            /// </summary>
            internal const string Info = "i";
        }

        /// <summary>
        ///     Marks that label a control rather than a section or an outcome — reorder arrows, a
        ///     dropdown caret, a disclosure triangle. Small and quiet by design: an affordance mark
        ///     tells the user what a button does, so it must never compete with the section mark or the
        ///     status mark on the same row.
        /// </summary>
        internal static class Affordance
        {
            /// <summary>Move this entry one place earlier in its list.</summary>
            internal const string MoveUp = "↑";

            /// <summary>Move this entry one place later in its list.</summary>
            internal const string MoveDown = "↓";

            /// <summary>This button opens a menu.</summary>
            internal const string Dropdown = "▾";

            /// <summary>
            ///     This button removes the row it sits on — an alias, a list entry, a slot.
            /// </summary>
            /// <remarks>
            ///     The same cross <see cref="Status.Fail" /> uses, and deliberately a separate name.
            ///     A remove button is an affordance the user presses, not a verdict the editor
            ///     reports, and the two drift apart the moment either vocabulary is restyled — a
            ///     failed check is tinted with the error colour, while a remove button must stay
            ///     quiet enough not to read as a warning on every row of a healthy list. Spell this
            ///     one when the user is meant to press it.
            /// </remarks>
            internal const string Remove = "✕";

            /// <summary>An expanded disclosure. Drawn by the design system's section header row.</summary>
            internal const string DisclosureOpen = "▾";

            /// <summary>A collapsed disclosure. Drawn by the design system's section header row.</summary>
            internal const string DisclosureClosed = "▸";
        }
    }
}
