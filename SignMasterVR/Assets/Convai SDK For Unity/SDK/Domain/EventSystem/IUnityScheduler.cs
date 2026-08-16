using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Abstracts thread marshaling for scheduling work on Unity's main thread or background threads.
    ///     Interface for EventHub to route event delivery based on EventDeliveryPolicy.
    /// </summary>
    /// <remarks>
    ///     This interface enables EventHub to remain platform-agnostic and testable.
    ///     The Unity-specific implementation (UnityScheduler) lives in the Convai.Runtime assembly.
    ///     Thread Safety:
    ///     - All methods must be thread-safe and callable from any thread
    ///     - ScheduleOnMainThread must safely enqueue work from background threads
    ///     Usage:
    ///     <code>
    /// 
    /// scheduler.ScheduleOnMainThread(() => Debug.Log("On main thread"));
    /// 
    /// 
    /// scheduler.ScheduleOnBackground(() => ProcessData());
    /// 
    /// 
    /// if (scheduler.IsMainThread())
    /// {
    /// 
    /// }
    /// </code>
    /// </remarks>
    public interface IUnityScheduler
    {
        /// <summary>
        ///     Schedules an action to execute on Unity's main thread.
        /// </summary>
        /// <param name="action">Action to execute on the main thread</param>
        /// <exception cref="ArgumentNullException">Thrown if action is null</exception>
        /// <remarks>
        ///     This method is thread-safe and can be called from any thread.
        ///     The action will be queued and executed during the next Unity Update cycle.
        ///     Use this for:
        ///     - Calling Unity APIs (GameObject, Transform, etc.)
        ///     - Updating UI elements
        ///     - Any work that must run on the main thread
        ///     Performance:
        ///     - Actions are queued in a thread-safe queue
        ///     - Executed in FIFO order during Update()
        ///     - Minimal overhead (~microseconds to enqueue)
        /// </remarks>
        public void ScheduleOnMainThread(Action action);

        /// <summary>
        ///     Schedules an action to execute on a background thread from the ThreadPool.
        /// </summary>
        /// <param name="action">Action to execute on a background thread</param>
        /// <exception cref="ArgumentNullException">Thrown if action is null</exception>
        /// <remarks>
        ///     This method is thread-safe and can be called from any thread.
        ///     The action will be executed on a ThreadPool thread immediately.
        ///     Use this for:
        ///     - CPU-intensive computations
        ///     - I/O operations
        ///     - Any work that doesn't require Unity APIs
        ///     Warning:
        ///     - Do NOT call Unity APIs from background threads
        ///     - Do NOT access Unity objects (GameObject, Transform, etc.)
        ///     - Unity will throw exceptions if you violate this
        ///     Performance:
        ///     - Executes immediately on ThreadPool
        ///     - No main thread blocking
        ///     - Ideal for parallel processing
        /// </remarks>
        public void ScheduleOnBackground(Action action);

        /// <summary>
        ///     Checks if the current thread is Unity's main thread.
        /// </summary>
        /// <returns>True if on main thread, false otherwise</returns>
        /// <remarks>
        ///     Use this to conditionally execute code based on thread context.
        ///     Example:
        ///     <code>
        /// if (scheduler.IsMainThread())
        /// {
        /// 
        ///     UpdateUI();
        /// }
        /// else
        /// {
        /// 
        ///     scheduler.ScheduleOnMainThread(() => UpdateUI());
        /// }
        /// </code>
        ///     Performance:
        ///     - Very fast (simple thread ID comparison)
        ///     - No allocations
        /// </remarks>
        public bool IsMainThread();
    }
}
