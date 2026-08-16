using UnityEngine;
using Convai.Modules.BodyAnimation.Core.Graph;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>
    ///     Short additive one-shot overlay for speech-rhythm beat gestures, owned by
    ///     <see cref="TalkLayer" /> and connected to its own root-mixer port
    ///     (<see cref="LayerPorts.TalkBeat" />, additive, arms-only). A beat plays a single
    ///     non-looping clip as a delta on top of whichever talk port is currently visible —
    ///     stationary override or the moving overlay — and fades back out on its own once the
    ///     clip finishes; it never owns a mask or replaces the talk pose.
    /// </summary>
    internal sealed class TalkBeatOverlay
    {
        /// <summary>Weight ramp-in for a beat gesture — fast, so it reads as a snap on the stress.</summary>
        internal const float FadeInSeconds = 0.06f;

        /// <summary>Weight ramp-out once the beat's clip finishes.</summary>
        internal const float FadeOutSeconds = 0.28f;

        private enum Mode
        {
            Off,
            Playing,
            FadingOut
        }

        private LayerRuntime _runtime;
        private OneShotSlot _slot;
        private Mode _mode = Mode.Off;
        private float _fade01;
        private float _targetWeight;

        public float Weight { get; private set; }

        internal string ModeForTests => _mode.ToString();
        internal string ActiveClipNameForTests => _slot?.ActiveClipName ?? "(none)";

        public void Initialize(LayerRuntime runtime, int port, AvatarMask armsMask)
        {
            _runtime = runtime;
            _slot = new OneShotSlot(runtime.Graph, runtime.Config.BlendCurve,
                msg => runtime.Trace?.Warning($"[TalkLayer.Beat] {msg}"));
            _slot.Completed += HandleCompleted;
            runtime.Mixer.ConnectLayer(port, _slot.Playable, armsMask, additive: true);
        }

        public void Teardown()
        {
            if (_slot != null)
                _slot.Completed -= HandleCompleted;

            _slot = null;
            _runtime = null;
            _mode = Mode.Off;
            _fade01 = 0f;
            _targetWeight = 0f;
            Weight = 0f;
        }

        /// <summary>
        ///     Fires a new beat one-shot at <paramref name="targetWeight" /> (already resolved
        ///     from onset strength / cue intensity, proximity, and the config scale — clamped
        ///     0..1 here as a safety net). Restarts if a beat is already playing: a fresh onset
        ///     always wins over a still-settling previous one.
        /// </summary>
        public void Play(AnimationClip clip, float targetWeight)
        {
            if (clip == null || _slot == null) return;

            _targetWeight = Mathf.Clamp01(targetWeight);
            var spec = OneShotSpec.For(clip);
            _slot.Play(in spec, _mode == Mode.Off ? 0f : FadeInSeconds);
            _mode = Mode.Playing;
        }

        public void Tick(float deltaTime)
        {
            if (_slot == null) return;

            _slot.Tick(deltaTime);

            bool on = _mode == Mode.Playing;
            float duration = Mathf.Max(0.01f, on ? FadeInSeconds : FadeOutSeconds);
            _fade01 = Mathf.MoveTowards(_fade01, on ? 1f : 0f, deltaTime / duration);
            Weight = Mathf.Clamp01(_fade01) * _targetWeight;

            if (_mode == Mode.FadingOut && _fade01 <= 0f)
            {
                _slot.Clear();
                _mode = Mode.Off;
                _targetWeight = 0f;
            }
        }

        /// <summary>Stops advancing a late speech beat and dissolves it from its live pose.</summary>
        public void Release()
        {
            if (_mode == Mode.Off || _slot == null) return;
            _slot.Freeze();
            _mode = Mode.FadingOut;
        }

        private void HandleCompleted()
        {
            if (_mode != Mode.Playing) return;
            _mode = Mode.FadingOut;
        }
    }
}
