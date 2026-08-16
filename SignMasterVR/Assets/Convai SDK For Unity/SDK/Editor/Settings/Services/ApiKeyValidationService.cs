using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Convai.Application;
using Convai.Domain.Logging;
using Convai.RestAPI;
using Convai.Runtime;
using Convai.Runtime.Logging;
using UnityEditor;

namespace Convai.Editor.Settings.Services
{
    /// <summary>Outcome of an API key validation attempt.</summary>
    public readonly struct ApiKeyValidationResult
    {
        public ApiKeyValidationResult(bool isValid, string message, bool isDefinitive = true)
        {
            IsValid = isValid;
            Message = message ?? string.Empty;
            IsDefinitive = isDefinitive;
        }

        public bool IsValid { get; }
        public string Message { get; }

        /// <summary>True when the backend conclusively accepted or rejected the credentials.</summary>
        public bool IsDefinitive { get; }
    }

    /// <summary>
    ///     Validates API keys against the Convai backend for the settings UI.
    ///     Results are cached per key/environment hash in <see cref="EditorPrefs" /> for a bounded
    ///     interval so the badge survives Editor restarts without ever persisting the key itself.
    /// </summary>
    public sealed class ApiKeyValidationService
    {
        private const string CachePrefix = "Convai.Settings.ApiKeyValidation.";
        internal const int CacheFormatVersion = 1;
        internal static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
        private int _requestVersion;

        /// <summary>True while a validation request is in flight.</summary>
        public bool IsValidating { get; private set; }

        /// <summary>
        ///     Validates the given (possibly unsaved) credentials. The callback runs on the
        ///     editor main thread; stale responses from superseded requests are dropped.
        /// </summary>
        public void Validate(string apiKey, ConvaiApiEnvironment environment, string customRestBaseUrl,
            Action<ApiKeyValidationResult> onCompleted)
        {
            int version = ++_requestVersion;
            IsValidating = true;
            _ = ValidateAsync(apiKey, environment, customRestBaseUrl, version, onCompleted);
        }

        /// <summary>Drops any in-flight request without invoking its callback.</summary>
        public void CancelPending()
        {
            _requestVersion++;
            IsValidating = false;
        }

        /// <summary>Looks up a cached validation result for the given credentials.</summary>
        public static bool TryGetCachedResult(string apiKey, ConvaiApiEnvironment environment,
            string customRestBaseUrl, out ApiKeyValidationResult result) =>
            TryGetCachedResult(apiKey, environment, customRestBaseUrl, DateTimeOffset.UtcNow, out result);

        internal static bool TryGetCachedResult(string apiKey, ConvaiApiEnvironment environment,
            string customRestBaseUrl, DateTimeOffset utcNow, out ApiKeyValidationResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(apiKey)) return false;

            string cacheKey = CacheKey(apiKey, environment, customRestBaseUrl);
            string cached = EditorPrefs.GetString(cacheKey, string.Empty);
            if (string.IsNullOrEmpty(cached)) return false;

            if (TryParseCachedResult(cached, utcNow, out result)) return true;

            // Remove expired, incompatible, and legacy timeless values instead of reusing stale state.
            EditorPrefs.DeleteKey(cacheKey);
            return false;
        }

        internal static void StoreResult(string apiKey, ConvaiApiEnvironment environment, string customRestBaseUrl,
            ApiKeyValidationResult result) =>
            StoreResult(apiKey, environment, customRestBaseUrl, result, DateTimeOffset.UtcNow);

        internal static void StoreResult(string apiKey, ConvaiApiEnvironment environment, string customRestBaseUrl,
            ApiKeyValidationResult result, DateTimeOffset validatedAtUtc)
        {
            if (string.IsNullOrEmpty(apiKey)) return;

            string payload = (result.IsValid ? "valid:" : "invalid:") + result.Message;
            string value = string.Join("|",
                CacheFormatVersion.ToString(CultureInfo.InvariantCulture),
                ConvaiSDK.Version.ToString(),
                validatedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                payload);
            EditorPrefs.SetString(CacheKey(apiKey, environment, customRestBaseUrl), value);
        }

        internal static string CacheKey(string apiKey, ConvaiApiEnvironment environment, string customRestBaseUrl)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{apiKey}|{environment}|{customRestBaseUrl}"));
            return CachePrefix + Convert.ToBase64String(hash);
        }

        private static bool TryParseCachedResult(string cached, DateTimeOffset utcNow,
            out ApiKeyValidationResult result)
        {
            result = default;
            int formatSeparator = cached.IndexOf('|');
            int sdkSeparator = formatSeparator < 0 ? -1 : cached.IndexOf('|', formatSeparator + 1);
            int timestampSeparator = sdkSeparator < 0 ? -1 : cached.IndexOf('|', sdkSeparator + 1);
            if (formatSeparator < 0 || sdkSeparator < 0 || timestampSeparator < 0) return false;

            if (!int.TryParse(cached.Substring(0, formatSeparator), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int formatVersion) || formatVersion != CacheFormatVersion)
                return false;

            string sdkVersion = cached.Substring(formatSeparator + 1, sdkSeparator - formatSeparator - 1);
            if (!string.Equals(sdkVersion, ConvaiSDK.Version.ToString(), StringComparison.Ordinal)) return false;

            if (!long.TryParse(cached.Substring(sdkSeparator + 1, timestampSeparator - sdkSeparator - 1),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long validatedAtSeconds))
                return false;

            long nowSeconds = utcNow.ToUnixTimeSeconds();
            long oldestAllowedSeconds = nowSeconds - (long)CacheLifetime.TotalSeconds;
            if (validatedAtSeconds > nowSeconds || validatedAtSeconds < oldestAllowedSeconds) return false;

            return TryParseResultPayload(cached.Substring(timestampSeparator + 1), out result);
        }

        private static bool TryParseResultPayload(string payload, out ApiKeyValidationResult result)
        {
            if (payload.StartsWith("valid:", StringComparison.Ordinal))
            {
                result = new ApiKeyValidationResult(true, payload.Substring("valid:".Length));
                return true;
            }

            if (payload.StartsWith("invalid:", StringComparison.Ordinal))
            {
                result = new ApiKeyValidationResult(false, payload.Substring("invalid:".Length));
                return true;
            }

            result = default;
            return false;
        }

        private async Task ValidateAsync(string apiKey, ConvaiApiEnvironment environment, string customRestBaseUrl,
            int version, Action<ApiKeyValidationResult> onCompleted)
        {
            ApiKeyValidationResult result;
            try
            {
                ConvaiRestClientOptions options =
                    ConvaiRestOptionsFactory.Create(apiKey, environment, customRestBaseUrl);
                using var client = new ConvaiRestClient(options);
                // ValidateApiKeyAsync throws ConvaiRestException for non-2xx responses (e.g., invalid API key).
                await client.Users.ValidateApiKeyAsync();
                result = new ApiKeyValidationResult(true, string.Empty);
            }
            catch (ConvaiRestException ex)
            {
                ConvaiLogger.Warning(
                    $"[ApiKeyValidation] API key validation failed ({ex.Category}, HTTP {ex.StatusCodeInt}): {ex.Message}",
                    LogCategory.Editor);
                result = new ApiKeyValidationResult(
                    false,
                    ex.GetUserFriendlyMessage(),
                    ex.Category == ConvaiRestErrorCategory.Authentication);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ApiKeyValidation] Unexpected error during validation: {ex}",
                    LogCategory.Editor);
                result = new ApiKeyValidationResult(false,
                    "Something went wrong. Please check your API key and network connection.", false);
            }

            EditorApplication.delayCall += () =>
            {
                if (version != _requestVersion) return;

                IsValidating = false;
                if (result.IsDefinitive)
                    StoreResult(apiKey, environment, customRestBaseUrl, result);
                try
                {
                    onCompleted?.Invoke(result);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"[ApiKeyValidation] Completion callback threw: {ex}", LogCategory.Editor);
                }
            };
        }
    }
}
