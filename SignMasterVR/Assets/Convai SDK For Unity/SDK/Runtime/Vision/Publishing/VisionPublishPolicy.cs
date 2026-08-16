namespace Convai.Runtime.Vision.Publishing
{
    /// <summary>
    ///     Describes how aggressively the SDK should publish visual context.
    /// </summary>
    public enum VisionPublishPolicy
    {
        /// <summary>
        ///     Continuous publish using a balanced transport budget that remains compatible with the current backend.
        /// </summary>
        AutoCompatible = 0,

        /// <summary>
        ///     Continuous publish tuned for responsiveness and higher visual fidelity.
        /// </summary>
        HighResponsiveness = 1,

        /// <summary>
        ///     Continuous publish tuned for lower CPU/GPU/network overhead.
        /// </summary>
        LowOverhead = 2,

        /// <summary>
        ///     Do not auto-publish on room connect. Publishing starts only after EnablePublishing(true).
        /// </summary>
        Manual = 3
    }
}
