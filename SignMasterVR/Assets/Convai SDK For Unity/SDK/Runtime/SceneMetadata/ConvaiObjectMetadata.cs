using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     UE-style world object surface: descriptive metadata for scene grounding plus optional
    ///     tracked properties that feed character dynamic context.
    /// </summary>
    [AddComponentMenu("Convai/World Object")]
    public class ConvaiObjectMetadata : MonoBehaviour
    {
        [Tooltip("Display name for this object that will be sent to the AI")]
        [SerializeField]
        [ConvaiInspectorSection("Object Metadata")]
        private string _objectName = "";

        [Tooltip("Description of this object that will be sent to the AI")] [TextArea(2, 4)] [SerializeField]
        [ConvaiInspectorSection("Object Metadata")]
        private string _objectDescription = "";

        [Tooltip("Whether this object's metadata should be included in scene metadata collection")]
        [SerializeField]
        [ConvaiInspectorSection("Settings")]
        private bool _includeInMetadata = true;

        [Tooltip("Runtime properties exposed to all characters as dynamic context state keys.")]
        [SerializeField]
        [ConvaiInspectorSection("Tracked Properties")]
        private List<ConvaiTrackedContextProperty> _trackedProperties = new();

        [ReadOnly]
        [Tooltip("Shows the current registration status (read-only)")]
        [SerializeField]
        [ConvaiInspectorSection("Status")]
        private bool _isRegistered;

        private readonly Dictionary<string, string> _trackedValueCache = new(StringComparer.Ordinal);

        /// <summary>
        ///     The display name for this object
        /// </summary>
        public string ObjectName
        {
            get => _objectName;
            set
            {
                if (_objectName == value) return;

                _objectName = value;
                MarkMetadataDirtyIfRegistered();
            }
        }

        /// <summary>
        ///     The description for this object
        /// </summary>
        public string ObjectDescription
        {
            get => _objectDescription;
            set
            {
                if (_objectDescription == value) return;

                _objectDescription = value;
                MarkMetadataDirtyIfRegistered();
            }
        }

        /// <summary>
        ///     Whether this object should be included in metadata collection
        /// </summary>
        public bool IncludeInMetadata
        {
            get => _includeInMetadata;
            set
            {
                if (_includeInMetadata == value) return;

                _includeInMetadata = value;
                MarkMetadataDirtyIfRegistered();
            }
        }

        /// <summary>
        ///     Whether this component is currently registered with the metadata registry
        /// </summary>
        public bool IsRegistered => _isRegistered;

        /// <summary>
        ///     Whether this metadata is valid (has required fields)
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(_objectName);

        private void OnEnable() => RegisterWithRegistry();

        private void OnDisable() => UnregisterFromRegistry();

        private void OnDestroy() => UnregisterFromRegistry();

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_objectName) && gameObject != null) _objectName = gameObject.name;

            List<string> errors = GetValidationErrors();
            if (errors.Count > 0)
            {
                ConvaiLogger.Warning(
                    $"Validation errors on {gameObject.name}: {string.Join(", ", errors)}",
                    LogCategory.SDK);
            }
        }

        /// <summary>
        ///     Updates one tracked property and fans the change out to all connected characters.
        /// </summary>
        public void SetTrackedPropertyValue(
            string propertyName,
            string value,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent)
        {
            if (!TryValidateTrackedPropertyName(propertyName) || value == null) return;

            string stateKey = BuildStateKey(propertyName);
            TrySetTrackedValue(stateKey, value, reaction, ResolveAgentRegistry());
        }

        /// <summary>
        ///     Polls tracked property definitions and pushes changes to all characters.
        /// </summary>
        internal void EvaluateTrackedProperties(IAgentRegistry agentRegistry)
        {
            if (!IsValid || _trackedProperties == null || _trackedProperties.Count == 0) return;

            for (int i = 0; i < _trackedProperties.Count; i++)
            {
                ConvaiTrackedContextProperty property = _trackedProperties[i];
                if (property == null || string.IsNullOrWhiteSpace(property.PropertyName)) continue;

                string stateKey = BuildStateKey(property.PropertyName);
                string currentValue;
                if (property.TryReadRuntimeValue(out string runtimeValue))
                {
                    currentValue = runtimeValue;
                }
                else if (!_trackedValueCache.ContainsKey(stateKey))
                {
                    currentValue = property.InitialValue;
                }
                else
                {
                    continue;
                }

                TrySetTrackedValue(stateKey, currentValue, property.Reaction, agentRegistry);
            }
        }

        /// <summary>
        ///     Seeds tracked property values onto one character, typically at session start.
        /// </summary>
        internal void SeedTrackedStateOntoCharacter(ConvaiCharacter character)
        {
            if (character == null || !IsValid || _trackedProperties == null) return;

            for (int i = 0; i < _trackedProperties.Count; i++)
            {
                ConvaiTrackedContextProperty property = _trackedProperties[i];
                if (property == null || string.IsNullOrWhiteSpace(property.PropertyName)) continue;

                string stateKey = BuildStateKey(property.PropertyName);
                string value = _trackedValueCache.TryGetValue(stateKey, out string cachedValue)
                    ? cachedValue
                    : property.ResolveInitialValue();

                _trackedValueCache[stateKey] = value;
                character.DynamicContext.SetState(stateKey, value, ConvaiRespondMode.Silent);
            }
        }

        /// <summary>
        ///     Builds the dynamic-context state key for a tracked property on this object.
        /// </summary>
        public string BuildStateKey(string propertyName) => $"{ObjectName?.Trim()}.{propertyName?.Trim()}";

        /// <summary>
        ///     Validates the metadata and returns any validation errors
        /// </summary>
        /// <returns>List of validation error messages, empty if valid</returns>
        public List<string> GetValidationErrors()
        {
            List<string> errors = new();

            if (string.IsNullOrWhiteSpace(_objectName)) errors.Add("Object Name is required");

            if ((_objectName?.Length ?? 0) > 50) errors.Add("Object Name should be 50 characters or less");

            if ((_objectDescription?.Length ?? 0) > 200)
                errors.Add("Object Description should be 200 characters or less");

            return errors;
        }

        /// <summary>
        ///     Creates a SceneMetadata object from this component's data
        /// </summary>
        /// <returns>SceneMetadata object for RTVI messaging</returns>
        public Infrastructure.Protocol.Messages.SceneMetadata ToSceneMetadata() =>
            new() { Name = _objectName?.Trim(), Description = _objectDescription ?? string.Empty };

        private void RegisterWithRegistry()
        {
            if (_isRegistered) return;

            ConvaiMetadataRegistry.RegisterMetadata(this);
            _isRegistered = true;
            InitializeTrackedValueCache();
            ConvaiWorldObjectPollDriver.EnsureStarted();
            NotifyCharactersAfterRegistration();
        }

        private void UnregisterFromRegistry()
        {
            if (!_isRegistered) return;

            RemoveTrackedStateFromAllCharacters();
            ConvaiMetadataRegistry.UnregisterMetadata(this);
            _isRegistered = false;
            ConvaiWorldObjectPollDriver.StopIfIdle();
        }

        private void InitializeTrackedValueCache()
        {
            _trackedValueCache.Clear();
            if (_trackedProperties == null) return;

            for (int i = 0; i < _trackedProperties.Count; i++)
            {
                ConvaiTrackedContextProperty property = _trackedProperties[i];
                if (property == null || string.IsNullOrWhiteSpace(property.PropertyName)) continue;

                string stateKey = BuildStateKey(property.PropertyName);
                _trackedValueCache[stateKey] = property.ResolveInitialValue();
            }
        }

        private void NotifyCharactersAfterRegistration()
        {
            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null || !manager.TryGetAgentRegistry(out IAgentRegistry registry)) return;

            IReadOnlyList<IConvaiCharacterAgent> characters = registry.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] is not ConvaiCharacter character) continue;

                SeedTrackedStateOntoCharacter(character);
                character.MarkPendingSceneMetadataSync();
            }
        }

        private bool TrySetTrackedValue(
            string stateKey,
            string value,
            ConvaiRespondMode reaction,
            IAgentRegistry agentRegistry)
        {
            if (_trackedValueCache.TryGetValue(stateKey, out string existingValue) && existingValue == value)
                return false;

            _trackedValueCache[stateKey] = value;
            BroadcastStateToCharacters(agentRegistry, stateKey, value, reaction);
            return true;
        }

        private static void BroadcastStateToCharacters(
            IAgentRegistry agentRegistry,
            string stateKey,
            string value,
            ConvaiRespondMode reaction)
        {
            if (agentRegistry?.Characters == null) return;

            IReadOnlyList<IConvaiCharacterAgent> characters = agentRegistry.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] is ConvaiCharacter character)
                    character.DynamicContext.SetState(stateKey, value, reaction);
            }
        }

        private void RemoveTrackedStateFromAllCharacters()
        {
            IAgentRegistry registry = ResolveAgentRegistry();
            if (registry == null) return;

            IReadOnlyList<IConvaiCharacterAgent> characters = registry.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] is not ConvaiCharacter character) continue;

                foreach (string stateKey in _trackedValueCache.Keys)
                    character.DynamicContext.RemoveState(stateKey);
            }
        }

        private static bool TryValidateTrackedPropertyName(string propertyName) =>
            !string.IsNullOrWhiteSpace(propertyName);

        private void MarkMetadataDirtyIfRegistered()
        {
            if (_isRegistered)
                ConvaiMetadataRegistry.MarkSceneMetadataDirty();
        }

        private static IAgentRegistry ResolveAgentRegistry()
        {
            ConvaiManager manager = ConvaiManager.ActiveManager;
            return manager != null && manager.TryGetAgentRegistry(out IAgentRegistry registry)
                ? registry
                : null;
        }
    }

    /// <summary>
    ///     Custom attribute to make fields read-only in the inspector.
    ///     The PropertyDrawer (ReadOnlyDrawer) is defined in the Editor assembly.
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute
    {
    }
}
