using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Default runtime microphone discovery and resolution service.
    /// </summary>
    public sealed class MicrophoneDeviceService : IMicrophoneDeviceService
    {
        private readonly IMicrophoneSourceFactory _factory;

        public MicrophoneDeviceService(IMicrophoneSourceFactory factory = null)
        {
            _factory = factory;
        }

        public IReadOnlyList<ConvaiMicrophoneDevice> GetAvailableDevices()
        {
            string[] names = GetDeviceNames();
            if (names == null || names.Length == 0) return Array.Empty<ConvaiMicrophoneDevice>();

            var idsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var devices = new List<ConvaiMicrophoneDevice>(names.Length);

            for (int i = 0; i < names.Length; i++)
            {
                string displayName = string.IsNullOrWhiteSpace(names[i]) ? $"Microphone {i + 1}" : names[i].Trim();

                if (!idsByName.TryGetValue(displayName, out int count)) count = 0;

                count++;
                idsByName[displayName] = count;

                string id = count == 1 ? displayName : $"{displayName}#{count}";
                devices.Add(new ConvaiMicrophoneDevice(id, displayName, i));
            }

            return devices;
        }

        public ConvaiMicrophoneDevice ResolvePreferredDevice(string preferredDeviceId)
        {
            IReadOnlyList<ConvaiMicrophoneDevice> devices = GetAvailableDevices();
            if (devices.Count == 0) return ConvaiMicrophoneDevice.None;

            if (!string.IsNullOrWhiteSpace(preferredDeviceId) &&
                TryResolveFromList(devices, preferredDeviceId, out ConvaiMicrophoneDevice resolved)) return resolved;

            return devices[0];
        }

        public string ResolvePreferredDeviceId(string preferredDeviceId) =>
            ResolvePreferredDevice(preferredDeviceId).Id;

        public int ResolvePreferredDeviceIndex(string preferredDeviceId) =>
            ResolvePreferredDevice(preferredDeviceId).Index;

        public bool TryResolveDeviceId(string deviceId, out ConvaiMicrophoneDevice device) =>
            TryResolveFromList(GetAvailableDevices(), deviceId, out device);

        private static bool TryResolveFromList(IReadOnlyList<ConvaiMicrophoneDevice> devices, string deviceId,
            out ConvaiMicrophoneDevice device)
        {
            device = ConvaiMicrophoneDevice.None;
            if (devices == null || devices.Count == 0 || string.IsNullOrWhiteSpace(deviceId)) return false;

            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Id, deviceId, StringComparison.Ordinal))
                {
                    device = devices[i];
                    return true;
                }
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    device = devices[i];
                    return true;
                }
            }

            return false;
        }

        private string[] GetDeviceNames()
        {
            try
            {
                if (_factory != null) return _factory.GetAvailableDevices() ?? Array.Empty<string>();

                // If _factory is null, fall through to platform Microphone API below.
            }
            catch
            {
                // Intentionally ignored - fallback to Unity microphone list.
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return Array.Empty<string>();
#else
            return Microphone.devices ?? Array.Empty<string>();
#endif
        }
    }
}
