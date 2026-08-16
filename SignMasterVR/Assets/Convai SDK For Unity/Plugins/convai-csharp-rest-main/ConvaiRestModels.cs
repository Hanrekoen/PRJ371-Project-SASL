using System;
using System.Collections.Generic;
using Convai.RestAPI.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI
{

#nullable enable

    [Serializable]
    internal class CharacterUpdateRequest
    {
        public CharacterUpdateRequest(string characterID, bool isEnabled)
        {
            CharacterID = characterID;
            MemorySettings = new MemorySettings(isEnabled);
        }

        [JsonProperty("charID")] public string CharacterID { get; set; }
        [JsonProperty("memorySettings")] public MemorySettings MemorySettings { get; set; }
    }

    /// <summary>
    /// Parameters for smart turn detection configuration.
    /// All parameters are optional - the server will use defaults if not specified.
    /// </summary>
    [Serializable]
    public class TurnDetectionParams
    {
        /// <summary>
        /// Duration of silence (in seconds) before detecting end of turn.
        /// Default: 3 seconds.
        /// </summary>
        [JsonProperty("stop_secs", NullValueHandling = NullValueHandling.Ignore)]
        public float? StopSecs { get; set; }

        /// <summary>
        /// Pre-speech buffer duration in milliseconds.
        /// Default: 0 ms.
        /// </summary>
        [JsonProperty("pre_speech_ms", NullValueHandling = NullValueHandling.Ignore)]
        public int? PreSpeechMs { get; set; }

        /// <summary>
        /// Maximum duration of a turn in seconds.
        /// Default: 8 seconds.
        /// </summary>
        [JsonProperty("max_duration_secs", NullValueHandling = NullValueHandling.Ignore)]
        public float? MaxDurationSecs { get; set; }
    }

    /// <summary>
    /// Configuration for turn detection in discrete models.
    /// Enable smart turn detection by setting Type to "smart_turn".
    /// </summary>
    [Serializable]
    public class TurnDetectionConfig
    {
        /// <summary>
        /// The type of turn detection. Use "smart_turn" to enable smart turn detection.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "smart_turn";

        /// <summary>
        /// Optional parameters for turn detection behavior.
        /// </summary>
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public TurnDetectionParams? Params { get; set; }

        /// <summary>
        /// Creates a default smart turn detection config with default parameters.
        /// </summary>
        public static TurnDetectionConfig CreateDefault()
        {
            return new TurnDetectionConfig
            {
                Type = "smart_turn",
                Params = new TurnDetectionParams
                {
                    StopSecs = 3f,
                    PreSpeechMs = 0,
                    MaxDurationSecs = 8f
                }
            };
        }
    }

    /// <summary>
    /// Voice activity detection parameters sent with room connection requests.
    /// Defaults match core-service <c>ConnectRequest.vad_params</c> (models/api.py VAD_*).
    /// </summary>
    [Serializable]
    public class VadParams
    {
        public const float DefaultConfidence = 0.7f;
        public const float DefaultStartSecs = 0.2f;
        public const float DefaultStopSecs = 0.2f;
        public const float DefaultMinVolume = 0.6f;

        /// <summary>
        /// Confidence threshold used by backend VAD.
        /// </summary>
        [JsonProperty("confidence")]
        public float Confidence { get; set; } = DefaultConfidence;

        /// <summary>
        /// Speech duration in seconds before VAD treats user speech as started.
        /// </summary>
        [JsonProperty("start_secs")]
        public float StartSecs { get; set; } = DefaultStartSecs;

        /// <summary>
        /// Silence duration in seconds before VAD treats user speech as stopped.
        /// </summary>
        [JsonProperty("stop_secs")]
        public float StopSecs { get; set; } = DefaultStopSecs;

        /// <summary>
        /// Minimum volume threshold used by backend VAD.
        /// </summary>
        [JsonProperty("min_volume")]
        public float MinVolume { get; set; } = DefaultMinVolume;
    }

    /// <summary>
    /// Configuration for blendshape facial animation (face sync / lip sync).
    /// Sent as part of the room connection request when lip sync transport is enabled.
    /// </summary>
    [Serializable]
    public class ConvaiBlendshapeConfig
    {
        public ConvaiBlendshapeConfig()
        {
        }

        /// <summary>
        /// Initializes the room-connect blendshape transport payload used by the SDK.
        /// </summary>
        public ConvaiBlendshapeConfig(
            bool enableChunking,
            int chunkSize,
            int outputFps,
            string format,
            float framesBufferDuration = 0f,
            bool deliverChunksAhead = false)
        {
            EnableChunking = enableChunking;
            ChunkSize = chunkSize;
            OutputFps = outputFps;
            Format = format ?? string.Empty;
            FramesBufferDuration = framesBufferDuration;
            DeliverChunksAhead = deliverChunksAhead;
        }

        /// <summary>Whether the server should send blendshapes in chunked packets.</summary>
        [JsonProperty("enable_chunking")]
        public bool EnableChunking { get; set; }

        /// <summary>Number of frames per chunk when <see cref="EnableChunking"/> is true.</summary>
        [JsonProperty("chunk_size")]
        public int ChunkSize { get; set; }

        /// <summary>Target frames-per-second for blendshape output.</summary>
        [JsonProperty("output_fps")]
        public int OutputFps { get; set; }

        /// <summary>
        /// Profile/format key that identifies the blendshape channel layout
        /// (for example "arkit", "cc4_extended", or "metahuman").
        /// </summary>
        [JsonProperty("format")]
        public string Format { get; set; } = string.Empty;

        /// <summary>Optional client-side buffering hint for the negotiated lip-sync transport.</summary>
        [JsonProperty("frames_buffer_duration")]
        public float FramesBufferDuration { get; set; }

        /// <summary>Opt-in capability for indexed chunks sent ahead of audio playback.</summary>
        [JsonProperty("deliver_chunks_ahead", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool DeliverChunksAhead { get; set; }
    }

    /// <summary>
    /// Emotion configuration sent to the WebRTC connect endpoint.
    /// Provider controls how bot emotions are inferred and streamed.
    /// </summary>
    [Serializable]
    public sealed class RoomEmotionConfig
    {
        private const string DefaultProvider = "nrclex";

        /// <summary>
        /// Gets or sets the emotion provider ("nrclex" or "llm").
        /// Invalid values are normalized to "nrclex".
        /// </summary>
        [JsonProperty("provider")]
        public string Provider { get; set; } = DefaultProvider;

        /// <summary>
        /// Returns a normalized provider string if supported; otherwise <c>null</c>.
        /// </summary>
        public static string? NormalizeProvider(string? rawProvider)
        {
            if (string.IsNullOrWhiteSpace(rawProvider))
            {
                return null;
            }

            string normalized = rawProvider.Trim().ToLowerInvariant();
            return normalized is "nrclex" or "llm" ? normalized : null;
        }

        /// <summary>
        /// Creates a config using the provided provider or falls back to default.
        /// </summary>
        public static RoomEmotionConfig Create(string? provider = null)
        {
            return new RoomEmotionConfig
            {
                Provider = NormalizeProvider(provider) ?? DefaultProvider
            };
        }
    }

    [Serializable]
    public class NarrativeDecisionData
    {
        public NarrativeDecisionData(string criteria, string nextSectionId, int? priority = null)
        {
            Criteria = criteria;
            NextSectionId = nextSectionId;
            Priority = priority;
        }

        [JsonProperty("criteria")]
        public string Criteria { get; set; }

        [JsonProperty("next_section_id")]
        public string NextSectionId { get; set; }

        [JsonProperty("priority", NullValueHandling = NullValueHandling.Ignore)]
        public int? Priority { get; set; }
    }

    [Serializable]
    public class NarrativeSectionUpdateData
    {
        public NarrativeSectionUpdateData(string? sectionName = null, string? objective = null, List<NarrativeDecisionData>? decisions = null)
        {
            SectionName = sectionName;
            Objective = objective;
            Decisions = decisions;
        }

        [JsonProperty("section_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? SectionName { get; set; }

        [JsonProperty("objective", NullValueHandling = NullValueHandling.Ignore)]
        public string? Objective { get; set; }

        [JsonProperty("decisions", NullValueHandling = NullValueHandling.Ignore)]
        public List<NarrativeDecisionData>? Decisions { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken?> AdditionalData { get; set; } = new Dictionary<string, JToken?>();
    }

    [Serializable]
    public class NarrativeTriggerUpdateData
    {
        public NarrativeTriggerUpdateData(string? triggerName = null, string? triggerMessage = null, string? destinationSection = null)
        {
            TriggerName = triggerName;
            TriggerMessage = triggerMessage;
            DestinationSection = destinationSection;
        }

        [JsonProperty("trigger_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? TriggerName { get; set; }

        [JsonProperty("trigger_message", NullValueHandling = NullValueHandling.Ignore)]
        public string? TriggerMessage { get; set; }

        [JsonProperty("destination_section", NullValueHandling = NullValueHandling.Ignore)]
        public string? DestinationSection { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken?> AdditionalData { get; set; } = new Dictionary<string, JToken?>();
    }

    [Serializable]
    public class NarrativeDecisionUpdatePayload
    {
        public NarrativeDecisionUpdatePayload(string? criteria = null, string? nextSectionId = null, int? priority = null)
        {
            Criteria = criteria;
            NextSectionId = nextSectionId;
            Priority = priority;
        }

        [JsonProperty("criteria", NullValueHandling = NullValueHandling.Ignore)]
        public string? Criteria { get; set; }

        [JsonProperty("next_section_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? NextSectionId { get; set; }

        [JsonProperty("priority", NullValueHandling = NullValueHandling.Ignore)]
        public int? Priority { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken?> AdditionalData { get; set; } = new Dictionary<string, JToken?>();
    }
}
