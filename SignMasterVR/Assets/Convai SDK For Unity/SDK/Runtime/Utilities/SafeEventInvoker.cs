using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Utilities
{
    internal static class SafeEventInvoker
    {
        public static void Invoke(Action handlers, ILogger logger, string surfaceName, LogCategory category)
        {
            if (handlers == null) return;

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    LogFailure(handler, ex, logger, surfaceName, category);
                }
            }
        }

        public static void Invoke<T>(
            Action<T> handlers,
            T arg,
            ILogger logger,
            string surfaceName,
            LogCategory category)
        {
            if (handlers == null) return;

            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(arg);
                }
                catch (Exception ex)
                {
                    LogFailure(handler, ex, logger, surfaceName, category);
                }
            }
        }

        public static void Invoke<T1, T2>(
            Action<T1, T2> handlers,
            T1 arg1,
            T2 arg2,
            ILogger logger,
            string surfaceName,
            LogCategory category)
        {
            if (handlers == null) return;

            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(arg1, arg2);
                }
                catch (Exception ex)
                {
                    LogFailure(handler, ex, logger, surfaceName, category);
                }
            }
        }

        public static void Invoke<T1, T2, T3>(
            Action<T1, T2, T3> handlers,
            T1 arg1,
            T2 arg2,
            T3 arg3,
            ILogger logger,
            string surfaceName,
            LogCategory category)
        {
            if (handlers == null) return;

            foreach (Action<T1, T2, T3> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(arg1, arg2, arg3);
                }
                catch (Exception ex)
                {
                    LogFailure(handler, ex, logger, surfaceName, category);
                }
            }
        }

        private static void LogFailure(
            Delegate handler,
            Exception ex,
            ILogger logger,
            string surfaceName,
            LogCategory category)
        {
            // Always log the full exception to the Unity console to ensure visibility
            Debug.LogException(ex);

            string declaringType = handler.Method.DeclaringType?.FullName ??
                                   handler.Target?.GetType().FullName ??
                                   "<unknown>";
            string methodName = handler.Method.Name;
            string message =
                $"[{surfaceName}] Subscriber '{declaringType}.{methodName}' threw: {ex.GetType().Name}: {ex.Message}";

            if (logger != null)
                logger.Error(message, category);
            else
                ConvaiLogger.Error(message, category);
        }
    }
}
