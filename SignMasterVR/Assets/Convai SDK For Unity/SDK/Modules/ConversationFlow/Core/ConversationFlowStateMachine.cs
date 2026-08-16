using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Modules.ConversationFlow.Core
{
    /// <summary>
    ///     POCO dialogue-phase state machine. Takes per-frame input + dt, returns the current
    ///     <see cref="DialogueStateReading" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The state machine is deliberately framework-free so it is unit-testable in
    ///         edit mode. The authoritative transition rules are:
    ///     </para>
    ///     <list type="number">
    ///         <item><description>Character not ready -> <see cref="DialogueState.Idle" />.</description></item>
    ///         <item>
    ///             <description>
    ///                 Player starts speaking -> <see cref="DialogueState.Listening" />.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Player stops speaking (no pending turn) -> <see cref="DialogueState.Attending" />
    ///                 for <see cref="ConversationFlowTimings.AttendingGracePeriod" />.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Player finalizes transcript -> <see cref="DialogueState.Thinking" />
    ///                 until the character begins speaking or the max hold elapses.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Character speech begins (transport or LipSync) ->
    ///                 <see cref="DialogueState.Speaking" />.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Character speech ends, interrupted ->
    ///                 <see cref="DialogueState.Interrupted" /> -> <see cref="DialogueState.Attending" />.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Character speech ends, normal -> <see cref="DialogueState.Settling" /> ->
    ///                 <see cref="DialogueState.Attending" /> -> <see cref="DialogueState.Idle" />
    ///                 after an inactivity timeout.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <see cref="DialogueState.Reacting" /> is reserved for explicit one-shot signals
    ///         (emotion spike, barge-in acknowledgement) and is driven by <see cref="Pulse" />
    ///         rather than automatic derivation.
    ///     </para>
    /// </remarks>
    internal sealed class ConversationFlowStateMachine
    {
        private DialogueState _primary = DialogueState.Idle;
        private DialogueState _blendTo = DialogueState.Idle;
        private float _blendWeight;
        private float _timeInPrimary;

        private float _thinkingEntryTime;
        private float _attendingEntryTime;
        private float _settlingEntryTime;
        private float _interruptedEntryTime;
        private float _reactingRemaining;
        private float _elapsedSeconds;
        private float _lastEngagementTime = float.NegativeInfinity;

        /// <summary>Current authoritative reading. Updated each call to <see cref="Tick" />.</summary>
        public DialogueStateReading Current { get; private set; } = DialogueStateReading.Idle;

        /// <summary>
        ///     Raised when <see cref="DialogueStateReading.Primary" /> changes (NOT on blend
        ///     progress). Consumers use this to trigger one-shot reactions.
        /// </summary>
        public event System.Action<DialogueStateReading> Changed;

        /// <summary>
        ///     Advances the state machine by <paramref name="deltaTime" /> and returns the
        ///     resulting reading.
        /// </summary>
        public DialogueStateReading Tick(
            in ConversationFlowInputs inputs,
            in ConversationFlowTimings timings,
            float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            _elapsedSeconds += deltaTime;
            UpdateEngagementClock(inputs);

            DialogueState desired = Derive(inputs, timings);

            if (_reactingRemaining > 0f)
            {
                _reactingRemaining -= deltaTime;
                if (_reactingRemaining <= 0f)
                {
                    _reactingRemaining = 0f;
                }
                else
                {
                    desired = DialogueState.Reacting;
                }
            }

            if (desired != _primary)
                TransitionTo(desired, timings);

            AdvanceBlend(deltaTime, timings.TransitionDuration);
            _timeInPrimary += deltaTime;

            float energy = ComputeEnergy(inputs, timings);

            Current = new DialogueStateReading(
                _primary,
                _blendTo,
                _blendWeight,
                _timeInPrimary,
                energy);

            return Current;
        }

        /// <summary>
        ///     Forces a short pulse through <see cref="DialogueState.Reacting" /> for
        ///     <paramref name="duration" /> seconds. The state machine returns to its derived
        ///     state automatically afterwards.
        /// </summary>
        public void Pulse(DialogueState state, float duration)
        {
            if (state != DialogueState.Reacting) return;
            if (duration <= 0f) return;
            _reactingRemaining = duration;
        }

        /// <summary>Resets the state machine; used on character shutdown.</summary>
        public void Reset()
        {
            _primary = DialogueState.Idle;
            _blendTo = DialogueState.Idle;
            _blendWeight = 0f;
            _timeInPrimary = 0f;
            _reactingRemaining = 0f;
            _thinkingEntryTime = 0f;
            _attendingEntryTime = 0f;
            _settlingEntryTime = 0f;
            _interruptedEntryTime = 0f;
            _elapsedSeconds = 0f;
            _lastEngagementTime = float.NegativeInfinity;
            Current = DialogueStateReading.Idle;
        }

        private DialogueState Derive(in ConversationFlowInputs inputs, in ConversationFlowTimings timings)
        {
            if (!inputs.IsCharacterReady)
                return DialogueState.Idle;

            if ((inputs.IsCharacterSpeaking || inputs.IsLipSyncSpeaking) &&
                ShouldHoldThinkingBeforeSpeech(inputs, timings))
            {
                return DialogueState.Thinking;
            }

            if (inputs.IsCharacterSpeaking || inputs.IsLipSyncSpeaking)
                return DialogueState.Speaking;

            if (inputs.IsPlayerSpeaking)
                return DialogueState.Listening;

            // Character was just speaking and a turn-completed signal arrived.
            if (inputs.TurnJustCompleted || _primary == DialogueState.Interrupted)
            {
                if (inputs.WasRecentlyInterrupted || _primary == DialogueState.Interrupted)
                {
                    // First entry: _interruptedEntryTime has not been set yet (TransitionTo records
                    // it after Derive returns). Return immediately so the freeze window is measured
                    // from the actual transition instant, not from state-machine construction.
                    if (_primary != DialogueState.Interrupted)
                        return DialogueState.Interrupted;

                    if (_elapsedSeconds - _interruptedEntryTime < timings.InterruptedFreezeDuration)
                        return DialogueState.Interrupted;
                    return ResolveEngagedRestState(timings);
                }

                if (_primary == DialogueState.Settling)
                {
                    if (_elapsedSeconds - _settlingEntryTime < timings.SettlingDuration)
                        return DialogueState.Settling;
                    return ResolveEngagedRestState(timings);
                }

                return DialogueState.Settling;
            }

            // Pending player turn without active player speech - Thinking window.
            if (inputs.HasPendingPlayerTurn)
            {
                // If we've already timed out from Thinking and transitioned to Attending,
                // don't bounce back to Thinking just because pendingTurn is still true.
                if (_primary == DialogueState.Attending)
                {
                    float heldTime = _elapsedSeconds - _thinkingEntryTime;
                    if (heldTime >= timings.ThinkingMaxHold)
                        return DialogueState.Attending;
                }

                if (_primary != DialogueState.Thinking)
                    return DialogueState.Thinking;

                float held = _elapsedSeconds - _thinkingEntryTime;
                if (held < timings.ThinkingMinHold)
                    return DialogueState.Thinking;

                if (held >= timings.ThinkingMaxHold)
                    return ResolveEngagedRestState(timings);
                return DialogueState.Thinking;
            }

            // Transient Attending after the player finished speaking, followed by an engaged
            // hold before the character is allowed to cool back to Idle.
            if (_primary == DialogueState.Listening || _primary == DialogueState.Attending)
            {
                if (_elapsedSeconds - _attendingEntryTime < timings.AttendingGracePeriod)
                    return DialogueState.Attending;
                return ResolveEngagedRestState(timings);
            }

            // Finish off any trailing Settling window.
            if (_primary == DialogueState.Settling)
            {
                if (_elapsedSeconds - _settlingEntryTime < timings.SettlingDuration)
                    return DialogueState.Settling;
                return ResolveEngagedRestState(timings);
            }

            return ResolveEngagedRestState(timings);
        }

        private void TransitionTo(DialogueState next, in ConversationFlowTimings timings)
        {
            DialogueState previous = _primary;
            _primary = next;
            _blendTo = next;
            _blendWeight = timings.TransitionDuration > 0f ? 0f : 1f;
            _timeInPrimary = 0f;

            switch (next)
            {
                case DialogueState.Thinking:
                    _thinkingEntryTime = _elapsedSeconds;
                    break;
                case DialogueState.Attending:
                    _attendingEntryTime = _elapsedSeconds;
                    break;
                case DialogueState.Settling:
                    _settlingEntryTime = _elapsedSeconds;
                    break;
                case DialogueState.Interrupted:
                    _interruptedEntryTime = _elapsedSeconds;
                    break;
            }

            if (previous != next)
                Changed?.Invoke(new DialogueStateReading(next, next, _blendWeight, 0f, 0f));
        }

        private void AdvanceBlend(float deltaTime, float transitionDuration)
        {
            if (transitionDuration <= 0f)
            {
                _blendWeight = 1f;
                return;
            }

            _blendWeight += deltaTime / transitionDuration;
            if (_blendWeight > 1f) _blendWeight = 1f;
        }

        private void UpdateEngagementClock(in ConversationFlowInputs inputs)
        {
            if (!inputs.IsCharacterReady)
            {
                _lastEngagementTime = float.NegativeInfinity;
                return;
            }

            if (inputs.IsPlayerSpeaking ||
                inputs.HasPendingPlayerTurn ||
                // Doing what you were asked is engagement. Without this a character sent to walk
                // somewhere decays to Idle mid-errand, and every behaviour keyed off the state —
                // most visibly gaze — stops treating the player as present.
                inputs.IsPerformingAction ||
                inputs.IsCharacterSpeaking ||
                inputs.IsLipSyncSpeaking ||
                inputs.TurnJustCompleted ||
                inputs.WasRecentlyInterrupted ||
                _primary == DialogueState.Listening ||
                _primary == DialogueState.Thinking ||
                _primary == DialogueState.Speaking ||
                _primary == DialogueState.Settling ||
                _primary == DialogueState.Interrupted)
            {
                _lastEngagementTime = _elapsedSeconds;
            }
        }

        private bool ShouldHoldThinkingBeforeSpeech(
            in ConversationFlowInputs inputs,
            in ConversationFlowTimings timings)
        {
            if (_primary == DialogueState.Thinking)
                return _elapsedSeconds - _thinkingEntryTime < timings.ThinkingMinHold;

            return inputs.HasPendingPlayerTurn;
        }

        private DialogueState ResolveEngagedRestState(in ConversationFlowTimings timings)
        {
            if (!HasRecentEngagement(timings))
                return DialogueState.Idle;

            return DialogueState.Attending;
        }

        private bool HasRecentEngagement(in ConversationFlowTimings timings)
        {
            if (timings.IdleReturnDelay <= 0f)
                return false;

            if (float.IsNegativeInfinity(_lastEngagementTime))
                return false;

            return _elapsedSeconds - _lastEngagementTime < timings.IdleReturnDelay;
        }

        private float ComputeEnergy(in ConversationFlowInputs inputs, in ConversationFlowTimings timings)
        {
            return _primary switch
            {
                DialogueState.Idle => 0.1f,
                DialogueState.Listening => 0.35f,
                DialogueState.Attending => 0.4f,
                DialogueState.Thinking => 0.25f,
                DialogueState.Speaking => timings.SpeakingBaseEnergy,
                DialogueState.Reacting => 0.9f,
                DialogueState.Interrupted => 0.2f,
                DialogueState.Settling => 0.3f,
                _ => 0.1f
            };
        }
    }
}
