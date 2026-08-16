namespace Convai.Shared.Types
{
    /// <summary>
    ///     Defines the server endpoint types for Convai API connections.
    /// </summary>
    /// <remarks>
    ///     This enum helps manage different API endpoints that can be appended
    ///     to the base server URL for different connection scenarios.
    /// </remarks>
    public enum ConvaiServerEndpoint
    {
        /// <summary>
        ///     Standard connection endpoint for production use.
        ///     Endpoint path: "/connect"
        /// </summary>
        Connect = 0,

        /// <summary>
        ///     Room session endpoint for multiplayer/session management.
        ///     Endpoint path: "/room-session"
        /// </summary>
        RoomSession = 1,

        /// <summary>
        ///     Demo connection endpoint for testing and demos.
        ///     Endpoint path: "/demo-connect"
        /// </summary>
        DemoConnect = 2
    }

    /// <summary>
    ///     Extension methods for <see cref="ConvaiServerEndpoint" /> enum.
    /// </summary>
    public static class ConvaiServerEndpointExtensions
    {
        /// <summary>
        ///     Gets the endpoint path string for the given endpoint type.
        /// </summary>
        /// <param name="endpoint">The server endpoint enum value.</param>
        /// <returns>The endpoint path (e.g., "/connect").</returns>
        public static string GetPath(this ConvaiServerEndpoint endpoint)
        {
            return endpoint switch
            {
                ConvaiServerEndpoint.Connect => "/connect",
                ConvaiServerEndpoint.RoomSession => "/room-session",
                ConvaiServerEndpoint.DemoConnect => "/demo-connect",
                _ => "/connect"
            };
        }

        /// <summary>
        ///     Builds a complete URL by appending the endpoint path to a base URL.
        /// </summary>
        /// <param name="endpoint">The server endpoint enum value.</param>
        /// <param name="baseUrl">The base server URL (e.g., "https://api.convai.com").</param>
        /// <returns>The complete URL with endpoint path appended.</returns>
        /// <remarks>
        ///     This method handles trailing slashes in the base URL to avoid double slashes.
        /// </remarks>
        public static string BuildUrl(this ConvaiServerEndpoint endpoint, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return endpoint.GetPath();

            string normalizedBaseUrl = baseUrl.TrimEnd('/');

            return normalizedBaseUrl + endpoint.GetPath();
        }

        /// <summary>
        ///     Parses an endpoint path string to the corresponding enum value.
        /// </summary>
        /// <param name="path">The endpoint path string.</param>
        /// <returns>The corresponding enum value, defaulting to Connect if not recognized.</returns>
        public static ConvaiServerEndpoint FromPath(string path)
        {
            return path?.ToLowerInvariant().TrimStart('/') switch
            {
                "connect" => ConvaiServerEndpoint.Connect,
                "room-session" => ConvaiServerEndpoint.RoomSession,
                "demo-connect" => ConvaiServerEndpoint.DemoConnect,
                _ => ConvaiServerEndpoint.Connect
            };
        }
    }
}
