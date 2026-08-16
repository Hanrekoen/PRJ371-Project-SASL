using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Default wrapper for an async stream plus completion/error status.
    /// </summary>
    public sealed class ConvaiStream<T> : IConvaiStream<T>
    {
        private readonly Func<ValueTask> _disposeAsync;
        private readonly Func<CancellationToken, IAsyncEnumerable<T>> _reader;

        public ConvaiStream(
            Func<CancellationToken, IAsyncEnumerable<T>> reader,
            StreamStatus initialStatus = StreamStatus.Created,
            ConvaiError error = default,
            Func<ValueTask> disposeAsync = null)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _disposeAsync = disposeAsync;
            Status = initialStatus;
            Error = error;
        }

        public StreamStatus Status { get; private set; }
        public ConvaiError Error { get; private set; }

        public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default)
        {
            Status = StreamStatus.Streaming;
            return ReadAllInternalAsync(ct);
        }

        public ValueTask DisposeAsync() => _disposeAsync?.Invoke() ?? default;

        private async IAsyncEnumerable<T> ReadAllInternalAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (T item in _reader(ct).WithCancellation(ct))
                yield return item;

            if (ct.IsCancellationRequested)
            {
                Status = StreamStatus.Canceled;
                Error = new ConvaiError("canceled", "The stream was canceled.");
            }
            else
                Status = StreamStatus.Completed;
        }
    }
}
