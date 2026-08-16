using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Factory interface for creating platform-specific video sources.
    ///     Implementations are provided by platform-specific assemblies (Native, WebGL).
    /// </summary>
    public interface IVideoSourceFactory
    {
        /// <summary>
        ///     Creates a video source from a Unity RenderTexture.
        /// </summary>
        /// <param name="texture">The RenderTexture to capture frames from.</param>
        /// <param name="name">Optional name for the video source.</param>
        /// <returns>A platform-specific video source implementation.</returns>
        public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null);

        /// <summary>
        ///     Creates a video source from a Unity Camera.
        /// </summary>
        /// <param name="camera">The Camera to capture frames from.</param>
        /// <param name="width">The capture width in pixels.</param>
        /// <param name="height">The capture height in pixels.</param>
        /// <param name="name">Optional name for the video source.</param>
        /// <returns>A platform-specific video source implementation.</returns>
        public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null);

        /// <summary>
        ///     Creates a browser-backed video source from the visible Unity canvas when supported.
        ///     This is primarily intended for WebGL builds where Unity textures are not published directly.
        /// </summary>
        /// <param name="name">Optional name for the video source.</param>
        /// <param name="targetFrameRate">Desired capture frame rate.</param>
        /// <returns>A platform-specific video source implementation.</returns>
        /// <exception cref="System.InvalidOperationException">
        ///     Thrown when browser canvas capture is unavailable on the current platform/runtime.
        /// </exception>
        public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15);
    }
}
