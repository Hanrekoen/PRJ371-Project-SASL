using Convai.Domain.Abstractions;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Abstractions;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Concrete implementation of <see cref="IConvaiDiagnosticsDependencies" />.
    ///     Created from the manager-owned service graph during runtime binding.
    /// </summary>
    internal sealed class ConvaiDiagnosticsDependencies : IConvaiDiagnosticsDependencies
    {
        /// <summary>
        ///     Creates a new diagnostics dependency bundle. All parameters are optional
        ///     because diagnostics must degrade gracefully when services are unavailable.
        /// </summary>
        public ConvaiDiagnosticsDependencies(
            ICredentialProvider credentialProvider = null,
            IEndUserIdentityProvider identityProvider = null,
            ISessionPersistence sessionPersistence = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null,
            IRealtimeTransportAccessor transportAccessor = null)
        {
            CredentialProvider = credentialProvider;
            IdentityProvider = identityProvider;
            SessionPersistence = sessionPersistence;
            RuntimeSettingsService = runtimeSettingsService;
            TransportAccessor = transportAccessor;
        }

        /// <inheritdoc />
        public ICredentialProvider CredentialProvider { get; private set; }

        /// <inheritdoc />
        public IEndUserIdentityProvider IdentityProvider { get; private set; }

        /// <inheritdoc />
        public ISessionPersistence SessionPersistence { get; }

        /// <inheritdoc />
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <inheritdoc />
        public IRealtimeTransportAccessor TransportAccessor { get; }

        internal void SetCredentialProvider(ICredentialProvider credentialProvider) =>
            CredentialProvider = credentialProvider;

        internal void SetIdentityProvider(IEndUserIdentityProvider identityProvider) =>
            IdentityProvider = identityProvider;
    }
}
