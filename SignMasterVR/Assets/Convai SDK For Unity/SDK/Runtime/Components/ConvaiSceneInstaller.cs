using System.Collections.Generic;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Explicit scene configuration component for Convai SDK.
    ///     Registers characters and players with the AgentRegistry on scene load.
    ///     Preferred over scene discovery/reflection for IL2CPP compatibility.
    /// </summary>
    [AddComponentMenu("Convai/Convai Scene Installer")]
    public class ConvaiSceneInstaller : MonoBehaviour
    {
        [Tooltip("Characters to register with the agent registry.")]
        [SerializeField]
        [ConvaiInspectorSection("Scene Agents")]
        private List<ConvaiCharacter> _characters = new();

        [Tooltip("Players to register with the agent registry. Usually just one.")]
        [SerializeField]
        [ConvaiInspectorSection("Scene Agents")]
        private List<ConvaiPlayer> _players = new();

        [Tooltip("Characters owned by the local player. Leave empty to use default ownership.")]
        [SerializeField]
        [ConvaiInspectorSection("Ownership Configuration")]
        private List<ConvaiCharacter> _ownedCharacters = new();

        /// <summary>All configured characters.</summary>
        public IReadOnlyList<ConvaiCharacter> Characters => _characters;

        /// <summary>All configured players.</summary>
        public IReadOnlyList<ConvaiPlayer> Players => _players;

        /// <summary>Characters explicitly owned by local player.</summary>
        public IReadOnlyList<ConvaiCharacter> OwnedCharacters => _ownedCharacters;

        /// <summary>Whether this installer has valid configuration.</summary>
        public bool IsValid => _characters.Count > 0 || _players.Count > 0;

        private void Awake()
        {
        }

        private void OnDestroy() { }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Warn about empty configuration
            if (_characters.Count == 0 && _players.Count == 0)
            {
                Debug.LogWarning(
                    $"[ConvaiSceneInstaller] {gameObject.name}: No characters or players configured. " +
                    "Add references or remove this component.", this);
            }

            // Warn about null entries
            for (int i = _characters.Count - 1; i >= 0; i--)
            {
                if (_characters[i] == null)
                {
                    Debug.LogWarning(
                        $"[ConvaiSceneInstaller] {gameObject.name}: Null character at index {i}.", this);
                }
            }

            for (int i = _players.Count - 1; i >= 0; i--)
            {
                if (_players[i] == null)
                {
                    Debug.LogWarning(
                        $"[ConvaiSceneInstaller] {gameObject.name}: Null player at index {i}.", this);
                }
            }

            // Validate owned characters are in the characters list
            foreach (ConvaiCharacter owned in _ownedCharacters)
            {
                if (owned != null && !_characters.Contains(owned))
                {
                    Debug.LogWarning(
                        $"[ConvaiSceneInstaller] {gameObject.name}: Owned character '{owned.name}' is not in the characters list.",
                        this);
                }
            }
        }
#endif
    }
}
