using System;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Draw = Convai.Editor.UI.ConvaiEditorDraw;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The one entry point call sites use to reach the Convai editor design system — a facade over
    ///     <see cref="ConvaiEditorTokens" />, <see cref="ConvaiEditorDraw" />,
    ///     <see cref="ConvaiEditorStyles" />, <see cref="ConvaiEditorControls" /> and
    ///     <see cref="ConvaiEditorFrame" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a facade.</b> Drawing one card touches four of the five layers. Without this,
    ///         every editor file would carry five <c>using</c> aliases and authors would have to
    ///         remember which layer owns which member. That friction is what makes copying a theme
    ///         file cheaper than reusing one; a single import makes reuse the path of least
    ///         resistance instead.
    ///     </para>
    ///     <para>
    ///         <b>This file contains no values and no logic.</b> Every member is a forwarding
    ///         expression. A colour, metric, style or drawing rule is still defined in exactly one
    ///         place, so this cannot become a second source of truth — and a guard test asserts no
    ///         colour literal ever appears here.
    ///     </para>
    ///     <para>
    ///         The layered classes remain public within the assembly and may be used directly where a
    ///         call site genuinely only needs one layer.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorTheme
    {
        #region Palette

        internal static Color Accent => Tokens.Accent;
        internal static Color AccentBright => Tokens.AccentBright;
        internal static Color OnAccent => Tokens.OnAccent;

        internal static Color StatusReady => Tokens.StatusReady;
        internal static Color StatusWarn => Tokens.StatusWarn;
        internal static Color StatusError => Tokens.StatusError;
        internal static Color StatusInfo => Tokens.StatusInfo;
        internal static Color StatusIdle => Tokens.StatusIdle;

        /// <summary>Alias of <see cref="StatusWarn" /> for surfaces that read as severity, not state.</summary>
        internal static Color Warning => Tokens.StatusWarn;

        /// <summary>Alias of <see cref="StatusError" />.</summary>
        internal static Color Error => Tokens.StatusError;

        /// <summary>Alias of <see cref="StatusInfo" />.</summary>
        internal static Color Info => Tokens.StatusInfo;

        internal static Color WindowBg => Tokens.WindowBg;
        internal static Color HeroBg => Tokens.HeroBg;
        internal static Color PaneBg => Tokens.PaneBg;
        internal static Color CardBg => Tokens.CardBg;
        internal static Color CardBgHover => Tokens.CardBgHover;
        internal static Color CardBgSelected => Tokens.CardBgSelected;
        internal static Color CardBorder => Tokens.CardBorder;
        internal static Color InnerBg => Tokens.InnerBg;
        internal static Color Divider => Tokens.Divider;
        internal static Color RowAlt => Tokens.RowAlt;
        internal static Color TableHeaderBg => Tokens.TableHeaderBg;
        internal static Color AccentRule => Tokens.AccentRule;

        internal static Color TextPrimary => Tokens.TextPrimary;
        internal static Color TextSecondary => Tokens.TextSecondary;
        internal static Color TextMuted => Tokens.TextMuted;

        internal static Color Tint(Color status) => Tokens.Tint(status);
        internal static Color TintBorder(Color status) => Tokens.TintBorder(status);
        internal static Color Fade(Color color, float alpha) => Tokens.Fade(color, alpha);

        /// <summary>Semi-transparent divider derived from a section's own accent colour.</summary>
        internal static Color DividerColor(Color baseColor) => Tokens.Fade(baseColor, 0.3f);

        #endregion

        #region Metrics

        // Re-exported as `const` (not properties) so call sites can use them in `const int` fields.

        internal const int SectionHeaderRowHeight = Tokens.SectionHeaderRowHeight;
        internal const int SectionIconFontSize = Tokens.SectionIconFontSize;
        internal const int SectionIconCellWidth = Tokens.SectionIconCellWidth;
        internal const int SectionChevronCellWidth = Tokens.SectionChevronCellWidth;
        internal const int SectionIconSpacing = Tokens.SectionIconSpacing;
        internal const int SectionChevronTextSpacing = Tokens.SectionChevronTextSpacing;
        internal const int SectionBodyTopPadding = Tokens.SectionBodyTopPadding;
        internal const int SectionBodyBottomPadding = Tokens.SectionBodyBottomPadding;
        internal const int SectionBodyBottomFill = Tokens.SectionBodyBottomFill;
        internal const int SectionOuterSpacing = Tokens.SectionOuterSpacing;

        #endregion

        #region Drawing primitives

        /// <summary>
        ///     One tint from the fixed palette for user-named labels (see
        ///     <see cref="ConvaiEditorTokens.LabelTint" />). Never a status colour.
        /// </summary>
        internal static Color LabelTint(int index) => Tokens.LabelTint(index);

        /// <summary>How many distinct label tints the palette holds.</summary>
        internal static int LabelTintCount => Tokens.LabelTintCount;

        internal static void FillRounded(Rect rect, Color color, float radius) =>
            Draw.FillRounded(rect, color, radius);

        internal static void StrokeRounded(Rect rect, Color color, float radius, float width = 1f) =>
            Draw.StrokeRounded(rect, color, radius, width);

        internal static void FillCircle(Vector2 center, float radius, Color color) =>
            Draw.FillCircle(center, radius, color);

        internal static void StatusDot(Vector2 center, Color color, bool emphasized = false) =>
            Draw.StatusDot(center, color, emphasized);

        /// <summary>
        ///     Status dot centred in <paramref name="rect" /> — prefer this over the
        ///     <see cref="Vector2" /> form, which makes the caller work out the row's centre itself.
        /// </summary>
        internal static void StatusDot(Rect rect, Color color, bool emphasized = false) =>
            Draw.StatusDot(rect, color, emphasized);

        internal static void Fill(Rect rect, Color color) => Draw.Fill(rect, color);

        internal static void DividerLine(Rect rect, Color color) => Draw.DividerLine(rect, color);

        internal static void HorizontalRule(Color color, float before = 3f, float after = 8f) =>
            Draw.HorizontalRule(color, before, after);

        #endregion

        #region Styles

        internal static void EnsureStyles() => Styles.EnsureStyles();

        internal static GUIStyle HeroTitle => Styles.HeroTitle;
        internal static GUIStyle HeroSubtitle => Styles.HeroSubtitle;
        internal static GUIStyle InspectorTitle => Styles.InspectorTitle;
        internal static GUIStyle SectionTitle => Styles.SectionTitle;
        internal static GUIStyle SectionGlyph => Styles.SectionGlyph;
        internal static GUIStyle SectionSummary => Styles.SectionSummary;
        internal static GUIStyle SelectedTitle => Styles.SelectedTitle;
        internal static GUIStyle CenteredTitle => Styles.CenteredTitle;

        internal static GUIStyle BodyWrapped => Styles.BodyWrapped;
        internal static GUIStyle MutedWrapped => Styles.MutedWrapped;
        internal static GUIStyle CaptionWrapped => Styles.CaptionWrapped;
        internal static GUIStyle MicroLabel => Styles.MicroLabel;
        internal static GUIStyle MicroLabelRight => Styles.MicroLabelRight;
        internal static GUIStyle GroupLabel => Styles.GroupLabel;
        internal static GUIStyle RowLabel => Styles.RowLabel;

        /// <summary>Caption over a cluster of controls. See <see cref="Controls.GroupCaption(GUIContent)" />.</summary>
        internal static void GroupCaption(GUIContent title) => Controls.GroupCaption(title);

        /// <inheritdoc cref="Controls.GroupCaption(GUIContent, bool)" />
        internal static void GroupCaption(GUIContent title, bool customStatus) =>
            Controls.GroupCaption(title, customStatus);

        /// <inheritdoc cref="GroupCaption(GUIContent)" />
        internal static void GroupCaption(string title) => Controls.GroupCaption(title);
        internal static GUIStyle ListGroupHeader => Styles.ListGroupHeader;
        internal static GUIStyle CenteredBody => Styles.CenteredBody;
        internal static GUIStyle PreviewQuote => Styles.PreviewQuote;
        internal static GUIStyle FooterLabel => Styles.FooterLabel;
        internal static GUIStyle Link => Styles.Link;

        /// <summary>
        ///     The key/value pair of a reading row — a label and the value it reports, one type size
        ///     apart in weight and colour rather than in scale. Use them together: a caption in one
        ///     size next to a value in another is the fastest way to make a one-line meta row look
        ///     unbalanced.
        /// </summary>
        internal static GUIStyle ReadingLabel => Styles.ReadingLabel;

        /// <inheritdoc cref="ReadingLabel" />
        internal static GUIStyle ReadingValue => Styles.ReadingValue;

        internal static GUIStyle CardName => Styles.CardName;
        internal static GUIStyle CardNameSelected => Styles.CardNameSelected;
        internal static GUIStyle CardTitle => Styles.CardTitle;
        internal static GUIStyle CardSubtitle => Styles.CardSubtitle;
        internal static GUIStyle TileNumber => Styles.TileNumber;
        internal static GUIStyle TileLabel => Styles.TileLabel;
        internal static GUIStyle LiveCellLabel => Styles.LiveCellLabel;
        internal static GUIStyle LiveCellValue => Styles.LiveCellValue;

        /// <summary>
        ///     Tinted variants. Each returns a pooled instance that carries only the colour asked
        ///     for, so it is safe to hold and safe to interleave with untinted draws of the same
        ///     style. The colour never lands on the shared base style above — a tinted reading in
        ///     one window used to leave its colour on every untinted reading drawn afterwards.
        /// </summary>
        internal static GUIStyle TileNumberTinted(Color color) => Styles.TileNumberTinted(color);

        /// <inheritdoc cref="TileNumberTinted" />
        internal static GUIStyle MetricNumberTinted(Color color) => Styles.MetricNumberTinted(color);

        /// <inheritdoc cref="TileNumberTinted" />
        internal static GUIStyle MicroLabelRightTinted(Color color) => Styles.MicroLabelRightTinted(color);

        /// <inheritdoc cref="TileNumberTinted" />
        internal static GUIStyle LiveCellValueTinted(Color color, bool bold = false) =>
            Styles.LiveCellValueTinted(color, bold);

        internal static GUIStyle SearchGlyph => Styles.SearchGlyph;
        internal static GUIStyle SearchText => Styles.SearchText;
        internal static GUIStyle SearchPlaceholder => Styles.SearchPlaceholder;
        internal static GUIStyle StarterGlyph => Styles.StarterGlyph;
        internal static GUIStyle StarterName => Styles.StarterName;
        internal static GUIStyle StarterDesc => Styles.StarterDesc;

        internal static GUIStyle CardBody => Styles.CardBody;
        internal static GUIStyle PanelBody => Styles.PanelBody;
        internal static GUIStyle PaneContent => Styles.PaneContent;
        internal static GUIStyle InvisibleButton => Styles.InvisibleButton;

        internal static GUIStyle CenteredMini(Color color) => Styles.CenteredMini(color);

        #endregion

        #region Measurement

        /// <summary>
        ///     Height of <paramref name="text" /> wrapped to <paramref name="width" />. Use this
        ///     rather than <see cref="GUIStyle.CalcHeight" />: measuring runs the text generator, and
        ///     a draw path runs on every repaint. See <see cref="ConvaiEditorTextMetrics" />.
        /// </summary>
        internal static float WrappedHeight(GUIStyle style, string text, float width) =>
            ConvaiEditorTextMetrics.WrappedHeight(style, text, width);

        /// <summary>Width of <paramref name="content" />. Prefer this over <see cref="GUIStyle.CalcSize" />.</summary>
        internal static float TextWidth(GUIStyle style, GUIContent content) =>
            ConvaiEditorTextMetrics.Width(style, content);

        /// <inheritdoc cref="TextWidth(GUIStyle,GUIContent)" />
        internal static float TextWidth(GUIStyle style, string text) =>
            ConvaiEditorTextMetrics.Width(style, text);

        #endregion

        #region Controls

        internal static bool PrimaryButton(Rect rect, GUIContent content) => Controls.PrimaryButton(rect, content);

        internal static bool GhostButton(Rect rect, GUIContent content) => Controls.GhostButton(rect, content);

        internal static bool IconButton(Rect rect, GUIContent content) => Controls.IconButton(rect, content);
        internal static float PillWidth(GUIContent content, bool dot = false) => Controls.PillWidth(content, dot);

        /// <summary>Width a <see cref="GhostButton" /> needs for <paramref name="content" />.</summary>
        internal static float GhostButtonWidth(GUIContent content) => Controls.GhostButtonWidth(content);

        internal static void Pill(Rect rect, GUIContent content, Color tint, bool dot = false) =>
            Controls.Pill(rect, content, tint, dot);

        internal static bool PillButton(Rect rect, GUIContent content, Color tint) =>
            Controls.PillButton(rect, content, tint);

        /// <summary>Full-width <see cref="PrimaryButton" /> that reserves its own layout row.</summary>
        internal static bool PrimaryButtonLayout(GUIContent content, float height = 28f) =>
            Controls.PrimaryButtonLayout(content, height);

        /// <summary>Full-width <see cref="GhostButton" /> that reserves its own layout row.</summary>
        internal static bool GhostButtonLayout(GUIContent content, float height = 22f) =>
            Controls.GhostButtonLayout(content, height);

        /// <summary>A row of mutually exclusive choices. See <see cref="Controls.SegmentedPicker" />.</summary>
        internal static int SegmentedPicker(GUIContent[] options, int selectedIndex, float height = 22f) =>
            Controls.SegmentedPicker(options, selectedIndex, height);

        /// <inheritdoc cref="Controls.PresetPicker" />
        internal static int PresetPicker(
            GUIContent caption, GUIContent[] options, int selectedIndex, string explanation, float height = 22f) =>
            Controls.PresetPicker(caption, options, selectedIndex, explanation, height);

        internal static void StatTile(GUIContent label, string value, Color? numberTint = null) =>
            Controls.StatTile(label, value, numberTint);

        /// <summary>Labelled live-telemetry readout.</summary>
        internal static void LiveCell(string label, string value, Color color, float width = 104f, bool bold = false) =>
            Controls.LiveCell(label, value, color, width, bold);

        /// <summary>Rounded search field. <paramref name="help" /> is the hover help for the field itself.</summary>
        internal static string SearchField(
            Rect rect, string value, GUIContent placeholder, string help, GUIContent clearButton, string controlName) =>
            Controls.SearchField(rect, value, placeholder, help, clearButton, controlName);

        /// <summary>Text field that shows an example inside itself while empty and unfocused.</summary>
        internal static string PlaceholderTextField(
            Rect rect, string value, GUIContent placeholder, string controlName) =>
            Controls.PlaceholderTextField(rect, value, placeholder, controlName);

        /// <summary>Labelled prose field that grows to fit its content, so nothing is ever clipped.</summary>
        internal static string ProseField(
            GUIContent label, string value, string key, GUIContent placeholder = null) =>
            Controls.ProseField(label, value, key, placeholder);

        #endregion

        #region Composite containers

        /// <summary>Convai component-inspector header. Drawn for you by <c>ConvaiInspectorEditor</c>.</summary>
        internal static void InspectorHeader(
            GUIContent title, GUIContent subtitle, GUIContent chip, Color tint, string helpUrl = null) =>
            Frame.InspectorHeader(title, subtitle, chip, tint, helpUrl);

        /// <inheritdoc cref="InspectorHeader(GUIContent,GUIContent,GUIContent,Color,string)" />
        internal static void InspectorHeader(GUIContent title, GUIContent chip, Color tint, string helpUrl = null) =>
            Frame.InspectorHeader(title, chip, tint, helpUrl);

        /// <summary>The hero band at the top of a module window. See <see cref="Frame.WindowHero" />.</summary>
        internal static Rect WindowHero(
            float windowWidth, GUIContent title, GUIContent subtitle,
            GUIContent chip = null, Color? chipTint = null, string helpUrl = null, float extraHeight = 0f) =>
            Frame.WindowHero(windowWidth, title, subtitle, chip, chipTint, helpUrl, extraHeight);

        internal static Rect BeginCard() => Frame.BeginCard();

        internal static void EndCard(float bottomSpace = ConvaiEditorTokens.CardSpacing) => Frame.EndCard(bottomSpace);

        internal static Rect BeginPanel(Color? statusColor = null, float accentBarWidth = 0f) =>
            Frame.BeginPanel(statusColor, accentBarWidth);

        internal static void EndPanel(float bottomSpace = 6f) => Frame.EndPanel(bottomSpace);

        /// <summary>
        ///     <c>using</c>-friendly section card. Prefer this over <see cref="BeginCard" /> — an early
        ///     <c>return</c> inside a <c>Begin</c>/<c>End</c> pair leaves the layout unbalanced, and a
        ///     scope cannot.
        /// </summary>
        internal static Frame.CardScope CardScope(float bottomSpace = ConvaiEditorTokens.CardSpacing) =>
            new(bottomSpace);

        /// <summary><c>using</c>-friendly nested panel. Prefer this over <see cref="BeginPanel" />.</summary>
        internal static Frame.PanelScope PanelScope(
            Color? statusColor = null, float bottomSpace = 6f, float accentBarWidth = 0f) =>
            new(statusColor, bottomSpace, accentBarWidth);
        /// <summary>
        ///     A height-capped list region that scrolls once its contents outgrow
        ///     <paramref name="maxHeight" />. See <see cref="Frame.BeginScrollRegion" />.
        /// </summary>
        internal static Vector2 BeginScrollRegion(Vector2 scroll, float contentHeight, float maxHeight) =>
            Frame.BeginScrollRegion(scroll, contentHeight, maxHeight);

        /// <summary>Ends a region opened with <see cref="BeginScrollRegion" />.</summary>
        internal static void EndScrollRegion(float bottomSpace = 0f) => Frame.EndScrollRegion(bottomSpace);

        /// <summary>A selectable card in a module window's character list. See <see cref="Frame.SelectableCard" />.</summary>
        internal static bool SelectableCard(
            GUIContent title, string subtitle, Color dotTint, bool selected, out Rect card,
            string tooltip = null) =>
            Frame.SelectableCard(title, subtitle, dotTint, selected, out card, tooltip);

        /// <summary>A rect of <paramref name="height" /> centred vertically in <paramref name="outer" />.</summary>
        internal static Rect CenteredSlice(Rect outer, float height) => Draw.CenteredSlice(outer, height);

        /// <summary>
        ///     Wrapped explanatory copy. Use this instead of <see cref="GUILayout.Label(GUIContent,GUIStyle)" />
        ///     for every paragraph in a wrapping style — see <see cref="Frame.Paragraph(GUIContent,GUIStyle)" />
        ///     for why a plain label reserves the wrong height and clips its own last line.
        /// </summary>
        internal static void Paragraph(GUIContent content, GUIStyle style) => Frame.Paragraph(content, style);

        /// <inheritdoc cref="Paragraph(GUIContent,GUIStyle)" />
        internal static void Paragraph(string text, GUIStyle style) => Frame.Paragraph(text, style);

        /// <summary>
        ///     Whether a paragraph settled its width during this pass. Call at the end of a host's
        ///     <c>OnGUI</c> and <c>Repaint()</c> when it returns true — see
        ///     <see cref="Frame.ConsumeMeasurementChange" />.
        /// </summary>
        internal static bool ConsumeMeasurementChange() => Frame.ConsumeMeasurementChange();

        internal static void SectionHeader(string glyph, GUIContent title) => Frame.SectionHeader(glyph, title);

        internal static bool CollapsibleSectionHeader(
            string glyph, GUIContent title, bool expanded, string summary = null) =>
            Frame.CollapsibleSectionHeader(glyph, title, expanded, summary);

        internal static bool SectionHeaderRow(
            string glyph, GUIContent title, bool expanded, Color accent,
            string summary = null,
            int glyphSize = ConvaiEditorTokens.SectionIconFontSize) =>
            Frame.SectionHeaderRow(glyph, title, expanded, accent, summary, glyphSize);

        internal static void InfoBox(
            string title, string message, string buttonText = null, Action buttonAction = null) =>
            Frame.InfoBox(title, message, buttonText, buttonAction);

        internal static void WarningBox(string title, string message, string buttonText = null, Action fix = null) =>
            Frame.WarningBox(title, message, buttonText, fix);

        internal static void ErrorBox(string title, string message, string buttonText = null, Action fix = null) =>
            Frame.ErrorBox(title, message, buttonText, fix);

        /// <summary>
        ///     A message box in a caller-chosen accent, for a state the three named boxes do not cover.
        ///     Prefer <see cref="InfoBox" />, <see cref="WarningBox" /> or <see cref="ErrorBox" />.
        /// </summary>
        internal static void MessageBox(
            Color accent, string glyph, string title, string body, string buttonText = null,
            Action buttonAction = null) =>
            Frame.MessageBox(accent, glyph, title, body, buttonText, buttonAction);

        /// <summary>Centred muted note shown where live data appears once the scene is playing.</summary>
        internal static void OfflinePlaceholder(string message = "Enter Play Mode to view live telemetry.") =>
            Frame.OfflinePlaceholder(message);

        /// <summary>
        ///     What a list, table or picker shows when it has nothing in it — mark, headline, cause and
        ///     optionally the one action that resolves it. See
        ///     <see cref="ConvaiEditorFrame.EmptyState" /> for why <paramref name="action" /> must defer
        ///     anything that opens a dialog.
        /// </summary>
        internal static void EmptyState(
            string glyph, string title, string message, GUIContent actionLabel = null, Action action = null) =>
            Frame.EmptyState(glyph, title, message, actionLabel, action);

        #endregion

        #region Readings, tables and mode bars

        /// <summary>One key/value reading with a fixed label column, so a stack of them aligns.</summary>
        internal static void KeyValueRow(
            GUIContent label, string value, Color? valueTint = null,
            float labelWidth = ConvaiEditorTokens.ReadingLabelWidth) =>
            Controls.KeyValueRow(label, value, valueTint, labelWidth);

        /// <summary>The tab row that switches a window between its views. Returns the clicked index, or -1.</summary>
        internal static int ModeBar(GUIContent[] options, int selectedIndex) =>
            Controls.ModeBar(options, selectedIndex);

        /// <summary>One column heading inside a <see cref="ConvaiEditorFrame.TableHeaderScope" />.</summary>
        internal static void TableColumn(GUIContent label, float width, bool centered = false) =>
            Frame.TableColumn(label, width, centered);

        /// <inheritdoc cref="ConvaiEditorFrame.TableHeaderLabel" />
        internal static void TableHeaderLabel(
            Rect header, float x, float width, GUIContent label, bool right = false) =>
            Frame.TableHeaderLabel(header, x, width, label, right);

        /// <summary>One cell inside a <see cref="ConvaiEditorFrame.TableRowScope" />.</summary>
        internal static void TableCell(string text, float width, Color? tint = null, bool centered = false) =>
            Frame.TableCell(text, width, tint, centered);

        #endregion
    }
}
