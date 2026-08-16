namespace Convai.Runtime
{
    /// <summary>
    ///     Convai service environment preset. Drives the REST API base URL;
    ///     the realtime core server URL is only overridable via <see cref="Custom" />.
    /// </summary>
    public enum ConvaiApiEnvironment
    {
        /// <summary>Production environment (api.convai.com / live.convai.com).</summary>
        Production,

        /// <summary>Beta/staging environment (beta.convai.com).</summary>
        Beta,

        /// <summary>Custom endpoints. Unlocks the raw REST base and core server URL fields.</summary>
        Custom
    }
}
