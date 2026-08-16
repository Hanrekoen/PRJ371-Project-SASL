using System;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Audio;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Builder for creating <see cref="RoomRuntime" /> instances with proper coordinator wiring.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Usage</b>:
    ///         <code>
    ///         var roomRuntime = new RoomRuntimeBuilder()
    ///             .WithConnectionAdapter(connectionAdapter)
    ///             .WithAudioAdapter(audioAdapter)
    ///             .WithAgentRegistry(agentRegistry)
    ///             .WithLogger(logger)
    ///             .Build();
    ///         </code>
    ///     </para>
    /// </remarks>
    internal sealed class RoomRuntimeBuilder
    {
        // Ownership coordinator dependencies
        private IAgentRegistry _agentRegistry;
        private IRoomAudioRuntimeAdapter _audioRuntimeAdapter;
        private IRoomConnectionRuntimeAdapter _connectionRuntimeAdapter;

        private ILogger _logger;
        private RemoteAudioPreferenceManager _remoteAudioPreferences;

        #region Connection Coordinator Configuration

        /// <summary>
        ///     Sets the connection runtime adapter used by the connection coordinator.
        /// </summary>
        public RoomRuntimeBuilder WithConnectionAdapter(
            IRoomConnectionRuntimeAdapter connectionRuntimeAdapter,
            Func<string> getRoomName = null,
            Func<string> getSessionId = null)
        {
            _connectionRuntimeAdapter = connectionRuntimeAdapter ??
                                        throw new ArgumentNullException(nameof(connectionRuntimeAdapter));
            return this;
        }

        #endregion

        #region Ownership Coordinator Configuration

        /// <summary>
        ///     Sets the agent registry for the ownership coordinator.
        /// </summary>
        public RoomRuntimeBuilder WithAgentRegistry(IAgentRegistry agentRegistry)
        {
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            return this;
        }

        #endregion

        #region Shared Configuration

        /// <summary>
        ///     Sets the logger.
        /// </summary>
        public RoomRuntimeBuilder WithLogger(ILogger logger)
        {
            _logger = logger;
            return this;
        }

        #endregion

        #region Audio Coordinator Configuration

        /// <summary>
        ///     Sets the audio runtime adapter used by the audio coordinator.
        /// </summary>
        public RoomRuntimeBuilder WithAudioAdapter(IRoomAudioRuntimeAdapter audioRuntimeAdapter)
        {
            _audioRuntimeAdapter = audioRuntimeAdapter ?? throw new ArgumentNullException(nameof(audioRuntimeAdapter));
            return this;
        }

        /// <summary>
        ///     Sets the remote audio preference manager.
        /// </summary>
        public RoomRuntimeBuilder WithRemoteAudioPreferences(RemoteAudioPreferenceManager preferences)
        {
            _remoteAudioPreferences = preferences;
            return this;
        }

        #endregion

        #region Build

        /// <summary>
        ///     Builds and returns a fully configured RoomRuntime.
        /// </summary>
        public RoomRuntime Build()
        {
            ValidateConfiguration();

            // Create diagnostics first (used by other coordinators)
            var diagnostics = new RoomDiagnostics(_agentRegistry);

            // Create connection coordinator
            var connectionCoordinator = new RoomConnectionCoordinator(
                _connectionRuntimeAdapter,
                diagnostics,
                _logger);

            // Create audio coordinator
            var audioCoordinator = new RoomAudioCoordinator(
                _audioRuntimeAdapter,
                _remoteAudioPreferences,
                _logger);

            // Create ownership coordinator
            var ownershipCoordinator = new RoomOwnershipCoordinator(
                _agentRegistry,
                _logger);

            // Create and return the room runtime
            return new RoomRuntime(
                connectionCoordinator,
                audioCoordinator,
                ownershipCoordinator,
                diagnostics,
                _logger);
        }

        private void ValidateConfiguration()
        {
            if (_connectionRuntimeAdapter == null)
            {
                throw new InvalidOperationException(
                    "Connection runtime adapter is required. Call WithConnectionAdapter() before Build().");
            }

            if (_audioRuntimeAdapter == null)
            {
                throw new InvalidOperationException(
                    "Audio runtime adapter is required. Call WithAudioAdapter() before Build().");
            }

            if (_agentRegistry == null)
            {
                throw new InvalidOperationException(
                    "Agent registry is required. Call WithAgentRegistry() before Build().");
            }
        }

        #endregion
    }
}
