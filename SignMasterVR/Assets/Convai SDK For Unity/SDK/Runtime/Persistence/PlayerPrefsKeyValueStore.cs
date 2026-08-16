using Convai.Domain.Abstractions;
using UnityEngine;

namespace Convai.Runtime.Persistence
{
    /// <summary>
    ///     Unity PlayerPrefs-based implementation of <see cref="IKeyValueStore" />.
    ///     Provides key-value storage using Unity's PlayerPrefs system.
    ///     All operations are marshaled to the Unity main thread via <see cref="UnityScheduler" />
    ///     to prevent threading violations when called from async/background contexts.
    /// </summary>
    public sealed class PlayerPrefsKeyValueStore : IKeyValueStore
    {
        /// <inheritdoc />
        public string GetString(string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;

            if (UnityScheduler.IsOnMainThread)
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, defaultValue) : defaultValue;

            return UnityScheduler.PostAsync(() =>
                PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, defaultValue) : defaultValue
            ).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UnityScheduler.IsOnMainThread)
            {
                SetStringDirect(key, value);
                return;
            }

            // Capture for the closure to avoid issues with mutated references.
            string capturedKey = key;
            string capturedValue = value;
            UnityScheduler.Post(() => SetStringDirect(capturedKey, capturedValue));
        }

        /// <inheritdoc />
        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (UnityScheduler.IsOnMainThread) return PlayerPrefs.HasKey(key);

            return UnityScheduler.PostAsync(() => PlayerPrefs.HasKey(key)).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public void DeleteKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UnityScheduler.IsOnMainThread)
            {
                PlayerPrefs.DeleteKey(key);
                return;
            }

            string capturedKey = key;
            UnityScheduler.Post(() => PlayerPrefs.DeleteKey(capturedKey));
        }

        /// <inheritdoc />
        public void Save()
        {
            if (UnityScheduler.IsOnMainThread)
            {
                PlayerPrefs.Save();
                return;
            }

            UnityScheduler.Post(PlayerPrefs.Save);
        }

        private static void SetStringDirect(string key, string value)
        {
            if (value == null)
                PlayerPrefs.DeleteKey(key);
            else
                PlayerPrefs.SetString(key, value);
        }
    }
}
