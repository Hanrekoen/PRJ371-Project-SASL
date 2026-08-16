using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IVideoSourceFactory" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         WebGL scene publishing uses browser canvas capture instead of Unity RenderTextures.
    ///         The visible Unity canvas can be captured through <c>captureStream()</c> and published as a browser
    ///         <see cref="LiveKit.MediaStreamTrack" />.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLVideoSourceFactory : IVideoSourceFactory
    {
        private const string NotSupportedMessage =
            "Unity RenderTexture and Camera video sources are not supported on WebGL. " +
            "Use CreateFromCanvasCapture() to publish the visible Unity canvas in browser builds.";

        private readonly IWebGLCanvasCaptureBridge _canvasCaptureBridge;

        public WebGLVideoSourceFactory()
            : this(new WebGLCanvasCaptureBridge())
        {
        }

        internal WebGLVideoSourceFactory(IWebGLCanvasCaptureBridge canvasCaptureBridge)
        {
            _canvasCaptureBridge = canvasCaptureBridge ?? throw new ArgumentNullException(nameof(canvasCaptureBridge));
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown on WebGL.</exception>
        public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null) =>
            throw new NotSupportedException(NotSupportedMessage);

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown on WebGL.</exception>
        public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null) =>
            throw new NotSupportedException(NotSupportedMessage);

        /// <inheritdoc />
        public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15) =>
            new WebGLCanvasVideoSource(_canvasCaptureBridge, targetFrameRate, name);
    }
}
