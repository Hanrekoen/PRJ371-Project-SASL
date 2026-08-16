namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Abstraction for a simple key-value storage system.
    ///     This interface allows for platform-agnostic persistence implementations.
    /// </summary>
    /// <remarks>
    ///     Implementations might include:
    ///     - Unity's PlayerPrefs (for Unity runtime)
    ///     - File-based storage
    ///     - In-memory storage (for testing)
    ///     - Platform-specific storage (e.g., Godot's ConfigFile)
    /// </remarks>
    public interface IKeyValueStore
    {
        /// <summary>
        ///     Gets a string value for the specified key.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="defaultValue">The default value to return if the key doesn't exist.</param>
        /// <returns>The stored value, or <paramref name="defaultValue" /> if not found.</returns>
        public string GetString(string key, string defaultValue = null);

        /// <summary>
        ///     Sets a string value for the specified key.
        /// </summary>
        /// <param name="key">The key to set.</param>
        /// <param name="value">The value to store.</param>
        public void SetString(string key, string value);

        /// <summary>
        ///     Checks if a key exists in the store.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if the key exists, false otherwise.</returns>
        public bool HasKey(string key);

        /// <summary>
        ///     Deletes a key from the store.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        public void DeleteKey(string key);

        /// <summary>
        ///     Persists any pending changes to storage.
        ///     Some implementations may auto-save; others require explicit save calls.
        /// </summary>
        public void Save();
    }
}
