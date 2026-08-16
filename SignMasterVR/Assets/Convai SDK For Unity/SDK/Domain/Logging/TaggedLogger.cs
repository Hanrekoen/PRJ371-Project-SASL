using System;
using System.Collections.Generic;

namespace Convai.Domain.Logging
{
    internal sealed class TaggedLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly string _prefix;
        private readonly string _tag;

        internal TaggedLogger(ILogger inner, string tag)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _tag = tag ?? throw new ArgumentNullException(nameof(tag));
            _prefix = $"[{tag}] ";
        }

        internal ILogger Retag(string tag) => string.Equals(_tag, tag, StringComparison.Ordinal)
            ? this
            : new TaggedLogger(_inner, tag);

        public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) =>
            _inner.Log(level, Prefix(message), category);

        public void Log(
            LogLevel level,
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Log(level, Prefix(message), context, category);

        public void Debug(string message, LogCategory category = LogCategory.SDK) =>
            _inner.Debug(Prefix(message), category);

        public void Debug(
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Debug(Prefix(message), context, category);

        public void Info(string message, LogCategory category = LogCategory.SDK) =>
            _inner.Info(Prefix(message), category);

        public void Info(
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Info(Prefix(message), context, category);

        public void Warning(string message, LogCategory category = LogCategory.SDK) =>
            _inner.Warning(Prefix(message), category);

        public void Warning(
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Warning(Prefix(message), context, category);

        public void Error(string message, LogCategory category = LogCategory.SDK) =>
            _inner.Error(Prefix(message), category);

        public void Error(
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Error(Prefix(message), context, category);

        public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
            _inner.Error(exception, Prefix(message), category);

        public void Error(
            Exception exception,
            string message,
            IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            _inner.Error(exception, Prefix(message), context, category);

        public bool IsEnabled(LogLevel level, LogCategory category) => _inner.IsEnabled(level, category);

        private string Prefix(string message) => _prefix + message;
    }

    internal static class LoggerTaggingExtensions
    {
        internal static ILogger WithTag(this ILogger logger, string tag)
        {
            if (logger == null || string.IsNullOrWhiteSpace(tag)) return logger;

            return logger is TaggedLogger taggedLogger
                ? taggedLogger.Retag(tag)
                : new TaggedLogger(logger, tag);
        }
    }
}
