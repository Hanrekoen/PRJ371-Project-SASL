using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking.Models;
using Convai.RestAPI;
using Convai.RestAPI.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Shared.Actions;
using Convai.Runtime.Room;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Builds the canonical room-connect request shared by native and WebGL transports.
    /// </summary>
    internal static class RoomConnectionRequestFactory
    {
        private const string DynamicLlmProvider = "dynamic";

        internal static RoomConnectionRequest Create(
            string characterId,
            string connectionType,
            string coreServerUrl,
            string characterSessionId,
            string endUserId,
            string videoTrackName,
            RoomEmotionConfig emotionConfig,
            RoomJoinOptions joinOptions,
            in LipSyncTransportOptions lipSyncTransportOptions,
            string dynamicInfoText = "",
            bool keepDynamicInfoInContext = false,
            bool debug = false,
            IReadOnlyDictionary<string, object> endUserMetadata = null,
            UserVadSettings userVadSettings = null,
            RoomVisionInputConfig visionInputConfig = null,
            RoomRespondModesConfig respondModes = null) =>
            Create(
                characterId,
                connectionType,
                coreServerUrl,
                characterSessionId,
                endUserId,
                videoTrackName,
                emotionConfig,
                joinOptions,
                ResolvedTurnTakingOptions.DefaultHandsFree,
                lipSyncTransportOptions,
                dynamicInfoText,
                keepDynamicInfoInContext,
                debug,
                endUserMetadata,
                userVadSettings,
                visionInputConfig,
                respondModes);

        internal static RoomConnectionRequest Create(
            string characterId,
            string connectionType,
            string coreServerUrl,
            string characterSessionId,
            string endUserId,
            string videoTrackName,
            RoomEmotionConfig emotionConfig,
            RoomJoinOptions joinOptions,
            ResolvedTurnTakingOptions turnTakingOptions,
            in LipSyncTransportOptions lipSyncTransportOptions,
            string dynamicInfoText = "",
            bool keepDynamicInfoInContext = false,
            bool debug = false,
            IReadOnlyDictionary<string, object> endUserMetadata = null,
            UserVadSettings userVadSettings = null,
            RoomVisionInputConfig visionInputConfig = null,
            RoomRespondModesConfig respondModes = null)
        {
            visionInputConfig ??= joinOptions?.ResolvedVisionInputConfig;
            respondModes ??= joinOptions?.ResolvedRespondModes;

            var roomRequest = new RoomConnectionRequest
            {
                CharacterId = characterId,
                Transport = "livekit",
                ConnectionType = connectionType,
                LlmProvider = DynamicLlmProvider,
                CoreServiceUrl = coreServerUrl,
                CharacterSessionId = string.IsNullOrWhiteSpace(characterSessionId) ? null : characterSessionId,
                EndUserId = string.IsNullOrWhiteSpace(endUserId) ? null : endUserId.Trim(),
                EndUserMetadata = endUserMetadata == null || endUserMetadata.Count == 0
                    ? null
                    : new Dictionary<string, object>(endUserMetadata),
                TurnDetectionConfig = CreateTransportTurnDetectionConfig(turnTakingOptions),
                DefaultSttEnabled = turnTakingOptions?.InitialServerSttEnabled,
                VadParams = CreateTransportVadParams(userVadSettings),
                EmotionConfig = emotionConfig,
                Debug = debug ? true : null,
                DynamicInfo = new RoomDynamicInfo
                {
                    Text = keepDynamicInfoInContext ? dynamicInfoText ?? string.Empty : string.Empty,
                    KeepInContext = keepDynamicInfoInContext
                },
                ActionConfig = joinOptions?.ResolvedActionConfig?.Clone(),
                VisionInputConfig = visionInputConfig,
                RespondModes = respondModes
            };

            ApplyVideoTrackName(roomRequest, connectionType, videoTrackName);
            ApplyJoinOptions(roomRequest, joinOptions);
            RoomConnectionRequestLipSyncMapper.Apply(roomRequest, lipSyncTransportOptions);
            return roomRequest;
        }

        private static TurnDetectionConfig CreateTransportTurnDetectionConfig(
            ResolvedTurnTakingOptions turnTakingOptions)
        {
            if (turnTakingOptions == null || !turnTakingOptions.HasTurnDetectionConfig)
                return null;

            SmartTurnSettings smartTurnSettings = turnTakingOptions.SmartTurnSettings;
            return new TurnDetectionConfig
            {
                Type = "smart_turn",
                Params = new TurnDetectionParams
                {
                    StopSecs = smartTurnSettings.StopSecs,
                    PreSpeechMs = smartTurnSettings.PreSpeechMs,
                    MaxDurationSecs = smartTurnSettings.MaxDurationSecs
                }
            };
        }

        private static VadParams CreateTransportVadParams(UserVadSettings userVadSettings) =>
            (userVadSettings ?? UserVadSettings.CreateDefault()).ToTransportVadParams();

        private static void ApplyVideoTrackName(
            RoomConnectionRequest roomRequest,
            string connectionType,
            string videoTrackName)
        {
            if (!string.Equals(connectionType, "video", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(videoTrackName))
                return;

            roomRequest.VideoTrackName = videoTrackName.Trim();
        }

        private static void ApplyJoinOptions(RoomConnectionRequest roomRequest, RoomJoinOptions joinOptions)
        {
            ApplySharedSessionKey(roomRequest, joinOptions);

            if (joinOptions != null && joinOptions.IsJoinRequest)
            {
                roomRequest.RoomName = joinOptions.RoomName;
                roomRequest.Mode = "join";
                roomRequest.SpawnAgent = joinOptions.SpawnAgent;
                ApplyMaxNumParticipants(roomRequest, joinOptions);
                return;
            }

            roomRequest.Mode = "create";
            roomRequest.SpawnAgent = true;
            ApplyMaxNumParticipants(roomRequest, joinOptions);
        }

        private static void ApplySharedSessionKey(RoomConnectionRequest roomRequest, RoomJoinOptions joinOptions)
        {
            string sharedSessionKey = joinOptions?.ResolvedSharedSessionKey;
            if (string.IsNullOrWhiteSpace(sharedSessionKey))
                return;

            roomRequest.SharedSessionKey = sharedSessionKey.Trim();
        }

        private static void ApplyMaxNumParticipants(RoomConnectionRequest roomRequest, RoomJoinOptions joinOptions)
        {
            int? maxNumParticipants = joinOptions?.ResolvedMaxNumParticipants ?? joinOptions?.MaxNumParticipants;
            if (maxNumParticipants is int value)
                roomRequest.MaxNumParticipants = value;
        }
    }
}
