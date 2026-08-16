using System.Collections.Generic;
using Convai.Runtime;
using Convai.Runtime.Settings;
using Convai.Shared.Types;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     Runtime Defaults section: values that seed the runtime settings service
    ///     (transcripts, notifications, microphone, player identity, audio).
    /// </summary>
    public sealed class RuntimeDefaultsSectionView : ConvaiSettingsSectionView
    {
        private const string SystemDefaultChoice = "System Default";

        private readonly MicrophoneDeviceService _microphoneService = new();
        private readonly DropdownField _microphoneDropdown;
        private readonly List<string> _deviceIds = new();

        public RuntimeDefaultsSectionView(ConvaiSettingsViewContext context)
            : base(context, "Runtime Defaults")
        {
            Body.Add(ConvaiSettingsUi.CreateHelpBox(
                "These values seed the runtime settings service. They apply project-wide until a runtime settings UI or script overrides them.",
                HelpBoxMessageType.Info));

            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_transcriptSystemEnabled",
                "Transcript System", "Enable transcript UI/event flow by default."));
            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_notificationSystemEnabled",
                "Notification System", "Enable session-state and error notifications by default."));
            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_defaultPlayerDisplayName",
                "Player Display Name", "Default display name for the local player."));

            _microphoneDropdown = new DropdownField("Microphone")
            {
                tooltip = "Project-wide default microphone. System Default follows the OS device order."
            };
            _microphoneDropdown.AddToClassList("convai-settings-field");
            _microphoneDropdown.RegisterValueChangedCallback(_ => OnMicrophoneSelected());

            var refreshButton = new Button(RefreshMicrophoneChoices)
            {
                text = "Refresh",
                tooltip = "Re-enumerate connected microphone devices."
            };
            refreshButton.AddToClassList("convai-settings-inline-button");

            VisualElement micRow = ConvaiSettingsUi.CreateRow(_microphoneDropdown, refreshButton);
            micRow.AddToClassList("convai-settings-mic-row");
            Body.Add(micRow);

            var volumeSlider = new Slider("Character Audio Volume", 0f, 1f) { showInputField = true };
            volumeSlider.AddToClassList("convai-settings-field");
            volumeSlider.BindProperty(context.Settings.FindProperty("_characterAudioVolume"));
            Body.Add(volumeSlider);

            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_audioFeedbackEnabled",
                "Audio Feedback", "Play audio feedback sounds (e.g., listening indicator) by default."));
            Body.Add(ConvaiSettingsUi.CreateField(context.Settings, "_backgroundPolicy",
                "Background Policy",
                "Choose whether Convai continues audibly, pauses local presentation, or mutes while catching up when the app is backgrounded."));

            RefreshMicrophoneChoices();
        }

        public override void Activate()
        {
            if (!HasValidSettings) return;

            Context.Settings.Update();
            RefreshMicrophoneChoices();
        }

        protected override void ResetToDefaults()
        {
            Context.Settings.FindProperty("_transcriptSystemEnabled").boolValue = true;
            Context.Settings.FindProperty("_notificationSystemEnabled").boolValue = false;
            Context.Settings.FindProperty("_defaultPlayerDisplayName").stringValue = "Player";
            Context.Settings.FindProperty("_defaultMicrophoneDeviceId").stringValue = string.Empty;
            Context.Settings.FindProperty("_characterAudioVolume").floatValue = 1f;
            Context.Settings.FindProperty("_audioFeedbackEnabled").boolValue = true;
            Context.Settings.FindProperty("_backgroundPolicy").enumValueIndex = 1;
            RefreshMicrophoneChoices();
        }

        private void RefreshMicrophoneChoices()
        {
            if (!HasValidSettings) return;

            string savedDeviceId = Context.Settings.FindProperty("_defaultMicrophoneDeviceId").stringValue ?? "";

            _deviceIds.Clear();
            var choices = new List<string> { SystemDefaultChoice };
            _deviceIds.Add(string.Empty);

            bool savedFound = string.IsNullOrEmpty(savedDeviceId);
            foreach (ConvaiMicrophoneDevice device in _microphoneService.GetAvailableDevices())
            {
                // Use the id as display text: it equals the name for unique devices and
                // carries a #n suffix for duplicates, keeping dropdown entries unambiguous.
                choices.Add(device.Id);
                _deviceIds.Add(device.Id);
                if (device.Id == savedDeviceId) savedFound = true;
            }

            if (!savedFound)
            {
                choices.Add($"{savedDeviceId} (not connected)");
                _deviceIds.Add(savedDeviceId);
            }

            _microphoneDropdown.choices = choices;
            int selectedIndex = _deviceIds.IndexOf(savedDeviceId);
            _microphoneDropdown.SetValueWithoutNotify(choices[selectedIndex < 0 ? 0 : selectedIndex]);
        }

        private void OnMicrophoneSelected()
        {
            if (!HasValidSettings) return;

            int index = _microphoneDropdown.choices.IndexOf(_microphoneDropdown.value);
            if (index < 0 || index >= _deviceIds.Count) return;

            SerializedProperty property = Context.Settings.FindProperty("_defaultMicrophoneDeviceId");
            if (property.stringValue == _deviceIds[index]) return;

            property.stringValue = _deviceIds[index];
            ApplyAndSave();
        }
    }
}
