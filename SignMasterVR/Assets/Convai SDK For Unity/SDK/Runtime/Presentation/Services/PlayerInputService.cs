using System;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Implementation of IPlayerInputService.
    ///     Manages the player agent reference for transcript UI components.
    /// </summary>
    internal class PlayerInputService : IPlayerInputService, IDisposable
    {
        /// <summary>
        ///     Disposes resources.
        /// </summary>
        public void Dispose() => Player = null;

        /// <inheritdoc />
        public IConvaiPlayerAgent Player { get; private set; }

        /// <inheritdoc />
        public bool HasPlayer => Player != null;

        /// <inheritdoc />
        public void SetPlayer(IConvaiPlayerAgent player) => Player = player;

        /// <inheritdoc />
        public void ClearPlayer() => Player = null;
    }
}
