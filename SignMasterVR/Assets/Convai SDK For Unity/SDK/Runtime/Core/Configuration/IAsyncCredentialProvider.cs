using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Additive credential-provider contract for credentials that must be resolved asynchronously.
    /// </summary>
    /// <remarks>
    ///     <see cref="ICredentialProvider" /> remains unchanged for source and binary compatibility.
    ///     The room connect path invokes this contract, when present, before reading the credential
    ///     through <see cref="ICredentialProvider.GetApiKey" />.
    /// </remarks>
    public interface IAsyncCredentialProvider
    {
        /// <summary>Resolves fresh credentials for the pending connection attempt.</summary>
        public Task EnsureCredentialsAsync(CancellationToken cancellationToken);
    }

    /// <summary>Internal diagnostics for a provider whose configuration is currently invalid.</summary>
    internal interface ICredentialConfigurationStatus
    {
        public string ConfigurationErrorCode { get; }
        public string ConfigurationErrorMessage { get; }
    }

    /// <summary>Internal diagnostics for the most recent asynchronous resolution attempt.</summary>
    internal interface IAsyncCredentialResolutionStatus
    {
        public string CredentialResolutionErrorMessage { get; }
    }

    /// <summary>
    ///     Internal one-shot channel used by the explicit auth-token connect path.
    /// </summary>
    /// <remarks>
    ///     The token is supplied immediately before credential resolution and must be consumed by the next
    ///     connection attempt. Keeping this contract internal prevents credentials from becoming serialized
    ///     project configuration.
    /// </remarks>
    internal interface IExplicitAuthTokenCredentialProvider
    {
        public void SetAuthTokenForNextConnection(string authToken);
    }
}
