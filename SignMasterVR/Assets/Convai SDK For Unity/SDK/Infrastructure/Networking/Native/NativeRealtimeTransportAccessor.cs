using System;
using Convai.Infrastructure.Networking.Connection;
using Convai.Infrastructure.Networking.Transport;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeRealtimeTransportAccessor : IRealtimeTransportAccessor, IDisposable
    {
        private readonly Func<IRealtimeTransport> _createTransport;
        private readonly object _lock = new();
        private GameObject _audioSourceHolder;

        private LiveKitRoomBackend _backend;
        private bool _disposed;
        private NativeTransportFallbackLogger _logger;
        private bool _ownsTransport;
        private IRealtimeTransport _transport;

        internal NativeRealtimeTransportAccessor(Func<IRealtimeTransport> createTransport = null)
        {
            _createTransport = createTransport;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_ownsTransport && _transport is IDisposable disposable)
                    disposable.Dispose();

                _transport = null;
                _ownsTransport = false;

                _backend?.Dispose();
                _backend = null;
                _logger = null;

                DestroyAudioSourceHolder();
            }
        }

        public TransportCapabilities Capabilities => NativeTransportPlatformResolver.GetCapabilities();

        public IRealtimeTransport Transport
        {
            get
            {
                ThrowIfDisposed();

                if (_createTransport != null)
                    return _createTransport.Invoke();

                if (_transport != null)
                    return _transport;

                lock (_lock)
                {
                    ThrowIfDisposed();

                    if (_transport != null)
                        return _transport;

                    _logger = new NativeTransportFallbackLogger();
                    _audioSourceHolder = CreateAudioSourceHolder();
                    _backend = new LiveKitRoomBackend(_logger);
                    _transport = new NativeRealtimeTransport(_backend, _logger, _audioSourceHolder);
                    _ownsTransport = true;
                    return _transport;
                }
            }
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
            if (_audioSourceHolder == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Object.Destroy(_audioSourceHolder);
            else
                Object.DestroyImmediate(_audioSourceHolder);

            _audioSourceHolder = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeRealtimeTransportAccessor));
        }
    }
}
