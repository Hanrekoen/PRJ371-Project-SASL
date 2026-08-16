using System;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Represents a destination for log messages.
    /// </summary>
    public interface ILogSink : IDisposable
    {
        /// <summary>
        ///     Gets the name of this sink for identification and debugging.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets whether this sink is currently enabled.
        ///     Disabled sinks will not receive Write() calls.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        ///     Writes a log entry to this sink.
        /// </summary>
        /// <param name="entry">The log entry to write.</param>
        public void Write(LogEntry entry);

        /// <summary>
        ///     Flushes any buffered log entries to the underlying destination.
        ///     Call this before application shutdown to ensure all logs are written.
        /// </summary>
        public void Flush();

        /// <summary>
        ///     Enables or disables this sink at runtime.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        public void SetEnabled(bool enabled);
    }

    /// <summary>
    ///     Extension methods for ILogSink.
    /// </summary>
    public static class LogSinkExtensions
    {
        /// <summary>
        ///     Writes a log entry if the sink is enabled.
        /// </summary>
        /// <param name="sink">The sink to write to.</param>
        /// <param name="entry">The log entry to write.</param>
        public static void WriteIfEnabled(this ILogSink sink, in LogEntry entry)
        {
            if (sink?.IsEnabled == true) sink.Write(entry);
        }
    }
}
