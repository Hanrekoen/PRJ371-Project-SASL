using System;
using System.Collections.Generic;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Audio;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Networking.Transport;
using Convai.RestAPI.Services;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.Room;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomManagerRuntimeContext
    {
        public IAgentRegistry AgentRegistry { get; set; }
        public ILogger Logger { get; set; }
        public IEventHub EventHub { get; set; }
        public ISessionStateMachine SessionStateMachine { get; set; }
        public ISessionService SessionService { get; set; }
        public Func<string> SessionIdProvider { get; set; }
        public Func<SessionState> CurrentStateProvider { get; set; }
        public Func<bool> IsConnectedProvider { get; set; }
        public Func<bool> CanConnectProvider { get; set; }
        public Func<int> StartWaitTimeoutProvider { get; set; }
        public Func<bool> PrepareConnection { get; set; }
        public Func<IConvaiCharacterAgent> ActiveCharacterProvider { get; set; }
        public Func<ConnectionContext> ConnectionContextProvider { get; set; }
        public Action<ConnectionContext> SetConnectionContext { get; set; }
        public Func<ReconnectPolicy> ResolveReconnectPolicy { get; set; }
        public Action<ReconnectPolicy> SetReconnectPolicy { get; set; }
        public Action<bool> OnConnectAttemptStarted { get; set; }
        public Func<IConvaiRoomController> ControllerProvider { get; set; }
        public Func<ConvaiConnectionType> ConnectionTypeProvider { get; set; }
        public Func<ICredentialProvider> CredentialProvider { get; set; }
        public Func<string> CoreServerUrlProvider { get; set; }
        public Func<TurnTakingOptions> ConfiguredTurnTakingOptionsProvider { get; set; }
        public Func<UserVadSettings> ConfiguredUserVadSettingsProvider { get; set; }
        public Func<RoomVisionInputConfig> VisionInputConfigProvider { get; set; }
        public Func<RoomRespondModesConfig> RespondModesProvider { get; set; }
        public Func<RoomSessionConnectOptions> ConsumePendingConnectOptions { get; set; }
        public Action<TurnTakingOptions, ResolvedTurnTakingOptions> PrepareSessionTurnTakingState { get; set; }
        public Action<ResolvedTurnTakingOptions> SetCurrentResolvedTurnTakingOptions { get; set; }
        public Func<ResolvedTurnTakingOptions> CurrentResolvedTurnTakingOptionsProvider { get; set; }
        public Func<ISessionPersistence> SessionPersistenceProvider { get; set; }
        public Func<AudioTrackManager> AudioTrackManagerProvider { get; set; }
        public Action<SessionState, SessionError?> UpdateSessionState { get; set; }
        public Action<string, string, string, string> RecordConnectionSuccess { get; set; }
        public Action<ConnectionFailure> RecordConnectionFailure { get; set; }
        public Action<bool, string> CompleteDisconnectionTracking { get; set; }
        public Action ResetBargeInCoordinatorState { get; set; }
        public Func<bool> RequiresUserGestureForAudioProvider { get; set; }
        public Func<bool> IsAudioPlaybackActiveProvider { get; set; }
        public Func<IMicrophoneSourceFactory> MicrophoneSourceFactoryProvider { get; set; }
        public Action<IMicrophoneSourceFactory> SetMicrophoneSourceFactory { get; set; }
        public Func<ITransportProvider> TransportProviderProvider { get; set; }
        public Func<string> PreferredMicrophoneDeviceIdProvider { get; set; }
        public Func<IMicrophoneDeviceService> MicrophoneDeviceServiceProvider { get; set; }
        public Func<GameObject> GameObjectProvider { get; set; }
        public Func<IReadOnlyList<IConvaiCharacterAgent>> CharacterListProvider { get; set; }
        public Action<bool> HandleMicMuteChanged { get; set; }
        public Action HandleUnexpectedRoomDisconnected { get; set; }
        public Action<IRemoteAudioTrack, string, string> HandleRemoteAudioTrackSubscribed { get; set; }
        public Action<string, string> HandleRemoteAudioTrackUnsubscribed { get; set; }
    }

    internal sealed class RoomRuntimeAssembly
    {
        public RoomSessionDiagnostics SessionDiagnostics { get; set; }
        public RoomCharacterLifecycleCoordinator CharacterLifecycleCoordinator { get; set; }
        public RoomDisconnectRuntimeAdapter DisconnectRuntimeAdapter { get; set; }
        public RoomControllerEventBinder RoomControllerEventBinder { get; set; }
        public RemoteAudioPreferenceManager RemoteAudioPreferences { get; set; }
        public RoomConnectionRuntimeAdapter ConnectionRuntimeAdapter { get; set; }
        public RoomAudioRuntimeAdapter AudioRuntimeAdapter { get; set; }
        public RoomRuntime RoomRuntime { get; set; }
        public IRoomDiagnostics DiagnosticsCoordinator { get; set; }
        public IRoomConnectionCoordinator ConnectionCoordinator { get; set; }
        public RoomAudioCoordinator AudioCoordinator { get; set; }
        public IRoomOwnershipCoordinator OwnershipCoordinator { get; set; }
    }

    internal sealed class RoomRuntimeAssemblyFactory
    {
        public RoomRuntimeAssembly Create(RoomManagerRuntimeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var remoteAudioPreferences = new RemoteAudioPreferenceManager(context.AgentRegistry, context.Logger);

            RoomConnectionRuntimeAdapter connectionRuntimeAdapter = null;
            var sessionDiagnostics = new RoomSessionDiagnostics(
                context.SessionStateMachine,
                context.SessionService,
                context.EventHub,
                context.Logger,
                context.SessionIdProvider,
                onConnectionStateChanged: state => connectionRuntimeAdapter?.SignalConnectionStateWaiters(state));

            var characterLifecycleCoordinator = new RoomCharacterLifecycleCoordinator(
                context.CurrentStateProvider,
                context.CharacterListProvider,
                () => context.AgentRegistry,
                context.AudioTrackManagerProvider,
                state => context.UpdateSessionState?.Invoke(state, null));

            var roomControllerEventBinder = new RoomControllerEventBinder(
                context.HandleMicMuteChanged,
                context.HandleUnexpectedRoomDisconnected,
                context.HandleRemoteAudioTrackSubscribed,
                context.HandleRemoteAudioTrackUnsubscribed);

            var disconnectRuntimeAdapter = new RoomDisconnectRuntimeAdapter(
                context.AudioTrackManagerProvider,
                context.ControllerProvider,
                context.UpdateSessionState,
                context.CompleteDisconnectionTracking,
                context.ResetBargeInCoordinatorState,
                context.Logger);

            connectionRuntimeAdapter = new RoomConnectionRuntimeAdapter(
                context.CurrentStateProvider,
                context.IsConnectedProvider,
                context.CanConnectProvider,
                context.StartWaitTimeoutProvider,
                context.PrepareConnection,
                context.ActiveCharacterProvider,
                context.ConnectionContextProvider,
                context.SetConnectionContext,
                context.ResolveReconnectPolicy,
                context.SetReconnectPolicy,
                context.OnConnectAttemptStarted,
                context.ControllerProvider,
                context.ConnectionTypeProvider,
                context.CoreServerUrlProvider,
                context.ConfiguredTurnTakingOptionsProvider,
                context.ConfiguredUserVadSettingsProvider,
                context.VisionInputConfigProvider,
                context.RespondModesProvider,
                context.ConsumePendingConnectOptions,
                context.PrepareSessionTurnTakingState,
                context.SetCurrentResolvedTurnTakingOptions,
                context.SessionPersistenceProvider,
                disconnectRuntimeAdapter,
                context.UpdateSessionState,
                context.RecordConnectionSuccess,
                context.RecordConnectionFailure,
                context.Logger,
                context.CredentialProvider);

            var audioRuntimeAdapter = new RoomAudioRuntimeAdapter(
                context.AudioTrackManagerProvider,
                context.ControllerProvider,
                context.RequiresUserGestureForAudioProvider,
                context.IsAudioPlaybackActiveProvider,
                context.MicrophoneSourceFactoryProvider,
                context.SetMicrophoneSourceFactory,
                context.TransportProviderProvider,
                context.CurrentResolvedTurnTakingOptionsProvider,
                context.PreferredMicrophoneDeviceIdProvider,
                context.MicrophoneDeviceServiceProvider,
                () => context.EventHub,
                context.GameObjectProvider,
                context.Logger);

            RoomRuntime roomRuntime = new RoomRuntimeBuilder()
                .WithConnectionAdapter(connectionRuntimeAdapter,
                    () => context.ControllerProvider?.Invoke()?.RoomName ?? string.Empty,
                    context.SessionIdProvider)
                .WithAudioAdapter(audioRuntimeAdapter)
                .WithRemoteAudioPreferences(remoteAudioPreferences)
                .WithAgentRegistry(context.AgentRegistry)
                .WithLogger(context.Logger)
                .Build();

            return new RoomRuntimeAssembly
            {
                SessionDiagnostics = sessionDiagnostics,
                CharacterLifecycleCoordinator = characterLifecycleCoordinator,
                DisconnectRuntimeAdapter = disconnectRuntimeAdapter,
                RoomControllerEventBinder = roomControllerEventBinder,
                RemoteAudioPreferences = remoteAudioPreferences,
                ConnectionRuntimeAdapter = connectionRuntimeAdapter,
                AudioRuntimeAdapter = audioRuntimeAdapter,
                RoomRuntime = roomRuntime,
                DiagnosticsCoordinator = roomRuntime.Diagnostics,
                ConnectionCoordinator = roomRuntime.Connection,
                AudioCoordinator = roomRuntime.Audio as RoomAudioCoordinator,
                OwnershipCoordinator = roomRuntime.Ownership
            };
        }
    }
}
