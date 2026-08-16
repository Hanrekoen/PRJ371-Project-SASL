using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using UnityEngine;

namespace Convai.Runtime.Core.Registry
{
    /// <summary>
    ///     Unified registry for all Convai agents.
    ///     Thread-safe implementation using Unity types directly.
    /// </summary>
    internal sealed class AgentRegistry : IAgentRegistry
    {
        private readonly Dictionary<string, AudioSource> _audioSources = new(); // characterId → AudioSource
        private readonly List<IConvaiCharacterAgent> _characters = new();
        private readonly object _lock = new();
        private readonly HashSet<string> _mutedCharacters = new(); // set of muted characterIds
        private readonly Dictionary<string, string> _ownershipMap = new(); // characterId → ownerId

        // Transport-layer bindings
        private readonly Dictionary<string, string> _participantIdMap = new(); // characterId → participantId
        private readonly Dictionary<string, string> _participantToCharacterMap = new(); // participantId → characterId
        private readonly List<IConvaiPlayerAgent> _players = new();

        #region Events

        public event Action<IConvaiCharacterAgent> CharacterRegistered;
        public event Action<IConvaiCharacterAgent> CharacterUnregistered;
        public event Action<IConvaiPlayerAgent> PlayerRegistered;

        #endregion

        #region Registration

        public void RegisterCharacter(IConvaiCharacterAgent character, string ownerId = null)
        {
            if (character == null) return;

            lock (_lock)
            {
                if (_characters.Contains(character)) return;
                _characters.Add(character);

                if (!string.IsNullOrEmpty(ownerId) && !string.IsNullOrEmpty(character.CharacterId))
                    _ownershipMap[character.CharacterId] = ownerId;
            }

            CharacterRegistered?.Invoke(character);
        }

        public void RegisterPlayer(IConvaiPlayerAgent player)
        {
            if (player == null) return;

            lock (_lock)
            {
                if (_players.Contains(player)) return;
                _players.Add(player);
            }

            PlayerRegistered?.Invoke(player);
        }

        public void Unregister(IConvaiCharacterAgent character)
        {
            if (character == null) return;

            bool removed;
            lock (_lock)
            {
                removed = _characters.Remove(character);
                if (removed && !string.IsNullOrEmpty(character.CharacterId))
                {
                    _ownershipMap.Remove(character.CharacterId);
                    _audioSources.Remove(character.CharacterId);
                }
            }

            if (removed)
                CharacterUnregistered?.Invoke(character);
        }

        public void Unregister(IConvaiPlayerAgent player)
        {
            if (player == null) return;

            lock (_lock) _players.Remove(player);
        }

        #endregion

        #region Query - Characters

        public IReadOnlyList<IConvaiCharacterAgent> Characters
        {
            get
            {
                lock (_lock) return _characters.ToArray();
            }
        }

        public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                agent = null;
                return false;
            }

            lock (_lock)
            {
                foreach (IConvaiCharacterAgent character in _characters)
                {
                    if (character.CharacterId == characterId)
                    {
                        agent = character;
                        return true;
                    }
                }
            }

            agent = null;
            return false;
        }

        #endregion

        #region Query - Players

        public IReadOnlyList<IConvaiPlayerAgent> Players
        {
            get
            {
                lock (_lock) return _players.ToArray();
            }
        }

        public IConvaiPlayerAgent LocalPlayer
        {
            get
            {
                lock (_lock) return _players.Count > 0 ? _players[0] : null;
            }
        }

        #endregion

        #region Ownership

        public string GetOwner(IConvaiCharacterAgent character)
        {
            if (character == null || string.IsNullOrEmpty(character.CharacterId))
                return null;

            lock (_lock) return _ownershipMap.TryGetValue(character.CharacterId, out string ownerId) ? ownerId : null;
        }

        public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return Array.Empty<IConvaiCharacterAgent>();

            lock (_lock)
            {
                var result = new List<IConvaiCharacterAgent>();
                foreach (IConvaiCharacterAgent character in _characters)
                {
                    if (!string.IsNullOrEmpty(character.CharacterId) &&
                        _ownershipMap.TryGetValue(character.CharacterId, out string owner) &&
                        owner == ownerId)
                        result.Add(character);
                }

                return result;
            }
        }

        public int GetCharacterCountByOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return 0;

            lock (_lock)
            {
                int count = 0;
                foreach (IConvaiCharacterAgent character in _characters)
                {
                    if (!string.IsNullOrEmpty(character.CharacterId) &&
                        _ownershipMap.TryGetValue(character.CharacterId, out string owner) &&
                        owner == ownerId)
                        count++;
                }

                return count;
            }
        }

        public bool TryGetCharacterById(string characterId, out IConvaiCharacterAgent agent)
        {
            // Delegate to existing TryGetCharacter method
            return TryGetCharacter(characterId, out agent);
        }

        #endregion

        #region Audio Binding

        public bool TryGetAudioSource(string characterId, out AudioSource source)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                source = null;
                return false;
            }

            lock (_lock) return _audioSources.TryGetValue(characterId, out source) && source != null;
        }

        public void SetAudioSource(string characterId, AudioSource source)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            lock (_lock)
            {
                if (source != null)
                    _audioSources[characterId] = source;
                else
                    _audioSources.Remove(characterId);
            }
        }

        #endregion

        #region Transport Binding

        public void SetParticipantId(string characterId, string participantId)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            lock (_lock)
            {
                // Remove old mapping if exists
                if (_participantIdMap.TryGetValue(characterId, out string oldParticipantId) &&
                    !string.IsNullOrEmpty(oldParticipantId))
                    _participantToCharacterMap.Remove(oldParticipantId);

                if (!string.IsNullOrEmpty(participantId))
                {
                    _participantIdMap[characterId] = participantId;
                    _participantToCharacterMap[participantId] = characterId;
                }
                else
                    _participantIdMap.Remove(characterId);
            }
        }

        public bool TryGetParticipantId(string characterId, out string participantId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                participantId = null;
                return false;
            }

            lock (_lock) return _participantIdMap.TryGetValue(characterId, out participantId);
        }

        public bool TryGetCharacterByParticipantId(string participantId, out IConvaiCharacterAgent agent)
        {
            agent = null;
            if (string.IsNullOrEmpty(participantId)) return false;

            lock (_lock)
            {
                if (!_participantToCharacterMap.TryGetValue(participantId, out string characterId))
                    return false;

                return TryGetCharacterInternal(characterId, out agent);
            }
        }

        public void ClearTransportBindings()
        {
            lock (_lock)
            {
                _participantIdMap.Clear();
                _participantToCharacterMap.Clear();
                _mutedCharacters.Clear();
            }
        }

        // Internal helper to avoid lock recursion
        private bool TryGetCharacterInternal(string characterId, out IConvaiCharacterAgent agent)
        {
            foreach (IConvaiCharacterAgent character in _characters)
            {
                if (character.CharacterId == characterId)
                {
                    agent = character;
                    return true;
                }
            }

            agent = null;
            return false;
        }

        #endregion

        #region Mute State

        public void SetCharacterMuted(string characterId, bool muted)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            lock (_lock)
            {
                if (muted)
                    _mutedCharacters.Add(characterId);
                else
                    _mutedCharacters.Remove(characterId);
            }
        }

        public bool IsCharacterMuted(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            lock (_lock) return _mutedCharacters.Contains(characterId);
        }

        #endregion
    }
}
