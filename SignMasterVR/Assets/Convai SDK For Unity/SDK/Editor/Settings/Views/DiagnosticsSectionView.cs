using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.Settings.Services;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     Diagnostics section: global log level, presets, output flags and
    ///     per-category level overrides.
    /// </summary>
    public sealed class DiagnosticsSectionView : ConvaiSettingsSectionView
    {
        private const string InheritChoice = "Inherit";

        private readonly Foldout _overridesFoldout;
        private readonly Dictionary<LogCategory, DropdownField> _overrideDropdowns = new();
        private bool _refreshingOverrides;

        public DiagnosticsSectionView(ConvaiSettingsViewContext context)
            : base(context, "Diagnostics")
        {
            var presetRow = new VisualElement();
            presetRow.AddToClassList("convai-settings-row");
            presetRow.AddToClassList("convai-settings-preset-row");
            presetRow.Add(new Label("Presets") { tooltip = "One-click logging configurations." });
            presetRow.Add(CreatePresetButton("Verbose", "Trace level with stack traces; clears overrides.",
                () => LogOverrideEditing.ApplyPreset(Context.Settings, LogLevel.Trace, true, true)));
            presetRow.Add(CreatePresetButton("Default", "Info level, SDK defaults; clears overrides.",
                () => LogOverrideEditing.ResetToDefaults(Context.Settings)));
            presetRow.Add(CreatePresetButton("Errors Only", "Error level only; clears overrides.",
                () => LogOverrideEditing.ApplyPreset(Context.Settings, LogLevel.Error, true, true)));
            Body.Add(presetRow);

            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_globalLogLevel",
                "Global Log Level", "Global minimum log level. Logs below this level are filtered."));
            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_includeStackTraces",
                "Include Stack Traces", "Enable stack traces for Warning and Error logs."));
            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_coloredOutput",
                "Colored Console Output", "Enable colored output in the Unity Console."));

            _overridesFoldout = new Foldout
            {
                viewDataKey = "convai-settings-category-overrides",
                value = false
            };
            _overridesFoldout.AddToClassList("convai-settings-overrides-foldout");
            BuildOverrideRows();
            Body.Add(_overridesFoldout);

            this.TrackPropertyValue(context.Settings.FindProperty("_categoryOverrides"), _ => RefreshOverrideRows());
            RefreshOverrideRows();
        }

        public override void Activate()
        {
            if (!HasValidSettings) return;

            Context.Settings.Update();
            RefreshOverrideRows();
        }

        protected override void ResetToDefaults() => LogOverrideEditing.ResetToDefaults(Context.Settings);

        private Button CreatePresetButton(string label, string tooltip, Action applyPreset)
        {
            var button = new Button(() =>
            {
                if (!HasValidSettings) return;

                Context.Settings.Update();
                applyPreset();
                ApplyAndSave();
                RefreshOverrideRows();
            })
            {
                text = label,
                tooltip = tooltip
            };
            button.AddToClassList("convai-settings-chip");
            return button;
        }

        private void BuildOverrideRows()
        {
            var categories = (LogCategory[])Enum.GetValues(typeof(LogCategory));
            Array.Sort(categories, (a, b) => string.Compare(
                GetCategoryDisplayName(a), GetCategoryDisplayName(b), StringComparison.OrdinalIgnoreCase));

            var choices = new List<string> { InheritChoice };
            foreach (LogLevel level in (LogLevel[])Enum.GetValues(typeof(LogLevel)))
                choices.Add(level.ToString());

            foreach (LogCategory category in categories)
            {
                LogCategory captured = category;
                var dropdown = new DropdownField(GetCategoryDisplayName(category)) { choices = choices };
                dropdown.AddToClassList("convai-settings-field");
                dropdown.AddToClassList("convai-settings-override-row");
                dropdown.RegisterValueChangedCallback(evt => OnOverrideChanged(captured, evt.newValue));
                _overrideDropdowns[category] = dropdown;
                _overridesFoldout.Add(dropdown);
            }
        }

        private void OnOverrideChanged(LogCategory category, string choice)
        {
            if (_refreshingOverrides || !HasValidSettings) return;

            LogLevel? level = null;
            if (!string.Equals(choice, InheritChoice, StringComparison.Ordinal) &&
                Enum.TryParse(choice, out LogLevel parsed)) level = parsed;

            Context.Settings.Update();
            LogOverrideEditing.SetOverride(Context.Settings, category, level);
            ApplyAndSave();
            UpdateFoldoutTitle();
        }

        private void RefreshOverrideRows()
        {
            if (!HasValidSettings) return;

            _refreshingOverrides = true;
            try
            {
                Dictionary<LogCategory, LogLevel> overrides =
                    LogOverrideEditing.GetOverridesSnapshot(Context.Settings);
                foreach (KeyValuePair<LogCategory, DropdownField> pair in _overrideDropdowns)
                {
                    string choice = overrides.TryGetValue(pair.Key, out LogLevel level)
                        ? level.ToString()
                        : InheritChoice;
                    pair.Value.SetValueWithoutNotify(choice);
                }

                UpdateFoldoutTitle();
            }
            finally
            {
                _refreshingOverrides = false;
            }
        }

        private void UpdateFoldoutTitle() =>
            _overridesFoldout.text = $"Category Overrides ({LogOverrideEditing.GetOverrideCount(Context.Settings)})";

        private static string GetCategoryDisplayName(LogCategory category) =>
            ObjectNames.NicifyVariableName(category.ToString());
    }
}
