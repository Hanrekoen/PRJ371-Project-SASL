using System.Collections.Generic;
using System.Threading.Tasks;

namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Result of a narrative design fetch operation.
    /// </summary>
    public readonly struct NarrativeFetchResult<T>
    {
        public bool Success { get; }
        public T Data { get; }
        public string Error { get; }

        public NarrativeFetchResult(bool success, T data, string error = null)
        {
            Success = success;
            Data = data;
            Error = error;
        }

        public static NarrativeFetchResult<T> Succeeded(T data) => new(true, data);
        public static NarrativeFetchResult<T> Failed(string error) => new(false, default, error);
    }

    /// <summary>
    ///     Service for fetching narrative design data from the Convai backend.
    /// </summary>
    public interface INarrativeDesignDataService
    {
        public Task<NarrativeFetchResult<List<NarrativeSectionInfo>>> FetchSectionsAsync(string characterId,
            string apiKey = null);

        public Task<NarrativeFetchResult<List<NarrativeTriggerInfo>>> FetchTriggersAsync(string characterId,
            string apiKey = null);
    }

    /// <summary>
    ///     Narrative section information from the backend.
    /// </summary>
    public readonly struct NarrativeSectionInfo
    {
        public string SectionId { get; }
        public string SectionName { get; }

        public NarrativeSectionInfo(string sectionId, string sectionName)
        {
            SectionId = sectionId ?? string.Empty;
            SectionName = sectionName ?? string.Empty;
        }
    }

    /// <summary>
    ///     Narrative trigger information from the backend.
    /// </summary>
    public readonly struct NarrativeTriggerInfo
    {
        public string TriggerId { get; }
        public string TriggerName { get; }
        public string TriggerMessage { get; }
        public string DestinationSection { get; }

        public NarrativeTriggerInfo(string triggerId, string triggerName, string triggerMessage,
            string destinationSection)
        {
            TriggerId = triggerId ?? string.Empty;
            TriggerName = triggerName ?? string.Empty;
            TriggerMessage = triggerMessage ?? string.Empty;
            DestinationSection = destinationSection ?? string.Empty;
        }
    }
}
