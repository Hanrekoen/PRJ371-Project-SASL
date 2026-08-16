using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Behaviors;
using UnityEngine;
using UnityEngine.Scripting;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Mock transport provider for headless testing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Testing Only</b>: Provides stub implementations that don't require
    ///         network connectivity or platform-specific dependencies.
    ///     </para>
    ///     <para>
    ///         <b>Thread-Safe</b>: All operations are thread-safe.
    ///     </para>
    ///     <para>
    ///         <b>IL2CPP Safety</b>: Uses [Preserve] attribute to prevent stripping.
    ///     </para>
    /// </remarks>
    [Preserve]
    internal sealed class MockTransportProvider : ITransportProvider
    {
        private static readonly Lazy<MockTransportProvider> LazyInstance = new(() => new MockTransportProvider());

        private MockTransportProvider()
        {
        }

        /// <summary>
        ///     Gets the singleton instance of the mock transport provider.
        /// </summary>
        public static MockTransportProvider Instance => LazyInstance.Value;

        /// <inheritdoc />
        public TransportCapabilities Capabilities => new()
        {
            Platform = TransportPlatform.Desktop, // Use Desktop as placeholder for mock
            SupportsSpatialAudio = false,
            SupportsVideo = false,
            SupportsScreenShare = false,
            SupportsUnityAudioSource = false,
            RequiresUserGestureForAudio = false,
            SupportsMicrophoneSelection = false
        };

        /// <inheritdoc />
        public IRealtimeTransport CreateTransport() => new MockRealtimeTransport();

        /// <inheritdoc />
        public IMicrophoneSourceFactory CreateMicrophoneFactory() => new MockMicrophoneSourceFactory();

        /// <inheritdoc />
        public IAudioStreamFactory CreateAudioStreamFactory() => new MockAudioStreamFactory();

        /// <inheritdoc />
        public IVideoSourceFactory CreateVideoSourceFactory() => new MockVideoSourceFactory();

        /// <inheritdoc />
        public IConvaiRoomControllerFactory CreateRoomControllerFactory() => new MockRoomControllerFactory();
    }

    /// <summary>
    ///     Mock microphone source factory for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockMicrophoneSourceFactory : IMicrophoneSourceFactory
    {
        public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null)
            => new MockMicrophoneSource(deviceName ?? "MockMicrophone", deviceIndex);

        public string[] GetAvailableDevices() => new[] { "MockMicrophone" };
    }

    /// <summary>
    ///     Mock microphone source for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockMicrophoneSource : IMicrophoneSource
    {
        public MockMicrophoneSource(string deviceName, int deviceIndex)
        {
            DeviceName = deviceName;
            DeviceIndex = deviceIndex;
            Name = deviceName;
        }

        // IAudioSource
        public string Name { get; }
        public bool IsCapturing { get; private set; }
        public void StartCapture() => IsCapturing = true;
        public void StopCapture() => IsCapturing = false;

        // IMicrophoneSource
        public string DeviceName { get; }
        public int DeviceIndex { get; }
        public event Action<IMicrophoneSource, MicrophoneSessionInvalidationReason> SessionInvalidated
        {
            add { }
            remove { }
        }
        public bool IsMuted { get; set; }
        public bool EnableAcousticEchoCancellation { get; set; }

        public void Dispose() => IsCapturing = false;
    }

    /// <summary>
    ///     Mock audio stream factory for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockAudioStreamFactory : IAudioStreamFactory
    {
        public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource)
            => new MockAudioStreamDisposable();
    }

    /// <summary>
    ///     Mock audio stream disposable for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockAudioStreamDisposable : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>
    ///     Mock realtime transport for testing.
    /// </summary>
    [Preserve]
#pragma warning disable CS0067 // Events satisfy IRealtimeTransport; not raised in this stub.
    internal sealed class MockRealtimeTransport : IRealtimeTransport
    {
        public TransportState State { get; private set; } = TransportState.Disconnected;
        public TransportSessionInfo? CurrentSession => null;
        public TransportCapabilities Capabilities => new() { Platform = TransportPlatform.Desktop };
        public AudioRuntimeState AudioState => new();
        public bool IsConnected => State == TransportState.Connected;
        public IRoomFacade Room => null;
        public bool IsMicrophoneEnabled => false;
        public bool IsMicrophoneMuted => false;

        public event Action<DataPacket> DataReceived;
        public event Action<TransportSessionInfo> Connected;
        public event Action<DisconnectReason> Disconnected;
        public event Action<TransportError> ConnectionFailed;
        public event Action Reconnecting;
        public event Action Reconnected;
        public event Action<TransportState> StateChanged;
        public event Action<TransportParticipantInfo> ParticipantConnected;
        public event Action<TransportParticipantInfo> ParticipantDisconnected;
        public event Action<TrackInfo> TrackSubscribed;
        public event Action<TrackInfo> TrackUnsubscribed;
        public event Action<bool> MicrophoneEnabledChanged;
        public event Action<bool> MicrophoneMuteChanged;
        public event Action<bool> AudioPlaybackStateChanged;

        public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
            string[] destinationIdentities = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
            CancellationToken ct = default)
        {
            State = TransportState.Connected;
            StateChanged?.Invoke(State);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken ct = default)
        {
            State = TransportState.Disconnected;
            StateChanged?.Invoke(State);
            Disconnected?.Invoke(reason);
            return Task.CompletedTask;
        }

        public void EnableAudio() { }

        public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task DisableMicrophoneAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void SetMicrophoneMuted(bool muted) { }
        public bool CanEnableMicrophone() => true;
        public bool CanEnableAudio() => true;
        public void Dispose() => State = TransportState.Disconnected;
    }

#pragma warning restore CS0067

    /// <summary>
    ///     Mock video source factory for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockVideoSourceFactory : IVideoSourceFactory
    {
        public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null)
            => new MockTextureVideoSource();

        public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null)
            => new MockTextureVideoSource();

        public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15)
            => new MockTextureVideoSource();
    }

    /// <summary>
    ///     Mock texture video source for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockTextureVideoSource : ITextureVideoSource
    {
        public bool IsCapturing { get; private set; }
        public string Name => "MockTextureVideoSource";
        public int Width => 640;
        public int Height => 480;
        public Texture SourceTexture { get; private set; }
        public int TargetFrameRate { get; set; } = 30;

        public void StartCapture() => IsCapturing = true;
        public void StopCapture() => IsCapturing = false;
        public void SetTexture(Texture texture) => SourceTexture = texture;
        public void Dispose() => IsCapturing = false;
    }

    /// <summary>
    ///     Mock room controller factory for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockRoomControllerFactory : IConvaiRoomControllerFactory
    {
        public IConvaiRoomController Create(
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            ITransportConfiguration transportConfiguration,
            ISessionPersistence sessionPersistence,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            INarrativeSectionNameResolver sectionNameResolver = null) =>
            new MockConvaiRoomController();
    }

    /// <summary>
    ///     Mock room controller for testing.
    /// </summary>
    [Preserve]
#pragma warning disable CS0067 // Events satisfy IConvaiRoomController; not raised in this stub.
    internal sealed class MockConvaiRoomController : IConvaiRoomController
    {
        // IRoomConnectionState
        public bool HasRoomDetails => false;
        public bool IsConnectedToRoom => false;
        public bool IsMicMuted => false;
        public string SessionID => "mock-session";
        public string CharacterSessionID => null;
        public string RoomName => null;
        public string RoomURL => null;
        public string Token => null;
        public string ResolvedSpeakerId => null;
        public string RequestTraceId => null;
        public string ResolvedEndUserId => null;
        public IReadOnlyDictionary<string, object> ResolvedEndUserMetadata => null;
        public RTVIHandler RTVIHandler => null;
        public IRoomFacade CurrentRoom => null;

        // IRoomConnectionInitializer
        public Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext) => Task.FromResult(RoomConnectionAttemptResult.Fail(
            ConnectionFailure.Create(
                SessionErrorCodes.ConnectionFailed,
                "Mock room controller is not configured to connect.",
                SessionErrorStage.Configuration)));

        public Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken = default) => Task.FromResult(RoomConnectionAttemptResult.Fail(
            ConnectionFailure.Create(
                SessionErrorCodes.ConnectionFailed,
                "Mock room controller is not configured to connect.",
                SessionErrorStage.Configuration)));

        public void DisconnectFromRoom() { }
        public Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        // IRoomAudioControl
        public void SetMicMuted(bool mute) { }
        public void ToggleMicMute() { }
        public bool SetCharacterAudioMuted(string characterId, bool mute) => false;
        public bool MuteCharacter(string characterId) => false;
        public bool UnmuteCharacter(string characterId) => false;
        public bool IsCharacterAudioMuted(string characterId) => false;
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy) { }
        public void ApplyRemoteAudioPreference(string characterId, bool enabled) { }

        // IRoomControllerEvents
        public event Action OnRoomConnectionSuccessful;
        public event Action OnRoomConnectionFailed;
        public event Action<bool> OnMicMuteChanged;
        public event Action OnRoomReconnecting;
        public event Action OnRoomReconnected;
        public event Action OnUnexpectedRoomDisconnected;
        public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed;
        public event Action<string, string> OnRemoteAudioTrackUnsubscribed;

        public void Dispose() { }
    }

#pragma warning restore CS0067
}
