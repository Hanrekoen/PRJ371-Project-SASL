using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Root visual element for the Convai configuration window.
    ///     Manages navigation and content display for all configuration sections.
    /// </summary>
    /// <remarks>
    ///     This element combines a navigation bar and content container,
    ///     allowing users to navigate between different configuration sections.
    /// </remarks>
    [UxmlElement]
    public partial class ConvaiConfigurationWindow : VisualElement
    {
        private readonly ConvaiContentContainerVisualElement _contentContainer;
        private readonly ConfigurationWindowContext _context;
        private readonly ConvaiNavigationBarVisualElement _navigation;
        private readonly IReadOnlyList<ConfigurationSectionDescriptor> _sections;

        private string _initialSection;
        private bool _isNavigationSubscribed;

        public ConvaiConfigurationWindow()
        {
            _context = new ConfigurationWindowContext();
            _sections = ConfigurationSectionRegistry.GetEnabledSections();
            _initialSection = _sections.FirstOrDefault()?.SectionId ?? string.Empty;

            _navigation = new ConvaiNavigationBarVisualElement(_sections);
            _contentContainer = new ConvaiContentContainerVisualElement(_context, _sections);
            AddToClassList("root");
            Add(_navigation);
            Add(_contentContainer);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        /// <summary>
        ///     UXML attribute for setting the initial section to display.
        /// </summary>
        [UxmlAttribute("initial-section")]
        public string InitialSection
        {
            get => _initialSection;
            set
            {
                _initialSection = value;
                if (panel != null) OpenSection(_initialSection);
            }
        }

        private void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            if (!_isNavigationSubscribed)
            {
                _navigation.OnNavigationButtonClicked += OpenSection;
                _isNavigationSubscribed = true;
            }

            _context.RefreshApiKeyAvailability(false);
            OpenSection(_initialSection);
            _contentContainer.PreWarmSections();
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            if (_isNavigationSubscribed)
            {
                _navigation.OnNavigationButtonClicked -= OpenSection;
                _isNavigationSubscribed = false;
            }

            _context.ClearSubscribers();
        }

        /// <summary>
        ///     Opens the specified configuration section.
        /// </summary>
        /// <param name="sectionName">Section name to open.</param>
        public void OpenSection(string sectionName)
        {
            string resolvedSection = sectionName;
            if (!_contentContainer.ContainsSection(resolvedSection)) resolvedSection = _contentContainer.FirstSectionId;

            if (string.IsNullOrEmpty(resolvedSection)) return;

            _navigation.NavigateTo(resolvedSection);
            _contentContainer.OpenSection(resolvedSection);
        }
    }
}
