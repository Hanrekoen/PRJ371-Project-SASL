using System;
using Convai.RestAPI;
using Convai.RestAPI.Services;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Provides configuration settings for Convai transport connections.
    /// </summary>
    /// <remarks>
    ///     This composite interface inherits from <see cref="ITransportConfiguration" />
    ///     for read-only config access and adds session-persistence mutations.
    ///     Consumers that only need to read configuration should depend on
    ///     <see cref="ITransportConfiguration" /> instead.
    /// </remarks>
    public interface IConfigurationProvider : ITransportConfiguration
    {
        /// <summary>
        ///     Stores a character session ID for session resume functionality.
        /// </summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="sessionId">The session ID to store.</param>
        public void StoreCharacterSessionId(string characterId, string sessionId);

        /// <summary>
        ///     Retrieves a stored character session ID.
        /// </summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <returns>The stored session ID, or null if not found.</returns>
        public string GetCharacterSessionId(string characterId);

        /// <summary>
        ///     Clears the stored session ID for a specific character.
        /// </summary>
        /// <param name="characterId">The character's unique identifier.</param>
        public void ClearCharacterSessionId(string characterId);

        /// <summary>
        ///     Clears all stored character session IDs.
        /// </summary>
        public void ClearAllCharacterSessionIds();
    }

    internal static class RoomConnectionRequestLipSyncMapper
    {
        internal static void Apply(RoomConnectionRequest roomRequest, in LipSyncTransportOptions options)
        {
            if (roomRequest == null) throw new ArgumentNullException(nameof(roomRequest));

            if (!options.IsValid)
            {
                roomRequest.BlendshapeProvider = null;
                roomRequest.BlendshapeConfig = null;
                return;
            }

            roomRequest.BlendshapeProvider = options.Provider;
            roomRequest.BlendshapeConfig = new ConvaiBlendshapeConfig(
                options.EnableChunking,
                options.ChunkSize,
                options.OutputFps,
                options.Format,
                options.FramesBufferDuration,
                options.DeliverChunksAhead);
        }
    }
}
