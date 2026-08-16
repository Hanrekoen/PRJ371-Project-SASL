using Convai.Domain.Abstractions;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Abstractions;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Typed dependency bundle for
    ///     <see cref="Convai.Runtime.Adapters.Networking.ConvaiRuntimeDiagnosticsService" />.
    ///     Replaces raw service-locator access with compile-time-safe typed properties.
    /// </summary>
    /// <remarks>
    ///     All dependencies are optional (diagnostics must never crash the runtime).
    /// </remarks>
    internal interface IConvaiDiagnosticsDependencies
    {
        /// <summary>Credential provider for API-key status reporting.</summary>
        public ICredentialProvider CredentialProvider { get; }

        /// <summary>End-user identity provider for identity status reporting.</summary>
        public IEndUserIdentityProvider IdentityProvider { get; }

        /// <summary>Session persistence service for persistence status reporting.</summary>
        public ISessionPersistence SessionPersistence { get; }

        /// <summary>Runtime settings service for current settings snapshot.</summary>
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <summary>Realtime transport accessor for transport type reporting.</summary>
        public IRealtimeTransportAccessor TransportAccessor { get; }
    }
}
