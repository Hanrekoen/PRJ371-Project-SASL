using System.Threading;
using System.Threading.Tasks;
using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Focused interface for room connection lifecycle (connect/disconnect).
    ///     Extracted from <see cref="IConvaiRoomController" /> so orchestration code can
    ///     depend only on connection logic.
    /// </summary>
    public interface IRoomConnectionInitializer
    {
        /// <summary>
        ///     Initializes the room connection process.
        /// </summary>
        /// <param name="connectionType">Connection type (audio/video).</param>
        /// <param name="coreServerUrl">Core server URL.</param>
        /// <param name="characterId">Character ID to connect to.</param>
        /// <param name="storedSessionId">Optional stored session ID for resumption.</param>
        /// <param name="enableSessionResume">Whether to enable session resume.</param>
        /// <param name="dynamicInfoText">Initial dynamic info text for the connection request.</param>
        /// <param name="keepDynamicInfoInContext">Whether dynamic info should be kept in context by backend.</param>
        /// <returns>The structured result of the initialization attempt.</returns>
        public Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext);

        /// <summary>
        ///     Initializes the room connection process with join options.
        /// </summary>
        /// <param name="connectionType">Connection type (audio/video).</param>
        /// <param name="coreServerUrl">Core server URL.</param>
        /// <param name="characterId">Character ID to connect to.</param>
        /// <param name="storedSessionId">Optional stored session ID for resumption.</param>
        /// <param name="enableSessionResume">Whether to enable session resume.</param>
        /// <param name="dynamicInfoText">Initial dynamic info text for the connection request.</param>
        /// <param name="keepDynamicInfoInContext">Whether dynamic info should be kept in context by backend.</param>
        /// <param name="joinOptions">Optional room join options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The structured result of the initialization attempt.</returns>
        public Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Disconnects from the current room.
        /// </summary>
        public void DisconnectFromRoom();

        /// <summary>
        ///     Disconnects from the current room asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default);
    }
}
