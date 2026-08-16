using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native (LiveKit) implementation of <see cref="IMicrophoneSourceFactory" />.
    ///     Creates microphone sources that wrap LiveKit's MicrophoneSource.
    /// </summary>
    internal sealed class NativeMicrophoneSourceFactory : IMicrophoneSourceFactory
    {
        /// <inheritdoc />
        public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null) =>
            new NativeMicrophoneSource(deviceName, deviceIndex, hostObject);

        /// <inheritdoc />
        public string[] GetAvailableDevices() => Microphone.devices;
    }
}
