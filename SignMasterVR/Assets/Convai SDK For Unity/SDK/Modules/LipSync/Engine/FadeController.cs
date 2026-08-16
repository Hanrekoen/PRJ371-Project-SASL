using System;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Controls smooth fade-out of blendshape values toward a target pose.
    ///     When no target is provided, values fade to zero.
    ///     Uses clamped delta time and ease-out quadratic curve.
    /// </summary>
    internal sealed class FadeController
    {
        private float _duration;
        private float[] _snapshot = Array.Empty<float>();
        private float[] _fadeTargets = Array.Empty<float>();
        private bool _hasFadeTargets;

        public bool IsActive { get; private set; }
        public float Progress { get; private set; }

        /// <summary>Begin fading out from the given values toward zero over the specified duration.</summary>
        public void Begin(float[] currentValues, float duration)
        {
            Begin(currentValues, duration, null);
        }

        /// <summary>
        ///     Begin fading out from the given values toward <paramref name="fadeTargets" />.
        ///     When <paramref name="fadeTargets" /> is null or empty, fades to zero.
        ///     Target values are in normalized 0-1 source space (matching engine output).
        /// </summary>
        public void Begin(float[] currentValues, float duration, float[] fadeTargets)
        {
            _duration = Math.Max(0.01f, duration);
            Progress = 0f;
            IsActive = true;

            if (currentValues == null || currentValues.Length == 0)
            {
                _snapshot = Array.Empty<float>();
                _hasFadeTargets = false;
                return;
            }

            if (_snapshot.Length != currentValues.Length) _snapshot = new float[currentValues.Length];
            Array.Copy(currentValues, _snapshot, currentValues.Length);

            _hasFadeTargets = fadeTargets != null && fadeTargets.Length > 0;
            if (_hasFadeTargets)
            {
                if (_fadeTargets.Length != currentValues.Length) _fadeTargets = new float[currentValues.Length];
                int copyLen = Math.Min(fadeTargets.Length, _fadeTargets.Length);
                Array.Copy(fadeTargets, _fadeTargets, copyLen);
                if (copyLen < _fadeTargets.Length)
                    Array.Clear(_fadeTargets, copyLen, _fadeTargets.Length - copyLen);
            }
        }

        /// <summary>
        ///     Advances the fade and writes interpolated values to the output buffer.
        ///     Returns true while fading is still in progress, false when complete.
        /// </summary>
        public bool Tick(float deltaTime, float[] output)
        {
            if (!IsActive) return false;

            float dt = Math.Clamp(deltaTime, LipSyncConstants.MinDeltaTime, LipSyncConstants.MaxDeltaTimeForFade);
            Progress += dt / _duration;

            if (Progress >= 1f)
            {
                IsActive = false;
                Progress = 1f;
                WriteFinalValues(output);
                return false;
            }

            float alpha = 1f - ((1f - Progress) * (1f - Progress));

            int outputLength = output?.Length ?? 0;
            int limit = Math.Min(_snapshot.Length, outputLength);

            for (int i = 0; i < limit; i++)
            {
                float target = _hasFadeTargets && i < _fadeTargets.Length ? _fadeTargets[i] : 0f;
                output[i] = _snapshot[i] + (target - _snapshot[i]) * alpha;
            }

            for (int i = limit; i < outputLength; i++) output[i] = 0f;

            return true;
        }

        public void Reset()
        {
            IsActive = false;
            Progress = 0f;
            _hasFadeTargets = false;

            if (_snapshot.Length > 0) Array.Clear(_snapshot, 0, _snapshot.Length);
            if (_fadeTargets.Length > 0) Array.Clear(_fadeTargets, 0, _fadeTargets.Length);
        }

        private void WriteFinalValues(float[] output)
        {
            if (output == null) return;

            if (_hasFadeTargets)
            {
                int limit = Math.Min(_fadeTargets.Length, output.Length);
                Array.Copy(_fadeTargets, output, limit);
                if (limit < output.Length)
                    Array.Clear(output, limit, output.Length - limit);
            }
            else
            {
                Array.Clear(output, 0, output.Length);
            }
        }
    }
}
