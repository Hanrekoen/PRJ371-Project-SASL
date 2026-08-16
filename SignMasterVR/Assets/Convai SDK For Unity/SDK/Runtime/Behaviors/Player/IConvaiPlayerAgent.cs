using System;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Unity-facing abstraction exposed to player behaviours so they can observe player context.
    /// </summary>
    public interface IConvaiPlayerAgent
    {
        /// <summary>
        ///     The configured player name.
        /// </summary>
        public string PlayerName { get; }

        /// <summary>
        ///     Local player identifier associated with the player.
        /// </summary>
        public string PlayerId { get; }

        /// <summary>
        ///     Gets the name tag color for transcript display.
        /// </summary>
        public Color NameTagColor { get; }

        /// <summary>
        ///     Sends a text message to the active Character.
        /// </summary>
        /// <param name="message">The text message to send.</param>
        public void SendTextMessage(string message);

        /// <summary>
        ///     Raised when a text message is sent via <see cref="SendTextMessage" />.
        ///     Subscribers (like ConvaiRoomManager) use this to send the message to the backend.
        /// </summary>
        public event Action<string> OnTextMessageSent;
    }
}
