using Convai.Domain.Embodiment.Semantics;

namespace Convai.Modules.Gaze.Core.Diagnostics
{
    /// <summary>
    ///     Event payload describing one gaze target transition, mirroring the trace log so
    ///     game code can react to re-targets without string parsing.
    /// </summary>
    public readonly struct GazeTargetChange
    {
        /// <summary>Kind of the previous target.</summary>
        public GazeTargetKind FromKind { get; }

        /// <summary>Kind of the new target.</summary>
        public GazeTargetKind ToKind { get; }

        /// <summary>Display name of the previous target ("-" when none).</summary>
        public string FromName { get; }

        /// <summary>Display name of the new target ("-" when none).</summary>
        public string ToName { get; }

        /// <summary>Human-readable reason for the change (arbiter decision, release, loss…).</summary>
        public string Reason { get; }

        /// <summary>Value of <c>Time.time</c> when the change happened.</summary>
        public float Time { get; }

        public GazeTargetChange(
            GazeTargetKind fromKind,
            GazeTargetKind toKind,
            string fromName,
            string toName,
            string reason,
            float time)
        {
            FromKind = fromKind;
            ToKind = toKind;
            FromName = string.IsNullOrEmpty(fromName) ? "-" : fromName;
            ToName = string.IsNullOrEmpty(toName) ? "-" : toName;
            Reason = reason ?? string.Empty;
            Time = time;
        }
    }
}
