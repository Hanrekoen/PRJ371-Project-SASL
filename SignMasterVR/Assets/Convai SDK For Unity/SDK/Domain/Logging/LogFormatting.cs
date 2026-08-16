using System;
using System.Text;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Shared formatting utilities for log entries.
    ///     Thread-safe with pooled StringBuilder instances.
    /// </summary>
    /// <remarks>
    ///     Provides centralized caching of enum names and StringBuilder pooling
    ///     to eliminate GC pressure in high-frequency logging scenarios.
    ///     All arrays are indexed by enum value for O(1) lookup performance.
    /// </remarks>
    public static class LogFormatting
    {
        #region Cached Names (Array-based O(1) lookup)

        /// <summary>
        ///     String names for LogLevel enum values.
        ///     Indexed by (int)LogLevel for O(1) lookup.
        /// </summary>
        public static readonly string[] LevelNames =
        {
            nameof(LogLevel.Off), // 0
            nameof(LogLevel.Error), // 1
            nameof(LogLevel.Warning), // 2
            nameof(LogLevel.Info), // 3
            nameof(LogLevel.Debug), // 4
            nameof(LogLevel.Trace) // 5
        };

        /// <summary>
        ///     Rich text color names for each LogLevel.
        ///     Used for Unity Console colored output.
        ///     Indexed by (int)LogLevel for O(1) lookup.
        /// </summary>
        public static readonly string[] LevelColors =
        {
            "", // Off - no color
            "red", // Error
            "yellow", // Warning
            "grey", // Info
            "cyan", // Debug
            "gray" // Trace
        };

        /// <summary>
        ///     String names for LogCategory enum values.
        ///     Indexed by (int)LogCategory for O(1) lookup.
        /// </summary>
        public static readonly string[] CategoryNames =
        {
            nameof(LogCategory.SDK), // 0
            nameof(LogCategory.Character), // 1
            nameof(LogCategory.Audio), // 2
            nameof(LogCategory.UI), // 3
            nameof(LogCategory.REST), // 4
            nameof(LogCategory.Transport), // 5
            nameof(LogCategory.Events), // 6
            nameof(LogCategory.Player), // 7
            nameof(LogCategory.Editor), // 8
            nameof(LogCategory.Vision), // 9
            nameof(LogCategory.Bootstrap), // 10
            nameof(LogCategory.Transcript), // 11
            nameof(LogCategory.Narrative), // 12
            nameof(LogCategory.LipSync) // 13
        };

        #endregion

        #region StringBuilder Pooling

        [ThreadStatic] private static StringBuilder _threadLocalBuilder;

        private const int DefaultBuilderCapacity = 256;

        /// <summary>
        ///     Gets a thread-local StringBuilder, cleared and ready for use.
        ///     Eliminates allocations for repeated formatting on the same thread.
        /// </summary>
        /// <param name="capacity">Initial capacity hint. Builder will grow if needed.</param>
        /// <returns>A cleared StringBuilder ready for use.</returns>
        public static StringBuilder GetBuilder(int capacity = DefaultBuilderCapacity)
        {
            StringBuilder sb = _threadLocalBuilder;
            if (sb == null)
            {
                sb = new StringBuilder(capacity);
                _threadLocalBuilder = sb;
            }
            else
            {
                sb.Clear();
                if (sb.Capacity < capacity) sb.Capacity = capacity;
            }

            return sb;
        }

        #endregion

        #region Name Lookup Methods

        /// <summary>
        ///     Gets the cached string name for a LogLevel.
        ///     Falls back to ToString() for out-of-range values.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <returns>The string name of the level.</returns>
        public static string GetLevelName(LogLevel level)
        {
            int index = (int)level;
            return index >= 0 && index < LevelNames.Length
                ? LevelNames[index]
                : level.ToString();
        }

        /// <summary>
        ///     Gets the cached string name for a LogCategory.
        ///     Falls back to ToString() for out-of-range values.
        /// </summary>
        /// <param name="category">The log category.</param>
        /// <returns>The string name of the category.</returns>
        public static string GetCategoryName(LogCategory category)
        {
            int index = (int)category;
            return index >= 0 && index < CategoryNames.Length
                ? CategoryNames[index]
                : category.ToString();
        }

        /// <summary>
        ///     Gets the rich text color for a LogLevel.
        ///     Returns empty string for levels without a defined color.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <returns>The color name (e.g., "red", "yellow") or empty string.</returns>
        public static string GetLevelColor(LogLevel level)
        {
            int index = (int)level;
            return index >= 0 && index < LevelColors.Length
                ? LevelColors[index]
                : "";
        }

        #endregion
    }
}
