using System;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using LogCategory = Convai.Domain.Logging.LogCategory;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     Factory for creating IConvaiRoomController instances on WebGL platforms.
    ///     Creates a WebGLRoomController using the IRealtimeTransport abstraction.
    /// </summary>
    internal sealed class WebGLRoomControllerFactory : IConvaiRoomControllerFactory, IDisposable
    {
        private readonly Func<IRealtimeTransport> _createTransport;
        private readonly object _lock = new();

        private MonoBehaviour _coroutineRunner;

        /// <summary>
        ///     Creates a new factory instance with the specified transport factory.
        /// </summary>
        /// <param name="createTransport">
        ///     Factory function that creates IRealtimeTransport instances.
        ///     Must be provided by the caller (typically an ITransportProvider).
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown if createTransport is null.</exception>
        internal WebGLRoomControllerFactory(Func<IRealtimeTransport> createTransport)
        {
            _createTransport = createTransport ?? throw new ArgumentNullException(nameof(createTransport));
        }

        /// <inheritdoc />
        public IConvaiRoomController Create(
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            ITransportConfiguration transportConfiguration,
            ISessionPersistence sessionPersistence,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            INarrativeSectionNameResolver sectionNameResolver = null)
        {
            logger = logger.WithTag(nameof(WebGLRoomControllerFactory));
            IRealtimeTransport transport;
            try
            {
                transport = _createTransport.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to create transport: {ex.Message}",
                    LogCategory.Transport);
                return null;
            }

            // Get or create a coroutine runner for HTTP calls
            MonoBehaviour coroutineRunner = GetOrCreateCoroutineRunner();
            if (coroutineRunner == null)
            {
                logger?.Error("Failed to create coroutine runner", LogCategory.Transport);
                return null;
            }

            return new WebGLRoomController(
                agentRegistry,
                playerSession,
                transportConfiguration,
                sessionPersistence,
                dispatcher,
                logger,
                eventHub,
                transport,
                coroutineRunner,
                sectionNameResolver);
        }

        public void Dispose()
        {
            MonoBehaviour coroutineRunner = _coroutineRunner;
            _coroutineRunner = null;

            if (coroutineRunner == null)
                return;

            GameObject runnerObject = coroutineRunner.gameObject;
            if (runnerObject == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Object.Destroy(runnerObject);
            else
                Object.DestroyImmediate(runnerObject);
        }

        /// <summary>
        ///     Gets or creates a coroutine runner for HTTP operations.
        /// </summary>
        private MonoBehaviour GetOrCreateCoroutineRunner()
        {
            if (_coroutineRunner != null) return _coroutineRunner;

            lock (_lock)
            {
                if (_coroutineRunner != null) return _coroutineRunner;

                // Create a dedicated GameObject for coroutine running
                var runnerObject = new GameObject("[Convai] WebGL HTTP Coroutine Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(runnerObject);

                _coroutineRunner = runnerObject.AddComponent<WebGLHttpCoroutineRunner>();

                ConvaiLogger.Debug("Created HTTP coroutine runner.",
                    LogCategory.Transport);

                return _coroutineRunner;
            }
        }
    }

    /// <summary>
    ///     Simple MonoBehaviour that provides coroutine support for WebGL HTTP operations.
    /// </summary>
    internal sealed class WebGLHttpCoroutineRunner : MonoBehaviour
    {
        // Intentionally empty - just provides coroutine hosting
    }
}
