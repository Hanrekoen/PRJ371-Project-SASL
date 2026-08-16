using Convai.Domain.Logging;
using Convai.Shared.Types;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Immutable snapshot of bootstrap configuration.
    ///     Captured at startup; cannot be changed at runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This class captures all configuration that must be known before the SDK initializes.
    ///         Once created, the values cannot be modified - this ensures consistent behavior
    ///         throughout the application lifecycle.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>: Created once during SDK initialization from <see cref="ConvaiSettings" />
    ///         or equivalent configuration sources. Injected into services that need bootstrap config.
    ///     </para>
    /// </remarks>
    public sealed class ConvaiBootstrapConfigSnapshot
    {
        /// <summary>
        ///     Creates a new bootstrap configuration snapshot.
        /// </summary>
        public ConvaiBootstrapConfigSnapshot(
            string apiKey,
            string serverUrl,
            ConvaiConnectionType connectionType = ConvaiConnectionType.Audio,
            ConvaiServerEndpoint serverEndpoint = ConvaiServerEndpoint.Connect,
            float connectionTimeoutSeconds = 30f,
            LogLevel globalLogLevel = LogLevel.Info,
            bool enableSessionResume = true,
            int maxRetryAttempts = 3)
        {
            ApiKey = apiKey ?? string.Empty;
            ServerUrl = serverUrl ?? string.Empty;
            ConnectionType = connectionType;
            ServerEndpoint = serverEndpoint;
            ConnectionTimeoutSeconds = connectionTimeoutSeconds;
            GlobalLogLevel = globalLogLevel;
            EnableSessionResume = enableSessionResume;
            MaxRetryAttempts = maxRetryAttempts;
        }

        /// <summary>
        ///     The Convai API key for authentication.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        ///     The base server URL for API connections.
        /// </summary>
        public string ServerUrl { get; }

        /// <summary>
        ///     The connection type (Audio or Video).
        /// </summary>
        public ConvaiConnectionType ConnectionType { get; }

        /// <summary>
        ///     The server endpoint configuration.
        /// </summary>
        public ConvaiServerEndpoint ServerEndpoint { get; }

        /// <summary>
        ///     Connection timeout in seconds.
        /// </summary>
        public float ConnectionTimeoutSeconds { get; }

        /// <summary>
        ///     Global log level for SDK logging.
        /// </summary>
        public LogLevel GlobalLogLevel { get; }

        /// <summary>
        ///     Whether session resume is enabled globally.
        /// </summary>
        public bool EnableSessionResume { get; }

        /// <summary>
        ///     Maximum retry attempts for connection failures.
        /// </summary>
        public int MaxRetryAttempts { get; }

        /// <summary>
        ///     Whether the configuration is valid for establishing connections.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ServerUrl);

        /// <summary>
        ///     Creates an empty/invalid configuration snapshot.
        /// </summary>
        public static ConvaiBootstrapConfigSnapshot Empty => new(string.Empty, string.Empty);

        /// <summary>
        ///     Returns a human-readable summary of the configuration.
        /// </summary>
        public override string ToString()
        {
            return "ConvaiBootstrapConfigSnapshot { " +
                   $"ServerUrl = {ServerUrl}, " +
                   $"ConnectionType = {ConnectionType}, " +
                   $"ServerEndpoint = {ServerEndpoint}, " +
                   $"LogLevel = {GlobalLogLevel}, " +
                   $"IsValid = {IsValid} }}";
        }
    }
}
