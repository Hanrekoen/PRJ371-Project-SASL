using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Modules.BodyAnimation.Core.Diagnostics
{
    /// <summary>
    ///     Structured, verbosity-gated diagnostics channel for one character's body animation
    ///     stack. Every subsystem (layers, locomotion state machine, selectors, executors)
    ///     traces through this class instead of calling <see cref="ConvaiLogger" /> directly,
    ///     which guarantees a consistent prefix, a single verbosity gate, and an in-memory
    ///     ring buffer that HUDs and tests can read back.
    /// </summary>
    /// <remarks>
    ///     Warnings and errors bypass the verbosity gate: they are always logged and always
    ///     recorded. <see cref="AnimTraceVerbosity.Firehose" /> messages are logged but not
    ///     recorded in the ring buffer, so per-tick dumps never evict the transition history
    ///     the buffer exists to preserve.
    /// </remarks>
    internal sealed class AnimTrace
    {
        /// <summary>Number of entries preserved in the ring buffer.</summary>
        public const int Capacity = 64;

        private readonly AnimTraceEntry[] _entries = new AnimTraceEntry[Capacity];
        private readonly Func<float> _clock;
        private int _count;
        private int _next;

        /// <summary>Owner tag included in every console line, e.g. the character name.</summary>
        public string Owner { get; }

        /// <summary>Current gate. Callers may change it at any time (config hot-swap).</summary>
        public AnimTraceVerbosity Verbosity { get; set; } = AnimTraceVerbosity.State;

        /// <summary>Total entries recorded since construction (monotonic, never wraps).</summary>
        public long TotalRecorded { get; private set; }

        /// <summary>
        ///     Whether a <see cref="State" /> line would survive the gate. Test this **before**
        ///     building the message, not after.
        /// </summary>
        /// <remarks>
        ///     The gate inside <see cref="State" />/<see cref="Detail" /> cannot save the caller
        ///     anything: C# evaluates arguments eagerly, so <c>Trace.Detail($"…{x}…")</c> builds
        ///     and immediately discards a string on every call, at every verbosity — including
        ///     <see cref="AnimTraceVerbosity.Off" />. Most such call sites are Detail-level, which
        ///     the default State verbosity throws away, so the allocation is pure waste on the
        ///     shipped path. Any call site whose message is interpolated or concatenated must be
        ///     wrapped in the matching property here, the way
        ///     <c>LayerRuntime.ReportTransition</c> already does.
        /// </remarks>
        public bool IsState => Verbosity >= AnimTraceVerbosity.State;

        /// <summary>Whether a <see cref="Detail" /> line would survive the gate. See <see cref="IsState" />.</summary>
        public bool IsDetail => Verbosity >= AnimTraceVerbosity.Detail;

        /// <summary>Whether a <see cref="Firehose" /> line would survive the gate. See <see cref="IsState" />.</summary>
        public bool IsFirehose => Verbosity >= AnimTraceVerbosity.Firehose;

        public AnimTrace(string owner, Func<float> clock = null)
        {
            Owner = string.IsNullOrEmpty(owner) ? "BodyAnimation" : owner;
            _clock = clock ?? DefaultClock;
        }

        /// <summary>State-level trace: transitions, ownership changes, lifecycle events.</summary>
        public void State(string message) => Trace(AnimTraceVerbosity.State, message);

        /// <summary>Detail-level trace: selector decisions, variant rolls, clamps.</summary>
        public void Detail(string message) => Trace(AnimTraceVerbosity.Detail, message);

        /// <summary>Firehose-level trace: per-tick numeric dumps. Logged, never recorded.</summary>
        public void Firehose(string message)
        {
            if (Verbosity < AnimTraceVerbosity.Firehose) return;
            ConvaiLogger.Debug($"[{Owner}] {message}", LogCategory.Animation);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Warning(string message)
        {
            Record(AnimTraceVerbosity.State, message);
            ConvaiLogger.Warning($"[{Owner}] {message}", LogCategory.Animation);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Error(string message)
        {
            Record(AnimTraceVerbosity.State, message);
            ConvaiLogger.Error($"[{Owner}] {message}", LogCategory.Animation);
        }

        /// <summary>
        ///     Copies the recorded entries (oldest first) into <paramref name="destination" />.
        ///     The list is cleared first. Returns the number of entries written.
        /// </summary>
        public int CopyRecentEntries(List<AnimTraceEntry> destination)
        {
            if (destination == null) return 0;

            destination.Clear();
            int start = _count < Capacity ? 0 : _next;
            for (int i = 0; i < _count; i++)
                destination.Add(_entries[(start + i) % Capacity]);

            return _count;
        }

        private void Trace(AnimTraceVerbosity level, string message)
        {
            if (Verbosity < level) return;

            Record(level, message);

            string line = $"[{Owner}] {message}";
            if (level == AnimTraceVerbosity.State)
                ConvaiLogger.Info(line, LogCategory.Animation);
            else
                ConvaiLogger.Debug(line, LogCategory.Animation);
        }

        private void Record(AnimTraceVerbosity level, string message)
        {
            _entries[_next] = new AnimTraceEntry(_clock(), level, message);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            TotalRecorded++;
        }

        private static float DefaultClock()
        {
#if UNITY_2017_1_OR_NEWER
            return UnityEngine.Application.isPlaying ? UnityEngine.Time.time : 0f;
#else
            return 0f;
#endif
        }
    }
}
