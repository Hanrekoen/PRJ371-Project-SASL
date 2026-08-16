using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Components;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     Resolves product-level focus scope from dialogue and the authoritative character
    ///     speech signal. The short release grace prevents VAD edges from flickering focus.
    /// </summary>
    internal sealed class GazeFocusScopeEvaluator
    {
        internal const float SpeechReleaseGraceSeconds = 0.2f;
        private float _speechReleaseRemaining;

        public bool Evaluate(
            GazeEyeContactMode mode,
            DialogueState state,
            bool characterSpeaking,
            float deltaTime)
        {
            if (characterSpeaking || state == DialogueState.Speaking)
                _speechReleaseRemaining = SpeechReleaseGraceSeconds;
            else
                _speechReleaseRemaining = System.Math.Max(0f, _speechReleaseRemaining - System.Math.Max(0f, deltaTime));

            return mode switch
            {
                GazeEyeContactMode.AlwaysLock => true,
                GazeEyeContactMode.ConversationLock => state != DialogueState.Idle,
                GazeEyeContactMode.SpeakingFocus => characterSpeaking ||
                                                   state == DialogueState.Speaking ||
                                                   _speechReleaseRemaining > 0f,
                _ => false
            };
        }

        public void Reset() => _speechReleaseRemaining = 0f;
    }
}
