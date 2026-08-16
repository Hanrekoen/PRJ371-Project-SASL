#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for character-related API operations.
    /// </summary>
    public sealed class CharacterService : ConvaiServiceBase
    {
        public const string ProductionCharacterGetUrl = "https://api.convai.com/character/get";
        private const string CharacterGetEndpoint = "character/get";
        private const string CharacterUpdateEndpoint = "character/update";

        internal CharacterService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        /// <summary>
        /// Gets the details of a character by ID.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The character details.</returns>
        public async Task<CharacterDetails> GetDetailsAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "charID", characterId }
            };
            Uri requestUri = BuildUrl(CharacterGetEndpoint);

            JObject response = await PostAsync<JObject>(
                CharacterGetEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            try
            {
                JToken detailsToken = response["response"] ?? response;
                CharacterDetails? details = detailsToken.ToObject<CharacterDetails>();
                if (details == null)
                {
                    throw ConvaiRestException.ParseError(
                        $"Failed to deserialize response to {nameof(CharacterDetails)}: result was null",
                        requestUri,
                        response.ToString(Formatting.None));
                }

                return details;
            }
            catch (JsonException ex)
            {
                throw ConvaiRestException.ParseError(
                    $"Failed to deserialize response to {nameof(CharacterDetails)}: {ex.Message}",
                    requestUri,
                    response.ToString(Formatting.None),
                    ex);
            }
        }

        /// <summary>
        /// Updates character settings.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="updateData">The update data object (will be serialized to JSON).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task UpdateAsync(
            string characterId,
            object updateData,
            CancellationToken cancellationToken = default)
        {
            JObject requestBody = updateData == null
                ? new JObject()
                : JObject.FromObject(updateData);
            requestBody["charID"] = characterId;

            await PostVoidAsync(
                CharacterUpdateEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets whether long-term memory is enabled for the character.
        /// </summary>
        public async Task<bool> GetMemoryEnabledAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            CharacterDetails details = await GetDetailsAsync(characterId, cancellationToken).ConfigureAwait(false);
            return details.MemorySettings?.IsEnabled ?? false;
        }

        /// <summary>
        /// Enables or disables long-term memory for the character.
        /// </summary>
        public async Task SetMemoryEnabledAsync(
            string characterId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new CharacterUpdateRequest(characterId, enabled);
            await PostVoidAsync(
                CharacterUpdateEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
