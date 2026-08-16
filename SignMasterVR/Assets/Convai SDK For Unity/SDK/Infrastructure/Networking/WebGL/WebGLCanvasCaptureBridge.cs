using LiveKit;

namespace Convai.Infrastructure.Networking.WebGL
{
    internal interface IWebGLCanvasCaptureBridge
    {
        public MediaStreamTrack CaptureVideoTrack(int targetFrameRate);
        public void StopVideoTrack(MediaStreamTrack track);
    }

    internal sealed class WebGLCanvasCaptureBridge : IWebGLCanvasCaptureBridge
    {
        public MediaStreamTrack CaptureVideoTrack(int targetFrameRate) =>
            UnityCanvasCaptureInterop.CaptureVideoTrack(targetFrameRate);

        public void StopVideoTrack(MediaStreamTrack track) => UnityCanvasCaptureInterop.StopVideoTrack(track);
    }
}
