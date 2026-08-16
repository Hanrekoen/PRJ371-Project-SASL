using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Service for accessing player input capabilities.
    ///     Decouples UI components from direct ConvaiPlayer reference.
    /// </summary>
    /// <remarks>
    ///     Provides transcript UIs with access to the registered player agent.
    /// </remarks>
    public interface IPlayerInputService
    {
        /// <summary>
        ///     Gets the player agent interface.
        ///     May be null if no player has been registered.
        /// </summary>
        public IConvaiPlayerAgent Player { get; }

        /// <summary>
        ///     Gets whether a player agent has been registered.
        /// </summary>
        public bool HasPlayer { get; }

        /// <summary>
        ///     Sets the player agent (called during scene initialization).
        /// </summary>
        /// <param name="player">The player agent to register.</param>
        public void SetPlayer(IConvaiPlayerAgent player);

        /// <summary>
        ///     Clears the player agent reference.
        /// </summary>
        public void ClearPlayer();
    }
}
