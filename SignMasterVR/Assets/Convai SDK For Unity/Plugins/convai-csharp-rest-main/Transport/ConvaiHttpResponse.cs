#nullable enable
using System;
using System.Net;

namespace Convai.RestAPI.Transport
{
    /// <summary>
    /// Represents the response from an HTTP request.
    /// </summary>
    public sealed class ConvaiHttpResponse
    {
        /// <summary>
        /// Whether the request completed successfully (2xx status code).
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// The HTTP status code returned.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The numeric status code.
        /// </summary>
        public int StatusCodeInt => (int)StatusCode;

        /// <summary>
        /// The response body as a string.
        /// </summary>
        public string Body { get; }

        /// <summary>
        /// The reason phrase from the response (e.g., "OK", "Not Found").
        /// </summary>
        public string ReasonPhrase { get; }

        /// <summary>
        /// The URL that was requested.
        /// </summary>
        public Uri Url { get; }

        /// <summary>
        /// Error message if the request failed at the transport level (network error, timeout, etc.).
        /// Null if the request completed (even with non-2xx status).
        /// </summary>
        public string? TransportError { get; }

        /// <summary>
        /// Whether there was a transport-level error (network failure, timeout, etc.).
        /// </summary>
        public bool HasTransportError => TransportError != null;

        private ConvaiHttpResponse(
            bool isSuccess,
            HttpStatusCode statusCode,
            string body,
            string reasonPhrase,
            Uri url,
            string? transportError)
        {
            IsSuccess = isSuccess;
            StatusCode = statusCode;
            Body = body;
            ReasonPhrase = reasonPhrase;
            Url = url;
            TransportError = transportError;
        }

        /// <summary>
        /// Creates a successful response.
        /// </summary>
        public static ConvaiHttpResponse Success(HttpStatusCode statusCode, string body, Uri url, string? reasonPhrase = null)
        {
            return new ConvaiHttpResponse(
                isSuccess: true,
                statusCode: statusCode,
                body: body,
                reasonPhrase: reasonPhrase ?? statusCode.ToString(),
                url: url,
                transportError: null);
        }

        /// <summary>
        /// Creates a failed response (server returned non-2xx).
        /// </summary>
        public static ConvaiHttpResponse Failure(HttpStatusCode statusCode, string body, Uri url, string? reasonPhrase = null)
        {
            return new ConvaiHttpResponse(
                isSuccess: false,
                statusCode: statusCode,
                body: body,
                reasonPhrase: reasonPhrase ?? statusCode.ToString(),
                url: url,
                transportError: null);
        }

        /// <summary>
        /// Creates a transport error response (network failure, timeout, etc.).
        /// </summary>
        public static ConvaiHttpResponse TransportFailure(Uri url, string errorMessage)
        {
            return new ConvaiHttpResponse(
                isSuccess: false,
                statusCode: 0,
                body: string.Empty,
                reasonPhrase: "Transport Error",
                url: url,
                transportError: errorMessage);
        }

        /// <summary>
        /// Gets a truncated version of the body for logging/error messages.
        /// </summary>
        public string GetTruncatedBody(int maxLength = 500)
        {
            if (string.IsNullOrEmpty(Body) || Body.Length <= maxLength)
                return Body;
            return Body.Substring(0, maxLength) + "... (truncated)";
        }

        /// <summary>
        /// Builds a detailed error message suitable for exceptions.
        /// </summary>
        public string BuildErrorMessage()
        {
            if (HasTransportError)
            {
                return $"Transport error for {Url}: {TransportError}";
            }

            return $"HTTP {StatusCodeInt} ({ReasonPhrase}) from {Url}: {GetTruncatedBody()}";
        }
    }
}
