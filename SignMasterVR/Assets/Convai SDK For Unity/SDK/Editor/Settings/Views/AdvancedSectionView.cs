using System.Collections.Generic;
using Convai.Editor.Settings.Services;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     Advanced section: connection tuning and the Convai feature scripting-define toggles.
    /// </summary>
    public sealed class AdvancedSectionView : ConvaiSettingsSectionView
    {
        private readonly Dictionary<string, Toggle> _defineToggles = new();
        private readonly Label _activeTargetLabel;

        public AdvancedSectionView(ConvaiSettingsViewContext context)
            : base(context, "Advanced")
        {
            var timeoutSlider = new Slider("Connection Timeout (s)", 5f, 120f) { showInputField = true };
            timeoutSlider.AddToClassList("convai-settings-field");
            timeoutSlider.BindProperty(context.Settings.FindProperty("_connectionTimeout"));
            Body.Add(timeoutSlider);
            Body.Add(ConvaiSettingsUi.CreateHelpBox(
                "Adjust the timeout only for support-directed or custom transport scenarios.",
                HelpBoxMessageType.None));

            var featureTitle = new Label("Feature Flags");
            featureTitle.AddToClassList("convai-settings-subtitle");
            Body.Add(featureTitle);

            _activeTargetLabel = new Label();
            _activeTargetLabel.AddToClassList("convai-settings-hint");
            Body.Add(_activeTargetLabel);

            foreach ((string symbol, string label, string description) in ScriptingDefineToggleService.FeatureDefines)
            {
                string capturedSymbol = symbol;
                var toggle = new Toggle(label) { tooltip = $"{description}\n\nScripting define: {symbol}" };
                toggle.AddToClassList("convai-settings-field");
                toggle.RegisterValueChangedCallback(evt => OnDefineToggled(capturedSymbol, evt.newValue));
                _defineToggles[symbol] = toggle;

                var hint = new Label(description);
                hint.AddToClassList("convai-settings-hint");

                Body.Add(toggle);
                Body.Add(hint);
            }

            RefreshDefineStates();
        }

        public override void Activate()
        {
            if (!HasValidSettings) return;

            Context.Settings.Update();
            RefreshDefineStates();
        }

        protected override bool ConfirmReset() => EditorUtility.DisplayDialog(
            "Reset Advanced Settings",
            $"Restore the connection timeout and disable all Convai feature flags for " +
            $"the {ScriptingDefineToggleService.ActiveGroup} build target group?\n\n" +
            "Changing feature flags triggers a script recompile.",
            "Reset",
            "Cancel");

        protected override void ResetToDefaults()
        {
            Context.Settings.FindProperty("_connectionTimeout").floatValue = 30f;
            ScriptingDefineToggleService.ClearFeatureDefines();
        }

        protected override void OnResetApplied() => RefreshDefineStates();

        private void RefreshDefineStates()
        {
            _activeTargetLabel.text =
                $"Applied to the active build target group ({ScriptingDefineToggleService.ActiveGroup}). Changing a flag triggers a script recompile.";

            foreach (KeyValuePair<string, Toggle> pair in _defineToggles)
                pair.Value.SetValueWithoutNotify(ScriptingDefineToggleService.IsDefined(pair.Key));
        }

        private void OnDefineToggled(string symbol, bool enable)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Change Convai Feature Flag",
                $"{(enable ? "Enable" : "Disable")} {symbol} for the {ScriptingDefineToggleService.ActiveGroup} build target group?\n\n" +
                "This edits the project's scripting define symbols and triggers a script recompile.",
                enable ? "Enable" : "Disable",
                "Cancel");

            if (!confirmed)
            {
                _defineToggles[symbol].SetValueWithoutNotify(ScriptingDefineToggleService.IsDefined(symbol));
                return;
            }

            // Let the toggle's UI event finish before the recompile tears the panel down.
            EditorApplication.delayCall += () => ScriptingDefineToggleService.SetDefined(symbol, enable);
        }
    }
}
