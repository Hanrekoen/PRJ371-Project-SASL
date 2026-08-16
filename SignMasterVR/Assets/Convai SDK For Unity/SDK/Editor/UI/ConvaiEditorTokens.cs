using UnityEditor;
using UnityEngine;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The single source of truth for every colour and metric in Convai's editor UI — the base
    ///     layer of the Convai editor design system. Inspectors, asset editors and module windows all
    ///     read from here, so a brand colour or a corner radius is defined exactly once and cannot
    ///     drift between two surfaces that are meant to look like one product.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Layering.</b> This is the bottom layer: <c>Tokens → Draw → Styles → Controls →
    ///         Frame → ConvaiInspectorEditor</c>. It deliberately knows nothing about
    ///         <see cref="GUIStyle" /> or drawing — it holds values and nothing else, so it stays
    ///         trivially readable and has no reason to change when the frame does.
    ///     </para>
    ///     <para>
    ///         <b>Every surface colour branches on <see cref="EditorGUIUtility.isProSkin" />.</b> A
    ///         hardcoded dark grey renders as a dark block with low-contrast text in Unity's Light
    ///         skin, so a colour that does not adapt is a defect rather than a style choice — and a
    ///         colour literal in an editor file outside this class is the same defect.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorTokens
    {
        private static bool Pro => EditorGUIUtility.isProSkin;

        #region Brand

        /// <summary>Convai brand green. The one colour the whole SDK is recognised by.</summary>
        internal static readonly Color Accent = new(0.322f, 0.718f, 0.533f);

        /// <summary>Brighter brand green for hover, emphasis and accent-coloured titles.</summary>
        internal static readonly Color AccentBright = new(0.435f, 0.812f, 0.592f);

        /// <summary>Near-black green for text sitting on an accent-filled surface (primary buttons).</summary>
        internal static Color OnAccent => new(0.051f, 0.094f, 0.071f);

        #endregion

        #region Status

        /// <summary>Healthy / configured / running.</summary>
        internal static readonly Color StatusReady = Accent;

        /// <summary>Configured but degraded, or an action the user should take.</summary>
        internal static readonly Color StatusWarn = new(1f, 0.655f, 0.149f);

        /// <summary>Broken — the feature cannot work until this is resolved.</summary>
        internal static readonly Color StatusError = new(0.937f, 0.325f, 0.314f);

        /// <summary>Neutral explanation, never a problem.</summary>
        internal static readonly Color StatusInfo = new(0.129f, 0.588f, 0.953f);

        /// <summary>Present but inactive — an idle live readout, a disabled control.</summary>
        internal static Color StatusIdle => TextMuted;

        #endregion

        #region Labels

        /// <summary>
        ///     The fixed palette for user-named labels — the categories a user files actions under.
        ///     Five muted hues that stay legible on both skins and never collide with the status
        ///     colours above, so a label can never be mistaken for a warning.
        /// </summary>
        /// <remarks>
        ///     A fixed set rather than a colour picker, deliberately. A picker lets a project make its
        ///     own inspector unreadable — and then that project's screenshots are the SDK's. Five is
        ///     enough to tell groups apart at a glance and few enough that they stay distinct.
        /// </remarks>
        private static readonly Color[] LabelTints =
        {
            Accent,
            new(0.353f, 0.612f, 0.855f),
            new(0.667f, 0.545f, 0.878f),
            new(0.902f, 0.694f, 0.310f),
            new(0.886f, 0.478f, 0.545f)
        };

        /// <summary>How many distinct label tints exist.</summary>
        internal static int LabelTintCount => LabelTints.Length;

        /// <summary>One label tint, by index. Any integer is accepted and wrapped into range.</summary>
        internal static Color LabelTint(int index) =>
            LabelTints[((index % LabelTints.Length) + LabelTints.Length) % LabelTints.Length];

        #endregion

        #region Surfaces

        /// <summary>Window background, behind every pane.</summary>
        internal static Color WindowBg => Pro ? new Color(0.145f, 0.153f, 0.149f) : new Color(0.784f, 0.796f, 0.788f);

        /// <summary>Hero banner background at the top of a module window.</summary>
        internal static Color HeroBg => Pro ? new Color(0.106f, 0.118f, 0.112f) : new Color(0.875f, 0.894f, 0.882f);

        /// <summary>Side/list pane background.</summary>
        internal static Color PaneBg => Pro ? new Color(0.125f, 0.133f, 0.129f) : new Color(0.812f, 0.827f, 0.816f);

        /// <summary>Section card surface — the most common container in the system.</summary>
        internal static Color CardBg => Pro ? new Color(0.184f, 0.196f, 0.188f) : new Color(0.878f, 0.890f, 0.880f);

        /// <summary>Card surface under the pointer.</summary>
        internal static Color CardBgHover => Pro ? new Color(0.216f, 0.231f, 0.220f) : new Color(0.910f, 0.922f, 0.912f);

        /// <summary>Card surface for the selected row in a list.</summary>
        internal static Color CardBgSelected => Color.Lerp(CardBg, Accent, Pro ? 0.16f : 0.22f);

        /// <summary>Hairline border around a card or control.</summary>
        internal static Color CardBorder => Pro ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.13f);

        /// <summary>Recessed panel nested inside a card (quotes, stat tiles, status blocks).</summary>
        internal static Color InnerBg => Pro ? new Color(0.135f, 0.145f, 0.139f) : new Color(0.845f, 0.858f, 0.848f);

        /// <summary>Structural separator between panes or list groups.</summary>
        internal static Color Divider => Pro ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.14f);

        /// <summary>Every other row in a data table, for readability across wide rows.</summary>
        internal static Color RowAlt => Pro ? new Color(1f, 1f, 1f, 0.032f) : new Color(0f, 0f, 0f, 0.04f);

        /// <summary>Column-header strip of a data table.</summary>
        internal static Color TableHeaderBg => Pro ? new Color(0.110f, 0.118f, 0.114f) : new Color(0.752f, 0.765f, 0.755f);

        #endregion

        #region Text

        /// <summary>Body and title text.</summary>
        internal static Color TextPrimary => Pro ? new Color(0.855f, 0.878f, 0.863f) : new Color(0.145f, 0.165f, 0.153f);

        /// <summary>Supporting text — subtitles, explanations under a field.</summary>
        internal static Color TextSecondary => Pro ? new Color(0.616f, 0.655f, 0.631f) : new Color(0.322f, 0.353f, 0.333f);

        /// <summary>De-emphasised text — captions, counts, placeholder copy.</summary>
        internal static Color TextMuted => Pro ? new Color(0.443f, 0.478f, 0.455f) : new Color(0.455f, 0.490f, 0.467f);

        #endregion

        #region Derived colours

        /// <summary>Faint fill for a status-tinted panel or pill.</summary>
        internal static Color Tint(Color status) => new(status.r, status.g, status.b, Pro ? 0.09f : 0.13f);

        /// <summary>Soft border for a status-tinted panel or pill.</summary>
        internal static Color TintBorder(Color status) => new(status.r, status.g, status.b, 0.38f);

        /// <summary>The same colour at a different alpha — for dividers and halos.</summary>
        internal static Color Fade(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        /// <summary>Thin accent rule drawn under a section header or inspector header.</summary>
        internal static Color AccentRule => Fade(Accent, 0.22f);

        /// <summary>
        ///     Soft plate under a glyph button on hover. Lightens on the dark skin and darkens on the
        ///     light one, so the affordance reads the same way in both.
        /// </summary>
        internal static Color HoverPlate =>
            Pro ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f);

        /// <summary>Highlight stroke on a pressed/hovered accent-filled control.</summary>
        internal static Color OnAccentHighlight => new(1f, 1f, 1f, 0.22f);

        /// <summary>Panel background behind an info/warning/error message box.</summary>
        internal static Color MessagePanelBg =>
            Pro ? new Color(0.160f, 0.170f, 0.165f, 0.92f) : new Color(0.860f, 0.872f, 0.862f, 0.92f);

        #endregion

        #region Metrics

        /// <summary>Corner radius of a section card.</summary>
        internal const float CardRadius = 8f;

        /// <summary>Corner radius of a panel nested inside a card.</summary>
        internal const float PanelRadius = 6f;

        /// <summary>Corner radius of a button or search field.</summary>
        internal const float ControlRadius = 5f;

        /// <summary>Corner radius of a small square glyph button.</summary>
        internal const float IconButtonRadius = 4f;

        /// <summary>Height of a collapsible section header row.</summary>
        internal const float SectionHeaderHeight = 22f;

        /// <summary>Height of the two-line Convai inspector header plate.</summary>
        internal const float InspectorHeaderHeight = 48f;

        /// <summary>Height of the single-line Convai inspector header plate.</summary>
        internal const float InspectorHeaderCompactHeight = 38f;

        /// <summary>Size of the Convai brand icon in an inspector header.</summary>
        internal const float BrandIconSize = 18f;

        /// <summary>Size of the tinted plate the brand icon sits on in an inspector header.</summary>
        internal const float BrandIconPlateSize = 28f;

        /// <summary>Corner radius of the brand-icon plate.</summary>
        internal const float BrandIconPlateRadius = 6f;

        /// <summary>
        ///     Height of the hero band at the top of a module window. One value for every window: a
        ///     band each window sizes for itself shifts the whole page when the user switches between
        ///     two Convai windows, which is invisible in either one alone and obvious across both.
        /// </summary>
        internal const float WindowHeroHeight = 64f;

        /// <summary>Standard height of a pill, chip or inline button.</summary>
        internal const float PillHeight = 20f;

        /// <summary>
        ///     Inset from a header's left edge to the brand emblem, and the gap from the emblem to the
        ///     title column. Shared by the inspector header and the window hero band on purpose: they
        ///     are the same opening line at two sizes, so they must not be free to drift apart.
        /// </summary>
        internal const float HeaderEdgeInset = 14f;

        /// <summary>Gap between the brand emblem and the title column.</summary>
        internal const float HeaderEmblemGap = 11f;

        /// <summary>
        ///     How far a full-bleed inspector band reaches past the right edge of the content column,
        ///     to meet the inspector window's own right margin. Unity's inspector margins are
        ///     asymmetric (a wide left gutter, a narrow right one), so a band that only zeroed its left
        ///     edge would stop four pixels short on the other side — the same gap the bleed exists to
        ///     remove, mirrored.
        /// </summary>
        internal const float InspectorEdgeBleed = 4f;

        /// <summary>Gap between two right-aligned header affordances (help button, status pill).</summary>
        internal const float HeaderAffordanceGap = 7f;

        /// <summary>Side of the square documentation ("?") button in a header.</summary>
        internal const float HeaderHelpButtonSize = 19f;

        /// <summary>
        ///     Widest a header status pill may grow. Past this it truncates rather than squeezing the
        ///     title out of the header — a character with a long name should shorten its own pill, not
        ///     the product name beside it.
        /// </summary>
        internal const float HeaderChipMaxWidth = 320f;

        /// <summary>Radius of a status dot — the filled core, not the halo.</summary>
        internal const float StatusDotRadius = 3.5f;

        /// <summary>Radius of the soft halo drawn behind an emphasised status dot.</summary>
        internal const float StatusDotHaloRadius = 7f;

        /// <summary>
        ///     Radius of the smaller status dot that leads a pill's label. Deliberately below
        ///     <see cref="StatusDotRadius" />: inside a 20-pixel pill the row-sized dot crowds the text.
        /// </summary>
        internal const float PillDotRadius = 2.5f;

        /// <summary>Inner padding of a section card.</summary>
        internal static readonly RectOffset CardPadding = new(14, 14, 12, 12);

        /// <summary>Inner padding of a nested panel.</summary>
        internal static readonly RectOffset PanelPadding = new(12, 10, 8, 8);

        /// <summary>Inner padding of a window's scrolling content pane.</summary>
        internal static readonly RectOffset PaneContentPadding = new(18, 18, 14, 14);

        /// <summary>Vertical gap between two stacked cards.</summary>
        internal const float CardSpacing = 10f;

        /// <summary>Height of a selectable card in a module window's character list.</summary>
        internal const float ListCardHeight = 42f;

        /// <summary>Vertical gap between two list cards.</summary>
        internal const float ListCardSpacing = 4f;

        /// <summary>Inset from the list pane's edges to a list card.</summary>
        internal const float ListCardInset = 8f;

        /// <summary>Inset from a list card's left edge to the centre of its status dot.</summary>
        internal const float ListCardDotInset = 14f;

        /// <summary>Inset from a list card's left edge to its text column.</summary>
        internal const float ListCardTextInset = 26f;

        #endregion

        #region Collapsible-section metrics

        // Integer constants because call sites use them in `const int` fields and GUIStyle sizes.

        /// <summary>Height of a collapsible section header row, in pixels.</summary>
        internal const int SectionHeaderRowHeight = 22;

        /// <summary>
        ///     Point size of a section header's leading glyph. Deliberately close to the 12pt section
        ///     title rather than well above it: at 16pt the solid marks out-weighed the words they
        ///     introduced, which is what made a stack of headers look decorated instead of designed.
        /// </summary>
        internal const int SectionIconFontSize = 13;

        /// <summary>Width of the glyph cell in a section header.</summary>
        internal const int SectionIconCellWidth = 16;

        /// <summary>Width of the chevron cell in a section header.</summary>
        internal const int SectionChevronCellWidth = 12;

        /// <summary>Gap after the glyph cell.</summary>
        internal const int SectionIconSpacing = 1;

        /// <summary>Gap between the chevron and the section title.</summary>
        internal const int SectionChevronTextSpacing = 2;

        /// <summary>Padding above a section body's first control.</summary>
        internal const int SectionBodyTopPadding = 4;

        /// <summary>Padding below a section body's last control.</summary>
        internal const int SectionBodyBottomPadding = 6;

        /// <summary>Extra fill drawn under a section body so its surface meets the next row cleanly.</summary>
        internal const int SectionBodyBottomFill = 4;

        /// <summary>Gap between one collapsible section and the next.</summary>
        internal const int SectionOuterSpacing = 4;

        #endregion

        #region Reading, table and mode-bar metrics

        /// <summary>
        ///     Width of the label column in a key/value reading row. Fixed rather than measured so
        ///     consecutive readings line their values up: a column that sizes to its own label puts
        ///     every value at a different x, which is what makes a stack of readouts look ragged.
        /// </summary>
        internal const float ReadingLabelWidth = 132f;

        /// <summary>Height of one key/value reading row.</summary>
        internal const float ReadingRowHeight = 18f;

        /// <summary>Height of a table's header row. Matches <see cref="ConvaiEditorStyles.TableHeaderCell"/>.</summary>
        internal const float TableHeaderHeight = 24f;

        /// <summary>Height of one table body row.</summary>
        internal const float TableRowHeight = 20f;

        /// <summary>Height of the mode bar that switches a window between its top-level views.</summary>
        internal const float ModeBarHeight = 34f;

        /// <summary>Height of one mode tab inside the bar.</summary>
        internal const float ModeTabHeight = 24f;

        /// <summary>Padding added to a mode tab's measured label width.</summary>
        internal const float ModeTabPadding = 30f;

        /// <summary>Gap between adjacent mode tabs.</summary>
        internal const float ModeTabGap = 6f;

        /// <summary>Inset from the window's left edge to the first mode tab.</summary>
        internal const float ModeBarInset = 12f;

        /// <summary>Point size of the glyph an empty state leads with.</summary>
        internal const int EmptyStateGlyphSize = 26;

        #endregion
    }
}
