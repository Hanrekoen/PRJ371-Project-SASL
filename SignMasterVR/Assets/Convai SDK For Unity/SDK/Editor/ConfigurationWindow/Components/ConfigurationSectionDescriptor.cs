using System;
using Convai.Editor.ConfigurationWindow.Components.Sections;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Describes one configuration window section.
    /// </summary>
    public sealed class ConfigurationSectionDescriptor
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ConfigurationSectionDescriptor" /> class.
        /// </summary>
        /// <param name="sectionId">Unique section identifier.</param>
        /// <param name="displayName">Display label for navigation.</param>
        /// <param name="factory">Factory used to create section content.</param>
        /// <param name="requiresApiKey">Whether opening this section requires an API key.</param>
        /// <param name="isEnabled">Whether this section is enabled for the current compile configuration.</param>
        public ConfigurationSectionDescriptor(
            string sectionId,
            string displayName,
            Func<ConfigurationWindowContext, ConvaiBaseSection> factory,
            bool requiresApiKey = false,
            bool isEnabled = true)
        {
            SectionId = sectionId;
            DisplayName = displayName;
            Factory = factory;
            RequiresApiKey = requiresApiKey;
            IsEnabled = isEnabled;
        }

        /// <summary>Unique section identifier.</summary>
        public string SectionId { get; }

        /// <summary>Navigation display label.</summary>
        public string DisplayName { get; }

        /// <summary>Factory for creating section UI.</summary>
        public Func<ConfigurationWindowContext, ConvaiBaseSection> Factory { get; }

        /// <summary>Whether section access requires a configured API key.</summary>
        public bool RequiresApiKey { get; }

        /// <summary>Whether this descriptor is currently enabled.</summary>
        public bool IsEnabled { get; }
    }
}
