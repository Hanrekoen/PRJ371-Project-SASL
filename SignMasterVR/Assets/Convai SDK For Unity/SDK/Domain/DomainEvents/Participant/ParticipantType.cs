namespace Convai.Domain.DomainEvents.Participant
{
    /// <summary>
    ///     Identifies the type of participant in a Convai session.
    /// </summary>
    public enum ParticipantType
    {
        /// <summary>
        ///     Unknown participant type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        ///     Local player (the user running the client).
        /// </summary>
        LocalPlayer = 1,

        /// <summary>
        ///     Remote player in a multiplayer session.
        /// </summary>
        RemotePlayer = 2,

        /// <summary>
        ///     AI character controlled by Convai.
        /// </summary>
        Character = 3
    }
}
