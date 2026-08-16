using Convai.Infrastructure.Networking.Transport;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    internal static class NativeTransportPlatformResolver
    {
        internal static TransportCapabilities GetCapabilities()
        {
            // In the Unity Editor we always want native desktop behavior, even if the active build target is mobile.
            if (UnityEngine.Application.isEditor)
                return TransportCapabilities.Native(isMobile: false);

            return TransportCapabilities.Native(IsMobileRuntimePlatform(UnityEngine.Application.platform));
        }

        private static bool IsMobileRuntimePlatform(RuntimePlatform platform) =>
            platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer;
    }
}
