using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Modules.BodyLanguage.Data;
using Convai.Runtime.Logging;

namespace Convai.Modules.BodyLanguage.Core.Diagnostics
{
    /// <summary>
    ///     Structured, verbosity-gated diagnostics channel for one character's body language
    ///     stack. Every subsystem (policy engine, directors, solvers) traces through this class
    ///     instead of calling <see cref="ConvaiLogger" /> directly, which guarantees a
    ///     consistent prefix, a single verbosity gate, and an in-memory ring buffer that HUDs
    ///     and tests can read back.
    /// </summary>
    /// <remarks>
    ///     Warnings and errors bypass the verbosity gate: they are always logged and always
    ///     recorded. <see cref="BodyLanguageTraceVerbosity.Firehose" /> messages are logged but
    ///     not recorded in the ring buffer, so per-tick dumps never evict the transition
    ///     history the buffer exists to preserve.
    /// </remarks>
    internal sealed class BodyLanguageTrace
    {
        /// <summary>Number of entries preserved in the ring buffer.</summary>
        public const int Capacity = 64;

        private readonly BodyLanguageTraceEntry[] _entries = new BodyLanguageTraceEntry[Capacity];
        private readonly Func<float> _clock;
        private int _count;
        private int _next;

        /// <summary>Owner tag included in every console line, e.g. the character name.</summary>
        public string Owner { get; }

        /// <summary>Current gate. Callers may change it at any time (profile hot-swap).</summary>
        public BodyLanguageTraceVerbosity Verbosity { get; set; } = BodyLanguageTraceVerbosity.Off;

        /// <summary>Total entries recorded since construction (monotonic, never wraps).</summary>
        public long TotalRecorded { get; private set; }

        public BodyLanguageTrace(string owner, Func<float> clock = null)
        {
            Owner = string.IsNullOrEmpty(owner) ? "ConvaiBodyLanguage" : owner;
            _clock = clock ?? DefaultClock;
        }

        /// <summary>State-level trace: policy switches, degradations, lifecycle events.</summary>
        public void State(string message) => Trace(BodyLanguageTraceVerbosity.State, message);

        /// <summary>Detail-level trace: director decisions, pulse verdicts, emotion blends.</summary>
        public void Detail(string message) => Trace(BodyLanguageTraceVerbosity.Detail, message);

        /// <summary>Firehose-level trace: per-tick numeric dumps. Logged, never recorded.</summary>
        public void Firehose(string message)
        {
            if (Verbosity < BodyLanguageTraceVerbosity.Firehose) return;
            ConvaiLogger.Debug($"[{Owner}] {message}", LogCategory.BodyLanguage);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Warning(string message)
        {
            Record(BodyLanguageTraceVerbosity.State, message);
            ConvaiLogger.Warning($"[{Owner}] {message}", LogCategory.BodyLanguage);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Error(string message)
        {
            Record(BodyLanguageTraceVerbosity.State, message);
            ConvaiLogger.Error($"[{Owner}] {message}", LogCategory.BodyLanguage);
        }

        /// <summary>
        ///     Copies the recorded entries (oldest first) into <paramref name="destination" />.
        ///     The list is cleared first. Returns the number of entries written.
        /// </summary>
        public int CopyRecentEntries(List<BodyLanguageTraceEntry> destination)
        {
            if (destination == null) return 0;

            destination.Clear();
            int start = _count < Capacity ? 0 : _next;
            for (int i = 0; i < _count; i++)
                destination.Add(_entries[(start + i) % Capacity]);

            return _count;
        }

        private void Trace(BodyLanguageTraceVerbosity level, string message)
        {
            if (Verbosity < level) return;

            Record(level, message);

            string line = $"[{Owner}] {message}";
            if (level == BodyLanguageTraceVerbosity.State)
                ConvaiLogger.Info(line, LogCategory.BodyLanguage);
            else
                ConvaiLogger.Debug(line, LogCategory.BodyLanguage);
        }

        private void Record(BodyLanguageTraceVerbosity level, string message)
        {
            _entries[_next] = new BodyLanguageTraceEntry(_clock(), level, message);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            TotalRecorded++;
        }

        private static float DefaultClock() =>
            UnityEngine.Application.isPlaying ? UnityEngine.Time.time : 0f;
    }
}
