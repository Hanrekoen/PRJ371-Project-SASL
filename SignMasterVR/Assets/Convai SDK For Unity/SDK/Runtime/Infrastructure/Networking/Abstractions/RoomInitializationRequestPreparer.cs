using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking.Models;
using Convai.RestAPI;
using Convai.RestAPI.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Compatibility policy for how room initialization should treat stored character-session fallback.
    /// </summary>
    internal enum StoredSessionFallbackPolicy
    {
        /// <summary>
        ///     Preserve the native controller's current null-coalescing behavior.
        /// </summary>
        NativeCompatibility,

        /// <summary>
        ///     Preserve the WebGL controller's current resume-gated stored-session fallback behavior.
        /// </summary>
        WebGLCompatibility
    }

    /// <summary>
    ///     Shared room-init request preparation result used before platform-specific room-details fetch/connect flows diverge.
    /// </summary>
    internal readonly struct PreparedRoomInitializationRequest
    {
        internal PreparedRoomInitializationRequest(RoomConnectionRequest request, bool isJoinRequest,
            string modeLogMessage)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            IsJoinRequest = isJoinRequest;
            ModeLogMessage = modeLogMessage ?? throw new ArgumentNullException(nameof(modeLogMessage));
        }

        public RoomConnectionRequest Request { get; }
        public bool IsJoinRequest { get; }
        public string ModeLogMessage { get; }

        public string FormatModeLogMessage(string prefix = null) => string.IsNullOrWhiteSpace(prefix)
            ? ModeLogMessage
            : $"{prefix} {ModeLogMessage}";
    }

    /// <summary>
    ///     Prepares the shared request/init metadata used by both Native and WebGL room controllers.
    /// </summary>
    internal static class RoomInitializationRequestPreparer
    {
        internal static PreparedRoomInitializationRequest Prepare(
            string characterId,
            string connectionType,
            string coreServerUrl,
            string storedSessionId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            string endUserId,
            IReadOnlyDictionary<string, object> endUserMetadata,
            string videoTrackName,
            RoomEmotionConfig emotionConfig,
            in LipSyncTransportOptions lipSyncTransportOptions,
            StoredSessionFallbackPolicy storedSessionFallbackPolicy,
            string dynamicInfoText = "",
            bool keepDynamicInfoInContext = false,
            bool debug = false,
            string playerName = null,
            UserVadSettings userVadSettings = null,
            RoomVisionInputConfig visionInputConfig = null,
            RoomRespondModesConfig respondModes = null) =>
            Prepare(
                characterId,
                connectionType,
                coreServerUrl,
                storedSessionId,
                enableSessionResume,
                joinOptions,
                endUserId,
                endUserMetadata,
                videoTrackName,
                emotionConfig,
                ResolvedTurnTakingOptions.DefaultHandsFree,
                lipSyncTransportOptions,
                storedSessionFallbackPolicy,
                dynamicInfoText,
                keepDynamicInfoInContext,
                debug,
                playerName,
                userVadSettings,
                visionInputConfig,
                respondModes);

        internal static PreparedRoomInitializationRequest Prepare(
            string characterId,
            string connectionType,
            string coreServerUrl,
            string storedSessionId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            string endUserId,
            IReadOnlyDictionary<string, object> endUserMetadata,
            string videoTrackName,
            RoomEmotionConfig emotionConfig,
            ResolvedTurnTakingOptions turnTakingOptions,
            in LipSyncTransportOptions lipSyncTransportOptions,
            StoredSessionFallbackPolicy storedSessionFallbackPolicy,
            string dynamicInfoText = "",
            bool keepDynamicInfoInContext = false,
            bool debug = false,
            string playerName = null,
            UserVadSettings userVadSettings = null,
            RoomVisionInputConfig visionInputConfig = null,
            RoomRespondModesConfig respondModes = null)
        {
            string resolvedSessionId = ResolveSessionId(
                storedSessionId,
                enableSessionResume,
                joinOptions,
                storedSessionFallbackPolicy);

            string normalizedEndUserId = NormalizeEndUserId(endUserId);
            IReadOnlyDictionary<string, object> normalizedEndUserMetadata =
                NormalizeEndUserMetadata(endUserMetadata, playerName);
            RoomConnectionRequest roomRequest = RoomConnectionRequestFactory.Create(
                characterId,
                connectionType,
                coreServerUrl,
                resolvedSessionId,
                normalizedEndUserId,
                videoTrackName,
                emotionConfig,
                joinOptions,
                turnTakingOptions,
                lipSyncTransportOptions,
                dynamicInfoText,
                keepDynamicInfoInContext,
                debug,
                normalizedEndUserMetadata,
                userVadSettings,
                visionInputConfig,
                respondModes);

            ApplyMultiplayerFields(roomRequest, joinOptions);

            bool isJoinRequest = joinOptions != null && joinOptions.IsJoinRequest;
            return new PreparedRoomInitializationRequest(roomRequest, isJoinRequest, CreateModeLogMessage(joinOptions));
        }

        private static void ApplyMultiplayerFields(RoomConnectionRequest roomRequest, RoomJoinOptions joinOptions)
        {
            if (roomRequest == null || joinOptions == null)
                return;

            int? maxNumParticipants = joinOptions.ResolvedMaxNumParticipants ?? joinOptions.MaxNumParticipants;
            if (maxNumParticipants is int maxParticipants)
                roomRequest.MaxNumParticipants = maxParticipants;

            string sharedSessionKey = joinOptions.ResolvedSharedSessionKey;
            if (!string.IsNullOrWhiteSpace(sharedSessionKey))
                roomRequest.SharedSessionKey = sharedSessionKey.Trim();
        }

        private static string ResolveSessionId(
            string storedSessionId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            StoredSessionFallbackPolicy storedSessionFallbackPolicy)
        {
            switch (storedSessionFallbackPolicy)
            {
                case StoredSessionFallbackPolicy.NativeCompatibility:
                    return joinOptions?.CharacterSessionId ?? storedSessionId;

                case StoredSessionFallbackPolicy.WebGLCompatibility:
                    return !string.IsNullOrEmpty(joinOptions?.CharacterSessionId)
                        ? joinOptions.CharacterSessionId
                        : enableSessionResume
                            ? storedSessionId
                            : null;

                default:
                    throw new ArgumentOutOfRangeException(nameof(storedSessionFallbackPolicy),
                        storedSessionFallbackPolicy,
                        "Unknown stored-session fallback policy.");
            }
        }

        private static string NormalizeEndUserId(string endUserId) => string.IsNullOrWhiteSpace(endUserId)
            ? null
            : endUserId.Trim();

        private static IReadOnlyDictionary<string, object> NormalizeEndUserMetadata(
            IReadOnlyDictionary<string, object> endUserMetadata,
            string playerName)
        {
            var normalized = new Dictionary<string, object>();
            if (endUserMetadata != null)
            {
                foreach (KeyValuePair<string, object> entry in endUserMetadata)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                        continue;

                    normalized[entry.Key.Trim()] = entry.Value;
                }
            }

            string normalizedPlayerName = string.IsNullOrWhiteSpace(playerName) ? null : playerName.Trim();
            if (!string.IsNullOrEmpty(normalizedPlayerName))
                normalized["name"] = normalizedPlayerName;

            return normalized.Count == 0 ? null : normalized;
        }

        private static string CreateModeLogMessage(RoomJoinOptions joinOptions) => joinOptions != null &&
            joinOptions.IsJoinRequest
                ? $"Room join mode: room={joinOptions.RoomName}, spawnAgent={joinOptions.SpawnAgent}"
                : "Room create mode (new room)";
    }
}
