using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime;
using Convai.Runtime.Core.Configuration;
using UnityEngine;

namespace Convai.Editor
{
    /// <summary>
    ///     Editor-only fallback that exchanges the saved project API key for a short-lived
    ///     auth token when Auth Token mode has no endpoint or custom provider configured.
    /// </summary>
    /// <remarks>
    ///     This type is compiled into the Editor assembly only. It is never included in a
    ///     player build, and it neither logs nor persists the returned token.
    /// </remarks>
    public sealed class ConvaiEditorApiKeyAuthTokenProvider : IConvaiAuthTokenProvider
    {
        private const string TokenEndpoint = "https://api.convai.com/user/connect";
        private static readonly ConvaiEditorApiKeyAuthTokenProvider Instance = new();

        private ConvaiEditorApiKeyAuthTokenProvider()
        {
        }

        /// <summary>Fetches a fresh Editor-only auth token using the saved project API key.</summary>
        public static Task<AuthTokenResult> FetchTokenAsync(
            CancellationToken cancellationToken = default) =>
            Instance.GetTokenAsync(cancellationToken);

        /// <inheritdoc />
        public async Task<AuthTokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConvaiSettings settings = ConvaiSettings.Instance;
            string apiKey = settings?.ApiKey?.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                return AuthTokenResult.Failed(
                    "An API key must be saved in Convai Project Settings before the Editor can mint an auth token.");
            }

            TimeSpan timeout = TimeSpan.FromSeconds(
                settings.ConnectionTimeout > 0f ? settings.ConnectionTimeout : 30f);
            var provider = new EndpointAuthTokenProvider(
                TokenEndpoint,
                ConvaiAuthTokenHttpMethod.Post,
                responseField: "apiAuthToken",
                headers: new[]
                {
                    new ConvaiAuthTokenHeader("CONVAI-API-KEY", apiKey)
                },
                timeout: timeout);

            return await provider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBeforeSceneLoad() => SynchronizeRegistration();

        private static void SynchronizeRegistration()
        {
            ConvaiSettings settings = ConvaiSettings.Instance;
            bool shouldProvideEditorFallback = settings != null &&
                                               settings.AuthMode == ConvaiAuthMode.AuthToken &&
                                               settings.HasApiKey &&
                                               string.IsNullOrWhiteSpace(settings.AuthTokenEndpointUrl);

            if (shouldProvideEditorFallback)
            {
                if (!ConvaiAuthTokenProviderRegistry.IsRegistered)
                    ConvaiAuthTokenProviderRegistry.Register(Instance);
                return;
            }

            ConvaiAuthTokenProviderRegistry.Unregister(Instance);
        }
    }
}
