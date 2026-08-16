namespace Convai.Domain.Identity
{
    /// <summary>
    ///     Provides device-specific information for identity generation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface abstracts access to device information, primarily used by
    ///         end-user identifier providers (see <see cref="IEndUserIdProvider" />), such as
    ///         <c>DeviceEndUserIdProvider</c>, to generate device-based user identifiers.
    ///     </para>
    ///     <para>
    ///         The abstraction enables unit testing of identity providers by allowing
    ///         mock implementations to simulate various device states (e.g., unavailable
    ///         device ID, all-zeros identifier, etc.).
    ///     </para>
    /// </remarks>
    public interface IDeviceInfoProvider
    {
        /// <summary>
        ///     Gets the unique identifier for this device.
        /// </summary>
        /// <returns>
        ///     The device's unique identifier string. This may return:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>A valid unique device identifier on supported platforms</description>
        ///         </item>
        ///         <item>
        ///             <description><c>null</c> or empty string on unsupported platforms</description>
        ///         </item>
        ///         <item>
        ///             <description>A system-defined "unsupported" constant on some platforms</description>
        ///         </item>
        ///         <item>
        ///             <description>All zeros on some platforms when the identifier is unavailable</description>
        ///         </item>
        ///     </list>
        /// </returns>
        public string GetDeviceUniqueIdentifier();
    }
}
