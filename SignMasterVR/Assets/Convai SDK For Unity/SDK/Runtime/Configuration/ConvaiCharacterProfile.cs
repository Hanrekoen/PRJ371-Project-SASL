using Convai.Runtime.Actions;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Runtime
{
    /// <summary>
    ///     Reusable character identity and default interaction settings.
    /// </summary>
    [MovedFrom(true, "Convai.Runtime", "Convai.Runtime",
        "ConvaiAgentDefinition")]
    [CreateAssetMenu(fileName = "ConvaiCharacterProfile", menuName = "Convai/Character Profile")]
    public sealed class ConvaiCharacterProfile : ScriptableObject
    {
        [SerializeField] [ConvaiInspectorSection("Identity")] [Tooltip("The unique character id from the Convai dashboard.")]
        private string _characterId = string.Empty;

        [SerializeField] [ConvaiInspectorSection("Identity")] [Tooltip("Display name used in transcripts and debug output.")]
        private string _characterName = string.Empty;

        [SerializeField] [ConvaiInspectorSection("Identity")] [Tooltip("Default nametag color for this character.")]
        private Color _nameTagColor = Color.white;

        [SerializeField] [ConvaiInspectorSection("Defaults")] [Tooltip("Whether remote audio should start enabled for this character.")]
        private bool _enableRemoteAudioOnStart = true;

        [SerializeField] [ConvaiInspectorSection("Defaults")] [Tooltip("Whether this character should attempt session resume on reconnect.")]
        private bool _enableSessionResume = true;

        public string CharacterId => _characterId ?? string.Empty;
        public string CharacterName => _characterName ?? string.Empty;
        public Color NameTagColor => _nameTagColor;
        public bool EnableRemoteAudioOnStart => _enableRemoteAudioOnStart;
        public bool EnableSessionResume => _enableSessionResume;
    }
}
