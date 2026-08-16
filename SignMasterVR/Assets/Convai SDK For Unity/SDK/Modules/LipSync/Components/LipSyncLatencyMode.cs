namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Preset strategies for balancing startup latency and stream resilience.
    /// </summary>
    public enum LipSyncLatencyMode
    {
        /// <summary>Recommended default for most network conditions.</summary>
        Balanced,

        /// <summary>Lower buffering for minimal delay, more prone to starvation on jittery links.</summary>
        UltraLowLatency,

        /// <summary>Higher buffering for unstable networks at the cost of additional delay.</summary>
        NetworkSafe,

        /// <summary>Leaves latency fields under manual user control.</summary>
        Custom
    }
}
