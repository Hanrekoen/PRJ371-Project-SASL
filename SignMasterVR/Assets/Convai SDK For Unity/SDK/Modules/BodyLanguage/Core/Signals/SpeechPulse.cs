namespace Convai.Modules.BodyLanguage.Core.Signals
{
    /// <summary>
    ///     The kind of discrete conversational event a <see cref="SpeechPulseAnalyzer" /> can
    ///     emit from a continuous speech-energy stream.
    /// </summary>
    internal enum SpeechPulseKind
    {
        /// <summary>No pulse fired this step.</summary>
        None = 0,

        /// <summary>The smoothed envelope crossed from inactive to active.</summary>
        Onset,

        /// <summary>A fast positive envelope derivative spike while active (a stressed syllable, a raised voice).</summary>
        Emphasis,

        /// <summary>A low-rate heartbeat emitted while the envelope stays continuously active.</summary>
        Sustain,

        /// <summary>The envelope dropped back below the hysteresis threshold after activity.</summary>
        Release
    }

    /// <summary>
    ///     A single discrete pulse produced by <see cref="SpeechPulseAnalyzer.Step" />. Carries
    ///     enough information for downstream directors (head-beats, posture pulses) to react
    ///     without re-deriving envelope state.
    /// </summary>
    internal readonly struct SpeechPulse
    {
        /// <summary>Creates a pulse record.</summary>
        public SpeechPulse(SpeechPulseKind kind, float strength, float time)
        {
            Kind = kind;
            Strength = strength;
            Time = time;
        }

        /// <summary>The kind of conversational event this pulse represents.</summary>
        public SpeechPulseKind Kind { get; }

        /// <summary>Normalized 0..1 strength of the pulse (louder/faster envelope changes read stronger).</summary>
        public float Strength { get; }

        /// <summary>The analyzer's internal accumulated time (seconds) at which the pulse fired.</summary>
        public float Time { get; }
    }
}
