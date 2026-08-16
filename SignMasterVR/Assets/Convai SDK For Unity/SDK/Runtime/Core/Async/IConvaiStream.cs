using System;
using System.Collections.Generic;
using System.Threading;

namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Structured SDK stream abstraction for long-lived async data flows.
    /// </summary>
    public interface IConvaiStream<T> : IAsyncDisposable
    {
        public StreamStatus Status { get; }
        public ConvaiError Error { get; }

        public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default);
    }
}
