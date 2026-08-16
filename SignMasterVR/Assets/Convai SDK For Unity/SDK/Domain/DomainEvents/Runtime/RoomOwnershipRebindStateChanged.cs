using System;
using Convai.Domain.DomainEvents.Session;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Public-facing ownership rebind status used by room lifecycle events and facades.
    /// </summary>
    public enum RoomOwnershipRebindStatus
    {
        DeferredUntilStartup = 0,
        AppliedImmediately,
        PendingReconnect,
        RejectedTransitionState,
        RejectedInvalidOwnership
    }

    /// <summary>
    ///     Domain event raised when room ownership rebinding is evaluated or consumed.
    /// </summary>
    public readonly struct RoomOwnershipRebindStateChanged
    {
        public RoomOwnershipRebindStatus Outcome { get; }
        public bool HasPendingReconnect { get; }
        public SessionState SessionState { get; }
        public string ActiveCharacterId { get; }
        public string RequestedCharacterId { get; }
        public DateTime Timestamp { get; }

        public bool ReconnectRequired => HasPendingReconnect || Outcome == RoomOwnershipRebindStatus.PendingReconnect;

        public RoomOwnershipRebindStateChanged(
            RoomOwnershipRebindStatus outcome,
            bool hasPendingReconnect,
            SessionState sessionState,
            string activeCharacterId,
            string requestedCharacterId,
            DateTime timestamp)
        {
            Outcome = outcome;
            HasPendingReconnect = hasPendingReconnect;
            SessionState = sessionState;
            ActiveCharacterId = activeCharacterId ?? string.Empty;
            RequestedCharacterId = requestedCharacterId ?? string.Empty;
            Timestamp = timestamp;
        }

        public static RoomOwnershipRebindStateChanged Create(
            RoomOwnershipRebindStatus outcome,
            bool hasPendingReconnect,
            SessionState sessionState,
            string activeCharacterId,
            string requestedCharacterId) =>
            new(outcome, hasPendingReconnect, sessionState, activeCharacterId, requestedCharacterId, DateTime.UtcNow);
    }
}
