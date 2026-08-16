using Convai.Modules.BodyLanguage.Data;

namespace Convai.Modules.BodyLanguage.Core.Diagnostics
{
    /// <summary>
    ///     One recorded body language trace line. The ring buffer of these lets HUDs and tests
    ///     replay the most recent policy decisions without parsing the console.
    /// </summary>
    public readonly struct BodyLanguageTraceEntry
    {
        /// <summary>Value of <c>Time.time</c> when the entry was recorded (0 outside play mode).</summary>
        public float Time { get; }

        /// <summary>Verbosity level the entry was recorded at.</summary>
        public BodyLanguageTraceVerbosity Level { get; }

        /// <summary>Fully formatted message, without the owner prefix.</summary>
        public string Message { get; }

        public BodyLanguageTraceEntry(float time, BodyLanguageTraceVerbosity level, string message)
        {
            Time = time;
            Level = level;
            Message = message;
        }
    }
}
