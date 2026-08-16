using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Thin MonoBehaviour adapter that bridges Unity lifecycle to ConvaiRuntime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Design Note</b>: This component holds a reference to a <see cref="ConvaiRuntime" />
    ///         instance and maps Unity lifecycle events (Awake, Start, OnDestroy, OnApplicationPause,
    ///         OnApplicationFocus) to runtime lifecycle operations.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>: Create via <see cref="ConvaiManager" /> which builds the runtime
    ///         and passes it to this adapter, or programmatically via <see cref="Initialize" />.
    ///     </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UnityConvaiAdapter : MonoBehaviour
    {
        private bool _isPaused;
        private readonly HashSet<RuntimePauseReason> _applicationPauseReasons = new();
        private Action<bool, RuntimePauseReason> _applicationLifecycleHandler;
        private bool _isApplicationBackgrounded;
        private CancellationTokenSource _lifetimeCts;
        private bool _startingOrStopping;

        public ConvaiRuntime Runtime { get; private set; }

        /// <summary>
        ///     Gets whether the adapter has been initialized with a runtime.
        /// </summary>
        public bool IsInitialized { get; private set; }

        public RuntimeState State => Runtime?.State ?? RuntimeState.Disposed;

        /// <summary>
        ///     Event fired when the runtime state changes.
        /// </summary>
        public event Action<RuntimeStateChanged> StateChanged;

        /// <summary>
        ///     Routes application focus/background transitions to an owner that applies a project policy.
        ///     When no handler is configured, the adapter preserves its legacy pause/resume behavior.
        /// </summary>
        internal void SetApplicationLifecycleHandler(Action<bool, RuntimePauseReason> handler) =>
            _applicationLifecycleHandler = handler;

        /// <summary>
        ///     Initializes the adapter with a runtime instance.
        /// </summary>
        /// <param name="runtime">The runtime to manage.</param>
        /// <exception cref="InvalidOperationException">If already initialized.</exception>
        public void Initialize(ConvaiRuntime runtime)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "[UnityConvaiAdapter] Already initialized. Cannot initialize twice.");
            }

            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _lifetimeCts = new CancellationTokenSource();
            IsInitialized = true;

            // Subscribe to runtime state changes
            if (Runtime.Events != null) Runtime.Events.Subscribe<RuntimeStateChanged>(OnRuntimeStateChanged);

            ConvaiLogger.Debug("Initialized with runtime.", LogCategory.Bootstrap);
        }

        /// <summary>
        ///     Starts the runtime asynchronously.
        /// </summary>
        public IConvaiOperation<Unit> StartRuntimeAsync() =>
            ConvaiOperation<Unit>.FromTask(StartRuntimeAsyncCore());

        private async Task<Unit> StartRuntimeAsyncCore()
        {
            if (!IsInitialized || Runtime == null)
            {
                ConvaiLogger.Error("Cannot start: not initialized.", LogCategory.Bootstrap);
                return Unit.Value;
            }

            if (_startingOrStopping)
            {
                ConvaiLogger.Warning("Start/stop already in progress.", LogCategory.Bootstrap);
                return Unit.Value;
            }

            if (Runtime.State != RuntimeState.Created)
            {
                ConvaiLogger.Warning($"Cannot start: runtime is in {Runtime.State} state.", LogCategory.Bootstrap);
                return Unit.Value;
            }

            try
            {
                _startingOrStopping = true;
                await Runtime.StartAsync(_lifetimeCts.Token);
            }
            finally
            {
                _startingOrStopping = false;
            }

            return Unit.Value;
        }

        /// <summary>
        ///     Stops the runtime asynchronously.
        /// </summary>
        public IConvaiOperation<Unit> StopRuntimeAsync() =>
            ConvaiOperation<Unit>.FromTask(StopRuntimeAsyncCore());

        private async Task<Unit> StopRuntimeAsyncCore()
        {
            if (!IsInitialized || Runtime == null)
                return Unit.Value;

            if (_startingOrStopping)
            {
                ConvaiLogger.Warning("Start/stop already in progress.", LogCategory.Bootstrap);
                return Unit.Value;
            }

            try
            {
                _startingOrStopping = true;
                await Runtime.StopAsync(_lifetimeCts.Token);
            }
            finally
            {
                _startingOrStopping = false;
            }

            return Unit.Value;
        }

        #region Unity Lifecycle

        private void OnEnable()
        {
            if (IsInitialized && _isPaused && Runtime?.State == RuntimeState.Paused)
            {
                // Resume on re-enable (e.g., scene re-activated)
                _ = ResumeRuntimeAsyncCore();
            }
        }

        private void OnDisable()
        {
            if (IsInitialized && Runtime?.State == RuntimeState.Running)
            {
                // Pause on disable (e.g., scene deactivated)
                _ = PauseRuntimeAsyncCore(RuntimePauseReason.SceneTransition);
            }
        }

        private async void OnDestroy()
        {
            if (Runtime != null)
            {
                _lifetimeCts?.Cancel();

                try
                {
                    await Runtime.DisposeAsync();
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Error disposing runtime: {ex.Message}", LogCategory.Bootstrap);
                }

                Runtime = null;
            }

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            IsInitialized = false;
            _applicationLifecycleHandler = null;
            _applicationPauseReasons.Clear();
            _isApplicationBackgrounded = false;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!IsInitialized || Runtime == null)
                return;

            HandleApplicationLifecycleState(pauseStatus, RuntimePauseReason.ApplicationBackground);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!IsInitialized || Runtime == null)
                return;

#if !UNITY_EDITOR
            if (!hasFocus)
            {
                HandleApplicationLifecycleState(true, RuntimePauseReason.ApplicationFocusLost);
            }
            else
            {
                HandleApplicationLifecycleState(false, RuntimePauseReason.ApplicationFocusLost);
            }
#endif
        }

        #endregion

        #region Internal Operations

        /// <summary>Pauses runtime modules while keeping the room connection owned by the room manager.</summary>
        public IConvaiOperation<Unit> PauseRuntimeAsync(
            RuntimePauseReason reason = RuntimePauseReason.UserRequested) =>
            ConvaiOperation<Unit>.FromTask(PauseRuntimeAsyncCore(reason));

        private async Task<Unit> PauseRuntimeAsyncCore(RuntimePauseReason reason)
        {
            if (Runtime?.State != RuntimeState.Running)
                return Unit.Value;

            try
            {
                _isPaused = true;
                await Runtime.PauseAsync(reason, _lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Error pausing runtime: {ex.Message}", LogCategory.Bootstrap);
            }

            return Unit.Value;
        }

        /// <summary>Resumes runtime modules from a paused state.</summary>
        public IConvaiOperation<Unit> ResumeRuntimeAsync() =>
            ConvaiOperation<Unit>.FromTask(ResumeRuntimeAsyncCore());

        private async Task<Unit> ResumeRuntimeAsyncCore()
        {
            if (Runtime?.State != RuntimeState.Paused)
                return Unit.Value;

            try
            {
                await Runtime.ResumeAsync(_lifetimeCts.Token);
                _isPaused = false;
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Error resuming runtime: {ex.Message}", LogCategory.Bootstrap);
            }

            return Unit.Value;
        }

        private void HandleApplicationLifecycleState(bool isPaused, RuntimePauseReason reason)
        {
            if (isPaused)
                _applicationPauseReasons.Add(reason);
            else
                _applicationPauseReasons.Remove(reason);

            bool isBackgrounded = _applicationPauseReasons.Count > 0;
            if (isBackgrounded == _isApplicationBackgrounded)
                return;

            _isApplicationBackgrounded = isBackgrounded;
            if (_applicationLifecycleHandler != null)
            {
                _applicationLifecycleHandler(isBackgrounded, reason);
                return;
            }

            if (isBackgrounded)
                _ = PauseRuntimeAsyncCore(reason);
            else
                _ = ResumeRuntimeAsyncCore();
        }

        private void OnRuntimeStateChanged(RuntimeStateChanged stateChange)
        {
            ConvaiLogger.Debug($"State changed: {stateChange.PreviousState} -> {stateChange.NewState}", LogCategory.Bootstrap);
            StateChanged?.Invoke(stateChange);
        }

        #endregion
    }
}
