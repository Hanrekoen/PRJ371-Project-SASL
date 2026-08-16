using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Stub implementation of <see cref="IParticipantCoordinator" /> for single-player mode.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In single-player mode, there is only the local participant who is always the room owner.
    ///         This stub provides that behavior without actual multi-participant networking.
    ///     </para>
    ///     <para>
    ///         Integrates with <see cref="IAgentRegistry" /> to expose character participants
    ///         for multi-character single-player scenarios.
    ///     </para>
    /// </remarks>
    internal sealed class ParticipantCoordinatorStub : IParticipantCoordinator
    {
        private readonly IAgentRegistry _agentRegistry;
        private readonly ParticipantInfo _localParticipant;

        /// <summary>
        ///     Creates a new participant coordinator stub with a local participant.
        /// </summary>
        /// <param name="localPlayerId">The local player's unique identifier.</param>
        /// <param name="displayName">Display name for the local player.</param>
        /// <param name="agentRegistry">Optional agent registry for character participant tracking.</param>
        public ParticipantCoordinatorStub(
            string localPlayerId = "local-player",
            string displayName = "Player",
            IAgentRegistry agentRegistry = null)
        {
            _agentRegistry = agentRegistry;
            _localParticipant = new ParticipantInfo(
                localPlayerId,
                displayName,
                true,
                true,
                DateTime.UtcNow);
        }

        /// <inheritdoc />
        public ParticipantInfo LocalParticipant => _localParticipant;

        /// <inheritdoc />
        public bool IsLocalOwner => true;

        /// <inheritdoc />
        public IReadOnlyList<ParticipantInfo> Participants
        {
            get
            {
                var participants = new List<ParticipantInfo> { _localParticipant };

                // Add character participants from registry if available
                if (_agentRegistry != null)
                {
                    string localOwnerId = _agentRegistry.LocalPlayer?.PlayerId ?? _localParticipant.ParticipantId;

                    foreach (IConvaiCharacterAgent character in _agentRegistry.GetCharactersByOwner(localOwnerId))
                    {
                        participants.Add(new ParticipantInfo(
                            $"character:{character.CharacterId}",
                            character.CharacterName ?? character.CharacterId,
                            true,
                            false, // Characters are not room owners
                            DateTime.UtcNow));
                    }
                }

                return participants;
            }
        }

        /// <inheritdoc />
        public int ParticipantCount
        {
            get
            {
                int count = 1; // Local player

                if (_agentRegistry != null)
                {
                    string localOwnerId = _agentRegistry.LocalPlayer?.PlayerId ?? _localParticipant.ParticipantId;
                    count += _agentRegistry.GetCharacterCountByOwner(localOwnerId);
                }

                return count;
            }
        }

        /// <inheritdoc />
        public bool TryGetParticipant(string participantId, out ParticipantInfo participant)
        {
            if (string.IsNullOrEmpty(participantId))
            {
                participant = default;
                return false;
            }

            // Check local player
            if (participantId == _localParticipant.ParticipantId)
            {
                participant = _localParticipant;
                return true;
            }

            // Check characters
            if (_agentRegistry != null && participantId.StartsWith("character:"))
            {
                string characterId = participantId.Substring("character:".Length);
                if (_agentRegistry.TryGetCharacterById(characterId, out IConvaiCharacterAgent character))
                {
                    participant = new ParticipantInfo(
                        participantId,
                        character.CharacterName ?? character.CharacterId,
                        true,
                        false,
                        DateTime.UtcNow);
                    return true;
                }
            }

            participant = default;
            return false;
        }

        /// <inheritdoc />
        public event Action<ParticipantJoined> ParticipantJoined
        {
            add { } // No-op in single-player
            remove { }
        }

        /// <inheritdoc />
        public event Action<ParticipantLeft> ParticipantLeft
        {
            add { } // No-op in single-player
            remove { }
        }
    }
}
