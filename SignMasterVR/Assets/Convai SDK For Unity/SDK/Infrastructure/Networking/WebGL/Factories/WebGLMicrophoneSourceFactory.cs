using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IMicrophoneSourceFactory" />.
    ///     Creates microphone sources that work with browser-based audio capture.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, microphone access requires a user gesture and browser permission.
    ///         The actual capture is managed by the LiveKit WebGL SDK through getUserMedia.
    ///         Device enumeration is limited on WebGL - the browser handles device selection.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLMicrophoneSourceFactory : IMicrophoneSourceFactory
    {
        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the hostObject parameter is ignored as microphone capture
        ///     is handled entirely by the browser. The deviceName and deviceIndex
        ///     are stored for reference but actual device selection is browser-controlled.
        /// </remarks>
        public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null) =>
            new WebGLMicrophoneSource(deviceName, deviceIndex);

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, device enumeration is limited. The browser handles device selection
        ///     through its own UI when getUserMedia is called. This returns a single default device entry.
        /// </remarks>
        public string[] GetAvailableDevices()
        {
            // WebGL doesn't support programmatic device enumeration the same way native does.
            // The browser handles device selection via its permission prompt.
            // Return a default device name as placeholder.
            return new[] { "Default Microphone" };
        }
    }
}
