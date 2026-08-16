using System;
using Convai.Infrastructure.Networking.Connection;
using Convai.Infrastructure.Networking.Transport;
using UnityEngine;
using UnityEngine.Scripting;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native platform transport provider for desktop and mobile platforms.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Singleton</b>: Access via <see cref="Instance" />. Thread-safe lazy initialization.
    ///     </para>
    ///     <para>
    ///         <b>Platform Support</b>: Windows, macOS, Linux, iOS, Android.
    ///         Uses LiveKit native SDK for real-time communication.
    ///     </para>
    ///     <para>
    ///         <b>IL2CPP Safety</b>: Uses [Preserve] attribute to prevent stripping.
    ///     </para>
    /// </remarks>
    [Preserve]
    public sealed class NativeTransportProvider : ITransportProvider, IDisposable
    {
        private static readonly object InstanceLock = new();
        private static Lazy<NativeTransportProvider> _lazyInstance = CreateLazyInstance();

        private readonly object _lock = new();
        private GameObject _audioSourceHolder;
        private LiveKitRoomBackend _backend;
        private bool _disposed;
        private NativeTransportFallbackLogger _logger;
        private NativeRealtimeTransport _transport;

        private NativeTransportProvider()
        {
        }

        /// <summary>
        ///     Gets the singleton instance of the native transport provider.
        /// </summary>
        public static NativeTransportProvider Instance
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
        public TransportCapabilities Capabilities => NativeTransportPlatformResolver.GetCapabilities();

        /// <inheritdoc />
        public IRealtimeTransport CreateTransport()
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                ThrowIfDisposed();

                // Dispose any existing transport
                DisposeTransport();

                // Create new transport
                _logger = new NativeTransportFallbackLogger();
                _audioSourceHolder = CreateAudioSourceHolder();
                _backend = new LiveKitRoomBackend(_logger);
                _transport = new NativeRealtimeTransport(_backend, _logger, _audioSourceHolder);

                return _transport;
            }
        }

        /// <inheritdoc />
        public IMicrophoneSourceFactory CreateMicrophoneFactory()
        {
            ThrowIfDisposed();
            return new NativeMicrophoneSourceFactory();
        }

        /// <inheritdoc />
        public IAudioStreamFactory CreateAudioStreamFactory()
        {
            ThrowIfDisposed();
            return new NativeAudioStreamFactory();
        }

        /// <inheritdoc />
        public IVideoSourceFactory CreateVideoSourceFactory()
        {
            ThrowIfDisposed();
            return new NativeVideoSourceFactory();
        }

        /// <inheritdoc />
        public IConvaiRoomControllerFactory CreateRoomControllerFactory()
        {
            ThrowIfDisposed();
            // Create transport inline - the factory will manage it
            return new NativeRoomControllerFactory(() => CreateTransport());
        }

        private void DisposeTransport()
        {
            _transport?.Dispose();
            _transport = null;

            _backend?.Dispose();
            _backend = null;

            _logger = null;

            DestroyAudioSourceHolder();
        }

        private static GameObject CreateAudioSourceHolder()
        {
            var holder = new GameObject("[Convai] Native Transport Audio");
            holder.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(holder);
            return holder;
        }

        private void DestroyAudioSourceHolder()
        {
            if (_audioSourceHolder == null) return;

            if (UnityEngine.Application.isPlaying)
                Object.Destroy(_audioSourceHolder);
            else
                Object.DestroyImmediate(_audioSourceHolder);

            _audioSourceHolder = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeTransportProvider));
        }

        private static Lazy<NativeTransportProvider> CreateLazyInstance() =>
            new(() => new NativeTransportProvider());
    }
}
