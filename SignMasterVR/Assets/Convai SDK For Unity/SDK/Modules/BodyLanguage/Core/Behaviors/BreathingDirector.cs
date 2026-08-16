using Convai.Modules.BodyLanguage.Core.Policy;
using Convai.Modules.BodyLanguage.Core.Signals;
using Convai.Modules.BodyLanguage.Data;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     A one-shot breath event layered on top of the continuous breathing rhythm.
    ///     Each kind is a transient rate/depth modulation — never a phase reset — so it
    ///     composes with the free-running oscillator without ever popping the breath.
    /// </summary>
    internal enum BreathEventKind
    {
        /// <summary>No event active.</summary>
        None = 0,

        /// <summary>A quick, sharp intake — the character catches its breath when interrupted (or, at reduced intensity, on a sharp reaction).</summary>
        CatchBreath = 1,

        /// <summary>A long, deep, slow breath as the conversation settles — motion only, no audio.</summary>
        Sigh = 2,

        /// <summary>A brief deeper/faster inhale as the character draws breath to start speaking.</summary>
        InhaleBeforeSpeaking = 3,

        /// <summary>
        ///     A gentle inhale taken in a phrase gap during continuous speech —
        ///     softer than <see cref="InhaleBeforeSpeaking" /> since it repeats many times per
        ///     utterance rather than firing once at speech onset.
        /// </summary>
        SpeechGapInhale = 4
    }

    /// <summary>
    ///     Cognition-tick POCO that turns the smoothed per-state policy plus emotion modulation
    ///     into the breath solver's continuous targets: rate (cpm), depth, and irregularity.
    ///     Targets slew over seconds so a state or emotion change never pops the
    ///     breath — <see cref="Core.Pose.BreathSolver" />'s phase never resets regardless.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Thinking's "irregular, held" column is implemented here as a higher
    ///         irregularity target than any other state authors by default — the solver's own hold
    ///         logic (triggered above its irregularity threshold) supplies the occasional plateau,
    ///         so this director only needs to slew the irregularity value itself.
    ///     </para>
    ///     <para>
    ///         <b>Breath events.</b> On top of the slewed rate/depth targets,
    ///         a single one-shot <see cref="BreathEventKind" /> envelope can be armed via
    ///         <see cref="TriggerEvent" /> — a catch-breath on interruption, a sigh on settling,
    ///         an inhale as speech starts. The envelope is a C1-eased attack/hold/decay curve
    ///         that scales rate and depth toward the event's peak multipliers and back to 1; it
    ///         only touches the exposed <see cref="RateCpm" />/<see cref="Depth" /> multipliers,
    ///         never the oscillator phase, so an event never introduces a discontinuity. When no
    ///         event is active the multipliers are exactly 1, so breathing is bit-identical to
    ///         the pre-event behavior.
    ///     </para>
    /// </remarks>
    internal sealed class BreathingDirector
    {
        /// <summary>Per-kind envelope shape plus rate/depth peak multipliers for a breath event.</summary>
        private readonly struct BreathEventShape
        {
            public readonly float AttackSeconds;
            public readonly float HoldSeconds;
            public readonly float DecaySeconds;
            public readonly float RatePeakMultiplier;
            public readonly float DepthPeakMultiplier;

            public BreathEventShape(
                float attackSeconds, float holdSeconds, float decaySeconds,
                float ratePeakMultiplier, float depthPeakMultiplier)
            {
                AttackSeconds = attackSeconds;
                HoldSeconds = holdSeconds;
                DecaySeconds = decaySeconds;
                RatePeakMultiplier = ratePeakMultiplier;
                DepthPeakMultiplier = depthPeakMultiplier;
            }

            public float TotalSeconds => AttackSeconds + HoldSeconds + DecaySeconds;
        }

        // ── Speech-gap inhale anti-pumping ────────────────────────────
        // A continuous Speaking utterance can emit many Release pulses (one per short pause
        // between phrases/sentences); without a refractory + lockout this would retrigger a
        // breath event every gap and read as hyperventilation. Owned here (not the controller)
        // so all breath-event arbitration lives in one place.

        /// <summary>
        ///     Minimum seconds between two speech-gap inhales, in normal (speech-energy-driven)
        ///     mode. Set at phrase scale: a gap-inhale is a phrase-boundary top-up, not a
        ///     per-syllable event, so a shorter cap lets it retrigger on roughly every other brief
        ///     pause, which reads as flutter rather than as natural breathing. This is a safety
        ///     cap on cadence, not a feel value — the felt rhythm comes from the envelope shape.
        /// </summary>
        private const float SpeechGapInhaleRefractorySeconds = 4.0f;

        /// <summary>
        ///     Minimum seconds between two speech-gap inhales when the fast channel is running on
        ///     the no-<c>ISpeechEnergyProvider</c> statistical-cadence fallback — longer than the
        ///     normal refractory because a "gap" there is a much weaker signal (no real envelope
        ///     to trust), so the character should draw fewer, more conservative gap-breaths.
        ///     Raised to 6.0s alongside <see cref="SpeechGapInhaleRefractorySeconds" /> for the
        ///     phrase-scale retune, preserving the same relative safety margin;
        ///     also feel-tunable once real speech energy is flowing.
        /// </summary>
        private const float SpeechGapInhaleRefractorySecondsConservative = 6.0f;

        /// <summary>
        ///     Minimum <see cref="SpeechPulse.Strength" /> (0..1) a <see cref="SpeechPulseKind.Release" />
        ///     must carry to be trusted as a genuine phrase gap, in normal mode. <c>Release</c>'s
        ///     strength is normalized against <see cref="SpeechPulseAnalyzerConfig.OnsetThresholdAboveBaseline" />
        ///     and, since a release is checked the very first tick the envelope crosses below the
        ///     hysteresis boundary (not after a large accumulated drop), realistic conversational
        ///     Release strengths for genuine phrase gaps cluster well under 0.5 — a threshold set
        ///     too high (e.g. 0.35+) would silently starve this feature of any real gaps to react
        ///     to. This value is deliberately low — a light filter against the weakest/most
        ///     marginal boundary dips, not the primary anti-pumping defense (that is the
        ///     refractory + envelope lockout below).
        /// </summary>
        private const float SpeechGapInhaleConfidenceThreshold = 0.1f;

        /// <summary>
        ///     Minimum <see cref="SpeechPulse.Strength" /> required in statistical-cadence
        ///     fallback mode — a higher bar than <see cref="SpeechGapInhaleConfidenceThreshold" />
        ///     so a noisy/derived envelope needs a much more decisive-looking drop before it is
        ///     trusted as a real gap (guards false-positive gaps). In the current
        ///     wiring this branch is effectively unreachable in practice: with no
        ///     <c>ISpeechEnergyProvider</c> registered the controller feeds the analyzer a
        ///     constant zero energy, so it never produces an <c>Onset</c> and therefore never a
        ///     <c>Release</c> either — this constant exists as a defensive, correctly-conservative
        ///     value for if/when a synthetic statistical-cadence energy signal is ever threaded
        ///     into the analyzer too, not as a value tuned against an observed pulse stream today.
        /// </summary>
        private const float SpeechGapInhaleConfidenceThresholdConservative = 0.3f;

        /// <summary>
        ///     Time constant (seconds) of the <see cref="_voicedFraction" /> EMA. Deliberately at
        ///     phrase scale: a shorter constant tracks individual syllable and word energy peaks
        ///     closely enough to visibly ride the chest with them (a 1–2 Hz flutter), whereas this
        ///     one reads phrase-level voicing cadence — the timescale exhale-shallowing should
        ///     actually respond to.
        /// </summary>
        private const float VoicedFractionTauSeconds = 2.0f;

        /// <summary>
        ///     Fraction of <see cref="Depth" /> sustained voiced speech visibly shallows the
        ///     breath by — you exhale while talking — feel-pass tunable.
        /// </summary>
        private const float ExhaleDepthLoss = 0.25f;

        /// <summary>
        ///     Global breath smoothness budget: the maximum rate
        ///     <see cref="Depth" />'s published value is allowed to change, in depth units per
        ///     second. Every internal modulator (state slew, emotion, breath events, the
        ///     speech-coupled exhale) still composes freely into the "combined" target — this cap
        ///     is applied once, at the single publish point, so no combination of fast modulators
        ///     can ever produce a frame-scale pop or flutter in what callers actually read.
        /// </summary>
        private const float MaxDepthChangePerSecond = 0.35f;

        /// <summary>
        ///     Global breath smoothness budget: the maximum rate
        ///     <see cref="RateCpm" />'s published value is allowed to change, in cpm per second.
        ///     Deliberately high enough that a catch-breath's peak swing (e.g. 13→23 cpm) still
        ///     completes in roughly its own envelope time (~0.4s) rather than being visibly
        ///     dragged out, but low enough to kill frame-scale jitter from the fast modulators
        ///     underneath it.
        /// </summary>
        private const float MaxRateChangeCpmPerSecond = 25f;

        private float _sinceLastSpeechGapInhale = SpeechGapInhaleRefractorySeconds;

        private float _rateCpm = 13f;
        private float _depth;
        private float _irregularity;
        private bool _initialized;

        /// <summary>
        ///     Slewed 0..1 fraction of "how continuously voiced speech has been happening
        ///     recently" — an EMA toward the current tick's speech energy while speaking, and
        ///     toward 0 while silent.
        /// </summary>
        private float _voicedFraction;

        private BreathEventKind _activeEvent;
        private BreathEventShape _activeEventShape;
        private float _activeEventIntensity = 1f;
        private float _activeEventElapsed;
        private float _eventRateMultiplier = 1f;
        private float _eventDepthMultiplier = 1f;

        // ── Exertion coupling ─────────────────────────────────────────
        // Caller-supplied multipliers (composed once per tick, defaulting to the identity 1 so a
        // caller that never passes them — or when no IExertionSource is registered upstream —
        // composes bit-identically to the pre-N8 behavior). Folded into the publish bus exactly
        // like the breath-event multipliers above, so they inherit the same slew-limited
        // smoothing rather than needing their own.
        private float _exertionRateMultiplier = 1f;
        private float _exertionDepthMultiplier = 1f;

        // ── Modulation bus ───────────────────────────────────────────────────────
        // Every fast modulator above (state slew, emotion, breath events, the speech-coupled
        // exhale) composes into a "combined" target as before; this single slew-limited stage is
        // the ONLY thing callers ever read (RateCpm/Depth below), so no combination of upstream
        // modulators can ever pop or flutter the published value frame to frame.
        private float _publishedDepth;
        private float _publishedRate;
        private bool _publishedInitialized;

        /// <summary>
        ///     Published breathing rate (cpm) — the sole external read of this director's rate.
        ///     Composes the slewed state target with any active breath-event rate multiplier,
        ///     then passes through the global smoothness bus: it can change by at most
        ///     <see cref="MaxRateChangeCpmPerSecond" /> per second, so upstream
        ///     modulators can move as fast as they like without ever popping this output.
        /// </summary>
        public float RateCpm => _publishedRate;

        /// <summary>
        ///     Published breathing depth, 0..1 — the sole external read of this director's depth.
        ///     Composes the slewed state target with any active breath-event depth multiplier and
        ///     the speech-coupled exhale (sustained voiced speech shallows the
        ///     breath by up to <see cref="ExhaleDepthLoss" />), then passes through the global
        ///     smoothness bus: it can change by at most <see cref="MaxDepthChangePerSecond" /> per
        ///     second, so upstream modulators can move as fast as they like without
        ///     ever popping this output.
        /// </summary>
        public float Depth => _publishedDepth;

        /// <summary>Slewed breathing irregularity target, 0..1.</summary>
        public float Irregularity => _irregularity;

        /// <summary>The breath event currently playing (<see cref="BreathEventKind.None" /> when idle) — diagnostics.</summary>
        public BreathEventKind ActiveEvent => _activeEvent;

        public void Reset()
        {
            _rateCpm = 13f;
            _depth = 0f;
            _irregularity = 0f;
            _initialized = false;
            _activeEvent = BreathEventKind.None;
            _activeEventShape = default;
            _activeEventIntensity = 1f;
            _activeEventElapsed = 0f;
            _eventRateMultiplier = 1f;
            _eventDepthMultiplier = 1f;
            _exertionRateMultiplier = 1f;
            _exertionDepthMultiplier = 1f;
            _voicedFraction = 0f;
            // Start "as if" a refractory period already elapsed (now 4.0s) so a
            // fresh director is immediately eligible for its first genuine speech-gap inhale
            // rather than being held back by a phantom cooldown from before Reset.
            _sinceLastSpeechGapInhale = SpeechGapInhaleRefractorySeconds;
            // Reset the published bus to mirror the just-reset internal state exactly (13cpm
            // resting rate, 0 depth — the same values RateCpm/Depth resolved to pre-bus) so a
            // Reset with no following Tick reads identically to before the bus existed; the next
            // Tick still starts unpublished-fresh (_publishedInitialized = false) so it snaps
            // rather than slewing from a stale pre-Reset value.
            _publishedRate = _rateCpm;
            _publishedDepth = _depth;
            _publishedInitialized = false;
        }

        /// <summary>
        ///     Arms a one-shot breath event, replacing any event already in flight (re-entry
        ///     retriggers). <paramref name="intensity" /> (0..1) scales the event's peak rate and
        ///     depth multipliers toward 1 — so a Reacting "sharp inhale" can reuse
        ///     <see cref="BreathEventKind.CatchBreath" /> at reduced strength. A
        ///     <see cref="BreathEventKind.None" /> request is ignored. The event modulates only
        ///     the exposed rate/depth multipliers, never the oscillator phase.
        /// </summary>
        public void TriggerEvent(BreathEventKind kind, float intensity = 1f)
        {
            if (kind == BreathEventKind.None) return;

            _activeEvent = kind;
            _activeEventShape = ShapeFor(kind);
            _activeEventIntensity = Mathf.Clamp01(intensity);
            _activeEventElapsed = 0f;
        }

        /// <summary>
        ///     Attempts to arm a gentle <see cref="BreathEventKind.SpeechGapInhale" /> from this
        ///     tick's <see cref="SpeechPulse" />. Only ever considers
        ///     <see cref="SpeechPulseKind.Release" /> — a phrase gap — never <c>Onset</c> or
        ///     <c>Emphasis</c>, and only fires when ALL of the anti-pumping conditions hold:
        ///     the pulse's own <see cref="SpeechPulse.Strength" /> (how decisive the drop below
        ///     the release threshold was) clears a confidence floor, the refractory interval
        ///     since the last speech-gap inhale has elapsed, and no breath event is already in
        ///     flight (the envelope lockout — this call never preempts a running event the way
        ///     <see cref="TriggerEvent" /> does, so it can never stack onto or cut off a
        ///     catch-breath/sigh/inhale-before-speaking/earlier speech-gap inhale). When
        ///     <paramref name="conservativeMode" /> is <c>true</c> (the fast channel is running on
        ///     the no-<c>ISpeechEnergyProvider</c> statistical-cadence fallback — see
        ///     <c>GesticulationDirector.IsStatisticalCadenceActive</c>) both the confidence floor
        ///     and the refractory interval are raised, since a "gap" derived without a real
        ///     speech-energy signal is a much weaker signal of an actual phrase boundary.
        /// </summary>
        /// <returns><see langword="true" /> if a speech-gap inhale was armed this call.</returns>
        public bool TryTriggerSpeechGapInhale(SpeechPulseKind pulseKind, float pulseStrength, bool conservativeMode)
        {
            if (pulseKind != SpeechPulseKind.Release) return false;
            if (_activeEvent != BreathEventKind.None) return false;

            float confidenceThreshold = conservativeMode
                ? SpeechGapInhaleConfidenceThresholdConservative
                : SpeechGapInhaleConfidenceThreshold;
            if (pulseStrength < confidenceThreshold) return false;

            float refractorySeconds = conservativeMode
                ? SpeechGapInhaleRefractorySecondsConservative
                : SpeechGapInhaleRefractorySeconds;
            if (_sinceLastSpeechGapInhale < refractorySeconds) return false;

            TriggerEvent(BreathEventKind.SpeechGapInhale);
            _sinceLastSpeechGapInhale = 0f;
            return true;
        }

        /// <param name="statePolicy">The smoothed per-state policy this tick targets.</param>
        /// <param name="emotion">Emotion body modulator supplying rate/depth scale multipliers.</param>
        /// <param name="targetSlewSeconds">Seconds (time constant) the rate/depth/irregularity targets slew over.</param>
        /// <param name="deltaTime">Seconds elapsed since the previous tick.</param>
        /// <param name="speechEnergy01">
        ///     This tick's speech-energy envelope, 0..1 (already computed by the caller — see
        ///     <c>ISpeechEnergyProvider</c>). Only consulted while <paramref name="isSpeaking" />
        ///     is <see langword="true" /> (completes the speech-coupled exhale).
        /// </param>
        /// <param name="isSpeaking">
        ///     Whether the dialogue state is <c>Speaking</c> this tick — gates
        ///     <see cref="_voicedFraction" />'s EMA goal (toward <paramref name="speechEnergy01" />
        ///     while speaking, toward 0 otherwise).
        /// </param>
        /// <param name="macroDepthScale">
        ///     Multiplier on the depth goal from the controller's idle macro-cycle drift
        ///     (<c>1f + 0.12f * MacroCycleDirector.Energy01</c>) — defaults to 1
        ///     (no-op), so a caller that never passes it composes bit-identically without it.
        /// </param>
        /// <param name="exertionRateMultiplier">
        ///     Multiplier on the published rate from locomotion exertion (e.g.
        ///     <c>1f + ExertionRateBoost * Exertion01</c>) — defaults to 1 (no-op), so a caller
        ///     that never passes it, or has no <c>IExertionSource</c> registered upstream,
        ///     composes bit-identically to the pre-N8 rate.
        /// </param>
        /// <param name="exertionDepthMultiplier">
        ///     Multiplier on the published depth from locomotion exertion (e.g.
        ///     <c>1f + ExertionDepthBoost * Exertion01</c>) — defaults to 1 (no-op), mirroring
        ///     <paramref name="exertionRateMultiplier" />.
        /// </param>
        public void Tick(
            in BodyLanguageStatePolicy statePolicy,
            EmotionBodyModulator emotion,
            float targetSlewSeconds,
            float deltaTime,
            float speechEnergy01,
            bool isSpeaking,
            float macroDepthScale = 1f,
            float exertionRateMultiplier = 1f,
            float exertionDepthMultiplier = 1f)
        {
            float rateGoal = Mathf.Clamp(statePolicy.BreathRateCpm * emotion.BreathRateScale, 4f, 40f);
            float depthGoal = Mathf.Clamp01(statePolicy.BreathDepth * emotion.BreathDepthScale * macroDepthScale);
            float irregularityGoal = Mathf.Clamp01(statePolicy.BreathIrregularity);

            _exertionRateMultiplier = Mathf.Max(0f, exertionRateMultiplier);
            _exertionDepthMultiplier = Mathf.Max(0f, exertionDepthMultiplier);

            float dt = deltaTime > 0f ? deltaTime : 0f;
            _sinceLastSpeechGapInhale += dt;

            // Speech-coupled exhale: slews toward the current speech energy while
            // speaking, toward 0 (fully restored breath) otherwise — never a snap either way.
            if (dt > 0f)
            {
                float voicedGoal = isSpeaking ? Mathf.Clamp01(speechEnergy01) : 0f;
                _voicedFraction += (voicedGoal - _voicedFraction) * (1f - Mathf.Exp(-dt / VoicedFractionTauSeconds));
            }

            if (!_initialized || targetSlewSeconds <= 0f || deltaTime <= 0f)
            {
                _rateCpm = rateGoal;
                _depth = depthGoal;
                _irregularity = irregularityGoal;
                _initialized = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-deltaTime / targetSlewSeconds);
                _rateCpm += (rateGoal - _rateCpm) * alpha;
                _depth += (depthGoal - _depth) * alpha;
                _irregularity += (irregularityGoal - _irregularity) * alpha;
            }

            AdvanceEvent(deltaTime);
            PublishBus(dt);
        }

        /// <summary>
        ///     The modulation bus: composes this tick's internal
        ///     "combined" rate/depth (state target × active breath-event multiplier, plus the
        ///     speech-coupled exhale for depth), then slew-limits the published
        ///     <see cref="RateCpm" />/<see cref="Depth" />
        ///     by <see cref="MaxRateChangeCpmPerSecond" />/<see cref="MaxDepthChangePerSecond" />.
        ///     This is the single point every fast modulator funnels through before a caller can
        ///     ever observe it, so no combination of them can pop or flutter the published value.
        ///     The very first publish snaps (nothing to slew from yet); a zero-or-negative
        ///     <paramref name="dt" /> holds the published values rather than slewing with a zero
        ///     budget.
        /// </summary>
        private void PublishBus(float dt)
        {
            float combinedDepth = Mathf.Clamp01(_depth * _eventDepthMultiplier) *
                                   (1f - ExhaleDepthLoss * _voicedFraction) * _exertionDepthMultiplier;
            float combinedRate = _rateCpm * _eventRateMultiplier * _exertionRateMultiplier;

            if (!_publishedInitialized)
            {
                _publishedDepth = combinedDepth;
                _publishedRate = combinedRate;
                _publishedInitialized = true;
            }
            else if (dt > 0f)
            {
                _publishedDepth = Mathf.MoveTowards(_publishedDepth, combinedDepth, MaxDepthChangePerSecond * dt);
                _publishedRate = Mathf.MoveTowards(_publishedRate, combinedRate, MaxRateChangeCpmPerSecond * dt);
            }
            // else: dt <= 0 this tick — hold the published values rather than slewing with a
            // zero budget (a MoveTowards step of 0 would be a no-op anyway, but this keeps the
            // "hold" intent explicit and avoids computing an unused step of zero).
        }

        /// <summary>
        ///     Advances the active breath-event envelope and recomputes the rate/depth
        ///     multipliers. When no event is active both multipliers are exactly 1 (breathing is
        ///     bit-identical to the eventless path).
        /// </summary>
        private void AdvanceEvent(float deltaTime)
        {
            if (_activeEvent == BreathEventKind.None)
            {
                _eventRateMultiplier = 1f;
                _eventDepthMultiplier = 1f;
                return;
            }

            _activeEventElapsed += deltaTime > 0f ? deltaTime : 0f;

            if (_activeEventElapsed >= _activeEventShape.TotalSeconds)
            {
                _activeEvent = BreathEventKind.None;
                _eventRateMultiplier = 1f;
                _eventDepthMultiplier = 1f;
                return;
            }

            float envelope = EnvelopeValue(_activeEventShape, _activeEventElapsed);
            float ratePeak = Mathf.Lerp(1f, _activeEventShape.RatePeakMultiplier, _activeEventIntensity);
            float depthPeak = Mathf.Lerp(1f, _activeEventShape.DepthPeakMultiplier, _activeEventIntensity);
            _eventRateMultiplier = Mathf.Lerp(1f, ratePeak, envelope);
            _eventDepthMultiplier = Mathf.Lerp(1f, depthPeak, envelope);
        }

        /// <summary>
        ///     C1-eased attack → hold → decay envelope, 0..1. Both the attack and decay use
        ///     <see cref="EaseInOutQuad" /> (zero-velocity at both endpoints), so the multiplier
        ///     it drives never kicks the breath at the event's start, peak, or end.
        /// </summary>
        private static float EnvelopeValue(in BreathEventShape shape, float elapsed)
        {
            if (elapsed <= 0f) return 0f;

            if (elapsed < shape.AttackSeconds)
                return EaseInOutQuad(Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, shape.AttackSeconds)));

            float afterAttack = elapsed - shape.AttackSeconds;
            if (afterAttack < shape.HoldSeconds) return 1f;

            float decayElapsed = afterAttack - shape.HoldSeconds;
            if (decayElapsed >= shape.DecaySeconds) return 0f;

            return 1f - EaseInOutQuad(Mathf.Clamp01(decayElapsed / Mathf.Max(0.0001f, shape.DecaySeconds)));
        }

        private static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

        /// <summary>
        ///     Per-kind envelope shapes and peak multipliers. These are conservative
        ///     starting points tuned by feel; kept as internal constants rather than profile
        ///     surface, which exposes only the three enable toggles.
        /// </summary>
        private static BreathEventShape ShapeFor(BreathEventKind kind) => kind switch
        {
            // Quick, sharp intake: rate spikes, depth lifts, releases over ~1s. The 0.2s attack
            // lets the catch-breath's own ramp start the rate spike smoothly rather than leaning
            // on the downstream smoothing; it still reads as sharp.
            BreathEventKind.CatchBreath => new BreathEventShape(0.2f, 0.1f, 1.0f, 1.8f, 1.3f),

            // Long, deep, slow breath: depth well up, rate well down, over ~one slow cycle.
            BreathEventKind.Sigh => new BreathEventShape(1.0f, 0.4f, 2.6f, 0.55f, 1.6f),

            // Draw breath to speak: a brief deeper/faster inhale as the utterance starts. Its
            // peaks sit slightly below a catch-breath's and its attack/decay is wider, so this
            // once-per-utterance event reads clearly without out-driving the phrase-scale
            // breathing underneath it.
            BreathEventKind.InhaleBeforeSpeaking => new BreathEventShape(0.3f, 0.1f, 0.7f, 1.35f, 1.3f),

            // A gentle top-up inhale in a phrase gap mid-utterance.
            // This event is DEPTH-ONLY (rate peak 1.0, i.e. no rate multiplier at all):
            // a mid-speech top-up must never bend the oscillator's phase velocity — only how deep
            // the current cycle reads — so it can never itself be a source of rate flutter. The
            // attack/hold/decay is wide enough that the event still reads as a soft top-up rather
            // than a snap at its sparse (4-6s refractory) cadence, and the depth peak stays modest
            // because there is no rate lift alongside it to visually compensate for.
            BreathEventKind.SpeechGapInhale => new BreathEventShape(0.3f, 0.15f, 0.9f, 1.0f, 1.12f),

            _ => default
        };
    }
}
