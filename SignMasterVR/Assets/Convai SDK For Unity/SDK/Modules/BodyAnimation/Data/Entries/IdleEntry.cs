using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     One idle variant. The scheduler cycles variants randomly using
    ///     <see cref="Weight" /> (optionally biased by <see cref="Affinities" />) and never
    ///     replays the same variant back-to-back when alternatives exist.
    /// </summary>
    [Serializable]
    public sealed class IdleEntry : IVariantEntry
    {
        [SerializeField]
        [Tooltip("Looping idle clip. Must be a Humanoid clip with Loop Time enabled.")]
        private AnimationClip _clip;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Relative random-selection weight. 0 disables the variant.")]
        private float _weight = 1f;

        [SerializeField]
        [Tooltip("Optional emotion biases applied to the weight during selection.")]
        private List<EmotionAffinity> _affinities = new();

        public AnimationClip Clip => _clip;
        public float Weight => _weight;
        public IReadOnlyList<EmotionAffinity> Affinities => _affinities;

        public bool IsValid => _clip != null && _weight > 0f;

        internal void Initialize(AnimationClip clip, float weight = 1f, List<EmotionAffinity> affinities = null)
        {
            _clip = clip;
            _weight = Mathf.Max(0f, weight);
            if (affinities != null) _affinities = affinities;
        }
    }
}
