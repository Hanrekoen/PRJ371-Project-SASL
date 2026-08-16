using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Utility methods for creating and styling UI Toolkit visual elements.
    ///     Provides helper functions for common UI operations in the Convai editor windows.
    /// </summary>
    public static class ConvaiVisualElementUtility
    {
        /// <summary>
        ///     Adds multiple CSS class names to a visual element.
        /// </summary>
        /// <param name="visualElement">The element to add styles to.</param>
        /// <param name="styles">One or more CSS class names to add.</param>
        public static void AddStyles(VisualElement visualElement, params string[] styles)
        {
            foreach (string s in styles) visualElement.AddToClassList(s);
        }

        /// <summary>
        ///     Creates a label with the specified name, content, and CSS classes.
        /// </summary>
        /// <param name="labelName">The name attribute for the label element.</param>
        /// <param name="content">The text content of the label.</param>
        /// <param name="styles">CSS class names to apply to the label.</param>
        /// <returns>A new Label element with the specified configuration.</returns>
        public static Label CreateLabel(string labelName, string content, params string[] styles)
        {
            Label label = new() { text = content, name = labelName };
            AddStyles(label, styles);
            return label;
        }

        /// <summary>
        ///     Sets the top and bottom margins of a visual element.
        /// </summary>
        /// <param name="element">The element to modify.</param>
        /// <param name="up">The top margin in pixels.</param>
        /// <param name="down">The bottom margin in pixels.</param>
        public static void ModifyMargin(VisualElement element, float up, float down)
        {
            element.style.marginTop = up;
            element.style.marginBottom = down;
        }

        /// <summary>
        ///     Creates a spacer element with the specified height.
        /// </summary>
        /// <param name="height">The height of the spacer in pixels.</param>
        /// <returns>A new visual element configured as a spacer.</returns>
        public static VisualElement CreateSpacer(float height) =>
            new() { name = "spacer", style = { height = height } };
    }
}
