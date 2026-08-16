using System;

namespace Convai.Modules.ClientVoiceActivity.Sentis
{
    internal sealed class StreamingLinearResampler
    {
        private const int TargetSampleRate = 16000;
        private int _sourceSampleRate;
        private double _sourcePosition;
        private float _left;
        private float _right;
        private bool _initialized;

        internal void Configure(int sourceSampleRate)
        {
            if (sourceSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceSampleRate));

            _sourceSampleRate = sourceSampleRate;
            Reset();
        }

        internal void Reset()
        {
            _sourcePosition = 0d;
            _left = 0f;
            _right = 0f;
            _initialized = false;
        }

        internal bool TryReadFrame(SpscFloatRingBuffer source, float[] destination)
        {
            if (_sourceSampleRate <= 0 || source == null || destination == null || destination.Length == 0)
                return false;

            double ratio = (double)_sourceSampleRate / TargetSampleRate;
            int requiredSamples =
                (int)Math.Ceiling(destination.Length * ratio) + (_initialized ? 0 : 2);
            if (source.Count < requiredSamples)
                return false;

            if (!_initialized)
            {
                source.TryRead(out _left);
                source.TryRead(out _right);
                _initialized = true;
            }

            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = _left + (_right - _left) * (float)_sourcePosition;
                _sourcePosition += ratio;

                while (_sourcePosition >= 1d)
                {
                    _left = _right;
                    source.TryRead(out _right);
                    _sourcePosition -= 1d;
                }
            }

            return true;
        }
    }
}
