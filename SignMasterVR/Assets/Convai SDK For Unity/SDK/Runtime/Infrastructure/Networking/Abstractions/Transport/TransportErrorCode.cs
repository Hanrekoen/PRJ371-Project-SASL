namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Error codes for transport operations.
    /// </summary>
    public enum TransportErrorCode
    {
        /// <summary>Unknown or unspecified error.</summary>
        Unknown = 0,

        /// <summary>Network connectivity error.</summary>
        NetworkError = 1,

        /// <summary>Authentication or token error.</summary>
        AuthenticationFailed = 2,

        /// <summary>Connection timeout.</summary>
        Timeout = 3,

        /// <summary>Server-side error.</summary>
        ServerError = 4,

        /// <summary>Permission denied (e.g., microphone access).</summary>
        PermissionDenied = 5,

        /// <summary>User gesture required but not provided.</summary>
        UserGestureRequired = 6,

        /// <summary>Feature not supported on this platform.</summary>
        NotSupported = 7
    }
}
