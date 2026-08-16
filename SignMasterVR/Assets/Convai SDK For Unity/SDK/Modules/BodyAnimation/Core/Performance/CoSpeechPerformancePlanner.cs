using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Performance
{
    /// <summary>
    ///     Allocation-free tick-time co-speech planner with event-time transcript parsing.
    ///     It publishes one coherent accent decision so body, gaze, and face do not derive
    ///     competing gestures from the same energy rise.
    /// </summary>
    internal sealed class CoSpeechPerformancePlanner : ICoSpeechPerformanceSource
    {
        private static readonly string[] SecondPerson =
            { "you", "your", "yours" };
        private static readonly string[] FirstPerson =
            { "i", "me", "my", "mine", "we", "our" };
        private static readonly string[] Enumerations =
            { "first", "second", "third", "one", "two", "three" };
        private static readonly string[] Greetings =
            { "hello", "hi", "welcome", "bye", "goodbye" };
        private static readonly string[] Affirmatives =
            { "yes", "sure", "certainly" };
        private static readonly string[] Negatives =
            { "no", "never", "not" };
        private static readonly string[] Uncertain =
            { "maybe", "perhaps", "possibly" };

        private readonly ConvaiBodyAnimationConfig _config;
        private DeterministicEmbodimentRandom _random;
        private CoSpeechPerformanceReading _current;
        private GestureCueKind _pendingSemanticKind;
        private float _pendingConfidence;
        private int _generation;
        private int _gestureSequence;
        private bool _wasSpeaking;
        private float _speakingElapsed;
        private float _previousEnergy;
        private float _baseline;
        private float _accentCooldown;
        private CoSpeechQualityTier _qualityTier;

        public CoSpeechPerformancePlanner(ConvaiBodyAnimationConfig config, int seed)
        {
            _config = config;
            _random = new DeterministicEmbodimentRandom(unchecked((uint)(seed ^ 0xC05F33C7)));
        }

        public CoSpeechPerformanceReading Current => _current;

        public void NotifyTranscript(string text, bool isFinal)
        {
            if (!isFinal || string.IsNullOrWhiteSpace(text)) return;

            GestureCueKind kind = Classify(text);
            if (kind == GestureCueKind.None) return;
            _pendingSemanticKind = kind;
            _pendingConfidence = 0.9f;
            _qualityTier = CoSpeechQualityTier.Transcript;
        }

        public void NotifyGestureCue(GestureCueKind kind, float confidence = 1f)
        {
            if (kind == GestureCueKind.None) return;
            _pendingSemanticKind = kind;
            _pendingConfidence = Mathf.Clamp01(confidence);
            _qualityTier = CoSpeechQualityTier.Transcript;
        }

        public void Tick(
            DialogueState state,
            float speechEnergy,
            float deltaTime,
            bool hasWorldTarget,
            Vector3 worldTarget)
        {
            float dt = Mathf.Max(0f, deltaTime);
            float energy = Mathf.Clamp01(speechEnergy);
            bool speaking = state == DialogueState.Speaking;

            if (speaking && !_wasSpeaking)
            {
                _generation++;
                _speakingElapsed = 0f;
                _baseline = 0f;
                _previousEnergy = energy;
            }
            else if (!speaking && _wasSpeaking)
            {
                _generation++;
                _pendingSemanticKind = GestureCueKind.None;
                _pendingConfidence = 0f;
            }

            _wasSpeaking = speaking;
            if (_accentCooldown > 0f) _accentCooldown = Mathf.Max(0f, _accentCooldown - dt);

            CoSpeechPhrasePhase phase;
            if (state == DialogueState.Interrupted)
            {
                phase = CoSpeechPhrasePhase.Interrupted;
                _pendingSemanticKind = GestureCueKind.None;
                _generation++;
            }
            else if (!speaking)
            {
                phase = _speakingElapsed > 0f ? CoSpeechPhrasePhase.Releasing : CoSpeechPhrasePhase.None;
            }
            else
            {
                _speakingElapsed += dt;
                float baselineAlpha = 1f - Mathf.Exp(-dt / 1.6f);
                _baseline = Mathf.Lerp(_baseline, energy, baselineAlpha);
                float activeThreshold = Mathf.Max(0.06f, _baseline + _config.CoSpeechPhraseEnergyMargin);
                bool active = energy >= activeThreshold;
                phase = _speakingElapsed < _config.CoSpeechPreparationSeconds
                    ? CoSpeechPhrasePhase.Preparing
                    : active ? CoSpeechPhrasePhase.Speaking : CoSpeechPhrasePhase.Gap;

                if (_accentCooldown <= 0f)
                {
                    if (_pendingSemanticKind != GestureCueKind.None)
                    {
                        // Always published, independent of EnableAdvancedCoSpeech. A pending
                        // semantic cue is something the character demonstrably meant — it was
                        // either classified from its own final transcript or handed over by the
                        // referential-gesture director because the animation set authors no clip
                        // for it. Gating this behind the advanced switch is what used to make
                        // "Enable Referential Gestures" a no-op on every set without referential
                        // content: the cue was raised, nothing was allowed to carry it, and the
                        // character stood still. Publishing it lets any registered peer performer
                        // (today: Convai Body Language's procedural arm solver) do it instead.
                        PublishGesture(
                            _pendingSemanticKind,
                            Mathf.Lerp(0.65f, 1f, energy),
                            _pendingConfidence,
                            hasWorldTarget,
                            worldTarget);
                        _pendingSemanticKind = GestureCueKind.None;
                    }
                    else if (_config.EnableAdvancedCoSpeech)
                    {
                        // Speculative accents derived from the energy envelope alone — no
                        // semantic evidence behind them — stay opt-in.
                        float derivative = dt > 0f ? (energy - _previousEnergy) / dt : 0f;
                        if (energy >= _config.CoSpeechMinimumAccentEnergy &&
                            derivative >= _config.CoSpeechEmphasisDerivative &&
                            _random.Value <= _config.CoSpeechAccentProbability)
                        {
                            GestureCueKind kind = energy > 0.72f ? GestureCueKind.Emphatic : GestureCueKind.Beat;
                            PublishGesture(kind, Mathf.InverseLerp(_config.CoSpeechMinimumAccentEnergy, 1f, energy),
                                0.65f, false, default);
                        }
                    }
                }
            }

            _previousEnergy = energy;
            CoSpeechGestureRequest gesture = _current.Gesture;
            float phraseProgress = speaking ? Mathf.Repeat(_speakingElapsed / 2.4f, 1f) : 0f;
            _current = new CoSpeechPerformanceReading(
                _generation,
                speaking,
                energy,
                phraseProgress,
                phase,
                _qualityTier,
                in gesture);
        }

        public void Reset()
        {
            _generation++;
            _pendingSemanticKind = GestureCueKind.None;
            _pendingConfidence = 0f;
            _gestureSequence = 0;
            _wasSpeaking = false;
            _speakingElapsed = 0f;
            _previousEnergy = 0f;
            _baseline = 0f;
            _accentCooldown = 0f;
            _qualityTier = CoSpeechQualityTier.EnergyOnly;
            _current = CoSpeechPerformanceReading.None;
        }

        private void PublishGesture(
            GestureCueKind kind,
            float intensity,
            float confidence,
            bool hasWorldTarget,
            Vector3 worldTarget)
        {
            _gestureSequence++;
            CoSpeechHandedness handedness = ResolveHandedness(kind);
            var request = new CoSpeechGestureRequest(
                _gestureSequence,
                kind,
                intensity,
                confidence,
                _config.CoSpeechPreparationSeconds,
                _config.CoSpeechStrokeSeconds,
                kind == GestureCueKind.IndicateObject ? _config.CoSpeechReferentialHoldSeconds : 0.08f,
                _config.CoSpeechRetractionSeconds,
                handedness,
                hasWorldTarget && kind == GestureCueKind.IndicateObject,
                worldTarget);
            _current = new CoSpeechPerformanceReading(
                _generation, true, _previousEnergy, 0f, CoSpeechPhrasePhase.Speaking,
                confidence > 0.8f ? CoSpeechQualityTier.Transcript : CoSpeechQualityTier.EnergyOnly,
                in request);
            _accentCooldown = _config.CoSpeechAccentRefractorySeconds;
        }

        private CoSpeechHandedness ResolveHandedness(GestureCueKind kind)
        {
            if (kind is GestureCueKind.Uncertain or GestureCueKind.Negative)
                return CoSpeechHandedness.Bilateral;
            return _random.Value < 0.5f ? CoSpeechHandedness.Left : CoSpeechHandedness.Right;
        }

        private static GestureCueKind Classify(string text)
        {
            List<string> words = Tokenize(text);
            if (ContainsAny(words, Greetings)) return GestureCueKind.Greeting;
            if (ContainsAny(words, Negatives)) return GestureCueKind.Negative;
            if (ContainsAny(words, Affirmatives)) return GestureCueKind.Affirmative;
            if (ContainsAny(words, Uncertain)) return GestureCueKind.Uncertain;
            if (ContainsAny(words, Enumerations)) return GestureCueKind.Enumerate;
            if (ContainsAny(words, SecondPerson)) return GestureCueKind.PalmToPlayer;
            if (ContainsAny(words, FirstPerson)) return GestureCueKind.HandToChest;
            return GestureCueKind.None;
        }

        private static List<string> Tokenize(string text)
        {
            var words = new List<string>(16);
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                bool letterOrDigit = char.IsLetterOrDigit(text[i]);
                if (letterOrDigit && start < 0) start = i;
                else if (!letterOrDigit && start >= 0)
                {
                    words.Add(text.Substring(start, i - start).ToLowerInvariant());
                    start = -1;
                }
            }
            if (start >= 0) words.Add(text.Substring(start).ToLowerInvariant());
            return words;
        }

        private static bool ContainsAny(List<string> words, string[] candidates)
        {
            for (int i = 0; i < words.Count; i++)
                for (int j = 0; j < candidates.Length; j++)
                    if (string.Equals(words[i], candidates[j], StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
