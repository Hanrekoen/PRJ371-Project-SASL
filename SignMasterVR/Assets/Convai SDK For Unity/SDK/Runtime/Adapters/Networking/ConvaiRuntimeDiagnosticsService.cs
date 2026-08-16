using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Domain.Abstractions;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Components;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.Core.Modules;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class ConvaiRuntimeDiagnosticsService : IConvaiRuntimeDiagnosticsService
    {
        private readonly IConvaiDiagnosticsDependencies _dependencies;
        private ConvaiManager _manager;
        private ConvaiRoomManager _roomManager;

        public ConvaiRuntimeDiagnosticsService(IConvaiDiagnosticsDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public ConvaiRuntimeDiagnosticsSnapshot CaptureSnapshot()
        {
            ConvaiManager manager = _manager;
            ConvaiRoomManager roomManager = _roomManager;

            IReadOnlyList<IConvaiModule> modules =
                manager?.ConvaiRuntime?.Modules ?? Array.Empty<IConvaiModule>();

            ICredentialProvider credentialProvider = _dependencies.CredentialProvider;
            IEndUserIdentityProvider identityProvider = _dependencies.IdentityProvider;
            ISessionPersistence sessionPersistence = _dependencies.SessionPersistence;
            IConvaiRuntimeSettingsService runtimeSettingsService = _dependencies.RuntimeSettingsService;
            IRealtimeTransportAccessor transportAccessor = _dependencies.TransportAccessor;

            bool runtimeCredentialsConfigured = credentialProvider?.HasValidCredentials == true;

            var agentDefinitions = new List<string>();
            if (manager != null)
            {
                IReadOnlyList<ConvaiCharacter> characters = manager.Characters;
                foreach (ConvaiCharacter character in characters)
                {
                    ConvaiCharacterProfile definition = character?.CharacterConfigAsset;
                    if (definition == null) continue;

                    string label = string.IsNullOrWhiteSpace(definition.name)
                        ? definition.CharacterId
                        : definition.name;
                    if (!string.IsNullOrWhiteSpace(label) && !agentDefinitions.Contains(label))
                        agentDefinitions.Add(label);
                }
            }

            var ownedCharacterIds = new List<string>();
            if (manager != null)
            {
                IReadOnlyList<ConvaiCharacter> characters = manager.Characters;
                ownedCharacterIds.AddRange(characters.Select(t => t?.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId)));
            }

            List<string> moduleNames = modules.Select(t => t?.DisplayName)
                .Where(displayName => !string.IsNullOrWhiteSpace(displayName)).ToList();

            var config = new ConvaiRuntimeDiagnosticsConfigSnapshot(
                "ConvaiSettings",
                credentialProvider?.GetType().Name,
                runtimeCredentialsConfigured,
                identityProvider?.GetType().Name,
                sessionPersistence?.GetType().Name,
                roomManager?.RoomConfigAsset != null ? roomManager.RoomConfigAsset.name : string.Empty,
                agentDefinitions,
                roomManager?.EffectiveConnectionType.ToString(),
                roomManager?.EffectiveVideoTrackName,
                roomManager?.EffectiveServerEndpoint.ToString(),
                roomManager?.EffectiveConnectOnStart ?? false,
                roomManager?.EffectiveReconnectPolicyDescription,
                runtimeSettingsService?.Current ?? default);

            // HasRuntimeContext: builder-built runtime is present (not service-container presence).
            bool hasRuntime = manager != null && manager.ConvaiRuntime != null;
            var graph = new ConvaiRuntimeDiagnosticsGraphSnapshot(
                hasRuntime,
                manager != null,
                roomManager != null,
                manager?.Player?.PlayerName,
                ownedCharacterIds,
                manager?.ActiveConversationCharacter?.CharacterId,
                moduleNames,
                roomManager?.RoomControllerTypeName,
                transportAccessor?.GetType().Name);

            var health = new ConvaiRuntimeDiagnosticsHealthSnapshot(
                roomManager?.CurrentState.ToString(),
                roomManager?.IsConnected ?? false,
                roomManager?.HasRoomDetails ?? false,
                roomManager?.CurrentRoomName,
                roomManager?.CurrentSessionId,
                roomManager?.CurrentCharacterSessionId,
                roomManager?.HasPendingOwnershipReconnect ?? false,
                roomManager?.ConnectAttemptCount ?? 0,
                roomManager?.ReconnectCount ?? 0,
                roomManager?.LastSessionErrorCode,
                roomManager?.LastSessionErrorMessage,
                roomManager?.LastOwnershipRebindOutcome,
                roomManager?.LastSuccessfulConnectionUtc?.ToString("O"));

            return new ConvaiRuntimeDiagnosticsSnapshot(config, graph, health);
        }

        public void AttachManager(ConvaiManager manager) => _manager = manager;

        public void AttachRoomManager(ConvaiRoomManager roomManager) => _roomManager = roomManager;
    }
}
