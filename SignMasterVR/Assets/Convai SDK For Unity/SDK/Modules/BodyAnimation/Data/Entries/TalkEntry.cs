using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     One talk variant played on the talk layer while the character is speaking.
    ///     Selection follows the same weighted, emotion-aware, no-immediate-repeat rules as
    ///     idle variants.
    /// </summary>
    [Serializable]
    public sealed class TalkEntry : IVariantEntry
    {
        [SerializeField]
        [Tooltip("Looping talk clip. Must be a Humanoid clip with Loop Time enabled.")]
        private AnimationClip _clip;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Relative random-selection weight. 0 disables the variant.")]
        private float _weight = 1f;

        [SerializeField]
        [Tooltip("Upper-body keeps legs/root on the base layer (safe while moving). " +
                 "Full-body drives the whole skeleton and is only honored while stationary.")]
        private BodyCoverage _coverage = BodyCoverage.UpperBody;

        [SerializeField]
        [Tooltip("Play the clip as an ADDITIVE delta over the base pose instead of " +
                 "overriding it — gestures layer on top of the idle posture without " +
                 "replacing it (no authored-lean takeover). Requires the clip to have an " +
                 "additive reference pose baked in its import settings.")]
        private bool _additive;

        [SerializeField]
        [Tooltip("Optional additive-baked twin of this gesture (delta from its own first " +
                 "frame). While the character walks, the walk-and-talk overlay plays this " +
                 "clip additively over the gait so arm swing survives under the gesture. " +
                 "Requires an additive reference pose baked in the clip's import settings.")]
        private AnimationClip _additiveClip;

        [SerializeField]
        [Tooltip("Optional emotion biases applied to the weight during selection.")]
        private List<EmotionAffinity> _affinities = new();

        [SerializeField]
        [Tooltip("Optional safe motion phrases inside the loop. With two or more valid entries, long speech crossfades among sub-ranges of this single clip to hide repetition.")]
        private List<TalkMotionFragment> _fragments = new();

        [SerializeField]
        [Tooltip("Optional lead-in played once when the character enters this talk pool from " +
                 "silence — hands visibly raise into gesture space instead of appearing purely " +
                 "via weight fade. Loop Time must be OFF. Talk pool only: never played for " +
                 "Listen/Think entries or the moving additive overlay, and never replayed for a " +
                 "same-pool variant switch or a pool-to-pool crossfade (e.g. Listening → " +
                 "Speaking) — only a fresh enter from silence.")]
        private AnimationClip _introClip;

        [SerializeField]
        [Tooltip("Optional wind-down played once as talk ends — hands visibly settle back down " +
                 "instead of vanishing purely via weight fade. Loop Time must be OFF. The added " +
                 "latency is capped by ConvaiBodyAnimationConfig.TalkOutroMaxSeconds, so a long " +
                 "outro clip never delays settling back to idle. Talk pool only.")]
        private AnimationClip _outroClip;

        [SerializeField]
        [Tooltip("When enabled, stop-speaking waits for this clip-phase window before fading. " +
                 "The Config release delay remains the hard maximum latency.")]
        private bool _useSafeReleaseWindow;

        [SerializeField, Range(0f, 1f)] private float _safeReleaseStart = 0.75f;
        [SerializeField, Range(0f, 1f)] private float _safeReleaseEnd = 1f;
        [SerializeField, Min(0.1f)] private float _outroMinPlaybackRate = 0.75f;
        [SerializeField, Min(0.1f)] private float _outroMaxPlaybackRate = 1.5f;

        public AnimationClip Clip => _clip;
        public float Weight => _weight;
        public BodyCoverage Coverage => _coverage;
        public bool Additive => _additive;

        /// <summary>Additive twin used by the walk-and-talk overlay; null = none authored.</summary>
        public AnimationClip AdditiveClip => _additiveClip;

        /// <summary>Optional lead-in played once on a fresh Speaking-enter. Null = today's weight-fade-only behavior.</summary>
        public AnimationClip IntroClip => _introClip;

        /// <summary>Optional wind-down played once as talk ends. Null = today's weight-fade-only behavior.</summary>
        public AnimationClip OutroClip => _outroClip;

        public bool HasIntro => _introClip != null;
        public bool HasOutro => _outroClip != null;
        public bool UseSafeReleaseWindow => _useSafeReleaseWindow;
        public float SafeReleaseStart => Mathf.Clamp01(_safeReleaseStart);
        public float SafeReleaseEnd => Mathf.Clamp(_safeReleaseEnd, SafeReleaseStart, 1f);
        public float OutroMinPlaybackRate => Mathf.Max(0.1f, _outroMinPlaybackRate);
        public float OutroMaxPlaybackRate => Mathf.Max(OutroMinPlaybackRate, _outroMaxPlaybackRate);

        public bool IsSafeReleaseTime(float normalizedTime)
        {
            float phase = normalizedTime - Mathf.Floor(normalizedTime);
            return !_useSafeReleaseWindow || phase >= SafeReleaseStart && phase <= SafeReleaseEnd;
        }

        public IReadOnlyList<EmotionAffinity> Affinities => _affinities;
        public IReadOnlyList<TalkMotionFragment> Fragments => _fragments;

        public bool HasFragments
        {
            get
            {
                for (int i = 0; i < _fragments.Count; i++)
                    if (_fragments[i] != null && _fragments[i].IsValid) return true;
                return false;
            }
        }

        public bool IsValid => _clip != null && _weight > 0f;

        /// <summary>
        ///     The clip the moving (additive) talk slot should play: the authored additive
        ///     twin, or the main clip itself when the entry is already additive. Null when
        ///     the entry has no additive-safe content (softened-override fallback applies).
        /// </summary>
        public AnimationClip ResolveMovingClip()
        {
            if (_additiveClip != null) return _additiveClip;
            return _additive ? _clip : null;
        }

        internal void Initialize(
            AnimationClip clip,
            float weight = 1f,
            BodyCoverage coverage = BodyCoverage.UpperBody,
            bool additive = false,
            AnimationClip introClip = null,
            AnimationClip outroClip = null)
        {
            _clip = clip;
            _weight = Mathf.Max(0f, weight);
            _coverage = coverage;
            _additive = additive;
            _introClip = introClip;
            _outroClip = outroClip;
        }

        /// <summary>Editor/wizard writers. Not part of the public runtime API.</summary>
        internal void SetAdditiveClip(AnimationClip clip) => _additiveClip = clip;

        internal void ReplaceFragments(List<TalkMotionFragment> fragments) =>
            _fragments = fragments ?? new List<TalkMotionFragment>();
    }
}
