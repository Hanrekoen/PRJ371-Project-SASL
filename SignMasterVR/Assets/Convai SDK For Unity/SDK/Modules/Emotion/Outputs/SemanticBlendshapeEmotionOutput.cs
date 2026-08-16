using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Animation;
using UnityEngine;

namespace Convai.Modules.Emotion.Outputs
{
    /// <summary>Rig-independent semantic face output for the AAA expression planner.</summary>
    internal sealed class SemanticBlendshapeEmotionOutput : IFacialBlendshapeSource
    {
        private const int ChannelCount = (int)StandardBlendshape.TongueOut + 1;
        private const float WriteEpsilon = 0.0001f;
        private readonly List<BlendshapeTargetKey>[] _targetsBySemantic = new List<BlendshapeTargetKey>[ChannelCount];
        private readonly List<BlendshapeTargetKey> _generalTargets = new(48);
        private readonly List<float> _generalWeights = new(48);
        private readonly List<BlendshapeTargetKey> _mouthTargets = new(32);
        private readonly List<float> _mouthWeights = new(32);

        private FacialBlendshapeCompositorHost _compositor;
        private Component _source;

        public Component SourceComponent => _source;
        public string SourceName => _source != null ? _source.name : nameof(SemanticBlendshapeEmotionOutput);

        internal int ResolvedSemanticCount { get; private set; }

        internal void Bind(Component owner, IStandardRigBinding rig, FacialBlendshapeCompositorHost compositor)
        {
            Unbind();
            _source = owner;
            _compositor = compositor;
            if (rig == null || compositor == null) return;

            // Plain index walk rather than Enum.GetValues: the latter allocates an array and boxes
            // every value, and StandardBlendshape is a contiguous 0..TongueOut enum, so the loop
            // covers exactly the same set.
            for (int index = 0; index < ChannelCount; index++)
            {
                var semantic = (StandardBlendshape)index;
                var targets = new List<BlendshapeTargetKey>(2);
                if (BlendshapeTargetCollector.CollectAllMatches(rig, semantic, targets) <= 0) continue;
                _targetsBySemantic[index] = targets;
                ResolvedSemanticCount++;
            }
        }

        internal void Apply(float[] semanticWeights)
        {
            if (_compositor == null || semanticWeights == null) return;
            _generalTargets.Clear();
            _generalWeights.Clear();
            _mouthTargets.Clear();
            _mouthWeights.Clear();

            int count = Mathf.Min(ChannelCount, semanticWeights.Length);
            for (int i = 0; i < count; i++)
            {
                float weight = semanticWeights[i];
                List<BlendshapeTargetKey> targets = _targetsBySemantic[i];
                if (weight <= WriteEpsilon || targets == null) continue;

                bool mouth = IsMouthOrJaw((StandardBlendshape)i);
                List<BlendshapeTargetKey> targetBuffer = mouth ? _mouthTargets : _generalTargets;
                List<float> weightBuffer = mouth ? _mouthWeights : _generalWeights;
                for (int t = 0; t < targets.Count; t++)
                {
                    targetBuffer.Add(targets[t]);
                    weightBuffer.Add(weight);
                }
            }

            if (_generalTargets.Count > 0)
                _compositor.SubmitLayer(this, FacialBlendshapeLayers.EmotionGeneral,
                    _generalTargets, _generalWeights, _generalTargets.Count);
            else
                _compositor.ClearLayer(this, FacialBlendshapeLayers.EmotionGeneral);
            if (_mouthTargets.Count > 0)
                _compositor.SubmitLayer(this, FacialBlendshapeLayers.EmotionMouth,
                    _mouthTargets, _mouthWeights, _mouthTargets.Count);
            else
                _compositor.ClearLayer(this, FacialBlendshapeLayers.EmotionMouth);
        }

        internal bool TryGetMouthWeight(BlendshapeTargetKey key, out float weight)
        {
            for (int i = 0; i < _mouthTargets.Count; i++)
            {
                if (!_mouthTargets[i].Equals(key)) continue;
                weight = _mouthWeights[i];
                return true;
            }

            weight = 0f;
            return false;
        }

        internal void Unbind()
        {
            if (_compositor != null)
            {
                _compositor.ClearLayer(this, FacialBlendshapeLayers.EmotionGeneral);
                _compositor.ClearLayer(this, FacialBlendshapeLayers.EmotionMouth);
            }

            for (int i = 0; i < _targetsBySemantic.Length; i++)
                _targetsBySemantic[i] = null;
            ResolvedSemanticCount = 0;
            _compositor = null;
            _source = null;
        }

        private static bool IsMouthOrJaw(StandardBlendshape semantic) =>
            (int)semantic >= (int)StandardBlendshape.JawForward;
    }
}
