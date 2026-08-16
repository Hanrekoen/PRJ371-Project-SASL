using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyLanguage.Core.Diagnostics;
using Convai.Modules.BodyLanguage.Core.Gestures;
using Convai.Modules.BodyLanguage.Core.Signals;
using Convai.Modules.BodyLanguage.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that turns speech pulses, dialogue state, and suppression into
    ///     co-speech gesticulation: a <b>fast channel</b> of
    ///     head-beats and posture pulses driven by <see cref="SpeechPulse" /> emphasis while
    ///     <c>Speaking</c>/<c>Reacting</c>, and a <b>semantic channel</b> that routes explicit
    ///     <see cref="GestureCue" />s to a <see cref="IConversationalGesturePerformer" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Fast channel / semantic channel separation.</b> These are structurally
    ///         independent code paths with no shared trigger condition: <see cref="Tick" />
    ///         (fed only speech pulses and state) never calls
    ///         <see cref="IConversationalGesturePerformer.TryPerform" />; only
    ///         <see cref="TryEmitCue" /> does, and nothing in this class calls
    ///         <see cref="TryEmitCue" /> from inside <see cref="Tick" />. The separation is
    ///         structural because the rule is absolute: speech energy must never trigger a
    ///         gesture that carries meaning. A stressed syllable must never fire "wave".
    ///     </para>
    ///     <para>
    ///         <b>Fast channel gating.</b> The fast channel runs while
    ///         <c>GesticulationEnabled &amp;&amp; (state == Speaking || state == Reacting)</c>.
    ///         Reacting is included deliberately: a reactive utterance is short but it is still
    ///         speech, and the state ships a <c>GesticulationIntensity</c> of 1.0, so an
    ///         occasional beat riding along with it is the intended reading rather than an
    ///         accident.
    ///     </para>
    ///     <para>
    ///         <b>No-provider fallback.</b> When <c>Context.SpeechEnergyProvider</c>
    ///         is null, feeding <see cref="Tick" /> with <c>hasSpeechEnergyProvider: false</c>
    ///         switches the fast channel from pulse-reactive to a statistical cadence: beats fire
    ///         at a randomized interval around <c>statisticalCadenceIntervalSeconds</c> (default
    ///         far lower rate than the busiest energy-driven cadence — see the profile tooltips),
    ///         still gated on the same state/enabled conditions.
    ///     </para>
    ///     <para>
    ///         <b>Suppression.</b> <see cref="GestureSuppression.FullBody" /> zeroes
    ///         the fast channel outright (no beats, no posture pulse) and makes
    ///         <see cref="TryEmitCue" /> refuse locally without calling the performer.
    ///         <see cref="GestureSuppression.UpperBody" /> scales fast-channel beat/pulse
    ///         intensity by the profile's <c>upperBodySuppressionPostureWeight</c> and still
    ///         refuses semantic cues locally (clips are off under any suppression per the
    ///         performer's own contract; refusing locally here keeps the behavior testable and
    ///         avoids a needless call).
    ///     </para>
    ///     <para>
    ///         <b>Refusal fallback.</b> Whenever a semantic cue attempt is refused — no performer
    ///         registered, or <see cref="IConversationalGesturePerformer.TryPerform" /> itself
    ///         returns <c>false</c> — <see cref="TryEmitCue" /> substitutes a head-beat plus
    ///         posture-pulse (the same primitives the fast channel uses) so the moment still
    ///         reads alive instead of a silent no-op.
    ///     </para>
    ///     <para>
    ///         <b>Semantic channel scope (Reacting affirm/negate).</b> The SDK has no
    ///         genuine, already-published signal that distinguishes an affirmative vs. negative
    ///         Reacting beat: <c>DialogueState</c> carries no valence, and deriving one from the
    ///         Emotion module's label/valence-arousal table would be an invented classifier with
    ///         no signal behind it, which is exactly what this module refuses to do for beat
    ///         CONTENT. This director therefore does NOT auto-emit a cue on entering Reacting;
    ///         <see cref="TryEmitCue" /> is a general-purpose, fully tested semantic entry point
    ///         that a real caller — the scripted API, or a backend signal that genuinely carries
    ///         the distinction — can drive instead.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed, and identical (seed, tick
    ///         input sequence) pairs always produce identical beat schedules — the anti-metronome
    ///         jitter comes from that seeded stream, never <c>UnityEngine.Random</c>.
    ///     </para>
    /// </remarks>
    internal sealed class GesticulationDirector
    {
        /// <summary>
        ///     Minimum raw pulse strength for an Onset to qualify as a beat trigger. Emphasis
        ///     always qualifies; a weak onset must not fire a beat — nor consume the refractory
        ///     window a stronger emphasis moments later would have used.
        /// </summary>
        private const float StrongOnsetMinStrength = 0.5f;

        /// <summary>
        ///     Probability an eligible Emphasis/strong-Onset pulse actually fires a beat (beat
        ///     discipline): at most ~1 beat per 2 strong pulses — sparse reads as
        ///     intentional, a beat-per-syllable reads as a metronome/twitch. A skipped pulse does
        ///     NOT restart the beat-interval timer (see the thinning draw's call site in
        ///     <see cref="Tick" />) — only a pulse that actually fires re-arms the refractory.
        /// </summary>
        private const float BeatThinningProbability = 0.55f;

        /// <summary>Minimum <see cref="SpeechPulseKind.Release" /> strength that arms a phrase-end nod.</summary>
        private const float PhraseEndNodMinStrength = 0.25f;

        /// <summary>
        ///     Duration (seconds) of a phrase-end nod's head-beat request — slower and calmer
        ///     than an ordinary co-speech beat's 0.45–0.65s draw, since this fires on a phrase
        ///     GAP rather than an emphasis peak. Internal so the controller can
        ///     pass it straight through to <c>HeadGestureDirector.TryRequestBeat</c>'s explicit-
        ///     duration overload.
        /// </summary>
        internal const float PhraseEndNodDurationSeconds = 0.9f;

        /// <summary>Fraction of a normal beat's would-be intensity a phrase-end nod requests at.</summary>
        private const float PhraseEndNodIntensityScale = 0.6f;

        /// <summary>Shoulder-shrug one-shot envelope attack seconds.</summary>
        private const float ShrugAttackSeconds = 0.4f;

        /// <summary>Shoulder-shrug one-shot envelope hold seconds.</summary>
        private const float ShrugHoldSeconds = 0.3f;

        /// <summary>Shoulder-shrug one-shot envelope decay seconds.</summary>
        private const float ShrugDecaySeconds = 0.8f;

        /// <summary>Minimum seconds between two shoulder shrugs.</summary>
        private const float ShrugRefractorySeconds = 6f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private float _beatRefractoryRemaining;
        private float _statisticalCadenceRemaining;
        private bool _hasStatisticalCadenceTarget;
        private float _semanticRefractoryRemaining;

        private bool _shrugActive;
        private float _shrugElapsed;
        private float _shrugRefractoryRemaining;

        private bool _wantsHeadBeat;
        private float _headBeatIntensity;
        private bool _wantsPhraseEndNod;
        private float _phraseEndNodIntensity;
        private float _posturePulseIntensity;

        private float _posturePulseElapsed;
        private bool _posturePulseActive;

        private bool _statisticalCadenceActiveLogged;
        private bool _providerPresentLogged;

        /// <summary>
        ///     Whether this tick produced a fast-channel head-beat request. The controller reads
        ///     this immediately after <see cref="Tick" /> and forwards it to
        ///     <c>HeadGestureDirector.TryRequestBeat</c> — this director never holds a reference
        ///     to the head-gesture machinery itself (directors are pure data producers; the
        ///     controller wires the plumbing), matching how <see cref="PostureDirector" />/
        ///     <see cref="BreathingDirector" /> only expose target properties.
        /// </summary>
        public bool WantsHeadBeat => _wantsHeadBeat;

        /// <summary>Intensity (0..1) to request the head-beat at when <see cref="WantsHeadBeat" /> is true.</summary>
        public float HeadBeatIntensity => _headBeatIntensity;

        /// <summary>
        ///     Whether this tick produced a phrase-end slow-nod request: a
        ///     confident <see cref="SpeechPulseKind.Release" /> pulse (a natural phrase gap)
        ///     while beats are enabled requests a slower, calmer nod than an ordinary co-speech
        ///     beat. Read and forwarded the same way as <see cref="WantsHeadBeat" /> — via
        ///     <c>HeadGestureDirector.TryRequestBeat</c>'s explicit-duration overload with
        ///     <see cref="PhraseEndNodDurationSeconds" />.
        /// </summary>
        public bool WantsPhraseEndNod => _wantsPhraseEndNod;

        /// <summary>Intensity (0..1) to request the phrase-end nod at when <see cref="WantsPhraseEndNod" /> is true.</summary>
        public float PhraseEndNodIntensity => _phraseEndNodIntensity;

        /// <summary>
        ///     Current posture-pulse envelope value, 0 (rest) rising fast then decaying — an
        ///     additive contribution the controller folds into <c>PostureSolveInput.TransientLeanTarget</c>
        ///     BEFORE spring integration, so the posture spring's own smoothing still applies
        ///     (no pops, no separate bone writes, no second write guard).
        /// </summary>
        public float PosturePulseValue { get; private set; }

        /// <summary>Whether the fast channel is currently running on the no-provider statistical cadence fallback (diagnostics).</summary>
        public bool IsStatisticalCadenceActive { get; private set; }

        /// <summary>The last semantic cue kind attempted via <see cref="TryEmitCue" /> (diagnostics; <see cref="GestureCueKind.None" /> if never attempted).</summary>
        public GestureCueKind LastCueKind { get; private set; } = GestureCueKind.None;

        /// <summary>Whether the last <see cref="TryEmitCue" /> attempt was accepted by the performer (diagnostics).</summary>
        public bool LastCueAccepted { get; private set; }

        /// <summary>True only for the current dispatch when refusal fallback actually fired.</summary>
        public bool ProceduralFallbackRequested { get; private set; }

        /// <summary>
        ///     Current one-shot shoulder-shrug envelope value, 0..1. Triggered by a
        ///     semantic cue of kind <see cref="GestureCueKind.Uncertain" /> via
        ///     <see cref="TryEmitCue" /> — procedural, so it fires even when the performer
        ///     refuses the clip. Ticked in <see cref="Tick" /> regardless of suppression/state
        ///     gating, since the trigger itself already gates on a genuine cue attempt.
        /// </summary>
        public float ShrugValue { get; private set; }

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, inactive state. Does not reset the seed.</summary>
        public void Reset()
        {
            _beatRefractoryRemaining = 0f;
            _statisticalCadenceRemaining = 0f;
            _hasStatisticalCadenceTarget = false;
            _semanticRefractoryRemaining = 0f;
            _wantsHeadBeat = false;
            _headBeatIntensity = 0f;
            _wantsPhraseEndNod = false;
            _phraseEndNodIntensity = 0f;
            _posturePulseIntensity = 0f;
            _posturePulseElapsed = 0f;
            _posturePulseActive = false;
            PosturePulseValue = 0f;
            IsStatisticalCadenceActive = false;
            _statisticalCadenceActiveLogged = false;
            _providerPresentLogged = false;
            LastCueKind = GestureCueKind.None;
            LastCueAccepted = false;
            ProceduralFallbackRequested = false;
            _shrugActive = false;
            _shrugElapsed = 0f;
            _shrugRefractoryRemaining = 0f;
            ShrugValue = 0f;
        }

        /// <summary>
        ///     Immediately clears any in-flight posture-pulse envelope (Interrupted
        ///     freeze): a beat's posture-pulse still decaying when the character freezes would
        ///     read as motion during a hard pause. Does not touch the beat refractory or the
        ///     one-tick head-beat request flag (already consumed by the controller each tick).
        /// </summary>
        public void ClearPosturePulse()
        {
            _posturePulseActive = false;
            _posturePulseElapsed = 0f;
            _posturePulseIntensity = 0f;
            PosturePulseValue = 0f;
        }

        /// <summary>
        ///     Fast-channel + posture-pulse envelope tick. Never calls
        ///     <see cref="IConversationalGesturePerformer.TryPerform" /> — see remarks on
        ///     channel separation.
        /// </summary>
        /// <param name="state">Current dialogue state (fast channel gates on Speaking/Reacting).</param>
        /// <param name="gesticulationEnabled">The smoothed policy's <c>GesticulationEnabled</c> gate.</param>
        /// <param name="gesticulationIntensity">The smoothed policy's <c>GesticulationIntensity</c> (0..1).</param>
        /// <param name="pulse">This tick's speech pulse (may be <see cref="SpeechPulseKind.None" />).</param>
        /// <param name="hasSpeechEnergyProvider">Whether <c>Context.SpeechEnergyProvider</c> is currently non-null.</param>
        /// <param name="suppression">Current suppression reported by the conversational gesture performer (or <see cref="GestureSuppression.None" /> when absent).</param>
        /// <param name="gestureIntensityScale">Emotion-derived intensity multiplier (<c>EmotionBodyModulator.GestureIntensityScale</c>).</param>
        /// <param name="gestureRateScale">Emotion-derived rate multiplier — higher scale shortens effective refractory/cadence.</param>
        /// <param name="deltaTime">Seconds since the previous tick.</param>
        /// <param name="beatMinIntervalSeconds">Minimum seconds between fast-channel beats.</param>
        /// <param name="beatIntervalVarianceSeconds">± seconds of jitter added to the min interval (anti-metronome).</param>
        /// <param name="beatHeadIntensity">Base head-beat amplitude scale (0..1) before energy/emotion scaling.</param>
        /// <param name="posturePulseAmplitude">Base posture-pulse amplitude scale (0..1) before energy/emotion scaling.</param>
        /// <param name="posturePulseAttackSeconds">Posture-pulse rise time.</param>
        /// <param name="posturePulseDecaySeconds">Posture-pulse decay time.</param>
        /// <param name="energyToIntensityGain">Multiplies <see cref="SpeechPulse.Strength" /> before it scales beat/pulse amplitude.</param>
        /// <param name="statisticalCadenceIntervalSeconds">Mean seconds between fallback beats when no speech-energy provider is registered.</param>
        /// <param name="statisticalCadenceVarianceSeconds">± seconds of jitter on the statistical cadence interval.</param>
        /// <param name="upperBodySuppressionWeight">Intensity multiplier applied to fast-channel output under <see cref="GestureSuppression.UpperBody" />.</param>
        public void Tick(
            DialogueState state,
            bool gesticulationEnabled,
            float gesticulationIntensity,
            in SpeechPulse pulse,
            bool hasSpeechEnergyProvider,
            GestureSuppression suppression,
            float gestureIntensityScale,
            float gestureRateScale,
            float deltaTime,
            float beatMinIntervalSeconds,
            float beatIntervalVarianceSeconds,
            float beatHeadIntensity,
            float posturePulseAmplitude,
            float posturePulseAttackSeconds,
            float posturePulseDecaySeconds,
            float energyToIntensityGain,
            float statisticalCadenceIntervalSeconds,
            float statisticalCadenceVarianceSeconds,
            float upperBodySuppressionWeight,
            BodyLanguageTrace trace)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0xBEA7C0DEu);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;
            _wantsHeadBeat = false;
            _headBeatIntensity = 0f;
            _wantsPhraseEndNod = false;
            _phraseEndNodIntensity = 0f;

            if (_beatRefractoryRemaining > 0f)
                _beatRefractoryRemaining = Mathf.Max(0f, _beatRefractoryRemaining - dt);
            if (_semanticRefractoryRemaining > 0f)
                _semanticRefractoryRemaining = Mathf.Max(0f, _semanticRefractoryRemaining - dt);
            if (_shrugRefractoryRemaining > 0f)
                _shrugRefractoryRemaining = Mathf.Max(0f, _shrugRefractoryRemaining - dt);

            TickPosturePulseEnvelope(dt, posturePulseAttackSeconds, posturePulseDecaySeconds);
            // Independent of suppression/state gating below — the trigger (TryEmitCue) already
            // only arms this on a genuine, non-refractory cue attempt.
            TickShrugEnvelope(dt);

            bool fastChannelEligible = gesticulationEnabled && (state == DialogueState.Speaking || state == DialogueState.Reacting);

            if (suppression == GestureSuppression.FullBody || !fastChannelEligible)
            {
                IsStatisticalCadenceActive = false;
                _hasStatisticalCadenceTarget = false;
                return;
            }

            float rateScale = gestureRateScale > 0f ? gestureRateScale : 1f;
            float suppressionScale = suppression == GestureSuppression.UpperBody
                ? Mathf.Clamp01(upperBodySuppressionWeight)
                : 1f;

            if (!hasSpeechEnergyProvider)
            {
                // Clear the "provider present" latch so a future provider loss re-logs the
                // statistical-cadence degradation exactly once per loss, not once ever.
                _providerPresentLogged = false;

                if (!_statisticalCadenceActiveLogged)
                {
                    _statisticalCadenceActiveLogged = true;
                    if (trace != null && trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                        trace.State(
                            "No ISpeechEnergyProvider — fast-channel gesticulation running on statistical " +
                            "cadence fallback (lower, randomized rate).");
                }

                IsStatisticalCadenceActive = true;
                TickStatisticalCadence(
                    dt, rateScale, statisticalCadenceIntervalSeconds, statisticalCadenceVarianceSeconds,
                    beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale, suppressionScale,
                    gesticulationIntensity);
                return;
            }

            _statisticalCadenceActiveLogged = false;
            IsStatisticalCadenceActive = false;

            if (!_providerPresentLogged)
            {
                _providerPresentLogged = true;
                if (trace != null && trace.Verbosity >= BodyLanguageTraceVerbosity.Detail)
                    trace.Detail("ISpeechEnergyProvider present — fast-channel gesticulation is pulse-driven.");
            }

            // Phrase-end nod: a confident Release pulse — a natural phrase gap
            // — is a distinct trigger from the Emphasis/Onset beat below, requesting a slower,
            // calmer nod instead of a quick accent. Shares the beat refractory/interval (the same
            // single-slot fire-now-or-drop semantics as an ordinary beat) but is NOT subject to
            // the emphasis-beat thinning draw further down — a phrase boundary is already a rare,
            // deliberate event, not a per-syllable accent that needs further sparsification.
            if (pulse.Kind == SpeechPulseKind.Release &&
                pulse.Strength >= PhraseEndNodMinStrength &&
                _beatRefractoryRemaining <= 0f)
            {
                float phraseStrength = Mathf.Clamp01(pulse.Strength * Mathf.Max(0f, energyToIntensityGain));
                FirePhraseEndNod(phraseStrength, beatHeadIntensity, gestureIntensityScale, gesticulationIntensity);

                float phraseInterval = SampleInterval(beatMinIntervalSeconds, beatIntervalVarianceSeconds) / rateScale;
                _beatRefractoryRemaining = Mathf.Max(0f, phraseInterval);
                return;
            }

            // Emphasis always qualifies; an Onset must be strong — a weak phrase
            // start neither fires a beat nor consumes the refractory a real emphasis would use.
            bool isEmphasisLike = pulse.Kind == SpeechPulseKind.Emphasis ||
                                  (pulse.Kind == SpeechPulseKind.Onset && pulse.Strength >= StrongOnsetMinStrength);
            if (!isEmphasisLike) return;
            if (_beatRefractoryRemaining > 0f) return;

            // Beat discipline: thin eligible pulses down to a ~55% fire rate —
            // at most ~1 beat per 2 strong pulses, so consecutive beats read as intentional
            // accents rather than a beat-per-syllable metronome. A skipped pulse deliberately does
            // NOT touch _beatRefractoryRemaining — the interval timer only (re)starts on an
            // actual fire, below.
            if (_random.Value >= BeatThinningProbability) return;

            float strength = Mathf.Clamp01(pulse.Strength * Mathf.Max(0f, energyToIntensityGain));
            FireBeat(strength, beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale, suppressionScale, gesticulationIntensity);

            float interval = SampleInterval(beatMinIntervalSeconds, beatIntervalVarianceSeconds) / rateScale;
            _beatRefractoryRemaining = Mathf.Max(0f, interval);
        }

        /// <summary>
        ///     Arms a phrase-end slow nod (see <see cref="WantsPhraseEndNod" />):
        ///     the same amplitude derivation <see cref="FireBeat" /> uses for an ordinary beat's
        ///     head-gesture intensity, scaled down by <see cref="PhraseEndNodIntensityScale" />.
        ///     Deliberately does NOT touch the posture-pulse envelope — a phrase-end nod is a
        ///     head-only punctuation gesture, not an emphasis accent (simplest faithful reading of
        ///     "request a SLOW beat" — see spec deviation note).
        /// </summary>
        private void FirePhraseEndNod(float strength, float beatHeadIntensity, float gestureIntensityScale, float gesticulationIntensity)
        {
            float emotionScale = gestureIntensityScale > 0f ? gestureIntensityScale : 1f;
            float baseAmplitude = Mathf.Clamp01(strength * Mathf.Clamp01(gesticulationIntensity) * emotionScale);

            _wantsPhraseEndNod = true;
            _phraseEndNodIntensity = Mathf.Clamp01(beatHeadIntensity * baseAmplitude) * PhraseEndNodIntensityScale;
        }

        private void TickStatisticalCadence(
            float dt,
            float rateScale,
            float intervalSeconds,
            float varianceSeconds,
            float beatHeadIntensity,
            float posturePulseAmplitude,
            float gestureIntensityScale,
            float suppressionScale,
            float gesticulationIntensity)
        {
            if (!_hasStatisticalCadenceTarget)
            {
                _statisticalCadenceRemaining = SampleInterval(intervalSeconds, varianceSeconds) / rateScale;
                _hasStatisticalCadenceTarget = true;
                return;
            }

            _statisticalCadenceRemaining -= dt;
            if (_statisticalCadenceRemaining > 0f) return;

            // Statistical-cadence beats use a fixed moderate strength (there is no real energy
            // signal to draw from) — the same amplitude a mid-strength emphasis pulse would use.
            const float syntheticStrength = 0.6f;
            FireBeat(syntheticStrength, beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale, suppressionScale, gesticulationIntensity);

            _statisticalCadenceRemaining = SampleInterval(intervalSeconds, varianceSeconds) / rateScale;
        }

        private void FireBeat(
            float strength,
            float beatHeadIntensity,
            float posturePulseAmplitude,
            float gestureIntensityScale,
            float suppressionScale,
            float gesticulationIntensity)
        {
            float emotionScale = gestureIntensityScale > 0f ? gestureIntensityScale : 1f;
            // suppressionScale (UpperBody's posture-suppression weight) applies ONLY to the
            // posture-pulse below, never to the head-beat: the head-beat is a head-channel
            // gesture composed by Gaze over the talk clip, and UpperBody suppression
            // means "posture reduced, breath stays" — not "head-gesture reduced". (FullBody
            // already returns early above, so this distinction only matters for None vs
            // UpperBody.)
            float baseAmplitude = Mathf.Clamp01(strength * Mathf.Clamp01(gesticulationIntensity) * emotionScale);

            _wantsHeadBeat = true;
            _headBeatIntensity = Mathf.Clamp01(beatHeadIntensity * baseAmplitude);

            // A retrigger while an envelope is still decaying keeps the LARGER of the two
            // amplitudes for this envelope only — never a persistent ratchet across beats: an
            // idle envelope always starts from exactly the new beat's own amplitude, so a quiet
            // passage after a loud one correctly reads quiet again.
            float pulseAmplitude = Mathf.Clamp01(posturePulseAmplitude * baseAmplitude * Mathf.Clamp01(suppressionScale));
            _posturePulseIntensity = _posturePulseActive
                ? Mathf.Max(_posturePulseIntensity, pulseAmplitude)
                : pulseAmplitude;
            _posturePulseElapsed = 0f;
            _posturePulseActive = true;
        }

        /// <summary>
        ///     Advances the posture-pulse's own attack/decay envelope — an additive, zero-alloc,
        ///     deterministic transient computed purely from elapsed-since-trigger time (no
        ///     coroutines). Attack uses an ease-out curve (fast rise, zero-velocity settle into
        ///     the peak) and decay eases back to zero, both C1-continuous at the shared peak so
        ///     the transition from rising to decaying never pops — mirroring the
        ///     <see cref="Core.Pose.BreathSolver" />/<see cref="Gestures.HeadGestureProgram" />
        ///     discipline of zero-derivative envelope joins.
        /// </summary>
        private void TickPosturePulseEnvelope(float dt, float attackSeconds, float decaySeconds)
        {
            if (!_posturePulseActive)
            {
                PosturePulseValue = 0f;
                return;
            }

            _posturePulseElapsed += dt;

            float attack = Mathf.Max(0.001f, attackSeconds);
            float decay = Mathf.Max(0.001f, decaySeconds);

            if (_posturePulseElapsed < attack)
            {
                float t = Mathf.Clamp01(_posturePulseElapsed / attack);
                PosturePulseValue = _posturePulseIntensity * EaseOutQuad(t);
                return;
            }

            float decayElapsed = _posturePulseElapsed - attack;
            if (decayElapsed >= decay)
            {
                _posturePulseActive = false;
                PosturePulseValue = 0f;
                return;
            }

            float u = Mathf.Clamp01(decayElapsed / decay);
            PosturePulseValue = _posturePulseIntensity * (1f - EaseOutQuad(u));
        }

        private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

        /// <summary>Arms the one-shot shoulder shrug if its own refractory has elapsed. Silently ignored otherwise — never a queued/pending state.</summary>
        private void TryTriggerShrug()
        {
            if (_shrugRefractoryRemaining > 0f) return;

            _shrugActive = true;
            _shrugElapsed = 0f;
            _shrugRefractoryRemaining = ShrugRefractorySeconds;
        }

        /// <summary>
        ///     Advances the shrug's own eased attack → hold → decay envelope — same recipe as
        ///     <see cref="BreathingDirector" />'s one-shot breath events.
        /// </summary>
        private void TickShrugEnvelope(float dt)
        {
            if (!_shrugActive)
            {
                ShrugValue = 0f;
                return;
            }

            _shrugElapsed += dt;
            float total = ShrugAttackSeconds + ShrugHoldSeconds + ShrugDecaySeconds;
            if (_shrugElapsed >= total)
            {
                _shrugActive = false;
                ShrugValue = 0f;
                return;
            }

            if (_shrugElapsed < ShrugAttackSeconds)
            {
                ShrugValue = EaseInOutQuad(Mathf.Clamp01(_shrugElapsed / Mathf.Max(0.0001f, ShrugAttackSeconds)));
                return;
            }

            float afterAttack = _shrugElapsed - ShrugAttackSeconds;
            if (afterAttack < ShrugHoldSeconds)
            {
                ShrugValue = 1f;
                return;
            }

            float decayElapsed = afterAttack - ShrugHoldSeconds;
            ShrugValue = 1f - EaseInOutQuad(Mathf.Clamp01(decayElapsed / Mathf.Max(0.0001f, ShrugDecaySeconds)));
        }

        private static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

        private float SampleInterval(float baseSeconds, float varianceSeconds)
        {
            float baseline = Mathf.Max(0f, baseSeconds);
            if (varianceSeconds <= 0f) return baseline;
            return Mathf.Max(0.01f, baseline + _random.Range(-varianceSeconds, varianceSeconds));
        }

        /// <summary>
        ///     Semantic-channel entry point: attempts to perform an explicit
        ///     <see cref="GestureCue" /> through <paramref name="performer" />. Refuses locally
        ///     (never calling <see cref="IConversationalGesturePerformer.TryPerform" />) when
        ///     <paramref name="performer" /> is null or <paramref name="suppression" /> is not
        ///     <see cref="GestureSuppression.None" />; a refused attempt (local or from the
        ///     performer itself) substitutes a head-beat + posture-pulse so the moment still
        ///     reads alive (refusal fallback) unless <paramref name="suppression" /> is
        ///     <see cref="GestureSuppression.FullBody" />, in which case the fallback is skipped
        ///     too (full-body suppression means posture/breath are already fading to zero — a
        ///     fresh pulse would fight that fade).
        /// </summary>
        /// <remarks>
        ///     Never called from <see cref="Tick" /> — see the class remarks on channel
        ///     separation. Gated by <paramref name="semanticCueRefractorySeconds" /> so repeated
        ///     calls (e.g. a scripted loop) cannot spam cues faster than the profile allows.
        /// </remarks>
        public bool TryEmitCue(
            in GestureCue cue,
            IConversationalGesturePerformer performer,
            GestureSuppression suppression,
            float gestureIntensityScale,
            float semanticCueRefractorySeconds,
            float beatHeadIntensity,
            float posturePulseAmplitude,
            BodyLanguageTrace trace)
        {
            LastCueKind = cue.Kind;
            LastCueAccepted = false;
            ProceduralFallbackRequested = false;

            if (cue.Kind == GestureCueKind.None) return false;
            if (_semanticRefractoryRemaining > 0f) return false;

            // Procedural, not clip-gated: fires alongside whatever clip dispatch
            // happens below, including when suppression/no-performer refuses it.
            if (cue.Kind == GestureCueKind.Uncertain)
                TryTriggerShrug();

            if (suppression != GestureSuppression.None || performer == null)
            {
                if (suppression != GestureSuppression.FullBody)
                    EmitRefusalFallback(cue, beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale);
                _semanticRefractoryRemaining = Mathf.Max(0f, semanticCueRefractorySeconds);
                return false;
            }

            bool accepted = performer.TryPerform(in cue);
            LastCueAccepted = accepted;
            _semanticRefractoryRemaining = Mathf.Max(0f, semanticCueRefractorySeconds);

            if (!accepted)
            {
                EmitRefusalFallback(cue, beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale);
                if (trace != null && trace.Verbosity >= BodyLanguageTraceVerbosity.State)
                    trace.State($"Semantic cue {cue.Kind} refused by performer — substituting head-beat/posture-pulse.");
            }

            return accepted;
        }

        private void EmitRefusalFallback(in GestureCue cue, float beatHeadIntensity, float posturePulseAmplitude, float gestureIntensityScale)
        {
            ProceduralFallbackRequested = true;
            // A refused cue still deserves a clearly visible substitute (the whole point is
            // "the moment still reads alive instead of a silent no-op"), so the strength floor
            // is 0.5 regardless of how quiet the original cue's intensity was — FireBeat's own
            // gesticulationIntensity parameter is passed as 1 (full) since suppression/emotion
            // scaling is already applied via strength and gestureIntensityScale here.
            float strength = Mathf.Max(0.5f, Mathf.Clamp01(cue.Intensity));
            FireBeat(strength, beatHeadIntensity, posturePulseAmplitude, gestureIntensityScale, suppressionScale: 1f, gesticulationIntensity: 1f);
        }
    }
}
