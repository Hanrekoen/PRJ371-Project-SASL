#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Payload for blendshape turn statistics.
    ///     Received from server-message with type "blendshape-turn-stats".
    /// </summary>
    public class BlendshapeTurnStatsPayload
    {
        /// <summary>
        ///     Statistics data for the blendshape turn.
        /// </summary>
        [JsonProperty("stats")]
        public BlendshapeTurnStats? Stats { get; set; }

        /// <summary>
        ///     Response owner id shared with chunked-neurosync-blendshapes for the same turn.
        /// </summary>
        [JsonProperty("response_id")]
        public string? ResponseId { get; set; }

        /// <summary>
        ///     NeuroSync turn id shared with the chunk stream for the same turn.
        /// </summary>
        [JsonProperty("neurosync_turn_id")]
        public int? NeuroSyncTurnId { get; set; }

        /// <summary>
        ///     Connection epoch shared with the chunk stream for the same turn.
        /// </summary>
        [JsonProperty("epoch")]
        public int? Epoch { get; set; }

        /// <summary>
        ///     Per-turn monotonic message sequence.
        /// </summary>
        [JsonProperty("sequence")]
        public int? Sequence { get; set; }
    }

    /// <summary>
    ///     Statistics for a blendshape animation turn.
    /// </summary>
    public class BlendshapeTurnStats
    {
        /// <summary>
        ///     Total number of blendshape frames sent by the server.
        /// </summary>
        [JsonProperty("total_blendshapes")]
        public int TotalBlendshapes { get; set; }

        /// <summary>
        ///     Total audio bytes associated with this turn.
        /// </summary>
        [JsonProperty("total_audio_bytes")]
        public int TotalAudioBytes { get; set; }

        /// <summary>
        ///     Total duration of the turn in milliseconds.
        /// </summary>
        [JsonProperty("total_turn_duration_ms")]
        public double TotalTurnDurationMs { get; set; }

        /// <summary>
        ///     Total duration of the generated audio in milliseconds.
        /// </summary>
        [JsonProperty("total_audio_duration_ms")]
        public double TotalAudioDurationMs { get; set; }

        /// <summary>
        ///     Frame rate of the blendshape data.
        /// </summary>
        [JsonProperty("fps")]
        public double Fps { get; set; }
    }
}
