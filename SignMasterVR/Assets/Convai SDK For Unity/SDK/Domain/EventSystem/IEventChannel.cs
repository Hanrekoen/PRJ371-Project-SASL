using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     High-performance event channel for struct events.
    ///     Optimized for high-frequency events with zero boxing overhead.
    /// </summary>
    /// <typeparam name="TEvent">Event type (should be a struct for best performance).</typeparam>
    /// <remarks>
    ///     <para>
    ///         Unlike <see cref="IEventHub" /> which uses boxing for generic events,
    ///         <see cref="IEventChannel{TEvent}" /> is strongly typed and optimized for
    ///         high-frequency events like audio state changes or transcript chunks.
    ///     </para>
    ///     <para>
    ///         <b>Performance</b>: Uses <c>in TEvent</c> to avoid copying large structs
    ///         and copy-on-write subscription lists for thread safety.
    ///     </para>
    /// </remarks>
    public interface IEventChannel<TEvent> where TEvent : struct
    {
        /// <summary>
        ///     Number of active subscribers.
        /// </summary>
        public int SubscriberCount { get; }

        /// <summary>
        ///     Subscribes to events on this channel.
        /// </summary>
        /// <param name="handler">Handler to invoke when events are published.</param>
        /// <param name="priority">Handler priority (higher = called first). Default is 0.</param>
        /// <returns>Token for unsubscribing.</returns>
        public SubscriptionToken Subscribe(Action<TEvent> handler, int priority = 0);

        /// <summary>
        ///     Subscribes to events with ref parameter (avoids struct copying for large events).
        /// </summary>
        /// <param name="handler">Handler to invoke when events are published.</param>
        /// <param name="priority">Handler priority (higher = called first). Default is 0.</param>
        /// <returns>Token for unsubscribing.</returns>
        public SubscriptionToken SubscribeRef(EventRefHandler<TEvent> handler, int priority = 0);

        /// <summary>
        ///     Unsubscribes a handler using its subscription token.
        /// </summary>
        /// <param name="token">Token returned from Subscribe.</param>
        public void Unsubscribe(SubscriptionToken token);

        /// <summary>
        ///     Publishes an event to all subscribers.
        /// </summary>
        /// <param name="evt">The event to publish.</param>
        public void Publish(in TEvent evt);
    }

    /// <summary>
    ///     Delegate for event handlers that receive events by reference.
    /// </summary>
    /// <typeparam name="TEvent">Event type.</typeparam>
    /// <param name="evt">Event passed by reference to avoid copying.</param>
    public delegate void EventRefHandler<TEvent>(in TEvent evt) where TEvent : struct;
}
