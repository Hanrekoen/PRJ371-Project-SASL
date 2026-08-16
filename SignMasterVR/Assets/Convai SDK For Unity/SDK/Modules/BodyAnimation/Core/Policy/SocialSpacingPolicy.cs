using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Pure decision logic for social stepping / proxemics: when the conversation
    ///     partner sustains a position inside the character's personal-space bubble, decides a
    ///     single reposition point that puts the character just outside it again. Contains no
    ///     NavMesh/locomotion calls — those live in the runtime wiring
    ///     (<see cref="Components.ConvaiBodyAnimationController" />), which samples the returned
    ///     point onto the NavMesh and issues the actual move.
    /// </summary>
    /// <remarks>
    ///     Only active while <see cref="DialogueState" /> is Idle/Attending/Listening — stepping
    ///     away mid-sentence (Speaking) or while busy with an action/locomotion/PlayActionAt reads
    ///     wrong, so the caller passes both. Guards against retrigger spam with two independent,
    ///     alloc-free gates: a hysteresis latch (the conversant must be seen to back off past
    ///     <c>ComfortRadius + ProximityMargin</c> at least once since the last trigger before a
    ///     new one is considered) and a rolling per-minute budget (fixed-size ring buffer of
    ///     trigger timestamps, no LINQ/alloc). A budget token is spent the instant the policy
    ///     decides to fire, even if the caller's later NavMesh sample fails — a cornered character
    ///     must not retry-spam every tick.
    /// </remarks>
    internal sealed class SocialSpacingPolicy
    {
        /// <summary>Extra clearance (m) beyond <see cref="_comfortRadius" /> used both for the
        /// hysteresis release threshold and the reposition target distance.</summary>
        private const float ProximityMargin = 0.3f;

        /// <summary>Fixed ring capacity — matches the config's own upper clamp on repositions/minute.</summary>
        private const int RingCapacity = 10;

        private const float BudgetWindowSeconds = 60f;

        private readonly float _comfortRadius;
        private readonly float _comfortHoldSeconds;
        private readonly int _maxRepositionsPerMinute;

        private readonly float[] _triggerTimestamps = new float[RingCapacity];
        private int _triggerCount;
        private int _ringWriteIndex;

        private float _clockSeconds;
        private float _holdTimer;
        private bool _hysteresisCleared = true;

        public SocialSpacingPolicy(float comfortRadius, float comfortHoldSeconds, int maxRepositionsPerMinute)
        {
            _comfortRadius = Mathf.Clamp(comfortRadius, 0.3f, 2f);
            _comfortHoldSeconds = Mathf.Clamp(comfortHoldSeconds, 0.1f, 3f);
            _maxRepositionsPerMinute = Mathf.Clamp(maxRepositionsPerMinute, 1, RingCapacity);
        }

        /// <summary>
        ///     Advances the sustained-proximity timer and evaluates every gate. Returns
        ///     <c>true</c> exactly on the tick a reposition should be commanded, with
        ///     <paramref name="targetPosition" /> set to the world-space point (horizontal
        ///     direction away from the conversant, <see cref="_comfortRadius" /> +
        ///     <see cref="ProximityMargin" /> from it, at the character's current height).
        /// </summary>
        public bool Tick(
            float distanceToConversant,
            Vector3 characterPosition,
            Vector3 conversantPosition,
            DialogueState dialogueState,
            bool isBusy,
            float deltaTime,
            out Vector3 targetPosition)
        {
            targetPosition = default;
            _clockSeconds += deltaTime > 0f ? deltaTime : 0f;

            // Hysteresis tracking runs regardless of eligibility: a player who backs off while
            // the character is (briefly) busy still clears the latch.
            if (distanceToConversant > _comfortRadius + ProximityMargin)
                _hysteresisCleared = true;

            bool eligibleState = dialogueState == DialogueState.Idle ||
                                  dialogueState == DialogueState.Attending ||
                                  dialogueState == DialogueState.Listening;

            if (!eligibleState || isBusy)
            {
                _holdTimer = 0f;
                return false;
            }

            if (distanceToConversant >= _comfortRadius)
            {
                _holdTimer = 0f;
                return false;
            }

            _holdTimer += deltaTime > 0f ? deltaTime : 0f;
            if (_holdTimer < _comfortHoldSeconds) return false;

            if (!_hysteresisCleared) return false;
            if (!BudgetAvailable()) return false;

            targetPosition = ComputeTargetPosition(characterPosition, conversantPosition);
            _holdTimer = 0f;
            _hysteresisCleared = false;
            RecordTrigger();
            return true;
        }

        private Vector3 ComputeTargetPosition(Vector3 characterPosition, Vector3 conversantPosition)
        {
            Vector3 away = characterPosition - conversantPosition;
            away.y = 0f;

            // Degenerate case (character standing exactly where the conversant is measured):
            // pick a fixed, deterministic horizontal direction rather than an undefined one.
            Vector3 direction = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;

            float targetDistance = _comfortRadius + ProximityMargin;
            Vector3 target = conversantPosition + direction * targetDistance;
            target.y = characterPosition.y;
            return target;
        }

        private bool BudgetAvailable()
        {
            PruneExpiredTokens();
            return _triggerCount < _maxRepositionsPerMinute;
        }

        private void PruneExpiredTokens()
        {
            while (_triggerCount > 0)
            {
                int oldestIndex = (_ringWriteIndex - _triggerCount + RingCapacity) % RingCapacity;
                if (_clockSeconds - _triggerTimestamps[oldestIndex] <= BudgetWindowSeconds) break;
                _triggerCount--;
            }
        }

        private void RecordTrigger()
        {
            PruneExpiredTokens();
            _triggerTimestamps[_ringWriteIndex] = _clockSeconds;
            _ringWriteIndex = (_ringWriteIndex + 1) % RingCapacity;
            if (_triggerCount < RingCapacity) _triggerCount++;
        }
    }
}
