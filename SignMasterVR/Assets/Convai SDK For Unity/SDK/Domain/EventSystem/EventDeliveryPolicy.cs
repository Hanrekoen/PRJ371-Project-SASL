namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Defines how events should be delivered to subscribers.
    /// </summary>
    public enum EventDeliveryPolicy
    {
        /// <summary>
        ///     Event delivered on Unity main thread (safe for Unity API calls).
        ///     Use this for subscribers that need to interact with Unity objects.
        /// </summary>
        MainThread,

        /// <summary>
        ///     Event delivered on background thread (for CPU-intensive work).
        ///     Use this for subscribers that perform heavy computation without Unity API calls.
        /// </summary>
        Background,

        /// <summary>
        ///     Event delivered immediately on calling thread (lowest latency).
        ///     Use this for time-critical subscribers that can handle any thread.
        /// </summary>
        Immediate
    }
}
