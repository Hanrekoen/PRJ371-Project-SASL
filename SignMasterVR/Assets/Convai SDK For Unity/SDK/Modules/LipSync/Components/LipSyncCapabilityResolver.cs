using System.Collections.Generic;
using Convai.Modules.LipSync.Profiles;
using Convai.Shared.Types;

namespace Convai.Modules.LipSync
{
    internal static class LipSyncCapabilityResolver
    {
        public static ConvaiLipSyncMapAsset ResolveEffectiveMapping(in LipSyncRuntimeConfig config) =>
            LipSyncDefaultMappingResolver.ResolveEffective(config.Mapping, config.ProfileId, out _);

        public static bool TryGetTransportOptions(in LipSyncRuntimeConfig config,
            out LipSyncTransportOptions options)
        {
            ConvaiLipSyncMapAsset effectiveMapping = ResolveEffectiveMapping(config);
            IReadOnlyList<string> sourceNames = effectiveMapping != null
                ? effectiveMapping.GetSourceBlendshapeNames()
                : LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(config.ProfileId);

            if (sourceNames == null || sourceNames.Count == 0)
                sourceNames = LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(config.ProfileId);

            bool built = LipSyncTransportDefaults.TryBuildForProfile(
                config.ProfileId,
                sourceNames,
                out options,
                config.DeliverChunksAhead);
            return built && options.IsValid;
        }
    }
}
