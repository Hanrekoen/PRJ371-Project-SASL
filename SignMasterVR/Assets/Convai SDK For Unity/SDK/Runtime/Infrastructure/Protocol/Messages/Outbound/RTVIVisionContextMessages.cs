using System;
using Convai.Runtime;
using Convai.Runtime.Vision.Context;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Outbound RTVI <c>vision-status</c> query. The backend answers with a
    ///     <c>server-response</c> carrying the current vision buffer diagnostics.
    /// </summary>
    public sealed class RTVIVisionStatus : RTVISendMessageBase
    {
        public RTVIVisionStatus(string updateId = null)
        {
            string stableUpdateId = string.IsNullOrWhiteSpace(updateId) ? Guid.NewGuid().ToString("N") : updateId.Trim();
            Type = "vision-status";
            Id = stableUpdateId;
            Data = new VisionStatusData { UpdateId = stableUpdateId };
        }

        private sealed class VisionStatusData
        {
            [JsonProperty("update_id")]
            public string UpdateId { get; set; }
        }
    }

    /// <summary>
    ///     Outbound RTVI <c>respond-mode-update</c> message: changes one input lane's respond mode
    ///     for the rest of the session. The backend answers with a <c>server-response</c> echoing
    ///     the applied mode and the full lane snapshot.
    /// </summary>
    public sealed class RTVIRespondModeUpdate : RTVISendMessageBase
    {
        public RTVIRespondModeUpdate(ConvaiRespondModeLane lane, ConvaiRespondMode mode, string updateId = null)
        {
            string stableUpdateId = string.IsNullOrWhiteSpace(updateId) ? Guid.NewGuid().ToString("N") : updateId.Trim();
            Type = "respond-mode-update";
            Id = stableUpdateId;
            Data = new RespondModeUpdateData
            {
                Modality = lane.ToWireString(),
                Mode = mode.ToWireString(),
                UpdateId = stableUpdateId
            };
        }

        private sealed class RespondModeUpdateData
        {
            [JsonProperty("modality")]
            public string Modality { get; set; }

            [JsonProperty("mode")]
            public string Mode { get; set; }

            [JsonProperty("update_id")]
            public string UpdateId { get; set; }
        }
    }

    /// <summary>
    ///     Outbound RTVI <c>vision-trigger</c> message built from a <see cref="ConvaiVisionTriggerRequest" />.
    /// </summary>
    public sealed class RTVIVisionTrigger : RTVISendMessageBase
    {
        public RTVIVisionTrigger(ConvaiVisionTriggerRequest request)
        {
            request ??= new ConvaiVisionTriggerRequest();
            Type = "vision-trigger";
            Id = request.UpdateId;
            Data = VisionTriggerData.FromRequest(request);
        }

        private sealed class VisionTriggerData
        {
            [JsonProperty("update_id")]
            public string UpdateId { get; set; }

            [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
            public string Text { get; set; }

            [JsonProperty("respond_mode", NullValueHandling = NullValueHandling.Ignore)]
            public string RespondMode { get; set; }

            // The backend contract requires exactly a two-element [start, end] window here;
            // anything else is rejected with an invalid_frame_indices error ack.
            [JsonProperty("frame_indices", NullValueHandling = NullValueHandling.Ignore)]
            public int[] FrameIndices { get; set; }

            [JsonProperty("frame_ids", NullValueHandling = NullValueHandling.Ignore)]
            public long[] FrameIds { get; set; }

            public static VisionTriggerData FromRequest(ConvaiVisionTriggerRequest request)
            {
                long[] frameIds = null;
                if (request.FramePtsIds is { Count: > 0 })
                {
                    frameIds = new long[request.FramePtsIds.Count];
                    for (int i = 0; i < frameIds.Length; i++)
                        frameIds[i] = request.FramePtsIds[i];
                }

                int[] frameWindow = null;
                if (request.FrameWindowStart.HasValue && request.FrameWindowEnd.HasValue)
                    frameWindow = new[] { request.FrameWindowStart.Value, request.FrameWindowEnd.Value };

                return new VisionTriggerData
                {
                    UpdateId = request.UpdateId,
                    Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text,
                    RespondMode = request.RespondMode?.ToWireString(),
                    FrameIndices = frameWindow,
                    FrameIds = frameIds
                };
            }
        }
    }
}
