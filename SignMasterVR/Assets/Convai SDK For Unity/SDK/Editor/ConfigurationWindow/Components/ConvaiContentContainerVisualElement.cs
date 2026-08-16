using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Editor.ConfigurationWindow.Components.Sections;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Container visual element that holds and manages all configuration sections.
    ///     Handles section switching and visibility based on navigation selection.
    /// </summary>
    /// <remarks>
    ///     Sections are registered during construction and displayed/hidden based on navigation.
    ///     Uses a ScrollView for content that may exceed the visible area.
    /// </remarks>
    [UxmlElement]
    public partial class ConvaiContentContainerVisualElement : VisualElement
    {
        private readonly ConfigurationWindowContext _context;
        private readonly IReadOnlyList<ConfigurationSectionDescriptor> _enabledSections;
        private readonly Dictionary<string, ConvaiBaseSection> _sectionInstances = new(StringComparer.Ordinal);
        private ScrollView _contentContainer;

        public ConvaiContentContainerVisualElement()
            : this(new ConfigurationWindowContext(), ConfigurationSectionRegistry.GetEnabledSections())
        {
        }

        public ConvaiContentContainerVisualElement(
            ConfigurationWindowContext context,
            IReadOnlyList<ConfigurationSectionDescriptor> enabledSections)
        {
            _context = context ?? new ConfigurationWindowContext();
            _enabledSections = enabledSections ?? Array.Empty<ConfigurationSectionDescriptor>();
            AddToClassList("content-container");
            CreateContentContainer();
        }

        /// <summary>
        ///     Gets the first enabled section id or empty string when none are registered.
        /// </summary>
        public string FirstSectionId => _enabledSections.Count > 0 ? _enabledSections[0].SectionId : string.Empty;

        private void CreateContentContainer()
        {
            _contentContainer = new ScrollView { name = "content-container" };
            Add(_contentContainer);
        }

        /// <summary>
        ///     Gets whether the container has an enabled section for the given id.
        /// </summary>
        public bool ContainsSection(string sectionName)
        {
            return _enabledSections.Any(section =>
                string.Equals(section.SectionId, sectionName, StringComparison.Ordinal));
        }

        private ConvaiBaseSection EnsureSectionInstance(string sectionName)
        {
            if (_sectionInstances.TryGetValue(sectionName, out ConvaiBaseSection existing)) return existing;

            ConfigurationSectionDescriptor descriptor = _enabledSections.FirstOrDefault(section =>
                string.Equals(section.SectionId, sectionName, StringComparison.Ordinal));
            if (descriptor?.Factory == null) return null;

            ConvaiBaseSection instance = descriptor.Factory(_context);
            if (instance == null) return null;

            _sectionInstances[sectionName] = instance;
            _contentContainer.Add(instance);
            return instance;
        }

        /// <summary>
        ///     Eagerly creates the Account section and starts its data fetch
        ///     so the usage data is ready when the user navigates to it.
        /// </summary>
        public void PreWarmSections()
        {
            ConvaiBaseSection section = EnsureSectionInstance(ConvaiAccountSection.SECTION_NAME);
            if (section == null) return;

            section.style.display = DisplayStyle.None;

            if (section is ConvaiAccountSection accountSection)
                accountSection.PreWarmData();
        }

        /// <summary>
        ///     Shows the specified section and hides all others.
        /// </summary>
        /// <param name="sectionName">Section name to display.</param>
        public void OpenSection(string sectionName)
        {
            string targetSection = sectionName;
            if (!ContainsSection(targetSection)) targetSection = FirstSectionId;

            if (string.IsNullOrEmpty(targetSection)) return;

            ConvaiBaseSection sectionToShow = EnsureSectionInstance(targetSection);
            if (sectionToShow == null) return;

            foreach (KeyValuePair<string, ConvaiBaseSection> pair in _sectionInstances)
            {
                if (pair.Key == targetSection) pair.Value.ShowSection();
                else pair.Value.HideSection();
            }
        }
    }
}
