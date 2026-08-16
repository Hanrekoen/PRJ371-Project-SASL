using System;
using System.Collections.Generic;
using Convai.Runtime.Animation;
using Convai.Shared.Compatibility;
using Unity.Profiling;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Applies lip sync output values to SkinnedMeshRenderer blendshapes
    ///     with compiled mapping and delta-skip optimization.
    /// </summary>
    internal sealed class SkinnedMeshBlendshapeSink : IFacialBlendshapeSource
    {
        private static readonly ProfilerMarker ApplyMarker = new("Convai.LipSync.Sink.Apply");
        private static readonly ProfilerMarker CompileMarker = new("Convai.LipSync.Sink.Compile");

        private readonly Component _context;
        private readonly List<MeshEntry> _entries = new();
        private readonly List<CompiledBlendshapeMapping.TargetBinding> _resolvedTargetBuffer = new();

        private readonly List<CompiledBlendshapeMapping.SourceRoute> _routesBuildBuffer = new();
        private readonly string[] _singleNameBuffer = new string[1];
        private readonly HashSet<long> _uniqueMeshIds = new();
        private readonly HashSet<long> _uniqueTargetKeys = new();
        private readonly Dictionary<BlendshapeTargetKey, int> _submittedTargetToIndex = new();
        private readonly List<BlendshapeTargetKey> _submittedTargets = new();
        private readonly List<float> _submittedWeights = new();

        private CompiledBlendshapeMapping _compiled;
        private bool _compiledBypassMappingModifiers;
        private int _compiledChannelCount = -1;
        private int _compiledChannelHash;
        private ConvaiLipSyncMapAsset _compiledForMapping;
        private int _compiledForMeshRevision = -1;
        private bool _bypassMappingModifiers;
        private int _meshRevision;
        private ILipSyncBlendshapeOutputSink _output;

        public SkinnedMeshBlendshapeSink(Component context)
        {
            _context = context;
        }

        public Component SourceComponent => _context;
        public string SourceName => "LipSync";

        /// <summary>
        ///     Initializes the sink mesh cache and marks the compiled mapping state as dirty.
        /// </summary>
        public void Initialize(IReadOnlyList<SkinnedMeshRenderer> meshes, ConvaiLipSyncMapAsset mapping,
            bool bypassMappingModifiers = false)
        {
            _output?.SetSpeechActive(false);
            _output?.ResetDrivenTargets();
            _meshRevision++;
            _bypassMappingModifiers = bypassMappingModifiers;
            _entries.Clear();
            InvalidateCompiled();
            _submittedTargetToIndex.Clear();
            _submittedTargets.Clear();
            _submittedWeights.Clear();
            _output = LipSyncBlendshapeOutputSinkFactory.Create(_context, this);

            if (meshes == null || meshes.Count == 0) return;

            _uniqueMeshIds.Clear();
            for (int i = 0; i < meshes.Count; i++)
            {
                SkinnedMeshRenderer mesh = meshes[i];
                if (mesh == null || mesh.sharedMesh == null) continue;

                if (!_uniqueMeshIds.Add(ConvaiObjectId.Of(mesh))) continue;

                _entries.Add(new MeshEntry(mesh));
            }

            _compiledForMapping = mapping;
        }

        /// <summary>
        ///     Applies the current source channel values to all resolved target blendshapes.
        /// </summary>
        public void Apply(float[] values, IReadOnlyList<string> channelNames)
        {
            if (values == null || channelNames == null || channelNames.Count == 0 || _entries.Count == 0) return;

            using (ApplyMarker.Auto())
            {
                CompiledBlendshapeMapping compiled = EnsureCompiled(channelNames, _compiledForMapping);
                if (compiled == null || compiled.Routes == null || compiled.Routes.Length == 0) return;

                _submittedTargetToIndex.Clear();
                _submittedTargets.Clear();
                _submittedWeights.Clear();

                for (int r = 0; r < compiled.Routes.Length; r++)
                {
                    CompiledBlendshapeMapping.SourceRoute route = compiled.Routes[r];
                    if (!route.Enabled || route.Targets == null || route.Targets.Length == 0) continue;

                    if (route.SourceIndex < 0 || route.SourceIndex >= values.Length) continue;

                    float weight = EvaluateWeight(route, values[route.SourceIndex], compiled.GlobalMultiplier,
                        compiled.GlobalOffset, _bypassMappingModifiers);

                    for (int ti = 0; ti < route.Targets.Length; ti++)
                    {
                        CompiledBlendshapeMapping.TargetBinding target = route.Targets[ti];
                        if (target.MeshCacheIndex < 0 || target.MeshCacheIndex >= _entries.Count) continue;

                        MeshEntry entry = _entries[target.MeshCacheIndex];
                        if (entry.Mesh == null || entry.Mesh.sharedMesh == null) continue;

                        AddSubmittedWeight(entry.Mesh, target.BlendshapeIndex, weight);
                    }
                }

                _output?.PushFrame(_submittedTargets, _submittedWeights, _submittedTargets.Count);
            }
        }

        /// <summary>
        ///     Resets previously applied non-zero blendshape weights to zero.
        /// </summary>
        public void ResetToZero(IReadOnlyList<string> channelNames)
        {
            _output?.ResetDrivenTargets();
            _submittedTargetToIndex.Clear();
            _submittedTargets.Clear();
            _submittedWeights.Clear();
        }

        public void SetSpeechActive(bool isActive) => _output?.SetSpeechActive(isActive);

        /// <summary>
        ///     Clears compiled route caches so the mapping is recompiled on the next apply.
        /// </summary>
        public void Invalidate() => InvalidateCompiled();

        private void InvalidateCompiled()
        {
            _compiled = null;
            _compiledForMeshRevision = -1;
            _compiledChannelHash = 0;
            _compiledChannelCount = -1;
            _compiledBypassMappingModifiers = false;
        }

        private CompiledBlendshapeMapping EnsureCompiled(IReadOnlyList<string> channelNames,
            ConvaiLipSyncMapAsset mapping)
        {
            if (channelNames == null || channelNames.Count == 0 || mapping == null || _entries.Count == 0) return null;

            int channelHash = ComputeChannelHash(channelNames);
            bool valid = _compiled != null &&
                         ReferenceEquals(_compiledForMapping, mapping) &&
                         _compiledForMeshRevision == _meshRevision &&
                         _compiledChannelHash == channelHash &&
                         _compiledChannelCount == channelNames.Count &&
                         _compiledBypassMappingModifiers == _bypassMappingModifiers;

            if (valid) return _compiled;

            using (CompileMarker.Auto())
            {
                _routesBuildBuffer.Clear();
                for (int i = 0; i < channelNames.Count; i++)
                {
                    string name = channelNames[i];
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    CompiledBlendshapeMapping.SourceRoute route = BuildRoute(mapping, name, i);
                    if (!route.Enabled || route.Targets == null || route.Targets.Length == 0) continue;

                    _routesBuildBuffer.Add(route);
                }

                _compiled = new CompiledBlendshapeMapping(
                    _routesBuildBuffer.ToArray(),
                    mapping.GlobalMultiplier,
                    mapping.GlobalOffset,
                    channelNames.Count);
                _compiledForMeshRevision = _meshRevision;
                _compiledChannelHash = channelHash;
                _compiledChannelCount = channelNames.Count;
                _compiledBypassMappingModifiers = _bypassMappingModifiers;
                return _compiled;
            }
        }

        private CompiledBlendshapeMapping.SourceRoute BuildRoute(ConvaiLipSyncMapAsset mapping, string sourceName,
            int sourceIndex)
        {
            bool enabled;
            bool useOverride = false;
            float overrideValue = 0f;
            bool ignoreGlobal = false;
            float multiplier = 1f;
            float offset = 0f;
            float curveExponent = 1f;
            float clampMin = 0f;
            float clampMax = 1f;
            IReadOnlyList<string> targetNames;

            if (mapping.TryGetEntry(sourceName, out ConvaiLipSyncMapAsset.BlendshapeMappingSnapshot snapshot))
            {
                enabled = snapshot.Enabled;
                useOverride = snapshot.UseOverrideValue;
                ignoreGlobal = snapshot.IgnoreGlobalModifiers;
                multiplier = snapshot.Multiplier;
                offset = snapshot.Offset;
                curveExponent = snapshot.CurveExponent;
                clampMin = snapshot.ClampMinValue;
                clampMax = Math.Max(snapshot.ClampMinValue, snapshot.ClampMaxValue);
                overrideValue = Math.Clamp(snapshot.OverrideValue, clampMin, clampMax);
                targetNames = snapshot.TargetNames.Count > 0
                    ? snapshot.TargetNames
                    : PassthroughNames(sourceName);
            }
            else
            {
                enabled = mapping.AllowUnmappedPassthrough;
                targetNames = PassthroughNames(sourceName);
            }

            CompiledBlendshapeMapping.TargetBinding[] targets =
                enabled ? ResolveTargets(targetNames) : Array.Empty<CompiledBlendshapeMapping.TargetBinding>();

            return new CompiledBlendshapeMapping.SourceRoute(
                sourceIndex, enabled, useOverride, overrideValue, ignoreGlobal,
                multiplier, offset, curveExponent, clampMin, clampMax, targets);
        }

        /// <summary>
        ///     Returns a shared single-element buffer containing the source name for passthrough mapping.
        ///     The returned array instance is reused across calls and is valid only within the
        ///     current compile iteration. Do not cache it or consume it asynchronously.
        /// </summary>
        private IReadOnlyList<string> PassthroughNames(string sourceName)
        {
            _singleNameBuffer[0] = sourceName;
            return _singleNameBuffer;
        }

        private CompiledBlendshapeMapping.TargetBinding[] ResolveTargets(IReadOnlyList<string> targetNames)
        {
            if (targetNames == null || targetNames.Count == 0 || _entries.Count == 0)
                return Array.Empty<CompiledBlendshapeMapping.TargetBinding>();

            _resolvedTargetBuffer.Clear();
            _uniqueTargetKeys.Clear();

            for (int m = 0; m < _entries.Count; m++)
            {
                MeshEntry me = _entries[m];
                if (me.Mesh == null || me.Mesh.sharedMesh == null) continue;

                for (int t = 0; t < targetNames.Count; t++)
                {
                    if (!me.NameToIndex.TryGetValue(targetNames[t], out int bsIndex)) continue;

                    long key = ((long)m << 32) ^ (uint)bsIndex;
                    if (!_uniqueTargetKeys.Add(key)) continue;

                    _resolvedTargetBuffer.Add(new CompiledBlendshapeMapping.TargetBinding(m, bsIndex));
                }
            }

            return _resolvedTargetBuffer.Count > 0
                ? _resolvedTargetBuffer.ToArray()
                : Array.Empty<CompiledBlendshapeMapping.TargetBinding>();
        }

        private static float EvaluateWeight(
            CompiledBlendshapeMapping.SourceRoute route,
            float inputValue,
            float globalMultiplier,
            float globalOffset,
            bool bypassMappingModifiers)
        {
            if (bypassMappingModifiers) return Math.Clamp(inputValue, 0f, 1f) * 100f;

            float mapped;
            if (route.UseOverrideValue)
            {
                mapped = route.OverrideValue;
            }
            else
            {
                float shaped = ApplyResponseCurve(inputValue, route.CurveExponent);
                mapped = route.IgnoreGlobalModifiers
                    ? (shaped * route.Multiplier) + route.Offset
                    : (shaped * route.Multiplier * globalMultiplier) + route.Offset + globalOffset;
            }

            return Math.Clamp(mapped, route.ClampMinValue, route.ClampMaxValue) * 100f;
        }

        /// <summary>
        ///     Shapes a normalized source value with a power curve before gain is applied.
        ///     Exponents below 1 lift the mid-range so articulation commits to its target pose;
        ///     the endpoints 0 and 1 are unchanged, so peaks never clip from shaping alone.
        /// </summary>
        private static float ApplyResponseCurve(float value, float exponent)
        {
            if (Math.Abs(exponent - 1f) < 1e-4f) return value;
            return (float)Math.Pow(Math.Clamp(value, 0f, 1f), exponent);
        }

        /// <summary>
        ///     Inverse of <see cref="EvaluateWeight" />: mesh weight (0–100) to normalized source (0–1).
        /// </summary>
        private static float InverseMapToNormalizedSource(
            CompiledBlendshapeMapping.SourceRoute route,
            float meshWeightHundred,
            float globalMultiplier,
            float globalOffset,
            bool bypassMappingModifiers)
        {
            if (!route.Enabled) return 0f;

            if (bypassMappingModifiers)
                return Math.Clamp(meshWeightHundred / 100f, 0f, 1f);

            if (route.UseOverrideValue) return 0f;

            float mapped = Math.Clamp(meshWeightHundred / 100f, route.ClampMinValue, route.ClampMaxValue);

            float shaped;
            if (route.IgnoreGlobalModifiers)
            {
                double m = route.Multiplier;
                if (Math.Abs(m) < 1e-8) return 0f;
                shaped = (float)((mapped - route.Offset) / m);
            }
            else
            {
                double denom = route.Multiplier * globalMultiplier;
                if (Math.Abs(denom) < 1e-8) return 0f;
                shaped = (float)((mapped - route.Offset - globalOffset) / denom);
            }

            shaped = Math.Clamp(shaped, 0f, 1f);
            if (Math.Abs(route.CurveExponent - 1f) < 1e-4f) return shaped;

            return (float)Math.Pow(shaped, 1.0 / route.CurveExponent);
        }

        private static int ComputeChannelHash(IReadOnlyList<string> names)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < names.Count; i++)
                    hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(names[i] ?? string.Empty);

                return hash;
            }
        }

        private void AddSubmittedWeight(SkinnedMeshRenderer mesh, int blendshapeIndex, float weight)
        {
            if (mesh == null || mesh.sharedMesh == null) return;
            if (blendshapeIndex < 0 || blendshapeIndex >= mesh.sharedMesh.blendShapeCount) return;

            var key = new BlendshapeTargetKey(mesh, blendshapeIndex);
            if (_submittedTargetToIndex.TryGetValue(key, out int existingIndex))
            {
                if (weight > _submittedWeights[existingIndex])
                    _submittedWeights[existingIndex] = weight;
                return;
            }

            _submittedTargetToIndex[key] = _submittedTargets.Count;
            _submittedTargets.Add(key);
            _submittedWeights.Add(weight);
        }

        private sealed class MeshEntry
        {
            public readonly SkinnedMeshRenderer Mesh;
            public readonly Dictionary<string, int> NameToIndex;

            public MeshEntry(SkinnedMeshRenderer mesh)
            {
                Mesh = mesh;
                NameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                BuildIndex();
            }

            private void BuildIndex()
            {
                NameToIndex.Clear();
                if (Mesh == null || Mesh.sharedMesh == null) return;

                Mesh shared = Mesh.sharedMesh;
                for (int i = 0; i < shared.blendShapeCount; i++)
                {
                    string name = shared.GetBlendShapeName(i);
                    NameToIndex[name] = i;

                    int dot = name.LastIndexOf('.');
                    if (dot > 0)
                    {
                        string suffix = name.Substring(dot + 1);
                        if (!NameToIndex.ContainsKey(suffix)) NameToIndex[suffix] = i;
                    }
                }
            }
        }
    }
}
