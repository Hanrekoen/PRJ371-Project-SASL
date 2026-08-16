using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Protocol;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Convai.Shared.Types;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Owns LipSync protocol detection, parsing, routing, timeline messages, and ingress diagnostics.
    /// </summary>
    internal sealed class LipSyncProtocolIngress
    {
        private const double DropLogIntervalSeconds = 5d;

        private static readonly ProfilerMarker DetectMarker = new("Convai.LipSync.Inbound.Detect");
        private static readonly ProfilerMarker ParseMarker = new("Convai.LipSync.Inbound.Parse");
        private static readonly ProfilerMarker PublishMarker = new("Convai.LipSync.Inbound.Publish");

        private readonly IAgentRegistry _agentRegistry;
        private readonly BlendshapeFrameStatsTracker _blendshapeFrameStats = new(8);
        private readonly RepeatedMessageThrottle _dropThrottle = new(DropLogIntervalSeconds);
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly float _parseFrameRate;
        private readonly LipSyncTransportOptions _transportOptions;

        private string _lastBlendshapeParticipantId;
        private int _receivedBlendshapeFrameCount;

        public LipSyncProtocolIngress(
            IAgentRegistry agentRegistry,
            IEventHub eventHub,
            ILogger logger,
            LipSyncTransportOptions transportOptions)
        {
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _eventHub = eventHub;
            _logger = logger.WithTag(nameof(LipSyncProtocolIngress));
            _transportOptions = transportOptions;
            _parseFrameRate = transportOptions.OutputFps > 0 ? transportOptions.OutputFps : 60f;
        }

        public bool TryHandlePacket(in ProtocolPacket packet)
        {
            using (DetectMarker.Auto())
            {
                if (!LipSyncMessageDetector.MayContainLipSyncServerMessage(packet.Payload.Span))
                    return false;
            }

            LipSyncParseResult parseResult;
            using (ParseMarker.Auto())
            {
                parseResult = LipSyncServerMessageParser.Parse(
                    packet.Payload,
                    _parseFrameRate,
                    _transportOptions);
            }

            if (!parseResult.Handled) return false;

            if (!parseResult.Parsed)
            {
                string payloadTypeForLog = string.IsNullOrWhiteSpace(parseResult.PayloadType)
                    ? "lipsync-server-message"
                    : parseResult.PayloadType;

                if (!string.IsNullOrWhiteSpace(parseResult.DropReasonCode))
                    LogDropRateLimited(parseResult.DropReasonCode, payloadTypeForLog);
                else
                    _logger.Info(
                        $"LipSync candidate handled but not parsed: payloadType='{payloadTypeForLog}', participant='{packet.ParticipantId}'.",
                        LogCategory.LipSync);

                return true;
            }

            using (PublishMarker.Auto())
            {
                PublishIncomingChunk(packet.ParticipantId, parseResult.Chunk);
            }

            return true;
        }

        public bool TryHandleServerMessage(string type, JObject payload, string participantId)
        {
            switch (type)
            {
                case "blendshape-turn-stats":
                    HandleBlendshapeTurnStats(payload, participantId);
                    return true;
                case "neurosync-blendshapes-cancel":
                    HandleTimelineReset(payload, participantId);
                    return true;
                case "neurosync-audio-timeline-anchor":
                    HandleAudioTimelineAnchor(payload, participantId);
                    return true;
                default:
                    return false;
            }
        }

        private void PublishIncomingChunk(string participantId, LipSyncPackedChunk chunk)
        {
            if (chunk == null || !chunk.IsValid)
            {
                if (IsInfoEnabled())
                {
                    _logger.Info(
                        $"LipSync publish skipped: invalid chunk, participant='{participantId}', chunkNull={chunk == null}.",
                        LogCategory.LipSync);
                }

                return;
            }

            _receivedBlendshapeFrameCount += chunk.FrameCount;
            var owner = new LipSyncResponseOwner(
                chunk.ResponseId,
                chunk.NeuroSyncTurnId,
                chunk.Epoch,
                chunk.Sequence);
            _blendshapeFrameStats.Add(owner, chunk.FrameCount);

            _lastBlendshapeParticipantId = participantId;
            PublishPackedDataReceived(participantId, chunk);
        }

        private void PublishPackedDataReceived(string participantId, LipSyncPackedChunk chunk)
        {
            if (chunk == null || !chunk.IsValid || _eventHub == null)
            {
                if (IsInfoEnabled())
                {
                    _logger.Info(
                        $"LipSync event publish skipped: eventHubNull={_eventHub == null}, participant='{participantId}', chunkNull={chunk == null}, valid={chunk?.IsValid.ToString() ?? "false"}.",
                        LogCategory.LipSync);
                }

                return;
            }

            if (!TryResolveCharacterIdFromParticipant(participantId, out string characterId))
            {
                LogDropRateLimited("lipsync.route.character_unresolved", "lipsync-packed-data");
                return;
            }

            _eventHub.Publish(LipSyncPackedDataReceived.Create(
                characterId,
                participantId ?? string.Empty,
                chunk));

            if (IsDebugEnabled())
            {
                _logger.Debug(
                    $"LipSync chunk published: characterId='{characterId}', participant='{participantId}', receivedTotal={_receivedBlendshapeFrameCount}, {ChunkSummary(chunk)}.",
                    LogCategory.LipSync);
            }
        }

        private void HandleBlendshapeTurnStats(JObject payload, string participantId)
        {
            var statsPayload = payload?.ToObject<BlendshapeTurnStatsPayload>();
            if (statsPayload?.Stats == null) return;

            BlendshapeTurnStats stats = statsPayload.Stats;
            string resolvedParticipantId = !string.IsNullOrWhiteSpace(participantId)
                ? participantId
                : _lastBlendshapeParticipantId ?? string.Empty;
            string characterId = ResolveCharacterId(resolvedParticipantId);

            int receivedFrames = _receivedBlendshapeFrameCount;
            var owner = new LipSyncResponseOwner(
                statsPayload.ResponseId,
                statsPayload.NeuroSyncTurnId,
                statsPayload.Epoch,
                statsPayload.Sequence);
            bool hasOwnerScopedCount = _blendshapeFrameStats.TryTake(owner, out int ownerScopedCount);
            if (hasOwnerScopedCount)
                receivedFrames = ownerScopedCount;

            bool frameCountMatch = receivedFrames == stats.TotalBlendshapes;
            _logger.Info(
                $"[LipSync] TurnStats - Server: {stats.TotalBlendshapes} frames | " +
                $"Received: {receivedFrames} frames ({(hasOwnerScopedCount ? "owner-scoped" : "global")}) | Match: {(frameCountMatch ? "YES" : "NO")} | " +
                $"Owner: response='{statsPayload.ResponseId}', turn={statsPayload.NeuroSyncTurnId?.ToString() ?? "null"}, epoch={statsPayload.Epoch?.ToString() ?? "null"} | " +
                $"Audio: {stats.TotalAudioBytes} bytes | Turn Duration: {stats.TotalTurnDurationMs / 1000.0:F2}s | " +
                $"Audio Duration: {stats.TotalAudioDurationMs / 1000.0:F2}s | FPS: {stats.Fps:F2}",
                LogCategory.LipSync);

            _eventHub?.Publish(BlendshapeTurnStatsReceived.Create(
                characterId,
                resolvedParticipantId,
                stats.TotalBlendshapes,
                receivedFrames,
                stats.TotalAudioBytes,
                stats.TotalTurnDurationMs,
                stats.TotalAudioDurationMs,
                stats.Fps,
                statsPayload.ResponseId,
                statsPayload.NeuroSyncTurnId,
                statsPayload.Epoch,
                statsPayload.Sequence));

            _receivedBlendshapeFrameCount = 0;
            _lastBlendshapeParticipantId = null;
        }

        private void HandleAudioTimelineAnchor(JObject payload, string participantId)
        {
            if (payload == null) return;

            string resolvedParticipantId = participantId ?? string.Empty;
            if (!TryResolveCharacterIdFromParticipant(resolvedParticipantId, out string characterId))
            {
                LogDropRateLimited("lipsync.route.character_unresolved", "neurosync-audio-timeline-anchor");
                return;
            }

            int? sampleRate = payload.Value<int?>("sample_rate");
            long? audioStartSample = payload.Value<long?>("audio_start_sample") ??
                                     payload.Value<long?>("audio_start_sample_index");
            long? finalAudioSample = payload.Value<long?>("final_audio_sample") ??
                                     payload.Value<long?>("audio_end_sample");
            double audioStartMs = payload.Value<double?>("audio_start_ms") ??
                                  (audioStartSample.HasValue && sampleRate > 0
                                      ? audioStartSample.Value * 1000d / sampleRate.Value
                                      : -1d);
            double audioDurationMs = payload.Value<double?>("audio_duration_ms") ??
                                     (audioStartSample.HasValue && finalAudioSample.HasValue && sampleRate > 0
                                         ? Math.Max(0d,
                                             (finalAudioSample.Value - audioStartSample.Value) * 1000d /
                                             sampleRate.Value)
                                         : 0d);

            var evt = LipSyncAudioTimelineAnchorReceived.Create(
                characterId,
                resolvedParticipantId,
                payload.Value<string>("response_id"),
                payload.Value<int?>("neurosync_turn_id"),
                payload.Value<int?>("epoch"),
                payload.Value<int?>("sequence"),
                audioStartMs,
                audioDurationMs,
                sampleRate,
                payload.Value<int?>("channels"));

            if (!evt.IsValid)
            {
                LogDropRateLimited("lipsync.anchor.invalid", "neurosync-audio-timeline-anchor");
                return;
            }

            _eventHub?.Publish(evt);
            if (audioStartSample.HasValue && sampleRate > 0)
            {
                _eventHub?.Publish(new AudioTimelineSampleAnchor(
                    characterId,
                    evt.ResponseId,
                    evt.NeuroSyncTurnId,
                    evt.Epoch,
                    evt.Sequence,
                    sampleRate.Value,
                    audioStartSample.Value,
                    finalAudioSample));
            }
        }

        private void HandleTimelineReset(JObject payload, string participantId)
        {
            if (payload == null) return;

            string resolvedParticipantId = participantId ?? string.Empty;
            if (!TryResolveCharacterIdFromParticipant(resolvedParticipantId, out string characterId))
            {
                LogDropRateLimited("lipsync.route.character_unresolved", "neurosync-blendshapes-cancel");
                return;
            }

            _eventHub?.Publish(LipSyncTimelineResetRequested.Create(
                characterId,
                resolvedParticipantId,
                payload.Value<string>("response_id"),
                payload.Value<int?>("neurosync_turn_id"),
                payload.Value<int?>("epoch"),
                payload.Value<int?>("sequence"),
                payload.Value<int?>("valid_through_frame_index"),
                payload.Value<string>("reason")));
        }

        private void LogDropRateLimited(string dropReasonCode, string payloadType)
        {
            if (string.IsNullOrWhiteSpace(dropReasonCode)) return;

            string key = $"{payloadType}:{dropReasonCode}";
            if (!_dropThrottle.ShouldSay(key, DateTime.UtcNow, out int suppressedCount))
                return;

            string suppressedSuffix = suppressedCount > 0
                ? $" (suppressed {suppressedCount} similar drops in last {DropLogIntervalSeconds:F0}s)"
                : string.Empty;
            _logger.Warning($"[{dropReasonCode}] Dropped '{payloadType}' payload.{suppressedSuffix}",
                LogCategory.LipSync);
        }

        private bool IsDebugEnabled() => _logger.IsEnabled(LogLevel.Debug, LogCategory.LipSync);

        private bool IsInfoEnabled() => _logger.IsEnabled(LogLevel.Info, LogCategory.LipSync);

        private string ResolveCharacterId(string participantId) =>
            TryResolveCharacterAgent(participantId, out IConvaiCharacterAgent agent)
                ? agent.CharacterId
                : participantId ?? string.Empty;

        private bool TryResolveCharacterIdFromParticipant(string participantId, out string characterId)
        {
            characterId = string.Empty;
            if (string.IsNullOrWhiteSpace(participantId)) return false;

            if (!TryResolveCharacterAgent(participantId, out IConvaiCharacterAgent agent))
                return false;

            characterId = agent.CharacterId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(characterId);
        }

        private bool TryResolveCharacterAgent(string participantId, out IConvaiCharacterAgent agent)
        {
            agent = null;
            if (string.IsNullOrWhiteSpace(participantId)) return false;

            if (_agentRegistry.TryGetCharacterByParticipantId(participantId, out agent)) return agent != null;
            return _agentRegistry.TryGetCharacter(participantId, out agent) && agent != null;
        }

        private static string ChunkSummary(LipSyncPackedChunk chunk)
        {
            if (chunk == null) return "chunk=null";

            return
                $"frames={chunk.FrameCount}, profile='{chunk.ProfileId}', fps={chunk.FrameRate:F1}, response='{FormatValue(chunk.ResponseId)}', turn={FormatValue(chunk.NeuroSyncTurnId)}, epoch={FormatValue(chunk.Epoch)}, start={FormatValue(chunk.StartFrameIndex)}, seq={FormatValue(chunk.Sequence)}, owner={chunk.HasOwnerMetadata}, timeline={chunk.HasTimelineMetadata}";
        }

        private static string FormatValue(string value) => string.IsNullOrWhiteSpace(value) ? "null" : value;

        private static string FormatValue(int? value) => value.HasValue ? value.Value.ToString() : "null";
    }
}
