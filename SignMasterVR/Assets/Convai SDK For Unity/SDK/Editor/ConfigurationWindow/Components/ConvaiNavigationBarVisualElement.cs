using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Navigation bar visual element for the Convai configuration window.
    ///     Displays the Convai logo and navigation buttons for switching between sections.
    /// </summary>
    /// <remarks>
    ///     Raises <see cref="OnNavigationButtonClicked" /> when a section button is clicked.
    ///     Manages visual state of navigation buttons to indicate the active section.
    /// </remarks>
    [UxmlElement]
    public partial class ConvaiNavigationBarVisualElement : VisualElement
    {
        private readonly List<(string sectionName, string displayName)> _navButtonEntries;

        private readonly Dictionary<string, Button> _navButtons = new();
        private ScrollView _buttonsContainer;
        private VisualElement _logo;

        /// <summary>Callback invoked when a navigation button is clicked (parameter: section name).</summary>
        public Action<string> OnNavigationButtonClicked;

        public ConvaiNavigationBarVisualElement() : this(ConfigurationSectionRegistry.GetEnabledSections())
        {
        }

        public ConvaiNavigationBarVisualElement(IReadOnlyList<ConfigurationSectionDescriptor> sections)
        {
            _navButtonEntries = sections
                .Where(section => section != null && section.IsEnabled)
                .Select(section => (section.SectionId, section.DisplayName))
                .ToList();

            AddToClassList("nav-bar");
            CreateLogo();
            CreateNavigationButtons();
        }

        private void CreateLogo()
        {
            _logo = new VisualElement { name = "convai-logo" };
            _logo.AddToClassList("convai-logo");
            Add(_logo);
        }

        private void CreateNavigationButtons()
        {
            _buttonsContainer = new ScrollView { name = "buttons-container" };
            foreach ((string sectionName, string displayName) in _navButtonEntries)
            {
                Button button = new() { text = displayName, name = $"{sectionName}-btn" };
                button.AddToClassList("nav-bar-btn");
                string capturedSectionName = sectionName;
                button.clicked += () =>
                {
                    OnNavigationButtonClicked?.Invoke(capturedSectionName);
                };
                _buttonsContainer.Add(button);
                _navButtons.Add(sectionName, button);
            }

            Add(_buttonsContainer);
        }

        /// <summary>
        ///     Updates the navigation bar to mark the given section as active.
        /// </summary>
        /// <param name="section">Section name to activate.</param>
        public void NavigateTo(string section)
        {
            foreach (KeyValuePair<string, Button> pair in _navButtons)
            {
                if (pair.Key == section)
                    pair.Value.AddToClassList("nav-bar-btn--active");
                else
                    pair.Value.RemoveFromClassList("nav-bar-btn--active");
            }
        }
    }
}
