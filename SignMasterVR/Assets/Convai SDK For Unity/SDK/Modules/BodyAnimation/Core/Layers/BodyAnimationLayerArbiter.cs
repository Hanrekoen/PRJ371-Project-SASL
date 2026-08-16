using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    internal readonly struct LayerArbitrationInput
    {
        public readonly float Locomotion;
        public readonly float Talk;
        public readonly float Action;
        public readonly float Pointing;
        public readonly float MovingTalk;
        public readonly float Beat;
        public readonly float FullBodyOwnership;
        public readonly bool ActionOwnsUpperBody;

        public LayerArbitrationInput(
            float locomotion, float talk, float action, float pointing, float movingTalk,
            float beat, float fullBodyOwnership, bool actionOwnsUpperBody)
        {
            Locomotion = locomotion;
            Talk = talk;
            Action = action;
            Pointing = pointing;
            MovingTalk = movingTalk;
            Beat = beat;
            FullBodyOwnership = fullBodyOwnership;
            ActionOwnsUpperBody = actionOwnsUpperBody;
        }
    }

    /// <summary>
    /// Resolves the final six-port pose weights after every layer has reported its current
    /// envelope. Priority is deterministic: full-body action, upper-body action, pointing,
    /// conversational overlays, then beat. Ownership changes ride the owning layer's existing
    /// envelope, so suppression cannot introduce a one-frame weight cut.
    /// </summary>
    internal sealed class BodyAnimationLayerArbiter
    {
        private readonly float[] _desired = new float[LayerPorts.Count];
        private readonly float[] _final = new float[LayerPorts.Count];
        private readonly string[] _owner = new string[LayerPorts.Count];

        public float GetDesiredWeight(int port) => _desired[port];
        public float GetFinalWeight(int port) => _final[port];
        public string GetOwner(int port) => _owner[port] ?? string.Empty;

        public void Resolve(
            LocomotionLayer locomotion,
            TalkLayer talk,
            ActionLayer action,
            PointingLayer pointing)
        {
            Resolve(new LayerArbitrationInput(
                locomotion?.Weight ?? 0f,
                talk?.Weight ?? 0f,
                action?.Weight ?? 0f,
                pointing?.Weight ?? 0f,
                talk?.MovingWeight ?? 0f,
                talk?.BeatWeight ?? 0f,
                action?.FullBodyDuck01 ?? 0f,
                action != null && action.SuppressesConversationOverlays));
        }

        internal void Resolve(in LayerArbitrationInput input)
        {
            _desired[LayerPorts.Locomotion] = input.Locomotion;
            _desired[LayerPorts.Talk] = input.Talk;
            _desired[LayerPorts.Action] = input.Action;
            _desired[LayerPorts.Pointing] = input.Pointing;
            _desired[LayerPorts.TalkMoving] = input.MovingTalk;
            _desired[LayerPorts.TalkBeat] = input.Beat;

            float actionWeight = Mathf.Clamp01(_desired[LayerPorts.Action]);
            float fullBodyOwnership = Mathf.Clamp01(input.FullBodyOwnership);
            float upperBodyOwnership = input.ActionOwnsUpperBody
                ? actionWeight
                : 0f;
            float pointOwnership = Mathf.Clamp01(_desired[LayerPorts.Pointing]) * (1f - actionWeight);

            _final[LayerPorts.Locomotion] = _desired[LayerPorts.Locomotion];
            _final[LayerPorts.Action] = actionWeight;
            _final[LayerPorts.Pointing] = _desired[LayerPorts.Pointing] * (1f - actionWeight);

            float conversationAvailability = 1f - Mathf.Max(fullBodyOwnership, upperBodyOwnership);
            _final[LayerPorts.Talk] = _desired[LayerPorts.Talk] * conversationAvailability;
            _final[LayerPorts.TalkMoving] = _desired[LayerPorts.TalkMoving] * conversationAvailability;
            _final[LayerPorts.TalkBeat] = _desired[LayerPorts.TalkBeat] *
                                          conversationAvailability *
                                          (1f - pointOwnership);

            _owner[LayerPorts.Locomotion] = "Locomotion";
            _owner[LayerPorts.Action] = actionWeight > 0f ? "Action" : string.Empty;
            _owner[LayerPorts.Pointing] = actionWeight > 0f ? "Action" : "Pointing";

            string conversationOwner = fullBodyOwnership > 0f
                ? "Full-body action"
                : upperBodyOwnership > 0f
                    ? "Upper-body action"
                    : "Conversation";
            _owner[LayerPorts.Talk] = conversationOwner;
            _owner[LayerPorts.TalkMoving] = conversationOwner;
            _owner[LayerPorts.TalkBeat] = actionWeight > 0f
                ? "Action"
                : pointOwnership > 0f
                    ? "Pointing"
                    : "Conversation beat";

            for (int i = 0; i < LayerPorts.Count; i++)
                _final[i] = Mathf.Clamp01(_final[i]);
        }
    }
}
