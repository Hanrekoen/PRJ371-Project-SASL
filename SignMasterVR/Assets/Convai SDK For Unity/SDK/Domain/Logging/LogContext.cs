using System;
using System.Collections.Generic;
using System.Threading;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Manages logging context data that flows across async operations.
    ///     Use this to set correlation IDs and global context properties.
    /// </summary>
    /// <remarks>
    ///     LogContext provides two types of context:
    ///     1. Global context - shared across all threads (e.g., SessionId, AppVersion)
    ///     2. Scoped context - flows through async/await via AsyncLocal (e.g., RequestId, TraceId)
    ///     Example usage:
    ///     <code>
    /// 
    /// LogContext.SetGlobalProperty("AppVersion", "1.0.0");
    /// LogContext.SetGlobalProperty("SessionId", sessionId);
    /// 
    /// 
    /// using (LogContext.PushCorrelationId(Guid.NewGuid().ToString("N")))
    /// {
    /// 
    ///     logger.Info("Processing request");
    /// }
    /// </code>
    /// </remarks>
    public static class LogContext
    {
        private static readonly AsyncLocal<string> _correlationId = new();

        private static readonly object _globalLock = new();
        private static Dictionary<string, object> _globalProperties = new();

        private static readonly AsyncLocal<Dictionary<string, object>> _scopedProperties = new();

        /// <summary>
        ///     Gets or sets the current correlation ID for distributed tracing.
        ///     This value flows through async/await operations.
        /// </summary>
        public static string CorrelationId
        {
            get => _correlationId.Value;
            set => _correlationId.Value = value;
        }

        /// <summary>
        ///     Pushes a new correlation ID onto the context and returns a disposable scope.
        ///     When disposed, the previous correlation ID is restored.
        /// </summary>
        /// <param name="correlationId">The correlation ID to set. If null, a new GUID is generated.</param>
        /// <returns>A disposable that restores the previous correlation ID when disposed.</returns>
        public static IDisposable PushCorrelationId(string correlationId = null)
        {
            string previous = _correlationId.Value;
            _correlationId.Value = correlationId ?? Guid.NewGuid().ToString("N");
            return new CorrelationIdScope(previous);
        }

        /// <summary>
        ///     Sets a global property that will be included in all log entries.
        ///     Thread-safe. Uses copy-on-write for lock-free reads.
        /// </summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        public static void SetGlobalProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (_globalLock)
            {
                var newDict = new Dictionary<string, object>(_globalProperties);
                newDict[key] = value;
                _globalProperties = newDict;
            }
        }

        /// <summary>
        ///     Removes a global property.
        /// </summary>
        /// <param name="key">The property key to remove.</param>
        public static void RemoveGlobalProperty(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (_globalLock)
            {
                var newDict = new Dictionary<string, object>(_globalProperties);
                newDict.Remove(key);
                _globalProperties = newDict;
            }
        }

        /// <summary>
        ///     Gets a snapshot of all global properties.
        ///     Lock-free read of the current snapshot.
        /// </summary>
        /// <returns>A read-only dictionary of global properties.</returns>
        public static IReadOnlyDictionary<string, object> GetGlobalProperties() => _globalProperties;

        /// <summary>
        ///     Sets a scoped property that flows through async/await.
        ///     These properties are only visible within the current async context.
        /// </summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        public static void SetScopedProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;

            Dictionary<string, object> current = _scopedProperties.Value;
            if (current == null)
            {
                current = new Dictionary<string, object>();
                _scopedProperties.Value = current;
            }

            var newDict = new Dictionary<string, object>(current);
            newDict[key] = value;
            _scopedProperties.Value = newDict;
        }

        /// <summary>
        ///     Gets a snapshot of all scoped properties for the current async context.
        /// </summary>
        /// <returns>A read-only dictionary of scoped properties, or null if none set.</returns>
        public static IReadOnlyDictionary<string, object> GetScopedProperties() => _scopedProperties.Value;

        /// <summary>
        ///     Gets all context properties (global + scoped) merged together.
        ///     Scoped properties override global properties with the same key.
        /// </summary>
        /// <returns>A merged dictionary of all context properties.</returns>
        public static IReadOnlyDictionary<string, object> GetAllProperties()
        {
            Dictionary<string, object> global = _globalProperties;
            Dictionary<string, object> scoped = _scopedProperties.Value;

            if (global.Count == 0 && (scoped == null || scoped.Count == 0)) return null;

            var merged = new Dictionary<string, object>(global);

            if (scoped != null)
            {
                foreach (KeyValuePair<string, object> kvp in scoped)
                    merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        /// <summary>
        ///     Clears all global properties.
        /// </summary>
        public static void ClearGlobalProperties()
        {
            lock (_globalLock) _globalProperties = new Dictionary<string, object>();
        }

        /// <summary>
        ///     Clears all scoped properties for the current async context.
        /// </summary>
        public static void ClearScopedProperties() => _scopedProperties.Value = null;

        /// <summary>
        ///     Clears all context (correlation ID, global, and scoped properties).
        /// </summary>
        public static void ClearAll()
        {
            _correlationId.Value = null;
            ClearGlobalProperties();
            ClearScopedProperties();
        }

        /// <summary>
        ///     Disposable scope that restores the previous correlation ID.
        /// </summary>
        private sealed class CorrelationIdScope : IDisposable
        {
            private readonly string _previousId;
            private bool _disposed;

            public CorrelationIdScope(string previousId)
            {
                _previousId = previousId;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _correlationId.Value = _previousId;
            }
        }
    }
}
