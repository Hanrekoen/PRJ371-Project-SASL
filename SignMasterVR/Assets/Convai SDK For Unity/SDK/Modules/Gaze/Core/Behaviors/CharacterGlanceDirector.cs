using Convai.Runtime.Embodiment;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Idle character-to-character glances: while the character is idle (no engaged
    ///     target), occasionally signals that it should glance at a nearby character. The
    ///     controller turns each decision into a short, soft, low-priority scripted glance —
    ///     the "occasional mutual glance" beat between idle NPCs that the arbiter's engagement
    ///     policy would otherwise suppress in Idle. Timing comes from the
    ///     <c>CharacterGazeTargetProvider</c>, not the profile, so all mutual-gaze tuning
    ///     lives on the one component.
    /// </summary>
    internal sealed class CharacterGlanceDirector
    {
        private float _countdown;
        private bool _armed;

        public void Reset()
        {
            _countdown = 0f;
            _armed = false;
        }

        /// <summary>
        ///     Returns <c>true</c> when an idle character glance should fire this tick. Only
        ///     counts down while <paramref name="idleActive" /> — engaged states disarm it.
        /// </summary>
        public bool Tick(
            bool enabled,
            float intervalMin,
            float intervalMax,
            float deltaTime,
            bool idleActive,
            ref DeterministicEmbodimentRandom random)
        {
            if (!enabled || !idleActive)
            {
                _armed = false;
                return false;
            }

            if (!_armed)
            {
                _armed = true;
                _countdown = random.Range(intervalMin, intervalMax);
            }

            _countdown -= deltaTime;
            if (_countdown > 0f) return false;

            _countdown = random.Range(intervalMin, intervalMax);
            return true;
        }
    }
}
