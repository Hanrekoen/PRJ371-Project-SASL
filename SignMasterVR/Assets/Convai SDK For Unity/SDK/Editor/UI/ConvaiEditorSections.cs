using System;
using UnityEditor;
using UnityEngine;
using Draw = Convai.Editor.UI.ConvaiEditorDraw;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     Describes one stateful collapsible section: which editor owns it, its stable id, its title,
    ///     its glyph and the accent colour that marks what kind of section it is.
    /// </summary>
    internal readonly struct ConvaiEditorSectionSpec
    {
        internal ConvaiEditorSectionSpec(
            string editorTypeId,
            string sectionId,
            string title,
            string glyph,
            Color? headerColor = null,
            int iconFontSize = Tokens.SectionIconFontSize,
            string summary = null)
        {
            EditorTypeId = editorTypeId;
            SectionId = sectionId;
            Title = title;
            Glyph = glyph;
            HeaderColor = headerColor ?? Tokens.Accent;
            IconFontSize = iconFontSize;
            Summary = summary;
        }

        /// <summary>Owning editor, used to namespace persisted expansion state.</summary>
        internal string EditorTypeId { get; }

        /// <summary>Stable id of this section within its editor.</summary>
        internal string SectionId { get; }

        /// <summary>Display title.</summary>
        internal string Title { get; }

        /// <summary>Leading glyph — see <see cref="ConvaiEditorGlyphs" />.</summary>
        internal string Glyph { get; }

        /// <summary>Accent for the glyph, title and divider.</summary>
        internal Color HeaderColor { get; }

        /// <summary>Point size of the glyph.</summary>
        internal int IconFontSize { get; }

        /// <summary>
        ///     Optional right-aligned state summary, so a collapsed section still reports what it
        ///     holds. Null draws no summary.
        /// </summary>
        internal string Summary { get; }
    }

    /// <summary>
    ///     Stateful collapsible sections: a header row whose whole width toggles expansion, and a
    ///     recessed body panel. The richer sibling of
    ///     <see cref="ConvaiEditorFrame.CollapsibleSectionHeader" /> — use this when a section is
    ///     colour-coded by kind and its expansion is persisted; use the frame form for a plain
    ///     disclosure inside a card.
    /// </summary>
    /// <remarks>
    ///     Section bodies are rounded panels drawn with <see cref="ConvaiEditorDraw" />, not flat
    ///     <see cref="EditorGUI.DrawRect" /> fills, and they take their greys from
    ///     <see cref="ConvaiEditorTokens" /> so they adapt to the Light editor skin instead of
    ///     rendering as dark blocks in it.
    /// </remarks>
    internal static class ConvaiEditorSections
    {
        /// <summary>
        ///     Draws a colour-coded collapsible section header and returns the new expanded state.
        ///     Renders through <see cref="ConvaiEditorFrame.SectionHeaderRow" /> — the single header
        ///     row of the design system — with this section's accent applied to its glyph, chevron,
        ///     title and underline.
        /// </summary>
        internal static bool DrawHeader(in ConvaiEditorSectionSpec spec, bool expanded) =>
            ConvaiEditorFrame.SectionHeaderRow(
                spec.Glyph, spec.Title, expanded, spec.HeaderColor, spec.Summary, spec.IconFontSize);

        /// <summary>Begins a section body. Pair with <see cref="EndBody" />.</summary>
        internal static void BeginBody(Color? backgroundOverride = null)
        {
            Styles.EnsureStyles();
            Rect body = EditorGUILayout.BeginVertical();
            Draw.FillRounded(
                new Rect(body.x, body.y, body.width, body.height + Tokens.SectionBodyBottomFill),
                backgroundOverride ?? Tokens.CardBg,
                Tokens.PanelRadius);

            GUILayout.Space(Tokens.SectionBodyTopPadding);
            EditorGUI.indentLevel++;
        }

        /// <summary>Ends a section body opened with <see cref="BeginBody" />.</summary>
        internal static void EndBody()
        {
            EditorGUI.indentLevel--;
            GUILayout.Space(Tokens.SectionBodyBottomPadding);
            EditorGUILayout.EndVertical();
            GUILayout.Space(Tokens.SectionOuterSpacing);
        }

        /// <summary>
        ///     Opens a section body for a <c>using</c> block:
        ///     <c>using (ConvaiEditorSections.Body()) { … }</c>. A factory, and the constructor below
        ///     takes no default, for the reason spelled out on <see cref="ConvaiEditorFrame.Card" />:
        ///     <c>new BodyScope()</c> on a struct never ran the constructor, so the body was never
        ///     opened while <c>Dispose</c> still closed it.
        /// </summary>
        internal static BodyScope Body(Color? backgroundOverride = null) => new(backgroundOverride);

        /// <summary><c>using</c>-friendly form of <see cref="BeginBody" />/<see cref="EndBody" />.</summary>
        internal readonly struct BodyScope : IDisposable
        {
            /// <summary>Open one through <see cref="Body" />.</summary>
            internal BodyScope(Color? backgroundOverride) => BeginBody(backgroundOverride);

            public void Dispose() => EndBody();
        }
    }
}
