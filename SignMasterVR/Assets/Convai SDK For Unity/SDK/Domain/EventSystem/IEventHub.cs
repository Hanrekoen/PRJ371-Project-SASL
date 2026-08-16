using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Central event bus for decoupled communication between services and components.
    /// </summary>
    /// <remarks>
    ///     The event hub provides a publish-subscribe mechanism for loosely-coupled communication.
    ///     Services and components can publish events without knowing who will receive them,
    ///     and subscribers can listen for events without knowing who published them.
    ///     Thread Safety:
    ///     - All methods are thread-safe and can be called from any thread
    ///     - EventDeliveryPolicy controls which thread the subscriber receives events on
    ///     Lifecycle:
    ///     - Subscribe returns a SubscriptionToken that must be kept to unsubscribe
    ///     - Always unsubscribe when done to prevent memory leaks
    ///     - Subscribers are held by weak reference to prevent memory leaks
    ///     Example Usage:
    ///     <code>
    /// 
    /// SubscriptionToken token = eventHub.Subscribe&lt;SessionStateChanged&gt;(mySubscriber, EventDeliveryPolicy.MainThread);
    /// 
    /// 
    /// SubscriptionToken token2 = eventHub.Subscribe&lt;SessionStateChanged&gt;(e => Debug.Log(e.NewState));
    /// 
    /// 
    /// eventHub.Publish(SessionStateChanged.Create(SessionState.Disconnected, SessionState.Connected, sessionId: null));
    /// 
    /// 
    /// eventHub.Unsubscribe(token);
    /// </code>
    /// </remarks>
    public interface IEventHub
    {
        /// <summary>
        ///     Subscribe to events of type TEvent with a subscriber callback.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to (must be non-null)</typeparam>
        /// <param name="subscriber">Subscriber that will receive events via OnEvent method</param>
        /// <param name="deliveryPolicy">How events should be delivered (main thread, background, immediate)</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        /// <exception cref="ArgumentNullException">Thrown if subscriber is null</exception>
        /// <remarks>
        ///     The subscriber will receive events via its OnEvent method according to the delivery policy.
        ///     <para>
        ///         <b>CRITICAL:</b> You must keep the returned <see cref="SubscriptionToken" /> and call
        ///         <see cref="Unsubscribe" /> when done.
        ///         Losing the token prevents cleanup and will lead to memory leaks, as the EventHub maintains a reference to the
        ///         adapter.
        ///     </para>
        /// </remarks>
        public SubscriptionToken Subscribe<TEvent>(
            IEventSubscriber<TEvent> subscriber,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread)
            where TEvent : notnull;

        /// <summary>
        ///     Subscribe to events with an action callback (convenience method).
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to (must be non-null)</typeparam>
        /// <param name="handler">Action to invoke when event is published</param>
        /// <param name="deliveryPolicy">How events should be delivered (main thread, background, immediate)</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        /// <exception cref="ArgumentNullException">Thrown if handler is null</exception>
        /// <remarks>
        ///     This is a convenience method that wraps the action in an IEventSubscriber adapter.
        ///     Prefer this for simple lambda subscriptions. For complex logic, implement IEventSubscriber.
        ///     <para>
        ///         <b>CRITICAL:</b> You must keep the returned <see cref="SubscriptionToken" /> and call
        ///         <see cref="Unsubscribe" /> when done.
        ///         Losing the token prevents cleanup and will lead to memory leaks, as the EventHub maintains a reference to the
        ///         adapter.
        ///     </para>
        /// </remarks>
        public SubscriptionToken Subscribe<TEvent>(
            Action<TEvent> handler,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread)
            where TEvent : notnull;

        /// <summary>
        ///     Subscribe to events with a filter that determines which events to receive.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to (must be non-null)</typeparam>
        /// <param name="subscriber">Subscriber that will receive filtered events</param>
        /// <param name="filter">Filter that determines which events to deliver</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        /// <remarks>
        ///     The filter is evaluated before event delivery. Events that don't pass the filter
        ///     are not delivered to the subscriber. This is more efficient than filtering in the
        ///     subscriber's OnEvent method as it avoids unnecessary scheduling overhead.
        /// </remarks>
        public SubscriptionToken Subscribe<TEvent>(
            IEventSubscriber<TEvent> subscriber,
            IEventFilter<TEvent> filter,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread)
            where TEvent : notnull;

        /// <summary>
        ///     Subscribe to events with an action callback and a filter.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to (must be non-null)</typeparam>
        /// <param name="handler">Action to invoke when a matching event is published</param>
        /// <param name="filter">Filter that determines which events to deliver</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        public SubscriptionToken Subscribe<TEvent>(
            Action<TEvent> handler,
            IEventFilter<TEvent> filter,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread)
            where TEvent : notnull;

        /// <summary>
        ///     Publish an event to all subscribers.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to publish (must be non-null)</typeparam>
        /// <param name="event">Event instance to publish</param>
        /// <exception cref="ArgumentNullException">Thrown if event is null</exception>
        /// <remarks>
        ///     All subscribers of TEvent will receive this event according to their delivery policy.
        ///     This method returns immediately; event delivery happens asynchronously based on policy.
        ///     Delivery guarantees:
        ///     - MainThread: Delivered on next Unity Update (or immediately if already on main thread)
        ///     - Background: Delivered on thread pool thread
        ///     - Immediate: Delivered synchronously on calling thread
        /// </remarks>
        public void Publish<TEvent>(TEvent @event) where TEvent : notnull;

        /// <summary>
        ///     Unsubscribe from events using the subscription token.
        /// </summary>
        /// <param name="token">Token returned from Subscribe</param>
        /// <remarks>
        ///     After unsubscribing, the subscriber will no longer receive events.
        ///     It is safe to call this multiple times with the same token.
        ///     It is safe to call this with an invalid or already-unsubscribed token (no-op).
        /// </remarks>
        public void Unsubscribe(SubscriptionToken token);

        /// <summary>
        ///     Attempts periodic cleanup of dead subscriptions if the cleanup interval has elapsed.
        /// </summary>
        /// <param name="currentTimeSeconds">Current time in seconds (e.g., Time.realtimeSinceStartup or DateTime-based)</param>
        /// <param name="cleanupIntervalSeconds">Minimum interval between cleanups in seconds (default: 60)</param>
        /// <returns>True if cleanup was performed, false if interval has not elapsed</returns>
        /// <remarks>
        ///     This method is intended to be called periodically (e.g., from Update) to clean up
        ///     dead WeakReference subscriptions. It uses a simple time-based throttle to avoid
        ///     performing cleanup too frequently.
        ///     Performance:
        ///     - When interval has not elapsed: O(1) with no allocations
        ///     - When cleanup is performed: O(n) where n = total subscriptions
        ///     Example usage:
        ///     <code>
        /// void Update()
        /// {
        ///     eventHub.TryPeriodicCleanup(Time.realtimeSinceStartup, cleanupIntervalSeconds: 60f);
        /// }
        /// </code>
        /// </remarks>
        public bool TryPeriodicCleanup(float currentTimeSeconds, float cleanupIntervalSeconds = 60f);
    }
}
