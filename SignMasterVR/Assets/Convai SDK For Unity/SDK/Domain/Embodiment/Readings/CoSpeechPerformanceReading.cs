using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>Quality of the evidence used to build a co-speech performance plan.</summary>
    public enum CoSpeechQualityTier
    {
        EnergyOnly = 0,
        Transcript = 1,
        TimedTranscript = 2
    }

    /// <summary>Current phrase-level phase of a speaking turn.</summary>
    public enum CoSpeechPhrasePhase
    {
        None = 0,
        Preparing = 1,
        Speaking = 2,
        Gap = 3,
        Releasing = 4,
        Interrupted = 5
    }

    /// <summary>Preferred arm-selection policy for a gesture request.</summary>
    public enum CoSpeechHandedness
    {
        Automatic = 0,
        Left = 1,
        Right = 2,
        Bilateral = 3
    }

    /// <summary>
    ///     Immutable discrete gesture request. Times are relative to the request publication;
    ///     they describe preparation, stroke, hold, and retraction instead of prescribing bone motion.
    /// </summary>
    public readonly struct CoSpeechGestureRequest
    {
        public int Sequence { get; }
        public GestureCueKind Kind { get; }
        public float Intensity { get; }
        public float Confidence { get; }
        public float PreparationSeconds { get; }
        public float StrokeSeconds { get; }
        public float HoldSeconds { get; }
        public float RetractionSeconds { get; }
        public CoSpeechHandedness Handedness { get; }
        public bool HasWorldTarget { get; }
        public Vector3 WorldTarget { get; }

        public CoSpeechGestureRequest(
            int sequence,
            GestureCueKind kind,
            float intensity,
            float confidence,
            float preparationSeconds,
            float strokeSeconds,
            float holdSeconds,
            float retractionSeconds,
            CoSpeechHandedness handedness = CoSpeechHandedness.Automatic,
            bool hasWorldTarget = false,
            Vector3 worldTarget = default)
        {
            Sequence = sequence;
            Kind = kind;
            Intensity = Mathf.Clamp01(intensity);
            Confidence = Mathf.Clamp01(confidence);
            PreparationSeconds = Mathf.Max(0.01f, preparationSeconds);
            StrokeSeconds = Mathf.Max(0.01f, strokeSeconds);
            HoldSeconds = Mathf.Max(0f, holdSeconds);
            RetractionSeconds = Mathf.Max(0.01f, retractionSeconds);
            Handedness = handedness;
            HasWorldTarget = hasWorldTarget;
            WorldTarget = worldTarget;
        }

        public static CoSpeechGestureRequest None => default;
    }

    /// <summary>One allocation-free snapshot of the current speaking performance.</summary>
    public readonly struct CoSpeechPerformanceReading
    {
        public int GenerationId { get; }
        public int GestureSequence { get; }
        public bool IsSpeaking { get; }
        public float SpeechEnergy { get; }
        public float PhraseProgress { get; }
        public CoSpeechPhrasePhase PhrasePhase { get; }
        public CoSpeechQualityTier QualityTier { get; }
        public CoSpeechGestureRequest Gesture { get; }

        public bool HasGesture => GestureSequence > 0 && Gesture.Kind != GestureCueKind.None;

        public CoSpeechPerformanceReading(
            int generationId,
            bool isSpeaking,
            float speechEnergy,
            float phraseProgress,
            CoSpeechPhrasePhase phrasePhase,
            CoSpeechQualityTier qualityTier,
            in CoSpeechGestureRequest gesture)
        {
            GenerationId = generationId;
            GestureSequence = gesture.Sequence;
            IsSpeaking = isSpeaking;
            SpeechEnergy = Mathf.Clamp01(speechEnergy);
            PhraseProgress = Mathf.Clamp01(phraseProgress);
            PhrasePhase = phrasePhase;
            QualityTier = qualityTier;
            Gesture = gesture;
        }

        public static CoSpeechPerformanceReading None => default;
    }
}
