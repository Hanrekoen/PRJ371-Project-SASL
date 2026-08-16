using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Editor.ConfigurationWindow.Components;
using Convai.Editor.ConfigurationWindow.Components.Sections;
using Convai.Editor.ConfigurationWindow.Components.Sections.LongTermMemory;
using Convai.Editor.UI;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow
{
    /// <summary>
    ///     Main editor window for the Convai SDK configuration.
    ///     Provides a comprehensive UI for managing SDK settings, account, logging, and features.
    /// </summary>
    /// <remarks>
    ///     Access via the Convai menu in Unity's menu bar.
    ///     The window uses UI Toolkit for rendering and supports multiple configuration sections.
    /// </remarks>
    public class ConvaiConfigurationWindowEditor : EditorWindow
    {
        private const string WindowTitle = "Convai Editor";
        private static readonly Vector2 MinimumWindowSize = new(980, 620);
        private ConvaiConfigurationWindow _configurationWindow;
        private string _pendingSection = ConvaiWelcomeSection.SECTION_NAME;

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            _configurationWindow = new ConvaiConfigurationWindow();
            rootVisualElement.Add(_configurationWindow);
            rootVisualElement.styleSheets.Clear();
            if (ConvaiEditorSettings.Instance.UnityStyleSheet != null)
                rootVisualElement.styleSheets.Add(ConvaiEditorSettings.Instance.UnityStyleSheet);
            if (ConvaiEditorSettings.Instance.ConvaiConfigurationWindowStyleSheet != null)
                rootVisualElement.styleSheets.Add(ConvaiEditorSettings.Instance.ConvaiConfigurationWindowStyleSheet);
            if (ConvaiEditorSettings.Instance.ConvaiSettingsSectionsStyleSheet != null)
                rootVisualElement.styleSheets.Add(ConvaiEditorSettings.Instance.ConvaiSettingsSectionsStyleSheet);
            rootVisualElement.Q<VisualElement>("convai-logo").style.backgroundImage =
                new StyleBackground(ConvaiEditorSettings.Instance.ConvaiLogoTextureWhite);
            TryOpenPendingSection();
        }

        // Only two of the sections below carry a menu entry. The rest are reachable one click
        // further in, from this window's own navigation rail, which is where a list of sections
        // belongs: a menu that mirrors a sidebar makes the menu as long as the window and teaches
        // the user nothing about where the sections actually live. Their methods stay public —
        // they are the documented way to open the window at a given section from your own tooling.

        /// <summary>Opens the configuration window to the Welcome section — the window's front door.</summary>
        [MenuItem("Convai/Convai Editor", priority = ConvaiEditorMenu.Configuration + 0)]
        public static void OpenWelcomeWindow() => OpenSection(ConvaiWelcomeSection.SECTION_NAME);

        /// <summary>Opens the configuration window to the Account section.</summary>
        public static void OpenAccountWindow() => OpenSection(ConvaiAccountSection.SECTION_NAME);

        /// <summary>Opens the configuration window to the SDK Settings section.</summary>
        [MenuItem("Convai/Settings", priority = ConvaiEditorMenu.Configuration + 1)]
        public static void OpenSettingsWindow() => OpenSection(ConvaiSettingsSection.SECTION_NAME);

        /// <summary>Opens the configuration window to the AI Coding section.</summary>
        public static void OpenAICodingWindow() => OpenSection(ConvaiAICodingSection.SECTION_NAME);

        /// <summary>Opens the configuration window to the Config Assets section.</summary>
        public static void OpenConfigAssetsWindow() => OpenSection(ConvaiProfilesSection.SECTION_NAME);

        /// <summary>Opens the configuration window to the Long Term Memory section.</summary>
        public static void OpenLongTermMemoryWindow() => OpenSection(ConvaiLongTermMemorySection.SECTION_NAME);

#if CONVAI_ENABLE_UPDATES_SECTION
        /// <summary>Opens the configuration window to the Updates section.</summary>
        public static void OpenUpdateWindow() => OpenSection(ConvaiUpdatesSection.SECTION_NAME);
#endif

        /// <summary>Opens the configuration window to the Contact section.</summary>
        public static void OpenContactUsWindow() => OpenSection(ConvaiContactSection.SECTION_NAME);

        private static void OpenSection(string sectionName)
        {
            IReadOnlyList<ConfigurationSectionDescriptor> enabledSections =
                ConfigurationSectionRegistry.GetEnabledSections();
            ConfigurationSectionDescriptor descriptor = enabledSections.FirstOrDefault(section =>
                string.Equals(section.SectionId, sectionName, StringComparison.Ordinal));

            if (descriptor == null && enabledSections.Count > 0) descriptor = enabledSections[0];

            if (descriptor == null) return;

            bool hasApiKey = ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            if (descriptor.RequiresApiKey && !hasApiKey)
            {
                EditorUtility.DisplayDialog(
                    "API Key Required",
                    "Please set up your API Key in the Settings section (Convai > Settings) to access this section.",
                    "OK");
                descriptor = enabledSections.FirstOrDefault(section =>
                                 string.Equals(section.SectionId, ConvaiWelcomeSection.SECTION_NAME,
                                     StringComparison.Ordinal)) ??
                             descriptor;
            }

            var window = GetWindow<ConvaiConfigurationWindowEditor>(WindowTitle);
            window.minSize = MinimumWindowSize;
            window._pendingSection = descriptor.SectionId;
            window.Show();
            window.Focus();
            window.TryOpenPendingSection();
        }

        private void TryOpenPendingSection()
        {
            if (_configurationWindow == null) return;

            if (string.IsNullOrEmpty(_pendingSection)) _pendingSection = ConvaiWelcomeSection.SECTION_NAME;

            _configurationWindow.OpenSection(_pendingSection);
        }
    }
}
