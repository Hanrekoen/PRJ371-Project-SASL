using Convai.Shared.Types;

namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Exposes character-specific lip sync capabilities used during room connection setup.
    /// </summary>
    public interface ILipSyncCapabilitySource
    {
        /// <summary>
        ///     Builds transport options for the current character rig.
        /// </summary>
        public bool TryGetLipSyncTransportOptions(out LipSyncTransportOptions options);
    }
}
