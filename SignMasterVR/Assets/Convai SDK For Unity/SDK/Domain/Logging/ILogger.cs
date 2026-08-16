using System;
using System.Collections.Generic;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Hierarchical log levels for the Convai SDK.
    ///     Lower numeric values = more severe. Standard convention: logs at configured level and below are shown.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>No logging.</summary>
        Off = 0,

        /// <summary>Critical errors that require immediate attention.</summary>
        Error = 1,

        /// <summary>Warnings about potential issues.</summary>
        Warning = 2,

        /// <summary>General informational messages.</summary>
        Info = 3,

        /// <summary>Detailed debugging information.</summary>
        Debug = 4,

        /// <summary>Verbose trace-level logging.</summary>
        Trace = 5
    }

    /// <summary>
    ///     Core logging interface for the Convai SDK.
    /// </summary>
    /// <remarks>
    ///     Supports both simple logging and structured logging with context data.
    ///     Use structured logging overloads when you need to attach key-value pairs
    ///     for better searchability in log aggregation systems.
    /// </remarks>
    public interface ILogger
    {
        /// <summary>
        ///     Logs a message at the specified level with a typed category.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="category">The log category.</param>
        public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs a message at the specified level with structured context data.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="context">Key-value pairs to attach to the log entry.</param>
        /// <param name="category">The log category.</param>
        public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs a debug message with a typed category.
        /// </summary>
        public void Debug(string message, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs a debug message with structured context data.
        /// </summary>
        public void Debug(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an info message with a typed category.
        /// </summary>
        public void Info(string message, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an info message with structured context data.
        /// </summary>
        public void Info(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs a warning message with a typed category.
        /// </summary>
        public void Warning(string message, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs a warning message with structured context data.
        /// </summary>
        public void Warning(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an error message with a typed category.
        /// </summary>
        public void Error(string message, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an error message with structured context data.
        /// </summary>
        public void Error(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an error with an exception.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Optional additional message.</param>
        /// <param name="category">The log category.</param>
        public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Logs an error with an exception and structured context data.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Optional additional message.</param>
        /// <param name="context">Key-value pairs to attach to the log entry.</param>
        /// <param name="category">The log category.</param>
        public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK);

        /// <summary>
        ///     Checks if logging is enabled for the specified level and category.
        ///     Use this to avoid expensive string formatting when logging is disabled.
        /// </summary>
        /// <param name="level">The log level to check.</param>
        /// <param name="category">The log category to check.</param>
        /// <returns>True if logging is enabled for this level and category.</returns>
        public bool IsEnabled(LogLevel level, LogCategory category);
    }
}
