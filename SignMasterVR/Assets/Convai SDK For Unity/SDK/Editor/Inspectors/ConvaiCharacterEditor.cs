using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.Utilities;
using Convai.RestAPI;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiCharacter" />: where the character's setup comes from
    ///     (inline values or a Character Profile asset), its identity, audio and session behaviour, a
    ///     validation section, and a Play-mode debug readout.
    /// </summary>
    [CustomEditor(typeof(ConvaiCharacter))]
    internal sealed class ConvaiCharacterEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Convai Character";

        private const string PurposeText =
            "One conversational character in this scene — its identity, audio output and session behavior.";

        private const string SectionConfigurationId = "Configuration";
        private const string SectionIdentityId = "Identity";
        private const string SectionAudioId = "Audio";
        private const string SectionSessionId = "Session";
        private const string SectionValidationId = "Validation";
        private const string SectionDebugId = "Debug";

        private static readonly Regex GuidRegex = new(
            @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$",
            RegexOptions.Compiled);

        internal static readonly string[] PrimarySectionTitles =
        {
            "Configuration", "Identity", "Audio", "Session", "Validation"
        };

        private static readonly GUIContent ConfigurationSection = new("Configuration");
        private static readonly GUIContent IdentitySection = new("Identity");
        private static readonly GUIContent AudioSection = new("Audio");
        private static readonly GUIContent SessionSection = new("Session");
        private static readonly GUIContent ValidationSection = new("Validation");
        private static readonly GUIContent DebugSection = new("Debug");

        private static readonly GUIContent ProfileModeChip = new("Character Profile");
        private static readonly GUIContent InlineModeChip = new("Inline");

        private static readonly GUIContent SetupSourceLabel = new("Character Setup Source");
        private static readonly GUIContent ProfileAssetLabel = new("Character Profile Asset");
        private static readonly GUIContent CharacterIdLabel = new("Character ID");
        private static readonly GUIContent CharacterNameLabel = new("Character Name");
        private static readonly GUIContent NameTagColorLabel = new("Name Tag Color");
        private static readonly GUIContent RemoteAudioLabel = new("Remote Audio On Start");
        private static readonly GUIContent SessionResumeLabel = new("Session Resume");
        private static readonly GUIContent AutoConnectLabel = new("Auto Connect");
        private static readonly GUIContent OwnerIdLabel = new("Owner ID");
        private static readonly GUIContent ReadyTimeoutLabel = new("Ready Timeout (Seconds)");
        private static readonly GUIContent KeepDynamicInfoLabel = new("Keep Initial Dynamic Info In Context");
        private static readonly GUIContent DynamicInfoTextLabel = new("Initial Dynamic Info Text");

        private static readonly GUIContent SessionIdLabel = new(
            "Character Session ID",
            "Used as character_session_id when Session Resume is enabled. Leave empty to start fresh; " +
            "successful connects populate the current value.");

        private static readonly GUIContent FetchNameButton = new("Fetch Name");
        private static readonly GUIContent CopyIdButton = new("Copy ID");
        private static readonly GUIContent DashboardButton = new("Dashboard");
        private static readonly GUIContent AddAudioOutputButton = new("Add ConvaiAudioOutput");
        private static readonly GUIContent CopySessionIdButton = new("Copy Session ID");
        private static readonly GUIContent ClearSessionIdButton = new("Clear");
        private static readonly GUIContent ProjectSettingsButton = new("Project Settings");
        private static readonly GUIContent QuickstartButton = new("Quickstart");

        private SerializedProperty _autoConnectProp;
        private ConvaiCharacter _character;
        private SerializedProperty _characterConfigAssetProp;
        private SerializedProperty _characterIdProp;
        private SerializedProperty _characterNameProp;
        private SerializedProperty _characterReadyTimeoutProp;
        private SerializedProperty _characterSessionIdProp;
        private SerializedProperty _configurationSourceInitializedProp;
        private SerializedProperty _configurationSourceProp;
        private SerializedProperty _dynamicInfoKeepInContextProp;
        private SerializedProperty _dynamicInfoTextProp;
        private SerializedProperty _enableRemoteAudioProp;
        private SerializedProperty _enableSessionResumeProp;
        private bool _isFetchingName;
        private SerializedProperty _nameTagColorProp;
        private SerializedProperty _ownerIdProp;

        protected override string Title => TitleText;
        protected override string Purpose => PurposeText;

        protected override GUIContent StatusChip => IsAssetModeSelected ? ProfileModeChip : InlineModeChip;

        protected override Color StatusChipTint =>
            IsAssetModeSelected ? Theme.StatusInfo : Theme.StatusIdle;

        private bool UsesCharacterConfigAsset =>
            (ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex == ConvaiConfigSourceMode.Asset &&
            _characterConfigAssetProp.objectReferenceValue != null;

        private bool IsAssetModeSelected =>
            _configurationSourceProp != null &&
            (ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex == ConvaiConfigSourceMode.Asset;

        protected override void OnEnable()
        {
            base.OnEnable();

            _character = (ConvaiCharacter)target;

            _configurationSourceProp = serializedObject.FindProperty("_configurationSource");
            _configurationSourceInitializedProp = serializedObject.FindProperty("_configurationSourceInitialized");
            _characterConfigAssetProp = serializedObject.FindProperty("_characterConfigAsset");
            _characterIdProp = serializedObject.FindProperty("_characterId");
            _characterNameProp = serializedObject.FindProperty("_characterName");
            _nameTagColorProp = serializedObject.FindProperty("_nameTagColor");
            _autoConnectProp = serializedObject.FindProperty("_autoConnect");
            _enableRemoteAudioProp = serializedObject.FindProperty("_enableRemoteAudio");
            _enableSessionResumeProp = serializedObject.FindProperty("_enableSessionResume");
            _characterSessionIdProp = serializedObject.FindProperty("_characterSessionId");
            _ownerIdProp = serializedObject.FindProperty("_ownerId");
            _characterReadyTimeoutProp = serializedObject.FindProperty("_characterReadyTimeoutSeconds");
            _dynamicInfoTextProp = serializedObject.FindProperty("_initialDynamicInfoText");
            _dynamicInfoKeepInContextProp = serializedObject.FindProperty("_initialDynamicInfoKeepInContext");

            EnsureConfigurationSourceMigration();
        }

        protected override void OnBeforeInspectorGUI() => EnsureConfigurationSourceMigration();

        /// <summary>
        ///     The blocking problems — a malformed Character ID, or asset mode with no asset — are
        ///     drawn above the sections so they are seen before anything is tuned.
        /// </summary>
        protected override void DrawHeaderExtras()
        {
            if (!HasRequiredProperties())
                return;

            string validationMessage = ValidateCharacterId(_character.CharacterId);
            if (!string.IsNullOrEmpty(validationMessage))
                WarningBox("Character ID", validationMessage);

            if (IsAssetModeSelected && _characterConfigAssetProp.objectReferenceValue == null)
                ErrorBox(
                    "Character Profile missing",
                    "Character Setup Source is set to Character Profile Asset, but no Character Profile " +
                    "asset is assigned.");
        }

        protected override void DrawBody()
        {
            // Without the identity and mode properties there is no bespoke page left to draw, so fall
            // through to the attribute-driven renderer rather than showing an empty inspector.
            if (!HasRequiredProperties())
            {
                DrawGeneratedSections();
                return;
            }

            DrawConfigurationSection();
            DrawIdentitySection();
            DrawAudioSection();
            DrawSessionSection();
            DrawValidationSection();
        }

        protected override void DrawLiveSection()
        {
            if (!DrawSection(SectionDebugId, DebugSection, Glyphs.Live, false, accent: Theme.StatusInfo)) return;
            DrawSectionBody(() =>
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle("Injected", _character.IsInjected);
                    EditorGUILayout.EnumPopup("Session State", _character.SessionState);
                    EditorGUILayout.Toggle("Character Ready", _character.IsCharacterReady);
                    EditorGUILayout.Toggle("Speaking", _character.IsSpeaking);
                    EditorGUILayout.TextField("Current Emotion", _character.CurrentEmotion ?? string.Empty);
                    EditorGUILayout.IntField("Emotion Intensity", _character.CurrentEmotionIntensity);
                }
            });
        }

        private bool HasRequiredProperties() =>
            _configurationSourceProp != null &&
            _characterConfigAssetProp != null &&
            _characterIdProp != null &&
            _characterNameProp != null &&
            _enableRemoteAudioProp != null &&
            _enableSessionResumeProp != null &&
            _characterSessionIdProp != null;

        private void EnsureConfigurationSourceMigration()
        {
            if (_configurationSourceProp == null || _configurationSourceInitializedProp == null ||
                _characterConfigAssetProp == null)
                return;

            if (_configurationSourceInitializedProp.boolValue) return;

            _configurationSourceProp.enumValueIndex =
                _characterConfigAssetProp.objectReferenceValue != null
                    ? (int)ConvaiConfigSourceMode.Asset
                    : (int)ConvaiConfigSourceMode.Inline;
            _configurationSourceInitializedProp.boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
        }

        private void DrawConfigurationSection()
        {
            if (!DrawSection(SectionConfigurationId, ConfigurationSection, Glyphs.Profile)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_configurationSourceProp, SetupSourceLabel);

                if ((ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex != ConvaiConfigSourceMode.Asset)
                {
                    InfoBox(
                        "Inline setup",
                        "Inline values on this component are active. Switch to Asset when you want to reuse a " +
                        "Character Profile across scenes or prefabs.");
                    return;
                }

                EditorGUILayout.PropertyField(_characterConfigAssetProp, ProfileAssetLabel);

                if (_characterConfigAssetProp.objectReferenceValue == null)
                {
                    WarningBox(
                        "No profile assigned",
                        "Assign a Character Profile asset to make asset-backed values active, or switch " +
                        "Character Setup Source back to Inline.");
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(CharacterIdLabel, _character.CharacterId);
                    EditorGUILayout.TextField(CharacterNameLabel, _character.CharacterName);
                    EditorGUILayout.ColorField(NameTagColorLabel, _character.NameTagColor);
                    EditorGUILayout.Toggle(RemoteAudioLabel, _character.EnableRemoteAudioOnStart);
                    EditorGUILayout.Toggle(SessionResumeLabel, _character.EnableSessionResume);
                }

                InfoBox(
                    "Coming from the profile",
                    "Character identity and default audio/session behavior are coming from the assigned " +
                    "Character Profile asset.");
            });
        }

        private void DrawIdentitySection()
        {
            if (!DrawSection(SectionIdentityId, IdentitySection, Glyphs.Identity)) return;
            DrawSectionBody(() =>
            {
                if (IsAssetModeSelected)
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(CharacterIdLabel, _character.CharacterId);
                        EditorGUILayout.TextField(CharacterNameLabel, _character.CharacterName);
                        EditorGUILayout.ColorField(NameTagColorLabel, _character.NameTagColor);
                    }
                else
                {
                    EditorGUILayout.PropertyField(_characterIdProp, CharacterIdLabel);
                    EditorGUILayout.PropertyField(_characterNameProp, CharacterNameLabel);
                    EditorGUILayout.PropertyField(_nameTagColorProp, NameTagColorLabel);
                }

                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();

                using (new EditorGUI.DisabledScope(IsAssetModeSelected))
                {
                    if (GUILayout.Button(FetchNameButton, Styles.MiniButton, GUILayout.Width(90f)))
                        FetchCharacterNameFromApi();
                }

                if (GUILayout.Button(CopyIdButton, Styles.MiniButton, GUILayout.Width(70f)))
                    GUIUtility.systemCopyBuffer = _character.CharacterId ?? string.Empty;

                if (GUILayout.Button(DashboardButton, Styles.MiniButton, GUILayout.Width(80f)))
                {
                    string url = string.IsNullOrEmpty(_character.CharacterId)
                        ? ConvaiEditorLinks.DashboardHomeUrl
                        : $"{ConvaiEditorLinks.CharacterDashboardBaseUrl}?id={_character.CharacterId}";
                    UnityEngine.Application.OpenURL(url);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawAudioSection()
        {
            if (!DrawSection(SectionAudioId, AudioSection, Glyphs.Content)) return;
            DrawSectionBody(() =>
            {
                if (IsAssetModeSelected)
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Toggle(RemoteAudioLabel, _character.EnableRemoteAudioOnStart);
                    }
                else
                    EditorGUILayout.PropertyField(_enableRemoteAudioProp, RemoteAudioLabel);

                bool remoteAudioEnabled = _character.EnableRemoteAudioOnStart;
                bool hasAudioOutput = _character.GetComponent<ConvaiAudioOutput>() != null;

                if (remoteAudioEnabled && !hasAudioOutput)
                {
                    WarningBox(
                        "No audio output",
                        "Remote audio is enabled, but this GameObject does not have a ConvaiAudioOutput " +
                        "component yet.");

                    if (GUILayout.Button(AddAudioOutputButton, Styles.MiniButton, GUILayout.Width(150f)))
                    {
                        Undo.AddComponent<ConvaiAudioOutput>(_character.gameObject);
                        EditorUtility.SetDirty(_character);
                    }
                }
            });
        }

        private void DrawSessionSection()
        {
            if (!DrawSection(SectionSessionId, SessionSection, Glyphs.Routing)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_autoConnectProp, AutoConnectLabel);
                EditorGUILayout.PropertyField(_ownerIdProp, OwnerIdLabel);

                if (IsAssetModeSelected)
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Toggle(SessionResumeLabel, _character.EnableSessionResume);
                    }
                else
                    EditorGUILayout.PropertyField(_enableSessionResumeProp, SessionResumeLabel);

                EditorGUILayout.PropertyField(_characterSessionIdProp, SessionIdLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(CopySessionIdButton, Styles.MiniButton, GUILayout.Width(120f)))
                    GUIUtility.systemCopyBuffer = _character.CharacterSessionId;

                if (GUILayout.Button(ClearSessionIdButton, Styles.MiniButton, GUILayout.Width(60f)))
                {
                    _characterSessionIdProp.stringValue = string.Empty;
                    serializedObject.ApplyModifiedProperties();
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(_dynamicInfoKeepInContextProp, KeepDynamicInfoLabel);
                if (_dynamicInfoKeepInContextProp != null && _dynamicInfoKeepInContextProp.boolValue)
                    EditorGUILayout.PropertyField(_dynamicInfoTextProp, DynamicInfoTextLabel);

                EditorGUILayout.PropertyField(_characterReadyTimeoutProp, ReadyTimeoutLabel);
            });
        }

        private void DrawValidationSection()
        {
            if (!DrawSection(SectionValidationId, ValidationSection, Glyphs.Validation)) return;
            DrawSectionBody(() =>
            {
                string validationMessage = ValidateCharacterId(_character.CharacterId ?? string.Empty);
                if (string.IsNullOrEmpty(validationMessage))
                    InfoBox("Character ID", "Character ID format looks valid.");
                else
                    WarningBox("Character ID", validationMessage);

                if (FindAnyObjectByType<ConvaiManager>() == null)
                    WarningBox(
                        "No ConvaiManager",
                        "ConvaiManager was not found in the scene. Add one so this character can connect.");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(ProjectSettingsButton, Styles.MiniButton, GUILayout.Width(110f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");

                if (GUILayout.Button(QuickstartButton, Styles.MiniButton, GUILayout.Width(80f)))
                    UnityEngine.Application.OpenURL(ConvaiEditorLinks.DocsUnityQuickstartUrl);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            });
        }

        private static string ValidateCharacterId(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return "Character ID is required. Copy it from your character's page on the Convai dashboard.";

            if (characterId != characterId.Trim())
                return "Character ID has a space before or after it. Delete the extra spaces.";

            if (characterId.Length != 36)
                return $"Character ID should be 36 characters, and this one is {characterId.Length}. "
                       + "Copy the whole ID from the Convai dashboard.";

            if (!GuidRegex.IsMatch(characterId))
                return "Character ID is not in the expected form "
                       + "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx. Copy it again from the Convai "
                       + "dashboard — it is easy to pick up a character's name by mistake.";

            return string.Empty;
        }

        private void FetchCharacterNameFromApi() => _ = FetchCharacterNameFromApiAsync();

        private async Task FetchCharacterNameFromApiAsync()
        {
            if (_isFetchingName || UsesCharacterConfigAsset) return;

            string characterId = _character?.CharacterId;
            string validationError = ValidateCharacterId(characterId);
            if (!string.IsNullOrEmpty(validationError))
            {
                ConvaiLogger.Warning($"Cannot fetch character name: {validationError}", LogCategory.Editor);
                return;
            }

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ConvaiLogger.Warning("Cannot fetch character name: API key not configured.", LogCategory.Editor);
                return;
            }

            _isFetchingName = true;
            try
            {
                var options = ConvaiRestOptionsFactory.Create(settings.ApiKey);
                using var client = new ConvaiRestClient(options);
                CharacterDetails details = await client.Characters.GetDetailsAsync(characterId);
                if (!string.IsNullOrWhiteSpace(details.CharacterName))
                {
                    string fetchedName = details.CharacterName.Trim();
                    EditorApplication.delayCall += () =>
                    {
                        if (this == null || _character == null || _characterNameProp == null || IsAssetModeSelected)
                            return;

                        Undo.RecordObject(_character, "Fetch Convai Character Name");
                        serializedObject.UpdateIfRequiredOrScript();
                        _characterNameProp.stringValue = fetchedName;
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_character);
                        Repaint();
                    };
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to fetch character name: {ex.Message}", LogCategory.Editor);
            }
            finally
            {
                _isFetchingName = false;
            }
        }
    }
}
