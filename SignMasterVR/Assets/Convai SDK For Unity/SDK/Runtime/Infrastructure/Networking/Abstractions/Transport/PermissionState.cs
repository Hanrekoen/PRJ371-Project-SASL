namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Permission state for browser/OS permissions.
    /// </summary>
    public enum PermissionState
    {
        /// <summary>Permission not yet requested.</summary>
        NotRequested = 0,

        /// <summary>Permission request pending user action.</summary>
        Pending = 1,

        /// <summary>Permission granted.</summary>
        Granted = 2,

        /// <summary>Permission denied by user or system.</summary>
        Denied = 3,

        /// <summary>Permission state unknown or not applicable.</summary>
        Unknown = 4
    }
}
