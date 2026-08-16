#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Transport;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for character-scoped long-term memory APIs.
    /// </summary>
    public sealed class MemoryService : ConvaiServiceBase
    {
        private const string AddMemoryEndpoint = "memory/add";
        private const string ListMemoryEndpoint = "memory/list";
        private const string GetMemoryEndpoint = "memory/get";
        private const string DeleteMemoryEndpoint = "memory/delete";
        private const string DeleteAllMemoryEndpoint = "memory/delete-all";

        internal MemoryService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        public async Task<AddMemoriesResponse> AddAsync(
            string characterId,
            string endUserId,
            IReadOnlyList<string> memories,
            IReadOnlyDictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "character_id", characterId },
                { "end_user_id", endUserId },
                { "memories", new List<string>(memories) }
            };

            if (metadata != null && metadata.Count > 0)
                requestBody["metadata"] = new Dictionary<string, object>(metadata);

            return await PostAsync<AddMemoriesResponse>(
                AddMemoryEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MemoryListResponse> ListAsync(
            string characterId,
            string endUserId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "character_id", characterId },
                { "end_user_id", endUserId },
                { "page", page },
                { "page_size", pageSize }
            };

            return await PostAsync<MemoryListResponse>(
                ListMemoryEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MemoryRecord> GetAsync(
            string characterId,
            string endUserId,
            string memoryId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "character_id", characterId },
                { "end_user_id", endUserId },
                { "memory_id", memoryId }
            };

            return await PostAsync<MemoryRecord>(
                GetMemoryEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MemoryDeleteResponse> DeleteAsync(
            string characterId,
            string endUserId,
            string memoryId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "character_id", characterId },
                { "end_user_id", endUserId },
                { "memory_id", memoryId }
            };

            return await PostAsync<MemoryDeleteResponse>(
                DeleteMemoryEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MemoryDeleteAllResponse> DeleteAllAsync(
            string characterId,
            string endUserId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "character_id", characterId },
                { "end_user_id", endUserId }
            };

            return await PostAsync<MemoryDeleteAllResponse>(
                DeleteAllMemoryEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
