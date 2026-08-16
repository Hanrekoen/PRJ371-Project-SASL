using System.Collections.Generic;
using System.Threading;
using Convai.Domain.EventSystem;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.Providers;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Top-level runtime contract for the Convai SDK.
    /// </summary>
    /// <remarks>
    ///     Created by <see cref="ConvaiRuntimeBuilder" /> and exposed through the active runtime host.
    ///     Use it for room access, event flow, module lifecycle, and runtime-scoped providers.
    /// </remarks>
    public interface IConvaiRuntime
    {
        #region State

        /// <summary>
        ///     Current runtime state.
        /// </summary>
        public RuntimeState State { get; }

        #endregion

        #region Lifecycle

        /// <summary>
        ///     Starts the runtime and all registered modules.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when startup is finished.</returns>
        /// <exception cref="System.InvalidOperationException">
        ///     Thrown if runtime is not in <see cref="RuntimeState.Created" /> state.
        /// </exception>
        public IConvaiOperation<Unit> StartAsync(CancellationToken ct = default);

        /// <summary>
        ///     Pauses the runtime and all modules.
        /// </summary>
        /// <param name="reason">Reason for pausing (e.g., application background, user request).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when pause is finished.</returns>
        /// <exception cref="System.InvalidOperationException">
        ///     Thrown if runtime is not in <see cref="RuntimeState.Running" /> state.
        /// </exception>
        public IConvaiOperation<Unit> PauseAsync(RuntimePauseReason reason, CancellationToken ct = default);

        /// <summary>
        ///     Resumes the runtime from a paused state.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when resume is finished.</returns>
        /// <exception cref="System.InvalidOperationException">
        ///     Thrown if runtime is not in <see cref="RuntimeState.Paused" /> state.
        /// </exception>
        public IConvaiOperation<Unit> ResumeAsync(CancellationToken ct = default);

        /// <summary>
        ///     Stops the runtime and all modules.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when stop is finished.</returns>
        /// <remarks>
        ///     Safe to call from any state. Does nothing if already stopped or disposed.
        /// </remarks>
        public IConvaiOperation<Unit> StopAsync(CancellationToken ct = default);

        #endregion

        #region Core Services

        /// <summary>
        ///     Room runtime for connection, audio, and ownership management.
        /// </summary>
        public IRoomRuntime Room { get; }

        /// <summary>
        ///     Event hub for decoupled communication.
        /// </summary>
        public IEventHub Events { get; }

        /// <summary>
        ///     Agent registry for character and player management.
        /// </summary>
        public IAgentRegistry Agents { get; }

        /// <summary>
        ///     Registered runtime modules.
        /// </summary>
        public IReadOnlyList<IConvaiModule> Modules { get; }

        #endregion

        #region Providers

        /// <summary>
        ///     Transport provider for platform-specific communication.
        /// </summary>
        public ITransportProvider Transport { get; }

        /// <summary>
        ///     Conversation provider for AI backend communication.
        /// </summary>
        public IConversationProvider Conversation { get; }

        /// <summary>
        ///     Bootstrap configuration snapshot (immutable).
        /// </summary>
        public ConvaiBootstrapConfigSnapshot Config { get; }

        /// <summary>
        ///     Mutable runtime preferences.
        /// </summary>
        public IRuntimePreferences RuntimePreferences { get; }

        /// <summary>
        ///     Feature variant provider.
        /// </summary>
        public IFeatureVariantProvider FeatureVariants { get; }

        /// <summary>
        ///     Persistence provider for runtime-owned storage.
        /// </summary>
        public IPersistenceProvider Persistence { get; }

        /// <summary>
        ///     Telemetry provider for runtime observability.
        /// </summary>
        public ITelemetryProvider Telemetry { get; }

        #endregion
    }
}
