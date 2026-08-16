namespace Convai.Modules.BodyAnimation.Core.Diagnostics
{
    /// <summary>
    ///     Single recorded trace line kept in the in-memory ring buffer so HUDs and tests can
    ///     replay the most recent body animation decisions without parsing the console.
    /// </summary>
    public readonly struct AnimTraceEntry
    {
        /// <summary>Value of <c>Time.time</c> when the entry was recorded (0 outside play mode).</summary>
        public float Time { get; }

        /// <summary>Verbosity level the entry was recorded at.</summary>
        public AnimTraceVerbosity Level { get; }

        /// <summary>Fully formatted message, without the owner prefix.</summary>
        public string Message { get; }

        public AnimTraceEntry(float time, AnimTraceVerbosity level, string message)
        {
            Time = time;
            Level = level;
            Message = message ?? string.Empty;
        }

        public override string ToString() => $"[{Time:F2}] {Message}";
    }
}
