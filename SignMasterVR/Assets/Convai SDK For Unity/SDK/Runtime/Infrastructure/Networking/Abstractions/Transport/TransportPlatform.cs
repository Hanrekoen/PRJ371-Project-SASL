namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Identifies the platform type for transport implementations.
    /// </summary>
    public enum TransportPlatform
    {
        /// <summary>Desktop platforms (Windows, Mac, Linux).</summary>
        Desktop = 0,

        /// <summary>Mobile platforms (iOS, Android).</summary>
        Mobile = 1,

        /// <summary>WebGL browser platform.</summary>
        WebGL = 2
    }
}
