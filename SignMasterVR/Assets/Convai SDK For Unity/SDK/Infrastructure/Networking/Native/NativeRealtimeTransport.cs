using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Connection;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
// Type aliases to disambiguate between LiveKit.Proto and Transport types
using TransportDisconnectReason = Convai.Infrastructure.Networking.Transport.DisconnectReason;
using TransportParticipantInfo = Convai.Infrastructure.Networking.Transport.TransportParticipantInfo;
using TransportTrackInfo = Convai.Infrastructure.Networking.Transport.TrackInfo;
using LKRemoteTrack = LiveKit.IRemoteTrack;
using Object = UnityEngine.Object;
using RoomOptions = LiveKit.RoomOptions;
using TrackKind = LiveKit.Proto.TrackKind;

namespace Convai.Infrastructure.Networking.Native
{
    internal interface INativeMicrophonePublisher : IDisposable
    {
        public bool IsEnabled { get; }
        public bool IsMuted { get; }
        public Task<bool> EnableAsync(int microphoneDeviceIndex, CancellationToken ct);
        public Task DisableAsync(CancellationToken ct);
        public void SetMuted(bool muted);
    }

    internal sealed class NativeMicrophonePublisher : INativeMicrophonePublisher
    {
        private readonly Func<GameObject> _audioSourceHolderProvider;
        private readonly LiveKitRoomBackend _backend;
        private readonly ILogger _logger;
        private LocalAudioTrack _localAudioTrack;
        private MicrophoneSource _microphoneSource;

        public NativeMicrophonePublisher(LiveKitRoomBackend backend, ILogger logger,
            Func<GameObject> audioSourceHolderProvider)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).WithTag("NativeTransport");
            _audioSourceHolderProvider = audioSourceHolderProvider ??
                                         throw new ArgumentNullException(nameof(audioSourceHolderProvider));
        }

        public bool IsEnabled => _localAudioTrack != null;

        public bool IsMuted { get; private set; }

        public async Task<bool> EnableAsync(int microphoneDeviceIndex, CancellationToken ct)
        {
            if (_backend.Room?.LocalParticipant == null)
            {
                _logger.Warning("Room not connected. Cannot enable microphone.");
                return false;
            }

            if (IsEnabled)
            {
                _logger.Warning("Microphone already enabled.");
                return true;
            }

            try
            {
                string[] devices = Microphone.devices;
                if (devices.Length == 0)
                {
                    _logger.Error("No microphone devices available.");
                    return false;
                }

                string deviceName = microphoneDeviceIndex < devices.Length
                    ? devices[microphoneDeviceIndex]
                    : devices[0];
                _logger.Info($"Using microphone device: {deviceName}");

                GameObject audioHolder = _audioSourceHolderProvider();
                _microphoneSource = new MicrophoneSource(deviceName, audioHolder);
                _microphoneSource.Start();

                _localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", _microphoneSource, _backend.Room);

                var publishOptions = new TrackPublishOptions { Source = TrackSource.SourceMicrophone };
                PublishTrackInstruction publishInstruction =
                    _backend.Room.LocalParticipant.PublishTrack(_localAudioTrack, publishOptions);

                while (!publishInstruction.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(50, ct);
                }

                if (publishInstruction.IsError)
                {
                    _logger.Error($"Failed to publish microphone track: {publishInstruction}");
                    Cleanup();
                    return false;
                }

                IsMuted = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Microphone enable cancelled");
                Cleanup();
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to enable microphone: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        public async Task DisableAsync(CancellationToken ct)
        {
            if (!IsEnabled) return;

            try
            {
                if (_localAudioTrack != null && _backend.Room?.LocalParticipant != null)
                {
                    UnpublishTrackInstruction unpublishInstruction =
                        _backend.Room.LocalParticipant.UnpublishTrack(_localAudioTrack, true);

                    while (!unpublishInstruction.IsDone) await Task.Delay(50, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error unpublishing audio track: {ex.Message}");
            }

            Cleanup();
        }

        public void SetMuted(bool muted)
        {
            if (!IsEnabled)
            {
                _logger.Warning("Microphone not enabled. Cannot set mute state.");
                return;
            }

            _microphoneSource?.SetMute(muted);
            IsMuted = muted;
        }

        public void Dispose() => Cleanup();

        private void Cleanup()
        {
            try
            {
                _microphoneSource?.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error stopping microphone: {ex.Message}");
            }

            try
            {
                _microphoneSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disposing microphone source: {ex.Message}");
            }

            NativeMicrophoneSource.EnsureAllRecordingDevicesReleased();

            _localAudioTrack = null;
            _microphoneSource = null;
            IsMuted = false;
        }
    }

    /// <summary>
    ///     Native (desktop/mobile) implementation of <see cref="IRealtimeTransport" />.
    ///     Wraps the LiveKit native backend and adds audio track management.
    /// </summary>
    /// <remarks>
    ///     Key native-specific behaviors:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Uses async/await for all operations</description>
    ///         </item>
    ///         <item>
    ///             <description>Audio plays through Unity AudioSource components</description>
    ///         </item>
    ///         <item>
    ///             <description>Supports microphone device selection</description>
    ///         </item>
    ///         <item>
    ///             <description>No user gesture required for audio</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    internal class NativeRealtimeTransport : IRealtimeTransport
    {
        #region Constructor

        /// <summary>
        ///     Creates a new native transport instance.
        /// </summary>
        /// <param name="backend">The underlying LiveKit room backend.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="audioSourceHolder">GameObject to hold audio sources (optional).</param>
        public NativeRealtimeTransport(LiveKitRoomBackend backend, ILogger logger,
            GameObject audioSourceHolder = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).WithTag("NativeTransport");
            _audioSourceHolder = audioSourceHolder;
            _microphonePublisher = new NativeMicrophonePublisher(_backend, _logger, GetOrCreateAudioSourceHolder);

            // Subscribe to backend events.
            _backend.DataPacketReceived += OnDataPacketReceived;
            _backend.RoomDisconnected += OnRoomDisconnected;
            _backend.RoomReconnecting += OnRoomReconnecting;
            _backend.RoomReconnected += OnRoomReconnected;
            _backend.ParticipantConnected += OnParticipantConnected;
            _backend.ParticipantDisconnected += OnParticipantDisconnected;
            _backend.TrackSubscribed += OnTrackSubscribed;
            _backend.TrackUnsubscribed += OnTrackUnsubscribed;
        }

        #endregion

        #region IRealtimeTransport - Data Methods

        /// <inheritdoc />
        public async Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
            string[] destinationIdentities = null, CancellationToken ct = default)
        {
            if (_backend.Room?.LocalParticipant == null)
            {
                _logger.Warning("Room not connected. Cannot send data.");
                return;
            }

            try
            {
                await _backend.SendDataAsync(payload, reliable, topic ?? string.Empty, ct);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending data: {ex.Message}");
                throw;
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

            // Unsubscribe from events
            _backend.DataPacketReceived -= OnDataPacketReceived;
            _backend.RoomDisconnected -= OnRoomDisconnected;
            _backend.RoomReconnecting -= OnRoomReconnecting;
            _backend.RoomReconnected -= OnRoomReconnected;
            _backend.ParticipantConnected -= OnParticipantConnected;
            _backend.ParticipantDisconnected -= OnParticipantDisconnected;
            _backend.TrackSubscribed -= OnTrackSubscribed;
            _backend.TrackUnsubscribed -= OnTrackUnsubscribed;

            _microphonePublisher.Dispose();
            DisposeRoomFacade();
            DestroyOwnedAudioSourceHolder();
            _currentSession = null;

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Helper Methods

        private void SetState(TransportState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(newState);
        }

        #endregion

        #region Private Fields

        private readonly LiveKitRoomBackend _backend;
        private readonly ILogger _logger;
        private readonly GameObject _audioSourceHolder;

        private GameObject _ownedAudioSourceHolder;
        private bool _ownsAudioSourceHolder;

        private TransportSessionInfo? _currentSession;
        private bool _isDisconnecting;
        private bool _disposed;
        private readonly INativeMicrophonePublisher _microphonePublisher;

        #endregion

        #region IRealtimeTransport - State Properties

        /// <inheritdoc />
        public TransportState State { get; private set; } = TransportState.Disconnected;

        /// <inheritdoc />
        public TransportSessionInfo? CurrentSession => _currentSession;

        /// <inheritdoc />
        public TransportCapabilities Capabilities => NativeTransportPlatformResolver.GetCapabilities();

        /// <inheritdoc />
        public AudioRuntimeState AudioState => new(
            true, // Always active on native
            IsMicrophoneEnabled,
            IsMicrophoneMuted
        );

        /// <inheritdoc />
        public bool IsConnected => State == TransportState.Connected;

        /// <inheritdoc />
        public IRoomFacade Room { get; private set; }

        /// <inheritdoc />
        public bool IsMicrophoneEnabled => _microphonePublisher.IsEnabled;

        /// <inheritdoc />
        public bool IsMicrophoneMuted => _microphonePublisher.IsMuted;

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
        public event Action<TransportTrackInfo> TrackSubscribed;

        /// <inheritdoc />
        public event Action<TransportTrackInfo> TrackUnsubscribed;

        /// <inheritdoc />
        public event Action<bool> MicrophoneEnabledChanged;

        /// <inheritdoc />
        public event Action<bool> MicrophoneMuteChanged;

        /// <inheritdoc />
        public event Action<bool> AudioPlaybackStateChanged;

        #endregion

        #region IRealtimeTransport - Connection Methods

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
            CancellationToken ct = default)
        {
            if (State != TransportState.Disconnected)
            {
                _logger.Warning("Already connected or connecting");
                return false;
            }

            SetState(TransportState.Connecting);

            try
            {
                RoomOptions roomOptions = options != null
                    ? new RoomOptions
                    {
                        AutoSubscribe = options.AutoSubscribe,
                        AdaptiveStream = options.AdaptiveStream,
                        Dynacast = options.Dynacast
                    }
                    : null;

                LiveKitConnectResult result = await _backend.ConnectAsync(url, token, roomOptions, ct);

                if (result.Success)
                {
                    _currentSession = new TransportSessionInfo(
                        result.RoomName ?? _backend.Room?.Name ?? "Unknown",
                        result.SessionId,
                        localParticipantId: _backend.Room?.LocalParticipant?.Sid,
                        localParticipantIdentity: _backend.Room?.LocalParticipant?.Identity,
                        connectedAt: DateTime.UtcNow
                    );

                    DisposeRoomFacade();
                    Room = _backend.Room != null ? new NativeRoomFacade(_backend.Room) : null;

                    SetState(TransportState.Connected);
                    Connected?.Invoke(_currentSession.Value);
                    _logger.Info($"Connected to room: {_currentSession.Value.RoomName}");
                    return true;
                }

                SetState(TransportState.Disconnected);
                ConnectionFailed?.Invoke(new TransportError(result.ErrorMessage ?? "Connection failed",
                    TransportErrorCode.NetworkError));
                return false;
            }
            catch (OperationCanceledException)
            {
                SetState(TransportState.Disconnected);
                _logger.Warning("Connection cancelled");
                return false;
            }
            catch (Exception ex)
            {
                SetState(TransportState.Disconnected);
                ConnectionFailed?.Invoke(new TransportError(ex.Message, TransportErrorCode.Unknown, ex));
                _logger.Error($"Connection error: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync(TransportDisconnectReason reason = TransportDisconnectReason.ClientInitiated,
            CancellationToken ct = default)
        {
            if (_isDisconnecting || State == TransportState.Disconnected) return;

            _isDisconnecting = true;
            SetState(TransportState.Disconnecting);

            try
            {
                // Disable microphone first
                if (IsMicrophoneEnabled) await DisableMicrophoneAsync(ct);

                await _backend.DisconnectAsync(reason, ct);

                _currentSession = null;
                DisposeRoomFacade();
                SetState(TransportState.Disconnected);
                Disconnected?.Invoke(reason);
                _logger.Info("Disconnected");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error during disconnect: {ex.Message}");
                SetState(TransportState.Disconnected);
                Disconnected?.Invoke(TransportDisconnectReason.TransportError);
            }
            finally
            {
                _isDisconnecting = false;
            }
        }

        #endregion

        #region IRealtimeTransport - Audio Methods

        /// <inheritdoc />
        public void EnableAudio()
        {
            // On native platforms, audio is always enabled - this is a no-op
            AudioPlaybackStateChanged?.Invoke(true);
        }

        /// <inheritdoc />
        public async Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default)
        {
            bool enabled = await _microphonePublisher.EnableAsync(microphoneDeviceIndex, ct);
            if (!enabled)
                return false;

            MicrophoneEnabledChanged?.Invoke(true);
            MicrophoneMuteChanged?.Invoke(false);
            _logger.Info("Microphone enabled and publishing");
            return true;
        }

        /// <inheritdoc />
        public async Task DisableMicrophoneAsync(CancellationToken ct = default)
        {
            if (!IsMicrophoneEnabled) return;

            await _microphonePublisher.DisableAsync(ct);
            MicrophoneEnabledChanged?.Invoke(false);
            MicrophoneMuteChanged?.Invoke(false);
            _logger.Info("Microphone disabled");
        }

        /// <inheritdoc />
        public void SetMicrophoneMuted(bool muted)
        {
            if (!IsMicrophoneEnabled)
            {
                _logger.Warning("Microphone not enabled. Cannot set mute state.");
                return;
            }

            _microphonePublisher.SetMuted(muted);
            MicrophoneMuteChanged?.Invoke(muted);
            _logger.Info($"Microphone muted: {muted}");
        }

        /// <inheritdoc />
        public bool CanEnableMicrophone() =>
            State == TransportState.Connected && !IsMicrophoneEnabled && Microphone.devices.Length > 0;

        /// <inheritdoc />
        public bool CanEnableAudio()
        {
            // On native platforms, audio is always available
            return true;
        }

        private void DisposeRoomFacade()
        {
            if (Room == null) return;

            try
            {
                if (Room is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disposing room facade: {ex.Message}");
            }
            finally
            {
                Room = null;
            }
        }

        private GameObject GetOrCreateAudioSourceHolder()
        {
            if (_audioSourceHolder != null) return _audioSourceHolder;

            if (_ownedAudioSourceHolder != null) return _ownedAudioSourceHolder;

            var go = new GameObject("NativeTransport_MicrophoneSource");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);

            _ownedAudioSourceHolder = go;
            _ownsAudioSourceHolder = true;
            return go;
        }

        private void DestroyOwnedAudioSourceHolder()
        {
            if (!_ownsAudioSourceHolder) return;

            if (_ownedAudioSourceHolder != null)
            {
                if (UnityEngine.Application.isPlaying)
                    Object.Destroy(_ownedAudioSourceHolder);
                else
                    Object.DestroyImmediate(_ownedAudioSourceHolder);
            }

            _ownedAudioSourceHolder = null;
            _ownsAudioSourceHolder = false;
        }

        #endregion

        #region Event Handlers

        private void OnDataPacketReceived(DataPacket packet)
        {
            if (_isDisconnecting) return;
            DataReceived?.Invoke(packet);
        }

        private void OnRoomDisconnected(Room room)
        {
            if (_isDisconnecting) return;

            _currentSession = null;
            _microphonePublisher.Dispose();
            DisposeRoomFacade();
            MicrophoneEnabledChanged?.Invoke(false);
            MicrophoneMuteChanged?.Invoke(false);
            SetState(TransportState.Disconnected);
            Disconnected?.Invoke(TransportDisconnectReason.RemoteHangUp);
        }

        private void OnRoomReconnecting(Room room)
        {
            if (_isDisconnecting) return;
            SetState(TransportState.Reconnecting);
            Reconnecting?.Invoke();
        }

        private void OnRoomReconnected(Room room)
        {
            if (_isDisconnecting) return;
            SetState(TransportState.Connected);
            Reconnected?.Invoke();
        }

        private void OnParticipantConnected(Participant participant)
        {
            if (_isDisconnecting) return;
            ParticipantConnected?.Invoke(new TransportParticipantInfo(
                participant.Sid,
                participant.Identity,
                participant is LocalParticipant,
                participant.Metadata
            ));
        }

        private void OnParticipantDisconnected(Participant participant)
        {
            if (_isDisconnecting) return;
            ParticipantDisconnected?.Invoke(new TransportParticipantInfo(
                participant.Sid,
                participant.Identity,
                false,
                participant.Metadata
            ));
        }

        private void OnTrackSubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (_isDisconnecting) return;
            TrackSubscribed?.Invoke(new TransportTrackInfo(
                track.Sid,
                participant.Sid,
                participant.Identity,
                track.Kind == TrackKind.KindAudio ? Transport.TrackKind.Audio : Transport.TrackKind.Video,
                publication.Name
            ));
        }

        private void OnTrackUnsubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (_isDisconnecting) return;
            TrackUnsubscribed?.Invoke(new TransportTrackInfo(
                track.Sid,
                participant.Sid,
                participant.Identity,
                track.Kind == TrackKind.KindAudio ? Transport.TrackKind.Audio : Transport.TrackKind.Video,
                publication.Name
            ));
        }

        #endregion
    }
}
