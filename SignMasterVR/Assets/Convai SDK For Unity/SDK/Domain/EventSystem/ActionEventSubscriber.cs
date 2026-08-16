using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Adapter that wraps an Action delegate as an IEventSubscriber.
    ///     Used by the Subscribe(Action) convenience overload.
    /// </summary>
    /// <typeparam name="TEvent">Type of event this subscriber handles</typeparam>
    /// <remarks>
    ///     Used internally so action-based subscriptions can participate in normal event delivery.
    /// </remarks>
    internal sealed class ActionEventSubscriber<TEvent> : IEventSubscriber<TEvent>
    {
        private readonly Action<TEvent> _handler;

        /// <param name="handler">Action to invoke when event is received</param>
        /// <exception cref="ArgumentNullException">Thrown if handler is null</exception>
        public ActionEventSubscriber(Action<TEvent> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        ///     Called when an event is published. Invokes the wrapped action.
        /// </summary>
        /// <param name="event">Event instance to pass to the action</param>
        public void OnEvent(TEvent @event) => _handler(@event);
    }
}
