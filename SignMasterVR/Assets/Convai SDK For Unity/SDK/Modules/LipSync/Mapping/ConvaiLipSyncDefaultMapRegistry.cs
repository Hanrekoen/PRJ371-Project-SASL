using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Shared.Compatibility;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    [CreateAssetMenu(
        fileName = "LipSyncDefaultMapRegistry",
        menuName = "Convai/Lip Sync/Default Map Registry")]
    public class ConvaiLipSyncDefaultMapRegistry : ScriptableObject
    {
        [SerializeField] private List<ProfileDefaultMapEntry> _entries = new();

        private readonly Dictionary<(string Profile, string Rig), ConvaiLipSyncMapAsset> _cache = new();
        private readonly List<string> _validationIssues = new();
        private int _cacheFingerprint;
        private bool _isCacheValid;
        private ReadOnlyCollection<ProfileDefaultMapEntry> _readOnlyEntries;

        public IReadOnlyList<ProfileDefaultMapEntry> Entries =>
            _readOnlyEntries ??= (_entries ??= new List<ProfileDefaultMapEntry>()).AsReadOnly();

        public IReadOnlyList<string> ValidationIssues
        {
            get
            {
                EnsureCache();
                return _validationIssues;
            }
        }

        private void OnValidate()
        {
            if (_entries != null)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    ProfileDefaultMapEntry entry = _entries[i];
                    if (entry == null) continue;

                    entry.NormalizeProfileId();
                }
            }

            _readOnlyEntries = null;
            InvalidateCache();
        }

        /// <summary>
        ///     The default map for a transport, for a character whose rig speaks that transport's own
        ///     blendshape vocabulary. Unchanged in meaning and result from before rig keys existed.
        /// </summary>
        public ConvaiLipSyncMapAsset GetForProfile(LipSyncProfileId profileId) =>
            GetForProfile(profileId, default);

        /// <summary>
        ///     The default map for a transport on a character whose mesh uses
        ///     <paramref name="rigProfileId" />'s blendshape vocabulary, falling back to the
        ///     transport's plain default when no rig-specific entry exists.
        /// </summary>
        /// <remarks>
        ///     Both arguments matter: a backend sending ARKit to a Character Creator 4 rig needs a
        ///     different map from one sending ARKit to an ARKit-named rig, and the two entries share
        ///     a transport. Passing an invalid <paramref name="rigProfileId" /> asks for the plain
        ///     default, which is what every caller written before rig keys did.
        /// </remarks>
        public ConvaiLipSyncMapAsset GetForProfile(LipSyncProfileId profileId, LipSyncProfileId rigProfileId)
        {
            EnsureCache();

            if (TryResolve(profileId, rigProfileId, out ConvaiLipSyncMapAsset map)) return map;

            LipSyncProfileId canonical = LipSyncProfileCatalog.CanonicalizeProfileId(profileId);
            return canonical != profileId && TryResolve(canonical, rigProfileId, out ConvaiLipSyncMapAsset aliased)
                ? aliased
                : null;
        }

        private bool TryResolve(
            LipSyncProfileId profileId, LipSyncProfileId rigProfileId, out ConvaiLipSyncMapAsset map)
        {
            // A rig-specific entry wins; otherwise the transport's plain default answers.
            if (rigProfileId.IsValid &&
                _cache.TryGetValue(BuildKey(profileId, rigProfileId), out map)) return true;

            return _cache.TryGetValue(BuildKey(profileId, default), out map);
        }

        /// <summary>
        ///     Cache key for a (transport, rig) pair. A tuple rather than a joined string: no
        ///     separator character has to be assumed absent from a profile id, so two distinct pairs
        ///     cannot collide on one key.
        /// </summary>
        private static (string Profile, string Rig) BuildKey(
            LipSyncProfileId profileId, LipSyncProfileId rigProfileId) =>
            (profileId.Value, rigProfileId.IsValid ? rigProfileId.Value : string.Empty);

        private void EnsureCache()
        {
            int nextFingerprint = ComputeCacheFingerprint();
            if (_isCacheValid && nextFingerprint == _cacheFingerprint) return;

            RebuildCache(nextFingerprint);
        }

        private void RebuildCache(int nextFingerprint)
        {
            _cache.Clear();
            _validationIssues.Clear();

            if (_entries == null)
            {
                _cacheFingerprint = nextFingerprint;
                _isCacheValid = true;
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                ProfileDefaultMapEntry entry = _entries[i];
                if (entry == null)
                {
                    _validationIssues.Add($"Entry #{i + 1} is null and was ignored.");
                    continue;
                }

                ConvaiLipSyncMapAsset map = entry.DefaultMap;
                if (map == null)
                {
                    LipSyncProfileId fallbackProfile = entry.ResolveProfileId();
                    string fallbackName = fallbackProfile.IsValid ? fallbackProfile.Value : "(empty)";
                    _validationIssues.Add($"Entry #{i + 1} has no map assigned (profile: {fallbackName}).");
                    continue;
                }

                LipSyncProfileId resolvedProfileId = entry.ResolveProfileId();
                if (!resolvedProfileId.IsValid)
                {
                    _validationIssues.Add($"Entry #{i + 1} map '{map.name}' has no valid target profile id.");
                    continue;
                }

                LipSyncProfileId rigProfileId = entry.RigProfileId;
                (string Profile, string Rig) cacheKey = BuildKey(resolvedProfileId, rigProfileId);

                if (_cache.TryGetValue(cacheKey, out ConvaiLipSyncMapAsset existing))
                {
                    string existingName = existing != null ? existing.name : "(null)";
                    string where = rigProfileId.IsValid
                        ? $"profile '{resolvedProfileId.Value}' on a '{rigProfileId.Value}' rig"
                        : $"profile '{resolvedProfileId.Value}'";
                    _validationIssues.Add(
                        $"Duplicate default map for {where}: '{existingName}' was overridden by '{map.name}'.");
                }

                if (!entry.UsesMapTargetProfile)
                {
                    _validationIssues.Add(
                        $"Entry #{i + 1} map '{map.name}' has invalid target profile; using fallback entry id '{resolvedProfileId.Value}'.");
                }

                _cache[cacheKey] = map;
            }

            _cacheFingerprint = nextFingerprint;
            _isCacheValid = true;
        }

        private int ComputeCacheFingerprint()
        {
            unchecked
            {
                int hash = 17;
                int count = _entries != null ? _entries.Count : 0;
                hash = (hash * 31) + count;

                if (_entries == null) return hash;

                for (int i = 0; i < _entries.Count; i++)
                {
                    ProfileDefaultMapEntry entry = _entries[i];
                    hash = (hash * 31) + (entry != null ? entry.ComputeFingerprint() : 0);
                }

                return hash;
            }
        }

        private void InvalidateCache() => _isCacheValid = false;

        [Serializable]
        public sealed class ProfileDefaultMapEntry
        {
            [SerializeField] private string _profileId = string.Empty;
            [SerializeField] private ConvaiLipSyncMapAsset _defaultMap;

            /// <summary>
            ///     Which blendshape vocabulary the character's mesh uses, when that differs from the
            ///     transport's own. Empty — the normal case, and what every entry authored before
            ///     this field existed deserializes to — means "the rig speaks the same vocabulary as
            ///     the transport", which is the default for that transport.
            /// </summary>
            /// <remarks>
            ///     The transport a character receives and the blendshape names on its mesh are two
            ///     independent facts, and the right map depends on both. A backend sending ARKit to
            ///     an ARKit-named rig and one sending ARKit to a Character Creator 4 rig need
            ///     different maps while sharing a transport, so keying entries by transport alone
            ///     could only ever express one of them.
            /// </remarks>
            [SerializeField] private string _rigProfileId = string.Empty;

            public LipSyncProfileId ProfileId => ResolveProfileId();
            public ConvaiLipSyncMapAsset DefaultMap => _defaultMap;
            public bool UsesMapTargetProfile => ResolveMapTargetProfileId().IsValid;

            /// <summary>
            ///     The rig vocabulary this entry is for, or an invalid id when it is the transport's
            ///     default entry.
            /// </summary>
            public LipSyncProfileId RigProfileId => new(_rigProfileId);

            /// <summary>True when this entry is the plain default for its transport.</summary>
            public bool IsTransportDefault => !RigProfileId.IsValid;

            public void NormalizeProfileId()
            {
                _profileId = LipSyncProfileId.Normalize(_profileId);
                LipSyncProfileId mapTargetProfile = ResolveMapTargetProfileId();
                if (mapTargetProfile.IsValid) _profileId = mapTargetProfile.Value;

                _rigProfileId = LipSyncProfileId.Normalize(_rigProfileId);

                // A rig key equal to the transport says nothing; it is the default entry.
                if (string.Equals(_rigProfileId, _profileId, StringComparison.Ordinal))
                    _rigProfileId = string.Empty;
            }

            public LipSyncProfileId ResolveProfileId()
            {
                LipSyncProfileId mapTargetProfile = ResolveMapTargetProfileId();
                if (mapTargetProfile.IsValid) return mapTargetProfile;

                return new LipSyncProfileId(_profileId);
            }

            internal int ComputeFingerprint()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + (_defaultMap != null ? ConvaiObjectId.Of(_defaultMap).GetHashCode() : 0);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ResolveProfileId().Value);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(LipSyncProfileId.Normalize(_profileId));
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(LipSyncProfileId.Normalize(_rigProfileId));
                    return hash;
                }
            }

            private LipSyncProfileId ResolveMapTargetProfileId() =>
                _defaultMap != null ? _defaultMap.TargetProfileId : default;
        }
    }
}
