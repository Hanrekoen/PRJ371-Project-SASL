using UnityEngine;

namespace Convai.Runtime.Animation
{
    internal struct SpeechEnergyWindow
    {
        private readonly float[] _samples;
        private int _head;
        private int _count;

        public SpeechEnergyWindow(int capacity)
        {
            _samples = new float[Mathf.Max(1, capacity)];
            _head = 0;
            _count = 0;
        }

        public int Capacity => _samples.Length;
        public int Count => _count;

        public void Push(float amplitude)
        {
            _samples[_head] = amplitude;
            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public float ComputeRms()
        {
            if (_count == 0) return 0f;

            double sumOfSquares = 0.0;
            for (int i = 0; i < _count; i++)
            {
                float v = _samples[i];
                sumOfSquares += v * v;
            }

            return Mathf.Sqrt((float)(sumOfSquares / _count));
        }
    }
}
