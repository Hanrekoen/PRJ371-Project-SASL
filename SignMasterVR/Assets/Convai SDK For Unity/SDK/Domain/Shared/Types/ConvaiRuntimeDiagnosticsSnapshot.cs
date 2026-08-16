using System;
using System.Collections.Generic;

namespace Convai.Shared.Types
{
    /// <summary>
    ///     Read-only runtime diagnostics snapshot.
    /// </summary>
    public readonly struct ConvaiRuntimeDiagnosticsSnapshot
    {
        public ConvaiRuntimeDiagnosticsSnapshot(
            ConvaiRuntimeDiagnosticsConfigSnapshot config,
            ConvaiRuntimeDiagnosticsGraphSnapshot graph,
            ConvaiRuntimeDiagnosticsHealthSnapshot health)
        {
            Config = config;
            Graph = graph;
            Health = health;
        }

        public ConvaiRuntimeDiagnosticsConfigSnapshot Config { get; }
        public ConvaiRuntimeDiagnosticsGraphSnapshot Graph { get; }
        public ConvaiRuntimeDiagnosticsHealthSnapshot Health { get; }
    }

    public readonly struct ConvaiRuntimeDiagnosticsConfigSnapshot
    {
        public ConvaiRuntimeDiagnosticsConfigSnapshot(
            string projectSettingsSource,
            string runtimeCredentialProviderType,
            bool runtimeCredentialsConfigured,
            string endUserIdentityProviderType,
            string sessionPersistenceType,
            string roomConfigName,
            IReadOnlyList<string> agentDefinitionNames,
            string effectiveConnectionType,
            string effectiveVideoTrackName,
            string effectiveServerEndpoint,
            bool effectiveConnectOnStart,
            string effectiveReconnectPolicy,
            ConvaiRuntimeSettingsSnapshot runtimeSettings)
        {
            ProjectSettingsSource = projectSettingsSource ?? string.Empty;
            RuntimeCredentialProviderType = runtimeCredentialProviderType ?? string.Empty;
            RuntimeCredentialsConfigured = runtimeCredentialsConfigured;
            EndUserIdentityProviderType = endUserIdentityProviderType ?? string.Empty;
            SessionPersistenceType = sessionPersistenceType ?? string.Empty;
            RoomConfigName = roomConfigName ?? string.Empty;
            AgentDefinitionNames = agentDefinitionNames ?? Array.Empty<string>();
            EffectiveConnectionType = effectiveConnectionType ?? string.Empty;
            EffectiveVideoTrackName = effectiveVideoTrackName ?? string.Empty;
            EffectiveServerEndpoint = effectiveServerEndpoint ?? string.Empty;
            EffectiveConnectOnStart = effectiveConnectOnStart;
            EffectiveReconnectPolicy = effectiveReconnectPolicy ?? string.Empty;
            RuntimeSettings = runtimeSettings;
        }

        public string ProjectSettingsSource { get; }
        public string RuntimeCredentialProviderType { get; }
        public bool RuntimeCredentialsConfigured { get; }
        public string EndUserIdentityProviderType { get; }
        public string SessionPersistenceType { get; }
        public string RoomConfigName { get; }
        public IReadOnlyList<string> AgentDefinitionNames { get; }
        public string EffectiveConnectionType { get; }
        public string EffectiveVideoTrackName { get; }
        public string EffectiveServerEndpoint { get; }
        public bool EffectiveConnectOnStart { get; }
        public string EffectiveReconnectPolicy { get; }
        public ConvaiRuntimeSettingsSnapshot RuntimeSettings { get; }
    }

    public readonly struct ConvaiRuntimeDiagnosticsGraphSnapshot
    {
        public ConvaiRuntimeDiagnosticsGraphSnapshot(
            bool hasRuntimeContext,
            bool hasManager,
            bool hasRoomManager,
            string ownedPlayerName,
            IReadOnlyList<string> ownedCharacterIds,
            string activeConversationCharacterId,
            IReadOnlyList<string> moduleParticipants,
            string roomControllerType,
            string transportAccessorType)
        {
            HasRuntimeContext = hasRuntimeContext;
            HasManager = hasManager;
            HasRoomManager = hasRoomManager;
            OwnedPlayerName = ownedPlayerName ?? string.Empty;
            OwnedCharacterIds = ownedCharacterIds ?? Array.Empty<string>();
            ActiveConversationCharacterId = activeConversationCharacterId ?? string.Empty;
            ModuleParticipants = moduleParticipants ?? Array.Empty<string>();
            RoomControllerType = roomControllerType ?? string.Empty;
            TransportAccessorType = transportAccessorType ?? string.Empty;
        }

        public bool HasRuntimeContext { get; }
        public bool HasManager { get; }
        public bool HasRoomManager { get; }
        public string OwnedPlayerName { get; }
        public IReadOnlyList<string> OwnedCharacterIds { get; }
        public string ActiveConversationCharacterId { get; }
        public IReadOnlyList<string> ModuleParticipants { get; }
        public string RoomControllerType { get; }
        public string TransportAccessorType { get; }
    }

    public readonly struct ConvaiRuntimeDiagnosticsHealthSnapshot
    {
        public ConvaiRuntimeDiagnosticsHealthSnapshot(
            string sessionState,
            bool isConnected,
            bool hasRoomDetails,
            string roomName,
            string sessionId,
            string characterSessionId,
            bool hasPendingOwnershipReconnect,
            int connectAttemptCount,
            int reconnectCount,
            string lastSessionErrorCode,
            string lastSessionErrorMessage,
            string lastOwnershipRebindOutcome,
            string lastSuccessfulConnectionUtc)
        {
            SessionState = sessionState ?? string.Empty;
            IsConnected = isConnected;
            HasRoomDetails = hasRoomDetails;
            RoomName = roomName ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            CharacterSessionId = characterSessionId ?? string.Empty;
            HasPendingOwnershipReconnect = hasPendingOwnershipReconnect;
            ConnectAttemptCount = connectAttemptCount;
            ReconnectCount = reconnectCount;
            LastSessionErrorCode = lastSessionErrorCode ?? string.Empty;
            LastSessionErrorMessage = lastSessionErrorMessage ?? string.Empty;
            LastOwnershipRebindOutcome = lastOwnershipRebindOutcome ?? string.Empty;
            LastSuccessfulConnectionUtc = lastSuccessfulConnectionUtc ?? string.Empty;
        }

        public string SessionState { get; }
        public bool IsConnected { get; }
        public bool HasRoomDetails { get; }
        public string RoomName { get; }
        public string SessionId { get; }
        public string CharacterSessionId { get; }
        public bool HasPendingOwnershipReconnect { get; }
        public int ConnectAttemptCount { get; }
        public int ReconnectCount { get; }
        public string LastSessionErrorCode { get; }
        public string LastSessionErrorMessage { get; }
        public string LastOwnershipRebindOutcome { get; }
        public string LastSuccessfulConnectionUtc { get; }
    }
}
