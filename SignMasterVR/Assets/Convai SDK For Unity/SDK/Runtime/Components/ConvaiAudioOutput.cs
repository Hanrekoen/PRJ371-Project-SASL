using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Optional companion component for audio output. Auto-discovers ConvaiCharacter.
    ///     Handles AudioSource configuration and playback control for character speech.
    /// </summary>
    /// <remarks>
    ///     This component follows the composition pattern:
    ///     - Must be attached to the same GameObject as ConvaiCharacter
    ///     - Auto-discovers and subscribes to ConvaiCharacter events
    ///     - Manages AudioSource volume and mute state
    ///     - Registers with room audio service for track routing
    /// </remarks>
    [AddComponentMenu("Convai/Convai Audio Output")]
    [RequireComponent(typeof(ConvaiCharacter))]
    [RequireComponent(typeof(AudioSource))]
    public class ConvaiAudioOutput : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField]
        [ConvaiInspectorSection("Audio Settings")]
        [Range(0f, 1f)]
        [Tooltip("Output volume for this character's speech, from silent (0) to full (1).")]
        private float _volume = 1.0f;

        [SerializeField]
        [ConvaiInspectorSection("Audio Settings")]
        [Tooltip("When enabled, this character's speech is silenced regardless of Volume.")]
        private bool _isMuted;

        [SerializeField]
        [ConvaiInspectorSection("3D Audio")]
        [Tooltip("Whether this character's speech attenuates with distance and direction, like a 3D sound source.")]
        private bool _use3DAudio = true;

        [SerializeField]
        [ConvaiInspectorSection("3D Audio")]
        [Tooltip("Distance at which 3D audio is at full volume. Closer than this has no extra boost.")]
        private float _minDistance = 1f;

        [SerializeField]
        [ConvaiInspectorSection("3D Audio")]
        [Tooltip("Distance at which 3D audio has fully attenuated to silence.")]
        private float _maxDistance = 50f;

        #endregion

        #region Components

        private ConvaiCharacter _character;
        private IConvaiRoomAudioService _roomAudioService;
        private IAgentRegistry _agentRegistry;

        #endregion

        #region Public Properties

        /// <summary>Audio output volume (0-1).</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                if (AudioSource != null) AudioSource.volume = _isMuted ? 0f : _volume;
            }
        }

        /// <summary>Whether audio output is muted.</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                if (AudioSource != null) AudioSource.volume = _isMuted ? 0f : _volume;
            }
        }

        /// <summary>The AudioSource used for playback.</summary>
        public AudioSource AudioSource { get; private set; }

        #endregion

        #region Dependency Injection

        /// <summary>
        ///     Injects the room audio service and agent registry.
        ///     Called by the ConvaiManager pipeline.
        /// </summary>
        public void Inject(IConvaiRoomAudioService roomAudioService, IAgentRegistry agentRegistry = null)
        {
            _roomAudioService = roomAudioService;
            _agentRegistry = agentRegistry;

            // Register AudioSource with the registry so AudioTrackManager can find it
            RegisterAudioSourceWithRegistry();
        }

        private void TryResolveDependencies()
        {
            if (_roomAudioService != null && _agentRegistry != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetRoomAudioService(out IConvaiRoomAudioService roomAudioService);
            manager.TryGetAgentRegistry(out IAgentRegistry agentRegistry);

            if (roomAudioService != null || agentRegistry != null)
                Inject(roomAudioService, agentRegistry);
        }

        private void RegisterAudioSourceWithRegistry()
        {
            if (_agentRegistry == null || _character == null || AudioSource == null) return;

            string characterId = _character.CharacterId;
            if (string.IsNullOrEmpty(characterId)) return;

            _agentRegistry.SetAudioSource(characterId, AudioSource);
            ConvaiLogger.Debug($"Registered AudioSource for character '{characterId}'",
                LogCategory.Audio);
        }

        private void UnregisterAudioSourceFromRegistry()
        {
            if (_agentRegistry == null || _character == null) return;

            string characterId = _character.CharacterId;
            if (string.IsNullOrEmpty(characterId)) return;

            _agentRegistry.SetAudioSource(characterId, null);
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            AudioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            if (_character == null)
            {
                ConvaiLogger.Error($"ConvaiCharacter component not found on {gameObject.name}",
                    LogCategory.Audio);
                enabled = false;
                return;
            }

            SubscribeToEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
            UnregisterAudioSourceFromRegistry();
        }

        private void OnValidate()
        {
            if (AudioSource != null) ConfigureAudioSource();
        }

        #endregion

        #region Private Helpers

        private void ConfigureAudioSource()
        {
            if (AudioSource == null) return;

            AudioSource.playOnAwake = false;
            AudioSource.volume = _isMuted ? 0f : _volume;
            AudioSource.spatialBlend = _use3DAudio ? 1f : 0f;
            AudioSource.minDistance = _minDistance;
            AudioSource.maxDistance = _maxDistance;
            AudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        private void SubscribeToEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted += OnCharacterSpeechStarted;
            _character.OnSpeechStopped += OnCharacterSpeechStopped;
        }

        private void UnsubscribeFromEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted -= OnCharacterSpeechStarted;
            _character.OnSpeechStopped -= OnCharacterSpeechStopped;
        }

        private void OnCharacterSpeechStarted()
        {
            // AudioSource is already playing via LiveKit's AudioStream
            // This hook is for future extensions (visual indicators, etc.)
        }

        private void OnCharacterSpeechStopped()
        {
            // AudioSource playback stops automatically when track ends
            // This hook is for future extensions (cleanup, etc.)
        }

        #endregion
    }
}
