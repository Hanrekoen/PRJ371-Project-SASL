#nullable enable
using System;
using System.IO;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Writes debug metrics to a single text file. Each line: UTC (ISO8601) | eventType | payload.
    ///     Thread-safe; flushes after each write so data is durable for analytics.
    ///     File initialization is lazy and non-blocking so startup never stalls if the file is temporarily locked.
    /// </summary>
    internal sealed class DebugMetricsFileWriter : IDebugMetricsFileWriter, IDisposable
    {
        private static readonly TimeSpan s_openRetryDelay = TimeSpan.FromMilliseconds(100);

        private readonly object _lock = new();
        private readonly string _filePath;
        private StreamWriter? _writer;
        private bool _headerPending;
        private DateTime _nextOpenAttemptUtc;
        private bool _disposed;

        public DebugMetricsFileWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            string directory = Path.GetDirectoryName(filePath) ?? "";
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _filePath = filePath;
            _headerPending = !File.Exists(filePath);
        }

        private bool TryEnsureWriterOpen()
        {
            if (_writer != null)
                return true;

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextOpenAttemptUtc)
                return false;

            try
            {
                // Allow other active sessions to keep the file open during reconnect churn.
                var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(stream)
                {
                    AutoFlush = true
                };

                if (_headerPending)
                {
                    _writer.WriteLine("# Convai debug metrics. Each line: UTC (ISO8601) | event_type | payload");
                    _headerPending = false;
                }

                _nextOpenAttemptUtc = default;
                return true;
            }
            catch (IOException)
            {
                _nextOpenAttemptUtc = nowUtc + s_openRetryDelay;
                return false;
            }
        }

        public void WriteLine(string eventType, string payload)
        {
            if (_disposed) return;

            string utc = DateTime.UtcNow.ToString("o");
            string safeType = (eventType ?? "").Replace("|", "_").Replace("\n", " ").Replace("\r", " ");
            string safePayload = (payload ?? "").Replace("\n", " ").Replace("\r", " ").Replace("|", ";");

            lock (_lock)
            {
                if (_disposed) return;
                if (!TryEnsureWriterOpen()) return;
                _writer?.WriteLine($"{utc} | {safeType} | {safePayload}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _writer?.Dispose();
                    _writer = null;
                }
                catch
                {
                    // ignore on shutdown
                }
            }
        }
    }
}
