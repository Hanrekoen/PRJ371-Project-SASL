using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Logging;
using LiveKit;
using UnityEngine;
using DataPacketKind = LiveKit.DataPacketKind;
using DisconnectReason = LiveKit.DisconnectReason;
using TransportDisconnectReason = Convai.Infrastructure.Networking.Transport.DisconnectReason;
using TrackKind = LiveKit.TrackKind;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IRealtimeTransport" />.
    ///     Uses the LiveKit WebGL SDK for real-time communication.
    /// </summary>
    /// <remarks>
    ///     Key WebGL-specific behaviors:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Uses coroutines internally for reliable operation (async/await is problematic in WebGL)</description>
    ///         </item>
    ///         <item>
    ///             <description>Requires user gesture for audio playback (browser limitation)</description>
    ///         </item>
    ///         <item>
    ///             <description>Requires user gesture for microphone access (browser limitation)</description>
    ///         </item>
    ///         <item>
    ///             <description>Audio plays through browser audio elements, not Unity AudioSource</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    internal class WebGLRealtimeTransport : IRealtimeTransport
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL transport instance.
        /// </summary>
        /// <param name="coroutineRunner">MonoBehaviour to run coroutines on. Required for WebGL async operations.</param>
        public WebGLRealtimeTransport(MonoBehaviour coroutineRunner)
        {
            _coroutineRunner = coroutineRunner ?? throw new ArgumentNullException(nameof(coroutineRunner));
        }

        #endregion

        #region IRealtimeTransport - Data Methods

        /// <inheritdoc />
        public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
            string[] destinationIdentities = null, CancellationToken ct = default)
        {
            if (_room?.LocalParticipant == null)
            {
                LogTransportWarning($"Room not connected. Cannot send data.");
                return Task.CompletedTask;
            }

            try
            {
                byte[] data = payload.ToArray();
                _room.LocalParticipant.PublishData(data, reliable, destinationIdentities, topic);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogTransportError(ex, $"Error sending data");
                return Task.FromException(ex);
            }
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _isDisconnecting = true;
            CleanupRoom();
            _currentSession = null;
            IsMicrophoneEnabled = false;
            IsMicrophoneMuted = false;
            _isAudioPlaybackActive = false;

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Fields


        private Room _room;

        private WebGLRoomFacade _roomFacade;
        private TransportSessionInfo? _currentSession;
        private bool _isAudioPlaybackActive;
        private bool _isDisconnecting;
        private bool _disposed;

        // Coroutine runner for WebGL (required since we can't use async/await reliably)
        private readonly MonoBehaviour _coroutineRunner;

        #endregion

        #region IRealtimeTransport - State Properties

        /// <inheritdoc />
        public TransportState State { get; private set; } = TransportState.Disconnected;

        /// <inheritdoc />
        public TransportSessionInfo? CurrentSession => _currentSession;

        /// <inheritdoc />
        public TransportCapabilities Capabilities => TransportCapabilities.WebGL();

        /// <inheritdoc />
        public AudioRuntimeState AudioState => new(
            _isAudioPlaybackActive,
            IsMicrophoneEnabled,
            IsMicrophoneMuted,
            PermissionState.Unknown, // Browser manages this
            !_isAudioPlaybackActive
        );

        /// <inheritdoc />
        public bool IsConnected => State == TransportState.Connected;

        /// <inheritdoc />
        public IRoomFacade Room => _roomFacade;

        /// <inheritdoc />
        public bool IsMicrophoneEnabled { get; private set; }

        /// <inheritdoc />
        public bool IsMicrophoneMuted { get; private set; }

        #endregion

        #region IRealtimeTransport - Events

        /// <inheritdoc />
        public event Action<TransportSessionInfo> Connected;

        /// <inheritdoc />
        public event Action<TransportDisconnectReason> Disconnected;

        /// <inheritdoc />
        public event Action<TransportError> ConnectionFailed;

        /// <inheritdoc />
        public event Action Reconnecting;

        /// <inheritdoc />
        public event Action Reconnected;

        /// <inheritdoc />
        public event Action<TransportState> StateChanged;

        /// <inheritdoc />
        public event Action<DataPacket> DataReceived;

        /// <inheritdoc />
        public event Action<TransportParticipantInfo> ParticipantConnected;

        /// <inheritdoc />
        public event Action<TransportParticipantInfo> ParticipantDisconnected;

        /// <inheritdoc />
        public event Action<TrackInfo> TrackSubscribed;

        /// <inheritdoc />
        public event Action<TrackInfo> TrackUnsubscribed;

        /// <inheritdoc />
        public event Action<bool> MicrophoneEnabledChanged;

        /// <inheritdoc />
        public event Action<bool> MicrophoneMuteChanged;

        /// <inheritdoc />
        public event Action<bool> AudioPlaybackStateChanged;

        #endregion

        #region IRealtimeTransport - Connection Methods

        /// <inheritdoc />
        public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _coroutineRunner.StartCoroutine(ConnectCoroutine(url, token, options, tcs, ct));
            return tcs.Task;
        }

        private IEnumerator ConnectCoroutine(string url, string token, TransportConnectOptions options,
            TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            if (State != TransportState.Disconnected)
            {
                tcs.TrySetResult(false);
                yield break;
            }

            SetState(TransportState.Connecting);

            // Create LiveKit Room
            try
            {
                _room = new Room(new RoomOptions
                {
                    AdaptiveStream = options?.AdaptiveStream ?? true, Dynacast = options?.Dynacast ?? true
                });
            }
            catch (Exception ex)
            {
                LogTransportError(ex, $"Failed to create Room");
                SetState(TransportState.Disconnected);
                ConnectionFailed?.Invoke(new TransportError(ex.Message, TransportErrorCode.Unknown, ex));
                tcs.TrySetResult(false);
                yield break;
            }

            SubscribeToRoomEvents();

            // Connect to LiveKit
            ConnectOperation connectOp;
            try
            {
                connectOp = _room.Connect(url, token,
                    new RoomConnectOptions { AutoSubscribe = options?.AutoSubscribe ?? true });
            }
            catch (Exception ex)
            {
                LogTransportError(ex, $"Exception calling Connect");
                SetState(TransportState.Disconnected);
                ConnectionFailed?.Invoke(new TransportError(ex.Message, TransportErrorCode.NetworkError, ex));
                tcs.TrySetResult(false);
                yield break;
            }

            // Wait for connection (LiveKit WebGL SDK uses IsDone/IsError)
            float timeout = options?.TimeoutSeconds ?? 30f;
            float elapsed = 0f;
            while (!connectOp.IsDone)
            {
                if (ct.IsCancellationRequested)
                {
                    CleanupRoom();
                    SetState(TransportState.Disconnected);
                    tcs.TrySetCanceled(ct);
                    yield break;
                }

                elapsed += Time.deltaTime;
                if (elapsed > timeout)
                {
                    CleanupRoom();
                    SetState(TransportState.Disconnected);
                    ConnectionFailed?.Invoke(new TransportError("Connection timeout", TransportErrorCode.Timeout));
                    tcs.TrySetResult(false);
                    yield break;
                }

                yield return null;
            }

            if (connectOp.IsError)
            {
                string errorMsg = connectOp.Error?.Message ?? "Unknown error";
                LogTransportError($"LiveKit connection failed: {errorMsg}");
                CleanupRoom();
                SetState(TransportState.Disconnected);
                ConnectionFailed?.Invoke(new TransportError(errorMsg, TransportErrorCode.NetworkError));
                tcs.TrySetResult(false);
                yield break;
            }

            // Connection successful - create room facade wrapper
            _roomFacade = new WebGLRoomFacade(_room, _coroutineRunner);

            _currentSession = new TransportSessionInfo(
                _room.Name,
                localParticipantId: _room.LocalParticipant?.Sid,
                localParticipantIdentity: _room.LocalParticipant?.Identity,
                connectedAt: DateTime.UtcNow
            );

            SetState(TransportState.Connected);
            Connected?.Invoke(_currentSession.Value);

            LogTransportInfo($"Connected to room: {_room.Name}");
            tcs.TrySetResult(true);
        }

        /// <inheritdoc />
        public Task DisconnectAsync(TransportDisconnectReason reason = TransportDisconnectReason.ClientInitiated,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _coroutineRunner.StartCoroutine(DisconnectCoroutine(reason, tcs));
            return tcs.Task;
        }

        private IEnumerator DisconnectCoroutine(TransportDisconnectReason reason, TaskCompletionSource<bool> tcs)
        {
            if (_isDisconnecting || State == TransportState.Disconnected)
            {
                tcs.TrySetResult(true);
                yield break;
            }

            _isDisconnecting = true;
            SetState(TransportState.Disconnecting);

            yield return null; // Let events settle

            // Clean up room facade first
            if (_roomFacade != null)
            {
                try
                {
                    _roomFacade.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }

                _roomFacade = null;
            }

            if (_room != null)
            {
                UnsubscribeFromRoomEvents();
                yield return null;

                try
                {
                    _room.Disconnect();
                }
                catch (Exception ex)
                {
                    LogTransportWarning($"Error during Disconnect: {ex.Message}");
                }

                yield return null;
                yield return null;

                try
                {
                    _room.Dispose();
                }
                catch (Exception ex)
                {
                    LogTransportWarning($"Error during Dispose: {ex.Message}");
                }

                _room = null;
            }

            _currentSession = null;
            IsMicrophoneEnabled = false;
            IsMicrophoneMuted = false;
            _isAudioPlaybackActive = false;
            _isDisconnecting = false;

            SetState(TransportState.Disconnected);
            Disconnected?.Invoke(reason);

            LogTransportInfo($"Disconnect complete");
            tcs.TrySetResult(true);
        }

        #endregion

        #region IRealtimeTransport - Audio Methods

        /// <inheritdoc />
        public void EnableAudio()
        {
            if (_room == null)
            {
                LogAudioWarning($"Room not connected. Cannot enable audio.");
                return;
            }

            if (_isAudioPlaybackActive)
            {
                LogAudioWarning($"Audio already enabled.");
                return;
            }

            // This must be called from user gesture context in browser
            try
            {
                WebGLAudioStream.ResumeTimingContext();
                _room.StartAudio();
            }
            catch (Exception ex)
            {
                LogAudioError(ex, $"Failed to enable audio playback");
                return;
            }

            _isAudioPlaybackActive = true;
            AudioPlaybackStateChanged?.Invoke(true);
            LogAudioInfo($"Audio playback enabled");
        }

        /// <inheritdoc />
        public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _coroutineRunner.StartCoroutine(EnableMicrophoneCoroutine(tcs, ct));
            return tcs.Task;
        }

        private IEnumerator EnableMicrophoneCoroutine(TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            if (_room == null || State != TransportState.Connected)
            {
                LogAudioWarning($"Room not connected. Cannot enable microphone.");
                tcs.TrySetResult(false);
                yield break;
            }

            if (IsMicrophoneEnabled)
            {
                tcs.TrySetResult(true);
                yield break;
            }

            // This must be called from user gesture context in browser
            JSPromise<LocalTrackPublication> micPromise = _room.LocalParticipant.SetMicrophoneEnabled(true);

            while (!micPromise.IsDone)
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    yield break;
                }

                yield return null;
            }

            if (micPromise.IsError)
            {
                JSError error = _room.LocalParticipant.LastMicrophoneError();
                string errorMsg = error?.Message ?? "Unknown error";
                LogAudioError($"Failed to enable microphone: {errorMsg}");
                tcs.TrySetResult(false);
                yield break;
            }

            IsMicrophoneEnabled = true;
            IsMicrophoneMuted = false;
            MicrophoneEnabledChanged?.Invoke(true);
            LogAudioInfo($"Microphone enabled");
            tcs.TrySetResult(true);
        }

        /// <inheritdoc />
        public Task DisableMicrophoneAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _coroutineRunner.StartCoroutine(DisableMicrophoneCoroutine(tcs, ct));
            return tcs.Task;
        }

        private IEnumerator DisableMicrophoneCoroutine(TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            if (_room == null || !IsMicrophoneEnabled)
            {
                tcs.TrySetResult(true);
                yield break;
            }

            JSPromise<LocalTrackPublication> micPromise = _room.LocalParticipant.SetMicrophoneEnabled(false);

            while (!micPromise.IsDone)
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    yield break;
                }

                yield return null;
            }

            if (micPromise.IsError)
            {
                JSError error = _room.LocalParticipant.LastMicrophoneError();
                LogAudioWarning($"Error disabling microphone: {error?.Message ?? "Unknown error"}");
            }

            IsMicrophoneEnabled = false;
            if (IsMicrophoneMuted)
            {
                IsMicrophoneMuted = false;
                MicrophoneMuteChanged?.Invoke(false);
            }

            MicrophoneEnabledChanged?.Invoke(false);
            LogAudioInfo($"Microphone disabled");
            tcs.TrySetResult(true);
        }

        /// <inheritdoc />
        public void SetMicrophoneMuted(bool muted)
        {
            if (!IsMicrophoneEnabled)
            {
                LogAudioWarning($"Microphone not enabled. Cannot set mute state.");
                return;
            }

            // In WebGL, we toggle the microphone on/off rather than true mute
            // This is because the WebGL SDK doesn't have a separate mute API
            _coroutineRunner.StartCoroutine(SetMicrophoneMutedCoroutine(muted));
        }

        private IEnumerator SetMicrophoneMutedCoroutine(bool muted)
        {
            if (_room?.LocalParticipant == null) yield break;

            JSPromise<LocalTrackPublication> micPromise = _room.LocalParticipant.SetMicrophoneEnabled(!muted);

            while (!micPromise.IsDone) yield return null;

            if (micPromise.IsError)
            {
                JSError error = _room.LocalParticipant.LastMicrophoneError();
                LogAudioWarning(
                    $"Error setting microphone mute state: {error?.Message ?? "Unknown error"}");
                yield break;
            }

            IsMicrophoneMuted = muted;
            MicrophoneMuteChanged?.Invoke(muted);
            LogAudioInfo($"Microphone muted: {muted}");
        }

        /// <inheritdoc />
        public bool CanEnableMicrophone()
        {
            // On WebGL, we can only enable microphone when:
            // 1. Connected to a room
            // 2. Called from user gesture context (button click, etc.)
            // We can't detect user gesture from here, so we just check connection
            return State == TransportState.Connected && !IsMicrophoneEnabled;
        }

        /// <inheritdoc />
        public bool CanEnableAudio()
        {
            // On WebGL, audio can only be enabled from user gesture context
            return State == TransportState.Connected && !_isAudioPlaybackActive;
        }

        #endregion

        #region Room Event Handlers

        private void SubscribeToRoomEvents() => SubscribeToRoomEvents(_room);

        private void SubscribeToRoomEvents(Room room)
        {
            if (room == null) return;

            room.Reconnecting += OnRoomReconnecting;
            room.Reconnected += OnRoomReconnected;
            room.Disconnected += OnRoomDisconnected;
            room.ParticipantConnected += OnParticipantConnected;
            room.ParticipantDisconnected += OnParticipantDisconnected;
            room.DataReceived += OnDataReceived;
            room.TrackSubscribed += OnTrackSubscribed;
            room.TrackUnsubscribed += OnTrackUnsubscribed;
            room.AudioPlaybackChanged += OnAudioPlaybackChanged;
        }

        private void UnsubscribeFromRoomEvents() => UnsubscribeFromRoomEvents(_room);

        private void UnsubscribeFromRoomEvents(Room room)
        {
            if (room == null) return;

            room.Reconnecting -= OnRoomReconnecting;
            room.Reconnected -= OnRoomReconnected;
            room.Disconnected -= OnRoomDisconnected;
            room.ParticipantConnected -= OnParticipantConnected;
            room.ParticipantDisconnected -= OnParticipantDisconnected;
            room.DataReceived -= OnDataReceived;
            room.TrackSubscribed -= OnTrackSubscribed;
            room.TrackUnsubscribed -= OnTrackUnsubscribed;
            room.AudioPlaybackChanged -= OnAudioPlaybackChanged;
        }

        private void OnRoomReconnecting()
        {
            if (_isDisconnecting) return;
            SetState(TransportState.Reconnecting);
            Reconnecting?.Invoke();
        }

        private void OnRoomReconnected()
        {
            if (_isDisconnecting) return;
            SetState(TransportState.Connected);
            Reconnected?.Invoke();
        }

        private void OnRoomDisconnected(DisconnectReason? reason)
        {
            if (_isDisconnecting) return;

            Room roomToCleanup = _room;
            WebGLRoomFacade roomFacadeToCleanup = _roomFacade;

            // Prevent further callbacks on the old room and allow clean reconnection.
            UnsubscribeFromRoomEvents(roomToCleanup);
            try
            {
                roomFacadeToCleanup?.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }

            _room = null;
            _roomFacade = null;

            _currentSession = null;
            IsMicrophoneEnabled = false;
            IsMicrophoneMuted = false;
            _isAudioPlaybackActive = false;

            SetState(TransportState.Disconnected);

            TransportDisconnectReason mappedReason = WebGLDisconnectReasonMapper.Map(reason);
            Disconnected?.Invoke(mappedReason);

            // Cleanup after the callback completes to avoid disposing the room inside LiveKit event dispatch.
            _coroutineRunner.StartCoroutine(CleanupAfterDisconnect(roomToCleanup, roomFacadeToCleanup));
        }

        private IEnumerator CleanupAfterDisconnect(Room roomToCleanup, WebGLRoomFacade roomFacadeToCleanup)
        {
            yield return null;
            CleanupRoom(roomToCleanup, roomFacadeToCleanup);
        }

        private void OnParticipantConnected(RemoteParticipant participant)
        {
            if (_isDisconnecting) return;

            ParticipantConnected?.Invoke(new TransportParticipantInfo(
                participant.Sid,
                participant.Identity,
                false,
                participant.Metadata
            ));
        }

        private void OnParticipantDisconnected(RemoteParticipant participant)
        {
            if (_isDisconnecting) return;

            ParticipantDisconnected?.Invoke(new TransportParticipantInfo(
                participant.Sid,
                participant.Identity,
                false,
                participant.Metadata
            ));
        }

        private void OnDataReceived(byte[] data, RemoteParticipant participant, DataPacketKind? kind)
        {
            if (_isDisconnecting) return;

            DataReceived?.Invoke(new DataPacket(
                new ReadOnlyMemory<byte>(data),
                null, // WebGL SDK doesn't provide topic in callback
                kind == DataPacketKind.RELIABLE
                    ? Transport.DataPacketKind.Reliable
                    : Transport.DataPacketKind.Lossy,
                participant?.Sid
            ));
        }

        private void OnTrackSubscribed(RemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (_isDisconnecting) return;

            if (track.Kind == TrackKind.Audio)
                track.Attach();

            TrackSubscribed?.Invoke(new TrackInfo(
                track.Sid,
                participant.Sid,
                participant.Identity,
                track.Kind == TrackKind.Audio ? Transport.TrackKind.Audio : Transport.TrackKind.Video,
                publication.TrackName
            ));
        }

        private void OnTrackUnsubscribed(RemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (_isDisconnecting) return;

            TrackUnsubscribed?.Invoke(new TrackInfo(
                track.Sid,
                participant.Sid,
                participant.Identity,
                track.Kind == TrackKind.Audio ? Transport.TrackKind.Audio : Transport.TrackKind.Video,
                publication.TrackName
            ));
        }

        private void OnAudioPlaybackChanged(bool canPlayback)
        {
            if (_isDisconnecting) return;

            _isAudioPlaybackActive = canPlayback;
            AudioPlaybackStateChanged?.Invoke(canPlayback);
        }

        #endregion

        #region Helper Methods

        private void SetState(TransportState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(newState);
        }

        private void CleanupRoom()
        {
            CleanupRoom(_room, _roomFacade);
            _roomFacade = null;
            _room = null;
        }

        private void CleanupRoom(Room roomToCleanup, WebGLRoomFacade roomFacadeToCleanup)
        {
            if (roomFacadeToCleanup != null)
            {
                try
                {
                    roomFacadeToCleanup.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            if (roomToCleanup != null)
            {
                UnsubscribeFromRoomEvents(roomToCleanup);
                // Intentionally swallow exceptions during cleanup - room may already be in invalid state
                try { roomToCleanup.Disconnect(); }
                catch
                {
                    /* Ignored during cleanup */
                }

                try { roomToCleanup.Dispose(); }
                catch
                {
                    /* Ignored during cleanup */
                }
            }
        }

        private static void LogTransportInfo(string message) => ConvaiLogger.Info(message, LogCategory.Transport);

        private static void LogTransportWarning(string message) => ConvaiLogger.Warning(message, LogCategory.Transport);

        private static void LogTransportError(string message) => ConvaiLogger.Error(message, LogCategory.Transport);

        private static void LogTransportError(Exception exception, string message) =>
            ConvaiLogger.Instance.Error(exception, message, LogCategory.Transport);

        private static void LogAudioInfo(string message) => ConvaiLogger.Info(message, LogCategory.Audio);

        private static void LogAudioWarning(string message) => ConvaiLogger.Warning(message, LogCategory.Audio);

        private static void LogAudioError(string message) => ConvaiLogger.Error(message, LogCategory.Audio);

        private static void LogAudioError(Exception exception, string message) =>
            ConvaiLogger.Instance.Error(exception, message, LogCategory.Audio);

        #endregion
    }
}
