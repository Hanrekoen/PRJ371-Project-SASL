using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal static class LipSyncDefaultMappingResolver
    {
        private const string RegistryResourcePath = "LipSync/DefaultMaps/LipSyncDefaultMapRegistry";

        private static readonly Dictionary<string, ConvaiLipSyncMapAsset> SafeDisabledMapCache =
            new(StringComparer.Ordinal);

        private static ConvaiLipSyncDefaultMapRegistry _registry;

#if UNITY_EDITOR
        private static ConvaiLipSyncDefaultMapRegistry _registryOverrideForTests;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DomainReload()
        {
            _registry = null;
            SafeDisabledMapCache.Clear();
        }

        public static ConvaiLipSyncMapAsset ResolveEffective(
            ConvaiLipSyncMapAsset assigned,
            LipSyncProfileId profileId,
            out bool usedSafeDisabledFallback)
        {
            LipSyncProfileId canonicalProfile = LipSyncProfileCatalog.CanonicalizeProfileId(profileId);
            if (assigned != null &&
                LipSyncProfileCatalog.CanonicalizeProfileId(assigned.TargetProfileId) == canonicalProfile)
            {
                usedSafeDisabledFallback = false;
                return assigned;
            }

            ConvaiLipSyncMapAsset profileDefault = ResolveProfileDefault(canonicalProfile);
            if (profileDefault != null)
            {
                usedSafeDisabledFallback = false;
                return profileDefault;
            }

            usedSafeDisabledFallback = true;
            return ResolveSafeDisabled(canonicalProfile);
        }

        public static ConvaiLipSyncMapAsset ResolveProfileDefault(LipSyncProfileId profileId)
        {
            ConvaiLipSyncDefaultMapRegistry registry = GetRegistry();
            return registry != null ? registry.GetForProfile(profileId) : null;
        }

        public static ConvaiLipSyncDefaultMapRegistry GetRegistry()
        {
#if UNITY_EDITOR
            if (_registryOverrideForTests != null) return _registryOverrideForTests;
#endif
            if (_registry != null) return _registry;

            _registry = Resources.Load<ConvaiLipSyncDefaultMapRegistry>(RegistryResourcePath);
            return _registry;
        }

        private static ConvaiLipSyncMapAsset ResolveSafeDisabled(LipSyncProfileId profileId)
        {
            if (SafeDisabledMapCache.TryGetValue(profileId.Value, out ConvaiLipSyncMapAsset cached) &&
                cached != null) return cached;

            var created = ConvaiLipSyncMapAsset.CreateSafeDisabledMap(profileId);
            SafeDisabledMapCache[profileId.Value] = created;
            return created;
        }

#if UNITY_EDITOR
        internal static void SetRegistryOverrideForTests(ConvaiLipSyncDefaultMapRegistry registry) =>
            _registryOverrideForTests = registry;

        internal static void ClearCachesForTests()
        {
            _registry = null;
            _registryOverrideForTests = null;
            SafeDisabledMapCache.Clear();
        }
#endif
    }
}
