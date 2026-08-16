using System;
using System.Collections.Generic;
using System.Threading;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Represents a single log entry with associated metadata.
    /// </summary>
    public readonly struct LogEntry
    {
        #region Timestamp Caching

        private static DateTime _cachedTimestamp = DateTime.Now;
        private static int _lastUpdateFrame = -1;

        private static Func<int> _frameCountProvider;

        private static int _mainThreadId = -1;

        /// <summary>
        ///     Registers a frame count provider for timestamp caching.
        /// </summary>
        /// <param name="provider">A function that returns the current frame count.</param>
        public static void SetFrameCountProvider(Func<int> provider)
        {
            _frameCountProvider = provider;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        ///     Checks if the current code is running on the Unity main thread.
        /// </summary>
        /// <returns>True if on main thread, false otherwise.</returns>
        private static bool IsOnMainThread()
        {
            if (_mainThreadId < 0) return false;

            return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        /// <summary>
        ///     Gets the current timestamp, using a cached per-frame value when available.
        /// </summary>
        private static DateTime GetTimestamp()
        {
            Func<int> provider = _frameCountProvider;

            if (provider != null && IsOnMainThread())
            {
                try
                {
                    int currentFrame = provider();
                    if (currentFrame != _lastUpdateFrame && currentFrame >= 0)
                    {
                        _cachedTimestamp = DateTime.Now;
                        _lastUpdateFrame = currentFrame;
                    }

                    return _cachedTimestamp;
                }
                catch
                {
                    return DateTime.Now;
                }
            }

            return DateTime.Now;
        }

        /// <summary>
        ///     Manually updates the cached timestamp.
        /// </summary>
        public static void UpdateCachedTimestamp()
        {
            if (!IsOnMainThread()) return;

            _cachedTimestamp = DateTime.Now;
            Func<int> provider = _frameCountProvider;
            if (provider != null)
            {
                try
                {
                    _lastUpdateFrame = provider();
                }
                catch
                {
                }
            }
        }

        #endregion

        /// <summary>
        ///     The timestamp when the log entry was created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     The severity level of the log entry.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        ///     The category/subsystem that generated the log.
        /// </summary>
        public LogCategory Category { get; }

        /// <summary>
        ///     The log message content.
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     The name of the method that generated the log (optional).
        /// </summary>
        public string CallerMemberName { get; }

        /// <summary>
        ///     The file path of the source that generated the log (optional).
        /// </summary>
        public string CallerFilePath { get; }

        /// <summary>
        ///     The line number in the source file (optional).
        /// </summary>
        public int CallerLineNumber { get; }

        /// <summary>
        ///     Optional structured context data for the log entry.
        ///     Used for key-value pairs that can be indexed by log aggregation systems.
        /// </summary>
        public IReadOnlyDictionary<string, object> Context { get; }

        /// <summary>
        ///     Optional exception associated with the log entry.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        ///     Correlation ID for distributed tracing.
        ///     Used to track related log entries across async operations.
        /// </summary>
        public string CorrelationId { get; }

        /// <summary>
        ///     Private constructor for immutable initialization.
        /// </summary>
        private LogEntry(
            DateTime timestamp,
            LogLevel level,
            LogCategory category,
            string message,
            Exception exception = null,
            string callerMemberName = null,
            string callerFilePath = null,
            int callerLineNumber = 0,
            IReadOnlyDictionary<string, object> context = null,
            string correlationId = null)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
            Exception = exception;
            CallerMemberName = callerMemberName;
            CallerFilePath = callerFilePath;
            CallerLineNumber = callerLineNumber;
            Context = context;
            CorrelationId = correlationId;
        }

        /// <summary>
        ///     Creates a new LogEntry with basic information.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="category">The log category.</param>
        /// <param name="message">The log message.</param>
        /// <param name="correlationId">Optional correlation ID for tracing.</param>
        /// <returns>A new LogEntry instance.</returns>
        public static LogEntry Create(LogLevel level, LogCategory category, string message,
            string correlationId = null) =>
            new(GetTimestamp(), level, category, message, correlationId: correlationId);

        /// <summary>
        ///     Creates a new LogEntry with structured context data.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="category">The log category.</param>
        /// <param name="message">The log message.</param>
        /// <param name="context">Key-value pairs to attach.</param>
        /// <param name="correlationId">Optional correlation ID for tracing.</param>
        /// <returns>A new LogEntry instance.</returns>
        public static LogEntry CreateWithContext(
            LogLevel level,
            LogCategory category,
            string message,
            IReadOnlyDictionary<string, object> context,
            string correlationId = null) =>
            new(GetTimestamp(), level, category, message, context: context, correlationId: correlationId);

        /// <summary>
        ///     Creates a new LogEntry with exception information.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="category">The log category.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">The exception to include.</param>
        /// <param name="correlationId">Optional correlation ID for tracing.</param>
        /// <returns>A new LogEntry instance.</returns>
        public static LogEntry CreateWithException(
            LogLevel level,
            LogCategory category,
            string message,
            Exception exception,
            string correlationId = null) =>
            new(GetTimestamp(), level, category, message, exception, correlationId: correlationId);

        /// <summary>
        ///     Creates a new LogEntry with exception and context information.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="category">The log category.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">The exception to include.</param>
        /// <param name="context">Key-value pairs to attach.</param>
        /// <param name="correlationId">Optional correlation ID for tracing.</param>
        /// <returns>A new LogEntry instance.</returns>
        public static LogEntry CreateWithExceptionAndContext(
            LogLevel level,
            LogCategory category,
            string message,
            Exception exception,
            IReadOnlyDictionary<string, object> context,
            string correlationId = null) =>
            new(GetTimestamp(), level, category, message, exception, context: context, correlationId: correlationId);

        /// <summary>
        ///     Indicates whether this entry has an associated exception.
        /// </summary>
        public bool HasException => Exception != null;

        /// <summary>
        ///     Indicates whether this entry has context data.
        /// </summary>
        public bool HasContext => Context != null && Context.Count > 0;

        /// <summary>
        ///     Indicates whether this entry has a correlation ID.
        /// </summary>
        public bool HasCorrelationId => !string.IsNullOrEmpty(CorrelationId);

        #region Convenience Factory Methods

        /// <summary>
        ///     Creates a Trace-level log entry.
        /// </summary>
        public static LogEntry Trace(LogCategory category, string message, string correlationId = null) =>
            Create(LogLevel.Trace, category, message, correlationId);

        /// <summary>
        ///     Creates a Trace-level log entry with context.
        /// </summary>
        public static LogEntry Trace(LogCategory category, string message, IReadOnlyDictionary<string, object> context,
            string correlationId = null) =>
            CreateWithContext(LogLevel.Trace, category, message, context, correlationId);

        /// <summary>
        ///     Creates a Debug-level log entry.
        /// </summary>
        public static LogEntry Debug(LogCategory category, string message, string correlationId = null) =>
            Create(LogLevel.Debug, category, message, correlationId);

        /// <summary>
        ///     Creates a Debug-level log entry with context.
        /// </summary>
        public static LogEntry Debug(LogCategory category, string message, IReadOnlyDictionary<string, object> context,
            string correlationId = null) =>
            CreateWithContext(LogLevel.Debug, category, message, context, correlationId);

        /// <summary>
        ///     Creates an Info-level log entry.
        /// </summary>
        public static LogEntry Info(LogCategory category, string message, string correlationId = null) =>
            Create(LogLevel.Info, category, message, correlationId);

        /// <summary>
        ///     Creates an Info-level log entry with context.
        /// </summary>
        public static LogEntry Info(LogCategory category, string message, IReadOnlyDictionary<string, object> context,
            string correlationId = null) => CreateWithContext(LogLevel.Info, category, message, context, correlationId);

        /// <summary>
        ///     Creates a Warning-level log entry.
        /// </summary>
        public static LogEntry Warning(LogCategory category, string message, string correlationId = null) =>
            Create(LogLevel.Warning, category, message, correlationId);

        /// <summary>
        ///     Creates a Warning-level log entry with context.
        /// </summary>
        public static LogEntry Warning(LogCategory category, string message,
            IReadOnlyDictionary<string, object> context, string correlationId = null) =>
            CreateWithContext(LogLevel.Warning, category, message, context, correlationId);

        /// <summary>
        ///     Creates an Error-level log entry.
        /// </summary>
        public static LogEntry Error(LogCategory category, string message, string correlationId = null) =>
            Create(LogLevel.Error, category, message, correlationId);

        /// <summary>
        ///     Creates an Error-level log entry with context.
        /// </summary>
        public static LogEntry Error(LogCategory category, string message, IReadOnlyDictionary<string, object> context,
            string correlationId = null) =>
            CreateWithContext(LogLevel.Error, category, message, context, correlationId);

        /// <summary>
        ///     Creates an Error-level log entry with an exception.
        /// </summary>
        public static LogEntry Error(LogCategory category, string message, Exception exception,
            string correlationId = null) =>
            CreateWithException(LogLevel.Error, category, message, exception, correlationId);

        /// <summary>
        ///     Creates an Error-level log entry with an exception and context.
        /// </summary>
        public static LogEntry Error(LogCategory category, string message, Exception exception,
            IReadOnlyDictionary<string, object> context, string correlationId = null) =>
            CreateWithExceptionAndContext(LogLevel.Error, category, message, exception, context, correlationId);

        #endregion
    }
}
