using Convai.Domain.Models.LipSync;
using UnityEngine;

namespace Convai.Modules.LipSync.Profiles
{
    [CreateAssetMenu(fileName = "ConvaiLipSyncProfile", menuName = "Convai/Lip Sync/Profile")]
    public sealed class ConvaiLipSyncProfile : ScriptableObject
    {
        [SerializeField] private string _profileId = LipSyncProfileId.ARKitValue;
        [SerializeField] private string _displayName = "ARKit";
        [SerializeField] private string _transportFormat = "arkit";

        public LipSyncProfileId ProfileId => new(_profileId);
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? ProfileId.Value : _displayName.Trim();
        public string TransportFormat => LipSyncProfileId.Normalize(_transportFormat);

        public bool IsValid =>
            ProfileId.IsValid &&
            !string.IsNullOrWhiteSpace(TransportFormat);

        /// <summary>
        ///     Builds one of the SDK's built-in profiles in memory, without an asset behind it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The built-in profiles used to ship as three <c>.asset</c> files whose entire
        ///         serialized content was these three strings, loaded through <c>Resources.Load</c>
        ///         out of the samples. That load could fail — stripped, renamed, shadowed, or simply
        ///         a project that never imported a sample — and when it did the catalog registered
        ///         no profiles at all and the character silently stopped lip-syncing, while the
        ///         correct values sat in the source the whole time. Built in code, they cannot be
        ///         missing. This mirrors <c>ConvaiFacialCompositionProfile.CreateDefault</c>, which
        ///         replaced the same arrangement for the same reason.
        ///     </para>
        ///     <para>
        ///         Flagged <see cref="HideFlags.HideAndDontSave" /> so a runtime-owned profile can
        ///         never be saved into a scene. The caller owns the instance and must destroy it.
        ///     </para>
        /// </remarks>
        internal static ConvaiLipSyncProfile CreateBuiltIn(
            string profileId, string displayName, string transportFormat)
        {
            ConvaiLipSyncProfile instance = CreateInstance<ConvaiLipSyncProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = displayName;
            instance._profileId = LipSyncProfileId.Normalize(profileId);
            instance._displayName = displayName;
            instance._transportFormat = LipSyncProfileId.Normalize(transportFormat);
            return instance;
        }

        private void OnValidate()
        {
            _profileId = LipSyncProfileId.Normalize(_profileId);
            _transportFormat = LipSyncProfileId.Normalize(_transportFormat);
            _displayName = string.IsNullOrWhiteSpace(_displayName) ? _profileId : _displayName.Trim();
        }

        public string DescribeValidationIssue()
        {
            if (!ProfileId.IsValid)
                return "ProfileId is empty.";
            if (string.IsNullOrWhiteSpace(TransportFormat))
                return "TransportFormat is empty.";
            return string.Empty;
        }
    }
}
