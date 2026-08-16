using System;
using System.Globalization;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.RestAPI;
using Convai.Runtime;
using Convai.Runtime.Logging;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.UserAccountInformation
{
    /// <summary>
    ///     Handles account information UI display and API usage data fetching.
    ///     Uses ConvaiSettings for API key access.
    /// </summary>
    public class AccountInformationUI
    {
        private readonly ConfigurationWindowContext _context;
        private readonly ConvaiAccountSection _ui;

        private bool _isFetchInProgress;
        private int _requestVersion;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AccountInformationUI" /> class.
        /// </summary>
        /// <param name="section">Account section UI instance.</param>
        /// <param name="context">Shared window context.</param>
        public AccountInformationUI(ConvaiAccountSection section, ConfigurationWindowContext context)
        {
            _ui = section;
            _context = context;
            InitializeApiKeyState();
        }

        private void InitializeApiKeyState()
        {
            var settings = ConvaiSettings.Instance;
            _context?.RefreshApiKeyAvailability(false);

            if (settings != null && settings.HasApiKey)
                SetReadyState("Open this section to load account usage.");
            else
                SetInvalidApiKeyState();
        }

        /// <summary>
        ///     Cancels pending requests and prevents stale UI updates.
        /// </summary>
        public void CancelPendingOperations()
        {
            _requestVersion++;
            _isFetchInProgress = false;
        }

        /// <summary>
        ///     Fetches the current user's API usage data and updates the UI.
        /// </summary>
        /// <param name="validApiKey">Whether the current API key is considered valid.</param>
        public void GetUserAPIUsageData(bool validApiKey = true)
        {
            if (_isFetchInProgress) return;

            int requestId = ++_requestVersion;
            _ = GetUserAPIUsageDataAsync(validApiKey, requestId);
        }

        /// <summary>
        ///     Sets account UI to invalid-key state.
        /// </summary>
        /// <param name="message">Status text to show.</param>
        public void SetInvalidApiKeyState(string message = "Set a valid API key to view account usage.")
        {
            _ui.PlanName.text = "-";
            _ui.PlanExpiry.text = "-";
            _ui.QuotaRenewal.text = "-";
            _ui.InteractionUsageUI.SetUsage(0, 0);
            _ui.ElevenlabsUsageUI.SetUsage(0, 0);
            _ui.CoreApiUsageUI.SetUsage(0, 0);
            _ui.PixelStreamingUsageUI.SetUsage(0, 0);
            SetErrorState(message, false);
        }

        private async Task GetUserAPIUsageDataAsync(bool validApiKey, int requestId)
        {
            if (!validApiKey)
            {
                SetInvalidApiKeyState("API key validation failed. Please update your API key.");
                return;
            }

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                SetInvalidApiKeyState();
                return;
            }

            _isFetchInProgress = true;
            SetLoadingState("Loading account usage...");

            try
            {
                var options = ConvaiRestOptionsFactory.Create(settings.ApiKey);
                using var client = new ConvaiRestClient(options);
                UserUsageData usage = await client.Users.GetUsageAsync();
                if (ShouldIgnoreResult(requestId)) return;

                UpdateUIWithUsageData(usage);
                SetReadyState("Usage data updated.");
            }
            catch (Exception ex)
            {
                if (ShouldIgnoreResult(requestId)) return;

                ConvaiLogger.Exception($"Error fetching API usage data: {ex.Message}", LogCategory.REST);
                SetInvalidApiKeyState("Unable to load usage data. Check your API key and network, then retry.");
                SetErrorState("Unable to load usage data. Check your API key and network, then retry.", true);
            }
            finally
            {
                if (requestId == _requestVersion) _isFetchInProgress = false;
            }
        }

        private bool ShouldIgnoreResult(int requestId) => requestId != _requestVersion;

        private void UpdateUIWithUsageData(UserUsageData usageData)
        {
            UserUsageData.UsageData data = usageData.Data;

            if (data == null)
            {
                SetInvalidApiKeyState("No usage data returned for this API key.");
                return;
            }

            _ui.PlanName.text = !string.IsNullOrEmpty(data.PlanName) ? data.PlanName : "-";
            _ui.PlanExpiry.text = FormatExpiryDate(data.ExpiryTimestamp);
            _ui.QuotaRenewal.text = FormatQuotaRenewal(data.ExpiryTimestamp);

            UserUsageData.UsageMetricDetail interaction = data.InteractionUsage;
            UserUsageData.UsageMetricDetail elevenlabs = data.ElevenlabsUsage;
            UserUsageData.UsageMetricDetail coreApi = data.CoreApiUsage;
            UserUsageData.UsageMetricDetail pixelStreaming = data.PixelStreamingUsage;

            _ui.InteractionUsageUI.SetUsage(interaction.Usage, interaction.Limit);
            _ui.ElevenlabsUsageUI.SetUsage(elevenlabs.Usage, elevenlabs.Limit);
            _ui.CoreApiUsageUI.SetUsage(coreApi.Usage, coreApi.Limit);
            _ui.PixelStreamingUsageUI.SetUsage(pixelStreaming.Usage, pixelStreaming.Limit);
        }

        private void SetLoadingState(string message)
        {
            if (_ui.UsageStatusLabel != null) _ui.UsageStatusLabel.text = message;

            if (_ui.UsageRetryButton != null)
            {
                _ui.UsageRetryButton.style.display = DisplayStyle.None;
                _ui.UsageRetryButton.SetEnabled(false);
            }
        }

        private void SetErrorState(string message, bool showRetry)
        {
            if (_ui.UsageStatusLabel != null) _ui.UsageStatusLabel.text = message;

            if (_ui.UsageRetryButton != null)
            {
                _ui.UsageRetryButton.style.display = showRetry ? DisplayStyle.Flex : DisplayStyle.None;
                _ui.UsageRetryButton.SetEnabled(showRetry);
            }
        }

        private void SetReadyState(string message)
        {
            if (_ui.UsageStatusLabel != null) _ui.UsageStatusLabel.text = message;

            if (_ui.UsageRetryButton != null)
            {
                _ui.UsageRetryButton.style.display = DisplayStyle.None;
                _ui.UsageRetryButton.SetEnabled(false);
            }
        }

        private static string FormatExpiryDate(string expiryTimestamp)
        {
            if (string.IsNullOrEmpty(expiryTimestamp))
                return "-";

            if (!DateTime.TryParse(expiryTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTime date))
                return expiryTimestamp.Length >= 10 ? expiryTimestamp[..10] : expiryTimestamp;

            int daysRemaining = (date.Date - DateTime.Today).Days;
            string daysText = daysRemaining switch
            {
                > 0 => $"(in {daysRemaining} days)",
                0 => "(today)",
                _ => "(expired)"
            };

            return $"{date:yyyy-MM-dd} {daysText}";
        }

        private static string FormatQuotaRenewal(string expiryTimestamp)
        {
            if (string.IsNullOrEmpty(expiryTimestamp))
                return "-";

            if (!DateTime.TryParse(expiryTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTime expiryDate))
                return "-";

            DateTime today = DateTime.Today;
            int renewalDay = Math.Min(expiryDate.Day, DateTime.DaysInMonth(today.Year, today.Month));
            DateTime nextRenewal = new(today.Year, today.Month, renewalDay);

            if (nextRenewal <= today)
            {
                nextRenewal = nextRenewal.AddMonths(1);
                renewalDay = Math.Min(expiryDate.Day, DateTime.DaysInMonth(nextRenewal.Year, nextRenewal.Month));
                nextRenewal = new DateTime(nextRenewal.Year, nextRenewal.Month, renewalDay);
            }

            int daysRemaining = (nextRenewal - today).Days;
            string daysText = daysRemaining > 0 ? $"(in {daysRemaining} days)" : "(today)";

            return $"{nextRenewal:yyyy-MM-dd} {daysText}";
        }
    }
}
