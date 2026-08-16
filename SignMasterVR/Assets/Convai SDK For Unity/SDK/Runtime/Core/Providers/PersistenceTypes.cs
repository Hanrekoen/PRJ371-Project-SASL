using System;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Result of a persistence operation.
    /// </summary>
    public readonly struct PersistenceResult
    {
        /// <summary>
        ///     Creates a successful result.
        /// </summary>
        public PersistenceResult(bool success, string errorMessage = null, long version = 0)
        {
            Success = success;
            ErrorMessage = errorMessage ?? string.Empty;
            Timestamp = DateTime.UtcNow;
            Version = version;
        }

        /// <summary>
        ///     Whether the operation succeeded.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        ///     Error message if the operation failed.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        ///     When the operation completed.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Version number after the operation (for versioned operations).
        /// </summary>
        public long Version { get; }

        /// <summary>
        ///     Creates a successful result.
        /// </summary>
        public static PersistenceResult Succeeded(long version = 0) => new(true, null, version);

        /// <summary>
        ///     Creates a failed result.
        /// </summary>
        public static PersistenceResult Failed(string error) => new(false, error);
    }

    /// <summary>
    ///     A versioned key with namespace support for scoped storage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Use namespaces to scope data (e.g., "user.preferences", "session.state").
    ///         Version is used for conflict detection in cloud sync scenarios.
    ///     </para>
    /// </remarks>
    public readonly struct VersionedKey
    {
        /// <summary>
        ///     Creates a versioned key.
        /// </summary>
        public VersionedKey(string ns, string key, long version = 0)
        {
            Namespace = ns ?? string.Empty;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version;
        }

        /// <summary>
        ///     The namespace for scoping (e.g., "user", "session", "game").
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        ///     The key within the namespace.
        /// </summary>
        public string Key { get; }

        /// <summary>
        ///     The version number for conflict detection.
        /// </summary>
        public long Version { get; }

        /// <summary>
        ///     Gets the full key path (namespace.key).
        /// </summary>
        public string FullKey => string.IsNullOrEmpty(Namespace) ? Key : $"{Namespace}.{Key}";

        /// <summary>
        ///     Creates a new key with an incremented version.
        /// </summary>
        public VersionedKey WithVersion(long newVersion) => new(Namespace, Key, newVersion);

        public override string ToString() => $"{FullKey}@v{Version}";
    }

    /// <summary>
    ///     Strategy for resolving conflicts between local and remote data.
    /// </summary>
    public enum ConflictResolutionStrategy
    {
        /// <summary>
        ///     The most recent write wins based on timestamp.
        /// </summary>
        LastWriteWins = 0,

        /// <summary>
        ///     The highest version number wins.
        /// </summary>
        HighestVersionWins = 1,

        /// <summary>
        ///     Local data always wins.
        /// </summary>
        LocalWins = 2,

        /// <summary>
        ///     Remote data always wins.
        /// </summary>
        RemoteWins = 3,

        /// <summary>
        ///     Return conflict info and let the caller resolve manually.
        /// </summary>
        Manual = 4
    }

    /// <summary>
    ///     Information about a conflict between local and remote data.
    /// </summary>
    /// <typeparam name="T">The type of the conflicting values.</typeparam>
    public readonly struct ConflictInfo<T>
    {
        /// <summary>
        ///     Creates conflict information.
        /// </summary>
        public ConflictInfo(T localValue, T remoteValue, long localVersion, long remoteVersion)
        {
            LocalValue = localValue;
            RemoteValue = remoteValue;
            LocalVersion = localVersion;
            RemoteVersion = remoteVersion;
        }

        /// <summary>
        ///     The local value that conflicts.
        /// </summary>
        public T LocalValue { get; }

        /// <summary>
        ///     The remote value that conflicts.
        /// </summary>
        public T RemoteValue { get; }

        /// <summary>
        ///     Version of the local value.
        /// </summary>
        public long LocalVersion { get; }

        /// <summary>
        ///     Version of the remote value.
        /// </summary>
        public long RemoteVersion { get; }

        /// <summary>
        ///     Whether the local version is newer.
        /// </summary>
        public bool IsLocalNewer => LocalVersion > RemoteVersion;
    }

    /// <summary>
    ///     Result of a versioned load operation.
    /// </summary>
    /// <typeparam name="T">The type of the loaded value.</typeparam>
    public readonly struct VersionedValue<T>
    {
        /// <summary>
        ///     Creates a versioned value.
        /// </summary>
        public VersionedValue(T value, long version, bool exists = true)
        {
            Value = value;
            Version = version;
            Exists = exists;
        }

        /// <summary>
        ///     The loaded value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        ///     The version of the value.
        /// </summary>
        public long Version { get; }

        /// <summary>
        ///     Whether the key exists in storage.
        /// </summary>
        public bool Exists { get; }

        /// <summary>
        ///     Creates an empty result for a non-existent key.
        /// </summary>
        public static VersionedValue<T> NotFound => new(default, 0, false);
    }

    /// <summary>
    ///     Policy for resolving conflicts when saving data.
    /// </summary>
    public enum ConflictResolutionPolicy
    {
        /// <summary>
        ///     Local data overwrites remote data.
        /// </summary>
        LocalWins = 0,

        /// <summary>
        ///     Remote data overwrites local data.
        /// </summary>
        RemoteWins = 1,

        /// <summary>
        ///     Most recently modified data wins.
        /// </summary>
        LastWriteWins = 2,

        /// <summary>
        ///     Fail the operation and let the caller decide.
        /// </summary>
        FailOnConflict = 3
    }

    /// <summary>
    ///     Options for persistence operations.
    /// </summary>
    public readonly struct PersistenceOptions
    {
        /// <summary>
        ///     Creates persistence options.
        /// </summary>
        public PersistenceOptions(
            ConflictResolutionPolicy conflictPolicy = ConflictResolutionPolicy.LastWriteWins,
            bool createIfMissing = true,
            int maxRetries = 3)
        {
            ConflictPolicy = conflictPolicy;
            CreateIfMissing = createIfMissing;
            MaxRetries = maxRetries;
        }

        /// <summary>
        ///     How to resolve conflicts.
        /// </summary>
        public ConflictResolutionPolicy ConflictPolicy { get; }

        /// <summary>
        ///     Whether to create the key if it doesn't exist.
        /// </summary>
        public bool CreateIfMissing { get; }

        /// <summary>
        ///     Maximum retry attempts for transient failures.
        /// </summary>
        public int MaxRetries { get; }

        /// <summary>
        ///     Default options.
        /// </summary>
        public static PersistenceOptions Default => new();
    }
}
