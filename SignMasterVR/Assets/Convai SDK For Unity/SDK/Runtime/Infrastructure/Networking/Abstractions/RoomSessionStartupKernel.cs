using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking.Models;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    internal readonly struct RoomSessionStartupPlan
    {
        internal RoomSessionStartupPlan(
            PreparedRoomInitializationRequest requestPreparation,
            InvalidStoredSessionRecoveryPolicy recoveryPolicy,
            bool enableSessionResume)
        {
            RequestPreparation = requestPreparation;
            RecoveryPolicy = recoveryPolicy;
            EnableSessionResume = enableSessionResume;
        }

        public PreparedRoomInitializationRequest RequestPreparation { get; }
        public RoomConnectionRequest Request => RequestPreparation.Request;
        public InvalidStoredSessionRecoveryPolicy RecoveryPolicy { get; }
        public bool EnableSessionResume { get; }

        public string FormatModeLogMessage(string prefix = null) => RequestPreparation.FormatModeLogMessage(prefix);
    }

    internal enum RoomSessionStartupDecisionKind
    {
        Accepted,
        Failed
    }

    internal readonly struct RoomSessionStartupDecision
    {
        private readonly AppliedRoomDetailsState? _appliedRoomDetailsState;
        private readonly RoomInitializationFailureOutcome? _failureOutcome;

        internal RoomSessionStartupDecision(
            in AppliedRoomDetailsState appliedRoomDetailsState,
            in RoomInitializationOutcome initializationOutcome)
        {
            Kind = RoomSessionStartupDecisionKind.Accepted;
            InitializationOutcome = initializationOutcome;
            _appliedRoomDetailsState = appliedRoomDetailsState;
            _failureOutcome = null;
            DiagnosticsMetadata = CreateAcceptedDiagnosticsMetadata(appliedRoomDetailsState, initializationOutcome);
        }

        internal RoomSessionStartupDecision(in RoomInitializationFailureOutcome failureOutcome)
        {
            Kind = RoomSessionStartupDecisionKind.Failed;
            InitializationOutcome = failureOutcome.RecoveryOutcome;
            _appliedRoomDetailsState = null;
            _failureOutcome = failureOutcome;
            DiagnosticsMetadata =
                $"kind=Failed; shouldApplyRoomDetails=False; shouldProceedToTransportConnect=False; failure={{{failureOutcome.DiagnosticsMetadata}}}";
        }

        public RoomSessionStartupDecisionKind Kind { get; }
        public RoomInitializationOutcome InitializationOutcome { get; }
        public bool ShouldApplyRoomDetails => Kind == RoomSessionStartupDecisionKind.Accepted;
        public bool ShouldProceedToTransportConnect => Kind == RoomSessionStartupDecisionKind.Accepted;
        public string DiagnosticsMetadata { get; }

        public AppliedRoomDetailsState AppliedRoomDetailsState => _appliedRoomDetailsState ??
                                                                  throw new InvalidOperationException(
                                                                      "Accepted room-details state is only available for accepted startup decisions.");

        public RoomInitializationFailureOutcome FailureOutcome => _failureOutcome ??
                                                                  throw new InvalidOperationException(
                                                                      "Failure outcome is only available for failed startup decisions.");

        public string FormatDiagnosticsLogMessage(string prefix = null) => Prefix(
            prefix,
            $"Room startup decision: {DiagnosticsMetadata}");

        private static string CreateAcceptedDiagnosticsMetadata(
            in AppliedRoomDetailsState appliedRoomDetailsState,
            in RoomInitializationOutcome initializationOutcome) =>
            $"kind=Accepted; shouldApplyRoomDetails=True; shouldProceedToTransportConnect=True; roomName={Normalize(appliedRoomDetailsState.RoomName)}; roomUrl={Normalize(appliedRoomDetailsState.RoomURL)}; sessionId={Normalize(appliedRoomDetailsState.SessionID)}; characterSessionId={Normalize(appliedRoomDetailsState.CharacterSessionID)}; recovery={{{initializationOutcome.DiagnosticsMetadata}}}";

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

        private static string Prefix(string prefix, string message) => string.IsNullOrWhiteSpace(prefix)
            ? message
            : $"{prefix} {message}";
    }

    internal static class RoomSessionStartupKernel
    {
        internal static RoomSessionStartupPlan Prepare(
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
            InvalidStoredSessionRecoveryPolicy recoveryPolicy,
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
                recoveryPolicy,
                dynamicInfoText,
                keepDynamicInfoInContext,
                debug,
                playerName,
                userVadSettings,
                visionInputConfig,
                respondModes);

        internal static RoomSessionStartupPlan Prepare(
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
            InvalidStoredSessionRecoveryPolicy recoveryPolicy,
            string dynamicInfoText = "",
            bool keepDynamicInfoInContext = false,
            bool debug = false,
            string playerName = null,
            UserVadSettings userVadSettings = null,
            RoomVisionInputConfig visionInputConfig = null,
            RoomRespondModesConfig respondModes = null)
        {
            PreparedRoomInitializationRequest requestPreparation = RoomInitializationRequestPreparer.Prepare(
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
                turnTakingOptions,
                lipSyncTransportOptions,
                storedSessionFallbackPolicy,
                dynamicInfoText,
                keepDynamicInfoInContext,
                debug,
                playerName,
                userVadSettings,
                visionInputConfig,
                respondModes);
            return new RoomSessionStartupPlan(requestPreparation, recoveryPolicy, enableSessionResume);
        }

        internal static RoomSessionStartupDecision AcceptRoomDetails(
            in RoomSessionStartupPlan startupPlan,
            RoomDetails roomDetails)
        {
            AppliedRoomDetailsState appliedRoomDetailsState = RoomDetailsStateApplier.CreateState(roomDetails);
            RoomInitializationOutcome initializationOutcome = RoomInitializationRecoverySupport.FromAccepted(
                startupPlan.Request.CharacterSessionId,
                startupPlan.EnableSessionResume,
                appliedRoomDetailsState.CharacterSessionID,
                startupPlan.RecoveryPolicy);
            return new RoomSessionStartupDecision(appliedRoomDetailsState, initializationOutcome);
        }

        internal static RoomSessionStartupDecision FromRequestFailure(
            in RoomSessionStartupPlan startupPlan,
            string failureMessage,
            long? responseCode = null,
            string sessionErrorMessage = null)
        {
            RoomInitializationFailureOutcome failureOutcome = RoomInitializationFailureSupport.FromRequestFailure(
                startupPlan.Request.CharacterSessionId,
                startupPlan.EnableSessionResume,
                failureMessage,
                startupPlan.RecoveryPolicy,
                responseCode,
                sessionErrorMessage);
            return new RoomSessionStartupDecision(failureOutcome);
        }

        internal static RoomSessionStartupDecision FromRequestException(
            in RoomSessionStartupPlan startupPlan,
            Exception exception)
        {
            RoomInitializationFailureOutcome failureOutcome = RoomInitializationFailureSupport.FromRequestException(
                startupPlan.Request.CharacterSessionId,
                startupPlan.EnableSessionResume,
                exception,
                startupPlan.RecoveryPolicy);
            return new RoomSessionStartupDecision(failureOutcome);
        }

        internal static RoomSessionStartupDecision FromInvalidRoomDetails(
            in RoomSessionStartupPlan startupPlan,
            string failureMessage)
        {
            RoomInitializationFailureOutcome failureOutcome = RoomInitializationFailureSupport.FromInvalidRoomDetails(
                startupPlan.Request.CharacterSessionId,
                startupPlan.EnableSessionResume,
                failureMessage,
                startupPlan.RecoveryPolicy);
            return new RoomSessionStartupDecision(failureOutcome);
        }
    }
}
