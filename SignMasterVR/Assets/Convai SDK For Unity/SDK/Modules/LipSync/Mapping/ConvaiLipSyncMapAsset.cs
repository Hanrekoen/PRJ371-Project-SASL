using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Data-driven mapping from source blendshape channels to one or more target mesh blendshapes.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiLipSyncMapAsset", menuName = "Convai/Lip Sync/Lip Sync Map")]
    public sealed class ConvaiLipSyncMapAsset : ScriptableObject
    {
        /// <summary>Lower bound for per-entry response curve exponents.</summary>
        public const float MinCurveExponent = 0.25f;

        /// <summary>Upper bound for per-entry response curve exponents.</summary>
        public const float MaxCurveExponent = 4f;

        private static readonly IReadOnlyList<string> EmptySourceBlendshapeNames = Array.Empty<string>();

        [SerializeField] private string _targetProfileId = LipSyncProfileId.ARKitValue;
        [SerializeField] [TextArea(2, 4)] private string _description;
        [SerializeField] private List<BlendshapeMappingEntry> _mappings = new();
        [SerializeField] [Range(0f, 3f)] private float _globalMultiplier = 1f;
        [SerializeField] [Range(-1f, 1f)] private float _globalOffset;
        [SerializeField] private bool _allowUnmappedPassthrough = true;
        private readonly HashSet<string> _seenNamesBuffer = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sourceBlendshapeNamesBuffer = new();

        private readonly Dictionary<string, BlendshapeMappingEntry> _sourceToEntryMap =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _isCacheValid;
        private ReadOnlyCollection<BlendshapeMappingEntry> _readOnlyMappings;
        private IReadOnlyList<string> _sourceBlendshapeNames;

        /// <summary>Profile id this map targets.</summary>
        public LipSyncProfileId TargetProfileId => new(_targetProfileId);

        /// <summary>Optional designer description for this map.</summary>
        public string Description => _description;

        /// <summary>Raw mapping entries defined on this asset.</summary>
        public IReadOnlyList<BlendshapeMappingEntry> Mappings =>
            _readOnlyMappings ??= (_mappings ??= new List<BlendshapeMappingEntry>()).AsReadOnly();

        /// <summary>Global multiplier applied when entries do not ignore global modifiers.</summary>
        public float GlobalMultiplier => _globalMultiplier;

        /// <summary>Global additive offset applied when entries do not ignore global modifiers.</summary>
        public float GlobalOffset => _globalOffset;

        /// <summary>Whether source channels without explicit entries should pass through by name.</summary>
        public bool AllowUnmappedPassthrough => _allowUnmappedPassthrough;

        private void OnValidate()
        {
            _targetProfileId = LipSyncProfileId.Normalize(_targetProfileId);
            _readOnlyMappings = null;
            InvalidateCache();
        }

        /// <summary>
        ///     Returns unique source channel names currently present in mapping entries.
        /// </summary>
        public IReadOnlyList<string> GetSourceBlendshapeNames()
        {
            EnsureCacheValid();
            return _sourceBlendshapeNames ?? EmptySourceBlendshapeNames;
        }

        /// <summary>
        ///     Resolves target blendshape names for a source channel after entry and passthrough rules.
        /// </summary>
        public IReadOnlyList<string> GetTargetNames(string sourceBlendshape)
        {
            EnsureCacheValid();
            if (_sourceToEntryMap.TryGetValue(sourceBlendshape, out BlendshapeMappingEntry entry))
            {
                if (!entry.enabled) return Array.Empty<string>();

                if (entry.targetNames != null && entry.targetNames.Count > 0) return entry.targetNames;

                // Allocates a single-element array during route compilation.
                // Compiled routes are cached, so this does not affect the runtime hot path.
                return new[] { sourceBlendshape };
            }

            // Allocates on unmapped passthrough during route compilation only.
            return _allowUnmappedPassthrough ? new[] { sourceBlendshape } : Array.Empty<string>();
        }

        /// <summary>
        ///     Determines whether a source channel is currently enabled for routing.
        /// </summary>
        public bool IsEnabled(string sourceBlendshape)
        {
            EnsureCacheValid();
            return !_sourceToEntryMap.TryGetValue(sourceBlendshape, out BlendshapeMappingEntry entry)
                ? _allowUnmappedPassthrough
                : entry.enabled;
        }

        /// <summary>
        ///     Attempts to retrieve a read-only snapshot of the mapping entry for the given source blendshape.
        ///     The snapshot is a value-type copy, so callers cannot mutate internal state through it.
        /// </summary>
        public bool TryGetEntry(string sourceBlendshape, out BlendshapeMappingSnapshot snapshot)
        {
            EnsureCacheValid();
            if (_sourceToEntryMap.TryGetValue(sourceBlendshape, out BlendshapeMappingEntry entry))
            {
                snapshot = new BlendshapeMappingSnapshot(entry);
                return true;
            }

            snapshot = default;
            return false;
        }

        /// <summary>
        ///     Rebuilds this asset as a fail-safe disabled mapping for the given profile.
        /// </summary>
        public void InitializeAsSafeDisabledProfile(LipSyncProfileId profileId)
        {
            _targetProfileId = profileId.Value;
            _description = $"Auto-generated safe-disabled map for {profileId}.";
            _globalMultiplier = 1f;
            _globalOffset = 0f;
            _allowUnmappedPassthrough = false;

            _mappings.Clear();
            IReadOnlyList<string> names = ResolveSourceBlendshapeNames(profileId);
            for (int i = 0; i < names.Count; i++)
            {
                _mappings.Add(new BlendshapeMappingEntry
                {
                    sourceBlendshape = names[i],
                    targetNames = new List<string>(),
                    multiplier = 1f,
                    offset = 0f,
                    curveExponent = 1f,
                    enabled = false,
                    useOverrideValue = false,
                    overrideValue = 0f,
                    ignoreGlobalModifiers = false,
                    clampMinValue = 0f,
                    clampMaxValue = 1f
                });
            }

            InvalidateCache();
        }

        /// <summary>
        ///     Creates a runtime-only fail-safe disabled map instance for fallback scenarios.
        /// </summary>
        public static ConvaiLipSyncMapAsset CreateSafeDisabledMap(LipSyncProfileId profileId)
        {
            var map = CreateInstance<ConvaiLipSyncMapAsset>();
            map.hideFlags = HideFlags.HideAndDontSave;
            map.InitializeAsSafeDisabledProfile(profileId);
            return map;
        }

        private void InvalidateCache() => _isCacheValid = false;

        private void EnsureCacheValid()
        {
            if (_isCacheValid) return;

            _sourceToEntryMap.Clear();
            _sourceBlendshapeNamesBuffer.Clear();
            _seenNamesBuffer.Clear();

            foreach (BlendshapeMappingEntry entry in _mappings)
            {
                if (string.IsNullOrWhiteSpace(entry.sourceBlendshape)) continue;

                string trimmed = entry.sourceBlendshape.Trim();
                _sourceToEntryMap[trimmed] = entry;

                if (_seenNamesBuffer.Add(trimmed)) _sourceBlendshapeNamesBuffer.Add(trimmed);
            }

            _sourceBlendshapeNames = _sourceBlendshapeNamesBuffer.Count > 0
                ? Array.AsReadOnly(_sourceBlendshapeNamesBuffer.ToArray())
                : EmptySourceBlendshapeNames;
            _isCacheValid = true;
        }

        private static IReadOnlyList<string> ResolveSourceBlendshapeNames(LipSyncProfileId profileId) =>
            LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(profileId);

        [Serializable]
        /// <summary>
        /// Mutable authoring entry that defines how one source channel maps to target blendshapes.
        /// </summary>
        public class BlendshapeMappingEntry
        {
            /// <summary>Source channel name emitted by transport payloads.</summary>
            public string sourceBlendshape;

            /// <summary>Target blendshape names to receive the mapped value.</summary>
            public List<string> targetNames = new();

            /// <summary>Per-entry multiplier applied before clamping.</summary>
            [Range(0f, 5f)] public float multiplier = 1f;

            /// <summary>Per-entry additive offset applied before clamping.</summary>
            [Range(-1f, 1f)] public float offset;

            /// <summary>
            ///     Response curve exponent applied to the normalized source value before gain.
            ///     1 = linear. Below 1 lifts the mid-range so subtle motion reads clearly
            ///     (more expressive articulation); above 1 suppresses low-level noise.
            /// </summary>
            [Range(MinCurveExponent, MaxCurveExponent)] public float curveExponent = 1f;

            /// <summary>Whether this mapping entry is active.</summary>
            public bool enabled = true;

            /// <summary>Whether to use <see cref="overrideValue" /> instead of source input.</summary>
            public bool useOverrideValue;

            /// <summary>Constant value used when <see cref="useOverrideValue" /> is enabled.</summary>
            [Range(0f, 1f)] public float overrideValue;

            /// <summary>Skips global multiplier/offset when enabled.</summary>
            public bool ignoreGlobalModifiers;

            /// <summary>Lower clamp bound for final normalized value.</summary>
            [Range(0f, 1f)] public float clampMinValue;

            /// <summary>Upper clamp bound for final normalized value.</summary>
            [Range(0f, 1f)] public float clampMaxValue = 1f;
        }

        /// <summary>
        ///     Read-only snapshot of a <see cref="BlendshapeMappingEntry" />.
        ///     Returned by <see cref="TryGetEntry" /> to prevent external mutation of
        ///     cached internal state without going through the proper invalidation path.
        /// </summary>
        public readonly struct BlendshapeMappingSnapshot
        {
            public bool Enabled { get; }
            public bool UseOverrideValue { get; }
            public float OverrideValue { get; }
            public bool IgnoreGlobalModifiers { get; }
            public float Multiplier { get; }
            public float Offset { get; }
            public float CurveExponent { get; }
            public float ClampMinValue { get; }
            public float ClampMaxValue { get; }

            /// <summary>Read-only view of the target blendshape names. May be empty but never null.</summary>
            public IReadOnlyList<string> TargetNames { get; }

            internal BlendshapeMappingSnapshot(BlendshapeMappingEntry entry)
            {
                Enabled = entry.enabled;
                UseOverrideValue = entry.useOverrideValue;
                OverrideValue = entry.overrideValue;
                IgnoreGlobalModifiers = entry.ignoreGlobalModifiers;
                Multiplier = entry.multiplier;
                Offset = entry.offset;
                // Assets serialized before curveExponent existed deserialize it as 0;
                // treat anything out of range as linear.
                CurveExponent = entry.curveExponent >= MinCurveExponent && entry.curveExponent <= MaxCurveExponent
                    ? entry.curveExponent
                    : 1f;
                ClampMinValue = entry.clampMinValue;
                ClampMaxValue = entry.clampMaxValue;
                TargetNames = entry.targetNames != null
                    ? entry.targetNames.AsReadOnly()
                    : Array.Empty<string>();
            }
        }

    }
}
