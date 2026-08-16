using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Resolves a short-lived credential suitable for a Convai runtime connection.</summary>
    /// <remarks>
    ///     Implementations must not log or persist returned tokens. A provider is invoked once for every new room
    ///     connection attempt; cross-connection caching and token refresh are intentionally outside this contract.
    /// </remarks>
    public interface IConvaiAuthTokenProvider
    {
        /// <summary>Resolves a fresh auth token.</summary>
        /// <param name="cancellationToken">Cancels the pending caller operation.</param>
        /// <returns>The resolved token result.</returns>
        public Task<AuthTokenResult> GetTokenAsync(CancellationToken cancellationToken);
    }
}
