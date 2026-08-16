using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convai.RestAPI
{
    /// <summary>
    /// Represents the API response from the user usage endpoint.
    /// Contains account details and usage metrics for the current billing period.
    /// </summary>
    public class UserUsageData
    {
        [JsonProperty("usage_v2")]
        public UsageData Data { get; set; }

        public static UserUsageData Default() => new() { Data = UsageData.Default() };

        /// <summary>
        /// Contains account plan information and usage metrics.
        /// </summary>
        public class UsageData
        {
            [JsonProperty("plan_name")]
            public string PlanName { get; set; }

            [JsonProperty("expiry_ts")]
            public string ExpiryTimestamp { get; set; }

            [JsonProperty("metrics")]
            public List<UsageMetric> Metrics { get; set; }

            public static UsageData Default() => new()
            {
                PlanName = string.Empty,
                ExpiryTimestamp = string.Empty,
                Metrics = new List<UsageMetric>()
            };

            /// <summary>
            /// Retrieves usage details for a specific metric by its identifier.
            /// </summary>
            /// <param name="metricId">The metric identifier (e.g., "interactions", "core-api").</param>
            /// <returns>The usage details, or a default empty detail if not found.</returns>
            public UsageMetricDetail GetMetric(string metricId)
            {
                if (Metrics == null)
                    return UsageMetricDetail.Empty;

                foreach (UsageMetric metric in Metrics)
                {
                    if (metric.Id == metricId && metric.Details is { Count: > 0 })
                        return metric.Details[0];
                }

                return UsageMetricDetail.Empty;
            }

            public UsageMetricDetail InteractionUsage => GetMetric(MetricIds.Interactions);
            public UsageMetricDetail ElevenlabsUsage => GetMetric(MetricIds.ProviderPool);
            public UsageMetricDetail CoreApiUsage => GetMetric(MetricIds.CoreApi);
            public UsageMetricDetail PixelStreamingUsage => GetMetric(MetricIds.PixelStreaming);
        }

        /// <summary>
        /// Represents a single usage metric with its identifier and usage details.
        /// </summary>
        public class UsageMetric
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("usage_details")]
            public List<UsageMetricDetail> Details { get; set; }
        }

        /// <summary>
        /// Contains the usage and limit values for a specific metric.
        /// </summary>
        public class UsageMetricDetail
        {
            [JsonProperty("limit")]
            public float Limit { get; set; }

            [JsonProperty("usage")]
            public float Usage { get; set; }

            public static UsageMetricDetail Empty => new() { Limit = 0, Usage = 0 };
        }

        /// <summary>
        /// Metric identifiers used by the Convai API.
        /// </summary>
        public static class MetricIds
        {
            public const string Interactions = "interactions";
            public const string ProviderPool = "provider_pool_1";
            public const string CoreApi = "core-api";
            public const string PixelStreaming = "pixel_streaming";
        }
    }
}
