using System.Collections.Generic;
using Convai.Domain.Identity;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class MutableEndUserContextProvider : IEndUserIdentityProvider, IEndUserMetadataProvider
    {
        private IEndUserIdentityProvider _identityProvider;
        private IEndUserMetadataProvider _metadataProvider;

        public string GetEndUserId() => _identityProvider?.GetEndUserId();

        public IReadOnlyDictionary<string, object> GetEndUserMetadata() => _metadataProvider?.GetEndUserMetadata();

        public void SetProviders(
            IEndUserIdentityProvider identityProvider,
            IEndUserMetadataProvider metadataProvider)
        {
            _identityProvider = identityProvider;
            _metadataProvider = metadataProvider;
        }
    }
}
