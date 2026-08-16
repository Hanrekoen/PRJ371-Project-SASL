using System;
using Convai.Infrastructure.Networking.Transport;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.WebGL
{
    internal sealed class WebGLRealtimeTransportAccessor : IRealtimeTransportAccessor, IDisposable
    {
        private readonly Func<IRealtimeTransport> _createTransport;
        private readonly object _lock = new();

        private MonoBehaviour _coroutineRunner;
        private bool _disposed;
        private bool _ownsTransport;
        private IRealtimeTransport _transport;

        internal WebGLRealtimeTransportAccessor(Func<IRealtimeTransport> createTransport = null)
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

                DestroyCoroutineRunner();
            }
        }

        public TransportCapabilities Capabilities => TransportCapabilities.WebGL();

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

                    _transport = new WebGLRealtimeTransport(GetOrCreateCoroutineRunner());
                    _ownsTransport = true;
                    return _transport;
                }
            }
        }

        private MonoBehaviour GetOrCreateCoroutineRunner()
        {
            if (_coroutineRunner != null)
                return _coroutineRunner;

            var runnerObject = new GameObject("[Convai] WebGL Coroutine Runner");
            runnerObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(runnerObject);

            _coroutineRunner = runnerObject.AddComponent<WebGLHttpCoroutineRunner>();
            return _coroutineRunner;
        }

        private void DestroyCoroutineRunner()
        {
            if (_coroutineRunner == null)
                return;

            GameObject runnerObject = _coroutineRunner.gameObject;
            _coroutineRunner = null;

            if (runnerObject == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Object.Destroy(runnerObject);
            else
                Object.DestroyImmediate(runnerObject);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebGLRealtimeTransportAccessor));
        }
    }
}
