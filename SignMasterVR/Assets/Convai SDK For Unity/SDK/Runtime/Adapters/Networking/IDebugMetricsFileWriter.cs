#nullable enable
using System;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Writes debug metrics (server RTVI + client latency) to a text file when debug mode is on.
    ///     Each line is linear: UTC timestamp (ISO8601) | event_type | payload for easy parsing and analytics.
    /// </summary>
    internal interface IDebugMetricsFileWriter : IDisposable
    {
        /// <summary>
        ///     Writes one line: UTC (ISO8601) | eventType | payload. Thread-safe; flushes after each write.
        /// </summary>
        /// <param name="eventType">Event identifier (e.g. rtvi_metrics, client_player_stopped, client_latency_summary).</param>
        /// <param name="payload">Data string (e.g. JSON or key=value). Should not contain newlines or pipe characters.</param>
        public void WriteLine(string eventType, string payload);
    }
}
