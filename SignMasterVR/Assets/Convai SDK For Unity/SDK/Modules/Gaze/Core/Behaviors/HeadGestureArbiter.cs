using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Single decision point between the two producers of the head/torso solver's gesture
    ///     channel: the internal <see cref="BackchannelDirector" /> (listening acknowledgment
    ///     nods) and an external <see cref="IHeadGestureChannel" /> program (scripted
    ///     nod/shake/tilt from Body Language). Exactly one of the two ever contributes to the
    ///     solver input on a given tick — this is the mechanism that prevents a double-nod when
    ///     both want to play at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>External wins.</b> While the channel reports an active, positive-weight
    ///         offset, the arbiter's output IS that offset (scaled by its weight) and the
    ///         backchannel's own contribution is held at zero. The arbiter never reaches into
    ///         <see cref="BackchannelDirector" /> to cancel it directly — instead
    ///         <see cref="ExternalActive" /> is exposed for the controller to OR into the same
    ///         <c>suppressed</c> input the director already accepts, so the nod (if one happens
    ///         to be mid-flight) fades out through its own shipped
    ///         <c>CancelFadeDegreesPerSecond</c> path. No new cancellation mechanics were added
    ///         to the director to make this work.
    ///     </para>
    ///     <para>
    ///         <b>Shared refractory.</b> After an external program's offset drops back to zero
    ///         weight, the arbiter keeps reporting <see cref="ExternalActive" /> (which keeps the
    ///         backchannel suppressed) for <c>PostExternalRefractorySeconds</c> more, so a
    ///         backchannel nod can never start the instant a scripted gesture ends and read as a
    ///         machine-gun double-tap. There is no cooldown in the other direction — an external
    ///         request may interrupt the backchannel at any time; Body Language rate-limits its
    ///         own programs.
    ///     </para>
    ///     <para>
    ///         <b>Aversion gate.</b> While the character is averting gaze, applying an external
    ///         program would read as a nod through a glance-away, so its contribution is faded to
    ///         zero over <c>AversionFadeSeconds</c> and faded back in once aversion ends. The
    ///         external program itself (the channel's own envelope/clock) keeps advancing
    ///         regardless — only ITS APPLICATION here is gated, exactly as the plan specifies.
    ///         Backchannel behavior is completely unaffected by aversion, same as today.
    ///     </para>
    ///     <para>
    ///         <b>No-channel path.</b> With no channel registered (or one that never reports an
    ///         active offset), <see cref="Compose" /> is a zero-allocation passthrough: the
    ///         output equals the backchannel offset handed in, roll is zero, and no
    ///         smoothing/fade state advances — bit-identical to the arbiter not existing at all.
    ///     </para>
    /// </remarks>
    internal sealed class HeadGestureArbiter
    {
        /// <summary>
        ///     Cooldown (seconds) after an external program completes during which the
        ///     backchannel stays suppressed. See the type remarks' "Shared refractory" note.
        /// </summary>
        private const float PostExternalRefractorySeconds = 0.75f;

        /// <summary>
        ///     Fade duration (seconds) for the external contribution's aversion gate, in both
        ///     directions (fading out on aversion start, back in on aversion end).
        /// </summary>
        private const float AversionFadeSeconds = 0.2f;

        private float _refractoryRemaining;
        private float _aversionFadeWeight = 1f;
        private Vector2 _output;
        private float _rollOutput;
        private bool _externalActive;
        private bool _hasExternalOffset;
        private HeadGestureOffset _externalOffset;

        /// <summary>
        ///     Current arbiter output (yaw/pitch degrees) to feed the solver's
        ///     <c>HeadTorsoSolveInput.GestureOffset</c> this tick.
        /// </summary>
        public Vector2 Offset => _output;

        /// <summary>
        ///     Current arbiter roll output (degrees) to feed the solver's
        ///     <c>HeadTorsoSolveInput.GestureRollDegrees</c> this tick.
        /// </summary>
        public float RollDegrees => _rollOutput;

        /// <summary>
        ///     True while an external program is active OR the post-external refractory is still
        ///     running. The controller ORs this into the backchannel's <c>suppressed</c> input —
        ///     this is the entire no-double-nod mechanism; see the type remarks.
        /// </summary>
        public bool ExternalActive => _externalActive;

        public void Reset()
        {
            _refractoryRemaining = 0f;
            _aversionFadeWeight = 1f;
            _output = Vector2.zero;
            _rollOutput = 0f;
            _externalActive = false;
        }

        /// <summary>
        ///     Reads the channel and updates <see cref="ExternalActive" /> for this tick, before
        ///     the controller ticks <see cref="BackchannelDirector" />. Split out from
        ///     <see cref="Compose" /> so the controller can fold this tick's external-active
        ///     state into the SAME tick's backchannel <c>suppressed</c> input (the no-double-nod
        ///     mechanism needs the freshest possible signal, not last frame's) without ticking
        ///     the backchannel before knowing whether an external program just started.
        /// </summary>
        /// <param name="channel">May be null (no producer registered on the context slot).</param>
        /// <param name="isAverting">Whether the gaze aversion beat is currently active.</param>
        /// <param name="deltaTime">Frame delta seconds.</param>
        public void SenseExternal(IHeadGestureChannel channel, bool isAverting, float deltaTime)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (channel == null)
            {
                // The producer is gone entirely (unregistered / disabled) — this is the
                // no-channel state whose behavior must be bit-identical to a gaze module that
                // never met a channel. A post-completion refractory from a program that was
                // playing when the producer vanished must NOT keep suppressing the backchannel,
                // so every trace of external state resets here, not just the offset.
                _hasExternalOffset = false;
                _externalActive = false;
                _refractoryRemaining = 0f;
                _aversionFadeWeight = 1f;
                return;
            }

            _hasExternalOffset = channel.TryGetOffset(out _externalOffset) && _externalOffset.Weight > 0f;

            if (!_hasExternalOffset)
            {
                // No program playing right now. Still counts as "externally active" while the
                // post-completion refractory drains, so the backchannel stays held off for a
                // clean beat after a scripted gesture ends.
                _externalActive = _refractoryRemaining > 0f;
                if (_externalActive)
                    _refractoryRemaining -= dt;

                // Nothing to fade toward — snap the aversion fade back to rest so the NEXT
                // external program starts its own fade-in from a clean baseline rather than
                // inheriting a stale partial value from a previous, unrelated gate.
                _aversionFadeWeight = 1f;
                return;
            }

            // A program is active: arm the refractory so it is ready the instant this program's
            // weight drops back to zero (the branch above, on a future tick).
            _refractoryRemaining = PostExternalRefractorySeconds;
            _externalActive = true;

            float fadeGoal = isAverting ? 0f : 1f;
            _aversionFadeWeight = AversionFadeSeconds > 0f
                ? Mathf.MoveTowards(_aversionFadeWeight, fadeGoal, dt / AversionFadeSeconds)
                : fadeGoal;
        }

        /// <summary>
        ///     Composes this tick's final gesture output from the backchannel offset the
        ///     controller already ticked (with <see cref="ExternalActive" /> folded into its
        ///     suppression input) and the external state <see cref="SenseExternal" /> captured
        ///     moments earlier in the same tick. Call after <see cref="SenseExternal" />, once
        ///     per tick, immediately before building the solver input.
        /// </summary>
        public void Compose(Vector2 backchannelOffset)
        {
            if (!_hasExternalOffset)
            {
                _output = backchannelOffset;
                _rollOutput = 0f;
                return;
            }

            float scale = Mathf.Clamp01(_externalOffset.Weight) * _aversionFadeWeight;
            _output = new Vector2(_externalOffset.YawDegrees, _externalOffset.PitchDegrees) * scale;
            _rollOutput = _externalOffset.RollDegrees * scale;
        }
    }
}
