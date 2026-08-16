using System;
using System.Collections.Generic;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Privacy-safe summary of one action-response filter pass. Used by editor diagnostics;
    ///     intentionally excludes the raw backend action payload.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Drops" /> is what makes this answerable rather than merely countable. The
    ///         counts say a command was rejected and for which category; the reports say which
    ///         command, what it asked for, and what to do about it — which is the whole difference
    ///         between a diagnostic and a tally.
    ///     </para>
    ///     <para>
    ///         The list is empty when nothing was listening at the time the batch was filtered, so a
    ///         consumer must treat "no reports" as "not gathered", never as "nothing went wrong" —
    ///         <see cref="RejectedCount" /> remains the authority on whether anything was dropped.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiActionResponseFilterDiagnostic
    {
        private static readonly Dictionary<string, int> NoCounts = new();
        private static readonly IReadOnlyList<ConvaiActionDropReport> NoDrops =
            Array.Empty<ConvaiActionDropReport>();

        private ConvaiActionResponseFilterDiagnostic(
            string characterId,
            string participantId,
            int receivedCount,
            int acceptedCount,
            int rejectedCount,
            IReadOnlyDictionary<string, int> rejectedByReason,
            IReadOnlyList<ConvaiActionDropReport> drops,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            ReceivedCount = receivedCount;
            AcceptedCount = acceptedCount;
            RejectedCount = rejectedCount;
            RejectedByReason = rejectedByReason ?? NoCounts;
            Drops = drops ?? NoDrops;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public int ReceivedCount { get; }
        public int AcceptedCount { get; }
        public int RejectedCount { get; }
        public IReadOnlyDictionary<string, int> RejectedByReason { get; }

        /// <summary>Why each dropped command was dropped; empty when nobody was gathering detail.</summary>
        public IReadOnlyList<ConvaiActionDropReport> Drops { get; }

        public DateTime Timestamp { get; }

        public static ConvaiActionResponseFilterDiagnostic Create(
            string characterId,
            string participantId,
            int acceptedCount,
            ConvaiActionDropCollector drops)
        {
            int rejectedCount = drops?.DroppedCount ?? 0;
            return new ConvaiActionResponseFilterDiagnostic(
                characterId,
                participantId,
                acceptedCount + rejectedCount,
                acceptedCount,
                rejectedCount,
                drops == null ? NoCounts : new Dictionary<string, int>(drops.CountsByReason),
                drops?.Reports,
                DateTime.UtcNow);
        }
    }

    /// <summary>Read-only editor diagnostic for one pending runtime action-state update.</summary>
    internal readonly struct ConvaiRuntimeActionUpdateDebugInfo
    {
        public ConvaiRuntimeActionUpdateDebugInfo(
            string updateId,
            DateTime sentAtUtc,
            bool mutatesActionConfig,
            bool mutatesTopLevelAttention,
            bool hasAcknowledgement,
            string acknowledgementStatus)
        {
            UpdateId = updateId ?? string.Empty;
            SentAtUtc = sentAtUtc;
            MutatesActionConfig = mutatesActionConfig;
            MutatesTopLevelAttention = mutatesTopLevelAttention;
            HasAcknowledgement = hasAcknowledgement;
            AcknowledgementStatus = acknowledgementStatus ?? string.Empty;
        }

        public string UpdateId { get; }
        public DateTime SentAtUtc { get; }
        public bool MutatesActionConfig { get; }
        public bool MutatesTopLevelAttention { get; }
        public bool HasAcknowledgement { get; }
        public string AcknowledgementStatus { get; }
    }
}
