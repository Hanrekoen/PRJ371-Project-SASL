using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Frame-wise timing primitives shared by the built-in action executors. Never uses
    ///     <see cref="Task.Delay(int)" />, <see cref="Task.Run(Action)" />, or a blocking sleep, so it
    ///     is WebGL-safe and keeps executor timing on the same per-frame cadence as the rest of the
    ///     SDK's gameplay code.
    /// </summary>
    internal static class ConvaiActionAsyncUtility
    {
        /// <summary>
        ///     Waits until <paramref name="seconds" /> of frame-wise elapsed time has passed, or the
        ///     token is canceled. A non-positive duration returns immediately (after honoring
        ///     cancellation).
        /// </summary>
        internal static async Task WaitSecondsAsync(float seconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seconds <= 0f)
                return;

            var clock = new ConvaiActionFrameClock();
            float elapsed = 0f;
            while (elapsed < seconds)
                elapsed += await clock.TickAsync(cancellationToken).ConfigureAwait(true);
        }

        /// <summary>
        ///     Waits, frame-wise, until <paramref name="predicate" /> returns true or the token is
        ///     canceled. Returns <c>true</c> once the predicate is satisfied, or <c>false</c> when
        ///     <paramref name="timeoutSeconds" /> of elapsed time passed first (values &lt;= 0 mean no
        ///     timeout — waits indefinitely for the predicate or cancellation).
        /// </summary>
        internal static async Task<bool> WaitUntilAsync(
            Func<bool> predicate,
            CancellationToken cancellationToken,
            float timeoutSeconds = -1f)
        {
            if (predicate == null)
                return true;

            cancellationToken.ThrowIfCancellationRequested();
            if (predicate())
                return true;

            var clock = new ConvaiActionFrameClock();
            float elapsed = 0f;
            while (true)
            {
                elapsed += await clock.TickAsync(cancellationToken).ConfigureAwait(true);

                if (predicate())
                    return true;

                if (timeoutSeconds > 0f && elapsed >= timeoutSeconds)
                    return false;
            }
        }
    }

    /// <summary>
    ///     Advances one Unity frame at a time and reports how much time elapsed for that frame.
    ///     While <see cref="Application.isPlaying" />, elapsed time is <see cref="Time.deltaTime" />
    ///     (matching normal gameplay pacing — zero extra timers). Outside Play mode — EditMode tests
    ///     and other non-playing tooling contexts, where <see cref="Time.deltaTime" /> is not reliably
    ///     driven by a running player loop — elapsed time instead falls back to real time measured
    ///     between ticks, so tests and editor tooling still make forward progress instead of hanging.
    /// </summary>
    // A class, deliberately: TickAsync is an async instance method, and calling an async method on
    // a mutable struct captures `this` by value into the compiler-generated state machine — any
    // field mutation (the stopwatch below) then only ever applies to that per-call copy and never
    // writes back to the caller's `clock` variable, so a struct here would silently never advance
    // real elapsed time across ticks (every call would re-create its own stopwatch from scratch).
    internal sealed class ConvaiActionFrameClock
    {
        private Stopwatch _editModeStopwatch;

        /// <summary>Yields one frame and returns the elapsed seconds to attribute to it.</summary>
        internal async Task<float> TickAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return ReadDeltaTime();
        }

        private float ReadDeltaTime()
        {
            if (UnityEngine.Application.isPlaying)
                return Time.deltaTime;

            _editModeStopwatch ??= Stopwatch.StartNew();
            float elapsedSeconds = (float)_editModeStopwatch.Elapsed.TotalSeconds;
            _editModeStopwatch.Restart();
            return elapsedSeconds;
        }
    }
}
