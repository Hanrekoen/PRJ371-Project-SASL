using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Modules.Emotion.Profiles;
using UnityEngine;

namespace Convai.Modules.Emotion.Core
{
    /// <summary>
    ///     Pure-POCO per-emotion score smoothing and micro-expression burst processor
    ///     backed by a taxonomy-driven design.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Maintains three score tables: a target table written by callers
    ///         (e.g. a server event), a current table updated each tick by exponential
    ///         smoothing, and a public output table that additionally folds in micro-expression
    ///         overshoot.
    ///     </para>
    ///     <para>
    ///         Tables are keyed by canonical lowercase label from the supplied
    ///         <see cref="IEmotionTaxonomy" />. Allocation-free in steady state.
    ///     </para>
    /// </remarks>
    internal sealed class EmotionScoreAccumulator
    {
        private readonly IEmotionTaxonomy _taxonomy;
        private readonly Dictionary<string, float> _targetScores;
        private readonly Dictionary<string, float> _currentScores;
        private readonly Dictionary<string, float> _previousTargets;
        private readonly Dictionary<string, float> _outputScores;

        private float _lerpSpeed;
        private float _decaySpeed;

        // Per-taxonomy-label attack/decay speeds, aligned index-for-index to _taxonomy.Emotions.
        // Defaulted to the global speed for every label; SetPerEmotionDynamics overrides individual
        // entries. Kept in sync with the global speed via _dynamicsOverridden (see SetLerpSpeed/
        // SetDecaySpeed/SetPerEmotionDynamics) rather than storing labels in a dictionary, so Tick
        // stays a flat array read with no per-frame lookup.
        private readonly float[] _attackSpeeds;
        private readonly float[] _decaySpeeds;
        private readonly bool[] _dynamicsOverridden;

        private bool _microBurstEnabled;
        private float _microBurstDuration = 0.25f;
        private float _microBurstOvershoot = 1.4f;
        private float _microBurstThreshold = 0.15f;
        private string _burstLabel;
        private float _burstTimeRemaining;

        // ── Persona baseline / runtime mood: two-slot crossfade (current + outgoing) ────────
        // "current" is the slot rising toward (or holding at) the active target intensity;
        // "outgoing" is the slot fading toward 0 after a retarget to a different label. Both
        // slots fold into OutputScores (see Tick); only GetMood/SetPersonaBaseline(Target)
        // read/write them, so GetDominant, which reports the transient only, is never affected.
        private string _moodCurrentLabel;
        private float _moodCurrentValue;
        private float _moodCurrentTarget;
        private string _moodOutgoingLabel;
        private float _moodOutgoingValue;
        private float _moodRate = 3f; // k = 3 / max(0.01, transitionSeconds); harmless default until a real transition is requested

        // ── Mood drift — a separate (label, intensity) channel that never mutates the
        // anchor mood slots above. Advanced in Tick from the dominant non-neutral transient of
        // the same frame; see ConfigureMoodDrift/Tick/ApplyMoodFold.
        private const float DriftActivationThreshold = 0.15f;

        private bool _moodDriftEnabled;
        private float _moodDriftRate = 0.02f;
        private float _moodRecoveryRate = 0.05f;
        private float _moodDriftMaxIntensity = 0.25f;
        private string _driftLabel;
        private float _driftValue;

        // ── Mood pickup — a fourth (label, intensity) channel fed by a witnessed
        // OTHER character's dominant transient (see ConvaiEmotionController's witness scan). Rates
        // and the intensity cap live controller-side (see SetContagionTarget); this channel only
        // eases the already-capped target and folds it in exactly like drift, never mutating the
        // anchor mood slots or the drift channel. Disabled (the default) contributes nothing.
        private const float ContagionEchoAttackRate = 1.5f;
        private const float ContagionEchoReleaseRate = 0.8f;

        private bool _contagionEnabled;
        private string _echoTargetLabel;
        private float _echoTargetIntensity;
        private string _echoLabel;
        private float _echoValue;

        // ── Outcome beat: a brief, self-expiring mood nudge (e.g. "satisfied" just after an
        // action succeeds) that rides ON TOP of whatever mood is active and lifts off again.
        // Like drift and echo it is its own (label, value) channel and never mutates the anchor
        // mood slots.
        //
        // This exists because the beat used to be implemented as SetMood() followed by
        // ClearMood(), and ClearMood means "return to the AUTHORED baseline" — so a two-second
        // reaction silently discarded a gameplay SetMood() and any accumulated drift, breaking
        // the documented SetMood > Initial Mood > profile baseline precedence from inside the
        // module itself.
        private string _beatLabel;
        private float _beatValue;
        private float _beatTargetIntensity;
        private float _beatHoldRemaining;
        private float _beatRate = 2f; // k = 3 / max(0.01, transitionSeconds), same shape as _moodRate

        public EmotionScoreAccumulator(IEmotionTaxonomy taxonomy, float lerpSpeed = 5f, float decaySpeed = 2f)
        {
            _taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
            _lerpSpeed = Mathf.Max(0.01f, lerpSpeed);
            _decaySpeed = Mathf.Max(0.01f, decaySpeed);
            _moodCurrentLabel = taxonomy.Neutral.Label;
            _moodCurrentValue = 0f;
            _moodCurrentTarget = 0f;
            _moodOutgoingLabel = null;
            _moodOutgoingValue = 0f;

            int capacity = taxonomy.Emotions.Count;
            _targetScores = new Dictionary<string, float>(capacity, StringComparer.OrdinalIgnoreCase);
            _currentScores = new Dictionary<string, float>(capacity, StringComparer.OrdinalIgnoreCase);
            _previousTargets = new Dictionary<string, float>(capacity, StringComparer.OrdinalIgnoreCase);
            _outputScores = new Dictionary<string, float>(capacity, StringComparer.OrdinalIgnoreCase);

            _attackSpeeds = new float[capacity];
            _decaySpeeds = new float[capacity];
            _dynamicsOverridden = new bool[capacity];

            for (int i = 0; i < taxonomy.Emotions.Count; i++)
            {
                string label = taxonomy.Emotions[i].Label;
                _targetScores[label] = 0f;
                _currentScores[label] = 0f;
                _previousTargets[label] = 0f;
                _outputScores[label] = 0f;
                _attackSpeeds[i] = _lerpSpeed;
                _decaySpeeds[i] = _decaySpeed;
            }
        }

        /// <summary>Read-only view of the per-emotion output scores for the current frame.</summary>
        public IReadOnlyDictionary<string, float> OutputScores => _outputScores;

        /// <summary>
        ///     Updates the interpolation (attack) speed at runtime. Applies immediately to every
        ///     taxonomy label that has no per-emotion override from <see cref="SetPerEmotionDynamics" />.
        /// </summary>
        public void SetLerpSpeed(float lerpSpeed)
        {
            _lerpSpeed = Mathf.Max(0.01f, lerpSpeed);
            for (int i = 0; i < _attackSpeeds.Length; i++)
            {
                if (!_dynamicsOverridden[i]) _attackSpeeds[i] = _lerpSpeed;
            }
        }

        /// <summary>
        ///     Updates the decay speed applied when targets are zero. Applies immediately to every
        ///     taxonomy label that has no per-emotion override from <see cref="SetPerEmotionDynamics" />.
        /// </summary>
        public void SetDecaySpeed(float decaySpeed)
        {
            _decaySpeed = Mathf.Max(0.01f, decaySpeed);
            for (int i = 0; i < _decaySpeeds.Length; i++)
            {
                if (!_dynamicsOverridden[i]) _decaySpeeds[i] = _decaySpeed;
            }
        }

        /// <summary>
        ///     Overrides per-emotion attack/decay smoothing speeds from an authored profile's
        ///     dynamics list. Labels absent from <paramref name="entries" /> (or when
        ///     <paramref name="entries" /> is <c>null</c>/empty) keep the global
        ///     <see cref="SetLerpSpeed" />/<see cref="SetDecaySpeed" /> speed — this is what makes an
        ///     empty list leaves every label on the global speed. Safe to call more than once (e.g. on profile
        ///     rebuild); each call recomputes the full override state from scratch.
        /// </summary>
        public void SetPerEmotionDynamics(IReadOnlyList<EmotionDynamicsEntry> entries)
        {
            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                _dynamicsOverridden[i] = false;
                _attackSpeeds[i] = _lerpSpeed;
                _decaySpeeds[i] = _decaySpeed;
            }

            if (entries == null) return;

            for (int e = 0; e < entries.Count; e++)
            {
                EmotionDynamicsEntry entry = entries[e];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Label)) continue;

                for (int i = 0; i < _taxonomy.Emotions.Count; i++)
                {
                    if (!string.Equals(_taxonomy.Emotions[i].Label, entry.Label, StringComparison.OrdinalIgnoreCase))
                        continue;

                    float attack = entry.AttackSpeed;
                    float decay = entry.DecaySpeed;
                    if (float.IsNaN(attack) || float.IsInfinity(attack)) attack = _lerpSpeed;
                    if (float.IsNaN(decay) || float.IsInfinity(decay)) decay = _decaySpeed;

                    _attackSpeeds[i] = Mathf.Max(0.01f, attack);
                    _decaySpeeds[i] = Mathf.Max(0.01f, decay);
                    _dynamicsOverridden[i] = true;
                    break;
                }
            }
        }

        /// <summary>Configures micro-expression burst behavior.</summary>
        public void ConfigureMicroBurst(bool enabled, float duration, float overshoot, float threshold)
        {
            _microBurstEnabled = enabled;
            _microBurstDuration = Mathf.Max(0.05f, duration);
            _microBurstOvershoot = Mathf.Max(1f, overshoot);
            _microBurstThreshold = Mathf.Clamp01(threshold);
        }

        /// <summary>
        ///     Configures the mood drift channel: sustained conversational transients slowly tint
        ///     the resting mood, decaying back to 0 once the sustaining transient fades or
        ///     switches label. Disabled (the default) means the channel contributes nothing —
        ///     output is untouched. Never mutates the anchor
        ///     persona-baseline/runtime-mood slots (see <see cref="SetPersonaBaselineTarget" />).
        /// </summary>
        public void ConfigureMoodDrift(bool enabled, float driftRate, float recoveryRate, float maxIntensity)
        {
            _moodDriftEnabled = enabled;
            _moodDriftRate = Mathf.Clamp(driftRate, 0.001f, 0.5f);
            _moodRecoveryRate = Mathf.Clamp(recoveryRate, 0.001f, 1f);
            _moodDriftMaxIntensity = Mathf.Clamp01(maxIntensity);
        }

        /// <summary>
        ///     Enables/disables the mood-pickup echo channel. Disabled
        ///     means the channel never advances and contributes nothing to <see cref="OutputScores" />,
        ///     so the channel contributes nothing. Rates/cap are not configured here — the
        ///     controller already caps <see cref="SetContagionTarget" />'s intensity before calling
        ///     it, so this method only needs the on/off gate.
        /// </summary>
        public void ConfigureContagion(bool enabled)
        {
            _contagionEnabled = enabled;
        }

        /// <summary>
        ///     Sets the current witness-scan result — the strongest nearby character's
        ///     dominant transient, already capped by the caller
        ///     (<see cref="Convai.Modules.Emotion.Components.ConvaiEmotionController" />'s witness
        ///     scan) at <c>Contagion Max Intensity</c>. An empty/unknown label or a
        ///     non-positive intensity means "no candidate this scan": the echo target becomes
        ///     <c>(null, 0)</c>, so a lone/no-candidate character naturally decays any residual echo
        ///     to zero. Plain-field write; zero allocation. No-op on the eased value itself — that
        ///     only happens in <see cref="Tick" /> — so calling this before Contagion is enabled is
        ///     harmless.
        /// </summary>
        public void SetContagionTarget(string canonicalLabel, float intensity)
        {
            if (string.IsNullOrEmpty(canonicalLabel) || intensity <= 0f)
            {
                _echoTargetLabel = null;
                _echoTargetIntensity = 0f;
                return;
            }

            _echoTargetLabel = canonicalLabel;
            _echoTargetIntensity = Mathf.Clamp01(intensity);
        }

        /// <summary>
        ///     Configures the persona baseline (resting mood) the face relaxes toward instead of
        ///     true neutral, snapping immediately. The baseline is folded into
        ///     <see cref="OutputScores" /> (render) only; it never appears via
        ///     <see cref="GetDominant" />, which stays transient-only. Neutral, empty, or unknown
        ///     (non-taxonomy) labels are treated as "no baseline". Equivalent to
        ///     <see cref="SetPersonaBaselineTarget" /> with <c>transitionSeconds = 0</c>.
        /// </summary>
        public void SetPersonaBaseline(string canonicalLabel, float intensity) =>
            SetPersonaBaselineTarget(canonicalLabel, intensity, 0f);

        /// <summary>
        ///     Retargets the persona baseline / runtime mood, smoothing over
        ///     <paramref name="transitionSeconds" /> instead of snapping. Neutral, empty, unknown
        ///     (non-taxonomy), or zero-intensity input is treated as "transition to no mood": both
        ///     the current and outgoing slots decay to exactly 0 (label bookkeeping is left alone,
        ///     since <see cref="GetMood" /> already reports the taxonomy neutral label once both
        ///     slot values reach 0). <paramref name="transitionSeconds" /> &lt;= 0 snaps both slots
        ///     immediately (see <see cref="SetPersonaBaseline" />).
        /// </summary>
        /// <remarks>
        ///     Internally this is a two-slot crossfade (current + outgoing), exponentially
        ///     smoothed in <see cref="Tick" /> at rate <c>k = 3 / max(0.01, transitionSeconds)</c>.
        ///     Retargeting onto the label already fading out in the outgoing slot swaps the two
        ///     slots (the outgoing slot becomes current and resumes rising from its current
        ///     value); retargeting onto any other new label retires whatever is currently in the
        ///     outgoing slot immediately and moves the current slot's (label, value) into it. That
        ///     retirement is an accepted simplification — a residual mid-fade value can be dropped
        ///     rather than continuing to decay — since only two slots are ever tracked.
        /// </remarks>
        public void SetPersonaBaselineTarget(string canonicalLabel, float intensity, float transitionSeconds)
        {
            ResolveMoodTarget(canonicalLabel, intensity, out string targetLabel, out float targetIntensity);

            if (targetLabel == null)
            {
                // "No mood": leave slot labels alone, just decay both toward 0.
                _moodCurrentTarget = 0f;
            }
            else if (string.Equals(targetLabel, _moodCurrentLabel, StringComparison.OrdinalIgnoreCase))
            {
                // Same label as the current slot: no swap, just retarget its intensity.
                _moodCurrentTarget = targetIntensity;
            }
            else if (!string.IsNullOrEmpty(_moodOutgoingLabel) &&
                     string.Equals(targetLabel, _moodOutgoingLabel, StringComparison.OrdinalIgnoreCase))
            {
                // Retarget mid-crossfade back onto the outgoing slot's label: swap slots so the
                // formerly-outgoing value resumes rising instead of restarting from 0.
                string swapLabel = _moodOutgoingLabel;
                float swapValue = _moodOutgoingValue;

                _moodOutgoingLabel = _moodCurrentLabel;
                _moodOutgoingValue = _moodCurrentValue;

                _moodCurrentLabel = swapLabel;
                _moodCurrentValue = swapValue;
                _moodCurrentTarget = targetIntensity;
            }
            else
            {
                // Unrelated new label: current slot retires into outgoing (dropping any prior
                // outgoing residual instantly, see remarks); the new label becomes current,
                // rising from 0.
                _moodOutgoingLabel = _moodCurrentLabel;
                _moodOutgoingValue = _moodCurrentValue;

                _moodCurrentLabel = targetLabel;
                _moodCurrentValue = 0f;
                _moodCurrentTarget = targetIntensity;
            }

            if (transitionSeconds <= 0f)
            {
                _moodCurrentValue = _moodCurrentTarget;
                _moodOutgoingValue = 0f;
            }
            else
            {
                _moodRate = 3f / Mathf.Max(0.01f, transitionSeconds);
            }
        }

        /// <summary>
        ///     Resolves raw mood-setter input into a validated taxonomy label + clamped
        ///     intensity, or <c>(null, 0)</c> when the input means "no mood" (empty/neutral/
        ///     unknown label, or non-positive intensity).
        /// </summary>
        private void ResolveMoodTarget(string canonicalLabel, float intensity, out string resolvedLabel, out float resolvedIntensity)
        {
            float clamped = Mathf.Clamp01(intensity);
            if (string.IsNullOrEmpty(canonicalLabel) ||
                string.Equals(canonicalLabel, _taxonomy.Neutral.Label, StringComparison.OrdinalIgnoreCase) ||
                !_currentScores.ContainsKey(canonicalLabel) ||
                clamped <= 0f)
            {
                resolvedLabel = null;
                resolvedIntensity = 0f;
                return;
            }

            resolvedLabel = canonicalLabel;
            resolvedIntensity = clamped;
        }

        /// <summary>
        ///     Returns the strongest of the mood channels — the persona-baseline/runtime-mood
        ///     current slot, its outgoing crossfade slot, the drift channel, and the outcome-beat
        ///     channel — and its current smoothed intensity. Ties prefer the current slot, then the
        ///     outgoing slot, then drift, then the beat. This is NEVER the dominant emotion — use
        ///     <see cref="GetDominant" /> for
        ///     the transient (server-driven) state. When no mood/drift is active, returns the
        ///     taxonomy's neutral label and 0.
        /// </summary>
        public void GetMood(out string moodLabel, out float moodScore)
        {
            string strongerLabel;
            float strongerValue;

            if (_moodCurrentValue >= _moodOutgoingValue)
            {
                strongerLabel = _moodCurrentLabel;
                strongerValue = _moodCurrentValue;
            }
            else
            {
                strongerLabel = _moodOutgoingLabel;
                strongerValue = _moodOutgoingValue;
            }

            if (_driftValue > strongerValue)
            {
                strongerLabel = _driftLabel;
                strongerValue = _driftValue;
            }

            if (_beatValue > strongerValue)
            {
                strongerLabel = _beatLabel;
                strongerValue = _beatValue;
            }

            if (strongerValue <= 0f || string.IsNullOrEmpty(strongerLabel))
            {
                moodLabel = _taxonomy.Neutral.Label;
                moodScore = 0f;
                return;
            }

            moodLabel = strongerLabel;
            moodScore = strongerValue;
        }

        /// <summary>
        ///     Sets a single emotion as the active target. All others decay toward zero.
        ///     Triggers a micro-burst when the delta exceeds <c>_microBurstThreshold</c>.
        /// </summary>
        public void SetTargetEmotion(string canonicalLabel, float score)
        {
            float clamped = Mathf.Clamp01(score);
            string neutral = _taxonomy.Neutral.Label;

            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                string label = _taxonomy.Emotions[i].Label;
                float newTarget = string.Equals(label, canonicalLabel, StringComparison.OrdinalIgnoreCase)
                    ? clamped
                    : 0f;

                MaybeTriggerBurst(label, newTarget, neutral);
                _previousTargets[label] = newTarget;
                _targetScores[label] = newTarget;
            }
        }

        /// <summary>
        ///     Sets target scores for multiple emotions at once. Any emotion absent from the
        ///     input is set to zero.
        /// </summary>
        public void SetTargetEmotions(IReadOnlyDictionary<string, float> scores)
        {
            string neutral = _taxonomy.Neutral.Label;

            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                string label = _taxonomy.Emotions[i].Label;
                float newTarget = scores != null && scores.TryGetValue(label, out float s)
                    ? Mathf.Clamp01(s)
                    : 0f;

                MaybeTriggerBurst(label, newTarget, neutral);
                _previousTargets[label] = newTarget;
                _targetScores[label] = newTarget;
            }
        }

        /// <summary>
        ///     Sets target scores for multiple emotions at once from parallel arrays (zero-alloc
        ///     overload). Any taxonomy emotion whose label is absent from
        ///     <paramref name="labels" /> (within the first <paramref name="count" /> entries) is
        ///     set to zero. Iterates the taxonomy once per call and does not allocate; callers own
        ///     and reuse the backing arrays.
        /// </summary>
        /// <param name="labels">Canonical labels to set targets for. Only the first <paramref name="count" /> entries are read.</param>
        /// <param name="scores">Parallel score array (clamped to <c>[0, 1]</c>). Only the first <paramref name="count" /> entries are read.</param>
        /// <param name="count">Number of valid entries in <paramref name="labels" />/<paramref name="scores" />.</param>
        public void SetTargetEmotions(string[] labels, float[] scores, int count)
        {
            string neutral = _taxonomy.Neutral.Label;

            // Defensive clamp: never read past either backing array's actual length, even if a
            // caller passes an oversized/stale count. Zero-alloc; no behavior change for callers
            // that already pass a safe count.
            int safeCount = Mathf.Clamp(count, 0, Mathf.Min(labels?.Length ?? 0, scores?.Length ?? 0));

            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                string label = _taxonomy.Emotions[i].Label;
                float newTarget = 0f;

                if (labels != null && scores != null)
                {
                    for (int j = 0; j < safeCount; j++)
                    {
                        if (string.Equals(labels[j], label, StringComparison.OrdinalIgnoreCase))
                        {
                            newTarget = Mathf.Clamp01(scores[j]);
                            break;
                        }
                    }
                }

                MaybeTriggerBurst(label, newTarget, neutral);
                _previousTargets[label] = newTarget;
                _targetScores[label] = newTarget;
            }
        }

        /// <summary>
        ///     Snaps all three tables to a single emotion immediately (used for previews and
        ///     "lock emotion" overrides).
        /// </summary>
        public void SetImmediateEmotion(string canonicalLabel, float score)
        {
            float clamped = Mathf.Clamp01(score);
            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                string label = _taxonomy.Emotions[i].Label;
                float value = string.Equals(label, canonicalLabel, StringComparison.OrdinalIgnoreCase)
                    ? clamped
                    : 0f;
                _targetScores[label] = value;
                _currentScores[label] = value;
                _outputScores[label] = value;
                _previousTargets[label] = value;
            }
            _burstLabel = null;
            _burstTimeRemaining = 0f;
        }

        /// <summary>Advances one frame of smoothing and burst animation.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (_burstTimeRemaining > 0f)
                _burstTimeRemaining -= deltaTime;

            float burstAlpha = _burstTimeRemaining > 0f
                ? Mathf.Sin((_burstTimeRemaining / _microBurstDuration) * Mathf.PI)
                : 0f;

            float maxCurrentTransient = 0f;
            string dominantTransientLabel = null;

            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                EmotionDescriptor descriptor = _taxonomy.Emotions[i];
                string label = descriptor.Label;
                float target = _targetScores[label];
                float current = _currentScores[label];
                float aSpeed = _attackSpeeds[i];
                float dSpeed = _decaySpeeds[i];
                float alpha = target > 0.001f
                    ? 1f - Mathf.Exp(-aSpeed * deltaTime)
                    : 1f - Mathf.Exp(-dSpeed * deltaTime);
                float next = current + (target - current) * alpha;
                if (next < 0.001f && target <= 0f) next = 0f;
                _currentScores[label] = next;

                if (!descriptor.IsNeutral && next > maxCurrentTransient)
                {
                    maxCurrentTransient = next;
                    dominantTransientLabel = label;
                }

                float output = next;
                if (burstAlpha > 0f && string.Equals(label, _burstLabel, StringComparison.OrdinalIgnoreCase))
                {
                    float overshoot = (_microBurstOvershoot - 1f) * burstAlpha;
                    output = Mathf.Clamp01(next * (1f + overshoot));
                }
                _outputScores[label] = output;
            }

            // Advance the two-slot mood crossfade at the configured rate, then fold each active
            // slot into OutputScores independently (same formula as the original single-slot fold).
            float moodAlpha = 1f - Mathf.Exp(-_moodRate * deltaTime);
            _moodCurrentValue += (_moodCurrentTarget - _moodCurrentValue) * moodAlpha;
            if (Mathf.Abs(_moodCurrentValue - _moodCurrentTarget) < 0.001f) _moodCurrentValue = _moodCurrentTarget;

            _moodOutgoingValue += (0f - _moodOutgoingValue) * moodAlpha;
            if (Mathf.Abs(_moodOutgoingValue) < 0.001f) _moodOutgoingValue = 0f;

            ApplyMoodFold(_moodCurrentLabel, _moodCurrentValue, maxCurrentTransient);
            ApplyMoodFold(_moodOutgoingLabel, _moodOutgoingValue, maxCurrentTransient);

            AdvanceMoodDrift(deltaTime, dominantTransientLabel, maxCurrentTransient);
            AdvanceContagionEcho(deltaTime, maxCurrentTransient);
            AdvanceOutcomeBeat(deltaTime, maxCurrentTransient);
        }

        /// <summary>
        ///     Advances the mood drift channel from the dominant non-neutral transient of this
        ///     same frame (as tracked inline by <see cref="Tick" />, with no extra iteration). A
        ///     no-op — the channel stays inert at <c>(null, 0)</c> — when
        ///     <see cref="ConfigureMoodDrift" /> was never called with <c>enabled: true</c>.
        /// </summary>
        private void AdvanceMoodDrift(float deltaTime, string dominantTransientLabel, float maxCurrentTransient)
        {
            if (!_moodDriftEnabled) return;

            bool dominantSustains = maxCurrentTransient >= DriftActivationThreshold && dominantTransientLabel != null;
            bool sustainingSameLabel = dominantSustains &&
                string.Equals(_driftLabel, dominantTransientLabel, StringComparison.OrdinalIgnoreCase);

            if (sustainingSameLabel)
            {
                float target = Mathf.Min(_moodDriftMaxIntensity, maxCurrentTransient);
                float driftAlpha = 1f - Mathf.Exp(-_moodDriftRate * deltaTime);
                _driftValue += (target - _driftValue) * driftAlpha;
            }
            else
            {
                float recoveryAlpha = 1f - Mathf.Exp(-_moodRecoveryRate * deltaTime);
                _driftValue += (0f - _driftValue) * recoveryAlpha;

                if (_driftValue < 0.01f)
                    _driftLabel = dominantSustains ? dominantTransientLabel : null;
            }

            if (_driftValue < 0.001f) _driftValue = 0f;

            ApplyMoodFold(_driftLabel, _driftValue, maxCurrentTransient);
        }

        /// <summary>
        ///     Advances the mood-pickup echo channel toward whatever
        ///     <see cref="SetContagionTarget" /> last supplied, folding it into
        ///     <see cref="OutputScores" /> exactly like <see cref="AdvanceMoodDrift" /> folds
        ///     drift — same drain-then-reseed rule on a label switch, same fold-suppression by a
        ///     strong own transient. A no-op — the channel stays inert at <c>(null, 0)</c> — when
        ///     <see cref="ConfigureContagion" /> was never called with <c>enabled: true</c>.
        /// </summary>
        private void AdvanceContagionEcho(float deltaTime, float maxCurrentTransient)
        {
            if (!_contagionEnabled) return;

            bool sameLabel = _echoTargetLabel != null &&
                string.Equals(_echoLabel, _echoTargetLabel, StringComparison.OrdinalIgnoreCase);

            if (sameLabel)
            {
                float rate = _echoTargetIntensity >= _echoValue ? ContagionEchoAttackRate : ContagionEchoReleaseRate;
                float alpha = 1f - Mathf.Exp(-rate * deltaTime);
                _echoValue += (_echoTargetIntensity - _echoValue) * alpha;
            }
            else
            {
                float releaseAlpha = 1f - Mathf.Exp(-ContagionEchoReleaseRate * deltaTime);
                _echoValue += (0f - _echoValue) * releaseAlpha;

                if (_echoValue < 0.01f)
                    _echoLabel = _echoTargetLabel;
            }

            if (_echoValue < 0.001f) _echoValue = 0f;

            ApplyMoodFold(_echoLabel, _echoValue, maxCurrentTransient);
        }

        /// <summary>
        ///     Folds a single mood slot's intensity into <see cref="_outputScores" />, discounted
        ///     by the strongest current transient (so a strong incoming emotion suppresses the
        ///     resting mood rather than stacking with it). No-op for an empty/neutral/zero-value
        ///     slot; never allocates.
        /// </summary>
        /// <summary>
        ///     Starts a brief outcome beat: <paramref name="canonicalLabel" /> eases in to
        ///     <paramref name="intensity" /> over <paramref name="transitionSeconds" />, holds for
        ///     <paramref name="holdSeconds" />, then eases back out on its own. The beat is folded
        ///     into <see cref="OutputScores" /> and reported by <see cref="GetMood" /> like every
        ///     other mood channel, but it owns no anchor state — whatever mood was active before
        ///     the beat (authored baseline, drift, or a gameplay <c>SetMood</c>) is still there when
        ///     it expires.
        /// </summary>
        /// <remarks>
        ///     Neutral, empty, unknown (non-taxonomy) or zero-intensity input clears any beat in
        ///     flight rather than starting one, matching <see cref="SetPersonaBaselineTarget" />'s
        ///     "no mood" handling.
        /// </remarks>
        public void SetOutcomeBeat(string canonicalLabel, float intensity, float holdSeconds, float transitionSeconds)
        {
            ResolveMoodTarget(canonicalLabel, intensity, out string targetLabel, out float targetIntensity);

            if (targetLabel == null)
            {
                ClearOutcomeBeat();
                return;
            }

            // Retargeting mid-beat resumes from the current value rather than snapping to 0, so
            // back-to-back action outcomes read as one continuous reaction.
            if (!string.Equals(targetLabel, _beatLabel, StringComparison.OrdinalIgnoreCase))
                _beatValue = 0f;

            _beatLabel = targetLabel;
            _beatTargetIntensity = targetIntensity;
            _beatHoldRemaining = Mathf.Max(0f, holdSeconds);
            _beatRate = 3f / Mathf.Max(0.01f, transitionSeconds);
        }

        /// <summary>Cancels any outcome beat immediately, leaving every other mood channel untouched.</summary>
        public void ClearOutcomeBeat()
        {
            _beatLabel = null;
            _beatValue = 0f;
            _beatTargetIntensity = 0f;
            _beatHoldRemaining = 0f;
        }

        /// <summary>
        ///     Advances the outcome-beat envelope and folds it into <see cref="OutputScores" />
        ///     exactly like <see cref="AdvanceMoodDrift" /> and <see cref="AdvanceContagionEcho" />
        ///     do. With no beat in flight this is a couple of comparisons and no writes.
        /// </summary>
        private void AdvanceOutcomeBeat(float deltaTime, float maxCurrentTransient)
        {
            if (string.IsNullOrEmpty(_beatLabel)) return;

            if (_beatHoldRemaining > 0f)
                _beatHoldRemaining = Mathf.Max(0f, _beatHoldRemaining - deltaTime);

            float target = _beatHoldRemaining > 0f ? _beatTargetIntensity : 0f;
            float alpha = 1f - Mathf.Exp(-_beatRate * deltaTime);
            _beatValue += (target - _beatValue) * alpha;

            if (target <= 0f && _beatValue < 0.001f)
            {
                ClearOutcomeBeat();
                return;
            }

            ApplyMoodFold(_beatLabel, _beatValue, maxCurrentTransient);
        }

        private void ApplyMoodFold(string label, float intensity, float maxCurrentTransient)
        {
            if (intensity <= 0f || string.IsNullOrEmpty(label)) return;
            if (!_outputScores.ContainsKey(label)) return;

            float contribution = intensity * (1f - Mathf.Clamp01(maxCurrentTransient));
            _outputScores[label] = Mathf.Clamp01(Mathf.Max(_outputScores[label], contribution));
        }

        /// <summary>Returns the dominant non-neutral emotion and its current score.</summary>
        public void GetDominant(out string dominantLabel, out float dominantScore)
        {
            dominantLabel = _taxonomy.Neutral.Label;
            dominantScore = 0f;

            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                EmotionDescriptor d = _taxonomy.Emotions[i];
                if (d.IsNeutral) continue;

                float value = _currentScores[d.Label];
                if (value > dominantScore)
                {
                    dominantScore = value;
                    dominantLabel = d.Label;
                }
            }
        }

        /// <summary>
        ///     Zeroes all tables and clears pending bursts. Also clears the mood-drift channel and
        ///     the mood-pickup echo channel (what the conversation and the room did to this
        ///     character dies with the session) while PRESERVING the persona baseline/runtime-mood
        ///     anchor slots — see the
        ///     two-slot crossfade fields above.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _taxonomy.Emotions.Count; i++)
            {
                string label = _taxonomy.Emotions[i].Label;
                _targetScores[label] = 0f;
                _currentScores[label] = 0f;
                _outputScores[label] = 0f;
                _previousTargets[label] = 0f;
            }
            _burstLabel = null;
            _burstTimeRemaining = 0f;
            _driftLabel = null;
            _driftValue = 0f;
            _echoLabel = null;
            _echoValue = 0f;
            _echoTargetLabel = null;
            _echoTargetIntensity = 0f;
            ClearOutcomeBeat();
        }

        private void MaybeTriggerBurst(string label, float newTarget, string neutralLabel)
        {
            if (!_microBurstEnabled) return;
            if (string.Equals(label, neutralLabel, StringComparison.OrdinalIgnoreCase)) return;

            float previous = _previousTargets[label];
            if (newTarget - previous <= _microBurstThreshold) return;

            _burstLabel = label;
            _burstTimeRemaining = _microBurstDuration;
        }
    }
}
