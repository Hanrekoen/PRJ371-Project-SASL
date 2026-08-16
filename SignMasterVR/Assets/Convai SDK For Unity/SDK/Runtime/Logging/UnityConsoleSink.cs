using System.Collections.Generic;
using System.Text;
using Convai.Domain.Logging;
using UnityEngine;

namespace Convai.Runtime.Logging
{
    /// <summary>
    ///     ILogSink implementation that writes to Unity's Debug console.
    ///     This is the default sink used by ConvaiLogger.
    /// </summary>
    /// <remarks>
    ///     Uses shared LogFormatting utilities for StringBuilder pooling and enum name caching.
    /// </remarks>
    internal sealed class UnityConsoleSink : ILogSink
    {
        /// <inheritdoc />
        public string Name => "UnityConsole";

        /// <inheritdoc />
        public bool IsEnabled { get; private set; } = true;

        /// <inheritdoc />
        public void Write(LogEntry entry)
        {
            if (!IsEnabled || entry.Level == LogLevel.Off) return;

            string formattedMessage = FormatLogEntry(entry);

            switch (entry.Level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(formattedMessage);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                case LogLevel.Error:
                    if (entry.Exception != null)
                    {
                        Debug.LogError(formattedMessage);
                        Debug.LogException(entry.Exception);
                        break;
                    }

                    Debug.LogError(formattedMessage);
                    break;
            }
        }

        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void SetEnabled(bool enabled) => IsEnabled = enabled;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>
        ///     Formats a log entry for console output using pooled StringBuilder.
        /// </summary>
        private string FormatLogEntry(LogEntry entry)
        {
            StringBuilder sb = LogFormatting.GetBuilder();

            string levelName = LogFormatting.GetLevelName(entry.Level);
            string categoryName = LogFormatting.GetCategoryName(entry.Category);
            string color = LoggingConfig.ColoredOutput ? LogFormatting.GetLevelColor(entry.Level) : null;

            if (!string.IsNullOrEmpty(color)) sb.Append("<color=").Append(color).Append('>');

            sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append(']');
            sb.Append('[').Append(levelName).Append(']');
            sb.Append('[').Append(categoryName).Append(']');

            if (entry.HasCorrelationId) sb.Append('[').Append(entry.CorrelationId).Append(']');

            sb.Append(": ");
            sb.Append(entry.Message);

            if (entry.HasContext)
            {
                sb.Append(" {");
                bool first = true;
                foreach (KeyValuePair<string, object> kvp in entry.Context)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kvp.Key).Append('=').Append(kvp.Value);
                }

                sb.Append('}');
            }

            if (entry.Exception != null)
            {
                sb.Append('\n').Append(entry.Exception.GetType().Name).Append(": ").Append(entry.Exception.Message);

                if (LoggingConfig.IncludeStackTraces && !string.IsNullOrEmpty(entry.Exception.StackTrace))
                    sb.Append('\n').Append(entry.Exception.StackTrace);
            }

            if (!string.IsNullOrEmpty(color)) sb.Append("</color>");

            return sb.ToString();
        }
    }
}
