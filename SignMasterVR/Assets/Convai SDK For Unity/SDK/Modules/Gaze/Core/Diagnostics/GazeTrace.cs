using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Logging;

namespace Convai.Modules.Gaze.Core.Diagnostics
{
    /// <summary>
    ///     Structured, verbosity-gated diagnostics channel for one character's gaze stack.
    ///     Every subsystem (targeting, policy, solvers, behaviors, reorientation) traces
    ///     through this class instead of calling <see cref="ConvaiLogger" /> directly, which
    ///     guarantees a consistent prefix, a single verbosity gate, and an in-memory ring
    ///     buffer that HUDs and tests can read back.
    /// </summary>
    /// <remarks>
    ///     Warnings and errors bypass the verbosity gate: they are always logged and always
    ///     recorded. <see cref="GazeTraceVerbosity.Firehose" /> messages are logged but not
    ///     recorded in the ring buffer, so per-tick dumps never evict the transition history
    ///     the buffer exists to preserve. Everything else is always recorded and only
    ///     conditionally logged — see <see cref="Trace" /> for why those two are separate.
    /// </remarks>
    internal sealed class GazeTrace
    {
        /// <summary>Number of entries preserved in the ring buffer.</summary>
        public const int Capacity = 64;

        private readonly GazeTraceEntry[] _entries = new GazeTraceEntry[Capacity];
        private readonly Func<float> _clock;
        private int _count;
        private int _next;

        /// <summary>Owner tag included in every console line, e.g. the character name.</summary>
        public string Owner { get; }

        /// <summary>
        ///     Current gate. Callers may change it at any time (profile hot-swap). Starts at
        ///     <see cref="GazeTraceVerbosity.Off" /> to match the shipped profile default, so a
        ///     trace that is constructed before its profile resolves is silent rather than
        ///     briefly chatty.
        /// </summary>
        public GazeTraceVerbosity Verbosity { get; set; } = GazeTraceVerbosity.Off;

        /// <summary>Total entries recorded since construction (monotonic, never wraps).</summary>
        public long TotalRecorded { get; private set; }

        public GazeTrace(string owner, Func<float> clock = null)
        {
            Owner = string.IsNullOrEmpty(owner) ? "ConvaiGaze" : owner;
            _clock = clock ?? DefaultClock;
        }

        /// <summary>
        ///     Whether <paramref name="level" /> currently passes the gate. Call sites that
        ///     fire often enough to matter (per saccade, per glance) test this BEFORE building
        ///     their interpolated message, so a character running at
        ///     <see cref="GazeTraceVerbosity.Off" /> allocates nothing. Edge-triggered call
        ///     sites (target transitions, lifecycle) format unconditionally — they are rare
        ///     enough that the extra guard would cost more readability than it saves garbage.
        /// </summary>
        public bool IsEnabled(GazeTraceVerbosity level) => Verbosity >= level;

        /// <summary>State-level trace: target changes, policy switches, lifecycle events.</summary>
        public void State(string message) => Trace(GazeTraceVerbosity.State, message);

        /// <summary>Detail-level trace: arbiter scores, saccade decisions, limit clamps.</summary>
        public void Detail(string message) => Trace(GazeTraceVerbosity.Detail, message);

        /// <summary>Firehose-level trace: per-tick numeric dumps. Logged, never recorded.</summary>
        public void Firehose(string message)
        {
            if (Verbosity < GazeTraceVerbosity.Firehose) return;
            ConvaiLogger.Debug($"[{Owner}] {message}", LogCategory.Gaze);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Warning(string message)
        {
            Record(GazeTraceVerbosity.State, message);
            ConvaiLogger.Warning($"[{Owner}] {message}", LogCategory.Gaze);
        }

        /// <summary>Always logged and recorded regardless of verbosity.</summary>
        public void Error(string message)
        {
            Record(GazeTraceVerbosity.State, message);
            ConvaiLogger.Error($"[{Owner}] {message}", LogCategory.Gaze);
        }

        /// <summary>
        ///     Copies the recorded entries (oldest first) into <paramref name="destination" />.
        ///     The list is cleared first. Returns the number of entries written.
        /// </summary>
        public int CopyRecentEntries(List<GazeTraceEntry> destination)
        {
            if (destination == null) return 0;

            destination.Clear();
            int start = _count < Capacity ? 0 : _next;
            for (int i = 0; i < _count; i++)
                destination.Add(_entries[(start + i) % Capacity]);

            return _count;
        }

        /// <summary>
        ///     Records into the ring buffer, then echoes to the console only if the gate allows.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The buffer is deliberately not gated by <see cref="Verbosity" />.</b> The gate
        ///         answers "what does this character write to the console", which now ships at
        ///         <see cref="GazeTraceVerbosity.Off" />. The ring buffer answers a different
        ///         question — "what did this character just do" — and it is what the inspector's Live
        ///         panel and the editor window's Live mode read back. Coupling the two would mean a
        ///         quiet console also blinds the only live diagnostic surface the module has, which
        ///         is the opposite of the trade a user makes when they turn logging down.
        ///     </para>
        ///     <para>
        ///         Free to do: the buffer is a fixed 64-entry array allocated once in the
        ///         constructor, and every message reaching this method was already interpolated at
        ///         the call site before the gate could reject it. Detail-level call sites test
        ///         <see cref="IsEnabled" /> themselves and never get this far while gated, so a
        ///         character running at <c>Off</c> records only its rare edge-triggered State lines.
        ///     </para>
        /// </remarks>
        private void Trace(GazeTraceVerbosity level, string message)
        {
            Record(level, message);

            if (Verbosity < level) return;

            string line = $"[{Owner}] {message}";
            if (level == GazeTraceVerbosity.State)
                ConvaiLogger.Info(line, LogCategory.Gaze);
            else
                ConvaiLogger.Debug(line, LogCategory.Gaze);
        }

        private void Record(GazeTraceVerbosity level, string message)
        {
            _entries[_next] = new GazeTraceEntry(_clock(), level, message);
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
