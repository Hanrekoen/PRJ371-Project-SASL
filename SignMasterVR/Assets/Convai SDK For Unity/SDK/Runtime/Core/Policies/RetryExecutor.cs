using System;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Policies
{
    /// <summary>
    ///     Executes async operations with retry logic based on a provided policy.
    /// </summary>
    public sealed class RetryExecutor
    {
        private readonly IRetryPolicy _policy;

        public RetryExecutor(IRetryPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        ///     Executes an operation with retry logic.
        /// </summary>
        /// <param name="operation">The operation to execute. Receives attempt number (0-based) and cancellation token.</param>
        /// <param name="cancellationToken">Token to cancel the entire retry sequence.</param>
        /// <returns>The result of the successful operation.</returns>
        /// <exception cref="Exception">Rethrown from the last failed attempt if all retries are exhausted.</exception>
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<int, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            Exception lastException = null;

            for (int attempt = 0; attempt < _policy.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Wait for delay (skip for first attempt which should have zero delay)
                TimeSpan delay = _policy.GetDelay(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                try
                {
                    return await operation(attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    lastException = ex;

                    if (!_policy.ShouldRetry(ex, attempt))
                        throw;
                }
            }

            // Should not reach here, but rethrow last exception if we do
            throw lastException ?? new InvalidOperationException("Retry exhausted with no result");
        }
    }
}
