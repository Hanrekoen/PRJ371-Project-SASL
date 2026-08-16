using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that turns emotion spikes (and scripted requests) into one-shot
    ///     bodily reactions: a quick startle flinch on a sudden surprise spike, or
    ///     a light amused bounce on a sudden joy spike. Exactly one reaction plays at a time; a
    ///     new trigger replaces whatever is currently playing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Autonomous spike detection.</b> Each watched label (<c>surprise</c> for
    ///         <see cref="ReactionKind.SurpriseFlinch" />, <c>joy</c> for
    ///         <see cref="ReactionKind.AmusementBounce" />) maintains a slow EMA of its own score;
    ///         when the current score jumps meaningfully above that EMA (a "spike", not just a
    ///         sustained high value) and the kind's own refractory has elapsed, the reaction
    ///         fires automatically. A sustained high score never retriggers — only the ONSET
    ///         does, since the EMA catches up and the spike collapses back toward zero.
    ///     </para>
    ///     <para>
    ///         <b>Envelopes.</b> Flinch is a short attack/hold/decay envelope (the same
    ///         eased-quad recipe <see cref="BreathingDirector" />'s one-shot breath events use —
    ///         duplicated here as tiny private consts/helpers rather than shared, so the two
    ///         directors stay decoupled). Bounce is a windowed 2 Hz oscillation: a Hann window
    ///         (zero and zero-derivative at both ends, so it never pops in or out) modulating a
    ///         sine, giving a couple of visible up/down cycles that settle back to rest on their
    ///         own.
    ///     </para>
    ///     <para>
    ///         <b>Scripted vs. autonomous.</b> <see cref="TryTrigger" /> is the single
    ///         entry point both paths use. Autonomous spikes always respect the per-kind
    ///         refractory; a scripted request may pass <c>bypassRefractory: true</c> to always
    ///         fire (scripted intent wins), which still re-arms the refractory so a burst of
    ///         scripted calls cannot machine-gun the envelope. <see cref="ReactionKind.CatchBreath" />
    ///         and <see cref="ReactionKind.Sigh" /> are breath events, not reaction envelopes —
    ///         <see cref="TryTrigger" /> always refuses them; the controller's
    ///         <c>TriggerReaction</c> routes those two kinds to <see cref="BreathingDirector" />
    ///         instead.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: purely a function of elapsed time and the fed
    ///         <see cref="EmotionReading" /> scores, no randomness — so no seed is needed.
    ///     </para>
    /// </remarks>
    internal sealed class ReactionDirector
    {
        // ── Flinch envelope (attack/hold/decay) ────────────────────────────────
        private const float FlinchAttackSeconds = 0.08f;
        private const float FlinchHoldSeconds = 0.05f;
        private const float FlinchDecaySeconds = 0.6f;

        // ── Bounce envelope (windowed oscillation) ─────────────────────────────
        private const float BounceTotalSeconds = 1.2f;
        private const float BounceFrequencyHz = 2f;

        // ── Autonomous spike detection ──────────────────────────────────────────
        private const string SurpriseLabel = "surprise";
        private const string JoyLabel = "joy";
        private const float SurpriseSpikeThreshold = 0.3f;
        private const float JoySpikeThreshold = 0.35f;
        private const float SpikeEmaTauSeconds = 1.5f;
        private const float SpikeIntensityGain = 1.5f;
        private const float AutonomousRefractorySeconds = 8f;

        private ReactionKind _activeReaction;
        private float _activeIntensity = 1f;
        private float _activeElapsed;

        private float _flinchRefractoryRemaining;
        private float _bounceRefractoryRemaining;

        private float _surpriseEma;
        private float _joyEma;
        private bool _emaInitialized;

        /// <summary>Current flinch envelope value, 0..1 (0 unless <see cref="ActiveReaction" /> is <see cref="ReactionKind.SurpriseFlinch" />).</summary>
        public float FlinchValue { get; private set; }

        /// <summary>Current bounce envelope value, signed roughly ±1 (0 unless <see cref="ActiveReaction" /> is <see cref="ReactionKind.AmusementBounce" />).</summary>
        public float BounceValue { get; private set; }

        /// <summary>The reaction currently playing (<see cref="ReactionKind.None" /> when idle).</summary>
        public ReactionKind ActiveReaction => _activeReaction;

        /// <summary>Restores the director to its initial, inactive state.</summary>
        public void Reset()
        {
            _activeReaction = ReactionKind.None;
            _activeIntensity = 1f;
            _activeElapsed = 0f;
            _flinchRefractoryRemaining = 0f;
            _bounceRefractoryRemaining = 0f;
            _surpriseEma = 0f;
            _joyEma = 0f;
            _emaInitialized = false;
            FlinchValue = 0f;
            BounceValue = 0f;
        }

        /// <summary>
        ///     Attempts to trigger <paramref name="kind" /> at <paramref name="intensity" />
        ///     (0..1). <see cref="ReactionKind.None" />, <see cref="ReactionKind.CatchBreath" />,
        ///     and <see cref="ReactionKind.Sigh" /> are always refused — the breath kinds are not
        ///     reaction envelopes (see class remarks). When <paramref name="bypassRefractory" />
        ///     is <c>false</c> (the autonomous spike path) the kind's own refractory must have
        ///     elapsed; when <c>true</c> (a scripted request) the trigger always fires and still
        ///     re-arms the refractory. A new trigger always replaces whatever reaction is
        ///     currently playing (retrigger allowed) — only one reaction plays at a time.
        /// </summary>
        /// <returns><c>true</c> if the reaction was armed this call.</returns>
        public bool TryTrigger(ReactionKind kind, float intensity, bool bypassRefractory)
        {
            if (kind != ReactionKind.SurpriseFlinch && kind != ReactionKind.AmusementBounce)
                return false;

            float refractoryRemaining = kind == ReactionKind.SurpriseFlinch
                ? _flinchRefractoryRemaining
                : _bounceRefractoryRemaining;
            if (!bypassRefractory && refractoryRemaining > 0f) return false;

            _activeReaction = kind;
            _activeIntensity = Mathf.Clamp01(intensity);
            _activeElapsed = 0f;

            if (kind == ReactionKind.SurpriseFlinch)
                _flinchRefractoryRemaining = AutonomousRefractorySeconds;
            else
                _bounceRefractoryRemaining = AutonomousRefractorySeconds;

            return true;
        }

        /// <summary>
        ///     Advances refractories and the active envelope, and checks this tick's
        ///     <paramref name="emotion" /> scores for an autonomous spike on the watched labels
        ///     (<c>surprise</c>/<c>joy</c>).
        /// </summary>
        public void Tick(in EmotionReading emotion, float deltaTime)
        {
            TickScores(emotion.GetScore(SurpriseLabel), emotion.GetScore(JoyLabel), deltaTime);
        }

        /// <summary>Allocation-free borrowed-frame overload used by the runtime controller.</summary>
        public void Tick(in EmotionStateFrame emotion, float deltaTime)
        {
            TickScores(emotion.GetScore(SurpriseLabel), emotion.GetScore(JoyLabel), deltaTime);
        }

        private void TickScores(float surpriseScore, float joyScore, float deltaTime)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (_flinchRefractoryRemaining > 0f)
                _flinchRefractoryRemaining = Mathf.Max(0f, _flinchRefractoryRemaining - dt);
            if (_bounceRefractoryRemaining > 0f)
                _bounceRefractoryRemaining = Mathf.Max(0f, _bounceRefractoryRemaining - dt);

            if (!_emaInitialized)
            {
                _surpriseEma = surpriseScore;
                _joyEma = joyScore;
                _emaInitialized = true;
            }
            else if (dt > 0f)
            {
                float alpha = 1f - Mathf.Exp(-dt / SpikeEmaTauSeconds);
                _surpriseEma += (surpriseScore - _surpriseEma) * alpha;
                _joyEma += (joyScore - _joyEma) * alpha;
            }

            float surpriseSpike = surpriseScore - _surpriseEma;
            if (surpriseSpike > SurpriseSpikeThreshold && _flinchRefractoryRemaining <= 0f)
                TryTrigger(ReactionKind.SurpriseFlinch, Mathf.Clamp01(surpriseSpike * SpikeIntensityGain), bypassRefractory: false);

            float joySpike = joyScore - _joyEma;
            if (joySpike > JoySpikeThreshold && _bounceRefractoryRemaining <= 0f)
                TryTrigger(ReactionKind.AmusementBounce, Mathf.Clamp01(joySpike * SpikeIntensityGain), bypassRefractory: false);

            AdvanceActiveEnvelope(dt);
        }

        private void AdvanceActiveEnvelope(float dt)
        {
            if (_activeReaction == ReactionKind.None)
            {
                FlinchValue = 0f;
                BounceValue = 0f;
                return;
            }

            _activeElapsed += dt;

            if (_activeReaction == ReactionKind.SurpriseFlinch)
            {
                float total = FlinchAttackSeconds + FlinchHoldSeconds + FlinchDecaySeconds;
                if (_activeElapsed >= total)
                {
                    _activeReaction = ReactionKind.None;
                    FlinchValue = 0f;
                    BounceValue = 0f;
                    return;
                }

                FlinchValue = FlinchEnvelope(_activeElapsed) * _activeIntensity;
                BounceValue = 0f;
                return;
            }

            // AmusementBounce.
            if (_activeElapsed >= BounceTotalSeconds)
            {
                _activeReaction = ReactionKind.None;
                FlinchValue = 0f;
                BounceValue = 0f;
                return;
            }

            float t01 = _activeElapsed / BounceTotalSeconds;
            float hann = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * t01));
            BounceValue = hann * Mathf.Sin(2f * Mathf.PI * BounceFrequencyHz * _activeElapsed) * _activeIntensity;
            FlinchValue = 0f;
        }

        /// <summary>
        ///     C1-eased attack → hold → decay envelope, 0..1 — same recipe as
        ///     <see cref="BreathingDirector" />'s breath-event envelope, duplicated locally (see
        ///     class remarks on decoupling).
        /// </summary>
        private static float FlinchEnvelope(float elapsed)
        {
            if (elapsed <= 0f) return 0f;

            if (elapsed < FlinchAttackSeconds)
                return EaseInOutQuad(Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, FlinchAttackSeconds)));

            float afterAttack = elapsed - FlinchAttackSeconds;
            if (afterAttack < FlinchHoldSeconds) return 1f;

            float decayElapsed = afterAttack - FlinchHoldSeconds;
            if (decayElapsed >= FlinchDecaySeconds) return 0f;

            return 1f - EaseInOutQuad(Mathf.Clamp01(decayElapsed / Mathf.Max(0.0001f, FlinchDecaySeconds)));
        }

        private static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
    }
}
