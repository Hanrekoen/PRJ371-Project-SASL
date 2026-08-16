using System.Threading;
using Convai.Runtime.Core.Async;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Provider for persistent key-value storage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Abstracts persistent storage to allow different backends:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>PlayerPrefs (default)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Cloud save (Steam, PlayStation, Xbox)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Custom file storage</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Key Format</b>: Keys should use dot notation for namespacing
    ///         (e.g., "convai.preferences.microphoneDevice").
    ///     </para>
    /// </remarks>
    public interface IPersistenceProvider
    {
        #region Read Operations

        /// <summary>
        ///     Gets a string value by key.
        /// </summary>
        /// <param name="key">Storage key.</param>
        /// <param name="defaultValue">Value to return if key not found.</param>
        /// <returns>Stored value or default.</returns>
        public string GetString(string key, string defaultValue = null);

        /// <summary>
        ///     Gets an integer value by key.
        /// </summary>
        public int GetInt(string key, int defaultValue = 0);

        /// <summary>
        ///     Gets a float value by key.
        /// </summary>
        public float GetFloat(string key, float defaultValue = 0f);

        /// <summary>
        ///     Gets a boolean value by key.
        /// </summary>
        public bool GetBool(string key, bool defaultValue = false);

        /// <summary>
        ///     Checks if a key exists.
        /// </summary>
        public bool HasKey(string key);

        #endregion

        #region Write Operations

        /// <summary>
        ///     Sets a string value.
        /// </summary>
        public PersistenceResult SetString(string key, string value, PersistenceOptions options = default);

        /// <summary>
        ///     Sets an integer value.
        /// </summary>
        public PersistenceResult SetInt(string key, int value, PersistenceOptions options = default);

        /// <summary>
        ///     Sets a float value.
        /// </summary>
        public PersistenceResult SetFloat(string key, float value, PersistenceOptions options = default);

        /// <summary>
        ///     Sets a boolean value.
        /// </summary>
        public PersistenceResult SetBool(string key, bool value, PersistenceOptions options = default);

        /// <summary>
        ///     Deletes a key.
        /// </summary>
        public PersistenceResult Delete(string key);

        /// <summary>
        ///     Deletes all keys with a given prefix.
        /// </summary>
        /// <param name="prefix">Key prefix (e.g., "convai.preferences.").</param>
        public PersistenceResult DeleteAll(string prefix);

        #endregion

        #region Sync Operations

        /// <summary>
        ///     Saves all pending changes to persistent storage.
        /// </summary>
        public void Save();

        /// <summary>
        ///     Syncs with remote storage (if applicable).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when sync is done.</returns>
        public IConvaiOperation<PersistenceResult> SyncAsync(CancellationToken ct = default);

        #endregion

        #region Versioned Operations (Cloud Save Support)

        /// <summary>
        ///     Saves a value with versioning and conflict resolution support.
        /// </summary>
        /// <typeparam name="T">Type of value to save (must be JSON-serializable).</typeparam>
        /// <param name="key">The versioned key with namespace and version.</param>
        /// <param name="value">The value to save.</param>
        /// <param name="strategy">How to resolve conflicts with remote data.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     Operation completing with result. The result contains the new version number
        ///     if successful, or error information if the operation failed.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///         <b>Default Implementation</b>: The default <see cref="PlayerPrefsPersistenceProvider" />
        ///         uses <see cref="ConflictResolutionStrategy.LastWriteWins" /> and does not support
        ///         true versioning. Custom cloud save implementations should implement full versioning.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<PersistenceResult> SaveVersionedAsync<T>(
            VersionedKey key,
            T value,
            ConflictResolutionStrategy strategy = ConflictResolutionStrategy.LastWriteWins,
            CancellationToken ct = default);

        /// <summary>
        ///     Loads a versioned value from storage.
        /// </summary>
        /// <typeparam name="T">Type of value to load (must be JSON-serializable).</typeparam>
        /// <param name="ns">The namespace.</param>
        /// <param name="key">The key within the namespace.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     Operation completing with the value and its version, or a not-found result
        ///     if the key doesn't exist.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///         <b>Default Implementation</b>: The default <see cref="PlayerPrefsPersistenceProvider" />
        ///         returns version 0 for all values. Custom cloud save implementations should
        ///         return actual version numbers.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<VersionedValue<T>> LoadVersionedAsync<T>(
            string ns,
            string key,
            CancellationToken ct = default);

        /// <summary>
        ///     Migrates data from one schema version to another.
        /// </summary>
        /// <param name="fromVersion">The current schema version.</param>
        /// <param name="toVersion">The target schema version.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation completing when migration is done.</returns>
        /// <remarks>
        ///     <para>
        ///         <b>Default Implementation</b>: The default <see cref="PlayerPrefsPersistenceProvider" />
        ///         returns success without performing any migration. Custom implementations should
        ///         implement actual data migration logic.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<PersistenceResult> MigrateAsync(
            int fromVersion,
            int toVersion,
            CancellationToken ct = default);

        #endregion
    }
}
