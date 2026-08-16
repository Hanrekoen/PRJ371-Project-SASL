using System;
using System.Collections.Generic;
using Convai.Domain.Abstractions;

namespace Convai.Infrastructure.Persistence
{
    /// <summary>
    ///     Platform-agnostic implementation of <see cref="ISessionPersistence" />.
    ///     Stores session identifiers using an <see cref="IKeyValueStore" /> with an in-memory cache for performance.
    /// </summary>
    public sealed class KeyValueStoreSessionPersistence : ISessionPersistence
    {
        private const string DefaultKeyPrefix = "convai.session.";
        private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
        private readonly string _keyPrefix;
        private readonly HashSet<string> _knownCharacterIds = new(StringComparer.Ordinal);
        private readonly IKeyValueStore _store;
        private readonly object _syncRoot = new();
        private readonly string AllSessionsKey;

        /// <summary>
        ///     Creates a new instance of <see cref="KeyValueStoreSessionPersistence" />.
        /// </summary>
        /// <param name="store">The key-value store to use for persistence.</param>
        /// <param name="keyPrefix">Optional prefix for storage keys. Defaults to "convai.session."</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="store" /> is null.</exception>
        public KeyValueStoreSessionPersistence(IKeyValueStore store, string keyPrefix = DefaultKeyPrefix)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
            AllSessionsKey = $"{_keyPrefix}__keys";
            LoadKnownCharacterIds();
        }

        /// <inheritdoc />
        public string LoadSession(string characterId)
        {
            ValidateCharacterId(characterId);

            lock (_syncRoot)
            {
                if (_cache.TryGetValue(characterId, out string cachedSessionId))
                    return cachedSessionId;
            }

            string key = BuildKey(characterId);
            if (_store.HasKey(key))
            {
                string sessionId = _store.GetString(key);
                if (!string.IsNullOrEmpty(sessionId))
                {
                    lock (_syncRoot) _cache[characterId] = sessionId;
                    return sessionId;
                }
            }

            return null;
        }

        /// <inheritdoc />
        public void SaveSession(string characterId, string sessionId)
        {
            ValidateCharacterId(characterId);

            lock (_syncRoot)
            {
                if (string.IsNullOrEmpty(sessionId))
                    _cache.Remove(characterId);
                else
                {
                    _cache[characterId] = sessionId;
                    _knownCharacterIds.Add(characterId);
                }
            }

            string key = BuildKey(characterId);
            if (string.IsNullOrEmpty(sessionId))
            {
                _store.DeleteKey(key);
                RemoveFromKnownCharacterIds(characterId);
            }
            else
            {
                _store.SetString(key, sessionId);
                AddToKnownCharacterIds(characterId);
            }

            _store.Save();
        }

        /// <inheritdoc />
        public void ClearSession(string characterId)
        {
            ValidateCharacterId(characterId);

            lock (_syncRoot)
            {
                _cache.Remove(characterId);
                _knownCharacterIds.Remove(characterId);
            }

            string key = BuildKey(characterId);
            _store.DeleteKey(key);
            RemoveFromKnownCharacterIds(characterId);
            _store.Save();
        }

        /// <inheritdoc />
        public void ClearAllSessions()
        {
            List<string> characterIdsToClear;

            lock (_syncRoot)
            {
                characterIdsToClear = new List<string>(_knownCharacterIds);
                _cache.Clear();
                _knownCharacterIds.Clear();
            }

            foreach (string characterId in characterIdsToClear)
            {
                string key = BuildKey(characterId);
                _store.DeleteKey(key);
            }

            _store.DeleteKey(AllSessionsKey);
            _store.Save();
        }

        /// <inheritdoc />
        public bool HasSession(string characterId)
        {
            ValidateCharacterId(characterId);

            lock (_syncRoot)
            {
                if (_cache.ContainsKey(characterId))
                    return true;
            }

            return _store.HasKey(BuildKey(characterId));
        }

        private string BuildKey(string characterId) => $"{_keyPrefix}{characterId}";

        private static void ValidateCharacterId(string characterId)
        {
            if (characterId == null) throw new ArgumentNullException(nameof(characterId));

            if (characterId.Length == 0)
                throw new ArgumentException("Character ID cannot be empty.", nameof(characterId));
        }

        private void LoadKnownCharacterIds()
        {
            if (_store.HasKey(AllSessionsKey))
            {
                string allKeys = _store.GetString(AllSessionsKey, string.Empty);
                if (!string.IsNullOrEmpty(allKeys))
                {
                    string[] ids = allKeys.Split(',');
                    lock (_syncRoot)
                    {
                        foreach (string id in ids)
                        {
                            if (!string.IsNullOrEmpty(id))
                                _knownCharacterIds.Add(id);
                        }
                    }
                }
            }
        }

        private void AddToKnownCharacterIds(string characterId)
        {
            lock (_syncRoot) _knownCharacterIds.Add(characterId);
            SaveKnownCharacterIds();
        }

        private void RemoveFromKnownCharacterIds(string characterId)
        {
            lock (_syncRoot) _knownCharacterIds.Remove(characterId);
            SaveKnownCharacterIds();
        }

        private void SaveKnownCharacterIds()
        {
            string[] ids;
            lock (_syncRoot)
            {
                ids = new string[_knownCharacterIds.Count];
                _knownCharacterIds.CopyTo(ids);
            }

            if (ids.Length == 0)
                _store.DeleteKey(AllSessionsKey);
            else
                _store.SetString(AllSessionsKey, string.Join(",", ids));
        }
    }
}
