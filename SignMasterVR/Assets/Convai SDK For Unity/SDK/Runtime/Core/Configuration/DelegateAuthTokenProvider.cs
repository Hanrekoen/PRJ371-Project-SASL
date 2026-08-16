using System;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Adapts a developer-supplied asynchronous delegate into an auth-token provider.</summary>
    public sealed class DelegateAuthTokenProvider : IConvaiAuthTokenProvider
    {
        private readonly Func<CancellationToken, Task<string>> _getTokenAsync;

        /// <summary>Creates a delegate-backed provider.</summary>
        /// <param name="getTokenAsync">Delegate that resolves a fresh token for each invocation.</param>
        public DelegateAuthTokenProvider(Func<CancellationToken, Task<string>> getTokenAsync)
        {
            _getTokenAsync = getTokenAsync ?? throw new ArgumentNullException(nameof(getTokenAsync));
        }

        /// <inheritdoc />
        public async Task<AuthTokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Task<string> tokenTask = _getTokenAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (tokenTask == null)
                    return AuthTokenResult.Failed("Auth token delegate returned no task.");

                string token = await tokenTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return string.IsNullOrWhiteSpace(token)
                    ? AuthTokenResult.Failed("Auth token delegate returned an empty token.")
                    : AuthTokenResult.Succeeded(token.Trim());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Auth token resolution was cancelled.",
                    exception,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                return AuthTokenResult.Failed(
                    $"Auth token delegate failed ({exception.GetType().Name}).");
            }
        }
    }
}
