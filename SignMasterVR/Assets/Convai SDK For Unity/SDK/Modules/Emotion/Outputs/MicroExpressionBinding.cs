using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Logging;
using Convai.Modules.Emotion.Core;
using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.Emotion.Outputs
{
    /// <summary>
    ///     Resolves the curated <see cref="MicroExpressionChannel" /> shape map
    ///     (<c>Convai.Modules.Emotion.Authoring.MicroExpressionShapeMap</c>) against the
    ///     character's facial meshes and, each tick, submits the
    ///     <see cref="MicroExpressionDirector" />'s per-channel weights to the
    ///     <see cref="FacialBlendshapeLayers.EmotionMicro" /> compositor layer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Internal authoring surface only — created and owned by
    ///         <c>ConvaiEmotionController</c> when the profile opts in
    ///         (<c>ConvaiEmotionProfile.MicroExpressionsEnabled</c>). Never bound when the
    ///         feature is off, so the compositor never sees a submission and the additive
    ///         <see cref="FacialBlendshapeLayers.EmotionMicro" /> term in
    ///         <see cref="FacialBlendshapeCompositorHost" /> stays a provable no-op.
    ///     </para>
    ///     <para>
    ///         Follows the module's standard bind/resolve/submit shape:
    ///         preallocated buffers, one graceful "no shapes resolved" log, zero per-frame
    ///         allocation in <see cref="Apply" />.
    ///     </para>
    /// </remarks>
    internal sealed class MicroExpressionBinding : IFacialBlendshapeSource
    {
        private const int ChannelCount = (int)MicroExpressionChannel.Count;

        private readonly List<BlendshapeTargetKey>[] _channelTargets = new List<BlendshapeTargetKey>[ChannelCount];
        private readonly List<BlendshapeTargetKey> _submitTargets = new();
        private readonly List<float> _submitWeights = new();

        private FacialBlendshapeCompositorHost _compositor;
        private Component _sourceComponent;
        private string _sourceName;
        private bool _bound;
        private bool _hasAnyShape;
        private bool _warnedNoShapes;

        /// <inheritdoc />
        public Component SourceComponent => _sourceComponent;

        /// <inheritdoc />
        public string SourceName => _sourceName;

        /// <summary>Whether at least one channel resolved to a live blendshape target.</summary>
        public bool HasAnyShape => _hasAnyShape;

        /// <summary>
        ///     Resolves the curated shape map against <paramref name="rig" />'s facial meshes
        ///     (falling back to a mesh scan under <paramref name="fallbackContext" />, mirroring
        ///     the semantic face output). Inert (no throw) when no compositor or
        ///     no facial meshes are available; logs once if shapes were expected but none
        ///     resolved.
        /// </summary>
        public void Bind(
            Component owner,
            IStandardRigBinding rig,
            Component fallbackContext,
            FacialBlendshapeCompositorHost compositor)
        {
            Unbind();

            _sourceComponent = owner;
            _sourceName = owner != null ? owner.name : "MicroExpressionBinding";
            _compositor = compositor;

            if (compositor == null) return;

            IReadOnlyList<SkinnedMeshRenderer> facialMeshes = ResolveFacialMeshes(rig, fallbackContext);
            if (facialMeshes.Count == 0) return;

            RigConvention convention = rig?.DetectedConvention ?? RigConvention.Unknown;
            // EditMode and custom integrations may provide only the facial mesh contract,
            // without a StandardRigBinding component. Infer the convention from those meshes
            // so curated micro-expression channels remain usable without reintroducing a
            // silent CC3 fallback for genuinely unknown rigs.
            if (convention == RigConvention.Unknown)
                convention = RigConventionResolver.Detect(facialMeshes, out _);

            var scratchNames = new List<string>(2);
            for (int i = 0; i < ChannelCount; i++)
            {
                var channel = (MicroExpressionChannel)i;
                string names = Authoring.MicroExpressionShapeMap.GetNames(channel, convention);
                _channelTargets[i] = ResolveTargets(facialMeshes, names, scratchNames);
                if (_channelTargets[i].Count > 0) _hasAnyShape = true;
            }

            _bound = true;

            if (!_hasAnyShape && !_warnedNoShapes)
            {
                _warnedNoShapes = true;
                ConvaiLogger.Warning(
                    $"[MicroExpressionBinding] '{_sourceName}' is set to never sit perfectly still, but none " +
                    "of the brow, cheek or squint blendshapes that layer needs were found on its face, so the " +
                    "face will hold still anyway. Check that the character's facial mesh carries those " +
                    "shapes and that its blendshape names follow a supported convention (ARKit, Reallusion " +
                    "CC3/CC4, or MetaHuman); for a rig using none of those, assign a Custom Rig Convention Map.",
                    LogCategory.SDK);
            }
        }

        /// <summary>
        ///     Submits the director's current per-channel weights to
        ///     <see cref="FacialBlendshapeLayers.EmotionMicro" />. No-op when unbound or no
        ///     shapes resolved.
        /// </summary>
        public void Apply(MicroExpressionDirector director)
        {
            if (!_bound || !_hasAnyShape || _compositor == null || director == null) return;

            _submitTargets.Clear();
            _submitWeights.Clear();

            for (int i = 0; i < ChannelCount; i++)
            {
                List<BlendshapeTargetKey> targets = _channelTargets[i];
                if (targets == null || targets.Count == 0) continue;

                float weight01 = director.GetChannelWeight((MicroExpressionChannel)i);
                if (weight01 <= 0.0001f) continue;

                float weight = weight01 * 100f;
                for (int t = 0; t < targets.Count; t++)
                {
                    _submitTargets.Add(targets[t]);
                    _submitWeights.Add(weight);
                }
            }

            if (_submitTargets.Count == 0) return;

            _compositor.SubmitLayer(this, FacialBlendshapeLayers.EmotionMicro,
                _submitTargets, _submitWeights, _submitTargets.Count);
        }

        /// <summary>
        ///     Releases all resolved targets and compositor reference. Clears any already-submitted
        ///     <see cref="FacialBlendshapeLayers.EmotionMicro" /> frame first so a toggle-off/rebuild
        ///     that happens after <see cref="Apply" /> but before the compositor's next
        ///     <see cref="FacialBlendshapeCompositorHost.LateUpdate" /> cannot leak a stale additive
        ///     micro-expression contribution.
        /// </summary>
        public void Unbind()
        {
            _compositor?.ClearLayer(this, FacialBlendshapeLayers.EmotionMicro);

            for (int i = 0; i < ChannelCount; i++)
                _channelTargets[i] = null;

            _submitTargets.Clear();
            _submitWeights.Clear();
            _compositor = null;
            _sourceComponent = null;
            _sourceName = null;
            _bound = false;
            _hasAnyShape = false;
        }

        private static IReadOnlyList<SkinnedMeshRenderer> ResolveFacialMeshes(
            IStandardRigBinding rig,
            Component fallbackContext)
        {
            if (rig != null && rig.FacialMeshes != null && rig.FacialMeshes.Count > 0)
                return rig.FacialMeshes;

            if (fallbackContext == null) return Array.Empty<SkinnedMeshRenderer>();

            Transform root = rig?.Root;
            if (root == null)
            {
                EmbodimentContext context = fallbackContext.GetComponentInParent<EmbodimentContext>(true);
                root = context != null ? context.CharacterRoot : fallbackContext.transform;
            }

            SkinnedMeshRenderer[] discovered = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var filtered = new List<SkinnedMeshRenderer>(discovered.Length);
            for (int i = 0; i < discovered.Length; i++)
            {
                SkinnedMeshRenderer smr = discovered[i];
                if (smr == null || smr.sharedMesh == null) continue;
                if (smr.sharedMesh.blendShapeCount == 0) continue;
                filtered.Add(smr);
            }
            return filtered;
        }

        private static List<BlendshapeTargetKey> ResolveTargets(
            IReadOnlyList<SkinnedMeshRenderer> meshes,
            string commaSeparatedNames,
            List<string> scratchNames)
        {
            var result = new List<BlendshapeTargetKey>(2);
            if (string.IsNullOrWhiteSpace(commaSeparatedNames)) return result;

            scratchNames.Clear();
            string[] parts = commaSeparatedNames.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (trimmed.Length > 0) scratchNames.Add(trimmed);
            }

            for (int n = 0; n < scratchNames.Count; n++)
            {
                string name = scratchNames[n];
                for (int m = 0; m < meshes.Count; m++)
                {
                    SkinnedMeshRenderer smr = meshes[m];
                    if (smr == null || smr.sharedMesh == null) continue;

                    int index = smr.sharedMesh.GetBlendShapeIndex(name);
                    if (index < 0) continue;

                    result.Add(new BlendshapeTargetKey(smr, index));
                }
            }
            return result;
        }
    }
}
