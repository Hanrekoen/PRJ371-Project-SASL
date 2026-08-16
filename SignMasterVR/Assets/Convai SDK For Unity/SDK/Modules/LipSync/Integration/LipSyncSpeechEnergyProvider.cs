using Convai.Runtime.Animation;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncSpeechEnergyProvider : IConfigurableSpeechEnergyProvider
    {
        private readonly ConvaiLipSyncComponent _lipSync;
        private SpeechEnergyWindow _window;
        private int _capacity;
        private float _current;

        public LipSyncSpeechEnergyProvider(ConvaiLipSyncComponent lipSync, float windowSeconds = 0.08f)
        {
            _lipSync = lipSync;
            Configure(windowSeconds);
        }

        public float Current => _current;

        public void Configure(float windowSeconds)
        {
            int capacity = Mathf.Max(4, Mathf.CeilToInt(Mathf.Clamp(windowSeconds, 0.02f, 0.5f) * 120f));
            if (_capacity == capacity) return;

            _capacity = capacity;
            _window = new SpeechEnergyWindow(capacity);
            _current = 0f;
        }

        public void Sample(float deltaTime)
        {
            if (_lipSync == null)
            {
                _current = 0f;
                return;
            }

            float amplitude = 0f;
            if (_lipSync.IsPlaying)
            {
                BlendshapeSnapshot snapshot = _lipSync.GetBlendshapeSnapshot();
                if (snapshot.IsValid)
                    amplitude = ComputeSnapshotAmplitude(snapshot);
            }

            _window.Push(amplitude);
            _current = Mathf.Clamp01(_window.ComputeRms());
        }

        private static float ComputeSnapshotAmplitude(in BlendshapeSnapshot snapshot)
        {
            int count = snapshot.Count;
            if (count <= 0) return 0f;

            float maxValue = 0f;
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float v = snapshot.GetValue(i);
                if (v < 0f) v = -v;
                if (v > maxValue) maxValue = v;
                sum += v;
            }

            float mean = sum / count;
            return Mathf.Clamp01(mean * 4f + maxValue * 0.25f);
        }
    }
}
