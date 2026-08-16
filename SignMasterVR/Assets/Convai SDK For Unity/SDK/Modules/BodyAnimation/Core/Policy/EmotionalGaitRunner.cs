using System;
using Convai.Domain.Embodiment.Readings;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     The emotion-driven gait-multiplier lifecycle, including the single
    ///     turn-off tick when the feature is switched off — <see cref="EmotionGaitModulator" /> is
    ///     the pure mapping/smoothing; this wraps its on/off bookkeeping and the delivery to the
    ///     locomotion drive.
    /// </summary>
    /// <remarks>
    ///     <see cref="Configure" /> takes the apply-multiplier delegate once (at build time, e.g.
    ///     <c>OnEnable</c>) rather than per tick, so <see cref="Tick" /> never allocates — the
    ///     delegate is a closure over the controller's own <c>_locomotion</c> field, which is
    ///     re-resolved only on a first build, not on a set-swap handoff, so a single configure call
    ///     stays correct across handoffs too.
    /// </remarks>
    internal sealed class EmotionalGaitRunner
    {
        private readonly EmotionGaitModulator _modulator = new();
        private Action<float> _applyMultiplier;
        private bool _active;

        internal void Configure(Action<float> applyMultiplier) => _applyMultiplier = applyMultiplier;

        /// <summary>
        ///     Only touches the locomotion drive at all while the feature is on (or on the single
        ///     tick it turns off) — off by default leaves the commanded speed path untouched.
        /// </summary>
        internal void Tick(bool enabled, in EmotionReading emotion, float range, float deltaTime)
        {
            if (enabled)
            {
                _active = true;
                float multiplier = _modulator.Tick(in emotion, range, deltaTime);
                _applyMultiplier?.Invoke(multiplier);
            }
            else if (_active)
            {
                _active = false;
                _modulator.Reset();
                _applyMultiplier?.Invoke(1f);
            }
        }

        internal void Reset()
        {
            _modulator.Reset();
            _active = false;
        }
    }
}
