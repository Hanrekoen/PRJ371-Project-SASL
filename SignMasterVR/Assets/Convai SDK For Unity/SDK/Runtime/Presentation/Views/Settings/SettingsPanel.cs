using System;
using System.Collections.Generic;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Services.Settings;
using Convai.Runtime.Presentation.Services.Utilities;
using Convai.Runtime.Room;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Settings
{
    /// <summary>
    ///     Runtime settings panel view.
    ///     Rendering and input collection live here; all behavior is handled by <see cref="SettingsPanelPresenter" />.
    /// </summary>
    public class SettingsPanel : MonoBehaviour, ISettingsPanelView
    {
        [Header("Data")] [SerializeField] private float fadeDuration = 0.5f;

        [Header("Visuals")]
        [SerializeField] private TMP_InputField playerNameInputField;
        [SerializeField] private TMP_Dropdown voiceInputDropdown;
        [SerializeField] private TMP_Dropdown conversationModeDropdown;
        [SerializeField] private Toggle transcriptToggle;
        [SerializeField] private Toggle notificationToggle;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject microphoneDropContainer;
        [SerializeField] private GameObject transcriptModeContainer;
        [SerializeField] private GameObject conversationModeContainer;

        private static readonly List<string> ConversationModeLabels = new() { "Hands Free", "Push to Talk" };

        private readonly List<ConvaiMicrophoneDevice> _microphoneOptions = new();
        private CanvasFader _canvasFader;

        private CanvasGroup _canvasGroup;
        private Func<string> _effectivePlayerNameProvider;
        private bool _isPresenterBound;
        private IMicrophoneDeviceService _microphoneDeviceService;

        private IConvaiSettingsPanelController _panelController;
        private SettingsPanelPresenter _presenter;
        private IConvaiRoomConnectionService _roomConnectionService;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;
        private bool _visibilitySubscribed;

        private void Awake()
        {
            EnsureFadeComponents();

            if (conversationModeDropdown != null)
            {
                conversationModeDropdown.ClearOptions();
                conversationModeDropdown.AddOptions(ConversationModeLabels);
            }

            if (transcriptModeContainer != null)
                transcriptModeContainer.SetActive(false);

            if (saveButton != null) saveButton.onClick.AddListener(HandleSaveClicked);

            if (closeButton != null) closeButton.onClick.AddListener(HandleCloseClicked);

            HideImmediate();
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            TryInitializePresenter();
            SubscribeToVisibility();

            if (_panelController != null)
                HandleVisibilityChanged(_panelController.IsOpen);
            else
                HideImmediate();
        }

        private void OnDisable()
        {
            UnsubscribeFromVisibility();
            UnbindPresenter();
        }

        private void OnDestroy()
        {
            if (saveButton != null) saveButton.onClick.RemoveListener(HandleSaveClicked);

            if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseClicked);

            UnsubscribeFromVisibility();
            UnbindPresenter();
            _presenter?.Dispose();
            _presenter = null;
        }

        public event Action SaveRequested;
        public event Action CloseRequested;

        public string PlayerDisplayNameInput => playerNameInputField != null ? playerNameInputField.text : null;
        public bool TranscriptEnabledInput => transcriptToggle == null || transcriptToggle.isOn;
        public bool NotificationsEnabledInput => notificationToggle != null && notificationToggle.isOn;

        public string SelectedMicrophoneDeviceId
        {
            get
            {
                if (_microphoneOptions.Count == 0 || voiceInputDropdown == null) return string.Empty;

                int index = Mathf.Clamp(voiceInputDropdown.value, 0, _microphoneOptions.Count - 1);
                return _microphoneOptions[index].Id;
            }
        }

        public ConversationInputMode SelectedConversationInputModeInput
        {
            get
            {
                if (conversationModeDropdown == null) return ConversationInputMode.HandsFree;
                return conversationModeDropdown.value == 1 ? ConversationInputMode.PushToTalk : ConversationInputMode.HandsFree;
            }
        }

        public void SetPlayerDisplayName(string value)
        {
            playerNameInputField?.SetTextWithoutNotify(value ?? string.Empty);
        }

        public void SetConversationInputMode(ConversationInputMode mode)
        {
            conversationModeDropdown?.SetValueWithoutNotify(mode == ConversationInputMode.PushToTalk ? 1 : 0);
        }

        public void SetConversationInputModeVisible(bool visible)
        {
            if (conversationModeContainer != null)
                conversationModeContainer.SetActive(visible);
            else if (conversationModeDropdown != null)
                conversationModeDropdown.gameObject.SetActive(visible);
        }

        public void SetTranscriptEnabled(bool value)
        {
            if (transcriptToggle == null) return;

            transcriptToggle.SetIsOnWithoutNotify(value);
        }

        public void SetNotificationsEnabled(bool value)
        {
            if (notificationToggle == null) return;

            notificationToggle.SetIsOnWithoutNotify(value);
        }

        public void SetMicrophoneOptions(IReadOnlyList<ConvaiMicrophoneDevice> devices, string selectedDeviceId)
        {
            _microphoneOptions.Clear();
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                    _microphoneOptions.Add(devices[i]);
            }

            if (voiceInputDropdown != null)
            {
                voiceInputDropdown.ClearOptions();

                var optionNames = new List<string>(_microphoneOptions.Count);
                for (int i = 0; i < _microphoneOptions.Count; i++) optionNames.Add(_microphoneOptions[i].Name);

                voiceInputDropdown.AddOptions(optionNames);

                int selectedIndex = 0;
                if (!string.IsNullOrEmpty(selectedDeviceId))
                {
                    for (int i = 0; i < _microphoneOptions.Count; i++)
                    {
                        if (string.Equals(_microphoneOptions[i].Id, selectedDeviceId, StringComparison.Ordinal))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                if (_microphoneOptions.Count > 0) voiceInputDropdown.SetValueWithoutNotify(selectedIndex);
            }

            if (microphoneDropContainer != null) microphoneDropContainer.SetActive(_microphoneOptions.Count > 0);
        }

        public void Inject(
            IConvaiSettingsPanelController panelController,
            IConvaiRuntimeSettingsService runtimeSettingsService,
            IMicrophoneDeviceService microphoneDeviceService,
            IConvaiRoomConnectionService roomConnectionService = null,
            Func<string> effectivePlayerNameProvider = null)
        {
            UnsubscribeFromVisibility();
            UnbindPresenter();
            _presenter?.Dispose();
            _presenter = null;

            _panelController = panelController;
            _runtimeSettingsService = runtimeSettingsService;
            _microphoneDeviceService = microphoneDeviceService;
            _roomConnectionService = roomConnectionService;
            _effectivePlayerNameProvider = effectivePlayerNameProvider;

            TryInitializePresenter();
            SubscribeToVisibility();
        }

        private void TryResolveDependencies()
        {
            if (_panelController != null &&
                _runtimeSettingsService != null &&
                _microphoneDeviceService != null)
                return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetSettingsPanelController(out IConvaiSettingsPanelController panelController);
            manager.TryGetRuntimeSettingsService(out IConvaiRuntimeSettingsService runtimeSettingsService);
            manager.TryGetMicrophoneDeviceService(out IMicrophoneDeviceService microphoneDeviceService);
            manager.TryGetRoomConnectionService(out IConvaiRoomConnectionService roomConnectionService);

            if (panelController != null && runtimeSettingsService != null && microphoneDeviceService != null)
                Inject(
                    panelController,
                    runtimeSettingsService,
                    microphoneDeviceService,
                    roomConnectionService,
                    () => manager.Player?.PlayerName);
        }

        private void HandleSaveClicked() => SaveRequested?.Invoke();

        private void HandleCloseClicked() => CloseRequested?.Invoke();

        private void HandleVisibilityChanged(bool isVisible)
        {
            if (isVisible)
                Show();
            else
                Hide();
        }

        private void Show()
        {
            EnsureFadeComponents();

            if (_canvasFader != null)
                _canvasFader.StartFadeIn(_canvasGroup, fadeDuration);
            else if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        private void Hide()
        {
            EnsureFadeComponents();

            if (_canvasFader != null)
                _canvasFader.StartFadeOut(_canvasGroup, fadeDuration);
            else
                HideImmediate();
        }

        private void HideImmediate()
        {
            EnsureFadeComponents();

            if (_canvasGroup == null) return;

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        private void TryInitializePresenter()
        {
            if (_panelController == null || _runtimeSettingsService == null || _microphoneDeviceService == null) return;

            if (_presenter == null)
            {
                _presenter =
                    new SettingsPanelPresenter(
                        _runtimeSettingsService,
                        _panelController,
                        _microphoneDeviceService,
                        _roomConnectionService,
                        _effectivePlayerNameProvider);
            }

            if (!_isPresenterBound)
            {
                _presenter.Bind(this);
                _isPresenterBound = true;
            }
        }

        private void UnbindPresenter()
        {
            if (_presenter == null || !_isPresenterBound) return;

            _presenter.Unbind();
            _isPresenterBound = false;
        }

        private void SubscribeToVisibility()
        {
            if (_panelController == null || _visibilitySubscribed) return;

            _panelController.VisibilityChanged += HandleVisibilityChanged;
            _visibilitySubscribed = true;
        }

        private void UnsubscribeFromVisibility()
        {
            if (_panelController == null || !_visibilitySubscribed) return;

            _panelController.VisibilityChanged -= HandleVisibilityChanged;
            _visibilitySubscribed = false;
        }

        private void EnsureFadeComponents()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_canvasFader == null)
                _canvasFader = GetComponent<CanvasFader>() ?? GetComponentInChildren<CanvasFader>();
        }
    }
}
