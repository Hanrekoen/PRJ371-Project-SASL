using UnityEditor;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The attention flash Convai editors run after a deep link — a status chip or an issue tile
    ///     opened a window and asked it to point at one row. Holds the "how much longer is that row
    ///     still highlighted" decision so the drawing code asks a question instead of reading a clock.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Not a refresh throttle.</b> <see cref="ConvaiEditorRefreshTimer" /> paces work that
    ///         would otherwise run every repaint; this one runs a highlight that is meant to end. They
    ///         look alike at the call site — both compare <see cref="EditorApplication.timeSinceStartup" />
    ///         against a stored deadline — which is exactly why a flash written out by hand reads as a
    ///         throttle written out by hand, and why both concepts are named.
    ///     </para>
    ///     <para>
    ///         <b>The repaint is part of the contract.</b> A flash is the one thing on a diagnostic
    ///         window that changes without input, so the window has to repaint itself while the flash
    ///         is running — and, just as importantly, stop the moment it ends. A window that forgets
    ///         <see cref="KeepAlive" /> shows a highlight frozen until the user happens to move the
    ///         mouse; a window that repaints unconditionally burns the editor's frame budget forever.
    ///         <see cref="KeepAlive" /> is both halves in one call.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     private ConvaiEditorFlashTimer _flash;
    ///
    ///     internal static void ShowFor(string findingId) { …; window._flash.Start(); }
    ///
    ///     private void OnGUI()
    ///     {
    ///         DrawRows(highlighted: _flash.IsRunning ? _focusFindingId : null);
    ///         _flash.KeepAlive(this);
    ///     }
    ///     </code>
    /// </example>
    internal struct ConvaiEditorFlashTimer
    {
        /// <summary>
        ///     How long a deep-linked row stays highlighted. Long enough to find with the eye after the
        ///     window has drawn and the user has looked up from whatever they clicked, short enough
        ///     that the highlight is gone before it becomes part of how the row normally looks.
        /// </summary>
        internal const double DefaultDurationSeconds = 2.5d;

        private double _until;

        /// <summary>Whether the flash is still running and its target should be drawn highlighted.</summary>
        internal bool IsRunning => EditorApplication.timeSinceStartup < _until;

        /// <summary>Starts (or restarts) the flash, so a second deep link re-flashes rather than inheriting a deadline that is nearly up.</summary>
        /// <param name="durationSeconds">How long the highlight lasts.</param>
        internal void Start(double durationSeconds = DefaultDurationSeconds) =>
            _until = EditorApplication.timeSinceStartup + durationSeconds;

        /// <summary>
        ///     Ends the flash now — the target it pointed at is gone, or the user has moved on and
        ///     highlighting a row they did not ask about would be noise.
        /// </summary>
        internal void Stop() => _until = 0d;

        /// <summary>
        ///     Repaints <paramref name="window" /> while the flash is running, and does nothing once it
        ///     is not. Call once at the end of the window's draw pass.
        /// </summary>
        internal void KeepAlive(EditorWindow window)
        {
            if (IsRunning) window.Repaint();
        }
    }
}
