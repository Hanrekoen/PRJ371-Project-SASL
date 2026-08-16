using Convai.Editor.ConfigurationWindow.Services;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Vision;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiRoomManager" />: how the player talks, how the scene
    ///     connects, a rolling scene setup health report, and three collapsed-by-default sections
    ///     holding the room source, the advanced room control and the runtime readout.
    /// </summary>
    [CustomEditor(typeof(ConvaiRoomManager))]
    internal sealed class ConvaiRoomManagerEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Convai Room Manager";

        private const string PurposeText =
            "Owns the conversation room for this scene — connection, turn-taking and reconnect behavior.";

        private const string SectionConfigurationId = "Configuration";
        private const string SectionRoomDefaultsId = "RoomDefaults";
        private const string SectionRuntimeId = "Runtime";
        private const string SectionConversationId = "Conversation";

        /// <remarks>
        ///     Kept as "Scene" so a project that already collapsed or expanded this section keeps its
        ///     choice across the rename to Connection. The id is storage, not copy.
        /// </remarks>
        private const string SectionConnectionId = "Scene";

        private const string SectionValidationId = "Validation";

        private static readonly GUIContent ConversationSection = new("Conversation");
        private static readonly GUIContent ConnectionSection = new("Connection");
        private static readonly GUIContent ValidationSection = new("Validation");
        private static readonly GUIContent RoomSourceSection = new("Room Source");
        private static readonly GUIContent RoomControlSection = new("Advanced Room Control");
        private static readonly GUIContent RuntimeSection = new("Runtime");

        private static readonly GUIContent PushToTalkChip = new("Push To Talk");
        private static readonly GUIContent HandsFreeChip = new("Hands Free");

        private static readonly GUIContent AddVisionComponentsButton = new("Add Missing Components");

        private static readonly GUIContent RunValidationButton = new("Run Full Validation");
        private static readonly GUIContent OpenProjectSettingsButton = new("Open Project Settings");
        private static readonly GUIContent CopyToProjectSettingsButton = new("Copy To Project Settings");
        private static readonly GUIContent ClearLegacyOverrideButton = new("Clear Legacy Override");
        private static readonly GUIContent OpenSdkSettingsButton = new("Open Convai SDK Project Settings");

        private SerializedProperty _autoMicStartDelaySecondsProp;
        private SetupHealthReport _cachedSetupHealthReport;
        private SerializedProperty _configurationSourceInitializedProp;
        private SerializedProperty _configurationSourceProp;
        private SerializedProperty _connectionTypeProp;
        private SerializedProperty _connectOnStartProp;
        private SerializedProperty _coreServerBaseUrlProp;
        private SerializedProperty _debugProp;
        private SerializedProperty _maxReconnectAttemptsProp;
        private ConvaiEditorRefreshTimer _validationTimer;
        private SerializedProperty _resumePolicyProp;
        private SerializedProperty _roomConfigAssetProp;
        private SerializedObject _roomConfigSerializedObject;
        private ConvaiRoomManager _roomManager;
        private SerializedProperty _roomPushToTalkKeyProp;
        private SerializedProperty _roomRejoinTtlSecondsProp;
        private SerializedProperty _serverEndpointProp;
        private SerializedProperty _spawnAgentOnRejoinProp;
        private SerializedProperty _startWaitTimeoutMsProp;
        private SerializedProperty _turnTakingOptionsProp;
        private SerializedProperty _userVadSettingsProp;
        private SerializedProperty _visionContextModeProp;
        private SerializedProperty _visionInputSettingsProp;
        private SerializedProperty _visionRespondModesProp;

        protected override string Title => TitleText;
        protected override string Purpose => PurposeText;

        protected override GUIContent StatusChip =>
            _roomManager != null &&
            _roomManager.EffectiveTurnTakingOptions.Mode == ConversationInputMode.PushToTalk
                ? PushToTalkChip
                : HandsFreeChip;

        protected override Color StatusChipTint => Theme.StatusInfo;

        private bool IsAssetModeSelected =>
            _configurationSourceProp != null &&
            (ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex == ConvaiConfigSourceMode.Asset;

        private bool HasLegacyCoreServerOverride =>
            _coreServerBaseUrlProp != null && !string.IsNullOrWhiteSpace(_coreServerBaseUrlProp.stringValue);

        protected override void OnEnable()
        {
            base.OnEnable();

            _roomManager = (ConvaiRoomManager)target;

            _configurationSourceProp = serializedObject.FindProperty("_configurationSource");
            _configurationSourceInitializedProp = serializedObject.FindProperty("_configurationSourceInitialized");
            _roomConfigAssetProp = serializedObject.FindProperty("_roomConfigAsset");
            _connectionTypeProp = serializedObject.FindProperty("_connectionType");
            _coreServerBaseUrlProp = serializedObject.FindProperty("<CoreServerBaseURL>k__BackingField");
            _serverEndpointProp = serializedObject.FindProperty("<ServerEndpoint>k__BackingField");
            _connectOnStartProp = serializedObject.FindProperty("<ConnectOnStart>k__BackingField");
            _turnTakingOptionsProp = serializedObject.FindProperty("_turnTakingOptions");
            _userVadSettingsProp = serializedObject.FindProperty("_userVadSettings");
            _visionContextModeProp = serializedObject.FindProperty("_visionContextMode");
            _visionInputSettingsProp = serializedObject.FindProperty("_visionInputSettings");
            _visionRespondModesProp = serializedObject.FindProperty("_visionRespondModes");
            _roomPushToTalkKeyProp = serializedObject.FindProperty("_pushToTalkKey");
            _debugProp = serializedObject.FindProperty("<Debug>k__BackingField");
            _roomRejoinTtlSecondsProp = serializedObject.FindProperty("_roomRejoinTtlSeconds");
            _resumePolicyProp = serializedObject.FindProperty("_resumePolicy");
            _maxReconnectAttemptsProp = serializedObject.FindProperty("_maxReconnectAttempts");
            _spawnAgentOnRejoinProp = serializedObject.FindProperty("_spawnAgentOnRejoin");
            _startWaitTimeoutMsProp = serializedObject.FindProperty("_startWaitTimeoutMs");
            _autoMicStartDelaySecondsProp = serializedObject.FindProperty("_autoMicStartDelaySeconds");

            EnsureConfigurationSourceMigration();
        }

        protected override void OnBeforeInspectorGUI() => EnsureConfigurationSourceMigration();

        protected override void DrawBody()
        {
            // Without the source/connection properties there is no bespoke page left to draw, so fall
            // through to the attribute-driven renderer rather than showing an empty inspector.
            if (!HasRequiredProperties())
            {
                DrawGeneratedSections();
                return;
            }

            DrawConversationSection();
            DrawConnectionSection();
            DrawValidationSection();

            // The three advanced sections sit flat alongside the everyday ones rather than nested
            // inside an "Advanced" wrapper: they start collapsed, so they stay out of the way, and a
            // flat stack of cards is what every other Convai inspector looks like. Nesting them put
            // a card inside a card inside a panel, which read as a rendering fault.
            DrawRoomSourceSection();
            DrawRoomControlSection();
            DrawRuntimeSection();
        }

        private bool HasRequiredProperties() =>
            _configurationSourceProp != null &&
            _roomConfigAssetProp != null &&
            _connectionTypeProp != null &&
            _connectOnStartProp != null;

        private void EnsureConfigurationSourceMigration()
        {
            if (_configurationSourceInitializedProp == null || _roomConfigAssetProp == null ||
                _configurationSourceProp == null)
                return;

            if (_configurationSourceInitializedProp.boolValue) return;

            _configurationSourceProp.enumValueIndex =
                _roomConfigAssetProp.objectReferenceValue != null
                    ? (int)ConvaiConfigSourceMode.Asset
                    : (int)ConvaiConfigSourceMode.Inline;
            _configurationSourceInitializedProp.boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
        }

        private void DrawConversationSection()
        {
            if (!DrawSection(SectionConversationId, ConversationSection, Glyphs.Content)) return;
            DrawSectionBody(() =>
            {
                SerializedProperty turnTakingOptionsProp =
                    GetConversationTurnTakingOptionsProperty(out bool readOnlyAssetValues);
                if (turnTakingOptionsProp == null)
                {
                    WarningBox(
                        "Turn-taking unavailable",
                        IsAssetModeSelected
                            ? "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults."
                            : "This Room Manager's saved data does not contain turn-taking settings. "
                              + "Remove the Convai Room Manager component and add it again to rebuild them.");
                    return;
                }

                if (readOnlyAssetValues && _roomConfigAssetProp.objectReferenceValue != null)
                    GUILayout.Label(
                        $"Using Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}",
                        Theme.MutedWrapped);

                SerializedProperty modeProp = turnTakingOptionsProp.FindPropertyRelative("<Mode>k__BackingField");
                SerializedProperty pushToTalkPolicyProp =
                    turnTakingOptionsProp.FindPropertyRelative("<PushToTalkPolicy>k__BackingField");
                if (modeProp == null || pushToTalkPolicyProp == null)
                    return;

                using (new EditorGUI.DisabledScope(readOnlyAssetValues))
                {
                    EditorGUILayout.PropertyField(modeProp, ConvaiInspectorContent.HowThePlayerTalks);
                }

                if ((ConversationInputMode)modeProp.enumValueIndex != ConversationInputMode.PushToTalk)
                    return;

                if (_roomPushToTalkKeyProp != null)
                    EditorGUILayout.PropertyField(_roomPushToTalkKeyProp, ConvaiInspectorContent.PushToTalkKey);

                using (new EditorGUI.DisabledScope(readOnlyAssetValues))
                {
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative("<InterruptBotOnPress>k__BackingField"),
                        ConvaiInspectorContent.InterruptCharacterWhenPressed);
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative(
                            "<RequireTurnCompletionBeforeNextPress>k__BackingField"),
                        ConvaiInspectorContent.WaitForCharacterToFinishBeforeTalkingAgain);
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative("<TurnCompletionTimeoutMs>k__BackingField"),
                        ConvaiInspectorContent.FallbackWaitTimeMs);
                }
            });
        }

        /// <summary>
        ///     The two decisions a scene makes about connecting, drawn together at the top.
        /// </summary>
        /// <remarks>
        ///     Connection Type decides whether the character can see the scene at all, which makes it an
        ///     ordinary first-day setup choice rather than advanced tuning — so it sits here, in the
        ///     open, and not inside the collapsed advanced section where a reader would have to already
        ///     know it existed. Advanced Room Control deliberately does not also offer it: a setting
        ///     editable in two places is a setting a user can disagree with themselves about.
        /// </remarks>
        private void DrawConnectionSection()
        {
            if (!DrawSection(SectionConnectionId, ConnectionSection, Glyphs.Routing)) return;
            DrawSectionBody(() =>
            {
                SerializedProperty connectionTypeProp = GetConnectionTypeProperty(out bool readOnlyAssetValue);
                SerializedProperty connectOnStartProp = GetConnectOnStartProperty(out _);
                if (connectionTypeProp == null && connectOnStartProp == null)
                {
                    WarningBox(
                        "Connection settings unavailable",
                        IsAssetModeSelected
                            ? "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults."
                            : "This Room Manager's saved data does not contain connection settings. "
                              + "Remove the Convai Room Manager component and add it again to rebuild them.");
                    return;
                }

                if (readOnlyAssetValue && _roomConfigAssetProp.objectReferenceValue != null)
                    GUILayout.Label(
                        $"These values come from Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}",
                        Theme.MutedWrapped);

                using (new EditorGUI.DisabledScope(readOnlyAssetValue))
                {
                    if (connectionTypeProp != null)
                        EditorGUILayout.PropertyField(connectionTypeProp, ConvaiInspectorContent.ConnectionType);

                    if (connectOnStartProp != null)
                        EditorGUILayout.PropertyField(connectOnStartProp, ConvaiInspectorContent.StartsConnected);
                }

                DrawVideoRequirementsHint(connectionTypeProp);
            });
        }

        /// <summary>
        ///     Video is the only connection type with extra scene requirements, so what it needs is named
        ///     — and offered — where the choice is made, instead of only in Validation further down.
        /// </summary>
        private void DrawVideoRequirementsHint(SerializedProperty connectionTypeProp)
        {
            if (connectionTypeProp == null ||
                (ConvaiConnectionType)connectionTypeProp.enumValueIndex != ConvaiConnectionType.Video)
                return;

            (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
            if (hasPublisher && hasFrameSource)
                return;

            WarningBox(
                "Video needs a camera feed",
                $"A Video connection sends camera frames to the character, which needs " +
                $"{GetMissingVideoRequirementsMessage(hasPublisher, hasFrameSource)} on this GameObject or a " +
                "child. Until then the character connects but sees nothing.",
                AddVisionComponentsButton.text,
                () => AddMissingVisionComponents(hasPublisher, hasFrameSource));
        }

        /// <summary>
        ///     Adds whichever of the two vision components is missing, on the Room Manager's own
        ///     GameObject, in one undoable step.
        /// </summary>
        /// <remarks>
        ///     <see cref="CameraVisionFrameSource" /> is the frame source to default to: it renders from a
        ///     scene camera, so it needs no device permission and no XR package, and a project that wants
        ///     the webcam or a Quest passthrough feed can swap the component afterwards.
        /// </remarks>
        private void AddMissingVisionComponents(bool hasPublisher, bool hasFrameSource)
        {
            if (_roomManager == null)
                return;

            GameObject host = _roomManager.gameObject;

            if (!hasPublisher)
                Undo.AddComponent<ConvaiVisionPublisher>(host);

            if (!hasFrameSource)
                Undo.AddComponent<CameraVisionFrameSource>(host);

            InvalidateValidationCache(true);
            EditorUtility.SetDirty(host);
        }

        private void DrawValidationSection()
        {
            if (!DrawSection(SectionValidationId, ValidationSection, Glyphs.Validation, accent: Theme.StatusWarn))
                return;
            DrawSectionBody(() =>
            {
                SetupHealthReport report = GetSetupHealthReport();
                if (report.HasBlockingIssues)
                    ErrorBox(
                        "Setup incomplete",
                        "Some required setup is still missing. Use the checks below to fix the scene.");
                else if (report.HasWarnings || HasRoomSpecificIssues())
                    WarningBox(
                        "Almost ready",
                        "Setup is mostly ready, but a few non-blocking issues should be reviewed.");
                else
                    InfoBox("Scene setup", "Scene setup looks healthy.");

                foreach (SetupHealthCheckResult result in report.Results)
                {
                    EditorGUILayout.LabelField($"{GetStatusIcon(result.Status)} {result.Title}", ConvaiEditorStyles.SectionTitle);
                    GUILayout.Label(result.Message, Theme.MutedWrapped);
                    GUILayout.Space(3f);
                }

                DrawRoomSpecificValidationMessages();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(RunValidationButton, Styles.MiniButton, GUILayout.Width(160f)))
                {
                    InvalidateValidationCache(true);
                    ConvaiSetupWizard.ValidateSceneSetup();
                }

                if (GUILayout.Button(OpenProjectSettingsButton, Styles.MiniButton, GUILayout.Width(160f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawRoomSourceSection()
        {
            if (!DrawSection(SectionConfigurationId, RoomSourceSection, Glyphs.Profile, defaultExpanded: false))
                return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_configurationSourceProp, ConvaiInspectorContent.RoomSetupSource);
                if (IsAssetModeSelected)
                    EditorGUILayout.PropertyField(_roomConfigAssetProp, ConvaiInspectorContent.RoomConfigAsset);

                GUILayout.Label(GetRoomSourceStatusText(), Theme.MutedWrapped);
                if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                    WarningBox(
                        "No profile assigned",
                        "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults.");
            });
        }

        private void DrawRoomControlSection()
        {
            if (!DrawSection(SectionRoomDefaultsId, RoomControlSection, Glyphs.Section, defaultExpanded: false))
                return;
            DrawSectionBody(() =>
            {
                if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                {
                    WarningBox(
                        "No profile assigned",
                        "Assign a Room Manager Profile asset to review advanced room control while Room Setup " +
                        "Source is set to Room Manager Profile Asset.");
                    return;
                }

                if (IsAssetModeSelected)
                    GUILayout.Label(
                        $"These values come from Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}. " +
                        "Edit the asset to change them.",
                        Theme.MutedWrapped);

                ConvaiRoomControlInspectorView.Draw(
                    BuildRoomControlProperties(out bool readOnlyAssetValues),
                    readOnlyAssetValues);
            });
        }

        /// <summary>
        ///     Collects the properties Advanced Room Control draws, from whichever object owns them.
        /// </summary>
        /// <remarks>
        ///     In asset mode the values live on the Room Manager Profile and are shown read-only, so the
        ///     component's inspector reports the room it will actually join rather than the fields it
        ///     happens to serialize.
        /// </remarks>
        private ConvaiRoomControlProperties BuildRoomControlProperties(out bool readOnlyAssetValues)
        {
            readOnlyAssetValues = false;

            if (!IsAssetModeSelected)
                return new ConvaiRoomControlProperties
                {
                    ConnectionTypeForDisplay = _connectionTypeProp,
                    ServerEndpoint = _serverEndpointProp,
                    TurnTakingOptions = _turnTakingOptionsProp,
                    UserVadSettings = _userVadSettingsProp,
                    VisionContextMode = _visionContextModeProp,
                    VisionInputSettings = _visionInputSettingsProp,
                    VisionRespondModes = _visionRespondModesProp,
                    RoomRejoinTtlSeconds = _roomRejoinTtlSecondsProp,
                    ResumePolicy = _resumePolicyProp,
                    MaxReconnectAttempts = _maxReconnectAttemptsProp,
                    SpawnAgentOnRejoin = _spawnAgentOnRejoinProp,
                    StartWaitTimeoutMs = _startWaitTimeoutMsProp,
                    AutoMicStartDelaySeconds = _autoMicStartDelaySecondsProp
                };

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject asset = GetRoomConfigSerializedObject(roomConfig);
            if (asset == null)
                return null;

            readOnlyAssetValues = true;
            return new ConvaiRoomControlProperties
            {
                ConnectionTypeForDisplay = asset.FindProperty("_connectionType"),
                VideoTrackName = asset.FindProperty("_videoTrackName"),
                ServerEndpoint = asset.FindProperty("_serverEndpoint"),
                TurnTakingOptions = asset.FindProperty("_turnTakingOptions"),
                UserVadSettings = asset.FindProperty("_userVadSettings"),
                VisionContextMode = asset.FindProperty("_visionContextMode"),
                VisionInputSettings = asset.FindProperty("_visionInputSettings"),
                VisionRespondModes = asset.FindProperty("_visionRespondModes"),
                RoomRejoinTtlSeconds = asset.FindProperty("_roomRejoinTtlSeconds"),
                ResumePolicy = asset.FindProperty("_resumePolicy"),
                MaxReconnectAttempts = asset.FindProperty("_maxReconnectAttempts"),
                SpawnAgentOnRejoin = asset.FindProperty("_spawnAgentOnRejoin"),
                StartWaitTimeoutMs = asset.FindProperty("_startWaitTimeoutMs"),
                AutoMicStartDelaySeconds = asset.FindProperty("_autoMicStartDelaySeconds")
            };
        }

        private void DrawRuntimeSection()
        {
            if (!DrawSection(
                    SectionRuntimeId, RuntimeSection, Glyphs.Live, defaultExpanded: false, accent: Theme.StatusInfo))
                return;
            DrawSectionBody(() =>
            {
                string projectServerUrl =
                    ConvaiSettings.Instance != null ? ConvaiSettings.Instance.ServerUrl : string.Empty;
                string legacyOverride = _coreServerBaseUrlProp?.stringValue ?? string.Empty;
                bool hasLegacyOverride = !string.IsNullOrWhiteSpace(legacyOverride);
                string effectiveBaseUrl = hasLegacyOverride ? legacyOverride.Trim() : projectServerUrl;

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Session State", _roomManager.CurrentState.ToString());
                    EditorGUILayout.Toggle("Connected", _roomManager.IsConnected);
                    EditorGUILayout.TextField("Current Room",
                        string.IsNullOrWhiteSpace(_roomManager.CurrentRoomName)
                            ? "Not connected"
                            : _roomManager.CurrentRoomName);
                    EditorGUILayout.TextField("Session ID",
                        string.IsNullOrWhiteSpace(_roomManager.CurrentSessionId)
                            ? "Not connected"
                            : _roomManager.CurrentSessionId);
                    EditorGUILayout.TextField(
                        "Core Server URL Source",
                        hasLegacyOverride ? "Legacy Override (this component)" : "Project Settings");
                    EditorGUILayout.TextField("Effective Core Server Base URL", effectiveBaseUrl);
                    EditorGUILayout.TextField("Dynamic Vision Context",
                        _roomManager.EffectiveVisionContextEnabled
                            ? $"{_roomManager.EffectiveVisionContextMode} (enabled)"
                            : $"{_roomManager.EffectiveVisionContextMode} (disabled)");
                    EditorGUILayout.TextField("Effective Connection Type",
                        _roomManager.EffectiveConnectionType.ToString());
                    if (!string.IsNullOrWhiteSpace(_roomManager.RoomControllerTypeName))
                        EditorGUILayout.TextField("Room Controller", _roomManager.RoomControllerTypeName);
                    if (!string.IsNullOrWhiteSpace(_roomManager.TransportAccessorTypeName))
                        EditorGUILayout.TextField("Transport", _roomManager.TransportAccessorTypeName);
                }

                if (hasLegacyOverride)
                {
                    WarningBox(
                        "Legacy server override",
                        "This component still has a legacy Core Server Base URL override. Move it to Project " +
                        "Settings, then clear the per-scene value.");

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(CopyToProjectSettingsButton, Styles.MiniButton, GUILayout.Width(170f)))
                        CopyLegacyCoreServerOverrideToProjectSettings();
                    if (GUILayout.Button(ClearLegacyOverrideButton, Styles.MiniButton, GUILayout.Width(150f)))
                        ClearLegacyCoreServerOverride();
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button(OpenSdkSettingsButton, Styles.MiniButton, GUILayout.Width(220f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");

                EditorGUILayout.PropertyField(_debugProp, ConvaiInspectorContent.Debug);
            });
        }

        private string GetRoomSourceStatusText()
        {
            if (!IsAssetModeSelected)
                return "Using scene defaults.";

            return _roomConfigAssetProp.objectReferenceValue != null
                ? $"Using Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}"
                : "Room Manager Profile not assigned.";
        }

        private SerializedProperty GetConversationTurnTakingOptionsProperty(out bool readOnlyAssetValues)
        {
            readOnlyAssetValues = false;
            if (!IsAssetModeSelected)
                return _turnTakingOptionsProp;

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject roomConfigSerializedObject = GetRoomConfigSerializedObject(roomConfig);
            if (roomConfigSerializedObject == null)
                return null;

            readOnlyAssetValues = true;
            return roomConfigSerializedObject.FindProperty("_turnTakingOptions");
        }

        private SerializedProperty GetConnectionTypeProperty(out bool readOnlyAssetValue)
        {
            readOnlyAssetValue = false;
            if (!IsAssetModeSelected)
                return _connectionTypeProp;

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject roomConfigSerializedObject = GetRoomConfigSerializedObject(roomConfig);
            if (roomConfigSerializedObject == null)
                return null;

            readOnlyAssetValue = true;
            return roomConfigSerializedObject.FindProperty("_connectionType");
        }

        private SerializedProperty GetConnectOnStartProperty(out bool readOnlyAssetValue)
        {
            readOnlyAssetValue = false;
            if (!IsAssetModeSelected)
                return _connectOnStartProp;

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject roomConfigSerializedObject = GetRoomConfigSerializedObject(roomConfig);
            if (roomConfigSerializedObject == null)
                return null;

            readOnlyAssetValue = true;
            return roomConfigSerializedObject.FindProperty("_connectOnStart");
        }

        private SerializedObject GetRoomConfigSerializedObject(ConvaiRoomManagerProfile roomConfig)
        {
            if (roomConfig == null)
            {
                _roomConfigSerializedObject = null;
                return null;
            }

            if (_roomConfigSerializedObject == null || _roomConfigSerializedObject.targetObject != roomConfig)
                _roomConfigSerializedObject = new SerializedObject(roomConfig);

            _roomConfigSerializedObject.UpdateIfRequiredOrScript();
            return _roomConfigSerializedObject;
        }

        private bool HasRoomSpecificIssues()
        {
            if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                return true;

            if (HasLegacyCoreServerOverride)
                return true;

            if (_roomManager != null && _roomManager.EffectiveConnectionType == ConvaiConnectionType.Video)
            {
                (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
                if (!hasPublisher || !hasFrameSource)
                    return true;
            }

            return false;
        }

        private void DrawRoomSpecificValidationMessages()
        {
            bool anyRoomSpecificIssues = false;

            if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
            {
                EditorGUILayout.LabelField($"{Glyphs.Status.Fail} Room Manager Profile", ConvaiEditorStyles.SectionTitle);
                GUILayout.Label(
                    "Room Manager Profile Asset is required while Room Setup Source is set to Room Manager " +
                    "Profile Asset.",
                    Theme.MutedWrapped);
                GUILayout.Space(3f);
                anyRoomSpecificIssues = true;
            }

            if (HasLegacyCoreServerOverride)
            {
                EditorGUILayout.LabelField($"{Glyphs.Status.Warn} Legacy Server Override", ConvaiEditorStyles.SectionTitle);
                GUILayout.Label(
                    "This component still has a legacy Core Server Base URL override. Move it to Project " +
                    "Settings, then clear the per-scene value.",
                    Theme.MutedWrapped);
                GUILayout.Space(3f);
                anyRoomSpecificIssues = true;
            }

            if (_roomManager != null && _roomManager.EffectiveConnectionType == ConvaiConnectionType.Video)
            {
                (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
                if (!hasPublisher || !hasFrameSource)
                {
                    EditorGUILayout.LabelField($"{Glyphs.Status.Warn} Dynamic Vision Requirements", ConvaiEditorStyles.SectionTitle);
                    GUILayout.Label(
                        "Dynamic vision context is missing required components: " +
                        $"{GetMissingVideoRequirementsMessage(hasPublisher, hasFrameSource)}.",
                        Theme.MutedWrapped);
                    GUILayout.Space(3f);
                    anyRoomSpecificIssues = true;
                }
            }

            if (!anyRoomSpecificIssues)
                GUILayout.Label("No room-specific issues detected.", Theme.MutedWrapped);
        }

        private static string GetStatusIcon(SetupHealthStatus status) =>
            status switch
            {
                SetupHealthStatus.Healthy => Glyphs.Status.Ok,
                SetupHealthStatus.Warning => Glyphs.Status.Warn,
                _ => Glyphs.Status.Fail
            };

        private SetupHealthReport GetSetupHealthReport()
        {
            if (_validationTimer.ShouldRefresh(_cachedSetupHealthReport != null))
                _cachedSetupHealthReport = SetupHealthService.BuildReport();

            return _cachedSetupHealthReport;
        }

        private void InvalidateValidationCache(bool forceImmediateRefresh = false)
        {
            _cachedSetupHealthReport = null;
            _validationTimer.Invalidate(forceImmediateRefresh);
        }

        private (bool hasPublisher, bool hasFrameSource) GetVisionComponentFlags()
        {
            bool hasPublisher = false;
            bool hasFrameSource = false;

            foreach (MonoBehaviour component in _roomManager.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!hasPublisher && component is IVisionPublisher) hasPublisher = true;
                if (!hasFrameSource && component is IVisionFrameSource) hasFrameSource = true;
                if (hasPublisher && hasFrameSource) break;
            }

            return (hasPublisher, hasFrameSource);
        }

        private static string GetMissingVideoRequirementsMessage(bool hasPublisher, bool hasFrameSource)
        {
            if (!hasPublisher && !hasFrameSource)
                return "ConvaiVisionPublisher and a vision frame source";
            if (!hasPublisher)
                return "ConvaiVisionPublisher";
            return "a vision frame source";
        }

        private void CopyLegacyCoreServerOverrideToProjectSettings()
        {
            if (!HasLegacyCoreServerOverride) return;

            var settings = ConvaiSettings.Instance;
            if (settings == null) return;

            Undo.RecordObject(settings, "Copy Convai Server URL To Project Settings");
            settings.SetServerUrl(_coreServerBaseUrlProp.stringValue.Trim());
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private void ClearLegacyCoreServerOverride()
        {
            if (!HasLegacyCoreServerOverride) return;

            Undo.RecordObject(_roomManager, "Clear Legacy Convai Server URL Override");
            _coreServerBaseUrlProp.stringValue = string.Empty;
            EditorUtility.SetDirty(_roomManager);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
        }
    }
}
