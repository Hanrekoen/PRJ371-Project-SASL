using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Fallback logger for native transport components when no logger is injected.
    ///     Delegates to ConvaiLogger.Instance when available, otherwise uses Unity Debug.
    /// </summary>
    internal sealed class NativeTransportFallbackLogger : ILogger
    {
        public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK)
        {
            if (ConvaiLogger.Instance != null)
                ConvaiLogger.Instance.Log(level, message, category);
            else
                LogToUnity(level, message);
        }

        public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (ConvaiLogger.Instance != null)
                ConvaiLogger.Instance.Log(level, message, context, category);
            else
                LogToUnity(level, message);
        }

        public void Debug(string message, LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Debug, message, category);

        public void Debug(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Debug, message, context, category);

        public void Info(string message, LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Info, message, category);

        public void Info(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Info, message, context, category);

        public void Warning(string message, LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Warning, message, category);

        public void Warning(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Warning, message, context, category);

        public void Error(string message, LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Error, message, category);

        public void Error(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Log(LogLevel.Error, message, context, category);

        public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK)
        {
            if (ConvaiLogger.Instance != null)
                ConvaiLogger.Instance.Error(exception, message, category);
            else
            {
                string fullMessage = string.IsNullOrEmpty(message)
                    ? exception?.ToString()
                    : $"{message}: {exception}";
                UnityEngine.Debug.LogError(fullMessage);
            }
        }

        public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Error(exception, message, category);

        public bool IsEnabled(LogLevel level, LogCategory category)
        {
            if (ConvaiLogger.Instance != null) return ConvaiLogger.Instance.IsEnabled(level, category);
            return level <= LogLevel.Info;
        }

        private static void LogToUnity(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(message);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(message);
                    break;
                default:
                    UnityEngine.Debug.Log(message);
                    break;
            }
        }
    }
}
