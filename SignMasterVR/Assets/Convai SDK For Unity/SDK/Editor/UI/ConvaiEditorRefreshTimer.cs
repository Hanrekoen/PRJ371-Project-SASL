using UnityEditor;
using UnityEngine;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The refresh throttle Convai editors put in front of expensive cached state — a scene scan,
    ///     a rig walk, a validation report. Holds one editor's "when may this be rebuilt again"
    ///     decision so the rebuild happens at a human rate rather than a repaint rate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why the throttle is needed at all.</b> An IMGUI inspector repaints on every mouse
    ///         move over the Inspector window, and in Play mode eleven Convai inspectors repaint
    ///         continuously. Work placed directly in a draw path therefore runs tens of times a
    ///         second. Nothing these editors inspect — components in a scene, bones on a rig, whether
    ///         a profile is assigned — can change faster than a user can act, so measuring it more
    ///         often than a few times a second buys nothing and costs everything.
    ///     </para>
    ///     <para>
    ///         <b>Why the Layout gate is part of the contract.</b> A refresh is only allowed on
    ///         <see cref="EventType.Layout" />. IMGUI reserves layout during the Layout pass and draws
    ///         against those reservations during Repaint; content that appears or disappears between
    ///         the two passes is drawn against a layout that was computed without it, which is how a
    ///         panel ends up clipped or overlapping. Refreshing only at the top of a pass makes both
    ///         passes see the same data.
    ///     </para>
    ///     <para>
    ///         <b>Why it is shared.</b> Written per editor this is always the same three parts — a
    ///         cached value, a next-refresh deadline and a Layout check — and the only thing that
    ///         varies is the interval, which then drifts silently between panels. Another copy is
    ///         exactly the fork the design system's guard tests exist to prevent.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     private ConvaiEditorRefreshTimer _timer;
    ///     private Report _cached;
    ///
    ///     private Report GetReport()
    ///     {
    ///         if (_timer.ShouldRefresh(_cached != null, 0.5d))
    ///             _cached = BuildReport();
    ///         return _cached;
    ///     }
    ///     </code>
    /// </example>
    internal struct ConvaiEditorRefreshTimer
    {
        /// <summary>
        ///     The interval Convai editors use unless they have a reason not to. Half a second is
        ///     below the threshold at which a user notices a readout lagging their own edit, and far
        ///     above the repaint rate it is protecting against.
        /// </summary>
        internal const double DefaultIntervalSeconds = 0.5d;

        private double _nextRefreshTime;

        /// <summary>
        ///     Whether the caller should rebuild its cached state now. Rebuilds immediately when there
        ///     is nothing cached; afterwards only at the top of a pass, and only once
        ///     <paramref name="intervalSeconds" /> have elapsed.
        /// </summary>
        /// <param name="hasCachedValue">
        ///     Whether the caller already holds a usable result. Pass <c>false</c> the first time and
        ///     after <see cref="Invalidate" />; the timer then refreshes without waiting, because an
        ///     editor with nothing to show must show something on this pass, not the next one.
        /// </param>
        /// <param name="intervalSeconds">Minimum seconds between two rebuilds.</param>
        internal bool ShouldRefresh(bool hasCachedValue, double intervalSeconds = DefaultIntervalSeconds)
        {
            if (!hasCachedValue)
            {
                Arm(intervalSeconds);
                return true;
            }

            Event current = Event.current;
            if (current == null || current.type != EventType.Layout)
                return false;

            if (EditorApplication.timeSinceStartup < _nextRefreshTime)
                return false;

            Arm(intervalSeconds);
            return true;
        }

        /// <summary>
        ///     Marks the cached state stale. The caller still clears its own cache; this only decides
        ///     <em>when</em> the rebuild is allowed to happen.
        /// </summary>
        /// <param name="immediately">
        ///     <c>true</c> after an edit the user just made, so the next pass reports the result of
        ///     their own click rather than making them wait out the interval to see it.
        /// </param>
        internal void Invalidate(bool immediately = false) =>
            _nextRefreshTime = immediately ? 0d : EditorApplication.timeSinceStartup;

        /// <summary>
        ///     Restarts the interval because the caller rebuilt its cached state out of band — from a
        ///     button, an undo callback, or its own <c>Refresh…</c> entry point — rather than because
        ///     <see cref="ShouldRefresh" /> said so.
        /// </summary>
        /// <remarks>
        ///     Without this, an out-of-band rebuild leaves an interval that expired long ago, and the
        ///     next pass rebuilds again for nothing.
        /// </remarks>
        internal void MarkRefreshed(double intervalSeconds = DefaultIntervalSeconds) => Arm(intervalSeconds);

        private void Arm(double intervalSeconds) =>
            _nextRefreshTime = EditorApplication.timeSinceStartup + intervalSeconds;
    }
}
