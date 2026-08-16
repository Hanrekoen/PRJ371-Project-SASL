using System.Collections.Generic;
using Convai.Editor.ConfigurationWindow;
using Convai.Editor.Settings;
using Convai.Editor.Settings.Views;
using Convai.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor
{
    /// <summary>
    ///     Provides Convai SDK settings in the Project Settings window
    ///     (Edit &gt; Project Settings &gt; Convai SDK). Thin UI Toolkit host that mounts
    ///     the shared settings section views also used by the Convai Editor window.
    /// </summary>
    public static class ConvaiSettingsProvider
    {
        private const string SettingsPath = "Project/Convai SDK";

        private static ConvaiSettingsViewContext _context;
        private static VisualElement _rootElement;
        private static List<ConvaiSettingsSectionView> _views;

        /// <summary>Creates the Project Settings provider for Convai SDK settings.</summary>
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Convai SDK",
                activateHandler = OnActivate,
                deactivateHandler = OnDeactivate,
                keywords = BuildKeywords()
            };
        }

        private static void OnActivate(string searchContext, VisualElement rootElement)
        {
            OnDeactivate();
            rootElement.Clear();
            _rootElement = rootElement;

            ConvaiSettings.EnsureSettingsAssetExists();
            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null)
            {
                rootElement.Add(new HelpBox(
                    "ConvaiSettings asset could not be loaded or created. Reimport the SDK package.",
                    HelpBoxMessageType.Error));
                return;
            }

            _context = new ConvaiSettingsViewContext(
                new SerializedObject(settings), ConvaiSettingsHostKind.ProjectSettings);

            ConvaiSettingsUi.PrepareRoot(rootElement, _context.Host);
            rootElement.Add(CreateHeader());

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("convai-settings-scroll");
            rootElement.Add(scrollView);

            scrollView.Add(ConvaiSettingsUi.CreateSaveTracker(_context));
            _views = ConvaiSettingsUi.CreateSectionViews(_context);

            foreach (ConvaiSettingsSectionView view in _views)
            {
                scrollView.Add(view);
                view.Activate();
            }

            rootElement.Bind(_context.Settings);
        }

        private static void OnDeactivate()
        {
            if (_views != null)
            {
                foreach (ConvaiSettingsSectionView view in _views) view.Deactivate();
                _views = null;
            }

            _rootElement?.Unbind();
            _context?.Dispose();
            _context = null;
            _rootElement?.Clear();
            _rootElement = null;
        }

        private static VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("convai-settings-header");

            Texture2D iconTexture = ConvaiEditorSettings.Instance != null
                ? ConvaiEditorSettings.Instance.ConvaiIconTexture
                : null;
            if (iconTexture != null)
            {
                var icon = new VisualElement();
                icon.AddToClassList("convai-settings-header-icon");
                icon.style.backgroundImage = new StyleBackground(iconTexture);
                header.Add(icon);
            }

            var title = new Label("Convai SDK");
            title.AddToClassList("convai-settings-header-title");
            header.Add(title);

            var openWindowButton = new Button(ConvaiConfigurationWindowEditor.OpenSettingsWindow)
            {
                text = "Open Convai Editor",
                tooltip = "Account, onboarding and content tools live in the Convai Editor window."
            };
            openWindowButton.AddToClassList("convai-settings-inline-button");
            header.Add(openWindowButton);

            return header;
        }

        private static HashSet<string> BuildKeywords()
        {
            var keywords = new HashSet<string>
            {
                "Convai", "API", "Key", "Server", "Environment", "Microphone", "Logging", "Transcript",
                "Notification", "Settings", "Diagnostics", "Feature", "Flags", "Health", "Timeout", "Volume",
                "Background", "Pause", "Resume", "Idle"
            };

            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings != null)
            {
                using var serialized = new SerializedObject(settings);
                keywords.UnionWith(SettingsProvider.GetSearchKeywordsFromSerializedObject(serialized));
            }

            return keywords;
        }
    }
}
