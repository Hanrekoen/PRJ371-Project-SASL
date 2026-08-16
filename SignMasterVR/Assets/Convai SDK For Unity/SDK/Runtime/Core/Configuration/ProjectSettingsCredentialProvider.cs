namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Credential provider that reads from ConvaiSettings (project settings).
    /// </summary>
    /// <remarks>
    ///     This is the default credential provider for development builds.
    ///     It reads API key and server URL from Unity's Project Settings.
    /// </remarks>
    internal sealed class ProjectSettingsCredentialProvider : ICredentialProvider
    {
        private ConvaiSettings _settings;

        /// <summary>
        ///     Creates a new ProjectSettingsCredentialProvider.
        /// </summary>
        /// <param name="settings">Optional settings instance. Uses ConvaiSettings.Instance if null.</param>
        public ProjectSettingsCredentialProvider(ConvaiSettings settings = null)
        {
            _settings = settings ?? ConvaiSettings.Instance;
        }

        /// <inheritdoc />
        public bool HasValidCredentials
        {
            get
            {
                string apiKey = _settings?.ApiKey;
                return !string.IsNullOrWhiteSpace(apiKey);
            }
        }

        /// <inheritdoc />
        public string GetApiKey() => _settings?.ApiKey;

        /// <inheritdoc />
        public string GetServerUrl() => _settings?.ServerUrl;

        /// <inheritdoc />
        public void Refresh()
        {
            // Re-read from singleton in case settings changed
            _settings = ConvaiSettings.Instance;
        }
    }
}
