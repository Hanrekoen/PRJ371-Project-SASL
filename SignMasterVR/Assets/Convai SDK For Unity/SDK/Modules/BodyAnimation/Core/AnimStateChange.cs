namespace Convai.Modules.BodyAnimation
{
    /// <summary>
    ///     One animation transition as reported by the body animation system: which layer
    ///     moved, from/to which state, into which clip, how long the fade is, and why.
    ///     Mirrors exactly what the trace log prints, so subscribers and log readers see the
    ///     same story.
    /// </summary>
    public readonly struct AnimStateChange
    {
        /// <summary>Layer that transitioned (e.g. "Locomotion", "Talk", "Action").</summary>
        public string Layer { get; }

        /// <summary>State label before the transition.</summary>
        public string From { get; }

        /// <summary>State label after the transition.</summary>
        public string To { get; }

        /// <summary>Clip the transition landed on (may be empty for pure weight fades).</summary>
        public string Clip { get; }

        /// <summary>Crossfade duration in seconds.</summary>
        public float FadeSeconds { get; }

        /// <summary>Human-readable trigger, e.g. "speaking started", "yaw error 142°".</summary>
        public string Reason { get; }

        public AnimStateChange(
            string layer, string from, string to, string clip, float fadeSeconds, string reason)
        {
            Layer = layer ?? string.Empty;
            From = from ?? string.Empty;
            To = to ?? string.Empty;
            Clip = clip ?? string.Empty;
            FadeSeconds = fadeSeconds;
            Reason = reason ?? string.Empty;
        }

        public override string ToString() =>
            $"[{Layer}] {From} -> {To} clip='{Clip}' fade={FadeSeconds:F2}s ({Reason})";
    }
}
