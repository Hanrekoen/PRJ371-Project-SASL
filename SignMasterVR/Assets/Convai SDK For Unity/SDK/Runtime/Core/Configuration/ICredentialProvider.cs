namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Provider for secure access to API credentials.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface abstracts credential storage and retrieval, allowing different
    ///         implementations for different security requirements:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Project settings (development)</description>
    ///         </item>
    ///         <item>
    ///             <description>Environment variables (CI/CD)</description>
    ///         </item>
    ///         <item>
    ///             <description>Secure storage (production)</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Security Note</b>: Implementations should never log or expose the API key.
    ///     </para>
    /// </remarks>
    public interface ICredentialProvider
    {
        /// <summary>
        ///     Whether valid credentials are currently configured.
        /// </summary>
        public bool HasValidCredentials { get; }

        /// <summary>
        ///     Gets the API key for Convai services.
        /// </summary>
        /// <returns>The API key, or null if not configured.</returns>
        public string GetApiKey();

        /// <summary>
        ///     Gets the server URL for API connections.
        /// </summary>
        /// <returns>The server URL, or null if not configured.</returns>
        public string GetServerUrl();

        /// <summary>
        ///     Refreshes credentials from the underlying source.
        /// </summary>
        /// <remarks>
        ///     Called when the configuration is refreshed or when credentials may have changed.
        /// </remarks>
        public void Refresh();
    }
}
