using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Backend acknowledgement of a <c>vision-trigger</c> request, reporting how the trigger was
    ///     resolved (respond mode, downgrades) and what frames were attached to the model turn.
    /// </summary>
    public readonly struct VisionContextTriggerReceived
    {
        private VisionContextTriggerReceived(
            string status,
            string message,
            string updateId,
            string outcome,
            string requestedRespondMode,
            string actualRespondMode,
            string requestedRunLlm,
            string actualRunLlm,
            bool llmTriggered,
            bool downgraded,
            string downgradeReason,
            int framesAttached,
            string attachOutcome,
            int imageTokensEstimate,
            IReadOnlyList<long> attachedFramePts,
            JObject rawExtras,
            DateTime timestamp)
        {
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            UpdateId = updateId ?? string.Empty;
            Outcome = outcome ?? string.Empty;
            RequestedRespondMode = requestedRespondMode ?? string.Empty;
            ActualRespondMode = actualRespondMode ?? string.Empty;
            RequestedRunLlm = requestedRunLlm ?? string.Empty;
            ActualRunLlm = actualRunLlm ?? string.Empty;
            LlmTriggered = llmTriggered;
            Downgraded = downgraded;
            DowngradeReason = downgradeReason ?? string.Empty;
            FramesAttached = framesAttached;
            AttachOutcome = attachOutcome ?? string.Empty;
            ImageTokensEstimate = imageTokensEstimate;
            AttachedFramePts = attachedFramePts ?? Array.Empty<long>();
            RawExtras = rawExtras;
            Timestamp = timestamp;
        }

        /// <summary>Response status: <c>success</c>, <c>error</c>, <c>processing</c> or <c>pending</c>.</summary>
        public string Status { get; }

        /// <summary>Optional human-readable message accompanying the response.</summary>
        public string Message { get; }

        /// <summary>Echo of the request's idempotency key.</summary>
        public string UpdateId { get; }

        /// <summary>
        ///     Trigger outcome, e.g. <c>frames_available</c>, <c>buffer_empty</c>, <c>vision_not_enabled</c>,
        ///     <c>invalid_respond_mode</c>, <c>invalid_frame_indices</c>, <c>frame_id_evicted</c> or
        ///     <c>rate_limited</c>.
        /// </summary>
        public string Outcome { get; }

        /// <summary>Respond mode the request asked for (<c>silent</c>/<c>auto</c>/<c>must_respond</c>).</summary>
        public string RequestedRespondMode { get; }

        /// <summary>Respond mode the backend actually applied after state-based downgrades.</summary>
        public string ActualRespondMode { get; }

        /// <summary>Requested LLM policy on the wire (<c>true</c>/<c>auto</c>/<c>false</c>).</summary>
        public string RequestedRunLlm { get; }

        /// <summary>LLM policy actually applied after downgrades.</summary>
        public string ActualRunLlm { get; }

        /// <summary>True when the trigger caused an LLM invocation.</summary>
        public bool LlmTriggered { get; }

        /// <summary>True when the backend lowered the requested respond mode (e.g. bot busy, user speaking).</summary>
        public bool Downgraded { get; }

        /// <summary>Why the request was downgraded, e.g. <c>bot_busy</c> or <c>user_speaking</c>; empty otherwise.</summary>
        public string DowngradeReason { get; }

        /// <summary>Number of image frames attached to the turn.</summary>
        public int FramesAttached { get; }

        /// <summary>Attach outcome: <c>attached</c>, <c>deduped_stub</c>, <c>stale_skipped</c> or <c>none</c>.</summary>
        public string AttachOutcome { get; }

        /// <summary>Backend estimate of the image tokens the attached frames cost (attribution only, not billing).</summary>
        public int ImageTokensEstimate { get; }

        /// <summary>Presentation timestamps (nanoseconds) of the exact frames the model saw.</summary>
        public IReadOnlyList<long> AttachedFramePts { get; }

        /// <summary>Full extras payload (including <c>vision_buffer</c> diagnostics) for fields without typed accessors.</summary>
        public JObject RawExtras { get; }

        /// <summary>UTC time this event was created on the client.</summary>
        public DateTime Timestamp { get; }

        public static VisionContextTriggerReceived Create(string status, string message, JObject extras)
        {
            extras ??= new JObject();
            return new VisionContextTriggerReceived(
                status,
                message,
                ExtrasReader.ReadString(extras, "update_id"),
                ExtrasReader.ReadString(extras, "vision_trigger_outcome"),
                ExtrasReader.ReadString(extras, "requested_respond_mode"),
                ExtrasReader.ReadString(extras, "actual_respond_mode"),
                ExtrasReader.ReadString(extras, "requested_run_llm"),
                ExtrasReader.ReadString(extras, "actual_run_llm"),
                ExtrasReader.ReadBool(extras, "llm_triggered"),
                ExtrasReader.ReadBool(extras, "downgraded"),
                ExtrasReader.ReadString(extras, "downgrade_reason"),
                ExtrasReader.ReadInt(extras, "vision_frames_attached"),
                ExtrasReader.ReadString(extras, "vision_attach_outcome"),
                ExtrasReader.ReadInt(extras, "vision_image_tokens_est"),
                ExtrasReader.ReadLongArray(extras, "attached_frame_pts"),
                extras,
                DateTime.UtcNow);
        }
    }
}
