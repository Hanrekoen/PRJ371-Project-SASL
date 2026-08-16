using System;
using UnityEngine;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Process-local registration point for a developer-supplied auth-token provider.</summary>
    /// <remarks>
    ///     Register a provider before the first connection attempt. The runtime looks up the current registration
    ///     lazily, so registration from an ordinary scene <c>Awake</c> callback is supported.
    /// </remarks>
    public static class ConvaiAuthTokenProviderRegistry
    {
        private static readonly object Sync = new();
        private static IConvaiAuthTokenProvider _provider;

        /// <summary>Whether a custom auth-token provider is currently registered.</summary>
        public static bool IsRegistered
        {
            get
            {
                lock (Sync)
                    return _provider != null;
            }
        }

        /// <summary>Registers or replaces the custom provider used by subsequent connection attempts.</summary>
        /// <param name="provider">Provider to register.</param>
        public static void Register(IConvaiAuthTokenProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            lock (Sync)
                _provider = provider;
        }

        /// <summary>
        ///     Unregisters <paramref name="provider" /> when it is still the active registration.
        /// </summary>
        /// <returns>True when the active registration was removed.</returns>
        public static bool Unregister(IConvaiAuthTokenProvider provider)
        {
            if (provider == null) return false;

            lock (Sync)
            {
                if (!ReferenceEquals(_provider, provider))
                    return false;

                _provider = null;
                return true;
            }
        }

        /// <summary>Unregisters whichever custom provider is currently active.</summary>
        public static void Unregister() => Clear();

        /// <summary>Clears the current custom provider registration.</summary>
        public static void Clear()
        {
            lock (Sync)
                _provider = null;
        }

        /// <summary>Attempts to read the provider active at the time of the call.</summary>
        internal static bool TryGetProvider(out IConvaiAuthTokenProvider provider)
        {
            lock (Sync)
            {
                provider = _provider;
                return provider != null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemRegistration() => Clear();
    }
}
