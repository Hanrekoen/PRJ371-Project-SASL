#nullable enable
using System;
using System.Collections.Generic;

namespace Convai.RestAPI.Transport
{
    /// <summary>
    /// HTTP method type for requests.
    /// </summary>
    public enum HttpMethod
    {
        Get,
        Post,
        Put,
        Delete
    }

    /// <summary>
    /// Represents an HTTP request to be sent via the transport layer.
    /// Immutable after construction.
    /// </summary>
    public sealed class ConvaiHttpRequest
    {
        /// <summary>
        /// The target URL for the request.
        /// </summary>
        public Uri Url { get; }

        /// <summary>
        /// The HTTP method to use.
        /// </summary>
        public HttpMethod Method { get; }

        /// <summary>
        /// The request body content (JSON string). Null for GET requests.
        /// </summary>
        public string? Body { get; }

        /// <summary>
        /// Headers to include with the request. Never null.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>
        /// Timeout for this specific request. Null to use transport default.
        /// </summary>
        public TimeSpan? Timeout { get; }

        private ConvaiHttpRequest(
            Uri url,
            HttpMethod method,
            string? body,
            IReadOnlyDictionary<string, string> headers,
            TimeSpan? timeout)
        {
            Url = url;
            Method = method;
            Body = body;
            Headers = headers;
            Timeout = timeout;
        }

        /// <summary>
        /// Creates a new request builder.
        /// </summary>
        public static Builder CreateBuilder(Uri url, HttpMethod method) => new(url, method);

        /// <summary>
        /// Creates a new request builder from a URL string.
        /// </summary>
        public static Builder CreateBuilder(string url, HttpMethod method) => new(new Uri(url), method);

        /// <summary>
        /// Builder for constructing ConvaiHttpRequest instances.
        /// </summary>
        public sealed class Builder
        {
            private readonly Uri _url;
            private readonly HttpMethod _method;
            private string? _body;
            private readonly Dictionary<string, string> _headers = new();
            private TimeSpan? _timeout;

            internal Builder(Uri url, HttpMethod method)
            {
                _url = url ?? throw new ArgumentNullException(nameof(url));
                _method = method;
            }

            /// <summary>
            /// Sets the request body (JSON content).
            /// </summary>
            public Builder WithBody(string body)
            {
                _body = body;
                return this;
            }

            /// <summary>
            /// Adds a header to the request.
            /// </summary>
            public Builder WithHeader(string name, string value)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentNullException(nameof(name));
                _headers[name] = value;
                return this;
            }

            /// <summary>
            /// Adds multiple headers to the request.
            /// </summary>
            public Builder WithHeaders(IEnumerable<KeyValuePair<string, string>> headers)
            {
                foreach (var kvp in headers)
                {
                    _headers[kvp.Key] = kvp.Value;
                }
                return this;
            }

            /// <summary>
            /// Sets the Convai API key header.
            /// </summary>
            public Builder WithApiKey(string apiKey)
            {
                return WithHeader("CONVAI-API-KEY", apiKey);
            }

            /// <summary>
            /// Sets the x-api-key header (used for core service endpoints).
            /// </summary>
            public Builder WithXApiKey(string apiKey)
            {
                return WithHeader("x-api-key", apiKey);
            }

            /// <summary>
            /// Sets the short-lived Convai auth-token header.
            /// </summary>
            public Builder WithAuthToken(string authToken)
            {
                return WithHeader("API-AUTH-TOKEN", authToken);
            }

            /// <summary>
            /// Sets a custom timeout for this request.
            /// </summary>
            public Builder WithTimeout(TimeSpan timeout)
            {
                _timeout = timeout;
                return this;
            }

            /// <summary>
            /// Builds the immutable request object.
            /// </summary>
            public ConvaiHttpRequest Build()
            {
                return new ConvaiHttpRequest(
                    _url,
                    _method,
                    _body,
                    new Dictionary<string, string>(_headers),
                    _timeout);
            }
        }
    }
}
