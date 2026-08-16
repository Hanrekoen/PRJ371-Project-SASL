using System;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents.Vision
{
    /// <summary>
    ///     Backend acknowledgement of a <c>respond-mode-update</c> request, echoing the lane and the
    ///     mode that was applied (or the rejection when a lane cannot be changed).
    /// </summary>
    public readonly struct RespondModeUpdateResultReceived
    {
        private RespondModeUpdateResultReceived(
            string status,
            string message,
            string updateId,
            string modality,
            string mode,
            JObject rawExtras,
            DateTime timestamp)
        {
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            UpdateId = updateId ?? string.Empty;
            Modality = modality ?? string.Empty;
            Mode = mode ?? string.Empty;
            RawExtras = rawExtras;
            Timestamp = timestamp;
        }

        /// <summary>Response status: <c>success</c> when applied, <c>error</c> when rejected.</summary>
        public string Status { get; }

        /// <summary>Optional human-readable message (e.g. the rejection reason for a user-input lane).</summary>
        public string Message { get; }

        /// <summary>Echo of the request's idempotency key, when the backend returns one.</summary>
        public string UpdateId { get; }

        /// <summary>The lane the update targeted, as a backend modality string (e.g. <c>vision</c>, <c>context_update</c>).</summary>
        public string Modality { get; }

        /// <summary>The respond mode now in effect for the lane (<c>silent</c>/<c>auto</c>/<c>must_respond</c>).</summary>
        public string Mode { get; }

        /// <summary>Full extras payload, including the backend's complete <c>respond_modes</c> lane snapshot.</summary>
        public JObject RawExtras { get; }

        /// <summary>UTC time this event was created on the client.</summary>
        public DateTime Timestamp { get; }

        public static RespondModeUpdateResultReceived Create(string status, string message, JObject extras)
        {
            extras ??= new JObject();
            return new RespondModeUpdateResultReceived(
                status,
                message,
                ExtrasReader.ReadString(extras, "update_id"),
                ExtrasReader.ReadString(extras, "modality"),
                ExtrasReader.ReadString(extras, "mode"),
                extras,
                DateTime.UtcNow);
        }
    }
}
