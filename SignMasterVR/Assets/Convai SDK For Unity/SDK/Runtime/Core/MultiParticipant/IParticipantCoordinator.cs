using System;
using System.Collections.Generic;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Coordinates participants in a multi-participant room session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface is scaffolded with stubs for future multi-participant support.
    ///     </para>
    ///     <para>
    ///         <b>Single-Player Mode</b>: In single-player mode, the local participant
    ///         is the only participant and is always the room owner.
    ///     </para>
    /// </remarks>
    public interface IParticipantCoordinator
    {
        #region Local Participant

        /// <summary>
        ///     Information about the local participant.
        /// </summary>
        public ParticipantInfo LocalParticipant { get; }

        /// <summary>
        ///     Whether the local participant is the room owner.
        /// </summary>
        public bool IsLocalOwner { get; }

        #endregion

        #region Participant List

        /// <summary>
        ///     All participants currently in the room.
        /// </summary>
        public IReadOnlyList<ParticipantInfo> Participants { get; }

        /// <summary>
        ///     Number of participants in the room.
        /// </summary>
        public int ParticipantCount { get; }

        /// <summary>
        ///     Tries to get a participant by their ID.
        /// </summary>
        public bool TryGetParticipant(string participantId, out ParticipantInfo participant);

        #endregion

        #region Events

        /// <summary>
        ///     Fired when a participant joins the room.
        /// </summary>
        public event Action<ParticipantJoined> ParticipantJoined;

        /// <summary>
        ///     Fired when a participant leaves the room.
        /// </summary>
        public event Action<ParticipantLeft> ParticipantLeft;

        #endregion
    }
}
