using System;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>Describes why a transport was disconnected.</summary>
    public enum DisconnectReason
    {
        ClientInitiated,
        RemoteHangUp,
        AuthenticationFailure,
        TransportError,
        Unknown
    }

    /// <summary>Represents a received transport data packet.</summary>
    public readonly struct DataPacket
    {
        public DataPacket(ReadOnlyMemory<byte> payload, string topic, DataPacketKind kind, string participantId)
        {
            Payload = payload;
            Topic = topic;
            Kind = kind;
            ParticipantId = participantId;
        }

        public ReadOnlyMemory<byte> Payload { get; }
        public string Topic { get; }
        public DataPacketKind Kind { get; }
        public string ParticipantId { get; }
    }

    /// <summary>Represents the kind of a received data packet.</summary>
    public enum DataPacketKind
    {
        Reliable,
        Lossy
    }

    /// <summary>
    ///     High-level abstraction for real-time communication transport.
    ///     Provides unified, platform-agnostic API for connection, audio, and data exchange.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface serves as the primary facade for real-time communication,
    ///         abstracting the differences between native LiveKit SDK and WebGL LiveKit SDK.
    ///     </para>
    ///     <para>
    ///         <see cref="DataReceived" /> exposes raw transport data for higher-level protocol handling.
    ///         <see cref="Capabilities" /> describes supported features, and <see cref="AudioState" />
    ///         reports current runtime audio state.
    ///     </para>
    /// </remarks>
    public interface IRealtimeTransport : IDisposable
    {
        #region Data Events

        /// <summary>
        ///     Raised when raw data is received from a remote participant.
        ///     Protocol-level parsing (e.g., RTVI messages) should happen at a higher layer.
        /// </summary>
        public event Action<DataPacket> DataReceived;

        #endregion

        #region Data Methods

        /// <summary>
        ///     Sends data to remote participants.
        /// </summary>
        /// <param name="payload">Data payload bytes.</param>
        /// <param name="reliable">Whether to use reliable (ordered) delivery.</param>
        /// <param name="topic">Optional topic for message routing.</param>
        /// <param name="destinationIdentities">
        ///     Specific participant identities to send to.
        ///     Null or empty sends to all participants.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public Task SendDataAsync(
            ReadOnlyMemory<byte> payload,
            bool reliable = true,
            string topic = null,
            string[] destinationIdentities = null,
            CancellationToken ct = default);

        #endregion

        #region State Properties

        /// <summary>
        ///     Gets the current transport connection state.
        /// </summary>
        public TransportState State { get; }

        /// <summary>
        ///     Gets the current session information.
        ///     Null when not connected.
        /// </summary>
        public TransportSessionInfo? CurrentSession { get; }

        /// <summary>
        ///     Gets the static platform capabilities for this transport.
        ///     Use for "can this platform ever support X?" questions.
        /// </summary>
        public TransportCapabilities Capabilities { get; }

        /// <summary>
        ///     Gets the current runtime audio state.
        ///     Use for "is X currently available/active?" questions.
        /// </summary>
        public AudioRuntimeState AudioState { get; }

        /// <summary>
        ///     Gets whether the transport is currently connected.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     Gets the room facade providing access to participants and tracks.
        ///     Null when not connected.
        /// </summary>
        /// <remarks>
        ///     The room facade provides platform-agnostic access to:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>Local participant with track publishing</description>
        ///         </item>
        ///         <item>
        ///             <description>Remote participants with subscribed tracks</description>
        ///         </item>
        ///         <item>
        ///             <description>Track subscription events</description>
        ///         </item>
        ///     </list>
        /// </remarks>
        public IRoomFacade Room { get; }

        #endregion

        #region Connection Events

        /// <summary>
        ///     Raised when successfully connected to the room.
        /// </summary>
        public event Action<TransportSessionInfo> Connected;

        /// <summary>
        ///     Raised when disconnected from the room.
        /// </summary>
        public event Action<DisconnectReason> Disconnected;

        /// <summary>
        ///     Raised when a connection attempt fails.
        /// </summary>
        public event Action<TransportError> ConnectionFailed;

        /// <summary>
        ///     Raised when the transport is attempting to reconnect after a transient failure.
        /// </summary>
        public event Action Reconnecting;

        /// <summary>
        ///     Raised when reconnection succeeds.
        /// </summary>
        public event Action Reconnected;

        /// <summary>
        ///     Raised when the transport state changes.
        /// </summary>
        public event Action<TransportState> StateChanged;

        #endregion

        #region Participant Events

        /// <summary>
        ///     Raised when a remote participant joins the room.
        /// </summary>
        public event Action<TransportParticipantInfo> ParticipantConnected;

        /// <summary>
        ///     Raised when a remote participant leaves the room.
        /// </summary>
        public event Action<TransportParticipantInfo> ParticipantDisconnected;

        /// <summary>
        ///     Raised when a track is subscribed from a remote participant.
        /// </summary>
        public event Action<TrackInfo> TrackSubscribed;

        /// <summary>
        ///     Raised when a track subscription ends.
        /// </summary>
        public event Action<TrackInfo> TrackUnsubscribed;

        #endregion

        #region Audio Events

        /// <summary>
        ///     Raised when microphone enabled state changes.
        /// </summary>
        public event Action<bool> MicrophoneEnabledChanged;

        /// <summary>
        ///     Raised when microphone mute state changes.
        /// </summary>
        public event Action<bool> MicrophoneMuteChanged;

        /// <summary>
        ///     Raised when audio playback activation state changes.
        ///     Relevant for WebGL where user gesture is required.
        /// </summary>
        public event Action<bool> AudioPlaybackStateChanged;

        #endregion

        #region Connection Methods

        /// <summary>
        ///     Connects to the real-time transport room.
        /// </summary>
        /// <param name="url">Transport server URL (e.g., LiveKit server URL).</param>
        /// <param name="token">Authentication token for the room.</param>
        /// <param name="options">Optional connection options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if connection succeeded; false otherwise.</returns>
        public Task<bool> ConnectAsync(
            string url,
            string token,
            TransportConnectOptions options = null,
            CancellationToken ct = default);

        /// <summary>
        ///     Disconnects from the transport room.
        /// </summary>
        /// <param name="reason">Reason for disconnection.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task DisconnectAsync(
            DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken ct = default);

        #endregion

        #region Audio Methods

        /// <summary>
        ///     Activates audio playback capability.
        ///     On WebGL, this must be called from a user gesture context (e.g., button click).
        ///     On native platforms, this is a no-op.
        /// </summary>
        public void EnableAudio();

        /// <summary>
        ///     Enables microphone capture and publishes audio to the room.
        ///     On WebGL, this must be called from a user gesture context.
        /// </summary>
        /// <param name="microphoneDeviceIndex">Microphone device index (native only).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if microphone was successfully enabled.</returns>
        public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default);

        /// <summary>
        ///     Disables microphone capture and unpublishes the audio track.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public Task DisableMicrophoneAsync(CancellationToken ct = default);

        /// <summary>
        ///     Sets microphone mute state without disabling the track.
        /// </summary>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void SetMicrophoneMuted(bool muted);

        /// <summary>
        ///     Gets whether microphone is currently enabled.
        /// </summary>
        public bool IsMicrophoneEnabled { get; }

        /// <summary>
        ///     Gets whether microphone is currently muted.
        /// </summary>
        public bool IsMicrophoneMuted { get; }

        /// <summary>
        ///     Checks if microphone can currently be enabled.
        ///     Accounts for permission state and user gesture requirements.
        /// </summary>
        /// <returns>True if EnableMicrophoneAsync is likely to succeed.</returns>
        public bool CanEnableMicrophone();

        /// <summary>
        ///     Checks if audio playback can currently be activated.
        ///     On WebGL, returns false if not in user gesture context.
        /// </summary>
        /// <returns>True if EnableAudio is likely to succeed.</returns>
        public bool CanEnableAudio();

        #endregion
    }
}
