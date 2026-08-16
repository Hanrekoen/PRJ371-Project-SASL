using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Wraps an event subscriber with a filter that determines which events to deliver.
    /// </summary>
    /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
    /// <remarks>
    ///     Used internally to apply a filter before forwarding an event to the wrapped subscriber.
    /// </remarks>
    internal sealed class FilteredEventSubscriber<TEvent> : IEventSubscriber<TEvent> where TEvent : notnull
    {
        private readonly IEventFilter<TEvent> _filter;
        private readonly IEventSubscriber<TEvent> _innerSubscriber;

        /// <summary>
        ///     Creates a new filtered subscriber.
        /// </summary>
        /// <param name="innerSubscriber">The subscriber to wrap</param>
        /// <param name="filter">The filter to apply before delivery</param>
        public FilteredEventSubscriber(IEventSubscriber<TEvent> innerSubscriber, IEventFilter<TEvent> filter)
        {
            _innerSubscriber = innerSubscriber ?? throw new ArgumentNullException(nameof(innerSubscriber));
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        /// <summary>
        ///     Evaluates the filter and delivers the event if it passes.
        /// </summary>
        public void OnEvent(TEvent @event)
        {
            if (_filter.ShouldDeliver(@event)) _innerSubscriber.OnEvent(@event);
        }
    }

    /// <summary>
    ///     Wraps an action handler with a filter that determines which events to deliver.
    /// </summary>
    /// <typeparam name="TEvent">Type of event to subscribe to</typeparam>
    internal sealed class FilteredActionSubscriber<TEvent> : IEventSubscriber<TEvent> where TEvent : notnull
    {
        private readonly IEventFilter<TEvent> _filter;
        private readonly Action<TEvent> _handler;

        /// <summary>
        ///     Creates a new filtered action subscriber.
        /// </summary>
        /// <param name="handler">The action handler to wrap</param>
        /// <param name="filter">The filter to apply before delivery</param>
        public FilteredActionSubscriber(Action<TEvent> handler, IEventFilter<TEvent> filter)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        /// <summary>
        ///     Evaluates the filter and invokes the handler if the event passes.
        /// </summary>
        public void OnEvent(TEvent @event)
        {
            if (_filter.ShouldDeliver(@event)) _handler(@event);
        }
    }
}
