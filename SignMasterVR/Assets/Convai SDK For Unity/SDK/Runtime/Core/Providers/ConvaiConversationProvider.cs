using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using UnityEngine;
using UnityEngine.Scripting;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Default conversation provider for Convai backend.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a placeholder implementation. The actual conversation flow is
    ///         currently handled by the room controller and RTVIHandler. This provider
    ///         will be fully implemented when the conversation layer is decoupled from
    ///         the transport layer.
    ///     </para>
    ///     <para>
    ///         <b>Current Usage</b>: Register as default provider, but conversation
    ///         still flows through ConvaiRoomManager/RTVIHandler.
    ///     </para>
    ///     <para>
    ///         <b>IL2CPP Safety</b>: Uses [Preserve] attribute to prevent stripping.
    ///     </para>
    /// </remarks>
    [Preserve]
    public sealed class ConvaiConversationProvider : IConversationProvider
    {
        private static readonly Lazy<ConvaiConversationProvider> LazyInstance =
            new(() => new ConvaiConversationProvider());

        private ConvaiConversationProvider()
        {
        }

        /// <summary>
        ///     Gets the singleton instance of the Convai conversation provider.
        /// </summary>
        public static ConvaiConversationProvider Instance => LazyInstance.Value;

        /// <inheritdoc />
        public string ProviderId => "convai";

        /// <inheritdoc />
        public ConversationCapabilities Capabilities => ConversationCapabilities.ConvaiDefault;

        /// <inheritdoc />
        public IConvaiOperation<IConversationSession> CreateSessionAsync(
            ConversationSessionRequest request,
            CancellationToken ct = default)
        {
            // Return a stub session. The actual conversation is still handled by RTVIHandler.
            var session = new ConvaiConversationSession(
                Guid.NewGuid().ToString("N"),
                request.CharacterId,
                request.PlayerId,
                Capabilities);

            return ConvaiOperation<IConversationSession>.Succeeded(session);
        }
    }

    /// <summary>
    ///     Convai conversation session placeholder.
    /// </summary>
    /// <remarks>
    ///     This is a placeholder. Actual conversation flows through RTVIHandler.
    ///     This session will be fully implemented when the conversation layer is
    ///     decoupled from transport.
    /// </remarks>
    [Preserve]
    internal sealed class ConvaiConversationSession : IConversationSession
    {
        private readonly object _responseLock = new();
        private readonly Queue<ConversationResponsePart> _responses = new();
        private readonly SemaphoreSlim _responseSignal = new(0);
        private readonly CancellationTokenSource _streamLifetimeCts = new();
        private bool _disposed;

        public ConvaiConversationSession(
            string sessionId,
            string characterId,
            string playerId,
            ConversationCapabilities capabilities)
        {
            SessionId = sessionId;
            CharacterId = characterId;
            PlayerId = playerId;
            Capabilities = capabilities;
        }

        /// <summary>
        ///     Local player ID for this session.
        /// </summary>
        public string PlayerId { get; }

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
                return ConvaiOperation<Unit>.Failed(new ObjectDisposedException(nameof(ConvaiConversationSession)));

            // No-op. Actual sending happens via RTVIHandler.
            Debug.LogWarning(
                "[ConvaiConversationSession] SendAsync called but conversation is handled by RTVIHandler. " +
                $"Character: {CharacterId}, Text: {request.Text?.Substring(0, Math.Min(50, request.Text?.Length ?? 0))}...");

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
