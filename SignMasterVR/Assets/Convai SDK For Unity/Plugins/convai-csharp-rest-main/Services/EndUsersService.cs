#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Transport;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for end-user identity management APIs.
    /// </summary>
    public sealed class EndUsersService : ConvaiServiceBase
    {
        private const string GetEndUserEndpoint = "user/end-users/get";
        private const string UpdateEndUserEndpoint = "user/end-users/update";
        private const string ListEndUsersEndpoint = "user/end-users/list";
        private const string DeleteEndUserEndpoint = "user/end-users/delete";

        internal EndUsersService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        public async Task<EndUserDetails> GetAsync(
            string endUserId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "end_user_id", endUserId }
            };

            return await PostAsync<EndUserDetails>(
                GetEndUserEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<EndUsersListResponse> ListAsync(
            int limit = 50,
            string? cursor = null,
            string? activeAfter = null,
            string? activeBefore = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "limit", limit }
            };

            if (!string.IsNullOrWhiteSpace(cursor))
                requestBody["cursor"] = cursor;
            if (!string.IsNullOrWhiteSpace(activeAfter))
                requestBody["active_after"] = activeAfter;
            if (!string.IsNullOrWhiteSpace(activeBefore))
                requestBody["active_before"] = activeBefore;

            return await PostAsync<EndUsersListResponse>(
                ListEndUsersEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<EndUserUpdateResponse> UpdateMetadataAsync(
            string endUserId,
            IReadOnlyDictionary<string, object> metadataPatch,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "end_user_id", endUserId },
                { "end_user_metadata", new Dictionary<string, object>(metadataPatch) }
            };

            return await PostAsync<EndUserUpdateResponse>(
                UpdateEndUserEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<EndUserDeleteResponse> DeleteAsync(
            string endUserId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "end_user_id", endUserId }
            };

            return await PostAsync<EndUserDeleteResponse>(
                DeleteEndUserEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
