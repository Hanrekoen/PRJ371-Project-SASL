#if UNITY_EDITOR
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Vision
{
    /// <summary>
    ///     Shared base for the Vision module's custom editors. The Convai header, section cards,
    ///     message boxes and live cells all come from <see cref="ConvaiInspectorEditor" />, so a Vision
    ///     inspector contains only its own domain logic.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This class used to carry a complete second implementation of frame the SDK already had:
    ///         its own header renderer, its own info/warning/error boxes, its own live cells, five
    ///         <see cref="GUIStyle" /> allocations and three hardcoded status colours. It even generated
    ///         a circle <see cref="Texture2D" /> at runtime for its status dot — which
    ///         <see cref="ConvaiEditorDraw.StatusDot" /> draws on the GPU with no texture at all — and
    ///         destroyed it in <c>OnDisable</c>.
    ///     </para>
    ///     <para>
    ///         Only <see cref="DrawStatusRow" /> and <see cref="LiveCellWidth" /> survive, because they
    ///         are genuinely Vision's own vocabulary. The colour members are forwarding properties, not
    ///         values, so Vision reads the same palette as every other Convai surface and cannot drift
    ///         from it.
    ///     </para>
    ///     <para>
    ///         <b>Accessibility.</b> This is <c>internal</c>, like every other Convai editor base. It was
    ///         previously <c>public</c>, which was an oversight rather than a decision — and a public
    ///         type cannot derive from the internal shared base, so it was also the one thing blocking
    ///         Vision from joining the design system.
    ///     </para>
    /// </remarks>
    internal abstract class ConvaiVisionBaseEditor : ConvaiInspectorEditor
    {
        /// <summary>Fixed width for live-status grid columns, so values stay aligned as they change.</summary>
        protected const float LiveCellWidth = 110f;

        // Palette shortcuts — forwarders only, never values.
        protected static Color ConvaiGreen => Theme.Accent;
        protected static Color ConvaiGreenLight => Theme.AccentBright;
        protected static Color ConvaiInfo => Theme.StatusInfo;

        /// <summary>Present but not running.</summary>
        protected static Color StatusIdle => Theme.StatusIdle;

        /// <summary>Edit-mode-only state, where no live value exists yet.</summary>
        protected static Color StatusEditor => Theme.TextSecondary;

        /// <summary>An ordinary live value carrying no special meaning.</summary>
        protected static Color DefaultValueColor => Theme.TextPrimary;

        /// <summary>
        ///     Background for a Live Status section body: faintly accent-tinted while the source is
        ///     actually producing frames, plain card surface otherwise. The tint is what makes "this is
        ///     live right now" readable at a glance without reading the values.
        /// </summary>
        protected static Color LiveSectionBackground(bool active) =>
            active ? Theme.Tint(Theme.Accent) : Theme.CardBg;

        /// <summary>
        ///     A live-status cell at Vision's fixed column width, so a row of them stays aligned as the
        ///     values change. Forwards to the shared cell renderer; the width is the only thing Vision
        ///     pins down.
        /// </summary>
        protected static void DrawLiveCell(string label, string value, Color valueColor, bool bold = false) =>
            LiveCell(label, value, valueColor, LiveCellWidth, bold);

        /// <summary>
        ///     Zero-argument placeholder so section bodies can pass it as a method group. The shared
        ///     helper takes an optional message, which C# will not convert to <see cref="System.Action" />.
        /// </summary>
        protected static void DrawOfflinePlaceholder() => OfflinePlaceholder();

        /// <summary>
        ///     Vision's live status line: a colour-coded state phrase with an optional right-side badge
        ///     (for example "▶ PUBLISHING" with "[720p]"). Kept local because no other Convai module
        ///     presents status this way.
        /// </summary>
        protected static void DrawStatusRow(
            string statusText, Color statusColor, string badgeText = null, Color? badgeColor = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    statusText,
                    ConvaiEditorStyles.LiveCellValueTinted(statusColor, true),
                    GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();

                if (string.IsNullOrEmpty(badgeText))
                    return;

                var badge = new GUIContent(badgeText);
                float width = ConvaiEditorControls.PillWidth(badge);
                Rect rect = GUILayoutUtility.GetRect(
                    width, ConvaiEditorTokens.PillHeight, GUILayout.Width(width));
                ConvaiEditorControls.Pill(rect, badge, badgeColor ?? Theme.StatusInfo);
            }
        }
    }
}
#endif
