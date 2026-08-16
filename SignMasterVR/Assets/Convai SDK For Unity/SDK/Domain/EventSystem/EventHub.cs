using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Domain.Logging;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Low-allocation implementation of IEventHub for event-driven communication.
    ///     Uses copy-on-write pattern to keep Publish() allocation-light and dictionary for O(1) event type lookup.
    /// </summary>
    /// <remarks>
    ///     Provides publish-subscribe event delivery with thread-safe subscription management,
    ///     weak-reference cleanup, and multiple delivery policies.
    ///     Usage:
    ///     <code>
    /// IUnityScheduler scheduler = UnityScheduler.Instance;
    /// ConvaiLogger logger = new ConvaiLogger();
    /// EventHub eventHub = new EventHub(scheduler, logger);
    /// 
    /// 
    /// SubscriptionToken token = eventHub.Subscribe&lt;MyEvent&gt;(mySubscriber, EventDeliveryPolicy.MainThread);
    /// 
    /// 
    /// eventHub.Publish(new MyEvent());
    /// 
    /// 
    /// eventHub.Unsubscribe(token);
    /// </code>
    /// </remarks>
    public class EventHub : IEventHub
    {
        /// <summary>
        ///     Strong references to ActionEventSubscriber wrappers to prevent garbage collection.
        ///     When using Subscribe(Action), the wrapper has no external strong reference, so we must keep it alive.
        ///     Cleaned up on Unsubscribe().
        /// </summary>
        private readonly Dictionary<SubscriptionToken, object> _actionSubscriberRefs = new();

        private readonly ILogger _logger;
        private readonly IUnityScheduler _scheduler;

        /// <summary>
        ///     Maps subscription tokens to their event types for O(1) unsubscribe lookup.
        ///     Modified only under _writeLock.
        /// </summary>
        private readonly Dictionary<SubscriptionToken, Type> _tokenToType = new();

        /// <summary>
        ///     Lock for write operations (Subscribe, Unsubscribe, Cleanup).
        ///     Not used during Publish() for allocation-free reads.
        /// </summary>
        private readonly object _writeLock = new();

        private bool _hasPerformedPeriodicCleanup;

        /// <summary>
        ///     Tracks the last time periodic cleanup was performed.
        ///     Used by TryPeriodicCleanup to throttle cleanup frequency.
        /// </summary>
        private float _lastPeriodicCleanupTime;

        /// <summary>
        ///     Copy-on-write subscription dictionary. Volatile ensures visibility across threads.
        ///     Key = event type, Value = array of subscriptions for that type.
        ///     Only modified under _writeLock, but read without locking in Publish().
        /// </summary>
        private volatile Dictionary<Type, Subscription[]> _subscriptionsByType = new();

        /// <summary>
        ///     Creates a new EventHub instance.
        /// </summary>
        /// <param name="scheduler">Scheduler for thread marshaling</param>
        /// <param name="logger">Logger for diagnostics (optional)</param>
        /// <exception cref="ArgumentNullException">Thrown if scheduler is null</exception>
        public EventHub(
            IUnityScheduler scheduler,
            ILogger logger = null)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _logger = logger.WithTag(nameof(EventHub));
            _lastPeriodicCleanupTime = 0f;
            _hasPerformedPeriodicCleanup = false;

            _logger?.Debug("Initialized (low-allocation mode)", LogCategory.Events);
        }

        /// <summary>
        ///     Subscribes to events of type TEvent with a subscriber callback.
        ///     Uses copy-on-write: creates a new dictionary with updated type-specific array.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
        /// <param name="subscriber">Subscriber that will receive events</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        /// <exception cref="ArgumentNullException">Thrown if subscriber is null</exception>
        public SubscriptionToken Subscribe<TEvent>(
            IEventSubscriber<TEvent> subscriber,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber));

            Type eventType = typeof(TEvent);
            var token = SubscriptionToken.Create();
            var subscription = new Subscription(token, subscriber, deliveryPolicy, eventType);

            lock (_writeLock)
            {
                Dictionary<Type, Subscription[]> oldDict = _subscriptionsByType;
                var newDict = new Dictionary<Type, Subscription[]>(oldDict);

                if (newDict.TryGetValue(eventType, out Subscription[] existing))
                {
                    var newArray = new Subscription[existing.Length + 1];
                    Array.Copy(existing, newArray, existing.Length);
                    newArray[existing.Length] = subscription;
                    newDict[eventType] = newArray;
                }
                else
                    newDict[eventType] = new[] { subscription };

                _tokenToType[token] = eventType;

                _subscriptionsByType = newDict;
            }

            return token;
        }

        /// <summary>
        ///     Subscribes to events of type TEvent with an action callback (convenience method).
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
        /// <param name="handler">Action that will receive events</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        /// <exception cref="ArgumentNullException">Thrown if handler is null</exception>
        /// <remarks>
        ///     Unlike IEventSubscriber-based subscriptions, Action-based subscriptions require
        ///     the EventHub to maintain a strong reference to the internal wrapper object.
        ///     This prevents premature garbage collection since the caller has no way to hold
        ///     a reference to the ActionEventSubscriber wrapper. The strong reference is
        ///     automatically cleaned up when Unsubscribe() is called.
        /// </remarks>
        public SubscriptionToken Subscribe<TEvent>(
            Action<TEvent> handler,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var subscriber = new ActionEventSubscriber<TEvent>(handler);
            SubscriptionToken token = Subscribe(subscriber, deliveryPolicy);

            lock (_writeLock) _actionSubscriberRefs[token] = subscriber;

            return token;
        }

        /// <summary>
        ///     Subscribes to events with a filter that determines which events to receive.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
        /// <param name="subscriber">Subscriber that will receive filtered events</param>
        /// <param name="filter">Filter that determines which events to deliver</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        public SubscriptionToken Subscribe<TEvent>(
            IEventSubscriber<TEvent> subscriber,
            IEventFilter<TEvent> filter,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber));
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            var filteredSubscriber = new FilteredEventSubscriber<TEvent>(subscriber, filter);
            SubscriptionToken token = Subscribe(filteredSubscriber, deliveryPolicy);

            lock (_writeLock) _actionSubscriberRefs[token] = filteredSubscriber;

            return token;
        }

        /// <summary>
        ///     Subscribes to events with an action callback and a filter.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
        /// <param name="handler">Action to invoke when a matching event is published</param>
        /// <param name="filter">Filter that determines which events to deliver</param>
        /// <param name="deliveryPolicy">How events should be delivered</param>
        /// <returns>Subscription token for unsubscribing later</returns>
        public SubscriptionToken Subscribe<TEvent>(
            Action<TEvent> handler,
            IEventFilter<TEvent> filter,
            EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            var filteredSubscriber = new FilteredActionSubscriber<TEvent>(handler, filter);
            SubscriptionToken token = Subscribe(filteredSubscriber, deliveryPolicy);

            lock (_writeLock) _actionSubscriberRefs[token] = filteredSubscriber;

            return token;
        }

        /// <summary>
        ///     Unsubscribes using a subscription token.
        ///     Uses O(1) token lookup and copy-on-write for type-specific array.
        /// </summary>
        /// <param name="token">Token returned from Subscribe</param>
        public void Unsubscribe(SubscriptionToken token)
        {
            lock (_writeLock)
            {
                if (!_tokenToType.TryGetValue(token, out Type eventType))
                {
                    _logger?.Warning($"Unsubscribe called with unknown token: {token}", LogCategory.Events);
                    return;
                }

                Dictionary<Type, Subscription[]> oldDict = _subscriptionsByType;

                if (!oldDict.TryGetValue(eventType, out Subscription[] oldArray))
                {
                    _logger?.Warning($"No subscriptions found for type {eventType.Name}",
                        LogCategory.Events);
                    _tokenToType.Remove(token);
                    return;
                }

                int indexToRemove = -1;
                for (int i = 0; i < oldArray.Length; i++)
                {
                    if (oldArray[i].Token.Equals(token))
                    {
                        indexToRemove = i;
                        break;
                    }
                }

                if (indexToRemove < 0)
                {
                    _logger?.Warning($"Token not found in {eventType.Name} subscriptions",
                        LogCategory.Events);
                    _tokenToType.Remove(token);
                    return;
                }

                var newDict = new Dictionary<Type, Subscription[]>(oldDict);

                if (oldArray.Length == 1)
                    newDict.Remove(eventType);
                else
                {
                    var newArray = new Subscription[oldArray.Length - 1];
                    if (indexToRemove > 0) Array.Copy(oldArray, 0, newArray, 0, indexToRemove);
                    if (indexToRemove < oldArray.Length - 1)
                    {
                        Array.Copy(oldArray, indexToRemove + 1, newArray, indexToRemove,
                            oldArray.Length - indexToRemove - 1);
                    }

                    newDict[eventType] = newArray;
                }

                _tokenToType.Remove(token);
                _actionSubscriberRefs.Remove(token);
                _subscriptionsByType = newDict;

                _logger?.Debug(
                    $"Unsubscribed from {eventType.Name} (Remaining for type: {(newDict.TryGetValue(eventType, out Subscription[] remaining) ? remaining.Length : 0)})",
                    LogCategory.Events);
            }
        }

        /// <summary>
        ///     Publishes an event to all subscribers.
        ///     Allocation-free type lookup: uses volatile dictionary read with O(1) type lookup.
        /// </summary>
        /// <typeparam name="TEvent">Type of event to publish</typeparam>
        /// <param name="event">Event instance to publish</param>
        /// <remarks>
        ///     Thread-safe: Can be called from any thread.
        ///     Allocation-free lookup/iteration: no heap allocations during publish unless
        ///     delivery is scheduled or dead subscriptions require cleanup.
        ///     O(1) type lookup: Dictionary provides constant-time event type resolution.
        ///     Delivery:
        ///     - MainThread: Queued to Unity main thread via scheduler
        ///     - Background: Executed on ThreadPool via scheduler
        ///     - Immediate: Executed synchronously on calling thread
        ///     Exception Handling:
        ///     - Exceptions in subscribers are caught and logged
        ///     - One subscriber's exception doesn't affect others
        ///     - Publishing continues even if a subscriber throws
        /// </remarks>
        public void Publish<TEvent>(TEvent @event) where TEvent : notnull
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            Dictionary<Type, Subscription[]> subscriptionsByType = _subscriptionsByType;
            Type eventType = typeof(TEvent);

            if (!subscriptionsByType.TryGetValue(eventType, out Subscription[] subscriptions)) return;

            bool hasDeadSubscribers = false;
            for (int i = 0; i < subscriptions.Length; i++)
            {
                Subscription subscription = subscriptions[i];

                if (!subscription.IsAlive)
                {
                    hasDeadSubscribers = true;
                    continue;
                }

                if (!subscription.TryGetSubscriber(out IEventSubscriber<TEvent> subscriber)) continue;

                DeliverEvent(subscriber, @event, subscription.Policy);
            }

            if (hasDeadSubscribers) CleanupAllDeadSubscriptions();
        }

        /// <summary>
        ///     Delivers an event to a subscriber based on delivery policy.
        /// </summary>
        private void DeliverEvent<TEvent>(IEventSubscriber<TEvent> subscriber, TEvent @event,
            EventDeliveryPolicy policy) where TEvent : notnull
        {
            switch (policy)
            {
                case EventDeliveryPolicy.MainThread:
                    _scheduler.ScheduleOnMainThread(() => InvokeSubscriber(subscriber, @event));
                    break;

                case EventDeliveryPolicy.Background:
                    _scheduler.ScheduleOnBackground(() => InvokeSubscriber(subscriber, @event));
                    break;

                case EventDeliveryPolicy.Immediate:
                    InvokeSubscriber(subscriber, @event);
                    break;

                default:
                    _logger?.Warning($"Unknown delivery policy: {policy}", LogCategory.Events);
                    break;
            }
        }

        /// <summary>
        ///     Invokes a subscriber with exception handling.
        /// </summary>
        private void InvokeSubscriber<TEvent>(IEventSubscriber<TEvent> subscriber, TEvent @event) where TEvent : notnull
        {
            try
            {
                subscriber.OnEvent(@event);
            }
            catch (Exception ex)
            {
                string subscriberDescription = DescribeSubscriber(subscriber);
                _logger?.Error(
                    $"Exception in subscriber '{subscriberDescription}' for {typeof(TEvent).Name}: {ex.Message}",
                    LogCategory.Events);

                if (!_scheduler.IsMainThread())
                {
                    _scheduler.ScheduleOnMainThread(() =>
                    {
                        _logger?.Error(
                            $"Background thread exception details for subscriber '{subscriberDescription}': {ex}",
                            LogCategory.Events);
                    });
                }
            }
        }

        #region Helper Methods

        private static string DescribeSubscriber(object subscriber)
        {
            if (subscriber == null) return "<null>";

            if (TryDescribeDelegateSubscriber(subscriber, out string delegateDescription))
                return delegateDescription;

            if (TryDescribeInnerSubscriber(subscriber, out string innerSubscriberDescription))
                return innerSubscriberDescription;

            return subscriber.GetType().FullName ?? subscriber.GetType().Name;
        }

        private static bool TryDescribeDelegateSubscriber(object subscriber, out string description)
        {
            FieldInfo handlerField = subscriber.GetType().GetField("_handler",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (handlerField?.GetValue(subscriber) is not Delegate handler)
            {
                description = null;
                return false;
            }

            string declaringType = handler.Target?.GetType().FullName ??
                                   handler.Method.DeclaringType?.FullName ??
                                   "<static>";
            description = $"{declaringType}.{handler.Method.Name}";
            return true;
        }

        private static bool TryDescribeInnerSubscriber(object subscriber, out string description)
        {
            FieldInfo innerSubscriberField = subscriber.GetType().GetField("_innerSubscriber",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (innerSubscriberField?.GetValue(subscriber) == null)
            {
                description = null;
                return false;
            }

            description = DescribeSubscriber(innerSubscriberField.GetValue(subscriber));
            return true;
        }

        /// <summary>
        ///     Cleans up all dead subscriptions (garbage collected subscribers).
        ///     Uses copy-on-write: creates new dictionary with cleaned arrays for each type.
        /// </summary>
        public void CleanupAllDeadSubscriptions()
        {
            lock (_writeLock)
            {
                Dictionary<Type, Subscription[]> oldDict = _subscriptionsByType;
                var newDict = new Dictionary<Type, Subscription[]>();
                int totalDeadCount = 0;
                var tokensToRemove = new List<SubscriptionToken>();

                foreach (KeyValuePair<Type, Subscription[]> kvp in oldDict)
                {
                    Type eventType = kvp.Key;
                    Subscription[] oldArray = kvp.Value;

                    int aliveCount = 0;
                    for (int i = 0; i < oldArray.Length; i++)
                    {
                        if (oldArray[i].IsAlive)
                            aliveCount++;
                    }

                    int deadCount = oldArray.Length - aliveCount;
                    totalDeadCount += deadCount;

                    for (int i = 0; i < oldArray.Length; i++)
                    {
                        if (!oldArray[i].IsAlive)
                            tokensToRemove.Add(oldArray[i].Token);
                    }

                    if (aliveCount == oldArray.Length)
                        newDict[eventType] = oldArray;
                    else if (aliveCount > 0)
                    {
                        var newArray = new Subscription[aliveCount];
                        int newIndex = 0;
                        for (int i = 0; i < oldArray.Length; i++)
                        {
                            if (oldArray[i].IsAlive)
                                newArray[newIndex++] = oldArray[i];
                        }

                        newDict[eventType] = newArray;
                    }
                }

                foreach (SubscriptionToken token in tokensToRemove)
                {
                    _tokenToType.Remove(token);
                    _actionSubscriberRefs.Remove(token);
                }

                if (totalDeadCount > 0)
                {
                    _subscriptionsByType = newDict;
                    _logger?.Debug($"Cleaned up {totalDeadCount} dead subscriptions", LogCategory.Events);
                }
            }
        }

        /// <summary>
        ///     Attempts periodic cleanup of dead subscriptions if the cleanup interval has elapsed.
        /// </summary>
        /// <param name="currentTimeSeconds">Current time in seconds (e.g., Time.realtimeSinceStartup)</param>
        /// <param name="cleanupIntervalSeconds">Minimum interval between cleanups in seconds (default: 60)</param>
        /// <returns>True if cleanup was performed, false if interval has not elapsed</returns>
        /// <remarks>
        ///     This method is designed to be called frequently (e.g., from Update) with minimal overhead.
        ///     When the interval has not elapsed, this method performs a single float comparison with no allocations.
        ///     When cleanup is triggered, it delegates to CleanupAllDeadSubscriptions.
        /// </remarks>
        public bool TryPeriodicCleanup(float currentTimeSeconds, float cleanupIntervalSeconds = 60f)
        {
            if (!_hasPerformedPeriodicCleanup)
            {
                _hasPerformedPeriodicCleanup = true;
                _lastPeriodicCleanupTime = currentTimeSeconds;
                CleanupAllDeadSubscriptions();

                _logger?.Debug($"Periodic cleanup triggered at t={currentTimeSeconds:F1}s",
                    LogCategory.Events);
                return true;
            }

            if (currentTimeSeconds - _lastPeriodicCleanupTime < cleanupIntervalSeconds) return false;

            _lastPeriodicCleanupTime = currentTimeSeconds;
            CleanupAllDeadSubscriptions();

            _logger?.Debug($"Periodic cleanup triggered at t={currentTimeSeconds:F1}s", LogCategory.Events);
            return true;
        }

        /// <summary>
        ///     Gets the last time periodic cleanup was performed (for diagnostics/testing).
        /// </summary>
        public float LastPeriodicCleanupTime => _lastPeriodicCleanupTime;

        /// <summary>
        ///     Resets the periodic cleanup timer (for testing purposes).
        /// </summary>
        public void ResetPeriodicCleanupTimer()
        {
            _lastPeriodicCleanupTime = 0f;
            _hasPerformedPeriodicCleanup = false;
        }

        /// <summary>
        ///     Gets the total number of subscriptions (including potentially dead ones).
        ///     Note: O(m) where m = number of event types.
        /// </summary>
        public int GetTotalSubscriptionCount()
        {
            Dictionary<Type, Subscription[]> subscriptionsByType = _subscriptionsByType;
            int total = 0;
            foreach (KeyValuePair<Type, Subscription[]> kvp in subscriptionsByType) total += kvp.Value.Length;
            return total;
        }

        /// <summary>
        ///     Gets the number of subscriptions for a specific event type.
        ///     O(1) lookup via dictionary.
        /// </summary>
        public int GetSubscriptionCount<TEvent>() where TEvent : notnull
        {
            Dictionary<Type, Subscription[]> subscriptionsByType = _subscriptionsByType;
            Type eventType = typeof(TEvent);

            if (subscriptionsByType.TryGetValue(eventType, out Subscription[] subscriptions))
                return subscriptions.Length;

            return 0;
        }

        /// <summary>
        ///     Gets all event types that have active subscriptions.
        ///     O(1) via dictionary keys (returns new list for safety).
        /// </summary>
        public IReadOnlyList<Type> GetSubscribedEventTypes()
        {
            Dictionary<Type, Subscription[]> subscriptionsByType = _subscriptionsByType;
            return new List<Type>(subscriptionsByType.Keys);
        }

        #endregion
    }
}
