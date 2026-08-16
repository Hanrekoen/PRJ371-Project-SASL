using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Convai.Runtime.Vision.Sources.Internal
{
    internal sealed class CameraCaptureBackendContext
    {
        public Camera TargetCamera { get; set; }

        public Func<Camera, bool> ShouldProcessCamera { get; set; }

        public Func<float> GetNextCaptureTime { get; set; }

        public Func<float> GetCurrentTime { get; set; }

        public Func<CommandBuffer> EnsureCaptureCommandBuffer { get; set; }

        public Action AttachCaptureCommandBuffer { get; set; }

        public Action DetachCaptureCommandBuffer { get; set; }

        public Action RegisterHookCapture { get; set; }

        public Action<string, string> StopCaptureWithError { get; set; }
    }

    internal interface ICameraCaptureBackend : IDisposable
    {
        public void Initialize(CameraCaptureBackendContext context);

        public bool HasRecentHookActivity(float now, float timeoutSeconds);

        public void Start();

        public void Stop();
    }
}
