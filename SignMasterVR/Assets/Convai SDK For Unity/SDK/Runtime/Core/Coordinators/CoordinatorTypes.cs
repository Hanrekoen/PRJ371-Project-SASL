using System;
using System.Collections.Generic;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Immutable room session information.
    ///     Used for both single-player sessions and multi-participant rooms.
    /// </summary>
    public readonly struct RoomSession
    {
        /// <summary>Creates a new room session.</summary>
        public RoomSession(string roomId, string roomCode = null, string ownerId = null, DateTime? createdAt = null)
        {
            RoomId = roomId ?? string.Empty;
            RoomCode = roomCode ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
            CreatedAt = createdAt ?? DateTime.UtcNow;
        }

        /// <summary>Internal room identifier.</summary>
        public string RoomId { get; }

        /// <summary>Human-readable room code for sharing (multi-participant).</summary>
        public string RoomCode { get; }

        /// <summary>Owner ID of the room creator.</summary>
        public string OwnerId { get; }

        /// <summary>When the session was created.</summary>
        public DateTime CreatedAt { get; }

        /// <summary>Whether this is a valid session.</summary>
        public bool IsValid => !string.IsNullOrEmpty(RoomId);

        /// <summary>Creates an empty/invalid session.</summary>
        public static RoomSession Empty => new(string.Empty);
    }

    /// <summary>
    ///     Options for creating a new room (multi-participant support).
    /// </summary>
    public readonly struct RoomCreateOptions
    {
        /// <summary>Creates room creation options.</summary>
        public RoomCreateOptions(string roomName, int maxParticipants = 10, IReadOnlyList<string> characterIds = null)
        {
            RoomName = roomName ?? string.Empty;
            MaxParticipants = maxParticipants;
            CharacterIds = characterIds ?? Array.Empty<string>();
        }

        /// <summary>Display name for the room.</summary>
        public string RoomName { get; }

        /// <summary>Maximum number of participants allowed.</summary>
        public int MaxParticipants { get; }

        /// <summary>Character IDs to activate in this room.</summary>
        public IReadOnlyList<string> CharacterIds { get; }
    }

    /// <summary>
    ///     Result of attempting to join a room.
    /// </summary>
    public readonly struct JoinResult
    {
        private JoinResult(bool success, string errorMessage, RoomSession session)
        {
            Success = success;
            ErrorMessage = errorMessage ?? string.Empty;
            Session = session;
        }

        /// <summary>Whether the join was successful.</summary>
        public bool Success { get; }

        /// <summary>Error message if join failed.</summary>
        public string ErrorMessage { get; }

        /// <summary>The session if join succeeded.</summary>
        public RoomSession Session { get; }

        /// <summary>Creates a successful join result.</summary>
        public static JoinResult Succeeded(RoomSession session) => new(true, null, session);

        /// <summary>Creates a failed join result.</summary>
        public static JoinResult Failed(string error) => new(false, error, RoomSession.Empty);
    }

    /// <summary>
    ///     Diagnostic information snapshot about a room session.
    /// </summary>
    public readonly struct RoomDiagnosticsSnapshot
    {
        /// <summary>Creates a diagnostics snapshot.</summary>
        public RoomDiagnosticsSnapshot(
            string currentState,
            int totalConnectionAttempts,
            int successfulConnections,
            int failedConnections,
            int totalErrors,
            DateTime? lastConnectedAt,
            DateTime? lastErrorAt,
            string lastErrorCode,
            string lastErrorMessage,
            TimeSpan? sessionUptime,
            int registeredCharacterCount,
            int registeredPlayerCount)
        {
            CurrentState = currentState ?? string.Empty;
            TotalConnectionAttempts = totalConnectionAttempts;
            SuccessfulConnections = successfulConnections;
            FailedConnections = failedConnections;
            TotalErrors = totalErrors;
            LastConnectedAt = lastConnectedAt;
            LastErrorAt = lastErrorAt;
            LastErrorCode = lastErrorCode ?? string.Empty;
            LastErrorMessage = lastErrorMessage ?? string.Empty;
            SessionUptime = sessionUptime;
            RegisteredCharacterCount = registeredCharacterCount;
            RegisteredPlayerCount = registeredPlayerCount;
        }

        /// <summary>Current session state name.</summary>
        public string CurrentState { get; }

        /// <summary>Total connection attempts since startup.</summary>
        public int TotalConnectionAttempts { get; }

        /// <summary>Successful connection attempts.</summary>
        public int SuccessfulConnections { get; }

        /// <summary>Failed connection attempts.</summary>
        public int FailedConnections { get; }

        /// <summary>Total errors recorded.</summary>
        public int TotalErrors { get; }

        /// <summary>Time of last successful connection.</summary>
        public DateTime? LastConnectedAt { get; }

        /// <summary>Time of last error.</summary>
        public DateTime? LastErrorAt { get; }

        /// <summary>Last error code, if any.</summary>
        public string LastErrorCode { get; }

        /// <summary>Last error message, if any.</summary>
        public string LastErrorMessage { get; }

        /// <summary>Current session uptime if connected.</summary>
        public TimeSpan? SessionUptime { get; }

        /// <summary>Number of registered characters.</summary>
        public int RegisteredCharacterCount { get; }

        /// <summary>Number of registered players.</summary>
        public int RegisteredPlayerCount { get; }
    }

    /// <summary>
    ///     Unit type for async operations that return no value.
    ///     Equivalent to void but usable as a generic type argument.
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        /// <summary>The singleton Unit value.</summary>
        public static readonly Unit Value = new();

        public bool Equals(Unit other) => true;
        public override bool Equals(object obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
        public override string ToString() => "()";
    }
}
