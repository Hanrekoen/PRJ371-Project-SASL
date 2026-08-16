using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Internal data structure representing a single event subscription.
    ///     Used by EventHub to track subscribers and their delivery preferences.
    /// </summary>
    /// <remarks>
    ///     Tracks the subscriber, delivery policy, event type, and liveness state used by EventHub.
    /// </remarks>
    internal sealed class Subscription
    {
        /// <summary>
        ///     Creates a new subscription.
        /// </summary>
        /// <param name="token">Unique subscription token</param>
        /// <param name="subscriber">Subscriber object (will be wrapped in WeakReference)</param>
        /// <param name="policy">Delivery policy</param>
        /// <param name="eventType">Type of event being subscribed to</param>
        /// <exception cref="ArgumentNullException">Thrown if subscriber or eventType is null</exception>
        public Subscription(
            SubscriptionToken token,
            object subscriber,
            EventDeliveryPolicy policy,
            Type eventType)
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber));
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            Token = token;
            SubscriberRef = new WeakReference(subscriber);
            Policy = policy;
            EventType = eventType;
        }

        /// <summary>
        ///     Unique token identifying this subscription.
        /// </summary>
        public SubscriptionToken Token { get; }

        /// <summary>
        ///     Weak reference to the subscriber to prevent memory leaks.
        /// </summary>
        /// <remarks>
        ///     Using WeakReference allows subscribers to be garbage collected
        ///     even if they forget to unsubscribe. EventHub will automatically
        ///     clean up dead subscriptions during publish or periodic cleanup.
        /// </remarks>
        public WeakReference SubscriberRef { get; }

        /// <summary>
        ///     How events should be delivered to this subscriber.
        /// </summary>
        public EventDeliveryPolicy Policy { get; }

        /// <summary>
        ///     The type of event this subscription is for.
        /// </summary>
        /// <remarks>
        ///     Stored for diagnostics and metrics.
        ///     EventHub uses Dictionary&lt;Type, List&lt;Subscription&gt;&gt; for O(1) lookup.
        /// </remarks>
        public Type EventType { get; }

        /// <summary>
        ///     Checks if the subscriber is still alive (not garbage collected).
        /// </summary>
        public bool IsAlive => SubscriberRef.IsAlive;

        /// <summary>
        ///     Gets the subscriber target if still alive, otherwise null.
        /// </summary>
        public object Target => SubscriberRef.Target;

        /// <summary>
        ///     Attempts to get the subscriber as a specific type.
        /// </summary>
        /// <typeparam name="T">Expected subscriber type</typeparam>
        /// <param name="subscriber">Output subscriber if alive and correct type</param>
        /// <returns>True if subscriber is alive and correct type, false otherwise</returns>
        public bool TryGetSubscriber<T>(out T subscriber) where T : class
        {
            object target = Target;
            if (target is T typedSubscriber)
            {
                subscriber = typedSubscriber;
                return true;
            }

            subscriber = null;
            return false;
        }

        /// <summary>
        ///     Returns a string representation for debugging.
        /// </summary>
        public override string ToString() =>
            $"Subscription[Token={Token}, EventType={EventType.Name}, Policy={Policy}, IsAlive={IsAlive}]";

        /// <summary>
        ///     Checks equality based on token.
        /// </summary>
        public override bool Equals(object obj) => obj is Subscription other && Token.Equals(other.Token);

        /// <summary>
        ///     Gets hash code based on token.
        /// </summary>
        public override int GetHashCode() => Token.GetHashCode();
    }
}
