using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Base interface for video sources that can be published.
    /// </summary>
    public interface IVideoSource : IDisposable
    {
        /// <summary>
        ///     Gets the name/identifier of this video source.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets whether this source is currently capturing video.
        /// </summary>
        public bool IsCapturing { get; }

        /// <summary>
        ///     Gets the video width.
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///     Gets the video height.
        /// </summary>
        public int Height { get; }

        /// <summary>
        ///     Starts capturing video from this source.
        /// </summary>
        public void StartCapture();

        /// <summary>
        ///     Stops capturing video from this source.
        /// </summary>
        public void StopCapture();
    }

    /// <summary>
    ///     Interface for video sources backed by a Unity Texture.
    /// </summary>
    public interface ITextureVideoSource : IVideoSource
    {
        /// <summary>
        ///     Gets the source texture.
        /// </summary>
        public Texture SourceTexture { get; }

        /// <summary>
        ///     Gets or sets the target frame rate.
        /// </summary>
        public int TargetFrameRate { get; set; }

        /// <summary>
        ///     Updates the source with a new texture.
        /// </summary>
        /// <param name="texture">The new source texture.</param>
        public void SetTexture(Texture texture);
    }
}
