#if UNITY_WEBGL || UNITY_EDITOR
using System;
using Convai.Infrastructure.Networking.Transport;
using UnityEngine;
using UnityEngine.Scripting;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL platform transport provider for browser-based builds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Singleton</b>: Access via <see cref="Instance" />. Thread-safe lazy initialization.
    ///     </para>
    ///     <para>
    ///         <b>Platform Support</b>: WebGL builds only.
    ///         Uses LiveKit WebGL SDK for real-time communication.
    ///     </para>
    ///     <para>
    ///         <b>WebGL Constraints</b>:
    ///         <list type="bullet">
    ///             <item>Requires user gesture for audio/microphone</item>
    ///             <item>Audio plays through browser elements (not Unity AudioSource)</item>
    ///             <item>Microphone device selection is browser-controlled</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>IL2CPP Safety</b>: Uses [Preserve] attribute to prevent stripping.
    ///     </para>
    /// </remarks>
    [Preserve]
    public sealed class WebGLTransportProvider : ITransportProvider, IDisposable
    {
        private static readonly object InstanceLock = new();
        private static Lazy<WebGLTransportProvider> _lazyInstance = CreateLazyInstance();

        private readonly object _lock = new();
        private MonoBehaviour _coroutineRunner;
        private bool _disposed;
        private WebGLRealtimeTransport _transport;

        private WebGLTransportProvider()
        {
        }

        /// <summary>
        ///     Gets the singleton instance of the WebGL transport provider.
        /// </summary>
        public static WebGLTransportProvider Instance
        {
            get
            {
                if (_lazyInstance.IsValueCreated && _lazyInstance.Value._disposed)
                {
                    lock (InstanceLock)
                    {
                        if (_lazyInstance.IsValueCreated && _lazyInstance.Value._disposed)
                            _lazyInstance = CreateLazyInstance();
                    }
                }

                return _lazyInstance.Value;
            }
        }

        /// <summary>
        ///     Releases the current transport resources while keeping the singleton reusable.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                DisposeTransport();
                _disposed = false;
            }
        }

        /// <inheritdoc />
        public TransportCapabilities Capabilities => TransportCapabilities.WebGL();

        /// <inheritdoc />
        public IRealtimeTransport CreateTransport()
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                ThrowIfDisposed();

                // Dispose any existing transport
                DisposeTransport();

                // Create coroutine runner for WebGL async operations
                _coroutineRunner = GetOrCreateCoroutineRunner();
                _transport = new WebGLRealtimeTransport(_coroutineRunner);

                return _transport;
            }
        }

        /// <inheritdoc />
        public IMicrophoneSourceFactory CreateMicrophoneFactory()
        {
            ThrowIfDisposed();
            return new WebGLMicrophoneSourceFactory();
        }

        /// <inheritdoc />
        public IAudioStreamFactory CreateAudioStreamFactory()
        {
            ThrowIfDisposed();
            return new WebGLAudioStreamFactory();
        }

        /// <inheritdoc />
        public IVideoSourceFactory CreateVideoSourceFactory()
        {
            ThrowIfDisposed();
            return new WebGLVideoSourceFactory();
        }

        /// <inheritdoc />
        public IConvaiRoomControllerFactory CreateRoomControllerFactory()
        {
            ThrowIfDisposed();
            // Create transport inline - the factory will manage it
            return new WebGLRoomControllerFactory(() => CreateTransport());
        }

        private void DisposeTransport()
        {
            _transport?.Dispose();
            _transport = null;

            DestroyCoroutineRunner();
        }

        private MonoBehaviour GetOrCreateCoroutineRunner()
        {
            if (_coroutineRunner != null && _coroutineRunner) return _coroutineRunner;

            var holder = new GameObject("[Convai] WebGL Transport Coroutine Runner");
            holder.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(holder);

            return holder.AddComponent<WebGLCoroutineRunner>();
        }

        private void DestroyCoroutineRunner()
        {
            if (_coroutineRunner == null) return;

            if (_coroutineRunner.gameObject != null)
            {
                if (UnityEngine.Application.isPlaying)
                    Object.Destroy(_coroutineRunner.gameObject);
                else
                    Object.DestroyImmediate(_coroutineRunner.gameObject);
            }

            _coroutineRunner = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebGLTransportProvider));
        }

        private static Lazy<WebGLTransportProvider> CreateLazyInstance() =>
            new(() => new WebGLTransportProvider());

        /// <summary>
        ///     Internal coroutine runner component for WebGL async operations.
        /// </summary>
        private sealed class WebGLCoroutineRunner : MonoBehaviour
        {
        }
    }
}
#endif
