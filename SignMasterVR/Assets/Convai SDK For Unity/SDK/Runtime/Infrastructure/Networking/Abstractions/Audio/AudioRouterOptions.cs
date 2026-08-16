using System;

namespace Convai.Infrastructure.Networking.Audio
{
    /// <summary>
    ///     Options for configuring audio routing.
    /// </summary>
    internal readonly struct AudioRouterOptions
    {
        /// <summary>Initial volume for routed audio (0.0 to 1.0).</summary>
        public float InitialVolume { get; }

        /// <summary>Whether to auto-enable audio when a track is subscribed.</summary>
        public bool AutoEnableOnSubscribe { get; }

        /// <summary>Default options with volume 1.0 and auto-enable on.</summary>
        public static AudioRouterOptions Default => new(1f);

        /// <summary>
        ///     Creates new AudioRouterOptions.
        /// </summary>
        public AudioRouterOptions(float initialVolume = 1f, bool autoEnableOnSubscribe = true)
        {
            InitialVolume = Math.Clamp(initialVolume, 0f, 1f);
            AutoEnableOnSubscribe = autoEnableOnSubscribe;
        }
    }
}
