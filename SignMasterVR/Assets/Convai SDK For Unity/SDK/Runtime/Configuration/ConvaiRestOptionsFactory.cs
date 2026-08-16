using Convai.RestAPI;

namespace Convai.Runtime
{
    /// <summary>
    ///     Creates <see cref="ConvaiRestClientOptions" /> honoring the project-wide
    ///     <see cref="ConvaiApiEnvironment" /> preset from <see cref="ConvaiSettings" />.
    ///     Use this instead of constructing options directly so the Beta/Custom presets apply.
    /// </summary>
    public static class ConvaiRestOptionsFactory
    {
        /// <summary>
        ///     Creates options for the given key (or the configured key when null),
        ///     using the environment configured in <see cref="ConvaiSettings" />.
        /// </summary>
        public static ConvaiRestClientOptions Create(string apiKey = null)
        {
            ConvaiSettings settings = ConvaiSettings.Instance;
            return Create(
                apiKey ?? settings?.ApiKey,
                settings?.ApiEnvironment ?? ConvaiApiEnvironment.Production,
                settings?.RestBaseUrlOverride);
        }

        /// <summary>
        ///     Creates options for an explicit key/environment combination, e.g. to validate
        ///     credentials that are not saved to <see cref="ConvaiSettings" /> yet.
        /// </summary>
        public static ConvaiRestClientOptions Create(string apiKey, ConvaiApiEnvironment environment,
            string customRestBaseUrl)
        {
            var options = new ConvaiRestClientOptions(apiKey);

            switch (environment)
            {
                case ConvaiApiEnvironment.Beta:
                    options.Environment = ConvaiEnvironment.Beta;
                    break;
                case ConvaiApiEnvironment.Custom:
                    options.Environment = ConvaiEnvironment.Production;
                    if (!string.IsNullOrWhiteSpace(customRestBaseUrl))
                        options.ProductionBaseUrl = customRestBaseUrl.Trim();
                    break;
                default:
                    options.Environment = ConvaiEnvironment.Production;
                    break;
            }

            return options;
        }

        /// <summary>
        ///     Creates options for a resolved runtime credential and selects the corresponding wire-level
        ///     authentication header.
        /// </summary>
        internal static ConvaiRestClientOptions CreateForRuntimeCredential(string credential, bool usesAuthToken)
        {
            ConvaiRestClientOptions options = Create(credential);
            options.AuthenticationMode = usesAuthToken
                ? ConvaiAuthenticationMode.AuthToken
                : ConvaiAuthenticationMode.ApiKey;
            return options;
        }
    }
}
