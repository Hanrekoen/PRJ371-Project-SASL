using System;
using Convai.Domain.Identity;
using UnityEngine;

namespace Convai.Runtime.Identity
{
    /// <summary>
    ///     Generates an end user identifier using a persisted editor fallback or a device identifier with
    ///     a persisted GUID fallback in player builds.
    ///     All Unity API calls (<see cref="PlayerPrefs" />, <see cref="SystemInfo" />) are marshaled to
    ///     the main thread via <see cref="UnityScheduler" /> to prevent threading violations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This provider generates a stable, device-specific identifier that allows the Convai server
    ///         to track end users across sessions. This enables features like:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>Stable identity tracking across sessions</item>
    ///         <item>End-user management and analytics</item>
    ///         <item>Character-scoped memory continuity when memory is enabled</item>
    ///     </list>
    ///     <para>
    ///         In the Unity Editor, this provider always uses a persisted <see cref="PlayerPrefs" /> fallback
    ///         so Play Mode sessions keep a stable end-user identity for the same project/editor environment.
    ///         In player builds, when the device identifier is unavailable, a GUID is generated once and
    ///         persisted via <see cref="PlayerPrefs" /> so the same fallback is reused across sessions.
    ///     </para>
    /// </remarks>
    public sealed class DeviceEndUserIdProvider : IEndUserIdProvider, IEndUserIdentityProvider
    {
        private const string FallbackIdKey = "convai.end_user_id";
        private readonly IDeviceInfoProvider _deviceInfoProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeviceEndUserIdProvider" /> class
        ///     using the default <see cref="SystemDeviceInfoProvider" />.
        /// </summary>
        public DeviceEndUserIdProvider() : this(new SystemDeviceInfoProvider())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeviceEndUserIdProvider" /> class
        ///     with the specified device info provider.
        /// </summary>
        /// <param name="deviceInfoProvider">
        ///     The provider for device information. If <c>null</c>, falls back to <see cref="SystemDeviceInfoProvider" />.
        /// </param>
        public DeviceEndUserIdProvider(IDeviceInfoProvider deviceInfoProvider)
        {
            _deviceInfoProvider = deviceInfoProvider ?? new SystemDeviceInfoProvider();
        }

        /// <inheritdoc />
        public string GetEndUserId()
        {
            if (UnityScheduler.IsOnMainThread) return GenerateEndUserIdDirect();

            return UnityScheduler.PostAsync(GenerateEndUserIdDirect).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Generates a stable end user identifier for this device.
        /// </summary>
        /// <returns>
        ///     In the editor, a project-scoped persisted identifier. In player builds, the device's unique
        ///     identifier from <see cref="IDeviceInfoProvider.GetDeviceUniqueIdentifier" />, or a persisted GUID
        ///     if the device identifier is unavailable or invalid.
        /// </returns>
        public string GenerateEndUserId() => GetEndUserId();

        /// <summary>
        ///     Core logic — must run on the main thread because both
        ///     <see cref="IDeviceInfoProvider.GetDeviceUniqueIdentifier" /> and
        ///     <see cref="PlayerPrefs" /> require main-thread access.
        /// </summary>
        private string GenerateEndUserIdDirect()
        {
            if (UnityEngine.Application.isEditor)
                return GetOrCreateFallbackId();

            string deviceIdentifier = _deviceInfoProvider.GetDeviceUniqueIdentifier();
            if (IsValid(deviceIdentifier)) return deviceIdentifier;

            return GetOrCreateFallbackId();
        }

        private static string GetOrCreateFallbackId()
        {
            string storedId = PlayerPrefs.GetString(FallbackIdKey, string.Empty);
            if (IsValid(storedId)) return storedId;

            string newId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(FallbackIdKey, newId);
            PlayerPrefs.Save();
            return newId;
        }

        /// <summary>
        ///     Validates that the device identifier is usable.
        /// </summary>
        /// <param name="value">The device identifier to validate.</param>
        /// <returns>
        ///     True if the identifier is valid and usable; false if it's null, empty,
        ///     unsupported, or consists entirely of zeros.
        /// </returns>
        internal static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            if (value == SystemInfo.unsupportedIdentifier) return false;

            bool allZeros = true;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '0')
                {
                    allZeros = false;
                    break;
                }
            }

            return !allZeros;
        }
    }
}
