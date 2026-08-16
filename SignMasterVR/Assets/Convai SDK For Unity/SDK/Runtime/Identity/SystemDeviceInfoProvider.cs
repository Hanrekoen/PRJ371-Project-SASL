using Convai.Domain.Identity;
using UnityEngine;

namespace Convai.Runtime.Identity
{
    /// <summary>
    ///     Provides device information using Unity's SystemInfo API.
    /// </summary>
    /// <remarks>
    ///     This is the default implementation of <see cref="IDeviceInfoProvider" /> that retrieves
    ///     the device's unique identifier from <see cref="SystemInfo.deviceUniqueIdentifier" />.
    /// </remarks>
    internal sealed class SystemDeviceInfoProvider : IDeviceInfoProvider
    {
        /// <inheritdoc />
        public string GetDeviceUniqueIdentifier() => SystemInfo.deviceUniqueIdentifier;
    }
}
