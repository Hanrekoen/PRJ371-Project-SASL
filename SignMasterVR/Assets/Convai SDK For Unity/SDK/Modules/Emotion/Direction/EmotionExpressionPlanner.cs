using System;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Emotion.Compilation;
using Convai.Modules.Emotion.Profiles;
using UnityEngine;

namespace Convai.Modules.Emotion.Direction
{
    /// <summary>
    /// Evaluates semantic recipes into temporally shaped, conflict-safe facial weights. Runtime
    /// state is fixed-size and indexed by <see cref="StandardBlendshape"/>; steady-state Tick is
    /// allocation-free and independent of concrete rig names.
    /// </summary>
    internal sealed class EmotionExpressionPlanner
    {
        internal const int ChannelCount = (int)StandardBlendshape.TongueOut + 1;

        private readonly CompiledEmotionModel _model;
        private readonly float[] _targets = new float[ChannelCount];
        private readonly float[] _current = new float[ChannelCount];
        private readonly float[] _attackSeconds = new float[ChannelCount];
        private readonly float[] _releaseSeconds = new float[ChannelCount];
        private readonly float[] _holdSeconds = new float[ChannelCount];
        private readonly float[] _holdRemaining = new float[ChannelCount];

        internal EmotionExpressionPlanner(CompiledEmotionModel model) =>
            _model = model ?? throw new ArgumentNullException(nameof(model));

        internal float[] Weights => _current;

        /// <param name="globalProsodyGain">
        ///     The profile-level Prosody Coupling gain, already smoothed by the controller and
        ///     exactly <c>1</c> when coupling is <c>0</c>. Multiplies the per-channel prosody term
        ///     so the profile knob actually reaches the face; at coupling <c>0</c> this is a
        ///     provable no-op.
        /// </param>
        internal void Tick(
            float[] indexedScores, bool speaking, float speechEnergy, float deltaTime,
            float globalProsodyGain = 1f)
        {
            if (float.IsNaN(globalProsodyGain) || globalProsodyGain <= 0f) globalProsodyGain = 1f;

            Array.Clear(_targets, 0, _targets.Length);
            Array.Clear(_holdSeconds, 0, _holdSeconds.Length);

            CompiledExpressionRecipe[] recipes = _model.Recipes;
            for (int r = 0; r < recipes.Length; r++)
            {
                CompiledExpressionRecipe recipe = recipes[r];
                if (indexedScores == null || recipe.EmotionIndex < 0 || recipe.EmotionIndex >= indexedScores.Length)
                    continue;

                float score = Mathf.Clamp01(indexedScores[recipe.EmotionIndex]);
                if (score <= 0f) continue;

                EmotionExpressionChannel[] channels = recipe.Channels;
                for (int c = 0; c < channels.Length; c++)
                {
                    EmotionExpressionChannel channel = channels[c];
                    int index = (int)channel.Semantic;
                    if (index < 0 || index >= ChannelCount) continue;

                    AnimationCurve curve = channel.IntensityCurve;
                    float shaped = curve != null ? curve.Evaluate(score) : score;
                    float prosodyGain = speaking
                        ? 1f + (Mathf.Clamp01(speechEnergy) - 0.5f) * 0.3f * channel.ProsodySensitivity
                        : 1f;
                    float target = Mathf.Clamp(
                        channel.FullWeight * Mathf.Clamp01(shaped) * recipe.MaximumIntensity *
                        prosodyGain * globalProsodyGain,
                        0f, 100f);

                    if (target <= _targets[index]) continue;
                    _targets[index] = target;
                    _attackSeconds[index] = recipe.OnsetSeconds;
                    _releaseSeconds[index] = recipe.ReleaseSeconds;
                    _holdSeconds[index] = recipe.HoldSeconds;
                }
            }

            ResolveConflicts();

            float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
            for (int i = 0; i < ChannelCount; i++)
            {
                float target = _targets[i];
                if (target > 0f)
                {
                    _holdRemaining[i] = Mathf.Max(_holdRemaining[i], _holdSeconds[i]);
                }
                else if (_holdRemaining[i] > 0f)
                {
                    _holdRemaining[i] = Mathf.Max(0f, _holdRemaining[i] - dt);
                    target = _current[i];
                }

                float seconds = target >= _current[i]
                    ? Mathf.Max(0.03f, _attackSeconds[i])
                    : Mathf.Max(0.05f, _releaseSeconds[i]);
                float alpha = 1f - Mathf.Exp(-dt * 3f / seconds);
                _current[i] += (target - _current[i]) * alpha;
                if (Mathf.Abs(_current[i] - target) < 0.001f) _current[i] = target;
            }

            ResolveCurrentConflicts();
        }

        internal void Reset()
        {
            Array.Clear(_targets, 0, _targets.Length);
            Array.Clear(_current, 0, _current.Length);
            Array.Clear(_holdRemaining, 0, _holdRemaining.Length);
        }

        private void ResolveConflicts()
        {
            NormalizePair(StandardBlendshape.BrowDownLeft, StandardBlendshape.BrowOuterUpLeft, 78f);
            NormalizePair(StandardBlendshape.BrowDownRight, StandardBlendshape.BrowOuterUpRight, 78f);
            NormalizePair(StandardBlendshape.EyeWideLeft, StandardBlendshape.EyeSquintLeft, 82f);
            NormalizePair(StandardBlendshape.EyeWideRight, StandardBlendshape.EyeSquintRight, 82f);
            NormalizePair(StandardBlendshape.MouthSmileLeft, StandardBlendshape.MouthFrownLeft, 80f);
            NormalizePair(StandardBlendshape.MouthSmileRight, StandardBlendshape.MouthFrownRight, 80f);
            NormalizePair(StandardBlendshape.MouthPressLeft, StandardBlendshape.MouthStretchLeft, 72f);
            NormalizePair(StandardBlendshape.MouthPressRight, StandardBlendshape.MouthStretchRight, 72f);
        }

        private void NormalizePair(StandardBlendshape a, StandardBlendshape b, float cap)
        {
            int ia = (int)a;
            int ib = (int)b;
            float total = _targets[ia] + _targets[ib];
            if (total <= cap || total <= 0f) return;
            float scale = cap / total;
            _targets[ia] *= scale;
            _targets[ib] *= scale;
        }

        private void ResolveCurrentConflicts()
        {
            NormalizeCurrentPair(StandardBlendshape.BrowDownLeft, StandardBlendshape.BrowOuterUpLeft, 78f);
            NormalizeCurrentPair(StandardBlendshape.BrowDownRight, StandardBlendshape.BrowOuterUpRight, 78f);
            NormalizeCurrentPair(StandardBlendshape.EyeWideLeft, StandardBlendshape.EyeSquintLeft, 82f);
            NormalizeCurrentPair(StandardBlendshape.EyeWideRight, StandardBlendshape.EyeSquintRight, 82f);
            NormalizeCurrentPair(StandardBlendshape.MouthSmileLeft, StandardBlendshape.MouthFrownLeft, 80f);
            NormalizeCurrentPair(StandardBlendshape.MouthSmileRight, StandardBlendshape.MouthFrownRight, 80f);
        }

        private void NormalizeCurrentPair(StandardBlendshape a, StandardBlendshape b, float cap)
        {
            int ia = (int)a;
            int ib = (int)b;
            float total = _current[ia] + _current[ib];
            if (total <= cap || total <= 0f) return;
            float scale = cap / total;
            _current[ia] *= scale;
            _current[ib] *= scale;
        }
    }
}
