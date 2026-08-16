namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Interface for event subscribers. Implement this to receive events from the event hub.
    /// </summary>
    /// <typeparam name="TEvent">Type of event to receive</typeparam>
    /// <remarks>
    ///     This is the strongly-typed interface for receiving events. Subscribers implement
    ///     this interface and register with the event hub. When an event of type TEvent is
    ///     published, the OnEvent method will be called.
    ///     Example:
    ///     <code>
    /// public class MySubscriber : IEventSubscriber&lt;SessionStateChanged&gt;
    /// {
    ///     public void OnEvent(SessionStateChanged @event)
    ///     {
    /// 
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public interface IEventSubscriber<in TEvent>
    {
        /// <summary>
        ///     Called when an event of type TEvent is published.
        /// </summary>
        /// <param name="event">The event instance that was published</param>
        public void OnEvent(TEvent @event);
    }
}
