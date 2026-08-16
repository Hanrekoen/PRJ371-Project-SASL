using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Default awaitable implementation of <see cref="IConvaiOperation{T}" />.
    ///     Supports progress reporting, cooperative cancellation, chaining, and Unity coroutine conversion.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Creating operations</b>: Use the static factory methods (<see cref="FromTask(Task{T})" />,
    ///         <see cref="Succeeded" />, <see cref="Failed" />, <see cref="Canceled()" />).
    ///         For producer-controlled operations with progress and cancellation, use
    ///         <see cref="ConvaiOperationSource{T}" />.
    ///     </para>
    ///     <para>
    ///         <see cref="Progress" /> returns 0.0 for operations created without a producer source,
    ///         and <see cref="Cancel" /> is a no-op for operations created without a <see cref="CancellationTokenSource" />.
    ///     </para>
    /// </remarks>
    public sealed class ConvaiOperation<T> : IConvaiOperation<T>
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task<T> _task;
        private volatile float _progress;

        private ConvaiOperation(Task<T> task, CancellationTokenSource cts = null)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            _cts = cts;
        }

        #region Cancellation

        /// <inheritdoc />
        public void Cancel()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // CTS already disposed — operation completed before cancel was called.
                }
            }
        }

        #endregion

        #region Unity Interop

        /// <inheritdoc />
        public IEnumerator ToCoroutine(Action<T> onSuccess = null, Action<ConvaiError> onError = null)
        {
            while (!_task.IsCompleted)
                yield return null;

            if (_task.IsCompletedSuccessfully)
                onSuccess?.Invoke(_task.Result);
            else if (_task.IsFaulted)
            {
                ConvaiError error = ConvaiError.FromException(_task.Exception?.GetBaseException());
                if (onError != null)
                    onError.Invoke(error);
                else
                    Debug.LogError($"[ConvaiOperation] Unhandled operation error: {error}");
            }
            else if (_task.IsCanceled) onError?.Invoke(new ConvaiError("canceled", "Operation was canceled."));
        }

        #endregion

        #region Internal (for ConvaiOperationSource)

        /// <summary>
        ///     Sets the progress value. Called by <see cref="ConvaiOperationSource{T}" />.
        /// </summary>
        internal void SetProgress(float value) => _progress = Mathf.Clamp01(value);

        #endregion

        #region Status

        /// <inheritdoc />
        public OperationStatus Status => _task.Status switch
        {
            TaskStatus.Created => OperationStatus.Created,
            TaskStatus.WaitingForActivation => OperationStatus.Running,
            TaskStatus.WaitingToRun => OperationStatus.Running,
            TaskStatus.Running => OperationStatus.Running,
            TaskStatus.WaitingForChildrenToComplete => OperationStatus.Running,
            TaskStatus.RanToCompletion => OperationStatus.Succeeded,
            TaskStatus.Faulted => OperationStatus.Faulted,
            TaskStatus.Canceled => OperationStatus.Canceled,
            _ => OperationStatus.Running
        };

        /// <inheritdoc />
        public bool IsCompleted => _task.IsCompleted;

        /// <inheritdoc />
        public bool IsSuccessful => _task.IsCompletedSuccessfully;

        /// <inheritdoc />
        public bool IsCanceled => _task.IsCanceled;

        /// <inheritdoc />
        public bool HasError => _task.IsFaulted;

        /// <inheritdoc />
        public ConvaiError Error => _task.IsFaulted
            ? ConvaiError.FromException(_task.Exception?.GetBaseException())
            : default;

        /// <inheritdoc />
        public float Progress => _task.IsCompletedSuccessfully ? 1f : _progress;

        #endregion

        #region Awaiting

        /// <inheritdoc />
        public Task<T> AsTask() => _task;

        /// <inheritdoc />
        public TaskAwaiter<T> GetAwaiter() => _task.GetAwaiter();

        #endregion

        #region Chaining

        /// <inheritdoc />
        public IConvaiOperation<TNext> ContinueWith<TNext>(Func<T, TNext> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            return ConvaiOperation<TNext>.FromTask(ContinueWithCore(selector));
        }

        /// <inheritdoc />
        public IConvaiOperation<TNext> ContinueWith<TNext>(Func<T, Task<TNext>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            return ConvaiOperation<TNext>.FromTask(ContinueWithAsyncCore(selector));
        }

        private async Task<TNext> ContinueWithCore<TNext>(Func<T, TNext> selector)
        {
            T result = await _task.ConfigureAwait(false);
            return selector(result);
        }

        private async Task<TNext> ContinueWithAsyncCore<TNext>(Func<T, Task<TNext>> selector)
        {
            T result = await _task.ConfigureAwait(false);
            return await selector(result).ConfigureAwait(false);
        }

        #endregion

        #region Static Factories

        /// <summary>
        ///     Creates an operation that wraps an existing task.
        /// </summary>
        /// <param name="task">The task to wrap.</param>
        /// <returns>A new operation tracking the task.</returns>
        public static ConvaiOperation<T> FromTask(Task<T> task) => new(task);

        /// <summary>
        ///     Creates an operation that wraps a task with cancellation support.
        /// </summary>
        /// <param name="task">The task to wrap.</param>
        /// <param name="cts">The cancellation token source for cooperative cancellation.</param>
        /// <returns>A new operation with cancellation support.</returns>
        public static ConvaiOperation<T> FromTask(Task<T> task, CancellationTokenSource cts) => new(task, cts);

        /// <summary>
        ///     Creates an already-succeeded operation with the given result.
        /// </summary>
        /// <param name="result">The result value.</param>
        /// <returns>A completed operation in Succeeded state.</returns>
        public static ConvaiOperation<T> Succeeded(T result) => new(Task.FromResult(result));

        /// <summary>
        ///     Creates an already-failed operation with the given exception.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <returns>A completed operation in Faulted state.</returns>
        public static ConvaiOperation<T> Failed(Exception exception) => new(Task.FromException<T>(
            exception ?? throw new ArgumentNullException(nameof(exception))));

        /// <summary>
        ///     Creates an already-canceled operation.
        /// </summary>
        /// <returns>A completed operation in Canceled state.</returns>
        public static ConvaiOperation<T> Canceled() =>
            new(Task.FromCanceled<T>(new CancellationToken(true)));

        #endregion
    }
}
