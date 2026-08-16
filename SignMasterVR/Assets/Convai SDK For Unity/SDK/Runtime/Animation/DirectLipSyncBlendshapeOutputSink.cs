using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Writes lip-sync weights straight to <see cref="SkinnedMeshRenderer" /> blendshapes,
    ///     mirroring <see cref="FacialBlendshapeCompositorHost" /> write epsilon and last-applied
    ///     tracking so idle / reset clears morphs correctly.
    /// </summary>
    internal sealed class DirectLipSyncBlendshapeOutputSink : ILipSyncBlendshapeOutputSink
    {
        private readonly Dictionary<BlendshapeTargetKey, float> _lastApplied = new();
        private readonly Dictionary<BlendshapeTargetKey, float> _frameWeights = new();

        public void PushFrame(IReadOnlyList<BlendshapeTargetKey> targets, IReadOnlyList<float> weights, int count)
        {
            _frameWeights.Clear();
            int appliedCount = Mathf.Min(count, Mathf.Min(targets.Count, weights.Count));
            for (int i = 0; i < appliedCount; i++)
            {
                BlendshapeTargetKey key = targets[i];
                if (!key.IsValid) continue;

                float weight = Mathf.Clamp(weights[i], 0f, 100f);
                if (weight <= FacialBlendshapeCompositorHost.BlendshapeWriteEpsilon) continue;

                if (_frameWeights.TryGetValue(key, out float current) && current >= weight)
                    continue;

                _frameWeights[key] = weight;
            }

            foreach (BlendshapeTargetKey key in _lastApplied.Keys)
            {
                if (!_frameWeights.ContainsKey(key))
                    _frameWeights[key] = 0f;
            }

            foreach (KeyValuePair<BlendshapeTargetKey, float> kvp in _frameWeights)
                WriteIfChanged(kvp.Key, kvp.Value);
        }

        public void SetSpeechActive(bool isActive)
        {
        }

        public void ResetDrivenTargets()
        {
            if (_lastApplied.Count == 0)
                return;

            foreach (BlendshapeTargetKey key in new List<BlendshapeTargetKey>(_lastApplied.Keys))
            {
                if (!key.IsValid) continue;
                WriteIfChanged(key, 0f);
            }

            _lastApplied.Clear();
        }

        private void WriteIfChanged(BlendshapeTargetKey key, float finalWeight)
        {
            finalWeight = Mathf.Clamp(finalWeight, 0f, 100f);
            _lastApplied.TryGetValue(key, out float previousWeight);

            if (Mathf.Abs(finalWeight - previousWeight) < FacialBlendshapeCompositorHost.BlendshapeWriteEpsilon)
                return;

            key.Mesh.SetBlendShapeWeight(key.BlendshapeIndex, finalWeight);

            if (finalWeight <= FacialBlendshapeCompositorHost.BlendshapeWriteEpsilon)
                _lastApplied.Remove(key);
            else
                _lastApplied[key] = finalWeight;
        }
    }
}
