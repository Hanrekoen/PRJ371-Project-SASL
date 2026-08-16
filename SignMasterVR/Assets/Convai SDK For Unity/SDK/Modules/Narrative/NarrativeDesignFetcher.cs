using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.RestAPI;
using Convai.RestAPI.Internal.Models;
using Convai.Runtime;
using Convai.Runtime.Logging;

namespace Convai.Modules.Narrative
{
    /// <summary>
    ///     Result of a narrative design fetch operation.
    /// </summary>
    /// <typeparam name="T">The type of data fetched.</typeparam>
    public readonly struct FetchResult<T>
    {
        /// <summary>Whether the fetch was successful.</summary>
        public bool Success { get; }

        /// <summary>The fetched data (null/empty if failed).</summary>
        public T Data { get; }

        /// <summary>Error message if the fetch failed.</summary>
        public string Error { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FetchResult{T}" /> struct.
        /// </summary>
        /// <param name="success">Whether the fetch succeeded.</param>
        /// <param name="data">Fetched data when successful.</param>
        /// <param name="error">Error message when unsuccessful.</param>
        public FetchResult(bool success, T data, string error = null)
        {
            Success = success;
            Data = data;
            Error = error;
        }

        /// <summary>Creates a successful result.</summary>
        /// <param name="data">Fetched data.</param>
        /// <returns>A successful result.</returns>
        public static FetchResult<T> Succeeded(T data) => new(true, data);

        /// <summary>Creates a failed result.</summary>
        /// <param name="error">Error message.</param>
        /// <returns>A failed result.</returns>
        public static FetchResult<T> Failed(string error) => new(false, default, error);
    }

    /// <summary>
    ///     Utility class for fetching narrative design data from the Convai backend.
    ///     Used by editor scripts and runtime components to retrieve sections and triggers.
    /// </summary>
    public static class NarrativeDesignFetcher
    {
        /// <summary>
        ///     Fetches all narrative design sections for a character.
        /// </summary>
        /// <param name="characterId">The character ID to fetch sections for.</param>
        /// <param name="apiKey">
        ///     Optional API key. Runtime callers should normally pass an explicit API key from the runtime
        ///     credential provider. When omitted, the fetcher falls back to project settings for editor/default flows.
        /// </param>
        /// <returns>Result containing list of sections or error message.</returns>
        public static async Task<FetchResult<List<SectionData>>> FetchSectionsAsync(string characterId,
            string apiKey = null)
        {
            if (string.IsNullOrEmpty(characterId))
                return FetchResult<List<SectionData>>.Failed("Character ID is required.");

            string key = apiKey ?? GetApiKey();
            if (string.IsNullOrEmpty(key))
            {
                return FetchResult<List<SectionData>>.Failed(
                    "API key is not configured. Please set it in Project Settings > Convai SDK.");
            }

            try
            {
                var options = ConvaiRestOptionsFactory.Create(key);
                using var client = new ConvaiRestClient(options);
                IReadOnlyList<SectionData> sections = await client.Narrative.GetSectionsAsync(characterId);

                List<SectionData> list = sections is List<SectionData> materialized
                    ? materialized
                    : new List<SectionData>(sections);
                return FetchResult<List<SectionData>>.Succeeded(list);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Error fetching sections: {ex.Message}",
                    LogCategory.Narrative);
                return FetchResult<List<SectionData>>.Failed($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        ///     Fetches all narrative design triggers for a character.
        /// </summary>
        /// <param name="characterId">The character ID to fetch triggers for.</param>
        /// <param name="apiKey">
        ///     Optional API key. Runtime callers should normally pass an explicit API key from the runtime
        ///     credential provider. When omitted, the fetcher falls back to project settings for editor/default flows.
        /// </param>
        /// <returns>Result containing list of triggers or error message.</returns>
        public static async Task<FetchResult<List<TriggerData>>> FetchTriggersAsync(string characterId,
            string apiKey = null)
        {
            if (string.IsNullOrEmpty(characterId))
                return FetchResult<List<TriggerData>>.Failed("Character ID is required.");

            string key = apiKey ?? GetApiKey();
            if (string.IsNullOrEmpty(key))
            {
                return FetchResult<List<TriggerData>>.Failed(
                    "API key is not configured. Please set it in Project Settings > Convai SDK.");
            }

            try
            {
                var options = ConvaiRestOptionsFactory.Create(key);
                using var client = new ConvaiRestClient(options);
                IReadOnlyList<TriggerData> triggers = await client.Narrative.GetTriggersAsync(characterId);

                List<TriggerData> list = triggers is List<TriggerData> materialized
                    ? materialized
                    : new List<TriggerData>(triggers);
                return FetchResult<List<TriggerData>>.Succeeded(list);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Error fetching triggers: {ex.Message}",
                    LogCategory.Narrative);
                return FetchResult<List<TriggerData>>.Failed($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        ///     Fetches both sections and triggers for a character.
        /// </summary>
        /// <param name="characterId">The character ID to fetch data for.</param>
        /// <param name="apiKey">
        ///     Optional API key. Runtime callers should normally pass an explicit API key from the runtime
        ///     credential provider. When omitted, the fetcher falls back to project settings for editor/default flows.
        /// </param>
        /// <returns>Result containing sections, triggers, and optional error message.</returns>
        public static async Task<(FetchResult<List<SectionData>> sections, FetchResult<List<TriggerData>> triggers)>
            FetchAllAsync(string characterId, string apiKey = null)
        {
            Task<FetchResult<List<SectionData>>> sectionsTask = FetchSectionsAsync(characterId, apiKey);
            Task<FetchResult<List<TriggerData>>> triggersTask = FetchTriggersAsync(characterId, apiKey);

            await Task.WhenAll(sectionsTask, triggersTask);

            return (sectionsTask.Result, triggersTask.Result);
        }

        /// <summary>
        ///     Gets the API key from project settings for editor/default flows.
        /// </summary>
        private static string GetApiKey()
        {
            string key = ConvaiSettings.Instance?.ApiKey;
            if (!string.IsNullOrEmpty(key)) return key;

#if UNITY_EDITOR
            if (!UnityEngine.Application.isPlaying) return null;
#endif

            ConvaiLogger.Warning(
                "API key is not available from project settings. Runtime callers should pass credentials from the runtime credential provider.",
                LogCategory.Narrative);
            return null;
        }
    }
}
