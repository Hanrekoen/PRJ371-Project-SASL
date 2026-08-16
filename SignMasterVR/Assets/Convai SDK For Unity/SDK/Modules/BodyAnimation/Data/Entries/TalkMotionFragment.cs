using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Safe motion phrase inside a looping talk clip. Multiple fragments let a single
    ///     source clip behave like a small repertoire without duplicating runtime clips.
    /// </summary>
    [Serializable]
    public sealed class TalkMotionFragment
    {
        [SerializeField, Range(0f, 1f)] private float _startNormalized;
        [SerializeField, Range(0f, 1f)] private float _endNormalized = 1f;
        [SerializeField, Min(0f)] private float _weight = 1f;
        [SerializeField] private string _label = "Motion Phrase";

        public float StartNormalized => Mathf.Clamp(_startNormalized, 0f, 0.98f);
        public float EndNormalized => Mathf.Clamp(_endNormalized, StartNormalized + 0.02f, 1f);
        public float Weight => Mathf.Max(0f, _weight);
        public string Label => _label;
        public bool IsValid => Weight > 0f && EndNormalized - StartNormalized >= 0.02f;

        internal void Initialize(float start, float end, float weight = 1f, string label = null)
        {
            _startNormalized = Mathf.Clamp(start, 0f, 0.98f);
            _endNormalized = Mathf.Clamp(end, _startNormalized + 0.02f, 1f);
            _weight = Mathf.Max(0f, weight);
            if (!string.IsNullOrWhiteSpace(label)) _label = label;
        }
    }
}
