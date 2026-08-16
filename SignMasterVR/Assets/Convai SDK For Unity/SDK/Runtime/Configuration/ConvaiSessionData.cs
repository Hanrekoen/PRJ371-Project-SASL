using System;
using System.Collections.Generic;
using System.IO;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace Convai.Runtime
{
    /// <summary>
    ///     Manages runtime session data for the Convai SDK.
    ///     This data persists to Application.persistentDataPath for use in builds.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="ConvaiSettings" />, this class stores mutable runtime state
    ///     that changes during gameplay (e.g., session IDs for conversation resumption).
    ///     Data is stored in: {Application.persistentDataPath}/Convai/sessions.json
    /// </remarks>
    public sealed class ConvaiSessionData
    {
        private const string DirectoryName = "Convai";
        private const string FileName = "sessions.json";

        private static ConvaiSessionData _instance;
        private static readonly object _lock = new();
        private readonly string _filePath;

        private SessionDataContainer _data;

        private ConvaiSessionData()
        {
            string directory = Path.Combine(UnityApplication.persistentDataPath, DirectoryName);
            _filePath = Path.Combine(directory, FileName);

            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            Load();
        }

        /// <summary>
        ///     Gets the singleton instance of ConvaiSessionData.
        /// </summary>
        public static ConvaiSessionData Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ConvaiSessionData();
                    }
                }

                return _instance;
            }
        }

        /// <summary>
        ///     Gets the session ID for a character, if one exists.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>The session ID, or null if not found.</returns>
        public string GetSessionId(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return null;

            if (_data.CharacterSessionIdMap.TryGetValue(characterId, out string sessionId)) return sessionId;
            return null;
        }

        /// <summary>
        ///     Stores a session ID for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="sessionId">The session ID to store.</param>
        public void StoreSessionId(string characterId, string sessionId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                ConvaiLogger.Warning("Cannot store session ID: characterId is null or empty.",
                    LogCategory.SDK);
                return;
            }

            _data.CharacterSessionIdMap[characterId] = sessionId;
            Save();
        }

        /// <summary>
        ///     Clears the session ID for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        public void ClearSessionId(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return;

            if (_data.CharacterSessionIdMap.Remove(characterId)) Save();
        }

        /// <summary>
        ///     Clears all stored session IDs.
        /// </summary>
        public void ClearAllSessionIds()
        {
            _data.CharacterSessionIdMap.Clear();
            Save();
        }

        /// <summary>
        ///     Gets a read-only copy of all stored session mappings.
        /// </summary>
        /// <returns>Dictionary of character ID to session ID mappings.</returns>
        public IReadOnlyDictionary<string, string> GetAllSessionIds() => _data.CharacterSessionIdMap;

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _data = JsonUtility.FromJson<SessionDataContainer>(json) ?? new SessionDataContainer();
                }
                else
                    _data = new SessionDataContainer();
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to load session data: {ex.Message}", LogCategory.SDK);
                _data = new SessionDataContainer();
            }
        }

        private void Save()
        {
            try
            {
                _data.SyncToLists();
                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to save session data: {ex.Message}", LogCategory.SDK);
            }
        }

        /// <summary>
        ///     Internal container for serialization.
        ///     JsonUtility requires a class with serializable fields.
        /// </summary>
        [Serializable]
        private class SessionDataContainer
        {
            public List<string> Keys = new();
            public List<string> Values = new();

            [NonSerialized] private Dictionary<string, string> _map;

            public Dictionary<string, string> CharacterSessionIdMap
            {
                get
                {
                    if (_map == null)
                    {
                        _map = new Dictionary<string, string>();
                        for (int i = 0; i < Keys.Count && i < Values.Count; i++) _map[Keys[i]] = Values[i];
                    }

                    return _map;
                }
            }

            /// <summary>
            ///     Syncs the dictionary to the parallel lists before serialization.
            /// </summary>
            public void SyncToLists()
            {
                Keys.Clear();
                Values.Clear();
                foreach (KeyValuePair<string, string> kvp in CharacterSessionIdMap)
                {
                    Keys.Add(kvp.Key);
                    Values.Add(kvp.Value);
                }
            }
        }
    }
}
