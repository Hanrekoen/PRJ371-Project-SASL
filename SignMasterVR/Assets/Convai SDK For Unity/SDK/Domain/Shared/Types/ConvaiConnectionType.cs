namespace Convai.Shared.Types
{
    /// <summary>
    ///     Defines the connection type for Convai character sessions.
    /// </summary>
    /// <remarks>
    ///     This enum is serializable for Unity Inspector and provides type-safe
    ///     connection type selection instead of raw strings.
    /// </remarks>
    public enum ConvaiConnectionType
    {
        /// <summary>
        ///     Audio-only connection for voice conversations.
        ///     Maps to API value: "audio"
        /// </summary>
        Audio = 0,

        /// <summary>
        ///     Audio and video connection for vision-enabled characters.
        ///     Maps to API value: "video"
        /// </summary>
        Video = 1
    }

    /// <summary>
    ///     Extension methods for <see cref="ConvaiConnectionType" /> enum.
    /// </summary>
    public static class ConvaiConnectionTypeExtensions
    {
        /// <summary>
        ///     Converts the enum value to its corresponding API string value.
        /// </summary>
        /// <param name="connectionType">The connection type enum value.</param>
        /// <returns>The API string representation ("audio" or "video").</returns>
        public static string ToApiString(this ConvaiConnectionType connectionType)
        {
            return connectionType switch
            {
                ConvaiConnectionType.Audio => "audio",
                ConvaiConnectionType.Video => "video",
                _ => "audio"
            };
        }

        /// <summary>
        ///     Parses an API string value to the corresponding enum value.
        /// </summary>
        /// <param name="apiString">The API string value.</param>
        /// <returns>The corresponding enum value, defaulting to Audio if not recognized.</returns>
        public static ConvaiConnectionType FromApiString(string apiString)
        {
            return apiString?.ToLowerInvariant() switch
            {
                "audio" => ConvaiConnectionType.Audio,
                "video" => ConvaiConnectionType.Video,
                _ => ConvaiConnectionType.Audio
            };
        }
    }
}
