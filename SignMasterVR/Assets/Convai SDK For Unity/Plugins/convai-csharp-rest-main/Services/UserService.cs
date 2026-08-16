#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Transport;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for user-related API operations.
    /// </summary>
    public sealed class UserService : ConvaiServiceBase
    {
        private const string ReferralSourceStatusEndpoint = "user/referral-source-status";
        private const string UpdateReferralSourceEndpoint = "user/update-source";
        private const string UserApiUsageEndpoint = "user/user-api-usage";

        internal UserService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        /// <summary>
        /// Validates the API key and gets the referral source status.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The referral source status.</returns>
        public async Task<ReferralSourceStatus> ValidateApiKeyAsync(
            CancellationToken cancellationToken = default)
        {
            return await PostAsync<ReferralSourceStatus>(
                ReferralSourceStatusEndpoint,
                null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the referral source.
        /// </summary>
        /// <param name="source">The referral source to set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task UpdateReferralSourceAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "referral_source", source }
            };

            await PostVoidAsync(
                UpdateReferralSourceEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the API usage details for the current user.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The user usage data.</returns>
        public async Task<UserUsageData> GetUsageAsync(
            CancellationToken cancellationToken = default)
        {
            return await PostAsync<UserUsageData>(
                UserApiUsageEndpoint,
                new Dictionary<string, string>(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
