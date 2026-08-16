using System;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Producer-side handle for creating and controlling an <see cref="IConvaiOperation{T}" />.
    ///     Provides progress reporting, result setting, and cancellation token access.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Usage pattern</b>: The producer creates a <see cref="ConvaiOperationSource{T}" />,
    ///         exposes <see cref="Operation" /> to the consumer, and calls <see cref="SetResult" />,
    ///         <see cref="SetException" />, or <see cref="SetCanceled" /> when the work completes.
    ///     </para>
    ///     <para>
    ///         <b>Progress</b>: Call <see cref="ReportProgress" /> during long-running work.
    ///         Consumers observe progress via <see cref="IConvaiOperation{T}.Progress" />.
    ///     </para>
    ///     <para>
    ///         <b>Cancellation</b>: Pass <see cref="Token" /> to async work so that
    ///         <see cref="IConvaiOperation{T}.Cancel" /> triggers cooperative cancellation.
    ///     </para>
    ///     <example>
    ///         <code>
    ///         public IConvaiOperation&lt;string&gt; DownloadAsync()
    ///         {
    ///             var source = new ConvaiOperationSource&lt;string&gt;();
    ///             _ = DownloadCore(source);
    ///             return source.Operation;
    ///         }
    /// 
    ///         private async Task DownloadCore(ConvaiOperationSource&lt;string&gt; source)
    ///         {
    ///             try
    ///             {
    ///                 source.ReportProgress(0.1f);
    ///                 string data = await FetchData(source.Token);
    ///                 source.ReportProgress(0.9f);
    ///                 source.SetResult(data);
    ///             }
    ///             catch (OperationCanceledException) { source.SetCanceled(); }
    ///             catch (Exception ex) { source.SetException(ex); }
    ///         }
    ///         </code>
    ///     </example>
    /// </remarks>
    public sealed class ConvaiOperationSource<T>
    {
        private readonly CancellationTokenSource _cts;
        private readonly ConvaiOperation<T> _operation;
        private readonly TaskCompletionSource<T> _tcs;

        /// <summary>
        ///     Creates a new operation source with its own cancellation token.
        /// </summary>
        public ConvaiOperationSource()
        {
            _tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cts = new CancellationTokenSource();
            _operation = ConvaiOperation<T>.FromTask(_tcs.Task, _cts);
        }

        /// <summary>
        ///     Creates a new operation source linked to an external cancellation token.
        ///     Cancellation of <paramref name="externalToken" /> or <see cref="IConvaiOperation{T}.Cancel" />
        ///     will both trigger cancellation.
        /// </summary>
        /// <param name="externalToken">External token to link with.</param>
        public ConvaiOperationSource(CancellationToken externalToken)
        {
            _tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _operation = ConvaiOperation<T>.FromTask(_tcs.Task, _cts);
        }

        /// <summary>
        ///     The consumer-facing operation handle. Expose this to callers.
        /// </summary>
        public IConvaiOperation<T> Operation => _operation;

        /// <summary>
        ///     Cancellation token that producers should pass to async work.
        ///     Triggered when the consumer calls <see cref="IConvaiOperation{T}.Cancel" />
        ///     or when a linked external token is canceled.
        /// </summary>
        public CancellationToken Token => _cts.Token;

        #region Progress

        /// <summary>
        ///     Reports progress for the operation.
        ///     Values are clamped to 0.0–1.0.
        /// </summary>
        /// <param name="progress">Progress value from 0.0 (not started) to 1.0 (complete).</param>
        public void ReportProgress(float progress) => _operation.SetProgress(progress);

        #endregion

        #region Completion

        /// <summary>
        ///     Completes the operation with a successful result.
        /// </summary>
        /// <param name="result">The result value.</param>
        /// <exception cref="InvalidOperationException">If the operation has already completed.</exception>
        public void SetResult(T result) => _tcs.SetResult(result);

        /// <summary>
        ///     Completes the operation with an exception.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <exception cref="InvalidOperationException">If the operation has already completed.</exception>
        public void SetException(Exception exception) =>
            _tcs.SetException(exception ?? throw new ArgumentNullException(nameof(exception)));

        /// <summary>
        ///     Completes the operation as canceled.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the operation has already completed.</exception>
        public void SetCanceled() => _tcs.SetCanceled();

        /// <summary>
        ///     Attempts to complete the operation with a successful result.
        /// </summary>
        /// <param name="result">The result value.</param>
        /// <returns>True if the result was set; false if the operation already completed.</returns>
        public bool TrySetResult(T result) => _tcs.TrySetResult(result);

        /// <summary>
        ///     Attempts to complete the operation with an exception.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <returns>True if the exception was set; false if the operation already completed.</returns>
        public bool TrySetException(Exception exception) =>
            _tcs.TrySetException(exception ?? throw new ArgumentNullException(nameof(exception)));

        /// <summary>
        ///     Attempts to complete the operation as canceled.
        /// </summary>
        /// <returns>True if cancellation was set; false if the operation already completed.</returns>
        public bool TrySetCanceled() => _tcs.TrySetCanceled();

        #endregion
    }
}
