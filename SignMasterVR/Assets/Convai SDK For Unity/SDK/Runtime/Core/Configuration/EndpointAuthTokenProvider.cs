using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TransportHttpMethod = Convai.RestAPI.Transport.HttpMethod;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Resolves an auth token from the endpoint configured in <see cref="ConvaiSettings" />.</summary>
    /// <remarks>
    ///     The default transport is created per request and disposed by this provider. A transport supplied to the
    ///     constructor is caller-owned, which keeps test doubles and shared custom transports usable across requests.
    /// </remarks>
    internal sealed class EndpointAuthTokenProvider : IConvaiAuthTokenProvider
    {
        private const string DefaultResponseField = "apiAuthToken";
        private const string ExpirationField = "expirationTime";
        internal const string InvalidEndpointMessage =
            "Auth token endpoint must use HTTPS, except for HTTP loopback URLs used during local development.";

        private readonly string _endpointUrl;
        private readonly ConvaiAuthTokenHeader[] _headers;
        private readonly ConvaiAuthTokenHttpMethod _httpMethod;
        private readonly string _responseField;
        private readonly TimeSpan _timeout;
        private readonly IConvaiHttpTransport _transport;

        /// <summary>Creates a provider from the current serialized settings.</summary>
        internal EndpointAuthTokenProvider(
            ConvaiSettings settings,
            IConvaiHttpTransport transport = null)
            : this(
                settings?.AuthTokenEndpointUrl,
                settings?.AuthTokenHttpMethod ?? ConvaiAuthTokenHttpMethod.Get,
                settings?.AuthTokenResponseField,
                settings?.AuthTokenHeaders,
                transport,
                TimeSpan.FromSeconds(settings == null || settings.ConnectionTimeout <= 0f
                    ? 30f
                    : settings.ConnectionTimeout))
        {
        }

        /// <summary>Creates a provider from explicit endpoint configuration.</summary>
        internal EndpointAuthTokenProvider(
            string endpointUrl,
            ConvaiAuthTokenHttpMethod httpMethod = ConvaiAuthTokenHttpMethod.Get,
            string responseField = DefaultResponseField,
            ConvaiAuthTokenHeader[] headers = null,
            IConvaiHttpTransport transport = null,
            TimeSpan? timeout = null)
        {
            _endpointUrl = endpointUrl?.Trim() ?? string.Empty;
            _httpMethod = httpMethod;
            _responseField = string.IsNullOrWhiteSpace(responseField)
                ? DefaultResponseField
                : responseField.Trim();
            _headers = CopyHeaders(headers);
            _transport = transport;
            _timeout = timeout.HasValue && timeout.Value > TimeSpan.Zero
                ? timeout.Value
                : TimeSpan.FromSeconds(30);
        }

        /// <inheritdoc />
        public async Task<AuthTokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCreateEndpointUri(_endpointUrl, out Uri endpoint))
                return AuthTokenResult.Failed(InvalidEndpointMessage);

            IConvaiHttpTransport transport = null;
            bool ownsTransport = false;

            try
            {
                transport = _transport ?? ConvaiHttpTransportFactory.Create(_timeout);
                ownsTransport = _transport == null;
                ConvaiHttpRequest request = BuildRequest(endpoint);
                ConvaiHttpResponse response = await transport.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                // Some transports represent cancellation as a response instead of throwing. Preserve caller
                // cancellation as OperationCanceledException so the connection layer can classify it correctly.
                cancellationToken.ThrowIfCancellationRequested();
                return ParseResponse(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Auth token endpoint request was cancelled.",
                    exception,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                return AuthTokenResult.Failed(
                    $"Auth token endpoint request failed ({exception.GetType().Name}).");
            }
            finally
            {
                if (ownsTransport && transport != null)
                    transport.Dispose();
            }
        }

        private ConvaiHttpRequest BuildRequest(Uri endpoint)
        {
            TransportHttpMethod method = _httpMethod == ConvaiAuthTokenHttpMethod.Post
                ? TransportHttpMethod.Post
                : TransportHttpMethod.Get;

            ConvaiHttpRequest.Builder builder = ConvaiHttpRequest.CreateBuilder(endpoint, method)
                .WithTimeout(_timeout);

            if (method == TransportHttpMethod.Post)
                builder.WithBody("{}");

            foreach (ConvaiAuthTokenHeader header in _headers)
            {
                string name = header.Name.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                builder.WithHeader(name, header.Value);
            }

            return builder.Build();
        }

        private AuthTokenResult ParseResponse(ConvaiHttpResponse response)
        {
            if (response == null)
                return AuthTokenResult.Failed("Auth token endpoint returned no response.");

            if (response.HasTransportError)
                return AuthTokenResult.Failed("Auth token endpoint transport failed.");

            if (!response.IsSuccess)
                return AuthTokenResult.Failed(
                    $"Auth token endpoint returned HTTP {response.StatusCodeInt}.");

            JToken root;
            try
            {
                root = JToken.Parse(response.Body ?? string.Empty);
            }
            catch (JsonException)
            {
                return AuthTokenResult.Failed("Auth token endpoint returned malformed JSON.");
            }

            JToken tokenNode = ResolveDottedPath(root, _responseField);
            if (tokenNode == null)
                return AuthTokenResult.Failed(
                    $"Auth token response field '{_responseField}' was not found.");

            string token = tokenNode.Type == JTokenType.String ? tokenNode.Value<string>() : null;
            if (string.IsNullOrWhiteSpace(token))
                return AuthTokenResult.Failed(
                    $"Auth token response field '{_responseField}' was empty.");

            DateTimeOffset? expiresAtUtc = null;
            JToken expirationNode = root is JObject rootObject ? rootObject[ExpirationField] : null;
            if (expirationNode != null && expirationNode.Type != JTokenType.Null)
            {
                if (!TryParseExpiration(expirationNode, out DateTimeOffset expiration))
                {
                    return AuthTokenResult.Failed(
                        "Auth token response contained an invalid expirationTime.");
                }

                expiresAtUtc = expiration.ToUniversalTime();
            }

            return AuthTokenResult.Succeeded(token.Trim(), expiresAtUtc);
        }

        private static bool TryParseExpiration(JToken expirationNode, out DateTimeOffset expiration)
        {
            if (expirationNode.Type == JTokenType.Date)
            {
                try
                {
                    expiration = expirationNode.ToObject<DateTimeOffset>().ToUniversalTime();
                    return true;
                }
                catch (JsonException)
                {
                    expiration = default;
                    return false;
                }
                catch (ArgumentException)
                {
                    expiration = default;
                    return false;
                }
            }

            if (expirationNode.Type != JTokenType.String)
            {
                expiration = default;
                return false;
            }

            return DateTimeOffset.TryParse(
                expirationNode.Value<string>(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expiration);
        }

        private static JToken ResolveDottedPath(JToken root, string path)
        {
            JToken current = root;
            string[] segments = path.Split('.');
            foreach (string rawSegment in segments)
            {
                string segment = rawSegment.Trim();
                if (string.IsNullOrEmpty(segment) || current is not JObject currentObject)
                    return null;

                current = currentObject[segment];
                if (current == null)
                    return null;
            }

            return current;
        }

        internal static bool TryCreateEndpointUri(string endpointUrl, out Uri endpoint)
        {
            endpoint = null;
            if (!Uri.TryCreate(endpointUrl?.Trim(), UriKind.Absolute, out Uri candidate))
                return false;

            bool isHttps = string.Equals(
                candidate.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
            bool isHttpLoopback = string.Equals(
                                      candidate.Scheme,
                                      Uri.UriSchemeHttp,
                                      StringComparison.OrdinalIgnoreCase) &&
                                  candidate.IsLoopback;
            if ((!isHttps && !isHttpLoopback) || string.IsNullOrWhiteSpace(candidate.Host))
                return false;

            endpoint = candidate;
            return true;
        }

        private static ConvaiAuthTokenHeader[] CopyHeaders(ConvaiAuthTokenHeader[] headers)
        {
            if (headers == null || headers.Length == 0)
                return Array.Empty<ConvaiAuthTokenHeader>();

            var copy = new ConvaiAuthTokenHeader[headers.Length];
            Array.Copy(headers, copy, headers.Length);
            return copy;
        }
    }
}
