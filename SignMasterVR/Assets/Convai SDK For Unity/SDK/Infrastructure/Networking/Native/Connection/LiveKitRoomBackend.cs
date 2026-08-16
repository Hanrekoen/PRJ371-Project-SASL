using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;
using DataPacketKind = LiveKit.Proto.DataPacketKind;
// Type alias to disambiguate LiveKit track types from abstraction interfaces
using LKRemoteTrack = LiveKit.IRemoteTrack;

namespace Convai.Infrastructure.Networking.Connection
{
    /// <summary>
    ///     LiveKit-backed native room backend.
    ///     Encapsulates the LiveKit <see cref="Room" /> instance and exposes
    ///     backend events consumed by the native transport layer.
    /// </summary>
    /// <remarks>
    ///     Uses IConnectionStateMachine for validated state transitions.
    ///     Thread-safe for state access and modification.
    ///     All public events are marshalled to the main thread for safe Unity API access.
    /// </remarks>
    internal sealed class LiveKitRoomBackend : IDisposable
    {
        private readonly RoomOptions _defaultOptions;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly ILogger _logger;

        private bool _disposed;

        /// <summary>
        ///     Creates a new LiveKitRoomBackend with a default ConnectionStateMachine.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="dispatcher">Main thread dispatcher for event marshalling (optional, but recommended).</param>
        /// <param name="defaultOptions">Default room options (optional).</param>
        public LiveKitRoomBackend(ILogger logger, IMainThreadDispatcher dispatcher = null,
            RoomOptions defaultOptions = null)
            : this(logger, new ConnectionStateMachine(logger), dispatcher, defaultOptions)
        {
        }

        /// <summary>
        ///     Creates a new LiveKitRoomBackend with a custom state machine.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="stateMachine">State machine for connection state management.</param>
        /// <param name="dispatcher">Main thread dispatcher for event marshalling (optional, but recommended).</param>
        /// <param name="defaultOptions">Default room options (optional).</param>
        public LiveKitRoomBackend(
            ILogger logger,
            IConnectionStateMachine stateMachine,
            IMainThreadDispatcher dispatcher = null,
            RoomOptions defaultOptions = null)
        {
            _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).WithTag(nameof(LiveKitRoomBackend));
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _dispatcher = dispatcher;
            _defaultOptions = defaultOptions ?? new RoomOptions
            {
                AutoSubscribe = true, Dynacast = true, AdaptiveStream = true
            };

            Room = new Room();
            SubscribeRoomEvents();

            StateMachine.StateChanged += OnStateMachineStateChanged;
        }

        public Room Room { get; private set; }

        public IConnectionStateMachine StateMachine { get; }

        public ConnectionState State => StateMachine.CurrentState;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (Room != null && Room.IsConnected)
                {
                    _logger.Debug("Disconnecting Room in Dispose()");
                    Room.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Exception disconnecting Room on dispose: {ex.Message}");
            }

            UnsubscribeRoomEvents();

            if (StateMachine != null) StateMachine.StateChanged -= OnStateMachineStateChanged;

            _disposed = true;
        }

        /// <summary>
        ///     Raised when the connection state changes.
        /// </summary>
        public event Action<ConnectionState> StateChanged;

        /// <summary>
        ///     Raised when a data packet is received.
        /// </summary>
        public event Action<DataPacket> DataPacketReceived;

        /// <summary>
        ///     Connects the backend to a LiveKit room.
        /// </summary>
        public async Task<LiveKitConnectResult> ConnectAsync(string url, string token,
            RoomOptions options = null, CancellationToken cancellationToken = default)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));

            if (token == null) throw new ArgumentNullException(nameof(token));

            ThrowIfDisposed();

            await ResetRoomAsync(cancellationToken);

            if (!StateMachine.TryTransition(ConnectionState.Connecting))
            {
                _logger.Warning("Cannot transition to Connecting state");
                return new LiveKitConnectResult(false, "Invalid state transition");
            }

            options ??= _defaultOptions;

            try
            {
                ConnectInstruction connect = Room.Connect(url, token, options);
                while (!connect.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(50, cancellationToken);
                }

                if (connect.IsError)
                {
                    string message = connect.ToString();
                    _logger.Error($"Connect failed: {message}");
                    StateMachine.TryTransition(ConnectionState.Disconnected, message);
                    return new LiveKitConnectResult(false, message);
                }

                if (StateMachine.CurrentState != ConnectionState.Connected)
                {
                    if (!StateMachine.TryTransition(ConnectionState.Connected))
                    {
                        _logger.Warning(
                            $"ConnectAsync fallback transition failed from {StateMachine.CurrentState}; forcing Connected state.");
                        StateMachine.ForceTransition(ConnectionState.Connected);
                    }
                }

                return new LiveKitConnectResult(true, roomName: Room?.Name, sessionId: Room?.Sid);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Connect cancelled.");
                StateMachine.TryTransition(ConnectionState.Disconnected, "Cancelled");
                return new LiveKitConnectResult(false, "Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected connect exception: {ex.Message}");
                StateMachine.TryTransition(ConnectionState.Disconnected, ex.Message);
                return new LiveKitConnectResult(false, ex.Message);
            }
        }

        public async Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (State == ConnectionState.Disconnected)
            {
                _logger.Debug("Already disconnected, skipping.");
                return;
            }

            _logger.Info($"Disconnect requested. Reason: {reason}");

            if (State == ConnectionState.Connected)
            {
                if (!StateMachine.TryTransition(ConnectionState.Disconnecting))
                {
                    _logger.Warning(
                        "Failed to transition to Disconnecting state, forcing transition.");
                    StateMachine.ForceTransition(ConnectionState.Disconnecting);
                }
            }

            var disconnectTcs = new TaskCompletionSource<bool>();

            void OnDisconnected(Room room)
            {
                Room.Disconnected -= OnDisconnected;
                disconnectTcs.TrySetResult(true);
            }

            try
            {
                Room.Disconnected += OnDisconnected;
                Room.Disconnect();

                const int TimeoutMs = 5000;
                Task completedTask = await Task.WhenAny(disconnectTcs.Task, Task.Delay(TimeoutMs, cancellationToken));

                if (completedTask != disconnectTcs.Task)
                {
                    _logger.Warning("Disconnect timed out waiting for Disconnected event.");
                    Room.Disconnected -= OnDisconnected;
                }
                else
                    _logger.Debug("Disconnect completed via Disconnected event.");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Disconnect cancelled.");
                Room.Disconnected -= OnDisconnected;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Disconnect threw: {ex.Message}");
                Room.Disconnected -= OnDisconnected;
            }

            if (!StateMachine.TryTransition(ConnectionState.Disconnected))
                StateMachine.ForceTransition(ConnectionState.Disconnected);
        }

        public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true,
            string topic = "", CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (Room.LocalParticipant == null)
            {
                _logger.Warning("Cannot send data. Local participant not available.");
                return Task.CompletedTask;
            }

            try
            {
                byte[] buffer = payload.ToArray();
                Room.LocalParticipant.PublishData(buffer, null, reliable, topic);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to send data: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Creates a fresh Room instance, unsubscribing from old events and subscribing to new ones.
        ///     This ensures a clean state between connection attempts, preventing duplicate participant issues.
        /// </summary>
        private async Task ResetRoomAsync(CancellationToken cancellationToken = default)
        {
            _logger.Debug("Resetting Room instance for fresh connection.");

            if (Room != null)
            {
                UnsubscribeRoomEvents();

                try
                {
                    if (Room.IsConnected)
                    {
                        _logger.Debug("Disconnecting old Room before disposal (awaiting)");

                        var disconnectTcs = new TaskCompletionSource<bool>();
                        void OnDisconnected(Room r) => disconnectTcs.TrySetResult(true);

                        Room.Disconnected += OnDisconnected;
                        Room.Disconnect();

                        const int TimeoutMs = 3000;
                        Task completed =
                            await Task.WhenAny(disconnectTcs.Task, Task.Delay(TimeoutMs, cancellationToken));
                        Room.Disconnected -= OnDisconnected;

                        if (completed != disconnectTcs.Task)
                            _logger.Warning(
                                "Old Room disconnect timed out in ResetRoom; proceeding.");
                        else
                            _logger.Debug("Old Room disconnected successfully.");
                    }

                    (Room as IDisposable)?.Dispose();
                }
                catch (OperationCanceledException)
                {
                    _logger.Warning("ResetRoom cancelled during old room disconnect.");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Exception disposing old Room: {ex.Message}");
                }
            }

            Room = new Room();

            SubscribeRoomEvents();

            StateMachine.Reset();
        }

        /// <summary>Raised when the LiveKit room connects.</summary>
        public event Action<Room> RoomConnected;

        /// <summary>Raised when the LiveKit room disconnects.</summary>
        public event Action<Room> RoomDisconnected;

        /// <summary>Raised when the LiveKit room begins reconnecting.</summary>
        public event Action<Room> RoomReconnecting;

        /// <summary>Raised when the LiveKit room reconnects successfully.</summary>
        public event Action<Room> RoomReconnected;

        /// <summary>Raised when a participant connects to the room.</summary>
        public event Action<Participant> ParticipantConnected;

        /// <summary>Raised when a participant disconnects from the room.</summary>
        public event Action<Participant> ParticipantDisconnected;

        /// <summary>Raised when a remote track is published by a participant.</summary>
        public event Action<RemoteTrackPublication, RemoteParticipant> TrackPublished;

        /// <summary>Raised when a remote track is subscribed.</summary>
        public event Action<LKRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;

        /// <summary>Raised when a remote track is unsubscribed.</summary>
        public event Action<LKRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;

        /// <summary>Raised when a track is muted.</summary>
        public event Action<TrackPublication, Participant> TrackMuted;

        /// <summary>Raised when a track is unmuted.</summary>
        public event Action<TrackPublication, Participant> TrackUnmuted;

        /// <summary>Raised when the active speakers list changes.</summary>
        public event Action<List<Participant>> ActiveSpeakersChanged;

        private void SubscribeRoomEvents()
        {
            Room.DataReceived += HandleDataReceived;
            Room.Disconnected += HandleDisconnected;
            Room.Connected += HandleConnected;
            Room.Reconnecting += HandleReconnecting;
            Room.Reconnected += HandleReconnected;
            Room.ParticipantConnected += HandleParticipantConnected;
            Room.ParticipantDisconnected += HandleParticipantDisconnected;
            Room.TrackPublished += HandleTrackPublished;
            Room.TrackSubscribed += HandleTrackSubscribed;
            Room.TrackUnsubscribed += HandleTrackUnsubscribed;
            Room.TrackMuted += HandleTrackMuted;
            Room.TrackUnmuted += HandleTrackUnmuted;
            Room.ActiveSpeakersChanged += HandleActiveSpeakersChanged;
        }

        private void UnsubscribeRoomEvents()
        {
            Room.DataReceived -= HandleDataReceived;
            Room.Disconnected -= HandleDisconnected;
            Room.Connected -= HandleConnected;
            Room.Reconnecting -= HandleReconnecting;
            Room.Reconnected -= HandleReconnected;
            Room.ParticipantConnected -= HandleParticipantConnected;
            Room.ParticipantDisconnected -= HandleParticipantDisconnected;
            Room.TrackPublished -= HandleTrackPublished;
            Room.TrackSubscribed -= HandleTrackSubscribed;
            Room.TrackUnsubscribed -= HandleTrackUnsubscribed;
            Room.TrackMuted -= HandleTrackMuted;
            Room.TrackUnmuted -= HandleTrackUnmuted;
            Room.ActiveSpeakersChanged -= HandleActiveSpeakersChanged;
        }

        private void HandleConnected(Room room)
        {
            _logger.Info($"Room connected: {room?.Name ?? "unknown"}");

            ConnectionState currentState = StateMachine.CurrentState;
            if (currentState != ConnectionState.Connected && !StateMachine.TryTransition(ConnectionState.Connected))
            {
                _logger.Warning(
                    $"HandleConnected failed to transition state from {currentState} to Connected");
            }

            DispatchToMainThread(() => RoomConnected?.Invoke(room));
        }

        private void HandleDisconnected(Room room)
        {
            _logger.Info($"Room disconnected: {room?.Name ?? "unknown"}");

            if (!StateMachine.TryTransition(ConnectionState.Disconnected))
                StateMachine.ForceTransition(ConnectionState.Disconnected, "Unexpected disconnect");

            DispatchToMainThread(() => RoomDisconnected?.Invoke(room));
        }

        private void HandleReconnecting(Room room)
        {
            _logger.Info($"Room reconnecting: {room?.Name ?? "unknown"}");
            StateMachine.TryTransition(ConnectionState.Reconnecting);
            DispatchToMainThread(() => RoomReconnecting?.Invoke(room));
        }

        private void HandleReconnected(Room room)
        {
            _logger.Info($"Room reconnected: {room?.Name ?? "unknown"}");

            ConnectionState currentState = StateMachine.CurrentState;
            if (currentState != ConnectionState.Connected && !StateMachine.TryTransition(ConnectionState.Connected))
            {
                _logger.Warning(
                    $"HandleReconnected failed to transition state from {currentState} to Connected");
            }

            DispatchToMainThread(() => RoomReconnected?.Invoke(room));
        }

        private void HandleParticipantConnected(Participant participant) =>
            DispatchToMainThread(() => ParticipantConnected?.Invoke(participant));

        private void HandleParticipantDisconnected(Participant participant) =>
            DispatchToMainThread(() => ParticipantDisconnected?.Invoke(participant));

        private void HandleTrackPublished(RemoteTrackPublication publication, RemoteParticipant participant) =>
            DispatchToMainThread(() => TrackPublished?.Invoke(publication, participant));

        private void HandleTrackSubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant) =>
            DispatchToMainThread(() => TrackSubscribed?.Invoke(track, publication, participant));

        private void HandleTrackUnsubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant) =>
            DispatchToMainThread(() => TrackUnsubscribed?.Invoke(track, publication, participant));

        private void HandleTrackMuted(TrackPublication publication, Participant participant) =>
            DispatchToMainThread(() => TrackMuted?.Invoke(publication, participant));

        private void HandleTrackUnmuted(TrackPublication publication, Participant participant) =>
            DispatchToMainThread(() => TrackUnmuted?.Invoke(publication, participant));

        private void HandleActiveSpeakersChanged(List<Participant> speakers) =>
            DispatchToMainThread(() => ActiveSpeakersChanged?.Invoke(speakers));

        private void HandleDataReceived(byte[] data, Participant participant, DataPacketKind kind,
            string topic)
        {
            if (data == null) return;

            string participantId = string.Empty;
            if (participant != null)
            {
                participantId = !string.IsNullOrEmpty(participant.Sid)
                    ? participant.Sid
                    : participant.Identity ?? string.Empty;
            }
            else
            {
                _logger.Warning(
                    "Received data packet but participant lookup returned null. Participant ID unavailable.");

                if (Room?.RemoteParticipants != null && Room.RemoteParticipants.Count > 0)
                {
                    RemoteParticipant firstParticipant = Room.RemoteParticipants.Values.FirstOrDefault();
                    if (firstParticipant != null)
                    {
                        participantId = !string.IsNullOrEmpty(firstParticipant.Sid)
                            ? firstParticipant.Sid
                            : firstParticipant.Identity ?? string.Empty;
                        _logger.Debug($"Using fallback participant lookup: {participantId}");
                    }
                }
            }

            DataPacket packet = new(
                data,
                topic ?? string.Empty,
                kind == DataPacketKind.KindReliable
                    ? Transport.DataPacketKind.Reliable
                    : Transport.DataPacketKind.Lossy,
                participantId);

            DispatchToMainThread(() => DataPacketReceived?.Invoke(packet));
        }

        private void
            OnStateMachineStateChanged(ConnectionState oldState, ConnectionState newState, string errorMessage) =>
            DispatchToMainThread(() => StateChanged?.Invoke(newState));

        /// <summary>
        ///     Dispatches an action to the main thread if a dispatcher is available.
        ///     If no dispatcher is configured, executes the action immediately (with a warning logged once).
        /// </summary>
        private void DispatchToMainThread(Action action)
        {
            if (action == null) return;

            if (_dispatcher != null)
            {
                if (!_dispatcher.TryDispatch(action))
                    _logger.Warning("Failed to dispatch action to main thread.");
            }
            else
                action();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LiveKitRoomBackend));
        }
    }

    /// <summary>Result of a LiveKit backend connection attempt.</summary>
    public readonly struct LiveKitConnectResult
    {
        public LiveKitConnectResult(bool success, string errorMessage = null, string roomName = null,
            string sessionId = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
            RoomName = roomName;
            SessionId = sessionId;
        }

        public bool Success { get; }
        public string ErrorMessage { get; }
        public string RoomName { get; }
        public string SessionId { get; }
    }
}
