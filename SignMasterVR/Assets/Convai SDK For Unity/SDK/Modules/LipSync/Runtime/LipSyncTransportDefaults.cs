using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Logging;
using Convai.Shared.Types;

namespace Convai.Modules.LipSync
{
    internal static class LipSyncTransportDefaults
    {
        public const string Provider = "neurosync";
        public const bool EnableChunking = true;
        public const int ChunkSize = 10;
        public const int OutputFps = 60;

        public static bool TryBuildForProfile(
            LipSyncProfileId profileId,
            IReadOnlyList<string> sourceBlendshapeNames,
            out LipSyncTransportOptions options,
            bool deliverChunksAhead = false)
        {
            if (!LipSyncProfileCatalog.TryGetProfile(profileId, out ConvaiLipSyncProfile profile))
            {
                options = LipSyncTransportOptions.Disabled;
                return false;
            }

            IReadOnlyList<string> transportSourceNames =
                ResolveSourceBlendshapeNamesForTransport(profile, sourceBlendshapeNames);

            options = new LipSyncTransportOptions(
                true,
                Provider,
                profile.ProfileId,
                profile.TransportFormat,
                transportSourceNames,
                EnableChunking,
                ChunkSize,
                OutputFps,
                LipSyncTransportOptions.DefaultFramesBufferDuration,
                deliverChunksAhead);
            return options.IsValid;
        }

        private static IReadOnlyList<string> ResolveSourceBlendshapeNamesForTransport(
            ConvaiLipSyncProfile profile,
            IReadOnlyList<string> sourceBlendshapeNames)
        {
            if (sourceBlendshapeNames == null || sourceBlendshapeNames.Count == 0)
                return sourceBlendshapeNames ?? Array.Empty<string>();

            // Resolve canonical source names by profile ID because the profile is already resolved.
            if (!LipSyncBuiltInProfileLibrary.TryGetSourceBlendshapeNames(
                    profile.ProfileId,
                    out IReadOnlyList<string> canonicalSourceNames) ||
                canonicalSourceNames == null ||
                canonicalSourceNames.Count == 0)
                return sourceBlendshapeNames;

            if (!HasExactSourceOrder(sourceBlendshapeNames, canonicalSourceNames))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CONVAI_DEBUG_LOGGING
                ConvaiLogger.Warning(
                    $"Source blendshape order for profile '{profile.ProfileId}' does not match canonical transport order. " +
                    "Using canonical order to prevent viseme index mismatch.",
                    LogCategory.LipSync);
#endif
            }

            return canonicalSourceNames;
        }

        private static bool HasExactSourceOrder(IReadOnlyList<string> current, IReadOnlyList<string> canonical)
        {
            if (ReferenceEquals(current, canonical)) return true;

            if (current == null || canonical == null || current.Count != canonical.Count) return false;

            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i], canonical[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
