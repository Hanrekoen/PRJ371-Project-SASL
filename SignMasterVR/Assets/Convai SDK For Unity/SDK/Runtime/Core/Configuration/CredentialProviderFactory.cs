namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Creates the default runtime credential provider selected by project settings.</summary>
    internal static class CredentialProviderFactory
    {
        /// <summary>Creates a credential provider for the configured authentication mode.</summary>
        /// <param name="settings">Optional settings source. Null uses <see cref="ConvaiSettings.Instance" />.</param>
        internal static ICredentialProvider Create(ConvaiSettings settings = null)
        {
            ConvaiSettings resolvedSettings = settings ?? ConvaiSettings.Instance;
            return resolvedSettings?.AuthMode == ConvaiAuthMode.AuthToken
                ? new AuthTokenCredentialProvider(resolvedSettings)
                : new ProjectSettingsCredentialProvider(resolvedSettings);
        }
    }
}
