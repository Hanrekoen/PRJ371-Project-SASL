#nullable enable
using System;
using System.Reflection;
using Convai.RestAPI.Transport;

namespace Convai.RestAPI
{
    /// <summary>
    /// The environment to use for API requests.
    /// </summary>
    public enum ConvaiEnvironment
    {
        /// <summary>
        /// Production environment.
        /// </summary>
        Production,

        /// <summary>
        /// Beta/staging environment.
        /// </summary>
        Beta
    }

    /// <summary>
    /// The credential type used to authenticate Convai API requests.
    /// </summary>
    public enum ConvaiAuthenticationMode
    {
        /// <summary>Authenticate with an account API key.</summary>
        ApiKey,

        /// <summary>Authenticate with a short-lived token minted by the user connect endpoint.</summary>
        AuthToken
    }

    /// <summary>
    /// Configuration options for the Convai REST client.
    /// </summary>
    public sealed class ConvaiRestClientOptions
    {
        /// <summary>
        /// The credential value used for authentication. This is an API key by default, or an auth token when
        /// <see cref="AuthenticationMode" /> is <see cref="ConvaiAuthenticationMode.AuthToken" />.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        /// The authentication scheme used for requests. Defaults to API-key authentication.
        /// </summary>
        public ConvaiAuthenticationMode AuthenticationMode { get; set; } = ConvaiAuthenticationMode.ApiKey;

        /// <summary>
        /// The environment to use. Defaults to Production.
        /// </summary>
        public ConvaiEnvironment Environment { get; set; } = ConvaiEnvironment.Production;

        /// <summary>
        /// Default timeout for requests. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Custom HTTP transport. If null, the appropriate transport for the platform is used.
        /// </summary>
        public IConvaiHttpTransport? CustomTransport { get; set; }

        /// <summary>
        /// Base URL for the production API.
        /// </summary>
        public string ProductionBaseUrl { get; set; } = "https://api.convai.com/";

        /// <summary>
        /// Base URL for the beta API.
        /// </summary>
        public string BetaBaseUrl { get; set; } = "https://beta.convai.com/";

        /// <summary>
        /// Source value used for room connect invocation metadata.
        /// </summary>
        public string InvocationSource { get; set; } = "unity_sdk";

        /// <summary>
        /// Client version used for room connect invocation metadata.
        /// </summary>
        public string ClientVersion { get; set; } = ResolveDefaultClientVersion();

        /// <summary>
        /// Creates new client options with the specified API key.
        /// </summary>
        public ConvaiRestClientOptions(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));

            ApiKey = apiKey;
        }

        /// <summary>
        /// Gets the base URL for the current environment.
        /// </summary>
        internal string GetBaseUrl()
        {
            return Environment == ConvaiEnvironment.Production ? ProductionBaseUrl : BetaBaseUrl;
        }

        private static string ResolveDefaultClientVersion()
        {
            Type? sdkType = Type.GetType("Convai.Application.ConvaiSDK, Convai.Runtime");
            FieldInfo? versionField = sdkType?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            return versionField?.GetValue(null)?.ToString() ?? "0.1.0";
        }
    }
}
