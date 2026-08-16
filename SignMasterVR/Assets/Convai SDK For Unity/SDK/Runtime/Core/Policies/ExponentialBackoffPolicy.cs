using System;
using System.Net.Http;

namespace Convai.Runtime.Core.Policies
{
    /// <summary>
    ///     A retry policy that implements exponential backoff with predefined delays.
    ///     Retries are only attempted for transient errors (timeouts, network issues, cancellation).
    /// </summary>
    /// <remarks>
    ///     Default delays: 0s (initial), 1s (first retry), 2s (second retry), 4s (third retry).
    ///     This gives a total of 4 attempts with increasing delays between retries.
    /// </remarks>
    public sealed class ExponentialBackoffPolicy : IRetryPolicy
    {
        /// <summary>
        ///     Default delay schedule for retry attempts.
        ///     Index 0 = initial attempt (no delay), subsequent indices = retry delays.
        /// </summary>
        private static readonly TimeSpan[] DefaultDelays =
        {
            TimeSpan.Zero, // Attempt 0: Initial attempt, no delay
            TimeSpan.FromSeconds(1), // Attempt 1: First retry after 1 second
            TimeSpan.FromSeconds(2), // Attempt 2: Second retry after 2 seconds
            TimeSpan.FromSeconds(4) // Attempt 3: Third retry after 4 seconds
        };

        /// <summary>
        ///     Gets the maximum number of attempts (4 by default: 1 initial + 3 retries).
        /// </summary>
        public int MaxAttempts => DefaultDelays.Length;

        /// <summary>
        ///     Gets the delay for the specified attempt number.
        /// </summary>
        /// <param name="attempt">Zero-based attempt number.</param>
        /// <returns>
        ///     The delay for the specified attempt. If the attempt number exceeds
        ///     the configured delays, returns the last configured delay.
        /// </returns>
        public TimeSpan GetDelay(int attempt)
        {
            if (attempt < 0)
                return TimeSpan.Zero;

            return attempt < DefaultDelays.Length
                ? DefaultDelays[attempt]
                : DefaultDelays[^1];
        }

        /// <summary>
        ///     Determines whether to retry based on the exception type and attempt count.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <param name="attempt">The zero-based attempt number that just failed.</param>
        /// <returns>
        ///     <c>true</c> if the exception is transient and more attempts remain;
        ///     otherwise, <c>false</c>.
        /// </returns>
        public bool ShouldRetry(Exception exception, int attempt)
        {
            // Cannot retry if we've exhausted all attempts
            // If attempt is N, we've completed N+1 attempts (0 through N)
            // We can retry if attempt+1 < MaxAttempts, i.e., attempt < MaxAttempts - 1
            if (attempt < 0 || attempt >= MaxAttempts - 1)
                return false;

            return IsTransientError(exception);
        }

        /// <summary>
        ///     Determines whether an exception represents a transient error
        ///     that may succeed on retry.
        /// </summary>
        /// <param name="exception">The exception to evaluate.</param>
        /// <returns><c>true</c> if the error is transient; otherwise, <c>false</c>.</returns>
        private static bool IsTransientError(Exception exception)
        {
            if (exception == null)
                return false;

            // Check the exception type and common transient patterns
            return exception switch
            {
                TimeoutException => true,
                HttpRequestException => true,
                OperationCanceledException => true,

                // Handle aggregate exceptions by checking inner exceptions
                AggregateException aggregate => ContainsTransientError(aggregate),

                // Check inner exception for wrapped transient errors
                _ when exception.InnerException != null => IsTransientError(exception.InnerException),

                _ => false
            };
        }

        /// <summary>
        ///     Checks if an aggregate exception contains any transient errors.
        /// </summary>
        private static bool ContainsTransientError(AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                if (IsTransientError(inner))
                    return true;
            }

            return false;
        }
    }
}
