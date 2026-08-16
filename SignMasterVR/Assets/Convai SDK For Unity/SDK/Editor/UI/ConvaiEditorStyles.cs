using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The cached <see cref="GUIStyle" /> set for the Convai editor design system. Every label,
    ///     caption, title and container style used anywhere in Convai's editor UI is declared here
    ///     once and reused, so typography stays consistent across inspectors, asset editors and module
    ///     windows.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Built once.</b> Constructing a <see cref="GUIStyle" /> inside <c>OnGUI</c> allocates
    ///         on every repaint, and an inspector repaints continuously in Play mode. Styles are built
    ///         on first use and rebuilt only when the editor skin flips.
    ///     </para>
    ///     <para>
    ///         <b>Headless-safe.</b> <see cref="EditorStyles" /> can be unavailable when inspectors are
    ///         constructed by batch-mode test runs or during editor bootstrap, and touching it then
    ///         throws. Every style therefore falls back through
    ///         <c>EditorStyles → GUI.skin → new GUIStyle()</c> rather than assuming a live skin.
    ///     </para>
    ///     <para>
    ///         <b>Shared mutable styles.</b> A few styles (<see cref="CenteredMini" />,
    ///         <see cref="TileNumber" />) have their text colour set per call site. That is safe
    ///         because IMGUI reads the style synchronously during the same <c>GUI.Label</c> call; it is
    ///         deliberately preferred over allocating a tinted style per label per frame.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorStyles
    {
        private static bool s_ready;
        private static bool s_lastProSkin;
        private static int s_generation;

        /// <summary>
        ///     Increments every time the style set is rebuilt — that is, on first use and on each
        ///     editor skin flip.
        /// </summary>
        /// <remarks>
        ///     Anything that caches a result derived from these styles watches this number to know
        ///     when its cache went stale. <see cref="ConvaiEditorTextMetrics" /> is the reason it
        ///     exists: a measured text height is only valid for the style instance and font that
        ///     produced it, and a skin flip replaces both.
        /// </remarks>
        internal static int Generation => s_generation;

        #region Headers and titles

        /// <summary>Window hero banner title.</summary>
        internal static GUIStyle HeroTitle { get; private set; }

        /// <summary>Line under a hero or inspector title.</summary>
        internal static GUIStyle HeroSubtitle { get; private set; }

        /// <summary>Convai component-inspector title (accent, bold).</summary>
        internal static GUIStyle InspectorTitle { get; private set; }

        /// <summary>Bold title of a section inside a card.</summary>
        internal static GUIStyle SectionTitle { get; private set; }

        /// <summary>Accent glyph to the left of a section title.</summary>
        internal static GUIStyle SectionGlyph { get; private set; }

        /// <summary>
        ///     Heading over one group of rows in a list pane. Deliberately not
        ///     <see cref="GroupLabel" />: that is a caption for a field cluster inside a card, and a
        ///     list heading that reads smaller and fainter than the rows it heads inverts the
        ///     hierarchy of the list it is meant to organise.
        /// </summary>
        internal static GUIStyle ListGroupHeader { get; private set; }

        /// <summary>One-line italic explanation under a section header.</summary>
        internal static GUIStyle SectionSummary { get; private set; }

        /// <summary>Large title for the selected item in a window's detail pane.</summary>
        internal static GUIStyle SelectedTitle { get; private set; }

        /// <summary>Title of a stateful collapsible section. Callers tint per section.</summary>
        internal static GUIStyle SectionHeaderLabel { get; private set; }

        /// <summary>Fixed-width glyph cell of a stateful collapsible section header.</summary>
        internal static GUIStyle SectionIcon { get; private set; }

        /// <summary>Fixed-width chevron cell of a stateful collapsible section header.</summary>
        internal static GUIStyle SectionChevron { get; private set; }

        /// <summary>Centred title for an empty-state or full-pane message.</summary>
        internal static GUIStyle CenteredTitle { get; private set; }

        #endregion

        #region Body text

        /// <summary>Wrapped primary body text.</summary>
        internal static GUIStyle BodyWrapped { get; private set; }

        /// <summary>Wrapped de-emphasised body text — the default for explanations.</summary>
        internal static GUIStyle MutedWrapped { get; private set; }

        /// <summary>Small wrapped caption text.</summary>
        internal static GUIStyle CaptionWrapped { get; private set; }

        /// <summary>Uppercase micro label for counts and inline annotations.</summary>
        internal static GUIStyle MicroLabel { get; private set; }

        /// <summary>
        ///     Caption over a cluster of controls — a character-type picker, a group of related
        ///     fields, a pane's list. Draw it through
        ///     <see cref="ConvaiEditorControls.GroupCaption(GUIContent)" /> rather than by hand: the
        ///     caption's whole job is to sit on the same left edge as the thing it names, and half the
        ///     call sites reached it through <see cref="EditorGUILayout.LabelField(string,GUIStyle,GUILayoutOption[])" />,
        ///     which applies <see cref="EditorGUI.indentLevel" /> while the control underneath does
        ///     not — so the same caption hung fifteen pixels off its own picker in one inspector and
        ///     flush against it in the next.
        /// </summary>
        internal static GUIStyle GroupLabel { get; private set; }

        /// <summary>
        ///     Fixed-width label in the left column of a check or status row — the "Skeleton", "Eye
        ///     bones", "Profile" side of a two-column readout.
        /// </summary>
        /// <remarks>
        ///     Split out of <see cref="GroupLabel" />, which it shared until the caption grew a size
        ///     to stop reading as a footnote. These rows are laid out against hand-measured column
        ///     widths (<c>GUILayout.Width(90f)</c> and friends), so a caption that grows takes their
        ///     labels with it and clips them — one style, two jobs, and only one of them free to change.
        /// </remarks>
        internal static GUIStyle RowLabel { get; private set; }

        /// <summary>Right-aligned micro label — trailing counts and annotations on a row.</summary>
        internal static GUIStyle MicroLabelRight { get; private set; }

        /// <summary>Column heading in a data table.</summary>
        internal static GUIStyle TableHeaderLabel { get; private set; }

        /// <summary>
        ///     Column heading of a fixed-height table header row. Same typography as
        ///     <see cref="TableHeaderLabel" />, but with the row height and zero margins the mapping
        ///     tables lay their columns out against. <see cref="GUIStyle.fixedHeight" /> is
        ///     <see cref="ConvaiEditorTokens.TableHeaderHeight" /> so a GUILayout header cannot
        ///     overflow its own strip.
        /// </summary>
        internal static GUIStyle TableHeaderCell { get; private set; }

        /// <summary>Centred variant of <see cref="TableHeaderCell" /> for numeric columns.</summary>
        internal static GUIStyle TableHeaderCellCentered { get; private set; }

        /// <summary>Right-aligned variant of <see cref="TableHeaderCell" /> for duration and timing columns.</summary>
        internal static GUIStyle TableHeaderCellRight { get; private set; }

        /// <summary>Left column of a key/value reading row — the name of the thing being reported.</summary>
        internal static GUIStyle ReadingLabel { get; private set; }

        /// <summary>Right column of a key/value reading row — the value itself.</summary>
        internal static GUIStyle ReadingValue { get; private set; }

        /// <summary>One cell of a table body row.</summary>
        internal static GUIStyle TableCell { get; private set; }

        /// <summary>A table body cell whose content reads better centred (counts, states, arrows).</summary>
        internal static GUIStyle TableCellCentered { get; private set; }

        /// <summary>The large muted mark an empty state leads with.</summary>
        internal static GUIStyle EmptyStateGlyph { get; private set; }

        /// <summary>An empty state's one-line headline.</summary>
        internal static GUIStyle EmptyStateTitle { get; private set; }

        /// <summary>Centred wrapped body text for empty states.</summary>
        internal static GUIStyle CenteredBody { get; private set; }

        /// <summary>Italic wrapped quote — a preview of what the character will say or do.</summary>
        internal static GUIStyle PreviewQuote { get; private set; }

        /// <summary>Footer text at the bottom of a window.</summary>
        internal static GUIStyle FooterLabel { get; private set; }

        /// <summary>Centred clickable link.</summary>
        internal static GUIStyle Link { get; private set; }

        #endregion

        #region List cards

        /// <summary>Name of an unselected row in a list.</summary>
        internal static GUIStyle CardName { get; private set; }

        /// <summary>Name of the selected row in a list.</summary>
        internal static GUIStyle CardNameSelected { get; private set; }

        /// <summary>Bold title inside a side-pane status card.</summary>
        internal static GUIStyle CardTitle { get; private set; }

        /// <summary>Wrapped supporting line inside a side-pane status card.</summary>
        internal static GUIStyle CardSubtitle { get; private set; }

        #endregion

        #region Stat tiles and live cells

        /// <summary>Large centred number on a stat tile. Callers tint per draw.</summary>
        internal static GUIStyle TileNumber { get; private set; }

        /// <summary>Small centred caption under a stat tile's number.</summary>
        internal static GUIStyle TileLabel { get; private set; }

        /// <summary>Large headline number for a single dominant metric. Callers tint per draw.</summary>
        internal static GUIStyle MetricNumber { get; private set; }

        /// <summary>Uppercase caption above a live telemetry value.</summary>
        internal static GUIStyle LiveCellLabel { get; private set; }

        /// <summary>Live telemetry value. Callers tint and set weight per draw.</summary>
        internal static GUIStyle LiveCellValue { get; private set; }

        #endregion

        #region Message boxes

        /// <summary>Leading glyph of an info/warning/error box. Callers tint per draw.</summary>
        internal static GUIStyle MessageIcon { get; private set; }

        /// <summary>Bold title line of a message box.</summary>
        internal static GUIStyle MessageTitle { get; private set; }

        /// <summary>Wrapped body of a message box.</summary>
        internal static GUIStyle MessageBody { get; private set; }

        #endregion

        #region Search field

        internal static GUIStyle SearchText { get; private set; }
        internal static GUIStyle SearchPlaceholder { get; private set; }
        internal static GUIStyle SearchGlyph { get; private set; }

        /// <summary>
        ///     Unity's own toolbar search box at the SDK's row height, for the layout-driven table
        ///     surfaces that use <see cref="EditorGUILayout" /> rather than the design system's
        ///     rect-based <c>SearchField</c> control.
        /// </summary>
        internal static GUIStyle SearchFieldBox { get; private set; }

        /// <summary>
        ///     Muted text drawn inside an empty field, aligned to Unity's own field text. Guidance
        ///     that lives inside the control it describes costs no row of its own and disappears the
        ///     moment it stops being true, so it never becomes the same sentence repeated down a list
        ///     of entries.
        /// </summary>
        /// <remarks>
        ///     Italic on purpose, on top of the muted colour. A placeholder occupies the exact pixels a
        ///     real value would, so it has to be unmistakably not-a-value at a glance — otherwise an
        ///     example name reads as a name already filled in. Colour alone does not carry that: a
        ///     muted value is still a value. Placeholders here are also paint-only — they are drawn
        ///     over an empty control and can never be written back — so a field showing one is empty in
        ///     fact as well as in appearance.
        /// </remarks>
        internal static GUIStyle FieldPlaceholder { get; private set; }

        /// <summary>
        ///     Wrapping text field for authored prose. Identical to Unity's own field to look at, so a
        ///     form row does not announce which of its fields happens to be growable.
        /// </summary>
        internal static GUIStyle GrowingTextArea { get; private set; }

        #endregion

        #region Starter tiles

        /// <summary>Large accent glyph on a starter/template tile.</summary>
        internal static GUIStyle StarterGlyph { get; private set; }

        /// <summary>Centred bold name on a starter/template tile.</summary>
        internal static GUIStyle StarterName { get; private set; }

        /// <summary>Centred wrapped description on a starter/template tile.</summary>
        internal static GUIStyle StarterDesc { get; private set; }

        #endregion

        #region Containers

        /// <summary>Padded container for a section card body.</summary>
        internal static GUIStyle CardBody { get; private set; }

        /// <summary>Padded container for a panel nested inside a card.</summary>
        internal static GUIStyle PanelBody { get; private set; }

        /// <summary>Padded container for a window's scrolling content pane.</summary>
        internal static GUIStyle PaneContent { get; private set; }

        /// <summary>Fully transparent button: hit-testing and tooltips with no visuals of its own.</summary>
        internal static GUIStyle InvisibleButton { get; private set; }

        /// <summary>
        ///     Compact <c>GUILayout.Button</c> style for inline actions. Shared so that "a mini button,
        ///     a bit smaller, with a bit more padding" resolves to one set of numbers rather than to
        ///     whatever each inspector picked.
        /// </summary>
        internal static GUIStyle MiniButton { get; private set; }

        #endregion

        #region Control labels

        internal static GUIStyle PillLabel { get; private set; }
        internal static GUIStyle ChipLabel { get; private set; }
        internal static GUIStyle PrimaryButtonLabel { get; private set; }
        internal static GUIStyle GhostButtonLabel { get; private set; }
        internal static GUIStyle IconButtonLabel { get; private set; }

        #endregion

        #region Pooled tinted styles

        /// <summary>
        ///     The tinted value styles, one instance per distinct colour, built on first request.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Pooled rather than re-tinted, for correctness before performance.</b> These
        ///         helpers used to write the colour onto the shared base instance and hand that
        ///         instance back. The write is permanent — nothing restores it — so the base style
        ///         kept the last caller's colour for the rest of the editor session, and every later
        ///         <i>untinted</i> draw through the same style inherited it. The visible symptom was
        ///         the Actions Editor overview reporting healthy group counts in the error colour,
        ///         because some earlier reading somewhere in the editor had been tinted red. A style
        ///         handed out by this class must read the same on the tenth window as on the first.
        ///     </para>
        ///     <para>
        ///         The pool is the same mechanism, and safe for the same reason, as
        ///         <see cref="SectionHeaderLabelPool" />: tints come from the fixed token set — the
        ///         four status colours, the accent, muted text — so the live pool settles at a handful
        ///         of entries per style, and <see cref="PoolCapacity" /> is the backstop against a
        ///         caller that computes a colour per frame. It is also cheaper than re-tinting was:
        ///         IMGUI keys its generated text on the style instance, so mutating one instance
        ///         between two labels discarded text layout it had already cached.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<uint, GUIStyle> CenteredMiniPool = new();

        private static readonly Dictionary<uint, GUIStyle> TileNumberPool = new();
        private static readonly Dictionary<uint, GUIStyle> MetricNumberPool = new();
        private static readonly Dictionary<uint, GUIStyle> MicroLabelRightPool = new();
        private static readonly Dictionary<uint, GUIStyle> ReadingValuePool = new();
        private static readonly Dictionary<(uint Colour, bool Centered), GUIStyle> TableCellPool = new();
        private static readonly Dictionary<(uint Colour, bool Bold), GUIStyle> LiveCellValuePool = new();
        private static readonly Dictionary<uint, GUIStyle> PillLabelPool = new();
        private static readonly Dictionary<uint, GUIStyle> ChipLabelPool = new();
        private static readonly Dictionary<uint, GUIStyle> MessageIconPool = new();

        private static GUIStyle s_centeredMini;

        /// <summary>
        ///     Centred mini label in <paramref name="color" />. Pooled rather than allocated per use:
        ///     a coverage grid or compass draws many of these per repaint.
        /// </summary>
        internal static GUIStyle CenteredMini(Color color)
        {
            EnsureStyles();
            return Tinted(CenteredMiniPool, s_centeredMini, color);
        }

        /// <summary>Returns <see cref="TileNumber" /> in <paramref name="color" />.</summary>
        internal static GUIStyle TileNumberTinted(Color color)
        {
            EnsureStyles();
            return Tinted(TileNumberPool, TileNumber, color);
        }

        /// <summary>Returns <see cref="MetricNumber" /> in <paramref name="color" />.</summary>
        internal static GUIStyle MetricNumberTinted(Color color)
        {
            EnsureStyles();
            return Tinted(MetricNumberPool, MetricNumber, color);
        }

        /// <summary>Returns <see cref="MicroLabelRight" /> in <paramref name="color" />.</summary>
        internal static GUIStyle MicroLabelRightTinted(Color color)
        {
            EnsureStyles();
            return Tinted(MicroLabelRightPool, MicroLabelRight, color);
        }

        /// <summary>Returns <see cref="ReadingValue" /> in <paramref name="color" />.</summary>
        internal static GUIStyle ReadingValueTinted(Color color)
        {
            EnsureStyles();
            return Tinted(ReadingValuePool, ReadingValue, color);
        }

        /// <summary>Returns <see cref="TableCell" /> in <paramref name="color" />, centred or not.</summary>
        internal static GUIStyle TableCellTinted(Color color, bool centered = false)
        {
            EnsureStyles();

            (uint, bool) key = (ColourKey(color), centered);
            if (TableCellPool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(centered ? TableCellCentered : TableCell, color);
            Store(TableCellPool, key, pooled);
            return pooled;
        }

        /// <summary>Returns <see cref="LiveCellValue" /> in <paramref name="color" />, at the given weight.</summary>
        internal static GUIStyle LiveCellValueTinted(Color color, bool bold)
        {
            EnsureStyles();

            (uint, bool) key = (ColourKey(color), bold);
            if (LiveCellValuePool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(LiveCellValue, color);
            pooled.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            Store(LiveCellValuePool, key, pooled);
            return pooled;
        }

        /// <summary>Returns <see cref="PillLabel" /> in <paramref name="color" />.</summary>
        internal static GUIStyle PillLabelTinted(Color color)
        {
            EnsureStyles();
            return Tinted(PillLabelPool, PillLabel, color);
        }

        /// <summary>Returns <see cref="ChipLabel" /> in <paramref name="color" />, on every state.</summary>
        internal static GUIStyle ChipLabelTinted(Color color)
        {
            EnsureStyles();
            return Tinted(ChipLabelPool, ChipLabel, color);
        }

        /// <summary>Returns <see cref="MessageIcon" /> in <paramref name="color" />.</summary>
        internal static GUIStyle MessageIconTinted(Color color)
        {
            EnsureStyles();
            return Tinted(MessageIconPool, MessageIcon, color);
        }

        /// <summary>Pool lookup for the single-colour styles, which key on nothing but the colour.</summary>
        private static GUIStyle Tinted(Dictionary<uint, GUIStyle> pool, GUIStyle baseStyle, Color color)
        {
            uint key = ColourKey(color);
            if (pool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(baseStyle, color);
            Store(pool, key, pooled);
            return pooled;
        }

        #endregion

        #region Pooled section-header styles

        /// <summary>
        ///     Section-header styles, one instance per distinct colour, built on first request.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Pooled rather than re-tinted.</b> Re-tinting single shared instances means
        ///         rewriting eight interaction states on every header of every repaint — twenty-four
        ///         native writes per header, and a six-section inspector draws six of them. The write
        ///         cost is the smaller half of it: IMGUI keys its generated text on the style instance,
        ///         so mutating that instance between two labels asks the text generator to redo work it
        ///         had already cached.
        ///     </para>
        ///     <para>
        ///         <b>Why a pool is safe here and unbounded growth is not a risk.</b> Header colours
        ///         come from the fixed token set — the brand accent plus the four status colours, each
        ///         optionally faded for the chevron's unhovered state — so the live pool settles at
        ///         roughly a dozen entries. <see cref="PoolCapacity" /> is a backstop against a caller
        ///         that computes a colour per frame, not an expected limit.
        ///     </para>
        ///     <para>
        ///         The pooled style carries the same eight states the shared instance was given, so
        ///         what it renders is identical — including the reason all eight were set: a header
        ///         row leaving hover or active on the previous section's colour flickers between
        ///         accents under the pointer.
        ///     </para>
        /// </remarks>
        private const int PoolCapacity = 64;

        private static readonly Dictionary<uint, GUIStyle> SectionHeaderLabelPool = new();
        private static readonly Dictionary<(uint Colour, int FontSize), GUIStyle> SectionIconPool = new();
        private static readonly Dictionary<uint, GUIStyle> SectionChevronPool = new();

        /// <summary>Returns <see cref="SectionHeaderLabel" /> in <paramref name="color" />.</summary>
        internal static GUIStyle SectionHeaderLabelTinted(Color color)
        {
            EnsureStyles();

            uint key = ColourKey(color);
            if (SectionHeaderLabelPool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(SectionHeaderLabel, color);
            Store(SectionHeaderLabelPool, key, pooled);
            return pooled;
        }

        /// <summary>Returns <see cref="SectionIcon" /> in <paramref name="color" /> at <paramref name="fontSize" />.</summary>
        internal static GUIStyle SectionIconTinted(Color color, int fontSize)
        {
            EnsureStyles();

            (uint, int) key = (ColourKey(color), fontSize);
            if (SectionIconPool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(SectionIcon, color);
            pooled.fontSize = fontSize;
            Store(SectionIconPool, key, pooled);
            return pooled;
        }

        /// <summary>Returns <see cref="SectionChevron" /> in <paramref name="color" />.</summary>
        internal static GUIStyle SectionChevronTinted(Color color)
        {
            EnsureStyles();

            uint key = ColourKey(color);
            if (SectionChevronPool.TryGetValue(key, out GUIStyle pooled))
                return pooled;

            pooled = TintedClone(SectionChevron, color);
            Store(SectionChevronPool, key, pooled);
            return pooled;
        }

        /// <summary>
        ///     Copies <paramref name="style" /> with one colour applied to every interaction state.
        /// </summary>
        private static GUIStyle TintedClone(GUIStyle style, Color color)
        {
            var clone = new GUIStyle(style);
            clone.normal.textColor = color;
            clone.onNormal.textColor = color;
            clone.focused.textColor = color;
            clone.onFocused.textColor = color;
            clone.hover.textColor = color;
            clone.onHover.textColor = color;
            clone.active.textColor = color;
            clone.onActive.textColor = color;
            return clone;
        }

        /// <summary>
        ///     Packs a colour into its 8-bit-per-channel identity. Two colours that quantise to the
        ///     same bytes are the same colour on screen, so they may share a style.
        /// </summary>
        private static uint ColourKey(Color color)
        {
            Color32 quantised = color;
            return ((uint)quantised.r << 24) | ((uint)quantised.g << 16) |
                   ((uint)quantised.b << 8) | quantised.a;
        }

        private static void Store<TKey>(Dictionary<TKey, GUIStyle> pool, TKey key, GUIStyle style)
        {
            if (pool.Count >= PoolCapacity)
                pool.Clear();

            pool[key] = style;
        }

        #endregion

        /// <summary>
        ///     Builds the style set if it has not been built, or rebuilds it if the editor skin
        ///     flipped since it was. Cheap enough to call at the top of every draw entry point.
        /// </summary>
        internal static void EnsureStyles()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (s_ready && s_lastProSkin == pro)
                return;

            s_ready = true;
            s_lastProSkin = pro;
            s_generation++;

            // Every pooled style clones a base style rebuilt below, so none of them can outlive
            // that base: a pooled style kept across a skin flip would draw the old skin's
            // typography in the new one.
            SectionHeaderLabelPool.Clear();
            SectionIconPool.Clear();
            SectionChevronPool.Clear();
            CenteredMiniPool.Clear();
            TileNumberPool.Clear();
            MetricNumberPool.Clear();
            MicroLabelRightPool.Clear();
            ReadingValuePool.Clear();
            TableCellPool.Clear();
            LiveCellValuePool.Clear();
            PillLabelPool.Clear();
            ChipLabelPool.Clear();
            MessageIconPool.Clear();

            Color primary = Tokens.TextPrimary;
            Color secondary = Tokens.TextSecondary;
            Color muted = Tokens.TextMuted;

            // ---------------------------------------------------------------- headers and titles
            HeroTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = primary }
            };
            HeroSubtitle = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = secondary }
            };
            InspectorTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Tokens.AccentBright }
            };
            SectionTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = primary }
            };
            SectionGlyph = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Tokens.Accent }
            };
            SectionSummary = new GUIStyle(Safe(Kind.MiniLabel))
            {
                fontSize = 10,
                wordWrap = true,
                fontStyle = FontStyle.Italic,
                normal = { textColor = secondary }
            };
            SelectedTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = primary }
            };
            SectionHeaderLabel = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                fixedHeight = Tokens.SectionHeaderRowHeight,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };
            SectionIcon = new GUIStyle(SectionHeaderLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = Tokens.SectionIconCellWidth,
                fontSize = Tokens.SectionIconFontSize,
                contentOffset = Vector2.zero
            };
            SectionChevron = new GUIStyle(SectionHeaderLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = Tokens.SectionChevronCellWidth,
                fontSize = 10,
                contentOffset = Vector2.zero
            };
            CenteredTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = primary }
            };

            // ---------------------------------------------------------------- body text
            BodyWrapped = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = primary }
            };
            MutedWrapped = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = secondary }
            };
            CaptionWrapped = new GUIStyle(Safe(Kind.MiniLabel))
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = secondary }
            };
            MicroLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = muted }
            };
            GroupLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                // No padding of its own, so the caption's first glyph lands exactly on the left edge
                // of the rect its margin reserves — the horizontal margin below then places that edge
                // on the same line as the control underneath.
                padding = new RectOffset(0, 0, 0, 0),
                contentOffset = Vector2.zero,
                normal = { textColor = secondary }
            };
            // Horizontal margin taken from the built-in label rather than written as a number,
            // because that is the margin the control under the caption reserves its own rect with —
            // the segmented picker measures its segments with GhostButtonLabel, which inherits it.
            // Hardcoding the value instead is how the caption ended up four pixels left of its own
            // picker: the number was right for the style it was copied from and for nothing else.
            GUIStyle labelMetrics = Safe(Kind.Label);
            GroupLabel.margin = new RectOffset(
                labelMetrics.margin.left,
                labelMetrics.margin.right,
                // Equal air above and below, so the caption reads as centred in the gap between the
                // section rule above it and the controls it names. One pixel more above than below
                // because a mini font sits high in its own line box: measured to the glyphs rather
                // than to the box, 7/6 is the pair that looks like 6/6.
                7,
                6);
            RowLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = muted }
            };
            ListGroupHeader = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = primary }
            };
            MicroLabelRight = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = muted }
            };
            TableHeaderLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = secondary }
            };
            TableHeaderCell = new GUIStyle(TableHeaderLabel)
            {
                fixedHeight = Tokens.TableHeaderHeight,
                stretchWidth = false,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                contentOffset = Vector2.zero
            };
            TableHeaderCellCentered = new GUIStyle(TableHeaderCell)
            {
                alignment = TextAnchor.MiddleCenter
            };
            TableHeaderCellRight = new GUIStyle(TableHeaderCell)
            {
                alignment = TextAnchor.MiddleRight
            };
            ReadingLabel = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = secondary }
            };
            ReadingValue = new GUIStyle(ReadingLabel)
            {
                normal = { textColor = primary }
            };
            TableCell = new GUIStyle(ReadingLabel)
            {
                fontSize = 10,
                normal = { textColor = primary }
            };
            TableCellCentered = new GUIStyle(TableCell)
            {
                alignment = TextAnchor.MiddleCenter
            };
            EmptyStateGlyph = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = Tokens.EmptyStateGlyphSize,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = muted }
            };
            EmptyStateTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = primary }
            };
            CenteredBody = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = secondary }
            };
            PreviewQuote = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
                normal = { textColor = primary }
            };
            FooterLabel = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = secondary }
            };
            Link = new GUIStyle(Safe(Kind.LinkLabel))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };

            // ---------------------------------------------------------------- list cards
            CardName = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = primary },
                hover = { textColor = primary },
                active = { textColor = primary },
                focused = { textColor = primary }
            };
            CardNameSelected = new GUIStyle(CardName)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Tokens.AccentBright },
                hover = { textColor = Tokens.AccentBright },
                active = { textColor = Tokens.AccentBright },
                focused = { textColor = Tokens.AccentBright }
            };
            CardTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 11,
                wordWrap = false,
                normal = { textColor = primary }
            };
            CardSubtitle = new GUIStyle(Safe(Kind.MiniLabel))
            {
                wordWrap = true,
                normal = { textColor = secondary }
            };

            // ---------------------------------------------------------------- tiles and live cells
            TileNumber = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = primary }
            };
            TileLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = muted }
            };
            MetricNumber = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = primary }
            };
            LiveCellLabel = new GUIStyle(Safe(Kind.MiniLabel))
            {
                fontSize = 9,
                normal = { textColor = muted }
            };
            LiveCellValue = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = primary }
            };

            // ---------------------------------------------------------------- message boxes
            MessageIcon = new GUIStyle(Safe(Kind.Label))
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 1, 0)
            };
            MessageTitle = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = primary }
            };
            MessageBody = new GUIStyle(Safe(Kind.MiniLabel))
            {
                fontSize = 10,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = secondary }
            };

            // ---------------------------------------------------------------- search field
            SearchText = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = primary },
                focused = { textColor = primary }
            };
            SearchPlaceholder = new GUIStyle(SearchText)
            {
                normal = { textColor = muted }
            };
            SearchGlyph = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = muted }
            };
            SearchFieldBox = new GUIStyle(Safe(Kind.ToolbarSearchField))
            {
                fixedHeight = 20
            };
            GrowingTextArea = new GUIStyle(Safe(Kind.TextArea))
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            FieldPlaceholder = new GUIStyle(Safe(Kind.Label))
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(2, 2, 1, 0),
                normal = { textColor = muted }
            };

            // ---------------------------------------------------------------- starter tiles
            StarterGlyph = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Tokens.Accent }
            };
            StarterName = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = primary }
            };
            StarterDesc = new GUIStyle(Safe(Kind.MiniLabel))
            {
                fontSize = 10,
                wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = secondary }
            };

            // ---------------------------------------------------------------- containers
            CardBody = new GUIStyle { padding = Tokens.CardPadding };
            PanelBody = new GUIStyle { padding = Tokens.PanelPadding };
            PaneContent = new GUIStyle { padding = Tokens.PaneContentPadding };
            MiniButton = new GUIStyle(Safe(Kind.MiniButton))
            {
                fontSize = 10,
                padding = new RectOffset(8, 8, 3, 3)
            };
            InvisibleButton = new GUIStyle
            {
                normal = { textColor = Color.clear },
                hover = { textColor = Color.clear },
                active = { textColor = Color.clear },
                focused = { textColor = Color.clear }
            };

            // ---------------------------------------------------------------- control labels
            // Padding zeroed deliberately. Inherited from miniBoldLabel it is left 2, right 2, top 3,
            // bottom 2, and that vertical asymmetry is a real alignment bug: MiddleCenter centres
            // text inside the *content* rect (the rect minus padding), so the text lands half a
            // pixel below the rect's true centre while a status dot at the geometric centre does not — which
            // reads, correctly, as a dot that is not lined up with its label. Zeroing it also makes
            // CalcSize report pure text metrics, so the pill can lay the dot and the label out as one
            // centred run instead of guessing at offsets.
            PillLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = primary }
            };
            // Padding zeroed for the same reason as PillLabel: miniBoldLabel's T3/B2 asymmetry pushes
            // MiddleCenter text half a pixel below the rect's real centre. Alignment stays centred —
            // this one is handed straight to GUI.Button, which centres content in the whole rect.
            ChipLabel = new GUIStyle(Safe(Kind.MiniBoldLabel))
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };
            PrimaryButtonLabel = new GUIStyle(Safe(Kind.BoldLabel))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Tokens.OnAccent },
                hover = { textColor = Tokens.OnAccent },
                active = { textColor = Tokens.Fade(Tokens.OnAccent, 0.7f) },
                focused = { textColor = Tokens.OnAccent }
            };
            GhostButtonLabel = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = primary },
                hover = { textColor = Tokens.AccentBright },
                active = { textColor = Tokens.Accent },
                focused = { textColor = primary }
            };
            IconButtonLabel = new GUIStyle(Safe(Kind.Label))
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = secondary },
                hover = { textColor = primary },
                active = { textColor = Tokens.AccentBright },
                focused = { textColor = secondary }
            };

            s_centeredMini = new GUIStyle(Safe(Kind.MiniLabel))
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        private enum Kind
        {
            Label,
            BoldLabel,
            MiniLabel,
            MiniBoldLabel,
            LinkLabel,
            MiniButton,
            ToolbarSearchField,
            TextArea
        }

        /// <summary>
        ///     Resolves a base <see cref="EditorStyles" /> entry, degrading to the GUI skin and then to
        ///     an empty style. Editor styles are genuinely unavailable during bootstrap and in
        ///     batch-mode test runs, where touching them throws rather than returning null.
        /// </summary>
        private static GUIStyle Safe(Kind kind)
        {
            try
            {
                GUIStyle style = kind switch
                {
                    Kind.BoldLabel => EditorStyles.boldLabel,
                    Kind.MiniLabel => EditorStyles.miniLabel,
                    Kind.MiniBoldLabel => EditorStyles.miniBoldLabel,
                    Kind.LinkLabel => EditorStyles.linkLabel,
                    Kind.MiniButton => EditorStyles.miniButton,
                    Kind.ToolbarSearchField => EditorStyles.toolbarSearchField,
                    Kind.TextArea => EditorStyles.textArea,
                    _ => EditorStyles.label
                };
                if (style != null)
                    return style;
            }
            catch
            {
                // EditorStyles is unavailable during editor bootstrap and in headless test runs.
            }

            try
            {
                GUIStyle skinStyle = kind switch
                {
                    Kind.MiniButton => GUI.skin?.button,
                    Kind.ToolbarSearchField => GUI.skin?.textField,
                    Kind.TextArea => GUI.skin?.textArea,
                    _ => GUI.skin?.label
                };
                if (skinStyle != null)
                    return skinStyle;
            }
            catch
            {
                // GUI.skin can also be unavailable outside a normal IMGUI event.
            }

            return new GUIStyle();
        }
    }
}
