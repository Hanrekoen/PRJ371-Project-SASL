using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>
    ///     One-dimensional blend (speed axis) with phase synchronization: all samples share a
    ///     single normalized cycle phase, so blending walk↔jog never double-steps — the feet
    ///     stay locked to the same stride cycle while the pose interpolates.
    /// </summary>
    /// <remarks>
    ///     Sample times are driven manually every tick (their playables run at speed 0), with
    ///     the phase advanced from the weighted average cycle rate scaled by
    ///     <see cref="RateScale" /> (playback-rate warping). Thresholds are authored speeds
    ///     (m/s), so <see cref="SetParameter" /> takes the live agent speed directly.
    /// </remarks>
    internal sealed class Blend1D
    {
        public readonly struct Sample
        {
            public readonly AnimationClip Clip;
            public readonly float Threshold;

            public Sample(AnimationClip clip, float threshold)
            {
                Clip = clip;
                Threshold = threshold;
            }
        }

        private readonly PlayableGraph _graph;
        private readonly Sample[] _samples;            // sorted ascending by threshold
        private readonly AnimationClipPlayable[] _playables;
        private readonly float[] _weights;
        private AnimationMixerPlayable _mixer;
        private float _phase;                          // normalized cycle phase [0..1)

        public Playable Playable => _mixer;

        /// <summary>Current blend parameter (agent speed, m/s).</summary>
        public float Parameter { get; private set; }

        /// <summary>
        ///     Weighted average of the active sample thresholds — i.e. the speed (m/s) the
        ///     blended animation is authored to move at. Rate warping divides the live agent
        ///     speed by this to eliminate foot slide.
        /// </summary>
        public float BlendedThreshold { get; private set; }

        /// <summary>Playback-rate warp multiplier applied to the shared cycle. 1 = authored rate.</summary>
        public float RateScale { get; set; } = 1f;

        /// <summary>Shared normalized cycle phase, [0..1). Foot-phase queries key off this.</summary>
        public float Phase => _phase;

        /// <summary>Threshold of the highest-weighted sample (diagnostics).</summary>
        public float DominantThreshold
        {
            get
            {
                int dominant = 0;
                for (int i = 1; i < _weights.Length; i++)
                {
                    if (_weights[i] > _weights[dominant]) dominant = i;
                }
                return _samples[dominant].Threshold;
            }
        }

        /// <summary>Name of the highest-weighted sample's clip (diagnostics).</summary>
        public string DominantClipName
        {
            get
            {
                int dominant = 0;
                for (int i = 1; i < _weights.Length; i++)
                {
                    if (_weights[i] > _weights[dominant]) dominant = i;
                }
                return _samples[dominant].Clip.name;
            }
        }

        public Blend1D(PlayableGraph graph, Sample[] samples, bool applyFootIK)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("Blend1D needs at least one sample.", nameof(samples));

            _graph = graph;
            _samples = (Sample[])samples.Clone();
            Array.Sort(_samples, (a, b) => a.Threshold.CompareTo(b.Threshold));

            _playables = new AnimationClipPlayable[_samples.Length];
            _weights = new float[_samples.Length];
            _mixer = AnimationMixerPlayable.Create(graph, _samples.Length);

            for (int i = 0; i < _samples.Length; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, _samples[i].Clip);
                playable.SetApplyFootIK(applyFootIK);
                playable.SetSpeed(0d); // time is driven manually for phase lock
                graph.Connect(playable, 0, _mixer, i);
                _mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
                _playables[i] = playable;
            }

            _weights[0] = 1f;
        }

        /// <summary>Sets the blend parameter (agent speed) and recomputes sample weights.</summary>
        public void SetParameter(float value)
        {
            Parameter = value;
            ComputeWeights(_samples, value, _weights);

            float blended = 0f;
            for (int i = 0; i < _weights.Length; i++)
            {
                _mixer.SetInputWeight(i, _weights[i]);
                blended += _weights[i] * _samples[i].Threshold;
            }

            BlendedThreshold = blended;
        }

        /// <summary>
        ///     Advances the shared cycle phase from the weighted average of the samples' cycle
        ///     rates and writes each sample's time from it.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float cycleRate = 0f; // cycles per second
            for (int i = 0; i < _samples.Length; i++)
            {
                float length = _samples[i].Clip.length;
                if (length > 0f)
                    cycleRate += _weights[i] / length;
            }

            _phase = Mathf.Repeat(_phase + cycleRate * RateScale * deltaTime, 1f);

            for (int i = 0; i < _playables.Length; i++)
            {
                if (_weights[i] <= 0f) continue;
                _playables[i].SetTime(_phase * _samples[i].Clip.length);
            }
        }

        /// <summary>Aligns the shared phase (e.g. to hand off from a start clip's foot phase).</summary>
        public void SetPhase(float normalizedPhase) => _phase = Mathf.Repeat(normalizedPhase, 1f);

        /// <summary>
        ///     Linear neighbor interpolation identical to a 1D blend tree: full weight on an
        ///     exact threshold, split between the two surrounding samples otherwise, clamped
        ///     at the ends. Exposed for tests.
        /// </summary>
        public static void ComputeWeights(Sample[] sortedSamples, float parameter, float[] weights)
        {
            Array.Clear(weights, 0, weights.Length);

            if (sortedSamples.Length == 1 || parameter <= sortedSamples[0].Threshold)
            {
                weights[0] = 1f;
                return;
            }

            int last = sortedSamples.Length - 1;
            if (parameter >= sortedSamples[last].Threshold)
            {
                weights[last] = 1f;
                return;
            }

            for (int i = 0; i < last; i++)
            {
                float lower = sortedSamples[i].Threshold;
                float upper = sortedSamples[i + 1].Threshold;
                if (parameter < lower || parameter > upper) continue;

                float span = upper - lower;
                float t = span <= 0f ? 1f : (parameter - lower) / span;
                weights[i] = 1f - t;
                weights[i + 1] = t;
                return;
            }
        }
    }
}
