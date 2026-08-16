using UnityEngine;

namespace Convai.Runtime.Vision.Sources.Internal
{
    internal sealed class BuiltInCameraCaptureBackend : ICameraCaptureBackend
    {
        private bool _captureRequestedThisFrame;
        private bool _commandBufferAttached;
        private CameraCaptureBackendContext _context;
        private float _lastHookActivityTime;

        public void Initialize(CameraCaptureBackendContext context)
        {
            _context = context;
            _captureRequestedThisFrame = false;
            _commandBufferAttached = false;
            _lastHookActivityTime = _context.GetCurrentTime();
        }

        public void Start()
        {
            if (_context == null)
                return;

            Camera.onPreRender -= HandlePreRender;
            Camera.onPostRender -= HandlePostRender;
            Camera.onPreRender += HandlePreRender;
            Camera.onPostRender += HandlePostRender;
        }

        public void Stop()
        {
            Camera.onPreRender -= HandlePreRender;
            Camera.onPostRender -= HandlePostRender;

            if (_commandBufferAttached)
            {
                _context?.DetachCaptureCommandBuffer?.Invoke();
                _commandBufferAttached = false;
            }

            _captureRequestedThisFrame = false;
        }

        public bool HasRecentHookActivity(float now, float timeoutSeconds) =>
            now - _lastHookActivityTime <= timeoutSeconds;

        public void Dispose()
        {
            Stop();
            _context = null;
        }

        private void HandlePreRender(Camera camera)
        {
            if (_context == null || !_context.ShouldProcessCamera(camera))
                return;

            float now = _context.GetCurrentTime();
            _lastHookActivityTime = now;

            if (now + Mathf.Epsilon < _context.GetNextCaptureTime())
            {
                _captureRequestedThisFrame = false;
                if (_commandBufferAttached)
                {
                    _context.DetachCaptureCommandBuffer?.Invoke();
                    _commandBufferAttached = false;
                }

                return;
            }

            _context.EnsureCaptureCommandBuffer?.Invoke();
            if (!_commandBufferAttached)
            {
                _context.AttachCaptureCommandBuffer?.Invoke();
                _commandBufferAttached = true;
            }

            _captureRequestedThisFrame = true;
        }

        private void HandlePostRender(Camera camera)
        {
            if (_context == null || !_context.ShouldProcessCamera(camera))
                return;

            float now = _context.GetCurrentTime();
            _lastHookActivityTime = now;

            if (!_captureRequestedThisFrame)
                return;

            _captureRequestedThisFrame = false;

            if (_commandBufferAttached)
            {
                _context.DetachCaptureCommandBuffer?.Invoke();
                _commandBufferAttached = false;
            }

            _context.RegisterHookCapture?.Invoke();
        }
    }
}
