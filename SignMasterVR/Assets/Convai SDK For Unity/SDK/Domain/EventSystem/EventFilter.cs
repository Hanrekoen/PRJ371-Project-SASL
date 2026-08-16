using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Provides factory methods for creating event filters.
    /// </summary>
    /// <remarks>
    ///     Event filters allow subscribers to receive only events that match specific criteria.
    ///     Filters are evaluated before event delivery, reducing unnecessary processing.
    ///     Usage:
    ///     <code>
    /// var filter = EventFilter.Where&lt;MyEvent&gt;(e => e.Priority > 5);
    /// eventHub.Subscribe&lt;MyEvent&gt;(handler, filter);
    /// 
    /// 
    /// var combined = EventFilter.Where&lt;MyEvent&gt;(e => e.Priority > 5).And(e => e.IsImportant);
    /// </code>
    /// </remarks>
    public static class EventFilter
    {
        /// <summary>
        ///     Creates a filter that accepts all events (no filtering).
        /// </summary>
        public static IEventFilter<TEvent> AcceptAll<TEvent>() where TEvent : notnull
            => new AcceptAllFilter<TEvent>();

        /// <summary>
        ///     Creates a filter based on a custom predicate.
        /// </summary>
        /// <param name="predicate">Predicate that returns true for events to accept</param>
        public static IEventFilter<TEvent> Where<TEvent>(Func<TEvent, bool> predicate) where TEvent : notnull
            => new PredicateFilter<TEvent>(predicate);
    }

    /// <summary>
    ///     Interface for event filters that determine whether an event should be delivered.
    /// </summary>
    public interface IEventFilter<in TEvent> where TEvent : notnull
    {
        /// <summary>
        ///     Determines whether the specified event should be delivered to the subscriber.
        /// </summary>
        /// <param name="event">The event to evaluate</param>
        /// <returns>True if the event should be delivered; false to skip delivery</returns>
        public bool ShouldDeliver(TEvent @event);
    }

    /// <summary>
    ///     Extension methods for combining event filters.
    /// </summary>
    internal static class EventFilterExtensions
    {
        /// <summary>
        ///     Combines this filter with another using logical AND.
        /// </summary>
        public static IEventFilter<TEvent> And<TEvent>(
            this IEventFilter<TEvent> first,
            IEventFilter<TEvent> second) where TEvent : notnull
            => new AndFilter<TEvent>(first, second);

        /// <summary>
        ///     Combines this filter with a predicate using logical AND.
        /// </summary>
        public static IEventFilter<TEvent> And<TEvent>(
            this IEventFilter<TEvent> first,
            Func<TEvent, bool> predicate) where TEvent : notnull
            => new AndFilter<TEvent>(first, new PredicateFilter<TEvent>(predicate));

        /// <summary>
        ///     Combines this filter with another using logical OR.
        /// </summary>
        public static IEventFilter<TEvent> Or<TEvent>(
            this IEventFilter<TEvent> first,
            IEventFilter<TEvent> second) where TEvent : notnull
            => new OrFilter<TEvent>(first, second);
    }

    #region Filter Implementations

    internal sealed class AcceptAllFilter<TEvent> : IEventFilter<TEvent> where TEvent : notnull
    {
        public bool ShouldDeliver(TEvent @event) => true;
    }

    internal sealed class PredicateFilter<TEvent> : IEventFilter<TEvent> where TEvent : notnull
    {
        private readonly Func<TEvent, bool> _predicate;

        public PredicateFilter(Func<TEvent, bool> predicate)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public bool ShouldDeliver(TEvent @event) => _predicate(@event);
    }

    internal sealed class AndFilter<TEvent> : IEventFilter<TEvent> where TEvent : notnull
    {
        private readonly IEventFilter<TEvent> _first;
        private readonly IEventFilter<TEvent> _second;

        public AndFilter(IEventFilter<TEvent> first, IEventFilter<TEvent> second)
        {
            _first = first ?? throw new ArgumentNullException(nameof(first));
            _second = second ?? throw new ArgumentNullException(nameof(second));
        }

        public bool ShouldDeliver(TEvent @event) => _first.ShouldDeliver(@event) && _second.ShouldDeliver(@event);
    }

    internal sealed class OrFilter<TEvent> : IEventFilter<TEvent> where TEvent : notnull
    {
        private readonly IEventFilter<TEvent> _first;
        private readonly IEventFilter<TEvent> _second;

        public OrFilter(IEventFilter<TEvent> first, IEventFilter<TEvent> second)
        {
            _first = first ?? throw new ArgumentNullException(nameof(first));
            _second = second ?? throw new ArgumentNullException(nameof(second));
        }

        public bool ShouldDeliver(TEvent @event) => _first.ShouldDeliver(@event) || _second.ShouldDeliver(@event);
    }

    #endregion
}
