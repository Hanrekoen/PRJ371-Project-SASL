using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Application.Services.Narrative;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Narrative;
using Convai.RestAPI.Internal.Models;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Logging;
using Convai.Runtime.Utilities;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Modules.Narrative
{
    /// <summary>
    ///     Unity-specific configuration for a narrative section's events.
    ///     This class wraps the engine-agnostic NarrativeSection with UnityEvents for Inspector configuration.
    /// </summary>
    [Serializable]
    public class UnitySectionEventConfig
    {
        [SerializeField] private string _sectionId;
        [SerializeField] private string _sectionName;
        [SerializeField] private bool _isOrphaned;
        [SerializeField] private UnityEvent _onSectionStart = new();
        [SerializeField] private UnityEvent _onSectionEnd = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnitySectionEventConfig" /> class.
        /// </summary>
        public UnitySectionEventConfig()
        {
            _sectionId = string.Empty;
            _sectionName = string.Empty;
            _isOrphaned = false;
            _onSectionStart = new UnityEvent();
            _onSectionEnd = new UnityEvent();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnitySectionEventConfig" /> class.
        /// </summary>
        /// <param name="sectionId">Section identifier.</param>
        /// <param name="sectionName">Section display name.</param>
        public UnitySectionEventConfig(string sectionId, string sectionName)
        {
            _sectionId = sectionId ?? string.Empty;
            _sectionName = sectionName ?? string.Empty;
            _isOrphaned = false;
            _onSectionStart = new UnityEvent();
            _onSectionEnd = new UnityEvent();
        }

        /// <summary>Unique identifier matching the section ID from Convai's Narrative Design.</summary>
        public string SectionId => _sectionId;

        /// <summary>Display name of the section.</summary>
        public string SectionName => _sectionName;

        /// <summary>Whether this section was deleted on the backend.</summary>
        public bool IsOrphaned => _isOrphaned;

        /// <summary>Unity Event invoked when this section becomes active.</summary>
        public UnityEvent OnSectionStart => _onSectionStart;

        /// <summary>Unity Event invoked when leaving this section.</summary>
        public UnityEvent OnSectionEnd => _onSectionEnd;

        /// <summary>Updates the section name.</summary>
        public void UpdateName(string newName) => _sectionName = newName ?? string.Empty;

        /// <summary>Sets the orphaned state.</summary>
        public void SetOrphaned(bool isOrphaned) => _isOrphaned = isOrphaned;

        /// <summary>Sets the section ID (for internal use).</summary>
        internal void SetSectionId(string sectionId) => _sectionId = sectionId ?? string.Empty;
    }

    /// <summary>
    ///     Unity-specific configuration for a template key.
    /// </summary>
    [Serializable]
    public class UnityTemplateKeyConfig
    {
        [SerializeField] private string _key;
        [SerializeField] private string _value;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnityTemplateKeyConfig" /> class.
        /// </summary>
        public UnityTemplateKeyConfig() { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnityTemplateKeyConfig" /> class.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="value">Template value.</param>
        public UnityTemplateKeyConfig(string key, string value)
        {
            _key = key;
            _value = value;
        }

        /// <summary>Gets or sets the template key name.</summary>
        public string Key
        {
            get => _key;
            set => _key = value;
        }

        /// <summary>Gets or sets the template value.</summary>
        public string Value
        {
            get => _value;
            set => _value = value;
        }
    }

    /// <summary>
    ///     MonoBehaviour wrapper for Convai Narrative Design integration.
    ///     Add this component to a GameObject to configure narrative sections and template keys in the Inspector.
    /// </summary>
    /// <remarks>
    ///     This component:
    ///     - Hosts the engine-agnostic NarrativeDesignController
    ///     - Provides Unity-specific event configuration via UnitySectionEventConfig
    ///     - Subscribes to NarrativeSectionChanged events via EventHub
    ///     - Forwards section changes to Unity Events
    ///     - Supports auto-fetching and syncing sections from the backend
    /// </remarks>
    [AddComponentMenu("Convai/Narrative Design Manager")]
    public class ConvaiNarrativeDesignManager : MonoBehaviour, INarrativeSectionNameResolver,
        IConvaiModule
    {
        [Header("Character Reference")]
        [Tooltip(
            "The Convai character to send narrative commands to. If not set, will try to find one on the same GameObject.")]
        [RequireInterface(typeof(IConvaiCharacterAgent))]
        [SerializeField]
        private MonoBehaviour _characterComponent;

        [Header("Narrative Sections")]
        [Tooltip("Section event configurations. Each section can have Unity Events for start/end.")]
        [SerializeField]
        private List<UnitySectionEventConfig> _sectionConfigs = new();

        [Header("Template Keys")]
        [Tooltip("Template keys for dynamic placeholder resolution in narrative objectives.")]
        [SerializeField]
        private List<UnityTemplateKeyConfig> _templateKeys = new();

        [Header("Sync Status")] [Tooltip("Whether a fetch operation is currently in progress.")] [SerializeField]
        private bool _isFetching;

        [Tooltip("Timestamp of the last successful sync.")] [SerializeField]
        private string _lastSyncTime;

        [Tooltip("Character ID from the last successful sync.")] [SerializeField]
        private string _lastSyncedCharacterId;

        [Tooltip("Error message from the last fetch attempt, if any.")] [SerializeField]
        private string _lastFetchError;

        private ICredentialProvider _credentialProvider;

        private IEventHub _eventHub;
        private bool _isSubscribed;
        private SubscriptionToken _sectionChangedToken;
        private NarrativeSectionNameResolverAdapter _sectionNameResolverRegistry;

        /// <summary>
        ///     Gets the engine-agnostic narrative design controller.
        /// </summary>
        public NarrativeDesignController Controller { get; private set; }

        /// <summary>
        ///     Gets the section event configurations for Inspector display.
        /// </summary>
        public IReadOnlyList<UnitySectionEventConfig> SectionConfigs => _sectionConfigs;

        /// <summary>
        ///     Gets the template keys for Inspector display.
        /// </summary>
        public IReadOnlyList<UnityTemplateKeyConfig> TemplateKeyConfigs => _templateKeys;

        /// <summary>
        ///     Gets the current section ID.
        /// </summary>
        public string CurrentSectionID => Controller?.CurrentSectionID ?? string.Empty;

        /// <summary>
        ///     Gets the current section data including behavior tree information.
        /// </summary>
        public NarrativeSectionData CurrentSectionData => Controller?.CurrentSectionData;

        /// <summary>
        ///     Event invoked when any narrative section changes.
        /// </summary>
        public UnityEvent<string> OnAnySectionChanged => _onAnySectionChanged;

        /// <summary>
        ///     Event invoked when full section data is received.
        /// </summary>
        public UnityEvent<NarrativeSectionData> OnSectionDataReceived => _onSectionDataReceived;

        /// <summary>
        ///     Event invoked when sections are synced from the backend.
        /// </summary>
        public UnityEvent<SectionSyncResult> OnSectionsSynced => _onSectionsSynced;

        /// <summary>
        ///     Whether a fetch operation is currently in progress.
        /// </summary>
        public bool IsFetching => _isFetching;

        /// <summary>
        ///     Timestamp of the last successful sync.
        /// </summary>
        public string LastSyncTime => _lastSyncTime;

        /// <summary>
        ///     Character ID from the last successful sync.
        /// </summary>
        public string LastSyncedCharacterId => _lastSyncedCharacterId;

        /// <summary>
        ///     Error message from the last fetch attempt, if any.
        /// </summary>
        public string LastFetchError => _lastFetchError;

        /// <summary>
        ///     Gets the character component reference.
        /// </summary>
        public MonoBehaviour CharacterComponent => _characterComponent;

        /// <summary>
        ///     Gets the character agent interface.
        /// </summary>
        public IConvaiCharacterAgent Character { get; private set; }

        /// <summary>
        ///     Gets the count of active (non-orphaned) sections.
        /// </summary>
        public int ActiveSectionCount
        {
            get
            {
                int count = 0;
                foreach (UnitySectionEventConfig config in _sectionConfigs)
                {
                    if (!config.IsOrphaned)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        ///     Gets the count of orphaned sections.
        /// </summary>
        public int OrphanedSectionCount
        {
            get
            {
                int count = 0;
                foreach (UnitySectionEventConfig config in _sectionConfigs)
                {
                    if (config.IsOrphaned)
                        count++;
                }

                return count;
            }
        }

        private void Awake()
        {
            ConvaiManager.ActiveManager?.RegisterModule(this);
            Controller = new NarrativeDesignController
            {
                LogInfo = msg => ConvaiLogger.Info($"[Narrative Design] {msg}", LogCategory.Narrative),
                LogWarning = msg => ConvaiLogger.Warning($"[Narrative Design] {msg}", LogCategory.Narrative)
            };

            Controller.OnTemplateKeysUpdateRequested += OnTemplateKeysUpdateRequested;
            Controller.OnSectionChanged += OnControllerSectionChanged;

            if (_characterComponent == null) _characterComponent = GetComponent<MonoBehaviour>();

            Character = _characterComponent as IConvaiCharacterAgent;

            SyncTemplateKeysToController();
        }

        private void OnEnable() => SubscribeToEventHub();

        private void OnDisable() => UnsubscribeFromEventHub();

        private void OnDestroy()
        {
            ConvaiManager.ActiveManager?.UnregisterModule(this);
            if (Controller != null)
            {
                Controller.OnTemplateKeysUpdateRequested -= OnTemplateKeysUpdateRequested;
                Controller.OnSectionChanged -= OnControllerSectionChanged;
            }

            _sectionNameResolverRegistry?.RemoveResolver(this);
            UnsubscribeFromEventHub();
        }

        #region Editor Helpers

        private void OnValidate()
        {
            if (_characterComponent == null)
            {
                MonoBehaviour[] components = GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour mb in components)
                {
                    if (mb is IConvaiCharacterAgent && mb != this)
                    {
                        _characterComponent = mb;
                        break;
                    }
                }
            }
        }

        #endregion

        /// <inheritdoc />
        public bool TryGetSectionName(string sectionId, out string sectionName)
        {
            sectionName = null;

            UnitySectionEventConfig config = FindSectionConfig(sectionId);
            if (config == null || string.IsNullOrWhiteSpace(config.SectionName))
                return false;

            sectionName = config.SectionName;
            return true;
        }

        /// <summary>
        ///     Injects the EventHub dependency. Called by the ConvaiManager pipeline.
        /// </summary>
        /// <param name="eventHub">The event hub to subscribe to.</param>
        /// <param name="credentialProvider">Optional credential provider for API access.</param>
        public void Inject(IEventHub eventHub, ICredentialProvider credentialProvider = null)
        {
            ConvaiLogger.Debug(
                $"Inject called with eventHub={(eventHub != null ? "valid" : "null")}",
                LogCategory.Narrative);
            UnsubscribeFromEventHub();
            _eventHub = eventHub;
            _credentialProvider = credentialProvider;
            SubscribeToEventHub();
        }

        /// <summary>
        ///     Sets the character reference for sending commands.
        /// </summary>
        /// <param name="character">The character agent to use.</param>
        public void SetCharacter(IConvaiCharacterAgent character)
        {
            Character = character;
            if (character is MonoBehaviour mb) _characterComponent = mb;
        }

        private void SubscribeToEventHub()
        {
            if (_eventHub == null || _isSubscribed) return;

            _sectionChangedToken = _eventHub.Subscribe<NarrativeSectionChanged>(OnNarrativeSectionChanged);
            _isSubscribed = true;
            ConvaiLogger.Debug("Subscribed to NarrativeSectionChanged events",
                LogCategory.Narrative);
        }

        private void UnsubscribeFromEventHub()
        {
            if (!_isSubscribed || _eventHub == null) return;

            _eventHub.Unsubscribe(_sectionChangedToken);
            _isSubscribed = false;
        }

        private void OnNarrativeSectionChanged(NarrativeSectionChanged evt)
        {
            ConvaiLogger.Debug("Received NarrativeSectionChanged event: " +
                               $"SectionId={evt.SectionId}, CharacterId={evt.CharacterId}, " +
                               $"HasBTCode={!string.IsNullOrEmpty(evt.BehaviorTreeCode)}, HasBTConstants={!string.IsNullOrEmpty(evt.BehaviorTreeConstants)}",
                LogCategory.Narrative);

            if (Character != null && !string.IsNullOrEmpty(Character.CharacterId))
            {
                if (!string.IsNullOrEmpty(evt.CharacterId) && evt.CharacterId != Character.CharacterId)
                {
                    ConvaiLogger.Debug("Ignoring event for different character. " +
                                       $"Expected={Character.CharacterId}, Received={evt.CharacterId}",
                        LogCategory.Narrative);
                    return;
                }
            }

            var sectionData = new NarrativeSectionData(
                evt.SectionId,
                evt.BehaviorTreeCode,
                evt.BehaviorTreeConstants
            );

            Controller?.OnNarrativeDesignSectionDataReceived(sectionData);

            _onAnySectionChanged?.Invoke(evt.SectionId);
            _onSectionDataReceived?.Invoke(sectionData);

            ConvaiLogger.Debug($"Processed section change: SectionId={evt.SectionId}",
                LogCategory.Narrative);
        }

        private void OnControllerSectionChanged(string previousSectionId, string newSectionId)
        {
            ConvaiLogger.Debug(
                $"Section transition: Previous={previousSectionId ?? "(none)"} → New={newSectionId ?? "(none)"}",
                LogCategory.Narrative);

            if (!string.IsNullOrEmpty(previousSectionId))
            {
                UnitySectionEventConfig prevConfig = FindSectionConfig(previousSectionId);
                if (prevConfig != null)
                {
                    ConvaiLogger.Debug(
                        $"Invoking OnSectionEnd for '{prevConfig.SectionName}' ({previousSectionId})",
                        LogCategory.Narrative);
                    prevConfig.OnSectionEnd?.Invoke();
                }
                else
                {
                    ConvaiLogger.Warning(
                        $"No config found for previous section: {previousSectionId}",
                        LogCategory.Narrative);
                }
            }

            if (!string.IsNullOrEmpty(newSectionId))
            {
                UnitySectionEventConfig newConfig = FindSectionConfig(newSectionId);
                if (newConfig != null)
                {
                    ConvaiLogger.Debug(
                        $"Invoking OnSectionStart for '{newConfig.SectionName}' ({newSectionId})",
                        LogCategory.Narrative);
                    newConfig.OnSectionStart?.Invoke();
                }
                else
                {
                    ConvaiLogger.Warning(
                        $"No config found for new section: {newSectionId}. " +
                        $"Available sections: {string.Join(", ", GetSectionIds())}", LogCategory.Narrative);
                }
            }
        }

        /// <summary>
        ///     Gets all configured section IDs for debugging purposes.
        /// </summary>
        private IEnumerable<string> GetSectionIds()
        {
            foreach (UnitySectionEventConfig config in _sectionConfigs) yield return config.SectionId;
        }

        private void OnTemplateKeysUpdateRequested(Dictionary<string, string> keys)
        {
            if (Character == null)
            {
                ConvaiLogger.Warning("Cannot send template keys: no character assigned.",
                    LogCategory.Narrative);
                return;
            }

            if (Character is ConvaiCharacter convaiCharacter)
                convaiCharacter.NarrativeDesign.SetTemplateKeys(keys);
            else
                Character.UpdateTemplateKeys(keys);
        }

        /// <summary>
        ///     Finds a section config by ID.
        /// </summary>
        /// <param name="sectionId">The section ID.</param>
        /// <returns>The config if found, null otherwise.</returns>
        public UnitySectionEventConfig FindSectionConfig(string sectionId) => string.IsNullOrEmpty(sectionId)
            ? null
            : _sectionConfigs.Find(c => c.SectionId == sectionId);

        #region IConvaiModule Implementation

        /// <inheritdoc />
        public string ModuleId => "convai.narrative";

        /// <inheritdoc />
        public string DisplayName => "Narrative Design";

        /// <inheritdoc />
        public IReadOnlyList<string> RequiredModules => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

        /// <inheritdoc />
        public IReadOnlyList<Type> ProvidedServices { get; } = new[] { typeof(INarrativeSectionNameResolver) };

        /// <inheritdoc />
        public bool IsActive => enabled && isActiveAndEnabled;

        /// <inheritdoc />
        public ValueTask RegisterAsync(IModuleContext context, CancellationToken ct = default)
        {
            if (context == null) return default;

            // Check if a resolver registry already exists (another NarrativeDesignManager registered it)
            if (context.TryGetModuleService(out NarrativeSectionNameResolverAdapter resolverRegistry))
            {
                _sectionNameResolverRegistry = resolverRegistry;
                _sectionNameResolverRegistry.AddResolver(this);
                return default;
            }

            // Create and register the resolver registry
            _sectionNameResolverRegistry = new NarrativeSectionNameResolverAdapter();
            _sectionNameResolverRegistry.AddResolver(this);
            context.ProvideModuleService(_sectionNameResolverRegistry);
            context.ProvideModuleService<INarrativeSectionNameResolver>(_sectionNameResolverRegistry);
            return default;
        }

        /// <inheritdoc />
        public ValueTask StartAsync(IModuleContext context, CancellationToken ct = default)
        {
            if (context == null) return default;

            IEventHub eventHub = context.Events;
            ICredentialProvider credentialProvider = context.Credentials;

            if (eventHub != null) Inject(eventHub, credentialProvider);

            return default;
        }

        /// <inheritdoc />
        public ValueTask PauseAsync(RuntimePauseReason reason, CancellationToken ct = default) => default;

        /// <inheritdoc />
        public ValueTask ResumeAsync(CancellationToken ct = default) => default;

        /// <inheritdoc />
        public ValueTask StopAsync(CancellationToken ct = default) => default;

        #endregion

#pragma warning disable CS0649
        [Header("Events")]
        [Tooltip("Invoked when any narrative section changes. Receives the section ID.")]
        [SerializeField]
        private UnityEvent<string> _onAnySectionChanged;

        [Tooltip("Invoked when full section data is received (includes BT data).")] [SerializeField]
        private UnityEvent<NarrativeSectionData> _onSectionDataReceived;

        [Tooltip("Invoked when sections are synced from the backend.")] [SerializeField]
        private UnityEvent<SectionSyncResult> _onSectionsSynced;
#pragma warning restore CS0649

        #region Public API

        /// <summary>
        ///     Updates a single template key.
        /// </summary>
        /// <param name="key">The key name (e.g., "PlayerName").</param>
        /// <param name="value">The value to set.</param>
        public void UpdateTemplateKey(string key, string value)
        {
            Controller?.UpdateTemplateKey(key, value);

            UnityTemplateKeyConfig existing = _templateKeys.Find(k => k.Key == key);
            if (existing != null)
                existing.Value = value;
            else
                _templateKeys.Add(new UnityTemplateKeyConfig(key, value));
        }

        /// <summary>
        ///     Updates multiple template keys.
        /// </summary>
        /// <param name="keys">Dictionary of key-value pairs.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> keys)
        {
            if (keys == null) return;
            foreach (KeyValuePair<string, string> kvp in keys) UpdateTemplateKey(kvp.Key, kvp.Value);
        }

        /// <summary>
        ///     Sends the current template keys to the server.
        /// </summary>
        public void SendTemplateKeysUpdate()
        {
            SyncTemplateKeysToController();
            Controller?.SendTemplateKeysUpdate();
        }

        /// <summary>
        ///     Updates a template key and immediately sends all keys to the server.
        /// </summary>
        /// <param name="key">The key name.</param>
        /// <param name="value">The value to set.</param>
        public void UpdateAndSendTemplateKey(string key, string value)
        {
            UpdateTemplateKey(key, value);
            SendTemplateKeysUpdate();
        }

        /// <summary>
        ///     Gets all configured template keys as a dictionary.
        /// </summary>
        /// <returns>Dictionary of template keys.</returns>
        public Dictionary<string, string> GetTemplateKeys()
        {
            var result = new Dictionary<string, string>();
            foreach (UnityTemplateKeyConfig key in _templateKeys)
            {
                if (!string.IsNullOrEmpty(key.Key))
                    result[key.Key] = key.Value ?? string.Empty;
            }

            return result;
        }

        /// <summary>
        ///     Resets the controller state.
        /// </summary>
        public void ResetController() => Controller?.Reset();

        #endregion

        #region Fetch and Sync

        /// <summary>
        ///     Fetches sections from the backend and syncs them with the local list.
        ///     This method preserves user-configured Unity Events while updating section data.
        /// </summary>
        /// <param name="apiKey">
        ///     Optional API key. If null, runtime callers use the registered runtime credential provider
        ///     when available; otherwise the fetcher falls back to project settings for editor/default flows.
        /// </param>
        public void FetchAndSyncFromBackend(string apiKey = null) => _ = FetchAndSyncFromBackendAsync(apiKey);

        /// <summary>
        ///     Fetches sections from the backend and syncs them with the local list.
        /// </summary>
        /// <param name="apiKey">
        ///     Optional API key. If null, runtime callers use the registered runtime credential provider
        ///     when available; otherwise the fetcher falls back to project settings for editor/default flows.
        /// </param>
        /// <returns>Sync result describing changes applied to the local config list.</returns>
        public async Task<SectionSyncResult> FetchAndSyncFromBackendAsync(string apiKey = null)
        {
            if (_isFetching)
            {
                ConvaiLogger.Warning("Fetch already in progress.",
                    LogCategory.Narrative);
                return new SectionSyncResult { Success = false, Error = "Fetch already in progress." };
            }

            string characterId = GetCharacterId();
            if (string.IsNullOrEmpty(characterId))
            {
                _lastFetchError = "No character assigned or character has no ID.";
                ConvaiLogger.Error($"{_lastFetchError}", LogCategory.Narrative);
                return new SectionSyncResult { Success = false, Error = _lastFetchError };
            }

            _isFetching = true;
            _lastFetchError = null;

            try
            {
                string resolvedApiKey = apiKey;
                if (string.IsNullOrWhiteSpace(resolvedApiKey) && _credentialProvider != null)
                    resolvedApiKey = _credentialProvider.GetApiKey();

                FetchResult<List<SectionData>> result =
                    await NarrativeDesignFetcher.FetchSectionsAsync(characterId, resolvedApiKey);

                if (result.Success)
                {
                    SectionSyncResult syncResult = SyncSectionConfigs(result.Data);

                    var syncData = new List<SectionSyncData>();
                    foreach (SectionData section in result.Data)
                        syncData.Add(new SectionSyncData(section.SectionId, section.SectionName));
                    Controller?.SyncWithBackendData(syncData);

                    _lastSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    _lastSyncedCharacterId = characterId;
                    _lastFetchError = null;

                    ConvaiLogger.Debug($"{syncResult}", LogCategory.Narrative);

                    _onSectionsSynced?.Invoke(syncResult);

                    return syncResult;
                }
                else
                {
                    _lastFetchError = result.Error;
                    ConvaiLogger.Error($"Fetch failed: {result.Error}",
                        LogCategory.Narrative);

                    return new SectionSyncResult { Success = false, Error = result.Error };
                }
            }
            catch (Exception ex)
            {
                _lastFetchError = ex.Message;
                ConvaiLogger.Error($"Fetch exception: {ex.Message}",
                    LogCategory.Narrative);

                return new SectionSyncResult { Success = false, Error = ex.Message };
            }
            finally
            {
                _isFetching = false;
            }
        }

        /// <summary>
        ///     Syncs the Unity section configs with backend data.
        ///     Preserves Unity Event configurations while updating section metadata.
        /// </summary>
        private SectionSyncResult SyncSectionConfigs(List<SectionData> backendSections)
        {
            if (backendSections == null) return new SectionSyncResult { Error = "Backend sections list is null." };

            int updated = 0;
            int added = 0;
            int orphaned = 0;
            int reactivated = 0;

            var backendIds = new HashSet<string>();
            foreach (SectionData bs in backendSections)
            {
                if (!string.IsNullOrEmpty(bs.SectionId))
                    backendIds.Add(bs.SectionId);
            }

            foreach (UnitySectionEventConfig config in _sectionConfigs)
            {
                if (string.IsNullOrEmpty(config.SectionId)) continue;

                SectionData backendMatch = null;
                foreach (SectionData bs in backendSections)
                {
                    if (bs.SectionId == config.SectionId)
                    {
                        backendMatch = bs;
                        break;
                    }
                }

                if (backendMatch != null)
                {
                    if (config.SectionName != backendMatch.SectionName)
                    {
                        config.UpdateName(backendMatch.SectionName);
                        updated++;
                    }

                    if (config.IsOrphaned)
                    {
                        config.SetOrphaned(false);
                        reactivated++;
                    }
                }
                else
                {
                    if (!config.IsOrphaned)
                    {
                        config.SetOrphaned(true);
                        orphaned++;
                    }
                }
            }

            foreach (SectionData backendSection in backendSections)
            {
                if (string.IsNullOrEmpty(backendSection.SectionId)) continue;

                UnitySectionEventConfig existing = FindSectionConfig(backendSection.SectionId);
                if (existing == null)
                {
                    _sectionConfigs.Add(new UnitySectionEventConfig(backendSection.SectionId,
                        backendSection.SectionName));
                    added++;
                }
            }

            return new SectionSyncResult
            {
                Success = true,
                SectionsUpdated = updated,
                SectionsAdded = added,
                SectionsOrphaned = orphaned,
                SectionsReactivated = reactivated
            };
        }

        /// <summary>
        ///     Gets the character ID from the assigned character.
        /// </summary>
        /// <returns>The character ID, or null if not available.</returns>
        public string GetCharacterId()
        {
            if (Character != null) return Character.CharacterId;

            return _characterComponent is IConvaiCharacterAgent agent ? agent.CharacterId : null;
        }

        /// <summary>
        ///     Clears the last fetch error.
        /// </summary>
        public void ClearFetchError() => _lastFetchError = null;

        /// <summary>
        ///     Clears all section configurations.
        ///     Used when switching to a different character to remove sections from the previous character.
        /// </summary>
        public void ClearAllSectionConfigs()
        {
            _sectionConfigs.Clear();
            _lastSyncedCharacterId = null;
            _lastSyncTime = null;
            Controller?.ClearSections();
            ConvaiLogger.Debug("All section configurations cleared.",
                LogCategory.Narrative);
        }

        /// <summary>
        ///     Syncs template keys from Inspector list to Controller.
        /// </summary>
        private void SyncTemplateKeysToController()
        {
            if (Controller == null) return;

            foreach (UnityTemplateKeyConfig key in _templateKeys)
            {
                if (!string.IsNullOrEmpty(key.Key))
                    Controller.AddTemplateKey(key.Key, key.Value);
            }
        }

        #endregion
    }
}
