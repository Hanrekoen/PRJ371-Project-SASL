using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Base class for all Convai configuration window sections.
    ///     Provides common functionality for showing and hiding sections.
    /// </summary>
    /// <remarks>
    ///     All section classes should inherit from this base class to ensure
    ///     consistent behavior and integration with the navigation system.
    /// </remarks>
    [UxmlElement]
    public partial class ConvaiBaseSection : VisualElement
    {
        /// <summary>
        ///     Gets a value indicating whether this section is currently visible.
        /// </summary>
        public bool IsSectionVisible { get; private set; }

        /// <summary>
        ///     Hides this section by setting display to None.
        /// </summary>
        public void HideSection()
        {
            style.display = DisplayStyle.None;
            if (!IsSectionVisible) return;

            IsSectionVisible = false;
            OnSectionHidden();
        }

        /// <summary>
        ///     Shows this section by setting display to Flex.
        /// </summary>
        public void ShowSection()
        {
            style.display = DisplayStyle.Flex;
            if (IsSectionVisible) return;

            IsSectionVisible = true;
            OnSectionShown();
        }

        /// <summary>
        ///     Called when the section becomes visible.
        /// </summary>
        protected virtual void OnSectionShown()
        {
        }

        /// <summary>
        ///     Called when the section becomes hidden.
        /// </summary>
        protected virtual void OnSectionHidden()
        {
        }
    }
}
