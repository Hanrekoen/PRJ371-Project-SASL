using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Implementation of <see cref="IRoomOwnershipCoordinator" /> for managing character ownership and focus.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This coordinator manages the relationship between players and characters,
    ///         tracking which characters are owned by which player, which are active,
    ///         and which character has the current focus for input.
    ///     </para>
    ///     <para>
    ///         <b>Integration</b>: Works with <see cref="IAgentRegistry" /> for character discovery
    ///         and tracking. Ownership changes are propagated via events.
    ///     </para>
    /// </remarks>
    internal sealed class RoomOwnershipCoordinator : IRoomOwnershipCoordinator
    {
        private readonly List<IConvaiCharacterAgent> _activeCharacters = new();
        private readonly IAgentRegistry _agentRegistry;
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private IConvaiCharacterAgent _focusedCharacter;
        private bool _hasPendingOwnershipReconnect;

        private IConvaiPlayerAgent _localPlayer;

        public RoomOwnershipCoordinator(IAgentRegistry agentRegistry, ILogger logger = null)
        {
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _logger = logger.WithTag(nameof(RoomOwnershipCoordinator));
        }

        /// <inheritdoc />
        public IConvaiPlayerAgent LocalPlayer
        {
            get
            {
                lock (_lock)
                    return _localPlayer;
            }
        }

        /// <inheritdoc />
        public bool HasPendingOwnershipReconnect
        {
            get
            {
                lock (_lock)
                    return _hasPendingOwnershipReconnect;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IConvaiCharacterAgent> OwnedCharacters
        {
            get
            {
                lock (_lock)
                {
                    if (_localPlayer == null)
                        return Array.Empty<IConvaiCharacterAgent>();

                    // Get characters owned by local player
                    return _agentRegistry.GetCharactersByOwner(_localPlayer.PlayerId);
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IConvaiCharacterAgent> ActiveCharacters
        {
            get
            {
                lock (_lock)
                    return _activeCharacters.ToArray();
            }
        }

        /// <inheritdoc />
        public IConvaiCharacterAgent FocusedCharacter
        {
            get
            {
                lock (_lock)
                    return _focusedCharacter;
            }
        }

        /// <inheritdoc />
        public event Action OwnershipChanged;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> CharacterActivated;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> CharacterDeactivated;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> FocusChanged;

        /// <inheritdoc />
        public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId) =>
            _agentRegistry.GetCharactersByOwner(ownerId);

        /// <inheritdoc />
        public void ActivateCharacter(IConvaiCharacterAgent character)
        {
            if (character == null) return;

            lock (_lock)
            {
                if (_activeCharacters.Contains(character))
                    return;

                _activeCharacters.Add(character);
                _logger?.Debug($"Character activated: {character.CharacterId}");
            }

            CharacterActivated?.Invoke(character);

            // Auto-focus first activated character
            if (FocusedCharacter == null)
                SetFocusedCharacter(character);
        }

        /// <inheritdoc />
        public void DeactivateCharacter(IConvaiCharacterAgent character)
        {
            if (character == null) return;

            bool removed;
            lock (_lock)
            {
                removed = _activeCharacters.Remove(character);
                if (!removed) return;

                _logger?.Debug($"Character deactivated: {character.CharacterId}");

                // Clear focus if deactivating focused character
                if (_focusedCharacter == character)
                    _focusedCharacter = _activeCharacters.Count > 0 ? _activeCharacters[0] : null;
            }

            CharacterDeactivated?.Invoke(character);

            // Fire focus changed if focus was cleared or moved
            if (removed && FocusedCharacter != character)
                FocusChanged?.Invoke(FocusedCharacter);
        }

        /// <inheritdoc />
        public void SetFocusedCharacter(IConvaiCharacterAgent character)
        {
            lock (_lock)
            {
                if (character != null && !_activeCharacters.Contains(character))
                {
                    throw new InvalidOperationException(
                        $"Cannot focus character '{character.CharacterId}' because it is not active.");
                }

                if (_focusedCharacter == character)
                    return;

                _focusedCharacter = character;
                _logger?.Debug($"Focus changed to: {character?.CharacterId ?? "null"}");
            }

            FocusChanged?.Invoke(character);
        }

        public void SetLocalPlayer(IConvaiPlayerAgent player)
        {
            lock (_lock)
            {
                _localPlayer = player;
                _logger?.Debug($"Local player set: {player?.PlayerId ?? "null"}");
            }

            OwnershipChanged?.Invoke();
        }

        public void SetPendingOwnershipReconnect(bool pending)
        {
            lock (_lock) _hasPendingOwnershipReconnect = pending;
        }

        /// <summary>
        ///     Clears all active characters and focus.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _activeCharacters.Clear();
                _focusedCharacter = null;
                _hasPendingOwnershipReconnect = false;
            }
        }
    }
}
