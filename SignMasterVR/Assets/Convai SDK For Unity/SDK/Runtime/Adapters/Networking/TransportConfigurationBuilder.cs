using System;
using System.Collections.Generic;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Types;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Transport configuration backed by ICredentialProvider.
    ///     Replaces the obsolete ConfigurationProviderAdapter.
    /// </summary>
    internal sealed class TransportConfigurationBuilder
    {
        private ConvaiConnectionType _connectionType = ConvaiConnectionType.Audio;
        private ICredentialProvider _credentialProvider;
        private bool _debug;
        private IEndUserIdentityProvider _endUserIdentityProvider;
        private IEndUserMetadataProvider _endUserMetadataProvider;
        private LipSyncTransportOptions _lipSyncTransportOptions = LipSyncTransportOptions.Disabled;
        private ConvaiServerEndpoint _serverEndpoint = ConvaiServerEndpoint.Connect;
        private string _videoTrackName = VideoPublishOptions.Default.TrackName;

        public TransportConfigurationBuilder WithCredentialProvider(ICredentialProvider provider)
        {
            _credentialProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        public TransportConfigurationBuilder WithEndUserIdentityProvider(IEndUserIdentityProvider provider)
        {
            _endUserIdentityProvider = provider;
            return this;
        }

        public TransportConfigurationBuilder WithEndUserMetadataProvider(IEndUserMetadataProvider provider)
        {
            _endUserMetadataProvider = provider;
            return this;
        }

        public TransportConfigurationBuilder WithConnectionType(ConvaiConnectionType connectionType)
        {
            _connectionType = connectionType;
            return this;
        }

        public TransportConfigurationBuilder WithVideoTrackName(string videoTrackName)
        {
            _videoTrackName = videoTrackName;
            return this;
        }

        public TransportConfigurationBuilder WithServerEndpoint(ConvaiServerEndpoint serverEndpoint)
        {
            _serverEndpoint = serverEndpoint;
            return this;
        }

        public TransportConfigurationBuilder WithLipSyncTransportOptions(LipSyncTransportOptions options)
        {
            _lipSyncTransportOptions = options;
            return this;
        }

        public TransportConfigurationBuilder WithDebug(bool debug)
        {
            _debug = debug;
            return this;
        }

        public ITransportConfiguration Build()
        {
            if (_credentialProvider == null)
                throw new InvalidOperationException("CredentialProvider is required");

            return new TransportConfiguration(
                _credentialProvider,
                _endUserIdentityProvider,
                _endUserMetadataProvider,
                _connectionType,
                _videoTrackName,
                _serverEndpoint,
                _lipSyncTransportOptions,
                _debug);
        }

        private sealed class TransportConfiguration :
            ITransportConfiguration,
            ITransportAuthenticationConfiguration
        {
            private readonly ICredentialProvider _credentialProvider;
            private readonly IEndUserIdentityProvider _endUserIdentityProvider;
            private readonly IEndUserMetadataProvider _endUserMetadataProvider;

            public TransportConfiguration(
                ICredentialProvider credentialProvider,
                IEndUserIdentityProvider endUserIdentityProvider,
                IEndUserMetadataProvider endUserMetadataProvider,
                ConvaiConnectionType connectionType,
                string videoTrackName,
                ConvaiServerEndpoint serverEndpoint,
                LipSyncTransportOptions lipSyncTransportOptions,
                bool debug)
            {
                _credentialProvider = credentialProvider;
                _endUserIdentityProvider = endUserIdentityProvider;
                _endUserMetadataProvider = endUserMetadataProvider;
                ConnectionType = connectionType;
                VideoTrackName = videoTrackName;
                ServerEndpoint = serverEndpoint;
                LipSyncTransportOptions = lipSyncTransportOptions;
                Debug = debug;
            }

            public string ApiKey => _credentialProvider.GetApiKey() ?? string.Empty;
            public bool UsesAuthToken => _credentialProvider is AuthTokenCredentialProvider;
            public string CoreServerUrl => _credentialProvider.GetServerUrl() ?? string.Empty;
            public ConvaiConnectionType ConnectionType { get; }
            public string VideoTrackName { get; }
            public ConvaiServerEndpoint ServerEndpoint { get; }
            public LipSyncTransportOptions LipSyncTransportOptions { get; }
            public string EndUserId => _endUserIdentityProvider?.GetEndUserId();

            public IReadOnlyDictionary<string, object> EndUserMetadata =>
                _endUserMetadataProvider?.GetEndUserMetadata();

            public bool Debug { get; }
        }
    }
}
