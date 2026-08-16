using System;

namespace Convai.Runtime.Core.Policies
{
    /// <summary>
    ///     Defines a retry policy for handling transient failures in async operations.
    ///     Implementations determine when to retry, how long to wait between attempts,
    ///     and the maximum number of attempts allowed.
    /// </summary>
    public interface IRetryPolicy
    {
        /// <summary>
        ///     Gets the maximum number of attempts allowed, including the initial attempt.
        ///     For example, a value of 4 means 1 initial attempt + 3 retries.
        /// </summary>
        public int MaxAttempts { get; }

        /// <summary>
        ///     Gets the delay to wait before the specified attempt.
        /// </summary>
        /// <param name="attempt">
        ///     The zero-based attempt number. Attempt 0 is the initial attempt,
        ///     attempt 1 is the first retry, etc.
        /// </param>
        /// <returns>
        ///     The duration to wait before executing the attempt.
        ///     Typically returns <see cref="TimeSpan.Zero" /> for the initial attempt (attempt 0).
        /// </returns>
        public TimeSpan GetDelay(int attempt);

        /// <summary>
        ///     Determines whether a retry should be attempted after a failure.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <param name="attempt">
        ///     The zero-based attempt number that just failed.
        ///     Attempt 0 means the initial attempt failed.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a retry should be attempted; otherwise, <c>false</c>.
        ///     Returns <c>false</c> if the exception is not transient or if
        ///     the maximum number of attempts has been reached.
        /// </returns>
        public bool ShouldRetry(Exception exception, int attempt);
    }
}
