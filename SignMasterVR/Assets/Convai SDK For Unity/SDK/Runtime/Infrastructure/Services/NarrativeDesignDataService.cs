using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.RestAPI;
using Convai.RestAPI.Internal.Models;
using Convai.Runtime;

namespace Convai.Infrastructure.Services
{
    /// <summary>
    ///     Infrastructure service for fetching narrative design data from the Convai backend.
    ///     Implements the domain abstraction using ConvaiRestClient operations.
    /// </summary>
    public class NarrativeDesignDataService : INarrativeDesignDataService
    {
        private readonly Func<string> _apiKeyProvider;

        /// <summary>
        ///     Creates a new instance of NarrativeDesignDataService.
        /// </summary>
        /// <param name="apiKeyProvider">Function to retrieve the API key at runtime.</param>
        public NarrativeDesignDataService(Func<string> apiKeyProvider)
        {
            _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        }

        /// <inheritdoc />
        public async Task<NarrativeFetchResult<List<NarrativeSectionInfo>>> FetchSectionsAsync(string characterId,
            string apiKey = null)
        {
            if (string.IsNullOrEmpty(characterId))
                return NarrativeFetchResult<List<NarrativeSectionInfo>>.Failed("Character ID is required.");

            string key = apiKey ?? _apiKeyProvider();
            if (string.IsNullOrEmpty(key))
            {
                return NarrativeFetchResult<List<NarrativeSectionInfo>>.Failed(
                    "API key is not configured. Please set it in Project Settings > Convai SDK.");
            }

            try
            {
                var options = ConvaiRestOptionsFactory.Create(key);
                using var client = new ConvaiRestClient(options);
                IReadOnlyList<SectionData> sections = await client.Narrative.GetSectionsAsync(characterId);

                var result = new List<NarrativeSectionInfo>();
                foreach (SectionData section in sections)
                    result.Add(new NarrativeSectionInfo(section.SectionId, section.SectionName));
                return NarrativeFetchResult<List<NarrativeSectionInfo>>.Succeeded(result);
            }
            catch (Exception ex)
            {
                return NarrativeFetchResult<List<NarrativeSectionInfo>>.Failed($"Exception: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<NarrativeFetchResult<List<NarrativeTriggerInfo>>> FetchTriggersAsync(string characterId,
            string apiKey = null)
        {
            if (string.IsNullOrEmpty(characterId))
                return NarrativeFetchResult<List<NarrativeTriggerInfo>>.Failed("Character ID is required.");

            string key = apiKey ?? _apiKeyProvider();
            if (string.IsNullOrEmpty(key))
            {
                return NarrativeFetchResult<List<NarrativeTriggerInfo>>.Failed(
                    "API key is not configured. Please set it in Project Settings > Convai SDK.");
            }

            try
            {
                var options = ConvaiRestOptionsFactory.Create(key);
                using var client = new ConvaiRestClient(options);
                IReadOnlyList<TriggerData> triggers = await client.Narrative.GetTriggersAsync(characterId);

                var result = new List<NarrativeTriggerInfo>();
                foreach (TriggerData trigger in triggers)
                {
                    result.Add(new NarrativeTriggerInfo(
                        trigger.TriggerId,
                        trigger.TriggerName,
                        trigger.TriggerMessage,
                        trigger.DestinationSection));
                }

                return NarrativeFetchResult<List<NarrativeTriggerInfo>>.Succeeded(result);
            }
            catch (Exception ex)
            {
                return NarrativeFetchResult<List<NarrativeTriggerInfo>>.Failed($"Exception: {ex.Message}");
            }
        }
    }
}
