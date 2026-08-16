using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Single registry for all agents. Uses Unity types directly.
    ///     Replaces both IConvaiCharacterLocatorService and ICharacterRegistry.
    /// </summary>
    public interface IAgentRegistry
    {
        #region Registration

        /// <summary>Registers a character, optionally with an owner ID for multi-character support.</summary>
        public void RegisterCharacter(IConvaiCharacterAgent character, string ownerId = null);

        /// <summary>Registers a player agent.</summary>
        public void RegisterPlayer(IConvaiPlayerAgent player);

        /// <summary>Unregisters a character.</summary>
        public void Unregister(IConvaiCharacterAgent character);

        /// <summary>Unregisters a player.</summary>
        public void Unregister(IConvaiPlayerAgent player);

        #endregion

        #region Query - Characters

        /// <summary>All registered characters.</summary>
        public IReadOnlyList<IConvaiCharacterAgent> Characters { get; }

        /// <summary>Tries to find a character by its character ID.</summary>
        public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent);

        #endregion

        #region Query - Players

        /// <summary>All registered players.</summary>
        public IReadOnlyList<IConvaiPlayerAgent> Players { get; }

        /// <summary>The local player (first registered, or null).</summary>
        public IConvaiPlayerAgent LocalPlayer { get; }

        #endregion

        #region Ownership

        /// <summary>Gets the owner ID for a character.</summary>
        public string GetOwner(IConvaiCharacterAgent character);

        /// <summary>Gets all characters owned by a specific owner.</summary>
        public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId);

        /// <summary>Gets the count of characters owned by a specific owner.</summary>
        public int GetCharacterCountByOwner(string ownerId);

        /// <summary>Tries to find a character by its character ID (convenience method).</summary>
        public bool TryGetCharacterById(string characterId, out IConvaiCharacterAgent agent);

        #endregion

        #region Audio Binding

        /// <summary>Tries to get the AudioSource bound to a character.</summary>
        public bool TryGetAudioSource(string characterId, out AudioSource source);

        /// <summary>Sets the AudioSource for a character.</summary>
        public void SetAudioSource(string characterId, AudioSource source);

        #endregion

        #region Transport Binding (ParticipantId Mapping)

        /// <summary>Sets the transport participant ID for a character.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="participantId">The participant ID from the transport layer (e.g., SID).</param>
        public void SetParticipantId(string characterId, string participantId);

        /// <summary>Gets the transport participant ID for a character.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="participantId">The participant ID if found.</param>
        /// <returns>True if a participant ID is set for this character.</returns>
        public bool TryGetParticipantId(string characterId, out string participantId);

        /// <summary>Finds a character by its transport participant ID.</summary>
        /// <param name="participantId">The participant ID from the transport layer.</param>
        /// <param name="agent">The found agent, or null if not found.</param>
        /// <returns>True if a character was found with this participant ID.</returns>
        public bool TryGetCharacterByParticipantId(string participantId, out IConvaiCharacterAgent agent);

        /// <summary>Clears all transport-layer bindings (participant IDs and mute states).</summary>
        /// <remarks>Called on room disconnect to reset transport state without unregistering characters.</remarks>
        public void ClearTransportBindings();

        #endregion

        #region Mute State

        /// <summary>Sets the muted state for a character.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void SetCharacterMuted(string characterId, bool muted);

        /// <summary>Gets the muted state for a character.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <returns>True if the character is muted; false otherwise.</returns>
        public bool IsCharacterMuted(string characterId);

        #endregion

        #region Events

        /// <summary>Fired when a character is registered.</summary>
        public event Action<IConvaiCharacterAgent> CharacterRegistered;

        /// <summary>Fired when a character is unregistered.</summary>
        public event Action<IConvaiCharacterAgent> CharacterUnregistered;

        /// <summary>Fired when a player is registered.</summary>
        public event Action<IConvaiPlayerAgent> PlayerRegistered;

        #endregion
    }
}
