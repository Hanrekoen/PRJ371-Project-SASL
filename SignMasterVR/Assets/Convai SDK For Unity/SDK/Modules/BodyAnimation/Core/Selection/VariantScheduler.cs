using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Selection
{
    /// <summary>
    ///     Deterministic weighted-random variant selection shared by the idle and talk pools:
    ///     entry weights are biased by emotion affinities against the current dominant
    ///     emotion, invalid/zero-weight entries are skipped, and the previously played
    ///     variant is excluded whenever an alternative exists (no immediate repeats).
    /// </summary>
    internal sealed class VariantScheduler
    {
        // Mutable struct on purpose: the embodiment-wide deterministic stream (never
        // UnityEngine.Random, never System.Random — reproducible across platforms).
        private DeterministicEmbodimentRandom _random;
        private readonly List<int> _candidates = new(8);
        private readonly List<float> _weights = new(8);

        public VariantScheduler(int seed)
        {
            _random = new DeterministicEmbodimentRandom(unchecked((uint)seed));
        }

        /// <summary>Uniform random interval, e.g. for idle variant swap timing.</summary>
        public float NextInterval(float min, float max) => _random.Range(min, max);

        /// <summary>
        ///     Picks the next variant index, or −1 when the pool has no playable entry.
        ///     <paramref name="lastIndex" /> is excluded when at least one other candidate
        ///     exists. Outputs the effective (emotion-biased) weight used for diagnostics.
        /// </summary>
        public int SelectNext<T>(
            IReadOnlyList<T> entries,
            int lastIndex,
            in EmotionReading emotion,
            out float selectedWeight)
            where T : IVariantEntry
        {
            selectedWeight = 0f;
            if (entries == null || entries.Count == 0) return -1;

            _candidates.Clear();
            _weights.Clear();
            float totalWeight = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                T entry = entries[i];
                if (!entry.IsValid) continue;

                float weight = EffectiveWeight(entry, in emotion);
                if (weight <= 0f) continue;

                _candidates.Add(i);
                _weights.Add(weight);
                totalWeight += weight;
            }

            if (_candidates.Count == 0) return -1;

            // Exclude the previous pick when alternatives exist.
            if (_candidates.Count > 1)
            {
                int lastPos = _candidates.IndexOf(lastIndex);
                if (lastPos >= 0)
                {
                    totalWeight -= _weights[lastPos];
                    _candidates.RemoveAt(lastPos);
                    _weights.RemoveAt(lastPos);
                }
            }

            if (totalWeight <= 0f || _candidates.Count == 1)
            {
                selectedWeight = _weights[0];
                return _candidates[0];
            }

            float roll = _random.Value * totalWeight;
            for (int i = 0; i < _candidates.Count; i++)
            {
                roll -= _weights[i];
                if (roll > 0f) continue;

                selectedWeight = _weights[i];
                return _candidates[i];
            }

            selectedWeight = _weights[^1];
            return _candidates[^1];
        }

        private static float EffectiveWeight<T>(T entry, in EmotionReading emotion)
            where T : IVariantEntry
        {
            float weight = entry.Weight;
            IReadOnlyList<EmotionAffinity> affinities = entry.Affinities;
            if (affinities == null || emotion.IsNeutral) return weight;

            for (int i = 0; i < affinities.Count; i++)
                weight *= affinities[i].Evaluate(emotion.DominantLabel, emotion.DominantScore);

            return Mathf.Max(0f, weight);
        }
    }
}
