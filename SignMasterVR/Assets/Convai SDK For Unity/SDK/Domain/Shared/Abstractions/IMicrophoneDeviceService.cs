using System.Collections.Generic;
using Convai.Shared.Types;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Service for runtime microphone device discovery and resolution.
    /// </summary>
    public interface IMicrophoneDeviceService
    {
        /// <summary>
        ///     Returns all available microphone devices.
        /// </summary>
        public IReadOnlyList<ConvaiMicrophoneDevice> GetAvailableDevices();

        /// <summary>
        ///     Resolves a preferred device ID to a valid device, applying fallback if needed.
        /// </summary>
        public ConvaiMicrophoneDevice ResolvePreferredDevice(string preferredDeviceId);

        /// <summary>
        ///     Resolves preferred device ID to a stable device identifier.
        /// </summary>
        public string ResolvePreferredDeviceId(string preferredDeviceId);

        /// <summary>
        ///     Resolves preferred device ID to runtime device index.
        /// </summary>
        public int ResolvePreferredDeviceIndex(string preferredDeviceId);

        /// <summary>
        ///     Attempts to resolve an explicit device ID.
        /// </summary>
        public bool TryResolveDeviceId(string deviceId, out ConvaiMicrophoneDevice device);
    }
}
