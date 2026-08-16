using Convai.Modules.Gaze.Data;

namespace Convai.Modules.Gaze.Core.Diagnostics
{
    /// <summary>
    ///     One recorded gaze trace line. The ring buffer of these lets HUDs and tests replay
    ///     the most recent gaze decisions without parsing the console.
    /// </summary>
    public readonly struct GazeTraceEntry
    {
        /// <summary>Value of <c>Time.time</c> when the entry was recorded (0 outside play mode).</summary>
        public float Time { get; }

        /// <summary>Verbosity level the entry was recorded at.</summary>
        public GazeTraceVerbosity Level { get; }

        /// <summary>Fully formatted message, without the owner prefix.</summary>
        public string Message { get; }

        public GazeTraceEntry(float time, GazeTraceVerbosity level, string message)
        {
            Time = time;
            Level = level;
            Message = message;
        }
    }
}
