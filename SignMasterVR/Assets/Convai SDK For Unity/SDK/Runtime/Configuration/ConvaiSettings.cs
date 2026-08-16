using System;
using System.IO;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Convai.Runtime
{
    /// <summary>
    ///     Centralized settings for the Convai SDK.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiSettings", menuName = "Convai/SDK Settings")]
    public class ConvaiSettings : ScriptableObject
    {
        private const string ResourcePath = "ConvaiSettings";
        private const string ResourceAssetPath = "Assets/Resources/ConvaiSettings.asset";

        /// <summary>Default realtime core server URL, used unless the environment is Custom.</summary>
        public const string DefaultCoreServerUrl = "https://live.convai.com";

#if UNITY_EDITOR
        /// <summary>
        ///     Called when script is loaded or a value is changed in the inspector.
        ///     Increments config version to invalidate logging caches.
        /// </summary>
        private void OnValidate()
        {
            IncrementConfigVersion();
        }
#endif

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        internal static void EnsureSettingsAssetExists()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ConvaiSettings>(ResourceAssetPath);
            if (asset == null) asset = Resources.Load<ConvaiSettings>(ResourcePath);

            if (asset == null)
            {
                string directory = Path.GetDirectoryName(ResourceAssetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                asset = CreateInstance<ConvaiSettings>();
                AssetDatabase.CreateAsset(asset, ResourceAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            _instance = asset;
            asset.MigrateLegacyDataIfNeeded();
        }

        /// <summary>
        ///     One-time migration of legacy serialized data. Currently: plaintext API key
        ///     to the obfuscated representation. Safe to call repeatedly.
        /// </summary>
        internal void MigrateLegacyDataIfNeeded()
        {
            if (string.IsNullOrEmpty(_apiKey)) return;

            if (string.IsNullOrEmpty(_apiKeyObfuscated))
                _apiKeyObfuscated = ConvaiApiKeyObfuscation.Obfuscate(_apiKey);
            _apiKey = string.Empty;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }
#endif

        #region Singleton Instance

        private static ConvaiSettings _instance;

        /// <summary>
        ///     Gets the singleton instance of ConvaiSettings.
        ///     Creates a new instance if one doesn't exist (Editor only).
        /// </summary>
        public static ConvaiSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ConvaiSettings>(ResourcePath);
#if UNITY_EDITOR
                    if (_instance == null) _instance = CreateInstance<ConvaiSettings>();
#endif
                }

                return _instance;
            }
        }

        #endregion

        #region Serialized Fields

        [SerializeField]
        [ConvaiInspectorSection("Credentials")]
        [Tooltip("Obfuscated Convai API key. Edit via Project Settings > Convai SDK.")]
        private string _apiKeyObfuscated = "";

        // Legacy plaintext key. Migrated into _apiKeyObfuscated and cleared on first editor load.
        [SerializeField] [HideInInspector] private string _apiKey = "";

        [SerializeField]
        [ConvaiInspectorSection("Credentials")]
        [Tooltip("Authentication mode used by runtime room connections.")]
        private ConvaiAuthMode _authMode = ConvaiAuthMode.ApiKey;

        [SerializeField]
        [Tooltip("Developer backend endpoint that returns a short-lived Convai auth token.")]
        private string _authTokenEndpointUrl = "";

        [SerializeField]
        [Tooltip("HTTP method used when requesting a short-lived auth token from the developer backend.")]
        private ConvaiAuthTokenHttpMethod _authTokenHttpMethod = ConvaiAuthTokenHttpMethod.Get;

        [SerializeField]
        [Tooltip("JSON response field containing the short-lived auth token. Dotted paths are supported.")]
        private string _authTokenResponseField = "apiAuthToken";

        [SerializeField]
        [Tooltip("Optional headers sent only to the developer auth-token endpoint.")]
        private ConvaiAuthTokenHeader[] _authTokenHeaders = Array.Empty<ConvaiAuthTokenHeader>();

        [SerializeField]
        [Tooltip("Convai service environment. Production unless directed otherwise; Custom unlocks raw URLs.")]
        private ConvaiApiEnvironment _apiEnvironment = ConvaiApiEnvironment.Production;

        [SerializeField]
        [ConvaiInspectorSection("Credentials")]
        [Tooltip("Convai realtime server URL used for room connect requests. Only honored when Environment is Custom.")]
        private string _serverUrl = DefaultCoreServerUrl;

        [SerializeField]
        [ConvaiInspectorSection("Credentials")]
        [Tooltip("REST API base URL override. Only honored when Environment is Custom. Empty = production URL.")]
        private string _customRestBaseUrl = "";

        [SerializeField]
        [ConvaiInspectorSection("Audio")]
        [Tooltip("Default microphone device id. Empty = system default device.")]
        private string _defaultMicrophoneDeviceId = "";

        [SerializeField] [ConvaiInspectorSection("Audio")]
        [Tooltip("Connection timeout in seconds.")] [Range(5f, 120f)]
        private float _connectionTimeout = 30f;

        [SerializeField] [ConvaiInspectorSection("Audio")]
        [Tooltip("Default master volume for character audio.")] [Range(0f, 1f)]
        private float _characterAudioVolume = 1f;

        [SerializeField] [ConvaiInspectorSection("Audio")]
        [Tooltip("Play audio feedback sounds (e.g., listening indicator) by default.")]
        private bool _audioFeedbackEnabled = true;

        [SerializeField] [ConvaiInspectorSection("Audio")]
        [Tooltip("How Convai audio, transcript presentation, and LipSync behave while the application is backgrounded.")]
        private RuntimeBackgroundPolicy _backgroundPolicy = RuntimeBackgroundPolicy.PauseTimeline;

        [SerializeField] [ConvaiInspectorSection("Logging")]
        [Tooltip("Global minimum log level. Logs below this level are filtered.")]
        private LogLevel _globalLogLevel = LogLevel.Info;

        [SerializeField] [ConvaiInspectorSection("Logging")]
        [Tooltip("Enable stack traces for Warning and Error logs.")]
        private bool _includeStackTraces = true;

        [SerializeField] [ConvaiInspectorSection("Logging")]
        [Tooltip("Enable colored output in Unity Console.")]
        private bool _coloredOutput = true;

        [SerializeField] [ConvaiInspectorSection("Logging")]
        [Tooltip("Per-category log level overrides. Empty = use global level.")]
        private LogLevelOverride[] _categoryOverrides = Array.Empty<LogLevelOverride>();

        [SerializeField] [ConvaiInspectorSection("Features")]
        [Tooltip("Enable the transcript system.")]
        private bool _transcriptSystemEnabled = true;

        [SerializeField] [ConvaiInspectorSection("Features")]
        [Tooltip("Enable the notification system.")]
        private bool _notificationSystemEnabled;

        [SerializeField] [ConvaiInspectorSection("Features")]
        [Tooltip("Default display name for the local player.")]
        private string _defaultPlayerDisplayName = "Player";

        #endregion

        #region Public Properties

        /// <summary>
        ///     Version number for cache invalidation. Increments when logging-related settings change.
        ///     Used by LoggingConfig for efficient cache validation.
        /// </summary>
        public int ConfigVersion { get; private set; }

        /// <summary>
        ///     Gets the category override array for batch processing.
        ///     Used by LoggingConfig for efficient cache building.
        /// </summary>
        public LogLevelOverride[] CategoryOverrides => _categoryOverrides;

        /// <summary>Convai API key for authentication.</summary>
        public string ApiKey
        {
            get
            {
                if (ConvaiApiKeyObfuscation.TryDeobfuscate(_apiKeyObfuscated, out string plain)) return plain;

                // Legacy plaintext fallback: covers builds made from an unmigrated asset.
                return _apiKey ?? string.Empty;
            }
        }

        /// <summary>Authentication mode used for runtime room connections.</summary>
        public ConvaiAuthMode AuthMode => _authMode;

        /// <summary>Developer backend URL used to resolve a short-lived auth token.</summary>
        public string AuthTokenEndpointUrl => _authTokenEndpointUrl?.Trim() ?? string.Empty;

        /// <summary>HTTP method used to request a short-lived auth token.</summary>
        public ConvaiAuthTokenHttpMethod AuthTokenHttpMethod => _authTokenHttpMethod;

        /// <summary>JSON field, or dotted field path, containing the resolved auth token.</summary>
        public string AuthTokenResponseField => string.IsNullOrWhiteSpace(_authTokenResponseField)
            ? "apiAuthToken"
            : _authTokenResponseField.Trim();

        /// <summary>Optional headers sent to the developer auth-token endpoint.</summary>
        public ConvaiAuthTokenHeader[] AuthTokenHeaders =>
            _authTokenHeaders ?? Array.Empty<ConvaiAuthTokenHeader>();

        /// <summary>Convai service environment preset.</summary>
        public ConvaiApiEnvironment ApiEnvironment => _apiEnvironment;

        /// <summary>
        ///     Convai realtime core server URL. Returns the serialized URL only when the
        ///     environment is Custom; Production and Beta both use the default core server.
        /// </summary>
        public string ServerUrl =>
            _apiEnvironment == ConvaiApiEnvironment.Custom && !string.IsNullOrWhiteSpace(_serverUrl)
                ? _serverUrl
                : DefaultCoreServerUrl;

        /// <summary>REST API base URL override. Only honored when the environment is Custom.</summary>
        public string RestBaseUrlOverride =>
            _apiEnvironment == ConvaiApiEnvironment.Custom ? _customRestBaseUrl : string.Empty;

        /// <summary>Default microphone device id. Empty = system default device.</summary>
        public string DefaultMicrophoneDeviceId => _defaultMicrophoneDeviceId ?? string.Empty;

        /// <summary>Connection timeout in seconds.</summary>
        public float ConnectionTimeout => _connectionTimeout;

        /// <summary>Default master volume for character audio (0-1).</summary>
        public float CharacterAudioVolume => _characterAudioVolume;

        /// <summary>Whether audio feedback sounds are enabled by default.</summary>
        public bool AudioFeedbackEnabled => _audioFeedbackEnabled;

        /// <summary>Project-wide default policy for application background transitions.</summary>
        public RuntimeBackgroundPolicy BackgroundPolicy => _backgroundPolicy;

        /// <summary>Default display name for the local player.</summary>
        public string DefaultPlayerDisplayName =>
            string.IsNullOrWhiteSpace(_defaultPlayerDisplayName) ? "Player" : _defaultPlayerDisplayName;

        /// <summary>Global minimum log level.</summary>
        public LogLevel GlobalLogLevel => _globalLogLevel;

        /// <summary>Whether stack traces are included for Warning and Error logs.</summary>
        public bool IncludeStackTraces => _includeStackTraces;

        /// <summary>Whether colored output is enabled in Unity Console.</summary>
        public bool ColoredOutput => _coloredOutput;

        /// <summary>
        ///     Gets the effective log level for a specific category.
        ///     Returns the category override if set, otherwise the global level.
        /// </summary>
        /// <param name="category">The log category to check.</param>
        /// <returns>The effective log level for the category.</returns>
        public LogLevel GetLogLevel(LogCategory category)
        {
            if (_categoryOverrides != null)
            {
                foreach (LogLevelOverride over in _categoryOverrides)
                {
                    if (over.Category == category)
                        return over.Level;
                }
            }

            return _globalLogLevel;
        }

        /// <summary>Whether the transcript system is enabled.</summary>
        public bool TranscriptSystemEnabled => _transcriptSystemEnabled;

        /// <summary>Whether the notification system is enabled.</summary>
        public bool NotificationSystemEnabled => _notificationSystemEnabled;

        /// <summary>Whether an API key is configured.</summary>
        public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

        /// <summary>
        ///     Whether the selected authentication mode has enough configuration to attempt a runtime connection.
        /// </summary>
        /// <remarks>
        ///     Auth Token mode is valid when a custom provider is registered or the configured endpoint uses HTTPS
        ///     (HTTP is accepted only for loopback development). The token itself is resolved once per connection.
        /// </remarks>
        public bool HasValidAuthConfig => _authMode == ConvaiAuthMode.ApiKey
            ? HasApiKey
            : ConvaiAuthTokenProviderRegistry.IsRegistered || TryGetAuthTokenEndpointUri(out _);

        /// <summary>Resolves a secure auth-token endpoint, allowing HTTP only for local loopback development.</summary>
        internal bool TryGetAuthTokenEndpointUri(out Uri endpoint) =>
            EndpointAuthTokenProvider.TryCreateEndpointUri(AuthTokenEndpointUrl, out endpoint);

        #endregion

        #region Runtime Setters

        private void MarkDirtyIfEditor()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        ///     Increments the config version to trigger cache invalidation.
        ///     Called when logging-related settings change.
        /// </summary>
        private void IncrementConfigVersion() => ConfigVersion++;

        /// <summary>
        ///     Sets the global log level at runtime.
        /// </summary>
        /// <param name="level">The new global log level.</param>
        public void SetGlobalLogLevel(LogLevel level)
        {
            if (_globalLogLevel != level)
            {
                _globalLogLevel = level;
                IncrementConfigVersion();
                MarkDirtyIfEditor();
            }
        }

        /// <summary>
        ///     Sets whether stack traces are included at runtime.
        /// </summary>
        /// <param name="include">Whether to include stack traces.</param>
        public void SetIncludeStackTraces(bool include)
        {
            if (_includeStackTraces != include)
            {
                _includeStackTraces = include;
                IncrementConfigVersion();
                MarkDirtyIfEditor();
            }
        }

        /// <summary>
        ///     Sets the category overrides at runtime.
        /// </summary>
        /// <param name="overrides">The new category overrides array.</param>
        public void SetCategoryOverrides(LogLevelOverride[] overrides)
        {
            _categoryOverrides = overrides ?? Array.Empty<LogLevelOverride>();
            IncrementConfigVersion();
            MarkDirtyIfEditor();
        }

        #endregion

        #region Editor-Only Setters

        /// <summary>
        ///     Sets the API key (stored obfuscated). In builds, this logs a warning and does nothing.
        ///     Use the Project Settings UI to configure the API key in the Editor.
        /// </summary>
        public void SetApiKey(string apiKey)
        {
#if UNITY_EDITOR
            _apiKeyObfuscated = ConvaiApiKeyObfuscation.Obfuscate(apiKey?.Trim());
            _apiKey = string.Empty;
            MarkDirtyIfEditor();
#else
            ConvaiLogger.Warning("SetApiKey is only available in the Editor. Use Project Settings to configure the API key.", LogCategory.SDK);
#endif
        }

        /// <summary>
        ///     Sets the custom core server URL. Only honored when the environment is Custom.
        ///     In builds, this logs a warning and does nothing.
        /// </summary>
        public void SetServerUrl(string serverUrl)
        {
#if UNITY_EDITOR
            _serverUrl = serverUrl;
            MarkDirtyIfEditor();
#else
            ConvaiLogger.Warning("SetServerUrl is only available in the Editor. Use Project Settings to configure the server URL.", LogCategory.SDK);
#endif
        }

        #endregion
    }

    /// <summary>
    ///     Serializable per-category log level override.
    /// </summary>
    [Serializable]
    public struct LogLevelOverride
    {
        /// <summary>The category to override.</summary>
        [Tooltip("The log category to override.")]
        public LogCategory Category;

        /// <summary>The log level for this category.</summary>
        [Tooltip("The log level for this category.")]
        public LogLevel Level;

        /// <summary>
        ///     Creates a new log level override.
        /// </summary>
        /// <param name="category">The category to override.</param>
        /// <param name="level">The log level for this category.</param>
        public LogLevelOverride(LogCategory category, LogLevel level)
        {
            Category = category;
            Level = level;
        }
    }
}
