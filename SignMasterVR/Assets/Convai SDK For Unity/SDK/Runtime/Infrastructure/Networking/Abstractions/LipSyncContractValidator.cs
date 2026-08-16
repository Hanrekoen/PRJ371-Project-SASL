using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    internal static class LipSyncContractValidator
    {
        public static bool TryValidateTransportOptions(
            in LipSyncTransportOptions options,
            out string dropReasonCode)
        {
            if (!options.IsValid)
            {
                dropReasonCode = "lipsync.parse.transport_options_invalid";
                return false;
            }

            dropReasonCode = string.Empty;
            return true;
        }
    }
}
