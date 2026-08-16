using System.Collections.Generic;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    public partial class ConvaiRoomManager
    {
        internal RoomOwnershipRebindOutcome HandleOwnedAgentStateChanged()
        {
            RoomOwnershipChangeResult result =
                _roomCompositionService.HandleOwnedAgentStateChanged(CreateCompositionContext(),
                    CreateCompositionState());

            if (result.Artifacts != null)
                ApplyCompositionArtifacts(result.Artifacts, result.ResetConnectionContext);

            if (result.ClearPendingReconnect)
                ClearPendingOwnershipReconnect();

            if (result.SetPendingReconnect)
                SetPendingOwnershipReconnect(result.PendingReconnectCharacterId);

            if (result.TransitionErrorToDisconnected)
                UpdateSessionState(SessionState.Disconnected);

            if (result.FailureReason != RoomStartupFailureReason.None)
                _roomCompositionService.LogOwnershipRebindFailure(_logger, result.FailureReason);

            return PublishOwnershipRebindOutcome(result.Outcome, result.RequestedCharacterId);
        }

        private bool PrepareOwnershipCompositionForNextConnect()
        {
            RoomCompositionPreparationResult result =
                _roomCompositionService.PrepareOwnershipCompositionForNextConnect(CreateCompositionContext(),
                    CreateCompositionState());

            if (!result.Success)
            {
                if (result.PublishedOutcome.HasValue)
                    PublishOwnershipRebindOutcome(result.PublishedOutcome.Value, result.PublishedCharacterId);

                _roomCompositionService.LogOwnershipRebindFailure(_logger, result.FailureReason);
                return false;
            }

            if (result.Artifacts != null)
                ApplyCompositionArtifacts(result.Artifacts, result.ResetConnectionContext);

            if (result.ClearPendingReconnect)
                ClearPendingOwnershipReconnect();

            if (result.PublishedOutcome.HasValue)
                PublishOwnershipRebindOutcome(result.PublishedOutcome.Value, result.PublishedCharacterId);

            return true;
        }

        private RoomCompositionContext CreateCompositionContext() =>
            new()
            {
                AgentRegistry = AgentRegistry,
                OwnershipProvider = _roomOwnershipProvider,
                EventHub = _eventHub,
                ControllerFactory = _controllerFactory,
                CredentialProvider = _credentialProvider,
                EndUserIdentityProvider = _endUserIdentityProvider,
                EndUserMetadataProvider = _endUserMetadataProvider,
                SessionPersistence = _sessionPersistence,
                Logger = _logger,
                SectionNameResolver = _sectionNameResolver,
                ConnectionType = EffectiveConnectionType,
                VideoTrackName = ResolveVideoTrackName(),
                ServerEndpoint = EffectiveServerEndpoint,
                Debug = EffectiveDebug,
                ConnectOnStart = EffectiveConnectOnStart,
                ReconnectPolicy = _reconnectPolicy,
                TransportProvider = _transportProvider,
                RemoteAudioPreferences = _remoteAudioPreferences,
                RoomControllerEventBinder = _roomControllerEventBinder,
                SessionDiagnostics = _sessionDiagnostics,
                HandleMicMuteChanged = HandleMicMuteChanged,
                CurrentRoomProvider = () => CurrentRoom,
                GetVisionComponentFlags = GetVisionComponentFlags,
                PersistentDataPath = UnityEngine.Application.persistentDataPath,
                ResolveLipSyncTransportOptions = ResolveLipSyncTransportOptions,
                PostToMainThread = UnityScheduler.Post,
                EnsureRuntimeSettingsDependencies = EnsureRuntimeSettingsDependencies
            };

        private RoomCompositionState CreateCompositionState() =>
            new()
            {
                IsInjected = _isInjected,
                HasStarted = _roomConnectionRuntimeAdapter?.HasStarted ?? false,
                CurrentState = CurrentState,
                HasPendingOwnershipReconnect = HasPendingOwnershipReconnect,
                PendingOwnershipRequestedCharacterId = _pendingOwnershipRequestedCharacterId,
                ConnectionContext = _connectionContext,
                Player = Player,
                ActiveCharacter = _activeCharacter,
                CharacterList = _characterList,
                RoomController = _convaiRoomController
            };

        private void ApplyCompositionArtifacts(RoomCompositionArtifacts artifacts, bool resetConnectionContext)
        {
            if (artifacts == null)
                return;

            DisposeCurrentComposition();

            if (resetConnectionContext)
                _connectionContext = ConnectionContext.Empty;

            Player = artifacts.Player;
            if (Player != null)
                Player.OnTextMessageSent += HandlePlayerTextMessage;

            _activeCharacter = artifacts.ActiveCharacter;
            _characterList = artifacts.Characters;
            _playerSession = artifacts.PlayerSession;
            _convaiRoomController = artifacts.RoomController;
            _audioTrackManager = artifacts.AudioTrackManager;
            _bargeInCoordinator = new BargeInCoordinator(
                () => _audioTrackManager,
                () => CurrentResolvedTurnTakingOptions,
                marker => _clientLatencyMetricsCollector?.RecordBargeInMarker(marker));
            _debugMetricsFileWriter = artifacts.DebugMetricsFileWriter;
            _clientLatencyMetricsCollector = artifacts.ClientLatencyMetricsCollector;
            _roomControllerEventBinder?.Attach(_convaiRoomController);

            ConvaiLogger.Debug(
                $"Resolved {CharacterList.Count} Character(s), using primary Character '{_activeCharacter?.CharacterName ?? _activeCharacter?.CharacterId}'.",
                LogCategory.SDK);
            ConvaiLogger.Debug("Room controller created via factory.", LogCategory.SDK);
        }

        private void DisposeCurrentComposition()
        {
            ResetConnectionScopedRuntimeState();
            _roomConnectionRuntimeAdapter?.ClearResolvedCredentials();

            if (Player != null)
                Player.OnTextMessageSent -= HandlePlayerTextMessage;

            UnsubscribeFromRtvMetrics();
            _clientLatencyMetricsCollector?.Dispose();
            _clientLatencyMetricsCollector = null;
            _debugMetricsFileWriter?.Dispose();
            _debugMetricsFileWriter = null;
            _roomControllerEventBinder?.Detach();
            _bargeInCoordinator?.Dispose();
            _audioTrackManager?.Dispose();
            _convaiRoomController?.Dispose();
            _playerSession?.Dispose();

            _audioTrackManager = null;
            _bargeInCoordinator = null;
            _convaiRoomController = null;
            _playerSession = null;
            _activeCharacter = null;
            _characterList = null;
            Player = null;
        }

        private void SetPendingOwnershipReconnect(string requestedCharacterId)
        {
            HasPendingOwnershipReconnect = true;
            _pendingOwnershipRequestedCharacterId = requestedCharacterId ?? string.Empty;
        }

        private void ClearPendingOwnershipReconnect()
        {
            HasPendingOwnershipReconnect = false;
            _pendingOwnershipRequestedCharacterId = null;
        }

        private RoomOwnershipRebindOutcome PublishOwnershipRebindOutcome(
            RoomOwnershipRebindOutcome outcome,
            string requestedCharacterId)
        {
            _lastOwnershipRebindOutcome = ToPublicOwnershipRebindStatus(outcome).ToString();
            _sessionDiagnostics?.PublishOwnershipRebindState(
                ToPublicOwnershipRebindStatus(outcome),
                HasPendingOwnershipReconnect,
                CurrentState,
                _activeCharacter?.CharacterId,
                requestedCharacterId);
            return outcome;
        }

        private static RoomOwnershipRebindStatus ToPublicOwnershipRebindStatus(RoomOwnershipRebindOutcome outcome) =>
            outcome switch
            {
                RoomOwnershipRebindOutcome.DeferredUntilStartup => RoomOwnershipRebindStatus.DeferredUntilStartup,
                RoomOwnershipRebindOutcome.AppliedImmediately => RoomOwnershipRebindStatus.AppliedImmediately,
                RoomOwnershipRebindOutcome.PendingReconnect => RoomOwnershipRebindStatus.PendingReconnect,
                RoomOwnershipRebindOutcome.RejectedTransitionState => RoomOwnershipRebindStatus.RejectedTransitionState,
                RoomOwnershipRebindOutcome.RejectedInvalidOwnership => RoomOwnershipRebindStatus
                    .RejectedInvalidOwnership,
                _ => RoomOwnershipRebindStatus.RejectedTransitionState
            };

        private void EnsureRuntimeSettingsDependencies()
        {
        }

        private (bool hasPublisher, bool hasFrameSource) GetVisionComponentFlags()
        {
            bool hasPublisher = false;
            bool hasFrameSource = false;

            foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!hasPublisher && component is IVisionPublisher) hasPublisher = true;
                if (!hasFrameSource && component is IVisionFrameSource) hasFrameSource = true;
                if (hasPublisher && hasFrameSource) break;
            }

            return (hasPublisher, hasFrameSource);
        }

        private LipSyncTransportOptions ResolveLipSyncTransportOptions() =>
            LipSyncTransportResolver.Resolve(CharacterList, _eventHub);

        private LipSyncTransportOptions ResolveLipSyncTransportOptions(
            IReadOnlyList<IConvaiCharacterAgent> characterList) =>
            LipSyncTransportResolver.Resolve(characterList, _eventHub);

        private string ResolveVideoTrackName()
        {
            if (UsesRoomConfigAsset && !string.IsNullOrWhiteSpace(_roomConfigAsset.VideoTrackName))
                return _roomConfigAsset.VideoTrackName;

            if (!string.IsNullOrWhiteSpace(VideoTrackName))
                return VideoTrackName;

            foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is IVisionPublisher publisher &&
                    !string.IsNullOrWhiteSpace(publisher.VideoTrackName))
                    return publisher.VideoTrackName;
            }

            return VideoPublishOptions.Default.TrackName;
        }
    }
}
