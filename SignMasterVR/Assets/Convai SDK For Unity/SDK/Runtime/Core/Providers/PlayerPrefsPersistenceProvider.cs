using System;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Persistence provider using Unity's PlayerPrefs for key-value storage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the default persistence provider for local storage.
    ///         It wraps <see cref="PlayerPrefs" /> with the <see cref="IPersistenceProvider" /> interface.
    ///     </para>
    ///     <para>
    ///         <b>Limitations</b>:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>No cloud sync (SyncAsync is no-op)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Platform-specific storage locations</description>
    ///             </item>
    ///             <item>
    ///                 <description>Limited to string/int/float types natively</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal sealed class PlayerPrefsPersistenceProvider : IPersistenceProvider
    {
        private const string BoolTrueValue = "1";
        private const string BoolFalseValue = "0";

        #region Read Operations

        /// <inheritdoc />
        public string GetString(string key, string defaultValue = null) =>
            PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : defaultValue;

        /// <inheritdoc />
        public int GetInt(string key, int defaultValue = 0) => PlayerPrefs.GetInt(key, defaultValue);

        /// <inheritdoc />
        public float GetFloat(string key, float defaultValue = 0f) => PlayerPrefs.GetFloat(key, defaultValue);

        /// <inheritdoc />
        public bool GetBool(string key, bool defaultValue = false)
        {
            if (!PlayerPrefs.HasKey(key))
                return defaultValue;

            string value = PlayerPrefs.GetString(key);
            return value == BoolTrueValue;
        }

        /// <inheritdoc />
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);

        #endregion

        #region Write Operations

        /// <inheritdoc />
        public PersistenceResult SetString(string key, string value, PersistenceOptions options = default)
        {
            try
            {
                PlayerPrefs.SetString(key, value);
                return PersistenceResult.Succeeded();
            }
            catch (Exception ex)
            {
                return PersistenceResult.Failed(ex.Message);
            }
        }

        /// <inheritdoc />
        public PersistenceResult SetInt(string key, int value, PersistenceOptions options = default)
        {
            try
            {
                PlayerPrefs.SetInt(key, value);
                return PersistenceResult.Succeeded();
            }
            catch (Exception ex)
            {
                return PersistenceResult.Failed(ex.Message);
            }
        }

        /// <inheritdoc />
        public PersistenceResult SetFloat(string key, float value, PersistenceOptions options = default)
        {
            try
            {
                PlayerPrefs.SetFloat(key, value);
                return PersistenceResult.Succeeded();
            }
            catch (Exception ex)
            {
                return PersistenceResult.Failed(ex.Message);
            }
        }

        /// <inheritdoc />
        public PersistenceResult SetBool(string key, bool value, PersistenceOptions options = default)
        {
            try
            {
                PlayerPrefs.SetString(key, value ? BoolTrueValue : BoolFalseValue);
                return PersistenceResult.Succeeded();
            }
            catch (Exception ex)
            {
                return PersistenceResult.Failed(ex.Message);
            }
        }

        /// <inheritdoc />
        public PersistenceResult Delete(string key)
        {
            try
            {
                PlayerPrefs.DeleteKey(key);
                return PersistenceResult.Succeeded();
            }
            catch (Exception ex)
            {
                return PersistenceResult.Failed(ex.Message);
            }
        }

        /// <inheritdoc />
        public PersistenceResult DeleteAll(string prefix)
        {
            // PlayerPrefs doesn't support prefix-based deletion
            // This would require iterating all keys, which PlayerPrefs doesn't support
            // For now, return success (no-op for prefix deletion)
            return PersistenceResult.Succeeded();
        }

        #endregion

        #region Sync Operations

        /// <inheritdoc />
        public void Save() => PlayerPrefs.Save();

        /// <inheritdoc />
        public IConvaiOperation<PersistenceResult> SyncAsync(CancellationToken ct = default)
        {
            // PlayerPrefs is local-only, no cloud sync
            Save();
            return ConvaiOperation<PersistenceResult>.Succeeded(PersistenceResult.Succeeded());
        }

        #endregion

        #region Versioned Operations (Limited Support)

        /// <inheritdoc />
        /// <remarks>
        ///     <para>
        ///         <b>PlayerPrefs Limitation</b>: This implementation uses
        ///         <see cref="ConflictResolutionStrategy.LastWriteWins" /> regardless of the
        ///         specified strategy, as PlayerPrefs doesn't support versioning or conflict detection.
        ///     </para>
        ///     <para>
        ///         For full versioning support, use a custom <see cref="IPersistenceProvider" />
        ///         backed by a cloud save service.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<PersistenceResult> SaveVersionedAsync<T>(
            VersionedKey key,
            T value,
            ConflictResolutionStrategy strategy = ConflictResolutionStrategy.LastWriteWins,
            CancellationToken ct = default)
        {
            try
            {
                string json = JsonUtility.ToJson(value);
                PlayerPrefs.SetString(key.FullKey, json);
                Save();

                // PlayerPrefs doesn't track versions, so we return version + 1 on success
                return ConvaiOperation<PersistenceResult>.Succeeded(
                    PersistenceResult.Succeeded(key.Version + 1));
            }
            catch (Exception ex)
            {
                return ConvaiOperation<PersistenceResult>.Succeeded(
                    PersistenceResult.Failed($"Failed to save versioned key '{key.FullKey}': {ex.Message}"));
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     <para>
        ///         <b>PlayerPrefs Limitation</b>: This implementation always returns version 0,
        ///         as PlayerPrefs doesn't support versioning.
        ///     </para>
        ///     <para>
        ///         For full versioning support, use a custom <see cref="IPersistenceProvider" />
        ///         backed by a cloud save service.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<VersionedValue<T>> LoadVersionedAsync<T>(
            string ns,
            string key,
            CancellationToken ct = default)
        {
            try
            {
                var versionedKey = new VersionedKey(ns, key);
                string fullKey = versionedKey.FullKey;

                if (!PlayerPrefs.HasKey(fullKey))
                    return ConvaiOperation<VersionedValue<T>>.Succeeded(VersionedValue<T>.NotFound);

                string json = PlayerPrefs.GetString(fullKey, null);
                if (string.IsNullOrEmpty(json))
                    return ConvaiOperation<VersionedValue<T>>.Succeeded(VersionedValue<T>.NotFound);

                var value = JsonUtility.FromJson<T>(json);
                // PlayerPrefs doesn't track versions, so we return version 0
                return ConvaiOperation<VersionedValue<T>>.Succeeded(
                    new VersionedValue<T>(value, 0));
            }
            catch (Exception ex)
            {
                // On deserialization error, return not found
                ConvaiLogger.Warning(
                    $"Failed to load versioned key '{ns}.{key}': {ex.Message}",
                    LogCategory.SDK);
                return ConvaiOperation<VersionedValue<T>>.Succeeded(VersionedValue<T>.NotFound);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     <para>
        ///         <b>PlayerPrefs Limitation</b>: This implementation is a no-op and always
        ///         returns success, as PlayerPrefs doesn't support schema versioning or migration.
        ///     </para>
        ///     <para>
        ///         For actual migration support, use a custom <see cref="IPersistenceProvider" />
        ///         that tracks schema versions and implements migration logic.
        ///     </para>
        /// </remarks>
        public IConvaiOperation<PersistenceResult> MigrateAsync(
            int fromVersion,
            int toVersion,
            CancellationToken ct = default)
        {
            // PlayerPrefs doesn't support schema migration - this is a no-op
            ConvaiLogger.Debug(
                $"Migration requested from v{fromVersion} to v{toVersion} (no-op)",
                LogCategory.SDK);
            return ConvaiOperation<PersistenceResult>.Succeeded(PersistenceResult.Succeeded());
        }

        #endregion
    }
}
