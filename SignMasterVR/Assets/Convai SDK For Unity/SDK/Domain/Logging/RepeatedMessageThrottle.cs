using System;
using System.Collections.Generic;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Keeps a fault that repeats every turn to one console line every so often, and says how
    ///     many times it repeated in between.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this is a type and not two fields in each caller.</b> Two places in this SDK
    ///         had already written the policy independently, and only one of them wrote it correctly.
    ///         The lip-sync ingress suppresses a repeat for a few seconds and then says it again with
    ///         a count; the action-response handler suppressed a repeat <em>forever</em>, using a set
    ///         it never cleared. One of those is a throttle and the other is a mute.
    ///     </para>
    ///     <para>
    ///         <b>Why "forever" is the wrong policy, specifically.</b> The reason to suppress is that
    ///         a Convai Character asks for the same missing target on every turn and a line per turn
    ///         buries the console. But the reason to say anything at all is that somebody is trying
    ///         to fix it — and fixing it means changing something and asking again. A mute answers
    ///         the second attempt with silence, which reads exactly like success. Worse, it is spent
    ///         on the first occurrence, which is usually before the developer has opened the console
    ///         to look: the diagnostic window still lists the fault, the console never mentioned it,
    ///         and the two channels appear to disagree about whether anything happened.
    ///     </para>
    ///     <para>
    ///         <b>The clock is the caller's.</b> Taking the time as an argument rather than reading
    ///         it here is what makes the policy testable without a clock abstraction — a test states
    ///         the instants it cares about and gets exact answers.
    ///     </para>
    /// </remarks>
    internal sealed class RepeatedMessageThrottle
    {
        private readonly struct Entry
        {
            internal Entry(DateTime saidAtUtc, int suppressed)
            {
                SaidAtUtc = saidAtUtc;
                Suppressed = suppressed;
            }

            internal DateTime SaidAtUtc { get; }

            internal int Suppressed { get; }
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly double _intervalSeconds;

        /// <param name="intervalSeconds">
        ///     How long one key stays quiet after it has been said. Clamped at zero, which makes
        ///     every occurrence speak — the setting a test uses to take the throttle out of the
        ///     picture.
        /// </param>
        internal RepeatedMessageThrottle(double intervalSeconds) =>
            _intervalSeconds = Math.Max(0d, intervalSeconds);

        /// <summary>How long a key stays quiet after being said, for callers that word the message.</summary>
        internal double IntervalSeconds => _intervalSeconds;

        /// <summary>
        ///     Whether this key should be said now, and how many occurrences were held back since the
        ///     last time it was.
        /// </summary>
        /// <remarks>
        ///     The count is handed over and cleared in the same call, so it describes the gap that
        ///     just ended rather than accumulating across every gap that ever happened.
        /// </remarks>
        internal bool ShouldSay(string key, DateTime nowUtc, out int suppressedSinceLast)
        {
            suppressedSinceLast = 0;
            if (string.IsNullOrEmpty(key))
                return false;

            if (_entries.TryGetValue(key, out Entry entry) &&
                (nowUtc - entry.SaidAtUtc).TotalSeconds < _intervalSeconds)
            {
                _entries[key] = new Entry(entry.SaidAtUtc, entry.Suppressed + 1);
                return false;
            }

            suppressedSinceLast = entry.Suppressed;
            _entries[key] = new Entry(nowUtc, 0);
            return true;
        }

        /// <summary>
        ///     Forgets every key, so the next occurrence of each is said again in full.
        /// </summary>
        /// <remarks>
        ///     For the moment something changed that could have fixed the fault — a new action config,
        ///     a reconnection. Without it the throttle answers "is it fixed?" with the silence it
        ///     owes to the previous question.
        /// </remarks>
        internal void Reset() => _entries.Clear();
    }
}
