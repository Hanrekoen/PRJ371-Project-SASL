using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Canonical internal identity for one indexed lip-sync response.
    /// </summary>
    internal readonly struct LipSyncResponseOwner
    {
        private readonly string _responseId;
        private readonly string _canonicalKey;

        internal LipSyncResponseOwner(
            string responseId,
            int? turnId,
            int? epoch,
            int? sequence = null)
        {
            _responseId = responseId?.Trim() ?? string.Empty;
            TurnId = turnId;
            Epoch = epoch;
            Sequence = sequence;
            _canonicalKey = BuildCanonicalKey(_responseId, TurnId, Epoch);
        }

        internal string ResponseId => _responseId ?? string.Empty;
        internal int? TurnId { get; }
        internal int? Epoch { get; }
        internal int? Sequence { get; }
        internal bool HasIdentity => !string.IsNullOrEmpty(_responseId) || TurnId.HasValue;
        internal string CanonicalKey => _canonicalKey ?? string.Empty;

        /// <summary>
        ///     Applies response, then turn-and-epoch, then turn-only identity precedence.
        ///     Missing stronger identity on either side never falls through to a looser match.
        /// </summary>
        internal bool Matches(in LipSyncResponseOwner other)
        {
            bool bothHaveResponse = ResponseId.Length > 0 && other.ResponseId.Length > 0;
            if (bothHaveResponse)
                return string.Equals(ResponseId, other.ResponseId, StringComparison.Ordinal);

            bool bothHaveTurnAndEpoch = TurnId.HasValue && other.TurnId.HasValue &&
                                        Epoch.HasValue && other.Epoch.HasValue;
            if (bothHaveTurnAndEpoch)
                return TurnId.Value == other.TurnId.Value && Epoch.Value == other.Epoch.Value;

            bool bothOmitResponseAndEpoch = ResponseId.Length == 0 && other.ResponseId.Length == 0 &&
                                            !Epoch.HasValue && !other.Epoch.HasValue;
            return bothOmitResponseAndEpoch && TurnId.HasValue && other.TurnId.HasValue &&
                   TurnId.Value == other.TurnId.Value;
        }

        private static string BuildCanonicalKey(string responseId, int? turnId, int? epoch)
        {
            if (responseId.Length > 0) return $"response:{responseId}";
            if (turnId.HasValue && epoch.HasValue) return $"turn:{turnId.Value}:epoch:{epoch.Value}";
            if (turnId.HasValue) return $"turn:{turnId.Value}";
            return string.Empty;
        }
    }
}
