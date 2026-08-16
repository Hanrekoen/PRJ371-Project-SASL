using System;
using DomainLogLevel = Convai.Domain.Logging.LogLevel;
using LogCategory = Convai.Domain.Logging.LogCategory;

namespace Convai.Runtime.Logging
{
    /// <summary>
    ///     Dynamic logging configuration that reads from ConvaiSettings with caching.
    ///     Replaces the static LoggerConfig for runtime-configurable logging.
    /// </summary>
    /// <remarks>
    ///     Performance optimizations:
    ///     - Pre-computed lookup table for category log levels (O(1) instead of O(n))
    ///     - Cached ConvaiSettings.Instance reference
    ///     - Cache invalidation via version number
    ///     This class provides a bridge between the Domain logging types and ConvaiSettings.
    ///     Runtime changes to settings are detected and cache is refreshed automatically.
    ///     Usage:
    ///     <code>
    /// if (LoggingConfig.IsEnabled(LogLevel.Debug, LogCategory.SDK))
    /// {
    /// 
    /// }
    /// </code>
    /// </remarks>
    public static class LoggingConfig
    {
        // Derived from the enum rather than named after its last member. Written by hand, this
        // silently under-sizes the cache the moment a category is added: every category past the
        // end falls through to a hardcoded Info, which ignores the user's per-category override
        // *and* their global level, so adding a category quietly made its logs unsilenceable.
        private static readonly int CategoryCount = Enum.GetValues(typeof(LogCategory)).Length;

        private static readonly DomainLogLevel[] _categoryLevels = new DomainLogLevel[CategoryCount];

        private static ConvaiSettings _cachedSettings;

        private static int _cachedVersion = -1;

        private static bool _includeStackTraces = true;
        private static bool _coloredOutput = true;

        static LoggingConfig()
        {
            for (int i = 0; i < CategoryCount; i++) _categoryLevels[i] = DomainLogLevel.Info;
        }

        /// <summary>
        ///     Gets the global log level from cached settings.
        /// </summary>
        public static DomainLogLevel GlobalLogLevel
        {
            get
            {
                EnsureCacheValid();
                return _cachedSettings?.GlobalLogLevel ?? DomainLogLevel.Info;
            }
        }

        /// <summary>
        ///     Gets whether stack traces should be included for Warning and Error logs.
        /// </summary>
        public static bool IncludeStackTraces
        {
            get
            {
                EnsureCacheValid();
                return _includeStackTraces;
            }
        }

        /// <summary>
        ///     Gets whether colored output is enabled in Unity Console.
        /// </summary>
        public static bool ColoredOutput
        {
            get
            {
                EnsureCacheValid();
                return _coloredOutput;
            }
        }

        /// <summary>
        ///     Ensures the cache is valid by checking settings version.
        ///     Called on every IsEnabled check but is very fast when cache is valid.
        /// </summary>
        private static void EnsureCacheValid()
        {
            var settings = ConvaiSettings.Instance;
            if (settings == null)
            {
                if (_cachedSettings != null)
                {
                    _cachedSettings = null;
                    _cachedVersion = -1;
                    ResetToDefaults();
                }

                return;
            }

            int currentVersion = settings.ConfigVersion;
            if (_cachedSettings != settings || _cachedVersion != currentVersion)
            {
                RefreshCache(settings);
                _cachedSettings = settings;
                _cachedVersion = currentVersion;
            }
        }

        /// <summary>
        ///     Refreshes the cached lookup table from settings.
        /// </summary>
        private static void RefreshCache(ConvaiSettings settings)
        {
            DomainLogLevel globalLevel = settings.GlobalLogLevel;

            for (int i = 0; i < CategoryCount; i++) _categoryLevels[i] = globalLevel;

            LogLevelOverride[] overrides = settings.CategoryOverrides;
            if (overrides != null)
            {
                foreach (LogLevelOverride over in overrides)
                {
                    int index = (int)over.Category;
                    if (index >= 0 && index < CategoryCount) _categoryLevels[index] = over.Level;
                }
            }

            _includeStackTraces = settings.IncludeStackTraces;
            _coloredOutput = settings.ColoredOutput;
        }

        /// <summary>
        ///     Resets cache to default values when settings are unavailable.
        /// </summary>
        private static void ResetToDefaults()
        {
            for (int i = 0; i < CategoryCount; i++) _categoryLevels[i] = DomainLogLevel.Info;
            _includeStackTraces = true;
            _coloredOutput = true;
        }

        /// <summary>
        ///     Forces cache invalidation. Call when settings change externally.
        /// </summary>
        public static void InvalidateCache() => _cachedVersion = -1;

        /// <summary>
        ///     Gets the effective log level for a specific category.
        ///     Uses O(1) array lookup instead of O(n) linear search.
        /// </summary>
        /// <param name="category">The log category to check.</param>
        /// <returns>The effective log level for the category.</returns>
        public static DomainLogLevel GetLogLevel(LogCategory category)
        {
            EnsureCacheValid();
            int index = (int)category;
            return index >= 0 && index < CategoryCount
                ? _categoryLevels[index]
                : DomainLogLevel.Info;
        }

        /// <summary>
        ///     Checks if a message at the given level and category should be logged.
        ///     Optimized with cached lookup table for maximum performance.
        /// </summary>
        /// <param name="level">The log level of the message.</param>
        /// <param name="category">The category of the message.</param>
        /// <returns>True if the message should be logged.</returns>
        public static bool IsEnabled(DomainLogLevel level, LogCategory category)
        {
            EnsureCacheValid();
            int index = (int)category;
            DomainLogLevel configuredLevel = index >= 0 && index < CategoryCount
                ? _categoryLevels[index]
                : DomainLogLevel.Info;

            return level <= configuredLevel;
        }

        /// <summary>
        ///     Checks if debug logging is enabled for a category.
        /// </summary>
        public static bool IsDebugEnabled(LogCategory category) =>
            IsEnabled(DomainLogLevel.Debug, category);

        /// <summary>
        ///     Checks if info logging is enabled for a category.
        /// </summary>
        public static bool IsInfoEnabled(LogCategory category) =>
            IsEnabled(DomainLogLevel.Info, category);

        /// <summary>
        ///     Checks if warning logging is enabled for a category.
        /// </summary>
        public static bool IsWarningEnabled(LogCategory category) =>
            IsEnabled(DomainLogLevel.Warning, category);

        /// <summary>
        ///     Checks if error logging is enabled for a category.
        /// </summary>
        public static bool IsErrorEnabled(LogCategory category) =>
            IsEnabled(DomainLogLevel.Error, category);
    }
}
