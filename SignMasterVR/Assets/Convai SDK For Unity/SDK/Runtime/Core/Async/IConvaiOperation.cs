using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Awaitable one-shot asynchronous SDK operation with progress, cancellation, and chaining support.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Awaiting</b>: Use <c>await operation</c> or <c>await operation.AsTask()</c>.
    ///     </para>
    ///     <para>
    ///         <b>Coroutines</b>: Use <c>yield return operation.ToCoroutine(...)</c> for Unity interop.
    ///     </para>
    ///     <para>
    ///         <b>Chaining</b>: Use <c>operation.ContinueWith(result => Transform(result))</c>.
    ///     </para>
    ///     <para>
    ///         <b>Progress</b>: Poll <see cref="Progress" /> (0.0–1.0) for long-running operations.
    ///         Producers report progress via <see cref="ConvaiOperationSource{T}.ReportProgress" />.
    ///     </para>
    ///     <para>
    ///         <b>Cancellation</b>: Call <see cref="Cancel" /> to request cooperative cancellation.
    ///         Not all operations support cancellation; check <see cref="IsCanceled" /> after requesting.
    ///     </para>
    /// </remarks>
    public interface IConvaiOperation<T>
    {
        #region Cancellation

        /// <summary>
        ///     Requests cooperative cancellation of the operation.
        ///     No-op if the operation does not support cancellation or has already completed.
        /// </summary>
        public void Cancel();

        #endregion

        #region Unity Interop

        /// <summary>
        ///     Converts this operation to a Unity coroutine that yields until completion.
        /// </summary>
        /// <param name="onSuccess">Called with the result when the operation succeeds. May be null.</param>
        /// <param name="onError">Called with the error when the operation faults. May be null.</param>
        /// <returns>An <see cref="IEnumerator" /> suitable for <c>StartCoroutine</c>.</returns>
        /// <example>
        ///     <code>
        ///     StartCoroutine(operation.ToCoroutine(
        ///         result => Debug.Log($"Done: {result}"),
        ///         error => Debug.LogError($"Failed: {error}")
        ///     ));
        ///     </code>
        /// </example>
        public IEnumerator ToCoroutine(Action<T> onSuccess = null, Action<ConvaiError> onError = null);

        #endregion

        #region Status

        /// <summary>Current execution status of the operation.</summary>
        public OperationStatus Status { get; }

        /// <summary>Whether the operation has completed (successfully, faulted, or canceled).</summary>
        public bool IsCompleted { get; }

        /// <summary>Whether the operation completed successfully.</summary>
        public bool IsSuccessful { get; }

        /// <summary>Whether the operation was canceled.</summary>
        public bool IsCanceled { get; }

        /// <summary>Whether the operation faulted with an error.</summary>
        public bool HasError { get; }

        /// <summary>Error details if the operation faulted; default if no error.</summary>
        public ConvaiError Error { get; }

        /// <summary>
        ///     Current progress of the operation, from 0.0 (not started) to 1.0 (complete).
        ///     Returns 0.0 if the producer does not report progress.
        ///     Returns 1.0 once the operation succeeds.
        /// </summary>
        public float Progress { get; }

        #endregion

        #region Awaiting

        /// <summary>Returns the underlying Task for async/await consumption.</summary>
        public Task<T> AsTask();

        /// <summary>Gets an awaiter for direct <c>await</c> usage.</summary>
        public TaskAwaiter<T> GetAwaiter();

        #endregion

        #region Chaining

        /// <summary>
        ///     Creates a new operation that transforms the result of this operation.
        /// </summary>
        /// <typeparam name="TNext">The type of the transformed result.</typeparam>
        /// <param name="selector">Synchronous transform function applied to the result.</param>
        /// <returns>A new operation that completes with the transformed result.</returns>
        /// <remarks>
        ///     If this operation faults or is canceled, the continuation propagates the fault/cancellation
        ///     without invoking <paramref name="selector" />.
        /// </remarks>
        public IConvaiOperation<TNext> ContinueWith<TNext>(Func<T, TNext> selector);

        /// <summary>
        ///     Creates a new operation that asynchronously transforms the result of this operation.
        /// </summary>
        /// <typeparam name="TNext">The type of the transformed result.</typeparam>
        /// <param name="selector">Asynchronous transform function applied to the result.</param>
        /// <returns>A new operation that completes with the transformed result.</returns>
        /// <remarks>
        ///     If this operation faults or is canceled, the continuation propagates the fault/cancellation
        ///     without invoking <paramref name="selector" />.
        /// </remarks>
        public IConvaiOperation<TNext> ContinueWith<TNext>(Func<T, Task<TNext>> selector);

        #endregion
    }
}
