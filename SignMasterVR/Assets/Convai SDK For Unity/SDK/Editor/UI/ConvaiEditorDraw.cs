using UnityEditor;
using UnityEngine;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The drawing primitives of the Convai editor design system: anti-aliased rounded rectangles,
    ///     circles, status dots and divider lines. Everything above this layer composes these — no
    ///     higher layer reimplements a primitive.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rounded drawing goes through the
    ///         <see cref="GUI.DrawTexture(Rect, Texture, ScaleMode, bool, float, Color, float, float)" />
    ///         border-radius overload with <see cref="Texture2D.whiteTexture" />. That path is
    ///         GPU-rasterised, anti-aliased and resolution-independent, so it needs no generated
    ///         textures and costs nothing to keep in memory — the reason the design system can afford
    ///         rounded corners everywhere.
    ///     </para>
    ///     <para>
    ///         Every call early-outs on anything other than <see cref="EventType.Repaint" />. Callers
    ///         may therefore invoke these unconditionally inside layout code without corrupting
    ///         IMGUI's event handling or wasting work on layout/mouse passes.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorDraw
    {
        /// <summary>Fills a rounded rectangle. Repaint-only; safe to call on every event.</summary>
        internal static void FillRounded(Rect rect, Color color, float radius)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        /// <summary>Strokes a rounded-rectangle outline. Repaint-only.</summary>
        internal static void StrokeRounded(Rect rect, Color color, float radius, float width = 1f)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width, radius);
        }

        /// <summary>Fills a rounded rectangle and strokes its border in one call.</summary>
        internal static void FillAndStrokeRounded(Rect rect, Color fill, Color border, float radius)
        {
            FillRounded(rect, fill, radius);
            StrokeRounded(rect, border, radius);
        }

        /// <summary>Fills a circle centred on <paramref name="center" />.</summary>
        internal static void FillCircle(Vector2 center, float radius, Color color) =>
            FillRounded(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color, radius);

        /// <summary>
        ///     Draws a traffic-light status dot, with a soft halo when <paramref name="emphasized" />
        ///     — used to mark the row under the pointer or a live/running state.
        /// </summary>
        internal static void StatusDot(Vector2 center, Color color, bool emphasized = false)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (emphasized)
                FillCircle(center, ConvaiEditorTokens.StatusDotHaloRadius, ConvaiEditorTokens.Fade(color, 0.22f));
            FillCircle(center, ConvaiEditorTokens.StatusDotRadius, color);
        }

        /// <summary>
        ///     Draws a status dot centred in <paramref name="rect" /> — the form to prefer, because the
        ///     caller does not have to know how tall its own row is.
        /// </summary>
        /// <remarks>
        ///     A hand-computed centre such as <c>row.y + 9f</c> is only correct for an 18-pixel row and
        ///     silently drifts off-centre in every other one. Give this the rect the dot belongs in and
        ///     the arithmetic stops being the call site's problem.
        /// </remarks>
        internal static void StatusDot(Rect rect, Color color, bool emphasized = false) =>
            StatusDot(new Vector2(rect.x + (rect.width * 0.5f), rect.y + (rect.height * 0.5f)), color, emphasized);

        /// <summary>
        ///     Returns a rect of <paramref name="height" /> centred vertically in
        ///     <paramref name="outer" />, keeping its horizontal extent.
        /// </summary>
        /// <remarks>
        ///     For placing a fixed-height control in a taller row. Written at the call site it becomes a
        ///     literal offset (<c>row.y + 4f</c>, <c>+ 5f</c>, <c>+ 9f</c>) measured against whatever
        ///     that row's height happens to be, which puts the same control at a different height in
        ///     every window that draws it.
        /// </remarks>
        internal static Rect CenteredSlice(Rect outer, float height) =>
            new(outer.x, outer.y + ((outer.height - height) * 0.5f), outer.width, height);

        /// <summary>Fills <paramref name="rect" /> flat — for table rows and full-bleed strips.</summary>
        internal static void Fill(Rect rect, Color color)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
        }

        /// <summary>Draws a 1px divider line across <paramref name="rect" />.</summary>
        internal static void DividerLine(Rect rect, Color color) => Fill(rect, color);

        /// <summary>Reserves a row of layout space and draws a full-width 1px divider in it.</summary>
        internal static void HorizontalRule(Color color, float spaceBefore = 3f, float spaceAfter = 8f)
        {
            GUILayout.Space(spaceBefore);
            Rect line = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            DividerLine(line, color);
            GUILayout.Space(spaceAfter);
        }
    }
}
