using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The measurement layer of the Convai editor design system: the one place in Convai's editor
    ///     UI that asks a <see cref="GUIStyle" /> how large a piece of text is, and the only place that
    ///     remembers the answer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> <see cref="GUIStyle.CalcHeight" /> on wrapped text is among the
    ///         most expensive calls IMGUI offers — it runs the full text generator to find the line
    ///         count. Convai's editor UI called it from inside draw passes: every message box measured
    ///         its body on every repaint, and the turn-taking drawer measured up to five paragraphs
    ///         twice per frame (once for Layout, once for Repaint). An inspector repaints on every
    ///         mouse move over the Inspector window, and eleven Convai inspectors additionally repaint
    ///         continuously in Play mode, so that cost was paid tens of times a second on every
    ///         machine the SDK ships to.
    ///     </para>
    ///     <para>
    ///         <b>Why memoising is correct, not just fast.</b> For a fixed skin and DPI,
    ///         <c>CalcHeight(style, text, width)</c> is a pure function of its arguments. Those two
    ///         things are exactly what invalidates this cache, so a cached answer cannot disagree with
    ///         a fresh measurement. That property is the reason call sites measure through here rather
    ///         than caching heights against hand-written keys of their own: a hand-written key has to
    ///         name every input that feeds the text, and the failure mode of forgetting one is an
    ///         inspector laid out against a stale height — clipped copy, overlapping controls — with
    ///         nothing to indicate why.
    ///     </para>
    ///     <para>
    ///         <b>Bounded on purpose.</b> Past <see cref="Capacity" /> entries the whole table is
    ///         dropped rather than evicted one at a time. The live working set is one screen of UI, so
    ///         the cap is only ever reached by text that varies per frame (a live telemetry readout,
    ///         say), and for that traffic an LRU would buy nothing but complexity. An unbounded
    ///         dictionary in a static editor field is a leak that survives until the next domain
    ///         reload.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorTextMetrics
    {
        /// <summary>
        ///     Entries kept before the table is dropped. Comfortably above one screen of Convai UI
        ///     (a dense inspector measures on the order of thirty distinct strings) and small enough
        ///     that the worst case stays trivial.
        /// </summary>
        private const int Capacity = 512;

        private static readonly Dictionary<MeasurementKey, float> Heights = new();
        private static readonly Dictionary<MeasurementKey, float> Widths = new();

        /// <summary>
        ///     The one <see cref="GUIContent" /> every measurement borrows. IMGUI reads it
        ///     synchronously inside the <c>Calc…</c> call and keeps no reference, so reuse is safe —
        ///     and it is what lets call sites measure without allocating.
        /// </summary>
        private static readonly GUIContent Scratch = new();

        private static int s_stylesGeneration = -1;
        private static float s_pixelsPerPoint = -1f;

        /// <summary>Entries currently memoised. Exposed so a test can assert the cap holds.</summary>
        internal static int CachedEntryCount => Heights.Count + Widths.Count;

        /// <summary>
        ///     Height of <paramref name="text" /> wrapped to <paramref name="width" /> in
        ///     <paramref name="style" />. Equal to <see cref="GUIStyle.CalcHeight" /> for the same
        ///     arguments; the difference is that asking twice costs once.
        /// </summary>
        internal static float WrappedHeight(GUIStyle style, string text, float width)
        {
            if (style == null)
                return 0f;

            EnsureCurrent();

            var key = new MeasurementKey(style, text, width);
            if (Heights.TryGetValue(key, out float cached))
                return cached;

            Scratch.text = text ?? string.Empty;
            float height = style.CalcHeight(Scratch, width);
            Scratch.text = string.Empty;

            Store(Heights, key, height);
            return height;
        }

        /// <summary>
        ///     Width of <paramref name="content" /> in <paramref name="style" />. Equal to
        ///     <see cref="GUIStyle.CalcSize" />'s x component.
        /// </summary>
        /// <remarks>
        ///     Only the text and tooltip-free width is memoised, which is all any Convai call site
        ///     asks for: pills, mode-bar tabs and section summaries size to their label. A content
        ///     carrying an image measures live, because its size does not follow from the string.
        /// </remarks>
        internal static float Width(GUIStyle style, GUIContent content)
        {
            if (style == null || content == null)
                return 0f;

            if (content.image != null)
                return style.CalcSize(content).x;

            return Width(style, content.text);
        }

        /// <inheritdoc cref="Width(GUIStyle,GUIContent)" />
        internal static float Width(GUIStyle style, string text)
        {
            if (style == null)
                return 0f;

            EnsureCurrent();

            // Width does not depend on an available width; 0 keeps this key out of the height table's
            // value space without needing a second key type.
            var key = new MeasurementKey(style, text, 0f);
            if (Widths.TryGetValue(key, out float cached))
                return cached;

            Scratch.text = text ?? string.Empty;
            float width = style.CalcSize(Scratch).x;
            Scratch.text = string.Empty;

            Store(Widths, key, width);
            return width;
        }

        /// <summary>
        ///     Drops every memoised measurement. Called for you when the skin or the DPI changes;
        ///     exposed for tests and for a caller that knows it has invalidated the font itself.
        /// </summary>
        internal static void Invalidate()
        {
            Heights.Clear();
            Widths.Clear();
        }

        /// <summary>
        ///     Drops the table if anything that changes text metrics has changed since it was filled.
        /// </summary>
        /// <remarks>
        ///     Two signals, and between them they cover every way a measurement can go stale.
        ///     <see cref="ConvaiEditorStyles.Generation" /> moves when the style set is rebuilt, which
        ///     is what a skin flip does. <see cref="EditorGUIUtility.pixelsPerPoint" /> moves when the
        ///     window is dragged to a display with a different scale factor — which changes text
        ///     metrics without touching the styles, and would otherwise leave every cached height a
        ///     line short on the new monitor.
        /// </remarks>
        private static void EnsureCurrent()
        {
            int generation = ConvaiEditorStyles.Generation;
            float pixelsPerPoint = SafePixelsPerPoint();

            if (generation == s_stylesGeneration && Mathf.Approximately(pixelsPerPoint, s_pixelsPerPoint))
                return;

            s_stylesGeneration = generation;
            s_pixelsPerPoint = pixelsPerPoint;
            Invalidate();
        }

        /// <summary>
        ///     Reads the display scale, degrading to 1 where <see cref="EditorGUIUtility" /> is
        ///     unavailable — batch-mode test runs and editor bootstrap, the same conditions
        ///     <see cref="ConvaiEditorStyles" /> guards its own skin lookups against.
        /// </summary>
        private static float SafePixelsPerPoint()
        {
            try
            {
                return EditorGUIUtility.pixelsPerPoint;
            }
            catch
            {
                return 1f;
            }
        }

        private static void Store(Dictionary<MeasurementKey, float> table, MeasurementKey key, float value)
        {
            if (table.Count >= Capacity)
                table.Clear();

            table[key] = value;
        }

        /// <summary>
        ///     Identifies one measurement. The style is compared by reference rather than by value:
        ///     style instances are stable between rebuilds, and a rebuild clears the table anyway, so
        ///     reference identity is both cheaper and stricter than comparing their contents.
        /// </summary>
        private readonly struct MeasurementKey : IEquatable<MeasurementKey>
        {
            private readonly GUIStyle _style;
            private readonly string _text;
            private readonly float _width;

            internal MeasurementKey(GUIStyle style, string text, float width)
            {
                _style = style;
                _text = text ?? string.Empty;
                _width = width;
            }

            public bool Equals(MeasurementKey other) =>
                ReferenceEquals(_style, other._style) &&
                _width.Equals(other._width) &&
                string.Equals(_text, other._text, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is MeasurementKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(RuntimeHelpers.GetHashCode(_style), _text, _width);
        }
    }
}
