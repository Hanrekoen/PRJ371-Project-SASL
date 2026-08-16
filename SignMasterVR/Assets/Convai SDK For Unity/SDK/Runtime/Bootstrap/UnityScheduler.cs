using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime
{
    /// <summary>
    ///     Unity-specific implementation of IUnityScheduler for thread marshaling.
    ///     Enables EventHub to safely deliver events on main thread or background threads.
    /// </summary>
    /// <remarks>
    ///     This MonoBehaviour provides thread-safe scheduling of actions:
    ///     - Main thread actions are queued and executed during Update()
    ///     - Background actions are executed immediately on ThreadPool
    ///     - Singleton pattern ensures single instance per application
    ///     Lifecycle:
    ///     - Created automatically when first accessed via Instance property
    ///     - Persists across scene loads (DontDestroyOnLoad)
    ///     - Cleaned up on application quit
    ///     Performance:
    ///     - Main thread queue uses ConcurrentQueue (lock-free)
    ///     - Minimal overhead per action (~microseconds)
    ///     - Processes all queued actions each frame
    /// </remarks>
    public class UnityScheduler : MonoBehaviour, IUnityScheduler
    {
        private static UnityScheduler _instance;
        private static readonly object _lock = new();

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private bool _initialized;
        private int _mainThreadId;
        private int? _maxPerFrame;

        /// <summary>
        ///     Gets or sets the maximum number of queued actions processed per frame.
        ///     Set to null (or &lt;= 0) to process all queued actions each frame.
        /// </summary>
        public static int? MaxPerFrame
        {
            get => Instance?._maxPerFrame;
            set
            {
                if (Instance == null) return;

                if (value <= 0)
                    Instance._maxPerFrame = null;
                else
                    Instance._maxPerFrame = value;
            }
        }

        /// <summary>
        ///     Gets the singleton instance of UnityScheduler.
        ///     Creates the instance if it doesn't exist.
        /// </summary>
        /// <remarks>
        ///     The instance is created automatically via <see cref="Bootstrap" /> before any scene loads.
        ///     Direct creation only occurs if accessed before Bootstrap runs (e.g., from static constructors).
        /// </remarks>
        public static UnityScheduler Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            // Create directly - no scene discovery needed.
                            // Bootstrap() ensures this runs before any scene code,
                            // and Awake() handles duplicate detection if a scene already has one.
                            GameObject go = new("UnityScheduler");
                            _instance = go.AddComponent<UnityScheduler>();
                            _instance.Initialize();
                        }
                    }
                }

                return _instance;
            }
        }

        /// <summary>
        ///     Gets the current queue size (for diagnostics/testing).
        /// </summary>
        public int QueueSize => _mainThreadQueue.Count;

        /// <summary>
        ///     Unity Awake callback - initializes the scheduler.
        /// </summary>
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                ConvaiLogger.Warning("Duplicate instance detected. Destroying duplicate.",
                    LogCategory.Events);
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            if (UnityEngine.Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                gameObject.hideFlags = HideFlags.HideInHierarchy;
            }
            else
                gameObject.hideFlags = HideFlags.HideAndDontSave;

            _initialized = true;
            ConvaiLogger.Info($"Initialized on thread {_mainThreadId}", LogCategory.Events);
        }

        /// <summary>
        ///     Unity Update callback - processes queued main thread actions.
        /// </summary>
        private void Update()
        {
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Exception in main thread action: {ex}", LogCategory.Events);
                }

                processed++;
                if (_maxPerFrame.HasValue && processed >= _maxPerFrame.Value) break;
            }
        }

        /// <summary>
        ///     Unity OnDestroy callback - cleanup.
        /// </summary>
        private void OnDestroy()
        {
            if (_instance == this)
            {
                ConvaiLogger.Info("Shutting down", LogCategory.Events);
                _instance = null;
            }
        }

        /// <summary>
        ///     Unity OnApplicationQuit callback - cleanup on application exit.
        /// </summary>
        private void OnApplicationQuit()
        {
            if (_instance == this)
            {
                ConvaiLogger.Info("Application quitting - cleaning up", LogCategory.Events);
                while (_mainThreadQueue.TryDequeue(out _)) { }

                _instance = null;
            }
        }

        /// <summary>
        ///     Schedules an action to execute on Unity's main thread.
        /// </summary>
        /// <param name="action">Action to execute on the main thread</param>
        /// <exception cref="ArgumentNullException">Thrown if action is null</exception>
        public void ScheduleOnMainThread(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (IsMainThread())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Exception in immediate main thread action: {ex}",
                        LogCategory.Events);
                }
            }
            else
                _mainThreadQueue.Enqueue(action);
        }

        /// <summary>
        ///     Schedules an action to execute on a background thread from the ThreadPool.
        /// </summary>
        /// <param name="action">Action to execute on a background thread</param>
        /// <exception cref="ArgumentNullException">Thrown if action is null</exception>
        public void ScheduleOnBackground(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ScheduleOnMainThread(() =>
                    {
                        ConvaiLogger.Error($"Exception in background thread action: {ex}",
                            LogCategory.Events);
                    });
                }
            });
        }

        /// <summary>
        ///     Checks if the current thread is Unity's main thread.
        /// </summary>
        /// <returns>True if on main thread, false otherwise</returns>
        public bool IsMainThread() => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        ///     Manually shuts down the UnityScheduler (useful for testing or explicit cleanup).
        /// </summary>
        /// <remarks>
        ///     When the editor is not in play mode the underlying GameObject is destroyed via
        ///     <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)" /> so EditMode tests do not leak hidden
        ///     scheduler instances between runs.
        /// </remarks>
        public static void Shutdown()
        {
            if (_instance != null)
            {
                ConvaiLogger.Info("Manual shutdown requested", LogCategory.Events);
                while (_instance._mainThreadQueue.TryDequeue(out _)) { }

                if (UnityEngine.Application.isPlaying)
                    Destroy(_instance.gameObject);
                else
                    DestroyImmediate(_instance.gameObject);

                _instance = null;
            }
        }

        /// <summary>
        ///     Ensures a scheduler instance exists before the first scene load.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Accessing Instance triggers lazy creation if needed.
            _ = Instance;
        }

        #region Static Convenience API

        /// <summary>
        ///     Gets a value indicating whether the current thread is the Unity main thread.
        /// </summary>
        public static bool IsOnMainThread => Instance?.IsMainThread() ?? false;

        public static int PendingCount => Instance?._mainThreadQueue?.Count ?? 0;

        /// <summary>
        ///     Posts an action to be executed on the Unity main thread.
        ///     If called from the main thread, the action is executed immediately.
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <returns>True if the action was accepted; otherwise false.</returns>
        public static bool Post(Action action)
        {
            if (action == null) return false;

            UnityScheduler instance = Instance;
            if (instance == null)
            {
                ConvaiLogger.Warning("Post called before Bootstrap.", LogCategory.Bootstrap);
                return false;
            }

            instance.ScheduleOnMainThread(action);
            return true;
        }

        /// <summary>
        ///     Posts an action to be executed on the Unity main thread and returns a task that completes when it finishes.
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <returns>A task that completes when the action finishes.</returns>
        public static Task PostAsync(Action action)
        {
            if (action == null) return Task.CompletedTask;

            TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            bool enqueued = Post(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (!enqueued) tcs.TrySetCanceled();

            return tcs.Task;
        }

        /// <summary>
        ///     Posts a function to be executed on the Unity main thread and returns a task containing its result.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="func">Function to execute.</param>
        /// <returns>A task that completes with the function's result.</returns>
        public static Task<T> PostAsync<T>(Func<T> func)
        {
            if (func == null) return Task.FromResult(default(T));

            TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            bool enqueued = Post(() =>
            {
                try
                {
                    T result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (!enqueued) tcs.TrySetCanceled();

            return tcs.Task;
        }

        /// <summary>Clears all pending queued actions.</summary>
        public static void Clear()
        {
            if (_instance != null)
                while (_instance._mainThreadQueue.TryDequeue(out _)) { }
        }

        #endregion

        #region Context Menu Utilities

        /// <summary>
        ///     Resets the singleton instance (useful for testing in editor).
        /// </summary>
        [ContextMenu("Reset Instance")]
        private void ResetInstance()
        {
            if (_instance == this)
            {
                _instance = null;
                ConvaiLogger.Info("Instance reset", LogCategory.Events);
            }
        }

        /// <summary>
        ///     Displays current queue size in the console.
        /// </summary>
        [ContextMenu("Show Queue Size")]
        private void ShowQueueSize() =>
            ConvaiLogger.Info($"Queue size: {QueueSize}", LogCategory.Events);

        #endregion
    }
}
