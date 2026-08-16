using Convai.Editor.Settings.Services;
using Convai.Runtime;
using Convai.Runtime.Core.Configuration;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     Credentials section: runtime authentication mode, API key entry with validation,
    ///     auth-token endpoint configuration, and service environment selection.
    /// </summary>
    public sealed class CredentialsSectionView : ConvaiSettingsSectionView
    {
        private readonly TextField _apiKeyField;
        private readonly VisualElement _apiKeyGroup;
        private readonly SerializedProperty _authModeProperty;
        private readonly VisualElement _authTokenGroup;
        private readonly SerializedProperty _authTokenEndpointUrlProperty;
        private readonly SerializedProperty _authTokenHeadersProperty;
        private readonly SerializedProperty _authTokenHttpMethodProperty;
        private readonly SerializedProperty _authTokenResponseFieldProperty;
        private readonly Button _validateButton;
        private readonly VisualElement _badgeDot;
        private readonly Label _badgeText;
        private readonly VisualElement _customGroup;
        private readonly SerializedProperty _environmentProperty;
        private readonly SerializedProperty _serverUrlProperty;
        private readonly SerializedProperty _customRestBaseUrlProperty;
        private bool _showApiKey;

        public CredentialsSectionView(ConvaiSettingsViewContext context)
            : base(context, "Credentials")
        {
            _authModeProperty = context.Settings.FindProperty("_authMode");
            _authTokenEndpointUrlProperty = context.Settings.FindProperty("_authTokenEndpointUrl");
            _authTokenHttpMethodProperty = context.Settings.FindProperty("_authTokenHttpMethod");
            _authTokenResponseFieldProperty = context.Settings.FindProperty("_authTokenResponseField");
            _authTokenHeadersProperty = context.Settings.FindProperty("_authTokenHeaders");
            _environmentProperty = context.Settings.FindProperty("_apiEnvironment");
            _serverUrlProperty = context.Settings.FindProperty("_serverUrl");
            _customRestBaseUrlProperty = context.Settings.FindProperty("_customRestBaseUrl");

            Body.Add(ConvaiSettingsUi.CreateHelpBox(
                "Choose how shipped players authenticate. API Key mode reads the project key locally. " +
                "Auth Token mode obtains a short-lived credential from your backend whenever a room connection starts.",
                HelpBoxMessageType.Info));

            var authModeField = new PropertyField(_authModeProperty, "Auth Mode")
            {
                tooltip = "API Key is intended for local development. Auth Token removes the account key from Auth Token builds."
            };
            authModeField.AddToClassList("convai-settings-field");
            Body.Add(authModeField);

            _apiKeyGroup = new VisualElement();
            _apiKeyGroup.AddToClassList("convai-settings-subgroup");
            _apiKeyGroup.Add(ConvaiSettingsUi.CreateHelpBox(
                "The key is stored obfuscated (not encrypted) in the project settings asset and is included in API Key builds.",
                HelpBoxMessageType.Warning));

            _apiKeyField = new TextField("API Key")
            {
                isPasswordField = true,
                maskChar = '●',
                tooltip = "Your Convai dashboard API key."
            };
            _apiKeyField.AddToClassList("convai-settings-field");
            _apiKeyField.AddToClassList("convai-settings-apikey-field");
            _apiKeyField.RegisterValueChangedCallback(_ => OnApiKeyInputChanged());

            var showHideButton = new Button { text = "Show" };
            showHideButton.AddToClassList("convai-settings-inline-button");
            showHideButton.clicked += () =>
            {
                _showApiKey = !_showApiKey;
                _apiKeyField.isPasswordField = !_showApiKey;
                showHideButton.text = _showApiKey ? "Hide" : "Show";
            };

            VisualElement keyRow = ConvaiSettingsUi.CreateRow(_apiKeyField, showHideButton);
            keyRow.AddToClassList("convai-settings-apikey-row");
            _apiKeyGroup.Add(keyRow);

            _validateButton = new Button(OnValidateClicked) { text = "Validate & Save" };
            _validateButton.AddToClassList("convai-settings-primary-button");

            var clearButton = new Button(OnClearClicked)
            {
                text = "Clear",
                tooltip = "Remove the saved API key from this project."
            };
            clearButton.AddToClassList("convai-settings-inline-button");

            VisualElement badge = ConvaiSettingsUi.CreateStatusBadge(out _badgeDot, out _badgeText);
            VisualElement actionRow = ConvaiSettingsUi.CreateRow(_validateButton, clearButton, badge);
            actionRow.AddToClassList("convai-settings-action-row");
            _apiKeyGroup.Add(actionRow);
            Body.Add(_apiKeyGroup);

            _authTokenGroup = new VisualElement();
            _authTokenGroup.AddToClassList("convai-settings-subgroup");
            _authTokenGroup.Add(ConvaiSettingsUi.CreateHelpBox(
                "The endpoint must return a short-lived Convai auth token. Keep the account API key only on your backend. " +
                "For player-specific authentication, register an IConvaiAuthTokenProvider before the first connection.",
                HelpBoxMessageType.Info));
            _authTokenGroup.Add(ConvaiSettingsUi.CreateHelpBox(
                "Your saved API key remains available to Editor tools, but both stored key fields are cleared while an Auth Token player build is produced and restored afterwards.",
                HelpBoxMessageType.Info));

            var endpointField = new PropertyField(_authTokenEndpointUrlProperty, "Token Endpoint URL")
            {
                tooltip = "HTTPS URL on your backend that returns a short-lived Convai auth token. HTTP is allowed only for loopback development."
            };
            endpointField.AddToClassList("convai-settings-field");
            _authTokenGroup.Add(endpointField);

            var methodField = new PropertyField(_authTokenHttpMethodProperty, "HTTP Method")
            {
                tooltip = "HTTP method used when requesting a token from your backend."
            };
            methodField.AddToClassList("convai-settings-field");
            _authTokenGroup.Add(methodField);

            var responseField = new PropertyField(_authTokenResponseFieldProperty, "Token Response Field")
            {
                tooltip = "JSON response field containing the token. Defaults to apiAuthToken."
            };
            responseField.AddToClassList("convai-settings-field");
            _authTokenGroup.Add(responseField);

            var headersField = new PropertyField(_authTokenHeadersProperty, "Request Headers")
            {
                tooltip = "Optional static headers sent to the token endpoint. Use a code provider for per-player session headers."
            };
            headersField.AddToClassList("convai-settings-field");
            _authTokenGroup.Add(headersField);
            Body.Add(_authTokenGroup);

            var environmentField = new PropertyField(_environmentProperty, "Environment")
            {
                tooltip = "Convai service environment. Keep Production unless directed otherwise."
            };
            environmentField.AddToClassList("convai-settings-field");
            Body.Add(environmentField);

            _customGroup = new VisualElement();
            _customGroup.AddToClassList("convai-settings-subgroup");
            _customGroup.Add(ConvaiSettingsUi.CreateHelpBox(
                "Custom endpoints are active for this project. Keep this only for staging, enterprise, or support-directed setups.",
                HelpBoxMessageType.Info));
            var serverUrlField = new PropertyField(_serverUrlProperty, "Core Server URL")
            {
                tooltip = "Realtime server used for room connect requests."
            };
            serverUrlField.AddToClassList("convai-settings-field");
            _customGroup.Add(serverUrlField);
            _customGroup.Add(ConvaiSettingsUi.CreateField(context.Settings, "_customRestBaseUrl",
                "REST Base URL", "REST API base URL. Empty uses the production URL."));
            Body.Add(_customGroup);

            this.TrackPropertyValue(_authModeProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_authTokenEndpointUrlProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_authTokenHttpMethodProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_authTokenResponseFieldProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_authTokenHeadersProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_environmentProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_serverUrlProperty, _ => OnStoredCredentialConfigurationChanged());
            this.TrackPropertyValue(_customRestBaseUrlProperty, _ => OnStoredCredentialConfigurationChanged());
            Context.CredentialsChanged += RefreshFromSettings;
            RefreshModeGroups();
            RefreshEnvironmentGroup();
        }

        public override void Activate()
        {
            if (!HasValidSettings) return;

            RefreshFromSettings();
        }

        public override void Deactivate() => Context.Validation.CancelPending();

        protected override bool ConfirmReset() => EditorUtility.DisplayDialog(
            "Reset Credentials",
            "Clear the saved API key and auth-token configuration, then restore API Key mode and Production endpoint defaults?",
            "Reset",
            "Cancel");

        protected override void ResetToDefaults()
        {
            Context.Validation.CancelPending();
            _validateButton.SetEnabled(true);
            _apiKeyField.SetValueWithoutNotify(string.Empty);
            Context.Settings.FindProperty("_apiKeyObfuscated").stringValue = string.Empty;
            Context.Settings.FindProperty("_apiKey").stringValue = string.Empty;
            _authModeProperty.enumValueIndex = (int)ConvaiAuthMode.ApiKey;
            _authTokenEndpointUrlProperty.stringValue = string.Empty;
            _authTokenHttpMethodProperty.enumValueIndex = 0;
            _authTokenResponseFieldProperty.stringValue = "apiAuthToken";
            _authTokenHeadersProperty.ClearArray();
            _environmentProperty.enumValueIndex = (int)ConvaiApiEnvironment.Production;
            _serverUrlProperty.stringValue = ConvaiSettings.DefaultCoreServerUrl;
            _customRestBaseUrlProperty.stringValue = string.Empty;
        }

        protected override void OnResetApplied()
        {
            Context.NotifyCredentialsChanged();
        }

        private ConvaiApiEnvironment CurrentEnvironment =>
            (ConvaiApiEnvironment)_environmentProperty.enumValueIndex;

        private ConvaiAuthMode CurrentAuthMode =>
            (ConvaiAuthMode)_authModeProperty.enumValueIndex;

        private string CurrentCustomRestBaseUrl => _customRestBaseUrlProperty.stringValue;

        private void RefreshFromSettings()
        {
            if (!HasValidSettings) return;

            Context.Settings.Update();
            var settings = (ConvaiSettings)Context.Settings.targetObject;
            _apiKeyField.SetValueWithoutNotify(settings.ApiKey);
            RefreshModeGroups();
            RefreshEnvironmentGroup();
            RefreshBadgeForCurrentInput();
        }

        private void OnApiKeyInputChanged()
        {
            CancelValidationForEditedCredentials();
            RefreshBadgeForCurrentInput();
        }

        private void OnStoredCredentialConfigurationChanged()
        {
            CancelValidationForEditedCredentials();
            RefreshModeGroups();
            RefreshEnvironmentGroup();
            RefreshBadgeForCurrentInput();
            Context.RequestSave();
            Context.NotifyCredentialsChanged();
        }

        private void CancelValidationForEditedCredentials()
        {
            if (!Context.Validation.IsValidating) return;

            Context.Validation.CancelPending();
            _validateButton.SetEnabled(true);
        }

        private void RefreshModeGroups()
        {
            if (!HasValidSettings) return;

            bool usesAuthToken = CurrentAuthMode == ConvaiAuthMode.AuthToken;
            _apiKeyGroup.style.display = usesAuthToken ? DisplayStyle.None : DisplayStyle.Flex;
            _authTokenGroup.style.display = usesAuthToken ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshEnvironmentGroup()
        {
            if (!HasValidSettings) return;

            bool isCustom = CurrentEnvironment == ConvaiApiEnvironment.Custom;
            _customGroup.style.display = isCustom ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshBadgeForCurrentInput()
        {
            if (!HasValidSettings) return;

            string key = _apiKeyField.value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Warning);
                _badgeText.text = "No API key configured";
                return;
            }

            string customBase = CurrentEnvironment == ConvaiApiEnvironment.Custom
                ? CurrentCustomRestBaseUrl
                : string.Empty;
            if (ApiKeyValidationService.TryGetCachedResult(key, CurrentEnvironment, customBase,
                    out ApiKeyValidationResult cached))
            {
                if (cached.IsValid)
                {
                    ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Ok);
                    _badgeText.text = "Key valid";
                }
                else
                {
                    ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Error);
                    _badgeText.text = string.IsNullOrEmpty(cached.Message) ? "Key invalid" : cached.Message;
                }

                return;
            }

            ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Neutral);
            _badgeText.text = "Not validated";
        }

        private void OnValidateClicked()
        {
            if (!HasValidSettings || Context.Validation.IsValidating) return;

            string key = _apiKeyField.value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Warning);
                _badgeText.text = "Enter an API key first";
                return;
            }

            ConvaiApiEnvironment environment = CurrentEnvironment;
            string customBase = environment == ConvaiApiEnvironment.Custom ? CurrentCustomRestBaseUrl : string.Empty;

            _validateButton.SetEnabled(false);
            ConvaiSettingsUi.SetBadgeState(_badgeDot, ConvaiSettingsBadgeState.Pending);
            _badgeText.text = "Validating…";

            Context.Validation.Validate(key, environment, customBase, result =>
            {
                _validateButton.SetEnabled(true);
                if (!HasValidSettings) return;

                if (result.IsValid)
                {
                    var settings = (ConvaiSettings)Context.Settings.targetObject;
                    settings.SetApiKey(key);
                    EditorUtility.SetDirty(settings);
                    Context.Settings.Update();
                    Context.RequestSave();
                    Context.NotifyCredentialsChanged();
                }

                DisplayValidationResult(result);
            });
        }

        private void DisplayValidationResult(ApiKeyValidationResult result)
        {
            ConvaiSettingsUi.SetBadgeState(
                _badgeDot,
                result.IsValid ? ConvaiSettingsBadgeState.Ok : ConvaiSettingsBadgeState.Error);
            _badgeText.text = result.IsValid
                ? "Key valid"
                : string.IsNullOrEmpty(result.Message) ? "Key invalid" : result.Message;
        }

        private void OnClearClicked()
        {
            if (!HasValidSettings) return;

            Context.Validation.CancelPending();
            _validateButton.SetEnabled(true);
            _apiKeyField.SetValueWithoutNotify(string.Empty);

            var settings = (ConvaiSettings)Context.Settings.targetObject;
            settings.SetApiKey(string.Empty);
            EditorUtility.SetDirty(settings);
            Context.Settings.Update();
            Context.RequestSave();
            Context.NotifyCredentialsChanged();
            RefreshBadgeForCurrentInput();
        }
    }
}
