using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Emotion.Core
{
    /// <summary>
    ///     Zero-allocation, deterministic POCO that computes per-channel weights
    ///     (<see cref="MicroExpressionChannel" />) for the micro-expression "life"
    ///     layer: low-amplitude idle drift so the face is never frozen, plus a short
    ///     speech-coupled brow-raise accent on rising speech energy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Idle drift.</b> Each channel gets its own deterministic noise stream (summed
    ///         sinusoids at incommensurate frequencies, seeded via
    ///         <see cref="DeterministicEmbodimentRandom" /> so the same seed always reproduces
    ///         the same sequence) scaled by <c>amplitude * stillness</c> and by the channel's
    ///         emotion-bias weight (see <see cref="SetEmotionBias" />).
    ///     </para>
    ///     <para>
    ///         <b>Speech accent.</b> The director derives its own lightweight onset envelope
    ///         from the raw energy sample supplied by the caller each tick — it does NOT read
    ///         or advance any shared speech-energy provider itself (the caller owns sampling;
    ///         see <see cref="Tick" />'s <c>speechEnergy</c> parameter). A rising edge above a
    ///         threshold starts a fast-attack / slower-decay accent envelope that biases
    ///         <see cref="MicroExpressionChannel.BrowOuterUp" /> (the conversational emphasis
    ///         raise), scaled by <c>speechAccentStrength</c>.
    ///     </para>
    ///     <para>
    ///         <b>Listening reactions.</b> While the caller reports the player is speaking (see
    ///         <see cref="SetListeningState" />), the director eases an attentiveness envelope in
    ///         (attack) and out (decay) — never snapping — and blends a sustained attentive lift
    ///         plus sparse, deterministic accent bursts into <see cref="MicroExpressionChannel.BrowOuterUp" />
    ///         and <see cref="MicroExpressionChannel.Squint" />. Bursts reuse the SAME per-channel
    ///         idle-drift signal already computed for idle life (no additional RNG state), just
    ///         reshaped so it reads as sparse "attentive" pulses instead of a smooth wobble. With
    ///         listening never set (or strength 0 and fully eased out), the envelope stays at 0 and
    ///         this contributes exactly nothing.
    ///     </para>
    ///     <para>
    ///         <b>Thinking.</b> While the caller reports the <c>Thinking</c> dialogue beat
    ///         (see <see cref="SetThinkingState" />), the director eases a sustained concentration
    ///         envelope in/out (same easing idiom as listening, just its own time constants) that
    ///         biases <see cref="MicroExpressionChannel.BrowDown" /> and, more faintly,
    ///         <see cref="MicroExpressionChannel.Squint" />. With thinking never set (or strength
    ///         0), the envelope stays at 0 and contributes nothing.
    ///     </para>
    ///     <para>
    ///         <b>Beat accents.</b> <see cref="TriggerBeatAccent" /> fires a single one-shot
    ///         envelope (fast attack, slower decay) for the <c>Reacting</c> or <c>Interrupted</c>
    ///         dialogue beats, biasing <see cref="MicroExpressionChannel.BrowOuterUp" />/
    ///         <see cref="MicroExpressionChannel.BrowInnerUp" /> (Reacting) or
    ///         <see cref="MicroExpressionChannel.Squint" />/<see cref="MicroExpressionChannel.BrowDown" />
    ///         (Interrupted). Both beats share ONE envelope + a <see cref="BeatAccent" /> kind field
    ///         rather than independent envelopes per kind — a deliberate, documented
    ///         simplification: a new trigger simply replaces whatever is in flight (re-attacking
    ///         from the current envelope value toward the new peak), since the two beats are
    ///         mutually exclusive dialogue states and never need to layer.
    ///     </para>
    ///     <para>
    ///         <b>Brow cues.</b> <see cref="TriggerBrowCue" /> forwards to an internal
    ///         <see cref="BrowCueEnvelope" /> (its own attack/decay shaping per
    ///         <see cref="BrowCueKind" />) whose <c>OuterWeight</c>/<c>InnerWeight</c> compose
    ///         into <see cref="MicroExpressionChannel.BrowOuterUp" />/
    ///         <see cref="MicroExpressionChannel.BrowInnerUp" /> alongside every other
    ///         contribution above. This is the Gaze module's eyebrow-gaze coordination seam
    ///         (<c>IBrowCueSink</c>) — never called directly by anything in this module, only via
    ///         the owning component forwarding a registered sink call.
    ///     </para>
    /// </remarks>
    internal sealed class MicroExpressionDirector
    {
        /// <summary>Dialogue-beat one-shot accents driven by <see cref="TriggerBeatAccent" />.</summary>
        internal enum BeatAccent
        {
            /// <summary>Short reaction beat after a significant event — a brief brow flash.</summary>
            Reacting,

            /// <summary>Character was interrupted mid-turn — a brief flinch.</summary>
            Interrupted
        }

        private const int ChannelCount = (int)MicroExpressionChannel.Count;

        // Distinct salts per channel so each idle-drift stream is independent even though all
        // channels share the same owning component.
        private const uint IdleSaltBase = 0x4D494352u; // "MICR"

        // Two incommensurate-frequency sinusoids per channel (Hz), offset by a per-channel
        // deterministic phase, sum to a smooth, non-repeating-looking low-amplitude drift.
        private const float IdleFrequencyA = 0.17f;
        private const float IdleFrequencyB = 0.041f;

        private const float SpeechOnsetThreshold = 0.08f;
        private const float SpeechAccentAttackSeconds = 0.09f;
        private const float SpeechAccentDecaySeconds = 0.5f;

        // Listening reactions: sustained attentive lift + sparse accent bursts, both scaled
        // by the eased-in/out envelope below. Attack/decay time constants are deliberately slower
        // than the speech accent's — attentiveness should read as a held state, not a flash.
        private const float ListeningAttackSeconds = 0.6f;
        private const float ListeningDecaySeconds = 1.2f;
        private const float ListeningSustainedLiftFraction = 0.35f;
        private const float ListeningBurstPeakFraction = 0.45f;
        private const float ListeningBurstExponent = 6f;
        private const float ListeningSquintScale = 0.6f;
        private const float ListeningEnvelopeFloor = 0.0005f;

        // Thinking: sustained concentration envelope, same easing idiom as listening, with
        // its own slightly snappier attack/decay so the "cognitive pause" reads distinctly.
        private const float ThinkingAttackSeconds = 0.5f;
        private const float ThinkingDecaySeconds = 0.8f;
        private const float ThinkingSquintScale = 0.5f;
        private const float ThinkingEnvelopeFloor = 0.0005f;

        // Beat accents: one-shot fast-attack/slower-decay envelope shared by the Reacting
        // and Interrupted dialogue beats (see the BeatAccent kind field). Attack completion is
        // detected by proximity to the peak rather than a fixed duration, matching the
        // exponential-ease idiom used everywhere else in this director.
        private const float ReactingAttackSeconds = 0.12f;
        private const float ReactingDecaySeconds = 0.6f;
        private const float InterruptedAttackSeconds = 0.09f;
        private const float InterruptedDecaySeconds = 0.5f;
        private const float BeatInnerScale = 0.6f;
        private const float BeatSecondaryScale = 0.7f;
        private const float BeatEnvelopeFloor = 0.0005f;

        private readonly float[] _idlePhaseA = new float[ChannelCount];
        private readonly float[] _idlePhaseB = new float[ChannelCount];
        private readonly float[] _idleWeight = new float[ChannelCount];
        private readonly float[] _emotionBias = new float[ChannelCount];
        private readonly float[] _channelWeights = new float[ChannelCount];

        private float _clock;
        private float _previousSpeechEnergy;
        private float _speechAccentEnvelope;
        private bool _listeningTargetActive;
        private float _listeningTargetStrength;
        private float _listeningEnvelope;

        private bool _thinkingTargetActive;
        private float _thinkingTargetStrength;
        private float _thinkingEnvelope;

        private BeatAccent _beatKind;
        private float _beatPeak;
        private float _beatEnvelope;
        private bool _beatAttacking;

        // The cue envelope owns only its own attack/decay shape; this director
        // composes its OuterWeight/InnerWeight into BrowOuterUp/BrowInnerUp below.
        private readonly BrowCueEnvelope _browCue = new();

        private bool _seeded;

        /// <summary>Per-channel composed weight (0..1) from the most recent <see cref="Tick" />.</summary>
        public float GetChannelWeight(MicroExpressionChannel channel) => _channelWeights[(int)channel];

        /// <summary>
        ///     Per-channel bias weight (0..1) set by the most recent <see cref="SetEmotionBias" />
        ///     call, independent of the per-channel idle-drift noise <see cref="Tick" /> composes it
        ///     with. Exposed for deterministic assertions on the bias table itself (the seeded idle
        ///     drift is intentionally random per channel, so asserting on <see cref="GetChannelWeight" />
        ///     right after a single tick conflates "did SetEmotionBias favor this channel" with "did
        ///     this channel's independent random phase happen to be large this run").
        /// </summary>
        internal float GetEmotionBias(MicroExpressionChannel channel) => _emotionBias[(int)channel];

        /// <summary>
        ///     Seeds the deterministic idle-drift streams. Call once after construction (or after
        ///     re-enable) before the first <see cref="Tick" />. Re-seeding resets accumulated
        ///     drift phase but not the speech-accent envelope (that is driven purely by the
        ///     live energy signal, not the seed).
        /// </summary>
        public void Seed(Component owner)
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                DeterministicEmbodimentRandom random =
                    DeterministicEmbodimentRandom.Create(owner, IdleSaltBase + (uint)i);
                _idlePhaseA[i] = random.Range(0f, Mathf.PI * 2f);
                _idlePhaseB[i] = random.Range(0f, Mathf.PI * 2f);
            }
            _seeded = true;
        }

        /// <summary>
        ///     Sets the per-channel idle/accent bias for the current dominant emotion. Small,
        ///     data-driven table: joy favors BrowOuterUp + CheekRaise; sadness favors
        ///     BrowInnerUp; anger favors BrowDown; neutral/unknown gets a mild BrowOuterUp-only
        ///     idle bias. Safe to call every tick (cheap, no allocation).
        /// </summary>
        public void SetEmotionBias(string dominantLabel, float dominantScore)
        {
            for (int i = 0; i < ChannelCount; i++)
                _emotionBias[i] = 0.25f; // baseline: every channel still drifts a little.

            float bias = Mathf.Clamp01(dominantScore);

            if (IsLabel(dominantLabel, "joy") || IsLabel(dominantLabel, "trust") || IsLabel(dominantLabel, "anticipation"))
            {
                _emotionBias[(int)MicroExpressionChannel.BrowOuterUp] = 0.25f + 0.75f * bias;
                _emotionBias[(int)MicroExpressionChannel.CheekRaise] = 0.25f + 0.75f * bias;
            }
            else if (IsLabel(dominantLabel, "sadness"))
            {
                _emotionBias[(int)MicroExpressionChannel.BrowInnerUp] = 0.25f + 0.75f * bias;
            }
            else if (IsLabel(dominantLabel, "anger") || IsLabel(dominantLabel, "disgust"))
            {
                _emotionBias[(int)MicroExpressionChannel.BrowDown] = 0.25f + 0.75f * bias;
            }
            else if (IsLabel(dominantLabel, "fear") || IsLabel(dominantLabel, "surprise"))
            {
                _emotionBias[(int)MicroExpressionChannel.BrowOuterUp] = 0.25f + 0.5f * bias;
                _emotionBias[(int)MicroExpressionChannel.BrowInnerUp] = 0.25f + 0.5f * bias;
            }
            else
            {
                // Neutral/unresolved: mild BrowOuterUp-only idle life, everything else at baseline.
                _emotionBias[(int)MicroExpressionChannel.BrowOuterUp] = 0.4f;
            }
        }

        /// <summary>
        ///     Sets the target listening state consumed by the NEXT <see cref="Tick" />. The
        ///     director does not react instantly — it eases its internal attentiveness envelope
        ///     toward <paramref name="strength" /> (when <paramref name="isListening" />) or 0
        ///     (otherwise), so toggling this every frame from the caller's dialogue-state read is
        ///     cheap and safe (no allocation, no immediate snap). Safe to call before <see cref="Seed" />;
        ///     the stored target is simply picked up once ticking starts.
        /// </summary>
        /// <param name="isListening">Whether the character is currently in the Listening dialogue state.</param>
        /// <param name="strength">
        ///     Authored listening-reaction strength, 0..1. Clamped internally; 0 means the
        ///     envelope eases back to 0 exactly as if not listening.
        /// </param>
        public void SetListeningState(bool isListening, float strength)
        {
            _listeningTargetActive = isListening;
            _listeningTargetStrength = Mathf.Clamp01(strength);
        }

        /// <summary>
        ///     Sets the target thinking state consumed by the NEXT <see cref="Tick" />, mirroring
        ///     <see cref="SetListeningState" />'s easing contract exactly (never snaps, cheap and
        ///     safe to call every frame, safe before <see cref="Seed" />).
        /// </summary>
        /// <param name="isThinking">Whether the character is currently in the Thinking dialogue state.</param>
        /// <param name="strength">
        ///     Authored thinking-reaction strength, 0..1. Clamped internally; 0 means the envelope
        ///     eases back to 0 exactly as if not thinking.
        /// </param>
        public void SetThinkingState(bool isThinking, float strength)
        {
            _thinkingTargetActive = isThinking;
            _thinkingTargetStrength = Mathf.Clamp01(strength);
        }

        /// <summary>
        ///     Fires a one-shot dialogue-beat accent (see <see cref="BeatAccent" />) for
        ///     <c>Reacting</c>/<c>Interrupted</c> beat transitions: the shared beat envelope
        ///     attacks quickly toward <c>clamp01(strength)</c> then auto-decays back to 0 over the
        ///     kind's authored time constants (see the <c>Reacting</c>/<c>Interrupted</c> attack/decay
        ///     constants). A new trigger replaces any in-flight beat (re-attacking from the
        ///     envelope's current value toward the new peak) rather than queuing or stacking —
        ///     an accepted simplification since Reacting/Interrupted are mutually exclusive
        ///     dialogue states. <paramref name="strength" /> &lt;= 0 is a no-op (no state change).
        /// </summary>
        /// <param name="kind">Which beat accent's channel mix and time constants to use.</param>
        /// <param name="strength">Peak envelope value, 0..1. Non-positive values do nothing.</param>
        public void TriggerBeatAccent(BeatAccent kind, float strength)
        {
            float peak = Mathf.Clamp01(strength);
            if (peak <= 0f) return;

            _beatKind = kind;
            _beatPeak = peak;
            _beatAttacking = true;
        }

        /// <summary>
        ///     Fires (or re-attacks) a brow cue raised by Gaze — forwards to the
        ///     internal <see cref="BrowCueEnvelope" /> (see its own docs for per-kind
        ///     attack/decay shaping and peak amplitude). Never called directly by anything in
        ///     this module; the owning component forwards its registered <c>IBrowCueSink</c>
        ///     calls here. <paramref name="intensity01" /> &lt;= 0 is a no-op.
        /// </summary>
        public void TriggerBrowCue(BrowCueKind kind, float intensity01) => _browCue.Trigger(kind, intensity01);

        /// <summary>
        ///     Advances idle drift and the internal speech-accent envelope by
        ///     <paramref name="deltaTime" /> seconds and recomputes all channel weights.
        /// </summary>
        /// <param name="deltaTime">Elapsed seconds. Non-positive values are ignored (no advance).</param>
        /// <param name="amplitude">Idle-drift amplitude authored on the profile, 0..1.</param>
        /// <param name="stillness">Global idle-drift damp authored on the profile, 0..1.</param>
        /// <param name="speechAccentStrength">Speech-accent amplitude authored on the profile, 0..1.</param>
        /// <param name="speechEnergy">
        ///     Current raw speech-energy sample (already sampled by the caller — this method
        ///     never advances or samples any external provider). Pass 0 when no provider is
        ///     available; the accent envelope then simply never triggers and idle drift alone
        ///     runs.
        /// </param>
        public void Tick(
            float deltaTime,
            float amplitude,
            float stillness,
            float speechAccentStrength,
            float speechEnergy)
        {
            if (!(deltaTime > 0f) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;

            if (!_seeded)
                return;

            _clock += deltaTime;

            if (float.IsNaN(speechEnergy) || float.IsInfinity(speechEnergy) || speechEnergy < 0f)
                speechEnergy = 0f;

            UpdateSpeechAccentEnvelope(speechEnergy, deltaTime);
            UpdateListeningEnvelope(deltaTime);
            UpdateThinkingEnvelope(deltaTime);
            UpdateBeatEnvelope(deltaTime);
            _browCue.Tick(deltaTime);

            float idleScale = Mathf.Clamp01(amplitude) * Mathf.Clamp01(stillness);
            float accentScale = Mathf.Clamp01(speechAccentStrength) * _speechAccentEnvelope;

            for (int i = 0; i < ChannelCount; i++)
            {
                float driftA = Mathf.Sin(_clock * (IdleFrequencyA * Mathf.PI * 2f) + _idlePhaseA[i]);
                float driftB = Mathf.Sin(_clock * (IdleFrequencyB * Mathf.PI * 2f) + _idlePhaseB[i]);
                // Average two unit sinusoids -> stays within [-1, 1]; remap to [0, 1] so it reads
                // as a small "life" pulse rather than a negative dip below rest.
                float drift01 = ((driftA + driftB) * 0.5f + 1f) * 0.5f;

                float idle = drift01 * idleScale * _emotionBias[i];

                // Speech accent only ever biases the conversational emphasis-raise channel
                // (BrowOuterUp) plus a faint co-activation on CheekRaise, matching the "brow
                // flashes on emphasis" conversational read called for in the plan.
                float accent = 0f;
                if (i == (int)MicroExpressionChannel.BrowOuterUp)
                    accent = accentScale;
                else if (i == (int)MicroExpressionChannel.CheekRaise)
                    accent = accentScale * 0.35f;

                // Listening reaction: a sustained attentive lift plus sparse accent bursts
                // reshaped from the SAME per-channel idle-drift sample above (no extra RNG state),
                // both scaled by the eased _listeningEnvelope so 0/never-listened is exactly 0.
                float listening = 0f;
                if (_listeningEnvelope > 0f)
                {
                    if (i == (int)MicroExpressionChannel.BrowOuterUp)
                        listening = _listeningEnvelope * ListeningContribution(drift01);
                    else if (i == (int)MicroExpressionChannel.Squint)
                        listening = _listeningEnvelope * ListeningSquintScale * ListeningContribution(drift01);
                }

                // Thinking: sustained concentration — BrowDown scaled by the eased envelope,
                // plus a fainter Squint co-activation. 0 (never Thinking, or 0 envelope) contributes
                // exactly nothing.
                float thinking = 0f;
                if (_thinkingEnvelope > 0f)
                {
                    if (i == (int)MicroExpressionChannel.BrowDown)
                        thinking = _thinkingEnvelope;
                    else if (i == (int)MicroExpressionChannel.Squint)
                        thinking = _thinkingEnvelope * ThinkingSquintScale;
                }

                // Beat accent: one-shot Reacting/Interrupted channel mix, scaled by the
                // shared beat envelope. 0 (never triggered, or fully decayed) contributes nothing.
                float beat = 0f;
                if (_beatEnvelope > 0f)
                {
                    if (_beatKind == BeatAccent.Reacting)
                    {
                        if (i == (int)MicroExpressionChannel.BrowOuterUp)
                            beat = _beatEnvelope;
                        else if (i == (int)MicroExpressionChannel.BrowInnerUp)
                            beat = _beatEnvelope * BeatInnerScale;
                    }
                    else // Interrupted
                    {
                        if (i == (int)MicroExpressionChannel.Squint)
                            beat = _beatEnvelope;
                        else if (i == (int)MicroExpressionChannel.BrowDown)
                            beat = _beatEnvelope * BeatSecondaryScale;
                    }
                }

                // Brow cue: one-shot upward-saccade/nod/interruption accent, scaled by the
                // envelope's own OuterWeight/InnerWeight. 0 (never triggered, or fully decayed)
                // contributes nothing.
                float browCue = 0f;
                if (_browCue.OuterWeight > 0f)
                {
                    if (i == (int)MicroExpressionChannel.BrowOuterUp)
                        browCue = _browCue.OuterWeight;
                    else if (i == (int)MicroExpressionChannel.BrowInnerUp)
                        browCue = _browCue.InnerWeight;
                }

                _channelWeights[i] = Mathf.Clamp01(idle + accent + listening + thinking + beat + browCue);
            }

            _previousSpeechEnergy = speechEnergy;
        }

        /// <summary>Resets all runtime state (drift phases stay seeded; call <see cref="Seed" /> again after this if a fresh phase is desired).</summary>
        public void Reset()
        {
            _clock = 0f;
            _previousSpeechEnergy = 0f;
            _speechAccentEnvelope = 0f;
            _listeningEnvelope = 0f;
            _thinkingEnvelope = 0f;
            _beatEnvelope = 0f;
            _beatAttacking = false;
            _beatPeak = 0f;
            _browCue.Reset();
            for (int i = 0; i < ChannelCount; i++)
                _channelWeights[i] = 0f;
        }

        private void UpdateSpeechAccentEnvelope(float speechEnergy, float deltaTime)
        {
            bool risingEdge = speechEnergy - _previousSpeechEnergy > SpeechOnsetThreshold * 0.5f &&
                               speechEnergy > SpeechOnsetThreshold;

            if (risingEdge && _speechAccentEnvelope < 1f)
            {
                float attackCoefficient = ExponentialCoefficient(SpeechAccentAttackSeconds, deltaTime);
                _speechAccentEnvelope += (1f - _speechAccentEnvelope) * attackCoefficient;
            }
            else
            {
                float decayCoefficient = ExponentialCoefficient(SpeechAccentDecaySeconds, deltaTime);
                _speechAccentEnvelope -= _speechAccentEnvelope * decayCoefficient;
            }

            if (_speechAccentEnvelope < 0.0005f) _speechAccentEnvelope = 0f;
        }

        /// <summary>
        ///     Eases <see cref="_listeningEnvelope" /> toward the current target
        ///     (<see cref="_listeningTargetStrength" /> while <see cref="_listeningTargetActive" />,
        ///     otherwise 0) using the same exponential attack/decay shape as the speech-accent
        ///     envelope, just with slower time constants so attentiveness reads as held rather than
        ///     flashed. Never snaps; reaches exactly 0 at rest.
        /// </summary>
        private void UpdateListeningEnvelope(float deltaTime)
        {
            float target = _listeningTargetActive ? _listeningTargetStrength : 0f;

            if (target > _listeningEnvelope)
            {
                float attackCoefficient = ExponentialCoefficient(ListeningAttackSeconds, deltaTime);
                _listeningEnvelope += (target - _listeningEnvelope) * attackCoefficient;
            }
            else
            {
                float decayCoefficient = ExponentialCoefficient(ListeningDecaySeconds, deltaTime);
                _listeningEnvelope += (target - _listeningEnvelope) * decayCoefficient;
            }

            if (_listeningEnvelope < ListeningEnvelopeFloor) _listeningEnvelope = 0f;
        }

        /// <summary>
        ///     Eases <see cref="_thinkingEnvelope" /> toward the current target
        ///     (<see cref="_thinkingTargetStrength" /> while <see cref="_thinkingTargetActive" />,
        ///     otherwise 0), mirroring <see cref="UpdateListeningEnvelope" /> exactly with its own
        ///     time constants. Never snaps; reaches exactly 0 at rest.
        /// </summary>
        private void UpdateThinkingEnvelope(float deltaTime)
        {
            float target = _thinkingTargetActive ? _thinkingTargetStrength : 0f;

            if (target > _thinkingEnvelope)
            {
                float attackCoefficient = ExponentialCoefficient(ThinkingAttackSeconds, deltaTime);
                _thinkingEnvelope += (target - _thinkingEnvelope) * attackCoefficient;
            }
            else
            {
                float decayCoefficient = ExponentialCoefficient(ThinkingDecaySeconds, deltaTime);
                _thinkingEnvelope += (target - _thinkingEnvelope) * decayCoefficient;
            }

            if (_thinkingEnvelope < ThinkingEnvelopeFloor) _thinkingEnvelope = 0f;
        }

        /// <summary>
        ///     Advances the one-shot beat-accent envelope set by <see cref="TriggerBeatAccent" />:
        ///     attacks toward <see cref="_beatPeak" /> (kind-specific attack time constant) until
        ///     within <see cref="BeatEnvelopeFloor" /> of the peak, then auto-decays back to
        ///     exactly 0 (kind-specific decay time constant). Idle (never triggered, or fully
        ///     decayed) is a no-op.
        /// </summary>
        private void UpdateBeatEnvelope(float deltaTime)
        {
            if (_beatAttacking)
            {
                float attackSeconds = _beatKind == BeatAccent.Reacting ? ReactingAttackSeconds : InterruptedAttackSeconds;
                float attackCoefficient = ExponentialCoefficient(attackSeconds, deltaTime);
                _beatEnvelope += (_beatPeak - _beatEnvelope) * attackCoefficient;

                if (_beatPeak - _beatEnvelope < BeatEnvelopeFloor)
                {
                    _beatEnvelope = _beatPeak;
                    _beatAttacking = false;
                }
            }
            else if (_beatEnvelope > 0f)
            {
                float decaySeconds = _beatKind == BeatAccent.Reacting ? ReactingDecaySeconds : InterruptedDecaySeconds;
                float decayCoefficient = ExponentialCoefficient(decaySeconds, deltaTime);
                _beatEnvelope -= _beatEnvelope * decayCoefficient;

                if (_beatEnvelope < BeatEnvelopeFloor) _beatEnvelope = 0f;
            }
        }

        /// <summary>
        ///     Combines the sustained attentive lift with a sparse accent-burst reshaping of the
        ///     SAME per-channel idle-drift sample (<paramref name="drift01" />, already in
        ///     <c>[0, 1]</c>) — raising it to a high power turns the smooth idle wobble into
        ///     infrequent, narrow peaks without any additional RNG state or per-tick allocation.
        ///     Result is NOT yet scaled by the listening envelope or per-channel scale; callers
        ///     apply those.
        /// </summary>
        private static float ListeningContribution(float drift01) =>
            ListeningSustainedLiftFraction + ListeningBurstPeakFraction * Mathf.Pow(drift01, ListeningBurstExponent);

        private static float ExponentialCoefficient(float timeConstantSeconds, float deltaTime)
        {
            if (timeConstantSeconds <= 0f) return 1f;
            float exponent = -deltaTime / timeConstantSeconds;
            float value = 1f - Mathf.Exp(exponent);
            return Mathf.Clamp01(value);
        }

        private static bool IsLabel(string label, string candidate) =>
            !string.IsNullOrEmpty(label) && string.Equals(label, candidate, System.StringComparison.OrdinalIgnoreCase);
    }
}
