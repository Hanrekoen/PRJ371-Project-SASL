using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEngine;

namespace Convai.Runtime.Configuration
{
    /// <summary>
    ///     Optional shared configuration asset for scene setup.
    ///     Can be used with ConvaiSceneInstaller for prefab-based or ID-based scene configuration.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiSceneConfig", menuName = "Convai/Scene Config")]
    public class ConvaiSceneConfig : ScriptableObject
    {
        [Tooltip("Character IDs to auto-connect when the scene loads.")]
        [SerializeField]
        [ConvaiInspectorSection("Character Configuration")]
        private List<string> _characterIds = new();

        [Tooltip("Character prefabs to instantiate when the scene loads.")]
        [SerializeField]
        [ConvaiInspectorSection("Character Configuration")]
        private List<GameObject> _characterPrefabs = new();

        [Tooltip("Default owner ID for characters in this scene.")]
        [SerializeField]
        [ConvaiInspectorSection("Default Settings")]
        private string _defaultOwnerId = "";

        [Tooltip("Auto-connect characters when the scene loads.")]
        [SerializeField]
        [ConvaiInspectorSection("Default Settings")]
        private bool _autoConnect = true;

        /// <summary>Character IDs configured for this scene.</summary>
        public IReadOnlyList<string> CharacterIds => _characterIds;

        /// <summary>Character prefabs to instantiate.</summary>
        public IReadOnlyList<GameObject> CharacterPrefabs => _characterPrefabs;

        /// <summary>Default owner ID for characters.</summary>
        public string DefaultOwnerId => _defaultOwnerId;

        /// <summary>Whether to auto-connect characters.</summary>
        public bool AutoConnect => _autoConnect;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Validate character IDs
            for (int i = 0; i < _characterIds.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(_characterIds[i]))
                    Debug.LogWarning($"[ConvaiSceneConfig] {name}: Empty character ID at index {i}.", this);
            }

            // Validate prefabs have ConvaiCharacter
            for (int i = 0; i < _characterPrefabs.Count; i++)
            {
                GameObject prefab = _characterPrefabs[i];
                if (prefab != null && prefab.GetComponent<ConvaiCharacter>() == null)
                {
                    Debug.LogWarning(
                        $"[ConvaiSceneConfig] {name}: Prefab '{prefab.name}' at index {i} does not have a ConvaiCharacter component.",
                        this);
                }
            }
        }
#endif
    }
}
