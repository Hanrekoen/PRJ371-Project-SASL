using System;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    internal sealed class WebGLCanvasVideoSource : IVideoSource
    {
        private readonly IWebGLCanvasCaptureBridge _captureBridge;
        private readonly int _targetFrameRate;
        private bool _disposed;

        public WebGLCanvasVideoSource(IWebGLCanvasCaptureBridge captureBridge, int targetFrameRate = 15,
            string name = null)
        {
            _captureBridge = captureBridge ?? throw new ArgumentNullException(nameof(captureBridge));
            _targetFrameRate = Mathf.Max(1, targetFrameRate);
            Name = string.IsNullOrWhiteSpace(name) ? "unity-scene" : name;
        }

        internal MediaStreamTrack MediaStreamTrack { get; private set; }

        public string Name { get; }

        public bool IsCapturing { get; private set; }

        public int Width => Mathf.Max(Screen.width, 1);

        public int Height => Mathf.Max(Screen.height, 1);

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (IsCapturing) return;

            MediaStreamTrack = _captureBridge.CaptureVideoTrack(_targetFrameRate);
            IsCapturing = MediaStreamTrack != null;

            if (!IsCapturing)
                throw new InvalidOperationException("Failed to capture the Unity WebGL canvas as a MediaStreamTrack.");
        }

        public void StopCapture()
        {
            if (!IsCapturing) return;

            _captureBridge.StopVideoTrack(MediaStreamTrack);
            MediaStreamTrack = null;
            IsCapturing = false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopCapture();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebGLCanvasVideoSource));
        }
    }
}
