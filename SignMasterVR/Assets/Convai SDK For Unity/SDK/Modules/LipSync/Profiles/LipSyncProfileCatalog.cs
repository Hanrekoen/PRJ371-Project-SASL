using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.LipSync.Profiles
{
    public static class LipSyncProfileCatalog
    {
        internal const string LegacyMetaHumanProfileId = "mha";

        /// <summary>
        ///     Where customer-supplied profile registries are discovered. This is a public extension
        ///     point — any <c>Resources/LipSync/ProfileRegistries/</c> folder in the project is
        ///     scanned — and must keep working.
        /// </summary>
        private const string RegistryResourcePath = "LipSync/ProfileRegistries";

        private static readonly Dictionary<string, ConvaiLipSyncProfile> ProfilesById =
            new(StringComparer.Ordinal);

        /// <summary>
        ///     The code-built built-in profiles this catalog created and must destroy. Empty while a
        ///     test override supplies the built-ins instead.
        /// </summary>
        private static readonly List<ConvaiLipSyncProfile> OwnedBuiltInProfiles = new();

        private static readonly List<ConvaiLipSyncProfile> OrderedProfiles = new();
        private static readonly List<string> ValidationIssues = new();
        private static readonly ReadOnlyCollection<ConvaiLipSyncProfile> ReadOnlyProfiles =
            OrderedProfiles.AsReadOnly();
        private static readonly ReadOnlyCollection<string> ReadOnlyValidationIssues = ValidationIssues.AsReadOnly();

        private static bool _initialized;
        private static readonly object InitLock = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DomainReload()
        {
            lock (InitLock)
            {
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        public static IReadOnlyList<ConvaiLipSyncProfile> GetProfiles()
        {
            EnsureInitialized();
            return ReadOnlyProfiles;
        }

        public static bool TryGetProfile(LipSyncProfileId profileId, out ConvaiLipSyncProfile profile)
        {
            EnsureInitialized();
            if (ProfilesById.TryGetValue(profileId.Value, out profile)) return true;

            return string.Equals(profileId.Value, LegacyMetaHumanProfileId, StringComparison.Ordinal) &&
                   ProfilesById.TryGetValue(LipSyncProfileId.MetaHumanValue, out profile);
        }

        public static bool TryGetProfile(string rawProfileId, out ConvaiLipSyncProfile profile) =>
            TryGetProfile(new LipSyncProfileId(rawProfileId), out profile);

        public static IReadOnlyList<string> GetSourceBlendshapeNamesOrEmpty(LipSyncProfileId profileId) =>
            LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(CanonicalizeProfileId(profileId));

        internal static LipSyncProfileId CanonicalizeProfileId(LipSyncProfileId profileId)
        {
            EnsureInitialized();
            if (ProfilesById.ContainsKey(profileId.Value)) return profileId;

            return string.Equals(profileId.Value, LegacyMetaHumanProfileId, StringComparison.Ordinal) &&
                   ProfilesById.ContainsKey(LipSyncProfileId.MetaHumanValue)
                ? LipSyncProfileId.MetaHuman
                : profileId;
        }

        public static IReadOnlyList<string> GetValidationIssues()
        {
            EnsureInitialized();
            return ReadOnlyValidationIssues;
        }

        private static void EnsureInitialized()
        {
            if (Volatile.Read(ref _initialized)) return;

            lock (InitLock)
            {
                if (Volatile.Read(ref _initialized)) return;

                ClearCaches();

                // Built-ins come from code and cannot fail to load. A test override replaces that
                // source entirely — including with nothing — so fixtures stay isolated from the
                // shipped set rather than colliding with it.
                ConvaiLipSyncProfileRegistry builtInRegistry = null;
#if UNITY_EDITOR
                if (_registryOverridesActive)
                {
                    builtInRegistry = _builtInRegistryOverrideForTests;
                    if (builtInRegistry != null) RegisterRegistryProfiles(builtInRegistry);
                }
                else
#endif
                {
                    RegisterBuiltInProfiles();
                }

                List<ConvaiLipSyncProfileRegistry> extensionRegistries =
                    ResolveExtensionRegistries(builtInRegistry);
                foreach (ConvaiLipSyncProfileRegistry registry in extensionRegistries)
                    RegisterRegistryProfiles(registry);

                RebuildFormatMapAndOrderedList();
                OrderedProfiles.Sort((a, b) => string.CompareOrdinal(a.ProfileId.Value, b.ProfileId.Value));
                Volatile.Write(ref _initialized, true);
            }
        }

        /// <summary>
        ///     Registers the SDK's own profiles, built in code so they cannot be missing. The
        ///     instances are owned by this catalog and destroyed in <see cref="ClearCaches" />.
        /// </summary>
        private static void RegisterBuiltInProfiles()
        {
            ConvaiLipSyncProfile[] builtIns = LipSyncBuiltInProfiles.CreateAll();
            for (int i = 0; i < builtIns.Length; i++)
            {
                OwnedBuiltInProfiles.Add(builtIns[i]);
                RegisterProfile(builtIns[i], "the built-in profiles");
            }
        }

        private static void RegisterRegistryProfiles(ConvaiLipSyncProfileRegistry registry)
        {
            IReadOnlyList<ConvaiLipSyncProfile> profiles = registry.Profiles;
            if (profiles == null || profiles.Count == 0) return;

            string sourceName = GetRegistryDisplayName(registry);
            for (int i = 0; i < profiles.Count; i++)
                RegisterProfile(profiles[i], sourceName);
        }

        /// <summary>
        ///     Validates one profile and adds it to the catalog, reporting where it came from.
        ///     Shared by the built-ins and by every registry so a defect is described the same way
        ///     wherever it originates.
        /// </summary>
        private static void RegisterProfile(ConvaiLipSyncProfile profile, string sourceName)
        {
            if (profile == null) return;

            LipSyncProfileId profileId = profile.ProfileId;
            if (!profileId.IsValid)
            {
                AddValidationIssue($"Skipping profile in '{sourceName}' because profile id is empty.");
                return;
            }

            if (!profile.IsValid)
            {
                string issue = profile.DescribeValidationIssue();
                AddValidationIssue($"Skipping invalid profile '{profileId}' in '{sourceName}': {issue}");
                return;
            }

            if (ProfilesById.TryGetValue(profileId.Value, out ConvaiLipSyncProfile existing))
            {
                AddValidationIssue(
                    $"Duplicate profile id '{profileId}' found. Overriding '{existing.name}' with '{profile.name}'.");
            }

            ProfilesById[profileId.Value] = profile;
        }

        private static void RebuildFormatMapAndOrderedList()
        {
            OrderedProfiles.Clear();
            var profilesByTransportFormat = new Dictionary<string, ConvaiLipSyncProfile>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, ConvaiLipSyncProfile> pair in ProfilesById)
            {
                ConvaiLipSyncProfile profile = pair.Value;
                OrderedProfiles.Add(profile);

                string transportFormat = profile.TransportFormat;
                if (string.IsNullOrWhiteSpace(transportFormat)) continue;

                if (profilesByTransportFormat.TryGetValue(transportFormat, out ConvaiLipSyncProfile existing))
                {
                    AddValidationIssue(
                        $"Duplicate transport format '{transportFormat}' mapped to both '{existing.name}' and '{profile.name}'. Last one wins.");
                }

                profilesByTransportFormat[transportFormat] = profile;
            }
        }

        /// <summary>
        ///     Collects the customer-supplied registries. Every
        ///     <c>Resources/LipSync/ProfileRegistries/</c> folder in the project is scanned; this is
        ///     the SDK's extension point for adding profiles and is deliberately unchanged by the
        ///     move of the built-ins into code.
        /// </summary>
        /// <param name="builtInRegistry">
        ///     A registry already registered as the built-in source, excluded here so it is not
        ///     applied twice. Null in normal operation, where the built-ins come from code and are
        ///     never discovered by the scan.
        /// </param>
        private static List<ConvaiLipSyncProfileRegistry> ResolveExtensionRegistries(
            ConvaiLipSyncProfileRegistry builtInRegistry)
        {
            List<ConvaiLipSyncProfileRegistry> result = new();

#if UNITY_EDITOR
            if (_extensionRegistryOverridesForTests != null)
            {
                for (int i = 0; i < _extensionRegistryOverridesForTests.Count; i++)
                {
                    ConvaiLipSyncProfileRegistry registry = _extensionRegistryOverridesForTests[i];
                    if (registry != null) result.Add(registry);
                }

                SortRegistries(result);
                return result;
            }
#endif

            ConvaiLipSyncProfileRegistry[] registries =
                Resources.LoadAll<ConvaiLipSyncProfileRegistry>(RegistryResourcePath);

            for (int i = 0; i < registries.Length; i++)
            {
                ConvaiLipSyncProfileRegistry registry = registries[i];
                if (registry != null && !ReferenceEquals(registry, builtInRegistry))
                    result.Add(registry);
            }

            SortRegistries(result);
            return result;
        }

        private static void SortRegistries(List<ConvaiLipSyncProfileRegistry> registries)
        {
            registries.Sort((a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                if (priorityCompare != 0) return priorityCompare;

                return string.CompareOrdinal(GetRegistrySortKey(a), GetRegistrySortKey(b));
            });
        }

        private static string GetRegistrySortKey(ConvaiLipSyncProfileRegistry registry) =>
            registry != null ? registry.name ?? string.Empty : string.Empty;

        private static string GetRegistryDisplayName(ConvaiLipSyncProfileRegistry registry) =>
            registry != null ? registry.name : "(null)";

        private static void ClearCaches()
        {
            ProfilesById.Clear();
            OrderedProfiles.Clear();
            ValidationIssues.Clear();

            // The built-ins are created by this catalog, so it has to destroy them. Leaking them
            // would accumulate a hidden ScriptableObject per domain reload.
            for (int i = 0; i < OwnedBuiltInProfiles.Count; i++)
            {
                ConvaiLipSyncProfile owned = OwnedBuiltInProfiles[i];
                if (owned == null) continue;
#if UNITY_EDITOR
                if (!UnityEngine.Application.isPlaying) UnityEngine.Object.DestroyImmediate(owned);
                else UnityEngine.Object.Destroy(owned);
#else
                UnityEngine.Object.Destroy(owned);
#endif
            }

            OwnedBuiltInProfiles.Clear();
        }

        private static void AddValidationIssue(string message)
        {
            ValidationIssues.Add(message);
            ConvaiLogger.Warning(message, LogCategory.LipSync);
        }

#if UNITY_EDITOR
        private static ConvaiLipSyncProfileRegistry _builtInRegistryOverrideForTests;
        private static IReadOnlyList<ConvaiLipSyncProfileRegistry> _extensionRegistryOverridesForTests;

        /// <summary>
        ///     True once a test has supplied registry overrides. Kept separate from the override
        ///     value being null, because "the test wants no built-ins at all" and "no test has
        ///     spoken, use the shipped built-ins" are different states and a null check cannot tell
        ///     them apart. Fixtures that build their own profile set depend on the first meaning.
        /// </summary>
        private static bool _registryOverridesActive;

        internal static void InvalidateCachesForEditor()
        {
            lock (InitLock)
            {
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        public static void SetRegistryOverridesForTests(
            ConvaiLipSyncProfileRegistry builtInRegistry,
            IReadOnlyList<ConvaiLipSyncProfileRegistry> extensionRegistries)
        {
            lock (InitLock)
            {
                _builtInRegistryOverrideForTests = builtInRegistry;
                _extensionRegistryOverridesForTests = extensionRegistries;
                _registryOverridesActive = true;
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        public static void ClearCachesForTests()
        {
            lock (InitLock)
            {
                _builtInRegistryOverrideForTests = null;
                _extensionRegistryOverridesForTests = null;
                _registryOverridesActive = false;
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }
#endif
    }
}
