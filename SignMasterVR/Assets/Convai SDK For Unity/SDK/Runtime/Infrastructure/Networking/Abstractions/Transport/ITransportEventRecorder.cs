using System;
using System.Collections.Generic;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Records transport-level diagnostic events for debugging, replay, and
    ///     post-mortem analysis of connection issues.
    /// </summary>
    /// <remarks>
    ///     Implementations may log to disk, buffer in memory for on-demand export,
    ///     or forward to an external diagnostics service. The recorder is designed
    ///     to be low-overhead so it can remain active in production builds.
    ///     <para />
    ///     All methods are fire-and-forget; implementations must never throw.
    /// </remarks>
    public interface ITransportEventRecorder
    {
        /// <summary>
        ///     Gets whether the recorder is currently active and accepting events.
        /// </summary>
        public bool IsRecording { get; }

        /// <summary>
        ///     Records a transport diagnostic event.
        /// </summary>
        /// <param name="evt">The event to record.</param>
        public void Record(in TransportDiagnosticEvent evt);

        /// <summary>
        ///     Records a transport diagnostic event with a structured payload.
        /// </summary>
        /// <param name="category">Event category (e.g. "connection", "audio", "protocol").</param>
        /// <param name="eventName">Event name within the category.</param>
        /// <param name="detail">Optional human-readable detail string.</param>
        public void Record(string category, string eventName, string detail = null);

        /// <summary>
        ///     Gets a snapshot of all recorded events since the last <see cref="Clear" />.
        /// </summary>
        /// <returns>Read-only list of recorded events, oldest first.</returns>
        public IReadOnlyList<TransportDiagnosticEvent> GetRecordedEvents();

        /// <summary>
        ///     Clears all recorded events.
        /// </summary>
        public void Clear();

        /// <summary>
        ///     Starts or resumes recording.
        /// </summary>
        public void Start();

        /// <summary>
        ///     Pauses recording without clearing the buffer.
        /// </summary>
        public void Pause();
    }

    /// <summary>
    ///     Immutable value representing a single transport diagnostic event.
    /// </summary>
    public readonly struct TransportDiagnosticEvent
    {
        /// <summary>
        ///     Monotonic timestamp in milliseconds (relative to recorder start).
        /// </summary>
        public long TimestampMs { get; }

        /// <summary>
        ///     Event category (e.g. "connection", "audio", "protocol", "session").
        /// </summary>
        public string Category { get; }

        /// <summary>
        ///     Event name within the category (e.g. "room_joined", "mic_muted", "packet_received").
        /// </summary>
        public string EventName { get; }

        /// <summary>
        ///     Optional human-readable detail or serialized payload.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        ///     Optional severity level for filtering.
        /// </summary>
        public TransportEventSeverity Severity { get; }

        /// <summary>
        ///     Creates a new diagnostic event.
        /// </summary>
        public TransportDiagnosticEvent(
            long timestampMs,
            string category,
            string eventName,
            string detail = null,
            TransportEventSeverity severity = TransportEventSeverity.Info)
        {
            TimestampMs = timestampMs;
            Category = category ?? throw new ArgumentNullException(nameof(category));
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            Detail = detail;
            Severity = severity;
        }

        /// <inheritdoc />
        public override string ToString()
            => Detail != null
                ? $"[{TimestampMs}ms] {Category}/{EventName}: {Detail}"
                : $"[{TimestampMs}ms] {Category}/{EventName}";
    }

    /// <summary>
    ///     Severity levels for transport diagnostic events.
    /// </summary>
    public enum TransportEventSeverity
    {
        /// <summary>Verbose trace-level event.</summary>
        Trace = 0,

        /// <summary>Informational event (default).</summary>
        Info = 1,

        /// <summary>Warning that may indicate a problem.</summary>
        Warning = 2,

        /// <summary>Error that impacted functionality.</summary>
        Error = 3
    }
}
