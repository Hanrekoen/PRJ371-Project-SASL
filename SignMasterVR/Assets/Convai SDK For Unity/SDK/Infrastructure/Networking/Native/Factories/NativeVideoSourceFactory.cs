using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native (LiveKit) implementation of <see cref="IVideoSourceFactory" />.
    ///     Creates video sources that wrap LiveKit's TextureVideoSource.
    /// </summary>
    internal sealed class NativeVideoSourceFactory : IVideoSourceFactory
    {
        private const int DefaultFrameRate = 30;

        /// <inheritdoc />
        public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null) =>
            new NativeTextureVideoSource(texture, DefaultFrameRate, name);

        /// <inheritdoc />
        public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null)
        {
            throw new NotSupportedException(
                "Direct native camera video sources are not supported because they assign Camera.targetTexture without capture lifecycle ownership. " +
                "Use CameraVisionFrameSource and publish its RenderTexture instead.");
        }

        /// <inheritdoc />
        public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15)
        {
            throw new NotSupportedException(
                "Canvas capture is only supported on WebGL. Native platforms should publish Unity RenderTextures directly.");
        }
    }
}
