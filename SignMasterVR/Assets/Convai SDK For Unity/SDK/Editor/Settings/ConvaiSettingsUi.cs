using System.Collections.Generic;
using Convai.Editor.Settings.Views;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings
{
    /// <summary>Visual state for a settings status badge.</summary>
    public enum ConvaiSettingsBadgeState
    {
        Neutral,
        Pending,
        Ok,
        Warning,
        Error
    }

    /// <summary>Small UI factory helpers shared by the settings section views and hosts.</summary>
    public static class ConvaiSettingsUi
    {
        /// <summary>Gets the shared settings stylesheet from the editor UI asset.</summary>
        public static StyleSheet LoadSectionStylesheet() =>
            ConvaiEditorSettings.Instance.ConvaiSettingsSectionsStyleSheet;

        /// <summary>
        ///     Attaches the shared stylesheet and skin modifier classes to a settings root element.
        /// </summary>
        public static void PrepareRoot(VisualElement root, ConvaiSettingsHostKind host)
        {
            StyleSheet sheet = LoadSectionStylesheet();
            if (host == ConvaiSettingsHostKind.ProjectSettings &&
                sheet != null &&
                !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);

            root.AddToClassList("convai-settings-root");
            root.EnableInClassList("convai-settings-root--dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("convai-settings-root--light", !EditorGUIUtility.isProSkin);
            root.EnableInClassList(
                "convai-settings-root--configuration-window",
                host == ConvaiSettingsHostKind.ConfigurationWindow);
            root.EnableInClassList(
                "convai-settings-root--project-settings",
                host == ConvaiSettingsHostKind.ProjectSettings);
        }

        /// <summary>Creates a HelpBox with the shared spacing class.</summary>
        public static HelpBox CreateHelpBox(string message, HelpBoxMessageType type)
        {
            var box = new HelpBox(message, type);
            box.AddToClassList("convai-settings-helpbox");
            return box;
        }

        /// <summary>
        ///     Creates a status badge (dot + text). Use <see cref="SetBadgeState" /> to switch state.
        /// </summary>
        public static VisualElement CreateStatusBadge(out VisualElement dot, out Label text)
        {
            var badge = new VisualElement();
            badge.AddToClassList("convai-settings-badge");

            dot = new VisualElement();
            dot.AddToClassList("convai-settings-badge-dot");
            badge.Add(dot);

            text = new Label();
            text.AddToClassList("convai-settings-badge-text");
            badge.Add(text);

            return badge;
        }

        /// <summary>Applies a status modifier to a badge dot.</summary>
        public static void SetBadgeState(VisualElement dot, ConvaiSettingsBadgeState state)
        {
            dot.EnableInClassList("convai-settings-badge-dot--neutral", state == ConvaiSettingsBadgeState.Neutral);
            dot.EnableInClassList("convai-settings-badge-dot--pending", state == ConvaiSettingsBadgeState.Pending);
            dot.EnableInClassList("convai-settings-badge-dot--ok", state == ConvaiSettingsBadgeState.Ok);
            dot.EnableInClassList("convai-settings-badge-dot--warn", state == ConvaiSettingsBadgeState.Warning);
            dot.EnableInClassList("convai-settings-badge-dot--error", state == ConvaiSettingsBadgeState.Error);
        }

        /// <summary>Creates the canonical ordered section set shared by both settings hosts.</summary>
        public static List<ConvaiSettingsSectionView> CreateSectionViews(ConvaiSettingsViewContext context) =>
            new()
            {
                new SetupHealthSectionView(context),
                new CredentialsSectionView(context),
                new RuntimeDefaultsSectionView(context),
                new DiagnosticsSectionView(context),
                new AdvancedSectionView(context),
                new AboutSectionView(context)
            };

        /// <summary>
        ///     Creates an invisible element that observes bound edits and schedules a narrow
        ///     save of the Convai settings asset.
        /// </summary>
        public static VisualElement CreateSaveTracker(ConvaiSettingsViewContext context)
        {
            var tracker = new VisualElement { style = { display = DisplayStyle.None } };
            tracker.TrackSerializedObjectValue(context.Settings, _ => context.RequestSave());
            return tracker;
        }

        /// <summary>Creates a horizontal row container.</summary>
        public static VisualElement CreateRow(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("convai-settings-row");
            foreach (VisualElement child in children) row.Add(child);
            return row;
        }

        /// <summary>Creates a bound property field with aligned label styling.</summary>
        public static PropertyField CreateField(SerializedObject settings, string propertyPath, string label,
            string tooltip = null)
        {
            var field = new PropertyField(settings.FindProperty(propertyPath), label) { tooltip = tooltip };
            field.AddToClassList("convai-settings-field");
            return field;
        }
    }
}
