using System;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>The outcome of resolving a short-lived Convai auth token.</summary>
    public readonly struct AuthTokenResult
    {
        /// <summary>Creates a token-resolution result.</summary>
        /// <param name="token">Resolved token. A null, empty, or whitespace value makes the result unsuccessful.</param>
        /// <param name="expiresAtUtc">Optional server-reported expiration instant.</param>
        /// <param name="errorMessage">Failure detail. A non-empty value makes the result unsuccessful.</param>
        public AuthTokenResult(
            string token,
            DateTimeOffset? expiresAtUtc = null,
            string errorMessage = null)
        {
            string normalizedToken = token?.Trim() ?? string.Empty;
            IsSuccess = normalizedToken.Length > 0 && string.IsNullOrWhiteSpace(errorMessage);
            Token = IsSuccess ? normalizedToken : string.Empty;
            ExpiresAtUtc = IsSuccess ? expiresAtUtc?.ToUniversalTime() : null;
            ErrorMessage = IsSuccess
                ? string.Empty
                : string.IsNullOrWhiteSpace(errorMessage)
                    ? "Auth token provider returned an empty token."
                    : errorMessage;
        }

        /// <summary>Resolved token, or an empty string when resolution failed.</summary>
        public string Token { get; }

        /// <summary>
        /// Optional server-reported expiration instant normalized to UTC. This is informational metadata;
        /// the connection service remains authoritative when validating a freshly resolved token.
        /// </summary>
        public DateTimeOffset? ExpiresAtUtc { get; }

        /// <summary>Whether resolution returned a non-empty token without an error.</summary>
        public bool IsSuccess { get; }

        /// <summary>Failure detail, or an empty string when resolution succeeded.</summary>
        public string ErrorMessage { get; }

        /// <summary>Creates a successful result.</summary>
        public static AuthTokenResult Succeeded(string token, DateTimeOffset? expiresAtUtc = null) =>
            new(token, expiresAtUtc);

        /// <summary>Creates a failed result.</summary>
        public static AuthTokenResult Failed(string errorMessage) =>
            new(string.Empty, null, errorMessage);
    }
}
