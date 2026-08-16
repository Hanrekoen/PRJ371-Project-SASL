namespace Convai.Infrastructure.Networking.Audio
{
    /// <summary>
    ///     State of audio routing for a participant.
    /// </summary>
    internal enum AudioRoutingState
    {
        /// <summary>Audio is not being routed.</summary>
        Stopped,

        /// <summary>Audio routing is starting (e.g., attaching to browser element).</summary>
        Starting,

        /// <summary>Audio is actively being routed to output.</summary>
        Active,

        /// <summary>Audio is muted but still attached.</summary>
        Muted,

        /// <summary>Audio routing failed.</summary>
        Error
    }
}
