using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Draw = Convai.Editor.UI.ConvaiEditorDraw;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The composite containers of the Convai editor design system — Convai headers, section
    ///     cards, nested panels, collapsible sections and message boxes. This is the layer concrete
    ///     editors actually call: it composes <see cref="ConvaiEditorControls" /> and
    ///     <see cref="ConvaiEditorDraw" /> and never re-implements either.
    /// </summary>
    /// <remarks>
    ///     Containers come in <c>Begin…</c>/<c>End…</c> pairs so callers keep using ordinary
    ///     <c>GUILayout</c> inside them; each pair also has a <see cref="IDisposable" /> scope
    ///     (<see cref="CardScope" />, <see cref="PanelScope" />) for call sites that prefer
    ///     <c>using</c> and cannot leak an unbalanced layout group if an exception unwinds.
    /// </remarks>
    internal static class ConvaiEditorFrame
    {
        #region Inspector header

        /// <summary>Cached help-button content so the header allocates nothing per repaint.</summary>
        private static readonly GUIContent HelpButtonContent =
            new("?", "Open the Convai documentation for this component");

        /// <summary>
        ///     Convai component-inspector header band: a full-bleed hero surface with a brand accent
        ///     bar down its left edge, the Convai icon on a tinted emblem plate, an accent title over a
        ///     muted subtitle, an optional status pill with a leading state dot, and an optional
        ///     documentation button when <paramref name="helpUrl" /> is provided.
        /// </summary>
        /// <remarks>
        ///     Full-bleed, like <see cref="WindowHero" />: the band runs to both edges of the inspector
        ///     rather than sitting inside Unity's content margins. Inside them it read as a plate
        ///     floating in the inspector with a stripe of empty background down its left, and the accent
        ///     bar — the mark that is meant to say "this whole column is one component" — marked a line
        ///     fifteen pixels in from the edge it belongs to. The cards below stay inside the margins;
        ///     the contrast is what makes the header read as the masthead of everything under it.
        /// </remarks>
        /// <param name="onChipClicked">
        ///     What the status chip leads to, or null to leave it an inert label. A chip that reports a
        ///     problem the user cannot open is the defect this parameter exists to close; a chip that
        ///     reports <c>Ready</c> passes null, because opening a report to be told nothing is wrong
        ///     is a worse answer than the word itself.
        /// </param>
        internal static void InspectorHeader(
            GUIContent title, GUIContent subtitle, GUIContent chip, Color chipTint, string helpUrl = null,
            Action onChipClicked = null)
        {
            Styles.EnsureStyles();

            bool twoLine = subtitle != null;
            float height = twoLine ? Tokens.InspectorHeaderHeight : Tokens.InspectorHeaderCompactHeight;
            Rect row = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));

            // The reserved row is inset by the inspector's content margins; the band is not. Widening
            // the reserved rect rather than measuring the view keeps this correct when a scrollbar
            // appears — the row Unity hands back has already shrunk by it.
            Rect band = Rect.MinMaxRect(0f, row.y, row.xMax + Tokens.InspectorEdgeBleed, row.yMax);

            Draw.Fill(band, Tokens.HeroBg);
            Draw.DividerLine(new Rect(band.x, band.yMax - 1f, band.width, 1f), Tokens.Divider);

            // Square and flush at the very edge, not an inset rounded pill: the bar is the component's
            // left margin marker, so it has to be on the margin.
            Draw.Fill(new Rect(band.x, band.y, 2.5f, band.height), Tokens.Accent);

            // The brand icon sits on a tinted plate so it reads as an emblem, not a floating image.
            Rect iconPlate = DrawBrandEmblem(band.x, band.y, band.height);

            // Right-aligned affordances: documentation button outermost, status pill inside it.
            float rightEdge = DrawHeaderAffordances(
                band.xMax, band.y, band.height, chip, chipTint, helpUrl, onChipClicked);

            float textX = iconPlate.xMax + Tokens.HeaderEmblemGap;
            float textWidth = Mathf.Max(40f, rightEdge - textX);
            if (twoLine)
            {
                GUI.Label(new Rect(textX, band.y + 6f, textWidth, 18f), title, Styles.InspectorTitle);
                GUI.Label(new Rect(textX, band.y + 24f, textWidth, 16f), subtitle, Styles.HeroSubtitle);
            }
            else
            {
                GUI.Label(new Rect(textX, band.y, textWidth, band.height), title, Styles.InspectorTitle);
            }

            GUILayout.Space(8f);
        }

        /// <summary>Single-line Convai inspector header with an optional status pill.</summary>
        internal static void InspectorHeader(
            GUIContent title, GUIContent chip, Color chipTint, string helpUrl = null) =>
            InspectorHeader(title, null, chip, chipTint, helpUrl);

        /// <summary>
        ///     Draws the Convai emblem on its tinted plate, vertically centred in a header row of
        ///     <paramref name="rowHeight" />, and returns the plate rect so the caller can lay its text
        ///     column out after it.
        /// </summary>
        /// <remarks>
        ///     Shared by <see cref="InspectorHeader" /> and <see cref="WindowHero" />. If each owned its
        ///     own emblem inset the two openings would stop lining up, which the user sees as a jump
        ///     when they move between a component and its window.
        /// </remarks>
        private static Rect DrawBrandEmblem(float rowX, float rowY, float rowHeight)
        {
            float plateSize = Tokens.BrandIconPlateSize;
            var plate = new Rect(
                rowX + Tokens.HeaderEdgeInset, rowY + ((rowHeight - plateSize) * 0.5f), plateSize, plateSize);

            Draw.FillAndStrokeRounded(
                plate, Tokens.Tint(Tokens.Accent), Tokens.TintBorder(Tokens.Accent), Tokens.BrandIconPlateRadius);

            Texture2D icon = ConvaiEditorIcons.Emblem();
            if (icon != null && Event.current.type == EventType.Repaint)
            {
                float pad = (plateSize - Tokens.BrandIconSize) * 0.5f;
                GUI.DrawTexture(
                    new Rect(plate.x + pad, plate.y + pad, Tokens.BrandIconSize, Tokens.BrandIconSize),
                    icon, ScaleMode.ScaleToFit, true);
            }

            return plate;
        }

        /// <summary>
        ///     Draws the right-aligned header affordances — documentation button outermost, status pill
        ///     inside it — and returns the left edge of the space they consumed, which is where the
        ///     title column must stop.
        /// </summary>
        /// <remarks>
        ///     Each affordance measures itself, so a long character name in the pill shortens the title
        ///     rather than colliding with it. Reserving a fixed width for the affordances instead makes
        ///     which of those two happens depend on the window rather than on the content.
        /// </remarks>
        private static float DrawHeaderAffordances(
            float rowXMax, float rowY, float rowHeight, GUIContent chip, Color chipTint, string helpUrl,
            Action onChipClicked = null)
        {
            float rightEdge = rowXMax - Tokens.HeaderEdgeInset;

            if (!string.IsNullOrEmpty(helpUrl))
            {
                float size = Tokens.HeaderHelpButtonSize;
                var helpRect = new Rect(rightEdge - size, rowY + ((rowHeight - size) * 0.5f), size, size);
                if (Controls.IconButton(helpRect, HelpButtonContent))
                    UnityEngine.Application.OpenURL(helpUrl);
                rightEdge = helpRect.x - Tokens.HeaderAffordanceGap;
            }

            if (chip != null)
            {
                float chipWidth = Mathf.Min(Tokens.HeaderChipMaxWidth, Controls.PillWidth(chip, true));
                var chipRect = new Rect(
                    rightEdge - chipWidth, rowY + ((rowHeight - Tokens.PillHeight) * 0.5f),
                    chipWidth, Tokens.PillHeight);

                if (onChipClicked == null)
                    Controls.Pill(chipRect, chip, chipTint, true);
                else if (Controls.PillButton(chipRect, chip, chipTint, true))
                    onChipClicked();

                rightEdge = chipRect.x - Tokens.HeaderAffordanceGap;
            }

            return rightEdge;
        }

        #endregion

        #region Window hero

        /// <summary>
        ///     The hero band at the top of a Convai module window: a full-bleed hero surface under an
        ///     accent rule, the Convai emblem, an accent title over a muted subtitle, and an optional
        ///     right-aligned status pill. Reserves its own layout row, so the window's content simply
        ///     follows it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Deliberately the same visual language as <see cref="InspectorHeader" /> — same emblem
        ///         plate, same title and subtitle styles, same pill with a leading state dot — because a
        ///         module's window and its inspector are one product and a user moves between them
        ///         constantly.
        ///     </para>
        ///     <para>
        ///         Every module window draws its band through this one method. A band has more
        ///         dimensions than it looks — height, the top accent rule, the bottom hairline, the
        ///         title baseline, whether the emblem appears, the pill's maximum width — and a window
        ///         that owns them privately can differ in any of them. None of that is visible inside
        ///         one window; all of it is visible switching between two.
        ///     </para>
        /// </remarks>
        /// <param name="windowWidth">
        ///     The window's full width (<c>position.width</c>). The band is full-bleed, so it cannot be
        ///     derived from the reserved layout rect, which is inset by the window's padding.
        /// </param>
        /// <param name="extraHeight">
        ///     Additional height for a window that hosts one more control inside the band — the Actions
        ///     Editor's character picker, for example. The returned rect is how the caller finds it.
        /// </param>
        /// <returns>
        ///     The band it drew, so a caller can place its own affordances inside it. This is the
        ///     extension seam instead of a parameter per affordance: pass <c>chip: null</c> to leave
        ///     the right-hand side free and draw buttons there yourself.
        /// </returns>
        internal static Rect WindowHero(
            float windowWidth, GUIContent title, GUIContent subtitle,
            GUIContent chip = null, Color? chipTint = null, string helpUrl = null, float extraHeight = 0f)
        {
            Styles.EnsureStyles();

            Rect reserved = GUILayoutUtility.GetRect(
                0f, Tokens.WindowHeroHeight + extraHeight, GUILayout.ExpandWidth(true));
            var hero = new Rect(0f, 0f, windowWidth, reserved.yMax);

            Draw.Fill(hero, Tokens.HeroBg);
            Draw.DividerLine(new Rect(0f, 0f, hero.width, 2f), Tokens.Accent);
            Draw.DividerLine(new Rect(0f, hero.yMax - 1f, hero.width, 1f), Tokens.Divider);

            // The emblem, the pill and the title all centre on the TITLE ROW, not on the whole band:
            // a window that asked for extra height wants its own control in that extra space, and the
            // opening line has to stay put whether or not it did.
            float titleRow = Tokens.WindowHeroHeight;

            Rect iconPlate = DrawBrandEmblem(hero.x, hero.y, titleRow);
            float rightEdge = DrawHeaderAffordances(
                hero.xMax, hero.y, titleRow, chip, chipTint ?? Tokens.Accent, helpUrl);

            float textX = iconPlate.xMax + Tokens.HeaderEmblemGap;
            float textWidth = Mathf.Max(60f, rightEdge - textX);
            if (subtitle != null)
            {
                GUI.Label(new Rect(textX, 13f, textWidth, 20f), title, Styles.HeroTitle);
                GUI.Label(new Rect(textX, 33f, textWidth, 18f), subtitle, Styles.HeroSubtitle);
            }
            else
            {
                GUI.Label(new Rect(textX, 0f, textWidth, titleRow), title, Styles.HeroTitle);
            }

            return hero;
        }

        #endregion

        #region Selectable list card

        /// <summary>
        ///     A selectable card in a module window's character list: a status dot, a title, a subtitle,
        ///     and selection and hover states. Reserves its own layout row and returns whether the user
        ///     clicked it. The card rect is available through <paramref name="card" /> so a caller can
        ///     place a trailing affordance of its own inside it.
        /// </summary>
        /// <remarks>
        ///     Every module window that lists characters draws this card, so it is defined once. A card
        ///     is height, radius, three background states, a dot inset and two text baselines; each copy
        ///     of it is another chance for one window's list to be the odd one out.
        /// </remarks>
        /// <param name="title">The character's name.</param>
        /// <param name="subtitle">Its status in words, from the module's own vocabulary.</param>
        /// <param name="dotTint">Status colour for the leading dot.</param>
        /// <param name="selected">Whether this is the card the window is currently showing.</param>
        /// <param name="tooltip">Optional explanation shown on hover. Null or empty draws none.</param>
        internal static bool SelectableCard(
            GUIContent title, string subtitle, Color dotTint, bool selected, out Rect card, string tooltip = null)
        {
            Styles.EnsureStyles();

            Rect slot = GUILayoutUtility.GetRect(
                0f, Tokens.ListCardHeight + Tokens.ListCardSpacing, GUILayout.ExpandWidth(true));
            card = new Rect(
                slot.x + Tokens.ListCardInset,
                slot.y + (Tokens.ListCardSpacing * 0.5f),
                slot.width - (Tokens.ListCardInset * 2f),
                Tokens.ListCardHeight);

            bool hover = card.Contains(Event.current.mousePosition);
            Draw.FillRounded(
                card,
                selected ? Tokens.CardBgSelected : hover ? Tokens.CardBgHover : Tokens.CardBg,
                Tokens.CardRadius);
            Draw.StrokeRounded(
                card,
                selected ? Tokens.Fade(Tokens.Accent, 0.85f) : Tokens.CardBorder,
                Tokens.CardRadius);

            // No halo, even on the selected card: the card already announces selection with its own
            // surface and accent border, so a halo would say the same thing a second time.
            Draw.StatusDot(
                new Vector2(card.x + Tokens.ListCardDotInset, card.y + (card.height * 0.5f)), dotTint);

            float textX = card.x + Tokens.ListCardTextInset;
            float textWidth = card.width - Tokens.ListCardTextInset - Tokens.ListCardInset;
            GUI.Label(new Rect(textX, card.y + 5f, textWidth, 16f), title, Styles.CardTitle);
            GUI.Label(new Rect(textX, card.y + 22f, textWidth, 14f), subtitle, Styles.CardSubtitle);

            EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
            ScratchCardHit.tooltip = tooltip ?? string.Empty;
            return GUI.Button(card, ScratchCardHit, GUIStyle.none);
        }

        /// <summary>Invisible hit-target content, reused so a card allocates nothing per repaint.</summary>
        private static readonly GUIContent ScratchCardHit = new(string.Empty);

        #endregion

        #region Section cards

        /// <summary>
        ///     Begins a rounded section card. Pair with <see cref="EndCard" />. The returned rect is
        ///     only meaningful during <see cref="EventType.Repaint" />.
        /// </summary>
        /// <summary>
        ///     How many containers this system has open in the current pass. Guards
        ///     <c>End…</c> against closing a group the system does not own.
        /// </summary>
        /// <remarks>
        ///     A modal dialog raised from a button inside a container (the shape that bricked the Body
        ///     Animation inspector) pumps the event loop and discards the layout state mid-pass. The
        ///     matching <c>End…</c> would then pop <em>Unity's</em> wrapper group instead of ours,
        ///     and the inspector threw on every later repaint with no way back. Counting our own
        ///     containers turns that into one skipped close: the frame looks wrong, the next one is
        ///     correct again. <see cref="ResetGroupDepth" /> re-arms it per pass.
        /// </remarks>
        private static int s_openContainers;

        /// <summary>
        ///     Begins a draw pass with a fresh container count and returns the previous one, so a pass
        ///     that unwound through an exception cannot leak its count into the next one. Pair with
        ///     <see cref="ExitDrawScope" />.
        /// </summary>
        /// <remarks>
        ///     Save-and-restore rather than a plain reset to zero, because draw passes nest: an editor
        ///     window that draws its own cards and then hosts a Convai inspector would have had its
        ///     open containers forgotten the moment the inspector started, and its own <c>End…</c> calls
        ///     would then have been refused as "not ours" — turning the guard against unbalanced layout
        ///     into a cause of it.
        /// </remarks>
        internal static int EnterDrawScope()
        {
            int previous = s_openContainers;
            s_openContainers = 0;
            return previous;
        }

        /// <summary>Ends a draw pass opened with <see cref="EnterDrawScope" />.</summary>
        internal static void ExitDrawScope(int previous) => s_openContainers = previous;

        internal static Rect BeginCard()
        {
            Styles.EnsureStyles();
            s_openContainers++;
            Rect rect = EditorGUILayout.BeginVertical(Styles.CardBody);
            Draw.FillAndStrokeRounded(rect, Tokens.CardBg, Tokens.CardBorder, Tokens.CardRadius);
            return rect;
        }

        /// <summary>Ends a card opened with <see cref="BeginCard" />.</summary>
        internal static void EndCard(float bottomSpace = Tokens.CardSpacing)
        {
            if (s_openContainers <= 0)
                return;

            s_openContainers--;
            EditorGUILayout.EndVertical();
            GUILayout.Space(bottomSpace);
        }

        /// <summary>Opens a section card for a <c>using</c> block: <c>using (Frame.Card()) { … }</c>.</summary>
        /// <remarks>
        ///     A factory, and <see cref="CardScope" />'s constructor deliberately takes no default,
        ///     because <c>new CardScope()</c> did not open a card at all. On a <em>struct</em> whose
        ///     only constructor has optional parameters, C# does not treat that constructor as the
        ///     parameterless one: <c>new CardScope()</c> compiles to the zero-initialised value and
        ///     never runs the constructor body. <see cref="BeginCard" /> therefore never ran while
        ///     <c>Dispose</c> still called <see cref="EndCard" />, so a card drew no frame and closed a
        ///     layout group it had never opened. Requiring the argument makes that form a compile
        ///     error; this method is the ergonomic way to open one.
        /// </remarks>
        internal static CardScope Card(float bottomSpace = Tokens.CardSpacing) => new(bottomSpace);

        /// <summary><c>using</c>-friendly form of <see cref="BeginCard" />/<see cref="EndCard" />.</summary>
        internal readonly struct CardScope : IDisposable
        {
            private readonly float _bottomSpace;

            /// <summary>Open one through <see cref="Card" /> — see its remarks for why this has no default.</summary>
            internal CardScope(float bottomSpace)
            {
                _bottomSpace = bottomSpace;
                BeginCard();
            }

            public void Dispose() => EndCard(_bottomSpace);
        }

        /// <summary>
        ///     Begins a panel nested inside a card. Pass a status colour for a tinted panel
        ///     (success/warning/error) or <c>null</c> for a neutral recessed one.
        ///     <paramref name="accentBarWidth" /> draws a vertical accent bar down the left edge.
        /// </summary>
        internal static Rect BeginPanel(Color? statusColor = null, float accentBarWidth = 0f)
        {
            Styles.EnsureStyles();
            s_openContainers++;
            Rect rect = EditorGUILayout.BeginVertical(Styles.PanelBody);

            if (statusColor.HasValue)
            {
                Draw.FillAndStrokeRounded(
                    rect, Tokens.Tint(statusColor.Value), Tokens.TintBorder(statusColor.Value), Tokens.PanelRadius);
            }
            else
            {
                Draw.FillAndStrokeRounded(rect, Tokens.InnerBg, Tokens.CardBorder, Tokens.PanelRadius);
            }

            if (accentBarWidth > 0f)
            {
                Draw.FillRounded(
                    new Rect(rect.x, rect.y + 4f, accentBarWidth, rect.height - 8f),
                    statusColor ?? Tokens.Accent,
                    accentBarWidth * 0.5f);
            }

            return rect;
        }

        /// <summary>Ends a panel opened with <see cref="BeginPanel" />.</summary>
        internal static void EndPanel(float bottomSpace = 6f)
        {
            if (s_openContainers <= 0)
                return;

            s_openContainers--;
            EditorGUILayout.EndVertical();
            GUILayout.Space(bottomSpace);
        }

        /// <summary>
        ///     Opens a nested panel for a <c>using</c> block: <c>using (Frame.Panel()) { … }</c>.
        ///     A factory for the same reason <see cref="Card" /> is one.
        /// </summary>
        internal static PanelScope Panel(
            Color? statusColor = null, float bottomSpace = 6f, float accentBarWidth = 0f) =>
            new(statusColor, bottomSpace, accentBarWidth);

        /// <summary><c>using</c>-friendly form of <see cref="BeginPanel" />/<see cref="EndPanel" />.</summary>
        internal readonly struct PanelScope : IDisposable
        {
            private readonly float _bottomSpace;

            /// <summary>Open one through <see cref="Panel" /> — see <see cref="Card" /> for why.</summary>
            internal PanelScope(Color? statusColor, float bottomSpace, float accentBarWidth)
            {
                _bottomSpace = bottomSpace;
                BeginPanel(statusColor, accentBarWidth);
            }

            public void Dispose() => EndPanel(_bottomSpace);
        }

        /// <summary>
        ///     Begins a height-capped scroll region: a list taller than <paramref name="maxHeight" />
        ///     keeps its full contents but is bounded to that height and scrolls inside it, while a
        ///     shorter one draws at its natural height with no scrollbar. Pair with
        ///     <see cref="EndScrollRegion" />; returns the new scroll position.
        /// </summary>
        /// <remarks>
        ///     Exists so a preview list in an inspector cannot grow without limit — the alternative
        ///     an inspector reaches for is truncating to the first few rows and adding an
        ///     "…and N more" line, which shows less than the user asked to see. Horizontal scrolling
        ///     is suppressed: these regions are always full-width lists.
        /// </remarks>
        /// <param name="contentHeight">Measured height of everything drawn inside the region.</param>
        /// <param name="maxHeight">The tallest the region may become before it starts scrolling.</param>
        internal static Vector2 BeginScrollRegion(Vector2 scroll, float contentHeight, float maxHeight)
        {
            Styles.EnsureStyles();
            s_openContainers++;
            return EditorGUILayout.BeginScrollView(
                scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none,
                // One pixel of slack under the cap: a region sized to exactly its content picks up a
                // scrollbar the moment the layout inside it rounds up by a fraction.
                GUILayout.Height(Mathf.Min(contentHeight + 1f, maxHeight)));
        }

        /// <summary>Ends a region opened with <see cref="BeginScrollRegion" />.</summary>
        internal static void EndScrollRegion(float bottomSpace = 0f)
        {
            if (s_openContainers <= 0)
                return;

            s_openContainers--;
            EditorGUILayout.EndScrollView();
            if (bottomSpace > 0f)
                GUILayout.Space(bottomSpace);
        }

        #endregion

        #region Prose

        /// <summary>
        ///     A wrapped paragraph of explanatory copy that reserves the height it will actually be
        ///     drawn at, so the line under it is never overlapped and its last line is never clipped.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why this is not <c>GUILayout.Label</c>.</b> IMGUI resolves heights during the
        ///         layout pass and widths after it, so a word-wrapped label is measured at the width it
        ///         <em>asks</em> for, not the width it gets. The two disagree by exactly the vertical
        ///         scrollbar whenever a pane grows tall enough to need one — which is why the defect
        ///         only ever showed on the paragraphs sitting near a line break: the layout pass counted
        ///         one line, the repaint wrapped to two, and the extra line drew straight through the
        ///         section rule above it and the control below.
        ///     </para>
        ///     <para>
        ///         The fix is the one <see cref="Controls.ProseField" /> already uses for the same
        ///         reason: record the rect's real width on repaint and measure the next pass against it.
        ///         A window resize settles in the frame that repaints the resize.
        ///     </para>
        ///     <para>
        ///         <b>There is no key parameter, on purpose.</b> A remembered width has to be filed
        ///         under something, and making callers invent that something is what would keep this
        ///         from replacing <c>GUILayout.Label</c> everywhere: a design-system primitive that
        ///         costs more to call than the wrong thing does not get called. Paragraphs are filed
        ///         under their style and their text instead, which needs nothing from the caller and is
        ///         what the width actually depends on. Two call sites sharing a string would share a
        ///         width — the copy in this SDK is authored as distinct <see cref="GUIContent" />
        ///         constants per surface, and the cost if it ever happened is one frame at the other
        ///         site's height, not a wrong layout.
        ///     </para>
        /// </remarks>
        internal static void Paragraph(GUIContent content, GUIStyle style)
        {
            Styles.EnsureStyles();
            if (content == null || style == null)
                return;

            var key = new ParagraphKey(style, content.text);
            ParagraphWidths.TryGetValue(key, out float lastWidth);

            // Before this paragraph has ever been drawn there is no real width to measure against, so
            // it is measured against a deliberate under-estimate of the content column. Guessing narrow
            // is the safe direction: too narrow reserves a line too many, which costs a strip of empty
            // card, while too wide reserves a line too few, which is the clipping this exists to stop.
            float width = lastWidth > 1f
                ? lastWidth
                : Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 60f);

            // Measured a hair narrower than the paragraph will be drawn, and the same reasoning
            // decides the direction. CalcHeight and the text generator that actually draws the label
            // do not have to agree to the pixel — fractional rect widths round independently on the
            // two paths — so a line ending flush against the right edge can measure as fitting and
            // then wrap. The slack costs a blank line in that one borderline case and removes the
            // whole class of "measured one line, drew two".
            float height = ConvaiEditorTextMetrics.WrappedHeight(
                style, content.text, Mathf.Max(40f, width - MeasurementSlack));
            Rect rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint && !Mathf.Approximately(rect.width, lastWidth))
            {
                // Bounded for the reason ConvaiEditorTextMetrics is: an unbounded dictionary in a
                // static editor field is a leak that survives until the next domain reload, and the
                // only traffic that can reach the cap is text that changes every frame.
                if (ParagraphWidths.Count >= ParagraphWidthCapacity)
                    ParagraphWidths.Clear();

                ParagraphWidths[key] = rect.width;

                // This frame was laid out against a width we did not have yet, so it can still be a
                // line short — the state right after a domain reload, when the table is empty and the
                // only width available is an estimate. Reporting it lets the host draw once more; see
                // ConsumeMeasurementChange for why the primitive cannot repaint the window itself.
                s_measurementChanged = true;
            }

            GUI.Label(rect, content, style);
        }

        /// <summary>String form of <see cref="Paragraph(GUIContent,GUIStyle)" />.</summary>
        internal static void Paragraph(string text, GUIStyle style)
        {
            ScratchParagraph.text = text ?? string.Empty;
            Paragraph(ScratchParagraph, style);
        }

        /// <summary>Scratch content so string-form paragraphs allocate nothing per repaint.</summary>
        private static readonly GUIContent ScratchParagraph = new();

        /// <summary>Set when a paragraph learned a width the pass was not laid out against.</summary>
        private static bool s_measurementChanged;

        /// <summary>
        ///     Whether a paragraph learned its real width during this pass, and clears the flag. A host
        ///     that draws paragraphs calls this at the end of its <c>OnGUI</c> and repaints when it
        ///     returns true; one extra pass then draws every paragraph at its settled height.
        /// </summary>
        /// <remarks>
        ///     Reported rather than acted on, because a static drawing primitive has no supported way to
        ///     reach the window currently drawing it: <see cref="EditorWindow.focusedWindow" /> is the
        ///     focused window, which during a repaint is very often a different one. The host knows
        ///     which window it is, so the host is where the repaint belongs. Without this, a paragraph
        ///     whose width is not yet known — every paragraph on the first pass after a domain reload —
        ///     stays a line short until something else happens to repaint the window, which for an
        ///     editor window can be a mouse move away.
        /// </remarks>
        internal static bool ConsumeMeasurementChange()
        {
            bool changed = s_measurementChanged;
            s_measurementChanged = false;
            return changed;
        }

        /// <summary>
        ///     Each paragraph's width as of its last repaint. Static, like <c>ProseField</c>'s table:
        ///     a paragraph's width belongs to the place it sits in the UI, not to an editor instance.
        /// </summary>
        private static readonly Dictionary<ParagraphKey, float> ParagraphWidths = new();

        /// <summary>Remembered widths kept before the table is dropped. One screen of copy is far below it.</summary>
        private const int ParagraphWidthCapacity = 512;

        /// <summary>
        ///     Pixels of measuring slack — see <see cref="Paragraph(GUIContent,GUIStyle)" />. Two, not
        ///     one: rounding can go against us on both the measured and the drawn side.
        /// </summary>
        private const float MeasurementSlack = 2f;

        /// <summary>
        ///     Identifies one paragraph by what its wrapped height depends on. The style is compared by
        ///     reference for the reason <see cref="ConvaiEditorTextMetrics" /> gives: style instances
        ///     are stable between rebuilds, so reference identity is both cheaper and stricter than
        ///     comparing their contents.
        /// </summary>
        private readonly struct ParagraphKey : IEquatable<ParagraphKey>
        {
            private readonly GUIStyle _style;
            private readonly string _text;

            internal ParagraphKey(GUIStyle style, string text)
            {
                _style = style;
                _text = text ?? string.Empty;
            }

            public bool Equals(ParagraphKey other) =>
                ReferenceEquals(_style, other._style) &&
                string.Equals(_text, other._text, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is ParagraphKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_style), _text);
        }

        #endregion

        #region Section headers

        /// <summary>Scratch content so string-titled headers allocate nothing per repaint.</summary>
        private static readonly GUIContent ScratchTitle = new();

        /// <summary>Scratch content for a message box's action button, for the same reason.</summary>
        private static readonly GUIContent ScratchButton = new();

        /// <summary>Static section header inside a card: accent glyph, bold title, thin accent rule.</summary>
        internal static void SectionHeader(string glyph, GUIContent title)
        {
            Styles.EnsureStyles();
            Rect row = GUILayoutUtility.GetRect(0f, Tokens.SectionHeaderHeight, GUILayout.ExpandWidth(true));

            // Same glyph/title columns as SectionHeaderRow, so titles align across stacked cards
            // that mix static and collapsible sections.
            GUI.Label(new Rect(row.x + 2f, row.y, Tokens.SectionIconCellWidth, row.height), glyph,
                Styles.SectionIconTinted(Tokens.Accent, Tokens.SectionIconFontSize));
            GUI.Label(new Rect(row.x + 22f, row.y, row.width - 24f, row.height), title,
                // Accent, not primary: a section title is the brand's landmark down the inspector,
                // and the colour is what makes the whole SDK's sections scan as one product.
                Styles.SectionHeaderLabelTinted(Tokens.Accent));

            Draw.HorizontalRule(Tokens.AccentRule);
        }

        /// <summary>
        ///     The one collapsible section-header row of the design system: accent glyph, bold title,
        ///     optional right-aligned summary, trailing chevron, a hover plate under the pointer and an
        ///     accent underline once expanded. The whole row is a hit target. Returns the new expanded
        ///     state. Every collapsible header — plain or colour-coded — renders through this row, so a
        ///     section reads the same in every Convai editor.
        /// </summary>
        /// <remarks>
        ///     <paramref name="accent" /> colours the glyph, the title and the underline together.
        ///     It is deliberately <em>not</em> possible to colour the title separately: when it was,
        ///     the two callers of this row passed different values and Body Animation's sections ended
        ///     up with white titles while every other inspector's were brand green. One colour in,
        ///     one section look out.
        /// </remarks>
        internal static bool SectionHeaderRow(
            string glyph, GUIContent title, bool expanded, Color accent,
            string summary = null, int glyphSize = Tokens.SectionIconFontSize)
        {
            Styles.EnsureStyles();

            Rect row = GUILayoutUtility.GetRect(0f, Tokens.SectionHeaderHeight, GUILayout.ExpandWidth(true));
            bool hover = GUI.enabled && row.Contains(Event.current.mousePosition);
            if (hover)
                Draw.FillRounded(row, Tokens.HoverPlate, 4f);

            GUI.Label(new Rect(row.x + 2f, row.y, Tokens.SectionIconCellWidth, row.height), glyph,
                Styles.SectionIconTinted(accent, glyphSize));

            // Small triangles, not full-size ones: a disclosure arrow is navigation, so it must stay
            // quieter than the section's own mark — and ▼/▶ at full size collided with the Validation
            // and Run glyphs, making the chevron read as a second section mark on the same row.
            var chevronRect = new Rect(row.xMax - 16f, row.y, Tokens.SectionChevronCellWidth, row.height);
            GUI.Label(chevronRect, expanded ? "▾" : "▸",
                Styles.SectionChevronTinted(hover ? accent : Tokens.Fade(accent, 0.75f)));

            float titleX = row.x + 22f;
            float titleRight = chevronRect.x - 4f;
            if (!string.IsNullOrEmpty(summary))
            {
                float summaryWidth = Mathf.Min(
                    (titleRight - titleX) * 0.55f,
                    ConvaiEditorTextMetrics.Width(Styles.MicroLabel, summary) + 4f);
                GUI.Label(new Rect(titleRight - summaryWidth, row.y, summaryWidth, row.height),
                    summary, Styles.MicroLabel);
                titleRight -= summaryWidth + 4f;
            }

            GUI.Label(new Rect(titleX, row.y, titleRight - titleX, row.height), title,
                Styles.SectionHeaderLabelTinted(accent));

            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && row.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
                GUI.changed = true;
            }

            if (expanded)
            {
                GUILayout.Space(2f);
                Rect line = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
                line.x += 2f;
                line.width -= 4f;
                Draw.DividerLine(line, Tokens.Fade(accent, 0.3f));
                GUILayout.Space(6f);
            }
            else
            {
                GUILayout.Space(2f);
            }

            return expanded;
        }

        /// <summary>String-titled form of <see cref="SectionHeaderRow" /> for spec-driven callers.</summary>
        internal static bool SectionHeaderRow(
            string glyph, string title, bool expanded, Color accent,
            string summary = null, int glyphSize = Tokens.SectionIconFontSize)
        {
            ScratchTitle.text = title;
            return SectionHeaderRow(glyph, ScratchTitle, expanded, accent, summary, glyphSize);
        }

        /// <summary>
        ///     Collapsible section header in the standard accent: the plain form of
        ///     <see cref="SectionHeaderRow" />. Returns the new expanded state.
        /// </summary>
        /// <param name="summary">
        ///     Optional right-aligned summary of what the collapsed section contains, so a user can
        ///     read the state without expanding it.
        /// </param>
        internal static bool CollapsibleSectionHeader(
            string glyph, GUIContent title, bool expanded, string summary = null) =>
            SectionHeaderRow(glyph, title, expanded, Tokens.Accent, summary);

        #endregion

        #region Message boxes

        /// <summary>
        ///     Neutral explanation, with an optional one-click action. Never signals a problem.
        /// </summary>
        /// <remarks>
        ///     The button matters as much as the tone: "this asset ships with the SDK, here is how to
        ///     make it yours" is information plus a next step, not a warning. Drawing it in the warn
        ///     colour just to get a button tells a first-time user something is wrong when nothing is.
        /// </remarks>
        internal static void InfoBox(
            string title, string message, string buttonText = null, Action buttonAction = null) =>
            MessageBox(Tokens.StatusInfo, "i", title, message, buttonText, buttonAction);

        /// <summary>Something the user should act on, with an optional one-click fix.</summary>
        internal static void WarningBox(string title, string message, string buttonText = null, Action buttonAction = null) =>
            MessageBox(Tokens.StatusWarn, "!", title, message, buttonText, buttonAction);

        /// <summary>The feature cannot work until this is resolved.</summary>
        internal static void ErrorBox(string title, string message, string buttonText = null, Action buttonAction = null) =>
            MessageBox(Tokens.StatusError, "×", title, message, buttonText, buttonAction);

        /// <summary>
        ///     A Convai message box: status-tinted surface with a left accent bar, a glyph, a bold
        ///     title, wrapped body copy and an optional action button.
        /// </summary>
        /// <remarks>
        ///     Laid out with explicit rects rather than nested layout groups because the body height
        ///     must be measured before the surface is drawn — a layout-group version would draw the
        ///     background a frame behind the text it is meant to sit under.
        /// </remarks>
        internal static void MessageBox(
            Color accent, string glyph, string title, string body, string buttonText, Action buttonAction)
        {
            Styles.EnsureStyles();

            const float paddingX = 10f;
            const float paddingY = 8f;
            const float accentWidth = 2f;
            const float glyphWidth = 22f;
            const float glyphGap = 8f;
            const float titleHeight = 16f;
            const float titleBodyGap = 2f;
            const float buttonHeight = 20f;
            const float buttonTopGap = 6f;

            // Measured against the width this box was drawn at last frame, not against the window
            // width. A message box nested in a card sits inside padding the window knows nothing
            // about, so currentViewWidth over-estimates the body column: the copy was measured for a
            // column wider than the surface it is drawn on, and the last words of a line ran past the
            // right edge. Same remembered-width idiom as Paragraph, keyed on the body style and text.
            var widthKey = new ParagraphKey(Styles.MessageBody, body ?? string.Empty);
            ParagraphWidths.TryGetValue(widthKey, out float lastBodyWidth);

            // Before this box has ever been drawn there is no real width, so it falls back to a
            // deliberate under-estimate of the content column — guessing narrow reserves a line too
            // many, which costs a strip of empty surface, rather than a line too few, which clips.
            float bodyWidth = lastBodyWidth > 1f
                ? lastBodyWidth
                : Mathf.Max(
                    80f,
                    Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 46f)
                    - paddingX - glyphWidth - glyphGap - paddingX);

            // Through the metrics cache, not GUIStyle.CalcHeight directly. A message box is drawn
            // inside a draw pass, so measuring its wrapped body here costs a full text-generator run
            // on every repaint — and an inspector repaints on every mouse move over it.
            float bodyHeight = ConvaiEditorTextMetrics.WrappedHeight(
                Styles.MessageBody, body, Mathf.Max(40f, bodyWidth - MeasurementSlack));
            bool hasButton = !string.IsNullOrWhiteSpace(buttonText) && buttonAction != null;

            float contentHeight = titleHeight + titleBodyGap + bodyHeight;
            if (hasButton)
                contentHeight += buttonTopGap + buttonHeight;
            float totalHeight = Mathf.Max(48f, paddingY + contentHeight + paddingY);

            GUILayout.Space(8f);
            Rect rect = GUILayoutUtility.GetRect(0f, totalHeight, GUILayout.ExpandWidth(true));
            rect.x += 2f;
            rect.width -= 4f;

            // The column the text is actually drawn in, derived from the surface rather than assumed.
            // Text is laid out against this, so it can never overrun the box even on the first frame,
            // when the height came from the estimate above.
            float drawnBodyWidth = Mathf.Max(40f, rect.width - paddingX - glyphWidth - glyphGap - paddingX);
            if (Event.current.type == EventType.Repaint && !Mathf.Approximately(drawnBodyWidth, lastBodyWidth))
            {
                if (ParagraphWidths.Count >= ParagraphWidthCapacity)
                    ParagraphWidths.Clear();

                ParagraphWidths[widthKey] = drawnBodyWidth;

                // Laid out against a width we did not have yet, so this pass can still be a line
                // short; see ConsumeMeasurementChange for why the primitive cannot repaint itself.
                s_measurementChanged = true;
            }

            Draw.FillRounded(rect, Tokens.MessagePanelBg, Tokens.PanelRadius);
            Draw.FillRounded(rect, Tokens.Tint(accent), Tokens.PanelRadius);
            Draw.StrokeRounded(rect, Tokens.TintBorder(accent), Tokens.PanelRadius);
            Draw.FillRounded(
                new Rect(rect.x + 1f, rect.y + 4f, accentWidth, rect.height - 8f), accent, accentWidth * 0.5f);

            GUIStyle icon = Styles.MessageIconTinted(accent);

            float contentX = rect.x + paddingX;
            float contentY = rect.y + paddingY;
            var glyphRect = new Rect(contentX, contentY, glyphWidth, titleHeight);
            var titleRect = new Rect(glyphRect.xMax + glyphGap, contentY, drawnBodyWidth, titleHeight);
            var bodyRect = new Rect(titleRect.x, titleRect.yMax + titleBodyGap, drawnBodyWidth, bodyHeight);

            GUI.Label(glyphRect, glyph, icon);
            GUI.Label(titleRect, title, Styles.MessageTitle);
            GUI.Label(bodyRect, body, Styles.MessageBody);

            if (hasButton)
            {
                var buttonRect = new Rect(
                    titleRect.x, bodyRect.yMax + buttonTopGap, Mathf.Min(180f, bodyWidth), buttonHeight);
                ScratchButton.text = buttonText;
                if (Controls.GhostButton(buttonRect, ScratchButton))
                    buttonAction();
            }

            GUILayout.Space(10f);
        }

        #endregion

        #region Tables

        /// <summary>
        ///     Opens a table header row: a recessed strip with a hairline under it. Draw the columns
        ///     inside with <see cref="TableColumn" />, then dispose.
        /// </summary>
        /// <remarks>
        ///     Built at the call site out of <c>GUILayout.Label</c> plus a hand-drawn rect, the same
        ///     table acquires a different header tint and a different rule on every surface that draws
        ///     it. The header owns its own surface here, which is what keeps them identical.
        /// </remarks>
        internal static TableHeaderScope TableHeader(float height = Tokens.TableHeaderHeight) => new(height);

        /// <inheritdoc cref="TableHeader" />
        internal readonly struct TableHeaderScope : IDisposable
        {
            /// <summary>Open one through <see cref="TableHeader" /> — see <see cref="Card" /> for why.</summary>
            public TableHeaderScope(float height)
            {
                Styles.EnsureStyles();
                Rect rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(height));
                Draw.Fill(rect, Tokens.TableHeaderBg);
                Draw.DividerLine(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Tokens.Divider);
            }

            public void Dispose() => EditorGUILayout.EndHorizontal();
        }

        /// <summary>One column heading inside a <see cref="TableHeaderScope" />.</summary>
        internal static void TableColumn(GUIContent label, float width, bool centered = false)
        {
            Styles.EnsureStyles();
            GUILayout.Label(
                label,
                centered ? Styles.TableHeaderCellCentered : Styles.TableHeaderCell,
                GUILayout.Width(width));
        }

        /// <summary>
        ///     Reserves the full width of the open <see cref="TableHeaderScope" /> at the header
        ///     strip's height, for a table whose column titles are placed by absolute rect so they
        ///     share an x with the cells below.
        /// </summary>
        internal static Rect ReserveTableHeaderRect() => ReserveScopeRect(Tokens.TableHeaderHeight);

        /// <summary>
        ///     Places one column title inside a header reserved with
        ///     <see cref="ReserveTableHeaderRect" />. <paramref name="x" /> and
        ///     <paramref name="width" /> are relative to <paramref name="header" /> and must match
        ///     the cell rects in the rows below.
        /// </summary>
        internal static void TableHeaderLabel(
            Rect header, float x, float width, GUIContent label, bool right = false)
        {
            Styles.EnsureStyles();
            if (width <= 0f)
                return;

            GUI.Label(
                new Rect(header.x + x, header.y, width, header.height),
                label,
                right ? Styles.TableHeaderCellRight : Styles.TableHeaderCell);
        }

        /// <summary>
        ///     Opens one table body row, zebra-striped on odd <paramref name="rowIndex" /> so a long
        ///     table stays readable across its width. Draw cells inside with
        ///     <see cref="TableCell" />, then dispose.
        /// </summary>
        internal readonly struct TableRowScope : IDisposable
        {
            public TableRowScope(int rowIndex, float height = Tokens.TableRowHeight)
            {
                Styles.EnsureStyles();
                Rect rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(height));
                if ((rowIndex & 1) == 1)
                    Draw.Fill(rect, Tokens.RowAlt);
            }

            public void Dispose() => EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        ///     Reserves the full width of the header or row scope you are inside and returns its rect,
        ///     for a table whose columns are placed by absolute rect rather than by sequential cells.
        /// </summary>
        /// <remarks>
        ///     Two kinds of table exist here and both are legitimate. One stacks read-only cells left
        ///     to right — that one uses <see cref="TableColumn" /> and <see cref="TableCell" /> and
        ///     never needs this. The other has editable controls whose columns must line up with the
        ///     header above them to the pixel; it computes column rects itself and needs the row rect
        ///     to compute them against. This exists so that second kind asks for the rect by name
        ///     rather than by re-deriving the <c>GUIContent.none</c> reservation idiom at each site.
        /// </remarks>
        internal static Rect ReserveScopeRect(float height) =>
            GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(height));

        /// <summary>One cell inside a <see cref="TableRowScope" />.</summary>
        internal static void TableCell(
            string text, float width, Color? tint = null, bool centered = false)
        {
            Styles.EnsureStyles();
            GUILayout.Label(
                text ?? string.Empty,
                tint.HasValue
                    ? Styles.TableCellTinted(tint.Value, centered)
                    : centered ? Styles.TableCellCentered : Styles.TableCell,
                GUILayout.Width(width));
        }

        #endregion

        #region Placeholders

        /// <summary>Centred muted note shown where live data will appear once the scene is playing.</summary>
        internal static void OfflinePlaceholder(string message = "Enter Play Mode to view live telemetry.")
        {
            Styles.EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(message, Styles.CenteredMini(Tokens.TextMuted));
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>
        ///     What a list, table or picker shows when it has nothing in it: a large muted mark, a
        ///     one-line headline naming the situation, a sentence of plain-English cause, and
        ///     optionally the single action that resolves it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An empty view with no copy reads as a broken view. The headline says what is
        ///         missing, the body says why, and the button — when there is an obvious next step —
        ///         does it. Never use this for an error; an unusable feature is a
        ///         <see cref="ErrorBox" />.
        ///     </para>
        ///     <para>
        ///         <paramref name="action" /> runs inside this draw pass, so anything that opens a
        ///         dialog, writes an asset or refreshes the asset database must defer itself with
        ///         <see cref="EditorApplication.delayCall" /> — raising a modal from inside a layout
        ///         scope corrupts the layout for the rest of the editor session.
        ///     </para>
        /// </remarks>
        internal static void EmptyState(
            string glyph, string title, string message, GUIContent actionLabel = null, Action action = null)
        {
            Styles.EnsureStyles();

            GUILayout.Space(14f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(320f)))
                {
                    if (!string.IsNullOrEmpty(glyph))
                    {
                        GUILayout.Label(glyph, Styles.EmptyStateGlyph, GUILayout.Height(34f));
                        GUILayout.Space(2f);
                    }

                    if (!string.IsNullOrEmpty(title))
                        GUILayout.Label(title, Styles.EmptyStateTitle);

                    if (!string.IsNullOrEmpty(message))
                    {
                        GUILayout.Space(2f);
                        GUILayout.Label(message, Styles.CenteredBody);
                    }

                    if (actionLabel != null && action != null)
                    {
                        GUILayout.Space(10f);
                        if (Controls.PrimaryButtonLayout(actionLabel, 26f))
                            action();
                    }
                }

                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(14f);
        }

        #endregion
    }
}
