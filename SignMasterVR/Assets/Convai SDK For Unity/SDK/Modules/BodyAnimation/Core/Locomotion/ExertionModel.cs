using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Expression-tick POCO that turns locomotion speed into a slow, smoothed
    ///     "exertion" signal, 0..1: it climbs while the character moves at jog/run pace and
    ///     settles back to 0 at rest or while walking. Published through
    ///     <see cref="Convai.Domain.Embodiment.Interfaces.IExertionSource" /> so a peer module
    ///     (Body Language) can fold physical effort into its breathing.
    /// </summary>
    internal sealed class ExertionModel
    {
        private float _value;

        /// <summary>Current exertion, 0 (rested) .. 1 (full sustained run effort).</summary>
        public float Value01 => _value;

        /// <summary>
        ///     Advances the model one tick. <paramref name="speed" /> is the character's current
        ///     horizontal speed (m/s); the effort input driving the rise is a linear ramp from 0
        ///     at <paramref name="walkSpeed" /> to 1 at <paramref name="jogSpeed" /> — walking at
        ///     or below walk pace contributes essentially nothing, only jog/run pace drives the
        ///     signal up. While the effort input is above the current value, exertion rises
        ///     toward it at a rate scaled by the input itself, so full effort (1) reaches 1 over
        ///     <paramref name="riseSeconds" /> and partial effort rises proportionally slower.
        ///     Otherwise (effort at or below the current value — slower movement or a stop),
        ///     exertion decays toward 0 at a constant rate that would empty a full reading over
        ///     <paramref name="recoverySeconds" />.
        /// </summary>
        public void Tick(
            float deltaTime, float speed, float walkSpeed, float jogSpeed,
            float riseSeconds, float recoverySeconds)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            float span = Mathf.Max(0.01f, jogSpeed - walkSpeed);
            float effort = Mathf.Clamp01((speed - walkSpeed) / span);
            float rise = Mathf.Max(0.01f, riseSeconds);
            float recovery = Mathf.Max(0.01f, recoverySeconds);

            _value = effort > _value
                ? Mathf.Min(effort, _value + effort / rise * dt)
                : Mathf.Max(0f, _value - dt / recovery);

            _value = Mathf.Clamp01(_value);
        }

        /// <summary>Resets to unexerted (0).</summary>
        public void Reset() => _value = 0f;
    }
}
