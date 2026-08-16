using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Errors;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Mode-aware runtime credential provider that exposes a freshly resolved auth token through the existing
    ///     synchronous <see cref="ICredentialProvider" /> contract.
    /// </summary>
    internal sealed class AuthTokenCredentialProvider :
        ICredentialProvider,
        IAsyncCredentialProvider,
        ICredentialConfigurationStatus,
        IAsyncCredentialResolutionStatus,
        IExplicitAuthTokenCredentialProvider
    {
        private const string MissingProviderMessage =
            "Auth Token mode requires a registered IConvaiAuthTokenProvider or a configured endpoint URL.";

        private const string InvalidEndpointMessage = EndpointAuthTokenProvider.InvalidEndpointMessage;

        private readonly object _sync = new();
        private readonly bool _usesSingletonSettings;
        private string _credentialResolutionErrorMessage = string.Empty;
        private string _nextConnectionAuthToken = string.Empty;
        private string _resolvedToken = string.Empty;
        private ConvaiSettings _settings;

        /// <summary>Creates an auth-token credential provider.</summary>
        /// <param name="settings">Settings source. Null follows <see cref="ConvaiSettings.Instance" />.</param>
        internal AuthTokenCredentialProvider(ConvaiSettings settings = null)
        {
            _usesSingletonSettings = settings == null;
            _settings = settings ?? ConvaiSettings.Instance;
        }

        /// <inheritdoc />
        public bool HasValidCredentials
        {
            get
            {
                if (ConvaiAuthTokenProviderRegistry.TryGetProvider(out _))
                    return true;

                return _settings?.TryGetAuthTokenEndpointUri(out _) == true;
            }
        }

        /// <inheritdoc />
        public string ConfigurationErrorCode
        {
            get
            {
                if (HasValidCredentials)
                    return string.Empty;

                return string.IsNullOrWhiteSpace(_settings?.AuthTokenEndpointUrl)
                    ? SessionErrorCodes.ConfigAuthTokenProviderMissing
                    : SessionErrorCodes.ConfigAuthTokenEndpointInvalid;
            }
        }

        /// <inheritdoc />
        public string ConfigurationErrorMessage
        {
            get
            {
                if (HasValidCredentials)
                    return string.Empty;

                return string.IsNullOrWhiteSpace(_settings?.AuthTokenEndpointUrl)
                    ? MissingProviderMessage
                    : InvalidEndpointMessage;
            }
        }

        /// <inheritdoc />
        public string CredentialResolutionErrorMessage
        {
            get
            {
                lock (_sync)
                    return _credentialResolutionErrorMessage;
            }
        }

        /// <inheritdoc />
        public string GetApiKey()
        {
            lock (_sync)
                return _resolvedToken;
        }

        /// <inheritdoc />
        public string GetServerUrl() => _settings?.ServerUrl ?? string.Empty;

        /// <inheritdoc />
        public void Refresh()
        {
            if (_usesSingletonSettings)
                _settings = ConvaiSettings.Instance;

            ClearCredentials();
        }

        /// <inheritdoc />
        public void SetAuthTokenForNextConnection(string authToken)
        {
            string normalizedToken = authToken?.Trim();
            if (string.IsNullOrEmpty(normalizedToken))
                throw new ArgumentException("A non-empty Convai auth token is required.", nameof(authToken));

            lock (_sync)
                _nextConnectionAuthToken = normalizedToken;
        }

        /// <inheritdoc />
        public async Task EnsureCredentialsAsync(CancellationToken cancellationToken)
        {
            // A token belongs only to one connection attempt. Clear it before every resolution, including failures,
            // so a previous session's credential can never be reused accidentally.
            ClearResolvedCredential();
            string explicitAuthToken = ConsumeNextConnectionAuthToken();
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(explicitAuthToken))
            {
                lock (_sync)
                {
                    _resolvedToken = explicitAuthToken;
                    _credentialResolutionErrorMessage = string.Empty;
                }

                return;
            }

            IConvaiAuthTokenProvider tokenProvider;
            if (!ConvaiAuthTokenProviderRegistry.TryGetProvider(out tokenProvider))
            {
                if (_settings?.TryGetAuthTokenEndpointUri(out _) != true)
                {
                    SetResolutionError(ConfigurationErrorMessage);
                    return;
                }

                tokenProvider = new EndpointAuthTokenProvider(_settings);
            }

            AuthTokenResult result;
            try
            {
                result = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ClearResolvedCredential();
                throw;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                ClearResolvedCredential();
                throw new OperationCanceledException(
                    "Auth token resolution was cancelled.",
                    exception,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                SetResolutionError(
                    $"Auth token provider failed ({exception.GetType().Name}).");
                return;
            }

            if (!result.IsSuccess)
            {
                SetResolutionError(string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Auth token provider failed to resolve a token."
                    : result.ErrorMessage);
                return;
            }

            string token = result.Token?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                SetResolutionError("Auth token provider returned an empty token.");
                return;
            }

            // ExpiresAtUtc is informational here. The token was resolved for this connection attempt,
            // and the Convai service is authoritative instead of the player's potentially skewed clock.
            lock (_sync)
            {
                _resolvedToken = token;
                _credentialResolutionErrorMessage = string.Empty;
            }
        }

        private void ClearResolvedCredential()
        {
            lock (_sync)
            {
                _resolvedToken = string.Empty;
                _credentialResolutionErrorMessage = string.Empty;
            }
        }

        private void ClearCredentials()
        {
            lock (_sync)
            {
                _resolvedToken = string.Empty;
                _nextConnectionAuthToken = string.Empty;
                _credentialResolutionErrorMessage = string.Empty;
            }
        }

        private string ConsumeNextConnectionAuthToken()
        {
            lock (_sync)
            {
                string authToken = _nextConnectionAuthToken;
                _nextConnectionAuthToken = string.Empty;
                return authToken;
            }
        }

        private void SetResolutionError(string errorMessage)
        {
            lock (_sync)
            {
                _resolvedToken = string.Empty;
                _credentialResolutionErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? "Auth token resolution failed."
                    : errorMessage;
            }
        }
    }
}
