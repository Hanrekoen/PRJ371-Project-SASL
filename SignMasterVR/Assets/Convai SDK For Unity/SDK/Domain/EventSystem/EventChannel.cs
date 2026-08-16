using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     High-performance event channel implementation for struct events.
    ///     Uses copy-on-write for thread-safe subscription management.
    /// </summary>
    /// <typeparam name="TEvent">Event type (should be a struct for best performance).</typeparam>
    /// <remarks>
    ///     <para>
    ///         <b>Performance Optimizations</b>:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Copy-on-write subscription list avoids locks during publish</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>in TEvent</c> parameter avoids copying large structs</description>
    ///             </item>
    ///             <item>
    ///                 <description>Priority ordering for handler execution</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: Subscription changes are thread-safe. Publishing
    ///         iterates a snapshot, so new subscriptions during publish won't receive the event.
    ///     </para>
    /// </remarks>
    public sealed class EventChannel<TEvent> : IEventChannel<TEvent> where TEvent : struct
    {
        private readonly object _lock = new();
        private readonly List<Subscription> _subscriptions = new();
        private Subscription[] _snapshot = Array.Empty<Subscription>();

        /// <inheritdoc />
        public int SubscriberCount => _snapshot.Length;

        /// <inheritdoc />
        public SubscriptionToken Subscribe(Action<TEvent> handler, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var token = SubscriptionToken.Create();
            var subscription = new Subscription(
                token,
                handler,
                null,
                priority);

            AddSubscription(subscription);
            return token;
        }

        /// <inheritdoc />
        public SubscriptionToken SubscribeRef(EventRefHandler<TEvent> handler, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var token = SubscriptionToken.Create();
            var subscription = new Subscription(
                token,
                null,
                handler,
                priority);

            AddSubscription(subscription);
            return token;
        }

        /// <inheritdoc />
        public void Unsubscribe(SubscriptionToken token)
        {
            lock (_lock)
            {
                int index = _subscriptions.FindIndex(s => s.Token == token);
                if (index < 0)
                    return;

                _subscriptions.RemoveAt(index);
                RebuildSnapshot();
            }
        }

        /// <inheritdoc />
        public void Publish(in TEvent evt)
        {
            // Take snapshot reference (atomic read)
            Subscription[] snapshot = _snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ref Subscription sub = ref snapshot[i];
                try
                {
                    if (sub.Handler != null)
                        sub.Handler(evt);
                    else
                        sub.RefHandler?.Invoke(in evt);
                }
                catch (Exception ex)
                {
                    // Log exception but continue delivery to other handlers
                    Trace.TraceError(
                        "EventChannel<{0}> handler threw an exception: {1}",
                        typeof(TEvent).FullName,
                        ex);
                }
            }
        }

        private void AddSubscription(Subscription subscription)
        {
            lock (_lock)
            {
                // Insert in priority order (highest first)
                int insertIndex = _subscriptions.FindIndex(s => s.Priority < subscription.Priority);
                if (insertIndex < 0)
                    _subscriptions.Add(subscription);
                else
                    _subscriptions.Insert(insertIndex, subscription);

                RebuildSnapshot();
            }
        }

        private void RebuildSnapshot() => _snapshot = _subscriptions.ToArray();

        private struct Subscription
        {
            public readonly SubscriptionToken Token;
            public readonly Action<TEvent> Handler;
            public readonly EventRefHandler<TEvent> RefHandler;
            public readonly int Priority;

            public Subscription(SubscriptionToken token, Action<TEvent> handler, EventRefHandler<TEvent> refHandler,
                int priority)
            {
                Token = token;
                Handler = handler;
                RefHandler = refHandler;
                Priority = priority;
            }
        }
    }
}
