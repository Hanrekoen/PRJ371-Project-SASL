using Convai.Infrastructure.Networking;
using UnityEngine;

namespace Convai.Runtime.Vision.Publishing
{
    internal readonly struct VisionPublishProfile
    {
        public VisionPublishProfile(int frameRate, int maxBitrate)
        {
            FrameRate = frameRate;
            MaxBitrate = maxBitrate;
        }

        public int FrameRate { get; }

        public int MaxBitrate { get; }

        public VideoPublishOptions ApplyTo(VideoPublishOptions options) =>
            options.WithMaxFrameRate(FrameRate).WithMaxBitrate(MaxBitrate);
    }

    internal static class VisionPublishProfileResolver
    {
        private static readonly VisionPublishProfile AutoCompatible = new(10, 750_000);
        private static readonly VisionPublishProfile HighResponsiveness = new(15, 1_000_000);
        private static readonly VisionPublishProfile LowOverhead = new(5, 350_000);

        internal static VisionPublishProfile Resolve(
            VisionPublishPolicy policy,
            int frameRateOverride,
            int maxBitrateOverride,
            RuntimePlatform platform)
        {
            VisionPublishProfile defaults = ResolveDefaults(policy, platform);

            int frameRate = frameRateOverride > 0 ? frameRateOverride : defaults.FrameRate;
            int maxBitrate = maxBitrateOverride > 0 ? maxBitrateOverride : defaults.MaxBitrate;

            return new VisionPublishProfile(frameRate, maxBitrate);
        }

        private static VisionPublishProfile ResolveDefaults(
            VisionPublishPolicy policy,
            RuntimePlatform platform)
        {
            VisionPublishProfile baseProfile = policy switch
            {
                VisionPublishPolicy.HighResponsiveness => HighResponsiveness,
                VisionPublishPolicy.LowOverhead => LowOverhead,
                _ => AutoCompatible
            };

            if (platform != RuntimePlatform.WebGLPlayer)
                return baseProfile;

            int frameRate = Mathf.Clamp(baseProfile.FrameRate, 1, 15);
            return new VisionPublishProfile(frameRate, baseProfile.MaxBitrate);
        }
    }
}
