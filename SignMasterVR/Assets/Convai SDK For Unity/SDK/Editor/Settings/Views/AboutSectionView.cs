using System.Text;
using Convai.Application;
using Convai.Editor.Settings.Services;
using Convai.Editor.Utilities;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     About section: SDK version, canonical links and support-info export.
    /// </summary>
    public sealed class AboutSectionView : ConvaiSettingsSectionView
    {
        public AboutSectionView(ConvaiSettingsViewContext context)
            : base(context, "About", supportsReset: false)
        {
            var versionLabel = new Label(
                $"Convai SDK {ConvaiSDK.Version}  ·  Unity {UnityEngine.Application.unityVersion}");
            versionLabel.AddToClassList("convai-settings-version");
            Body.Add(versionLabel);

            var linksRow = new VisualElement();
            linksRow.AddToClassList("convai-settings-row");
            linksRow.AddToClassList("convai-settings-links-row");
            linksRow.Add(CreateLinkButton("Dashboard", ConvaiEditorLinks.DashboardHomeUrl));
            linksRow.Add(CreateLinkButton("Documentation", ConvaiEditorLinks.DocsHomeUrl));
            linksRow.Add(CreateLinkButton("Quickstart", ConvaiEditorLinks.DocsUnityQuickstartUrl));
            linksRow.Add(CreateLinkButton("Changelog", ConvaiEditorLinks.ChangelogUrl));
            linksRow.Add(CreateLinkButton("Forum", ConvaiEditorLinks.DeveloperForumUrl));
            Body.Add(linksRow);

            var supportRow = new VisualElement();
            supportRow.AddToClassList("convai-settings-row");
            var copyButton = new Button(CopySupportInfo)
            {
                text = "Copy Support Info",
                tooltip = "Copies environment details (never the API key) for support tickets."
            };
            copyButton.AddToClassList("convai-settings-inline-button");
            supportRow.Add(copyButton);

            var supportLabel = new Label($"Support: {ConvaiEditorLinks.SupportEmail}");
            supportLabel.AddToClassList("convai-settings-hint");
            supportRow.Add(supportLabel);
            Body.Add(supportRow);
        }

        private static Button CreateLinkButton(string label, string url)
        {
            var button = new Button(() => UnityEngine.Application.OpenURL(url))
            {
                text = label,
                tooltip = url
            };
            button.AddToClassList("convai-settings-chip");
            return button;
        }

        private void CopySupportInfo()
        {
            ConvaiSettings settings = HasValidSettings ? (ConvaiSettings)Context.Settings.targetObject : null;

            var builder = new StringBuilder();
            builder.AppendLine($"Convai SDK: {ConvaiSDK.Version}");
            builder.AppendLine($"Unity: {UnityEngine.Application.unityVersion}");
            builder.AppendLine($"OS: {SystemInfo.operatingSystem}");
            builder.AppendLine($"Active Build Target: {EditorUserBuildSettings.activeBuildTarget}");
            if (settings != null)
            {
                builder.AppendLine($"Environment: {settings.ApiEnvironment}");
                builder.AppendLine($"API Key Configured: {settings.HasApiKey}");
                if (settings.ApiEnvironment == ConvaiApiEnvironment.Custom)
                    builder.AppendLine($"Core Server URL: {settings.ServerUrl}");
                builder.AppendLine($"Transcript System: {settings.TranscriptSystemEnabled}");
                builder.AppendLine($"Notification System: {settings.NotificationSystemEnabled}");
                builder.AppendLine($"Background Policy: {settings.BackgroundPolicy}");
                builder.AppendLine($"Global Log Level: {settings.GlobalLogLevel}");
            }

            foreach ((string symbol, _, _) in ScriptingDefineToggleService.FeatureDefines)
                builder.AppendLine($"{symbol}: {ScriptingDefineToggleService.IsDefined(symbol)}");

            EditorGUIUtility.systemCopyBuffer = builder.ToString();
        }
    }
}
