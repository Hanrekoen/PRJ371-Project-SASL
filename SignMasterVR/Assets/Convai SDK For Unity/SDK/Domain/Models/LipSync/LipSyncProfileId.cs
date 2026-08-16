using System;

namespace Convai.Domain.Models.LipSync
{
    /// <summary>
    ///     Stable identifier for a lip sync rig profile.
    /// </summary>
    [Serializable]
    public readonly struct LipSyncProfileId : IEquatable<LipSyncProfileId>
    {
        public const string ARKitValue = "arkit";
        public const string MetaHumanValue = "metahuman";
        public const string Cc4ExtendedValue = "cc4_extended";

        public static readonly LipSyncProfileId ARKit = new(ARKitValue);
        public static readonly LipSyncProfileId MetaHuman = new(MetaHumanValue);
        public static readonly LipSyncProfileId Cc4Extended = new(Cc4ExtendedValue);

        private readonly string _value;

        public LipSyncProfileId(string value)
        {
            _value = Normalize(value);
        }

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            return value.Trim().ToLowerInvariant();
        }

        public bool Equals(LipSyncProfileId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LipSyncProfileId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;

        public static bool operator ==(LipSyncProfileId left, LipSyncProfileId right) => left.Equals(right);

        public static bool operator !=(LipSyncProfileId left, LipSyncProfileId right) => !left.Equals(right);
    }
}
