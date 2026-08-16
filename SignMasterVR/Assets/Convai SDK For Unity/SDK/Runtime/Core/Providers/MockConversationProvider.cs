using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using UnityEngine.Scripting;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Mock conversation provider for headless testing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Testing Only</b>: Provides stub implementations that don't require
    ///         network connectivity or the Convai backend.
    ///     </para>
    ///     <para>
    ///         <b>IL2CPP Safety</b>: Uses [Preserve] attribute to prevent stripping.
    ///     </para>
    /// </remarks>
    [Preserve]
    internal sealed class MockConversationProvider : IConversationProvider
    {
        private static readonly Lazy<MockConversationProvider> LazyInstance = new(() => new MockConversationProvider());

        private MockConversationProvider()
        {
        }

        /// <summary>
        ///     Gets the singleton instance of the mock conversation provider.
        /// </summary>
        public static MockConversationProvider Instance => LazyInstance.Value;

        /// <inheritdoc />
        public string ProviderId => "mock";

        /// <inheritdoc />
        public ConversationCapabilities Capabilities =>
            ConversationCapabilities.TextInput | ConversationCapabilities.StreamingResponse;

        /// <inheritdoc />
        public IConvaiOperation<IConversationSession> CreateSessionAsync(
            ConversationSessionRequest request,
            CancellationToken ct = default)
        {
            var session = new MockConversationSession(
                Guid.NewGuid().ToString("N"),
                request.CharacterId,
                Capabilities);

            return ConvaiOperation<IConversationSession>.Succeeded(session);
        }
    }

    /// <summary>
    ///     Mock conversation session for testing.
    /// </summary>
    [Preserve]
    internal sealed class MockConversationSession : IConversationSession
    {
        private readonly object _responseLock = new();
        private readonly Queue<ConversationResponsePart> _responses = new();
        private readonly SemaphoreSlim _responseSignal = new(0);
        private readonly CancellationTokenSource _streamLifetimeCts = new();
        private bool _disposed;

        public MockConversationSession(
            string sessionId,
            string characterId,
            ConversationCapabilities capabilities)
        {
            SessionId = sessionId;
            CharacterId = characterId;
            Capabilities = capabilities;
        }

        /// <inheritdoc />
        public string SessionId { get; }

        /// <inheritdoc />
        public string CharacterId { get; }

        /// <inheritdoc />
        public ConversationCapabilities Capabilities { get; }

        /// <inheritdoc />
        public event Action<ConversationEvent> EventReceived;

        /// <inheritdoc />
        public IConvaiOperation<Unit> SendAsync(ConversationRequest request, CancellationToken ct = default)
        {
            if (_disposed)
                return ConvaiOperation<Unit>.Failed(new ObjectDisposedException(nameof(MockConversationSession)));

            // Simulate a response
            string responseText = !string.IsNullOrEmpty(request.Text)
                ? $"[Mock] Received: {request.Text}"
                : "[Mock] Hello! This is a mock response.";

            EnqueueResponse(new ConversationResponsePart(responseText, null, true));

            return ConvaiOperation<Unit>.Succeeded(Unit.Value);
        }

        /// <inheritdoc />
        public IConvaiStream<ConversationResponsePart> OpenResponseStream(CancellationToken ct = default) =>
            new ConvaiStream<ConversationResponsePart>(
                readCt => ReadResponsesAsync(readCt),
                disposeAsync: DisposeStreamAsync);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _streamLifetimeCts.Cancel();
                _responseSignal.Release();
                EventReceived?.Invoke(new ConversationEvent("session_ended"));
            }

            return default;
        }

        private void EnqueueResponse(ConversationResponsePart response)
        {
            lock (_responseLock) _responses.Enqueue(response);
            _responseSignal.Release();
        }

        private async IAsyncEnumerable<ConversationResponsePart> ReadResponsesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _streamLifetimeCts.Token);

            while (!linkedCts.IsCancellationRequested)
            {
                try
                {
                    await _responseSignal.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                while (true)
                {
                    ConversationResponsePart next;
                    lock (_responseLock)
                    {
                        if (_responses.Count == 0) break;
                        next = _responses.Dequeue();
                    }

                    yield return next;
                }
            }
        }

        private ValueTask DisposeStreamAsync()
        {
            if (!_streamLifetimeCts.IsCancellationRequested)
                _streamLifetimeCts.Cancel();

            return default;
        }
    }
}
