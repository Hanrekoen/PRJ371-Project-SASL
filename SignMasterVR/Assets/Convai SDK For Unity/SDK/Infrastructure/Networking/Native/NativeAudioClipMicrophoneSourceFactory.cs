using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Creates <see cref="NativeAudioClipMicrophoneSource" /> instances backed by a prerecorded clip.
    /// </summary>
    public sealed class NativeAudioClipMicrophoneSourceFactory : IMicrophoneSourceFactory
    {
        private readonly AudioClip _clip;
        private readonly string _deviceName;

        public NativeAudioClipMicrophoneSourceFactory(AudioClip clip, string deviceName = null)
        {
            _clip = clip ?? throw new ArgumentNullException(nameof(clip));
            _deviceName = string.IsNullOrWhiteSpace(deviceName) ? clip.name : deviceName.Trim();
        }

        public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null) =>
            new NativeAudioClipMicrophoneSource(_clip, hostObject, _deviceName);

        public string[] GetAvailableDevices() => new[] { _deviceName };
    }
}
