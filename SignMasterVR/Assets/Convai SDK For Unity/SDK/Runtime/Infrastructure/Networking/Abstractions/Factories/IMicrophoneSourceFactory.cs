using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Factory interface for creating platform-specific microphone sources.
    ///     Implementations are provided by platform-specific assemblies (Native, WebGL).
    /// </summary>
    public interface IMicrophoneSourceFactory
    {
        /// <summary>
        ///     Creates a new microphone source for the specified device.
        /// </summary>
        /// <param name="deviceName">The name of the microphone device to use. Null or empty for default device.</param>
        /// <param name="deviceIndex">The index of the device if multiple devices share the same name.</param>
        /// <param name="hostObject">Optional GameObject to host the microphone source component. If null, one will be created.</param>
        /// <returns>A platform-specific microphone source implementation.</returns>
        public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null);

        /// <summary>
        ///     Gets the list of available microphone device names.
        /// </summary>
        /// <returns>Array of available microphone device names.</returns>
        public string[] GetAvailableDevices();
    }
}
