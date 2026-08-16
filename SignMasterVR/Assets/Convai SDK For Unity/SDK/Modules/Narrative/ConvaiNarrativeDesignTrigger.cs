using System;
using System.Collections;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.RestAPI.Internal.Models;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Utilities;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Convai.Modules.Narrative
{
    /// <summary>
    ///     Activation modes for narrative triggers.
    /// </summary>
    public enum TriggerActivationMode
    {
        /// <summary>Triggers on OnTriggerEnter when tagged object enters collider.</summary>
        Collision,

        /// <summary>Continuously checks distance in Update, triggers when player within radius.</summary>
        Proximity,

        /// <summary>Only triggers when InvokeTrigger() is called from code/events.</summary>
        Manual,

        /// <summary>Triggers after player enters zone + configured delay.</summary>
        TimeBased
    }

    /// <summary>
    ///     Describes the current status of the trigger for diagnostics.
    /// </summary>
    public enum TriggerStatus
    {
        /// <summary>Trigger is ready and waiting for activation conditions.</summary>
        Ready,

        /// <summary>Trigger has already fired and TriggerOnce is enabled.</summary>
        AlreadyFired,

        /// <summary>Trigger is queued waiting for character to be ready.</summary>
        QueuedWaitingForCharacter,

        /// <summary>Trigger has a configuration error.</summary>
        ConfigurationError,

        /// <summary>Trigger is disabled.</summary>
        Disabled
    }

    /// <summary>
    ///     Component for invoking Convai Narrative Design triggers from Unity.
    /// </summary>
    [AddComponentMenu("Convai/Convai Narrative Design Trigger")]
    public class ConvaiNarrativeDesignTrigger : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Character Reference")]
        [Tooltip("The Convai character to send the trigger to.")]
        [RequireInterface(typeof(IConvaiCharacterAgent))]
        [SerializeField]
        private MonoBehaviour _characterComponent;

        [Tooltip("If true, automatically searches for a ConvaiCharacter in the scene if none is assigned.")]
        [SerializeField]
        private bool _autoFindCharacter = true;

        [Header("Trigger Selection")]
        [Tooltip("Index of the selected trigger in the dropdown (used by custom editor).")]
        [SerializeField]
        private int _selectedTriggerIndex = -1;

        [Tooltip("Unique identifier of the selected trigger.")] [SerializeField]
        private string _triggerId;

        [Tooltip("Name of the trigger (display name).")] [SerializeField]
        private string _triggerName;

        [Tooltip("Read-only saved trigger message fetched from Convai for Inspector display. Not sent by this component.")] [SerializeField]
        private string _triggerMessage;

        [Header("Activation Settings")] [Tooltip("How this trigger should be activated.")] [SerializeField]
        private TriggerActivationMode _activationMode = TriggerActivationMode.Collision;

        [Tooltip("Radius for proximity-based activation.")] [SerializeField]
        private float _proximityRadius = 3f;

        [Tooltip("Delay in seconds before triggering (for TimeBased mode).")] [SerializeField]
        private float _timeDelay;

        [Tooltip("If true, trigger only fires once and then disables.")] [SerializeField]
        private bool _triggerOnce = true;

        [Tooltip("Layer mask for detecting the player.")] [SerializeField]
        private LayerMask _playerLayer = -1;

        [Tooltip("Tag to identify the player GameObject.")] [SerializeField]
        private string _playerTag = "Player";

        [Header("Auto-Recovery Settings")]
        [Tooltip("If true, automatically finds the player GameObject in the scene for proximity detection.")]
        [SerializeField]
        private bool _autoFindPlayer = true;

        [Tooltip("If true, queues the trigger until the character is ready instead of failing silently.")]
        [SerializeField]
        private bool _queueUntilReady = true;

        [Tooltip("Maximum time to wait for character to be ready (seconds). 0 = wait indefinitely.")] [SerializeField]
        private float _maxWaitTime = 30f;

        [Tooltip("If true, automatically resets the trigger when the scene is reloaded.")] [SerializeField]
        private bool _resetOnSceneLoad = true;

        [Header("Diagnostics")]
        [Tooltip("If true, logs detailed diagnostic information to the console.")]
        [SerializeField]
        private bool _enableDiagnostics;

        [Tooltip("If true, validates configuration on Awake and logs warnings for issues.")] [SerializeField]
        private bool _validateOnStart = true;

        [Header("Events")] [Tooltip("Event invoked when the trigger is activated.")] [SerializeField]
        private UnityEvent _onTriggerActivated = new();

        [Tooltip("Event invoked when a player enters the trigger zone.")] [SerializeField]
        private UnityEvent _onPlayerEnterZone = new();

        [Tooltip("Event invoked when a player exits the trigger zone.")] [SerializeField]
        private UnityEvent _onPlayerExitZone = new();

        [Tooltip("Event invoked when the trigger fails to fire due to an error.")] [SerializeField]
        private UnityEvent<string> _onTriggerFailed = new();

        [Tooltip("Event invoked when the trigger is queued waiting for character.")] [SerializeField]
        private UnityEvent _onTriggerQueued = new();

        [Header("Cached Trigger Data")]
        [Tooltip("List of available triggers fetched from backend (used by custom editor).")]
        [SerializeField]
        private List<TriggerData> _availableTriggers = new();

        #endregion

        #region Runtime State

        private Transform _playerTransform;
        private Coroutine _delayedTriggerCoroutine;
        private Coroutine _queuedTriggerCoroutine;
        private readonly List<string> _validationWarnings = new();
        private ConvaiCharacter _cachedConvaiCharacter;

        #endregion

        #region Properties

        /// <summary>Gets the character agent interface from the serialized component.</summary>
        public IConvaiCharacterAgent Character => _characterComponent as IConvaiCharacterAgent;

        /// <summary>Gets the character component reference.</summary>
        public MonoBehaviour CharacterComponent => _characterComponent;

        /// <summary>Gets the ConvaiCharacter component if available (for state checking).</summary>
        public ConvaiCharacter ConvaiCharacterComponent
        {
            get
            {
                if (_cachedConvaiCharacter == null && _characterComponent != null)
                    _cachedConvaiCharacter = _characterComponent as ConvaiCharacter;
                return _cachedConvaiCharacter;
            }
        }

        /// <summary>Gets the selected trigger index.</summary>
        public int SelectedTriggerIndex => _selectedTriggerIndex;

        /// <summary>Gets the trigger ID.</summary>
        public string TriggerId => _triggerId;

        /// <summary>Gets or sets the trigger name.</summary>
        public string TriggerName
        {
            get => _triggerName;
            set => _triggerName = value;
        }

        /// <summary>Gets the saved trigger message fetched from Convai. This value is display-only.</summary>
        public string TriggerMessage => _triggerMessage;

        /// <summary>Gets the activation mode.</summary>
        public TriggerActivationMode ActivationMode => _activationMode;

        /// <summary>Gets the proximity radius.</summary>
        public float ProximityRadius => _proximityRadius;

        /// <summary>Gets the time delay.</summary>
        public float TimeDelay => _timeDelay;

        /// <summary>Gets whether trigger only fires once.</summary>
        public bool TriggerOnce => _triggerOnce;

        /// <summary>Gets the player layer mask.</summary>
        public LayerMask PlayerLayer => _playerLayer;

        /// <summary>Gets the player tag.</summary>
        public string PlayerTag => _playerTag;

        /// <summary>Gets whether the trigger has already fired.</summary>
        public bool HasTriggered { get; private set; }

        /// <summary>Gets whether the player is currently in the zone.</summary>
        public bool PlayerInZone { get; private set; }

        /// <summary>Gets the list of available triggers.</summary>
        public List<TriggerData> AvailableTriggers => _availableTriggers;

        /// <summary>Event invoked when the trigger is activated.</summary>
        public UnityEvent OnTriggerActivated => _onTriggerActivated;

        /// <summary>Event invoked when player enters the zone.</summary>
        public UnityEvent OnPlayerEnterZone => _onPlayerEnterZone;

        /// <summary>Event invoked when player exits the zone.</summary>
        public UnityEvent OnPlayerExitZone => _onPlayerExitZone;

        /// <summary>Event invoked when trigger fails with error message.</summary>
        public UnityEvent<string> OnTriggerFailed => _onTriggerFailed;

        /// <summary>Event invoked when trigger is queued waiting for character.</summary>
        public UnityEvent OnTriggerQueued => _onTriggerQueued;

        /// <summary>Gets the current status of the trigger for diagnostics.</summary>
        public TriggerStatus CurrentStatus { get; private set; } = TriggerStatus.Ready;

        /// <summary>Gets the last error message if trigger failed.</summary>
        public string LastErrorMessage { get; private set; }

        /// <summary>Gets whether a trigger is currently queued waiting for character.</summary>
        public bool IsQueuedForCharacterReady { get; private set; }

        /// <summary>Gets the list of validation warnings from the last validation check.</summary>
        public IReadOnlyList<string> ValidationWarnings => _validationWarnings;

        /// <summary>Gets whether the character is currently in a conversation and ready for triggers.</summary>
        public bool IsCharacterReady
        {
            get
            {
                ConvaiCharacter convaiChar = ConvaiCharacterComponent;
                return convaiChar != null && convaiChar.IsInConversation;
            }
        }

        /// <summary>Gets a formatted diagnostic string for debugging.</summary>
        public string DiagnosticInfo =>
            $"[NarrativeTrigger] Status={CurrentStatus}, HasTriggered={HasTriggered}, " +
            $"TriggerOnce={_triggerOnce}, CharacterReady={IsCharacterReady}, " +
            $"PlayerInZone={PlayerInZone}, Mode={_activationMode}, " +
            $"TriggerName='{_triggerName}', Queued={IsQueuedForCharacterReady}";

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            if (_resetOnSceneLoad) SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (_characterComponent == null && _autoFindCharacter) TryAutoFindCharacter();

            if (_autoFindPlayer && _playerTransform == null) TryAutoFindPlayer();

            if (_validateOnStart) ValidateConfiguration();

            UpdateStatus();

            LogDiagnostic($"Initialized: {DiagnosticInfo}");
        }

        private void OnEnable() => UpdateStatus();

        private void OnDisable()
        {
            CancelQueuedTrigger();

            if (_delayedTriggerCoroutine != null)
            {
                StopCoroutine(_delayedTriggerCoroutine);
                _delayedTriggerCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            if (_resetOnSceneLoad) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (_activationMode != TriggerActivationMode.Proximity) return;
            if (HasTriggered && _triggerOnce) return;

            if (_playerTransform == null)
            {
                if (_autoFindPlayer) TryAutoFindPlayer();

                if (_playerTransform == null) return;
            }

            if (_playerTransform == null) return;

            float sqrDistance = (transform.position - _playerTransform.position).sqrMagnitude;
            float sqrRadius = _proximityRadius * _proximityRadius;
            if (sqrDistance <= sqrRadius) TryInvokeTrigger();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsValidPlayer(other.gameObject)) return;

            PlayerInZone = true;
            _playerTransform = other.transform;
            _onPlayerEnterZone?.Invoke();

            LogDiagnostic($"Player entered trigger zone: {other.gameObject.name}");

            switch (_activationMode)
            {
                case TriggerActivationMode.Collision:
                    TryInvokeTrigger();
                    break;
                case TriggerActivationMode.TimeBased:
                    if (_delayedTriggerCoroutine != null) StopCoroutine(_delayedTriggerCoroutine);
                    _delayedTriggerCoroutine = StartCoroutine(DelayedTriggerCoroutine());
                    break;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsValidPlayer(other.gameObject)) return;

            PlayerInZone = false;
            _playerTransform = null;
            _onPlayerExitZone?.Invoke();

            LogDiagnostic($"Player exited trigger zone: {other.gameObject.name}");

            if (_activationMode == TriggerActivationMode.TimeBased && _delayedTriggerCoroutine != null)
            {
                StopCoroutine(_delayedTriggerCoroutine);
                _delayedTriggerCoroutine = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_resetOnSceneLoad)
            {
                LogDiagnostic("Scene loaded, resetting trigger state");
                ResetTrigger();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_activationMode == TriggerActivationMode.Proximity)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, _proximityRadius);
                Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.1f);
                Gizmos.DrawSphere(transform.position, _proximityRadius);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        ///     Sends the configured trigger to the assigned character.
        ///     This is the main method to invoke the trigger programmatically.
        ///     If the character is not ready and queueUntilReady is enabled, the trigger will be queued.
        /// </summary>
        /// <returns>True if trigger was sent or queued successfully, false if it failed.</returns>
        public bool InvokeTrigger()
        {
            LogDiagnostic($"InvokeTrigger called: {DiagnosticInfo}");

            if (Character == null)
            {
                if (_autoFindCharacter) TryAutoFindCharacter();

                if (Character == null)
                {
                    string error =
                        "Character is not set. Assign a ConvaiCharacter in the Inspector or enable Auto Find Character.";
                    HandleTriggerError(error);
                    return false;
                }
            }

            if (HasTriggered && _triggerOnce)
            {
                string warning =
                    "Trigger already fired and TriggerOnce is enabled. Call ResetTrigger() to allow it to fire again.";
                CurrentStatus = TriggerStatus.AlreadyFired;
                ConvaiLogger.Warning($"{warning}", LogCategory.Narrative);
                return false;
            }

            if (string.IsNullOrEmpty(_triggerName))
            {
                string error =
                    "No trigger name configured. Select a saved trigger from the dropdown or set one programmatically.";
                HandleTriggerError(error);
                return false;
            }

            if (!IsCharacterReady)
            {
                LogDiagnostic($"Character not ready for trigger. IsCharacterReady={IsCharacterReady}");

                if (_queueUntilReady) return QueueTriggerUntilReady();

                string error = $"Character '{Character.CharacterName}' is not in conversation. " +
                               "Enable 'Queue Until Ready' or ensure the character is connected before triggering.";
                HandleTriggerError(error);
                return false;
            }

            return SendTriggerToCharacter();
        }

        /// <summary>
        ///     Attempts to invoke the trigger, respecting the TriggerOnce setting.
        /// </summary>
        /// <returns>True if trigger was sent or queued successfully.</returns>
        public bool TryInvokeTrigger()
        {
            if (HasTriggered && _triggerOnce)
            {
                LogDiagnostic("TryInvokeTrigger: Skipped - already triggered with TriggerOnce enabled");
                return false;
            }

            return InvokeTrigger();
        }

        /// <summary>
        ///     Resets the trigger so it can fire again.
        ///     Also cancels any queued trigger attempts.
        /// </summary>
        public void ResetTrigger()
        {
            HasTriggered = false;
            IsQueuedForCharacterReady = false;
            CancelQueuedTrigger();
            UpdateStatus();
            LogDiagnostic("Trigger reset - can fire again");
        }

        /// <summary>
        ///     Cancels any queued trigger that is waiting for character ready.
        /// </summary>
        public void CancelQueuedTrigger()
        {
            if (_queuedTriggerCoroutine != null)
            {
                StopCoroutine(_queuedTriggerCoroutine);
                _queuedTriggerCoroutine = null;
                IsQueuedForCharacterReady = false;
                UpdateStatus();
                LogDiagnostic("Queued trigger cancelled");
            }
        }

        /// <summary>
        ///     Forces a re-validation of the trigger configuration and logs any issues.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        public bool ValidateConfiguration()
        {
            _validationWarnings.Clear();
            bool isValid = true;

            if (_characterComponent == null)
            {
                _validationWarnings.Add("Character reference is not assigned.");
                isValid = false;
            }
            else if (Character == null)
            {
                _validationWarnings.Add("Assigned component does not implement IConvaiCharacterAgent.");
                isValid = false;
            }

            if (string.IsNullOrEmpty(_triggerName))
            {
                _validationWarnings.Add("No trigger name configured.");
                isValid = false;
            }

            if (_activationMode == TriggerActivationMode.Collision ||
                _activationMode == TriggerActivationMode.TimeBased)
                ValidateColliderSetup();

            ValidatePlayerDetection();

            if (_validationWarnings.Count > 0)
            {
                CurrentStatus = TriggerStatus.ConfigurationError;
                foreach (string warning in _validationWarnings)
                {
                    ConvaiLogger.Warning($"Validation: {warning}",
                        LogCategory.Narrative);
                }
            }

            return isValid;
        }

        /// <summary>
        ///     Prints detailed diagnostic information to the console.
        /// </summary>
        public void PrintDiagnostics()
        {
            ConvaiLogger.Debug("=== DIAGNOSTICS ===\n" +
                               $"  GameObject: {gameObject.name}\n" +
                               $"  Status: {CurrentStatus}\n" +
                               $"  Has Triggered: {HasTriggered}\n" +
                               $"  Trigger Once: {_triggerOnce}\n" +
                               $"  Trigger Name: '{_triggerName}'\n" +
                               $"  Trigger ID: '{_triggerId}'\n" +
                               $"  Activation Mode: {_activationMode}\n" +
                               $"  Character Assigned: {(_characterComponent != null ? Character?.CharacterName ?? "Invalid" : "None")}\n" +
                               $"  Character Ready: {IsCharacterReady}\n" +
                               $"  Player In Zone: {PlayerInZone}\n" +
                               $"  Player Transform: {(_playerTransform != null ? _playerTransform.name : "None")}\n" +
                               $"  Queued For Ready: {IsQueuedForCharacterReady}\n" +
                               $"  Last Error: {LastErrorMessage ?? "None"}\n" +
                               $"  Validation Warnings: {_validationWarnings.Count}\n" +
                               "===========================", LogCategory.Narrative);
        }

        /// <summary>
        ///     Sets the character agent for this trigger.
        /// </summary>
        /// <param name="character">The character agent to target.</param>
        public void SetCharacter(IConvaiCharacterAgent character)
        {
            if (character is MonoBehaviour mb)
            {
                _characterComponent = mb;
                _cachedConvaiCharacter = mb as ConvaiCharacter;
                LogDiagnostic($"Character set to: {character.CharacterName}");
            }
        }

        /// <summary>
        ///     Sets the trigger configuration.
        /// </summary>
        /// <param name="triggerId">The trigger ID.</param>
        /// <param name="triggerName">The trigger name.</param>
        public void SetTrigger(string triggerId, string triggerName)
        {
            _triggerId = triggerId;
            _triggerName = NormalizeTriggerName(triggerName);
            _triggerMessage = string.Empty;
            LogDiagnostic($"Trigger configured: name='{triggerName}', id='{triggerId}'");
        }

        /// <summary>
        ///     Sets the trigger name.
        /// </summary>
        /// <param name="triggerName">The trigger name to use.</param>
        public void SetTriggerName(string triggerName) => _triggerName = NormalizeTriggerName(triggerName);

        /// <summary>
        ///     Sets the activation mode.
        /// </summary>
        /// <param name="mode">The activation mode.</param>
        public void SetActivationMode(TriggerActivationMode mode) => _activationMode = mode;

        /// <summary>
        ///     Sets the proximity radius for Proximity mode.
        /// </summary>
        /// <param name="radius">The detection radius.</param>
        public void SetProximityRadius(float radius) => _proximityRadius = Mathf.Max(0f, radius);

        /// <summary>
        ///     Sets the time delay for TimeBased mode.
        /// </summary>
        /// <param name="delay">The delay in seconds.</param>
        public void SetTimeDelay(float delay) => _timeDelay = Mathf.Max(0f, delay);

        /// <summary>
        ///     Gets the character ID from the assigned character.
        /// </summary>
        /// <returns>The character ID, or null if not available.</returns>
        public string GetCharacterId() => Character?.CharacterId;

        /// <summary>
        ///     Updates the available triggers list (called from editor).
        /// </summary>
        /// <param name="triggers">List of triggers from backend.</param>
        public void SetAvailableTriggers(List<TriggerData> triggers)
        {
            string selectedTriggerId = _triggerId;
            _availableTriggers = triggers ?? new List<TriggerData>();

            if (!string.IsNullOrEmpty(selectedTriggerId))
            {
                for (int i = 0; i < _availableTriggers.Count; i++)
                {
                    if (!string.Equals(_availableTriggers[i].TriggerId, selectedTriggerId, StringComparison.Ordinal))
                        continue;

                    SelectTriggerByIndex(i);
                    return;
                }
            }

            ClearSelectedTrigger();
        }

        /// <summary>
        ///     Selects a trigger by index from the available triggers list.
        /// </summary>
        /// <param name="index">The index in the available triggers list.</param>
        public void SelectTriggerByIndex(int index)
        {
            if (_availableTriggers == null || index < 0 || index >= _availableTriggers.Count)
            {
                ClearSelectedTrigger();
                return;
            }

            _selectedTriggerIndex = index;
            TriggerData trigger = _availableTriggers[index];
            _triggerId = trigger.TriggerId;
            _triggerName = NormalizeTriggerName(trigger.TriggerName);
            _triggerMessage = string.Empty;
        }

        /// <summary>
        ///     Manually sets the player transform for proximity detection.
        ///     Useful when auto-find doesn't work for your setup.
        /// </summary>
        /// <param name="playerTransform">The player's transform.</param>
        public void SetPlayerTransform(Transform playerTransform)
        {
            _playerTransform = playerTransform;
            LogDiagnostic(
                $"Player transform manually set to: {(playerTransform != null ? playerTransform.name : "null")}");
        }

        /// <summary>
        ///     Enables or disables diagnostic logging.
        /// </summary>
        /// <param name="enabled">Whether to enable diagnostics.</param>
        public void SetDiagnosticsEnabled(bool enabled) => _enableDiagnostics = enabled;

        #endregion

        #region Private Methods

        /// <summary>
        ///     Sends the trigger to the character. Called after all validation passes.
        /// </summary>
        private bool SendTriggerToCharacter()
        {
            try
            {
                bool accepted = Character is ConvaiCharacter convaiCharacter
                    ? convaiCharacter.NarrativeDesign.InvokeTrigger(_triggerName)
                    : InvokeLegacyCharacterTrigger();

                if (!accepted) return false;

                HasTriggered = true;
                IsQueuedForCharacterReady = false;
                CurrentStatus = _triggerOnce ? TriggerStatus.AlreadyFired : TriggerStatus.Ready;
                _onTriggerActivated?.Invoke();

                ConvaiLogger.Info(
                    $"Trigger '{_triggerName}' invoked successfully on character '{Character.CharacterName}'.",
                    LogCategory.Narrative);
                return true;
            }
            catch (Exception ex)
            {
                HandleTriggerError($"Exception while sending trigger: {ex.Message}");
                return false;
            }
        }

        private bool InvokeLegacyCharacterTrigger()
        {
            if (Character == null) return false;

            if (string.IsNullOrEmpty(_triggerName))
                return false;

            Character.SendTrigger(_triggerName);
            return true;
        }

        private void ClearSelectedTrigger()
        {
            _selectedTriggerIndex = -1;
            _triggerId = string.Empty;
            _triggerName = string.Empty;
            _triggerMessage = string.Empty;
        }

        private static string NormalizeTriggerName(string triggerName) => triggerName?.Trim() ?? string.Empty;

        /// <summary>
        ///     Queues the trigger to fire when the character becomes ready.
        /// </summary>
        private bool QueueTriggerUntilReady()
        {
            if (IsQueuedForCharacterReady)
            {
                LogDiagnostic("Trigger already queued, ignoring duplicate queue request");
                return true;
            }

            IsQueuedForCharacterReady = true;
            CurrentStatus = TriggerStatus.QueuedWaitingForCharacter;
            _onTriggerQueued?.Invoke();

            ConvaiLogger.Debug(
                $"Trigger '{_triggerName}' queued. Waiting for character to be ready (max {_maxWaitTime}s).",
                LogCategory.Narrative);

            if (_queuedTriggerCoroutine != null) StopCoroutine(_queuedTriggerCoroutine);
            _queuedTriggerCoroutine = StartCoroutine(WaitForCharacterReadyCoroutine());

            return true;
        }

        /// <summary>
        ///     Coroutine that waits for the character to be ready and then sends the trigger.
        /// </summary>
        private IEnumerator WaitForCharacterReadyCoroutine()
        {
            float elapsed = 0f;
            float checkInterval = 0.25f;

            LogDiagnostic("Starting wait for character ready...");

            while (!IsCharacterReady)
            {
                yield return new WaitForSeconds(checkInterval);
                elapsed += checkInterval;

                if (_maxWaitTime > 0 && elapsed >= _maxWaitTime)
                {
                    string error = $"Timed out waiting for character to be ready after {_maxWaitTime} seconds.";
                    HandleTriggerError(error);
                    IsQueuedForCharacterReady = false;
                    _queuedTriggerCoroutine = null;
                    yield break;
                }

                if (!IsQueuedForCharacterReady)
                {
                    LogDiagnostic("Queued trigger was cancelled while waiting");
                    _queuedTriggerCoroutine = null;
                    yield break;
                }
            }

            LogDiagnostic($"Character became ready after {elapsed:F1}s, sending queued trigger");
            _queuedTriggerCoroutine = null;
            SendTriggerToCharacter();
        }

        /// <summary>
        ///     Handles trigger errors by logging and invoking error events.
        /// </summary>
        private void HandleTriggerError(string error)
        {
            LastErrorMessage = error;
            CurrentStatus = TriggerStatus.ConfigurationError;
            ConvaiLogger.Error($"{error}", LogCategory.Narrative);
            _onTriggerFailed?.Invoke(error);
        }

        /// <summary>
        ///     Updates the current status based on internal state.
        /// </summary>
        private void UpdateStatus()
        {
            if (!enabled || !gameObject.activeInHierarchy)
                CurrentStatus = TriggerStatus.Disabled;
            else if (HasTriggered && _triggerOnce)
                CurrentStatus = TriggerStatus.AlreadyFired;
            else if (IsQueuedForCharacterReady)
                CurrentStatus = TriggerStatus.QueuedWaitingForCharacter;
            else if (_validationWarnings.Count > 0)
                CurrentStatus = TriggerStatus.ConfigurationError;
            else
                CurrentStatus = TriggerStatus.Ready;
        }

        /// <summary>
        ///     Attempts to automatically find a ConvaiCharacter.
        ///     First checks parent hierarchy, then queries the manager's owned characters.
        /// </summary>
        private void TryAutoFindCharacter()
        {
            if (_characterComponent != null) return;

            // First, check parent hierarchy
            var localCharacter = GetComponentInParent<ConvaiCharacter>();
            if (localCharacter != null)
            {
                _characterComponent = localCharacter;
                _cachedConvaiCharacter = localCharacter;
                LogDiagnostic($"Auto-found character in parent: {localCharacter.name}");
                return;
            }

            // Then, query the manager's owned characters (no scene scanning)
            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null)
            {
                LogDiagnostic("No ConvaiManager available for auto-assignment");
                return;
            }

            IReadOnlyList<ConvaiCharacter> characters = manager.Characters;
            if (characters.Count == 1)
            {
                _characterComponent = characters[0];
                _cachedConvaiCharacter = characters[0];
                ConvaiLogger.Warning(
                    $"Auto-assigned character '{characters[0].name}'. " +
                    "Consider assigning it explicitly in the Inspector.", LogCategory.Narrative);
            }
            else if (characters.Count > 1)
            {
                ConvaiLogger.Warning(
                    $"Multiple ConvaiCharacters found ({characters.Count}). " +
                    "Cannot auto-assign. Please assign one explicitly.", LogCategory.Narrative);
            }
            else
                LogDiagnostic("No ConvaiCharacter found via ConvaiManager for auto-assignment");
        }

        /// <summary>
        ///     Attempts to automatically find the player in the scene.
        /// </summary>
        private void TryAutoFindPlayer()
        {
            if (_playerTransform != null) return;

            if (!string.IsNullOrEmpty(_playerTag))
            {
                var playerByTag = GameObject.FindGameObjectWithTag(_playerTag);
                if (playerByTag != null)
                {
                    _playerTransform = playerByTag.transform;
                    LogDiagnostic($"Auto-found player by tag '{_playerTag}': {playerByTag.name}");
                    return;
                }
            }

            string[] commonPlayerNames = { "Player", "PlayerCharacter", "FPSController", "ThirdPersonController" };
            foreach (string name in commonPlayerNames)
            {
                GameObject playerByName = GameObject.Find(name);
                if (playerByName != null && IsValidPlayer(playerByName))
                {
                    _playerTransform = playerByName.transform;
                    LogDiagnostic($"Auto-found player by name: {playerByName.name}");
                    return;
                }
            }

            if (Camera.main != null)
            {
                Transform cameraParent = Camera.main.transform.parent;
                if (cameraParent != null)
                {
                    _playerTransform = cameraParent;
                    LogDiagnostic($"Auto-found player via main camera parent: {cameraParent.name}");
                }
            }
        }

        /// <summary>
        ///     Validates collider setup for collision-based activation modes.
        /// </summary>
        private void ValidateColliderSetup()
        {
            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                _validationWarnings.Add($"No Collider found on '{gameObject.name}'. " +
                                        $"{_activationMode} mode requires a Collider with 'Is Trigger' enabled.");
            }
            else if (!collider.isTrigger)
            {
                _validationWarnings.Add($"Collider on '{gameObject.name}' has 'Is Trigger' disabled. " +
                                        "Enable 'Is Trigger' for collision detection to work.");
            }

            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                LogDiagnostic(
                    "No Rigidbody on trigger object. Ensure the player has a Rigidbody for OnTriggerEnter to work.");
            }
        }

        /// <summary>
        ///     Validates player detection settings.
        /// </summary>
        private void ValidatePlayerDetection()
        {
            if (!string.IsNullOrEmpty(_playerTag))
            {
                try
                {
                    GameObject.FindGameObjectWithTag(_playerTag);
                }
                catch (UnityException)
                {
                    _validationWarnings.Add($"Player tag '{_playerTag}' is not defined in the Tag Manager. " +
                                            "Add the tag or change the Player Tag setting.");
                }
            }

            if (_playerLayer == 0)
                _validationWarnings.Add("Player Layer is set to 'Nothing'. No objects will be detected.");
        }

        private bool IsValidPlayer(GameObject obj)
        {
            if (!string.IsNullOrEmpty(_playerTag))
            {
                try
                {
                    if (!obj.CompareTag(_playerTag)) return false;
                }
                catch (UnityException)
                {
                    LogDiagnostic($"Tag '{_playerTag}' does not exist. Skipping tag check.");
                }
            }

            if (_playerLayer != -1 && (_playerLayer & (1 << obj.layer)) == 0) return false;

            return true;
        }

        private IEnumerator DelayedTriggerCoroutine()
        {
            LogDiagnostic($"Starting delayed trigger coroutine (delay={_timeDelay}s)");
            yield return new WaitForSeconds(_timeDelay);

            if (PlayerInZone)
            {
                LogDiagnostic("Player still in zone after delay, invoking trigger");
                TryInvokeTrigger();
            }
            else
                LogDiagnostic("Player left zone during delay, skipping trigger");

            _delayedTriggerCoroutine = null;
        }

        /// <summary>
        ///     Logs diagnostic information if diagnostics are enabled.
        /// </summary>
        private void LogDiagnostic(string message)
        {
            if (_enableDiagnostics)
            {
                ConvaiLogger.Debug($"[{gameObject.name}] {message}",
                    LogCategory.Narrative);
            }
        }

        #endregion
    }
}
