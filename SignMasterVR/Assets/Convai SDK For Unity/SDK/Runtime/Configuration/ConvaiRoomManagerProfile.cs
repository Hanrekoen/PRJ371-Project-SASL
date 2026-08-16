using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Context;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Runtime
{
    /// <summary>
    ///     Selects whether a component uses local inline values or a reusable configuration asset.
    /// </summary>
    public enum ConvaiConfigSourceMode
    {
        Inline = 0,
        Asset = 1
    }

    /// <summary>
    ///     Reusable room/session defaults for Convai runtime scenes.
    /// </summary>
    [MovedFrom(true, "Convai.Runtime", "Convai.Runtime",
        "ConvaiRoomConfig")]
    [CreateAssetMenu(fileName = "ConvaiRoomManagerProfile", menuName = "Convai/Room Manager Profile")]
    public sealed class ConvaiRoomManagerProfile : ScriptableObject
    {
        // No [Header] on serialized fields: ConvaiRoomManagerProfileEditor draws every one of them
        // into its own named sections and never falls back to the default inspector, so a header
        // here would render nowhere and could only drift away from the section it was meant to name.
        [SerializeField] [Tooltip("The default connection type for room sessions.")]
        private ConvaiConnectionType _connectionType = ConvaiConnectionType.Audio;

        [SerializeField]
        [Tooltip("Optional override for the outgoing video track name. Leave blank to use publisher defaults.")]
        private string _videoTrackName = string.Empty;

        [SerializeField] [Tooltip("The default backend endpoint used when connecting room sessions.")]
        private ConvaiServerEndpoint _serverEndpoint = ConvaiServerEndpoint.Connect;

        [SerializeField] [Tooltip("Whether room sessions should connect automatically on Start by default.")]
        private bool _connectOnStart = true;

        [SerializeField]
        [Tooltip("Advanced default turn-taking, microphone startup, and initial STT policy for room sessions.")]
        private TurnTakingOptions _turnTakingOptions = TurnTakingOptions.CreateHandsFreeDefault();

        [SerializeField]
        [Tooltip("Advanced default backend user voice activity detection policy for room sessions.")]
        private UserVadSettings _userVadSettings = UserVadSettings.CreateDefault();

        [SerializeField]
        [Tooltip("Controls backend dynamic context vision. Auto follows the configured Connection Type (Video enables it); Enabled always uses video; Disabled never sends vision config.")]
        private ConvaiVisionContextMode _visionContextMode = ConvaiVisionContextMode.Auto;

        [SerializeField] [Tooltip("Backend frame sampling settings for dynamic context vision.")]
        private ConvaiVisionInputSettings _visionInputSettings = ConvaiVisionInputSettings.CreateDefault();

        [SerializeField] [Tooltip("Backend response mode policy for dynamic context vision updates and triggers.")]
        private ConvaiVisionRespondModeSettings _visionRespondModes = ConvaiVisionRespondModeSettings.CreateDefault();

        [SerializeField]
        [Min(0f)]
        [Tooltip("How long a room can be rejoined before forcing a fresh room.")]
        private float _roomRejoinTtlSeconds = 60f;

        [SerializeField] [Tooltip("How session resume should behave on reconnect.")]
        private ResumePolicy _resumePolicy = ResumePolicy.ResumeIfPossible;

        [SerializeField] [Min(0)] [Tooltip("Maximum reconnect attempts before giving up.")]
        private int _maxReconnectAttempts = 3;

        [SerializeField] [Tooltip("Whether the agent should spawn again when rejoining an existing room.")]
        private bool _spawnAgentOnRejoin = true;

        [SerializeField] [Min(0)] [Tooltip("How long ConnectAsync waits for Start() before timing out.")]
        private int _startWaitTimeoutMs = 5000;

        [SerializeField] [Min(0f)] [Tooltip("Delay before starting the microphone after a successful connection.")]
        private float _autoMicStartDelaySeconds = 0.5f;

        public ConvaiConnectionType ConnectionType => _connectionType;
        public string VideoTrackName => _videoTrackName ?? string.Empty;
        public ConvaiServerEndpoint ServerEndpoint => _serverEndpoint;
        public bool ConnectOnStart => _connectOnStart;

        public TurnTakingOptions TurnTakingOptions =>
            _turnTakingOptions?.Clone() ?? TurnTakingOptions.CreateHandsFreeDefault();

        public UserVadSettings UserVadSettings =>
            _userVadSettings?.Clone() ?? UserVadSettings.CreateDefault();

        public ConvaiVisionContextMode VisionContextMode => _visionContextMode;

        public ConvaiVisionInputSettings VisionInputSettings =>
            _visionInputSettings?.Clone() ?? ConvaiVisionInputSettings.CreateDefault();

        public ConvaiVisionRespondModeSettings VisionRespondModes =>
            _visionRespondModes?.Clone() ?? ConvaiVisionRespondModeSettings.CreateDefault();

        public ReconnectPolicy CreateReconnectPolicy() =>
            new(_roomRejoinTtlSeconds, _resumePolicy, _maxReconnectAttempts, _spawnAgentOnRejoin,
                _startWaitTimeoutMs, _autoMicStartDelaySeconds);

        public string DescribeReconnectPolicy() => CreateReconnectPolicy().ToString();
    }
}
