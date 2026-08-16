using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.RestAPI.Internal;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Networking
{
    internal interface IRoomDetailsStateTarget
    {
        public string Token { get; set; }
        public string RoomName { get; set; }
        public string RoomURL { get; set; }
        public string SessionID { get; set; }
        public string CharacterSessionID { get; set; }
        public string ResolvedSpeakerId { get; set; }
        public string RequestTraceId { get; set; }
        public string ResolvedEndUserId { get; set; }
        public IReadOnlyDictionary<string, object> ResolvedEndUserMetadata { get; set; }
        public bool HasRoomDetails { get; set; }
    }

    internal readonly struct AppliedRoomDetailsState
    {
        internal AppliedRoomDetailsState(RoomDetails roomDetails)
        {
            if (roomDetails == null) throw new ArgumentNullException(nameof(roomDetails));

            Token = roomDetails.Token;
            RoomName = roomDetails.RoomName;
            RoomURL = roomDetails.RoomURL;
            SessionID = roomDetails.SessionId;
            CharacterSessionID = roomDetails.CharacterSessionId;
            ResolvedSpeakerId = roomDetails.SpeakerId;
            RequestTraceId = roomDetails.RequestTraceId;
            ResolvedEndUserId = roomDetails.EndUserId;
            ResolvedEndUserMetadata = roomDetails.EndUserMetadata;
            ReceivedLogMessage = $"Room details received: {JsonConvert.SerializeObject(roomDetails)}";
            SummaryLogMessage = CreateSummaryLogMessage(
                Token,
                RoomName,
                RoomURL,
                SessionID,
                CharacterSessionID,
                ResolvedSpeakerId,
                RequestTraceId,
                ResolvedEndUserId);
        }

        public string Token { get; }
        public string RoomName { get; }
        public string RoomURL { get; }
        public string SessionID { get; }
        public string CharacterSessionID { get; }
        public string ResolvedSpeakerId { get; }
        public string RequestTraceId { get; }
        public string ResolvedEndUserId { get; }
        public IReadOnlyDictionary<string, object> ResolvedEndUserMetadata { get; }
        public string ReceivedLogMessage { get; }
        public string SummaryLogMessage { get; }

        public string FormatReceivedLogMessage(string prefix = null) => Prefix(prefix, ReceivedLogMessage);
        public string FormatSummaryLogMessage(string prefix = null) => Prefix(prefix, SummaryLogMessage);

        private static string CreateSummaryLogMessage(
            string token,
            string roomName,
            string roomUrl,
            string sessionId,
            string characterSessionId,
            string resolvedSpeakerId,
            string requestTraceId,
            string resolvedEndUserId)
        {
            string message =
                $"Token: {token}; Room Name: {roomName}; Room URL: {roomUrl}; Session ID: {sessionId}; Character Session ID: {characterSessionId}";
            if (!string.IsNullOrWhiteSpace(resolvedSpeakerId))
                message += $"; Resolved Speaker ID (diagnostics): {resolvedSpeakerId}";
            if (!string.IsNullOrWhiteSpace(requestTraceId))
                message += $"; Request Trace ID: {requestTraceId}";
            if (!string.IsNullOrWhiteSpace(resolvedEndUserId))
                message += $"; Resolved End User ID: {resolvedEndUserId}";

            return message;
        }

        private static string Prefix(string prefix, string message) => string.IsNullOrWhiteSpace(prefix)
            ? message
            : $"{prefix} {message}";
    }

    internal static class RoomDetailsStateApplier
    {
        internal static AppliedRoomDetailsState CreateState(RoomDetails roomDetails) => new(roomDetails);

        internal static AppliedRoomDetailsState Apply(
            IRoomDetailsStateTarget target,
            RoomDetails roomDetails,
            ILogger logger,
            bool markHasRoomDetails = false,
            string logPrefix = null)
        {
            AppliedRoomDetailsState state = CreateState(roomDetails);
            return Apply(target, state, logger, markHasRoomDetails, logPrefix);
        }

        internal static AppliedRoomDetailsState Apply(
            IRoomDetailsStateTarget target,
            in AppliedRoomDetailsState state,
            ILogger logger,
            bool markHasRoomDetails = false,
            string logPrefix = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            logger.Debug(state.FormatReceivedLogMessage(logPrefix), LogCategory.Transport);

            target.Token = state.Token;
            target.RoomName = state.RoomName;
            target.RoomURL = state.RoomURL;
            target.SessionID = state.SessionID;
            target.CharacterSessionID = state.CharacterSessionID;
            target.ResolvedSpeakerId = state.ResolvedSpeakerId;
            target.RequestTraceId = state.RequestTraceId;
            target.ResolvedEndUserId = state.ResolvedEndUserId;
            target.ResolvedEndUserMetadata = state.ResolvedEndUserMetadata;
            target.HasRoomDetails = markHasRoomDetails;

            logger.Debug(state.FormatSummaryLogMessage(logPrefix), LogCategory.Transport);
            return state;
        }
    }
}
