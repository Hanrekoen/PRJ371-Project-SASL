using Convai.Domain.Models.LipSync;

namespace Convai.Modules.LipSync.Profiles
{
    /// <summary>
    ///     The lipsync profiles the SDK ships with, defined in code rather than as assets.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These three used to be <c>.asset</c> files under the samples, reached through
    ///         <c>Resources.Load</c>. Each one's entire serialized content was the three strings
    ///         below, so the asset bought nothing and could fail: a project that had not imported a
    ///         sample got no profiles at all, and the character silently stopped lip-syncing. Built
    ///         here, they are always present.
    ///     </para>
    ///     <para>
    ///         Adding a profile is a deliberate change to what the SDK claims to support: the
    ///         transport format has to exist on the backend and
    ///         <see cref="LipSyncBuiltInProfileLibrary" /> has to carry its source blendshape
    ///         catalog. Customers extend the catalog instead by dropping their own
    ///         <see cref="ConvaiLipSyncProfileRegistry" /> into any
    ///         <c>Resources/LipSync/ProfileRegistries/</c> folder — that path is unaffected by this
    ///         type and must stay that way.
    ///     </para>
    /// </remarks>
    internal static class LipSyncBuiltInProfiles
    {
        /// <summary>
        ///     Creates a fresh instance of every built-in profile. The caller owns what it gets back
        ///     and is responsible for destroying the instances.
        /// </summary>
        internal static ConvaiLipSyncProfile[] CreateAll()
        {
            return new[]
            {
                // Transport format matches the profile id for these two.
                ConvaiLipSyncProfile.CreateBuiltIn(
                    LipSyncProfileId.ARKitValue, "ARKit", LipSyncProfileId.ARKitValue),
                ConvaiLipSyncProfile.CreateBuiltIn(
                    LipSyncProfileId.Cc4ExtendedValue, "CC4 Extended", LipSyncProfileId.Cc4ExtendedValue),

                // MetaHuman is the one built-in whose wire format is not its profile id: the
                // backend has always sent "mha". LipSyncProfileCatalog additionally accepts "mha"
                // as a legacy alias for the profile id itself.
                ConvaiLipSyncProfile.CreateBuiltIn(
                    LipSyncProfileId.MetaHumanValue, "MetaHuman", "mha")
            };
        }
    }
}
