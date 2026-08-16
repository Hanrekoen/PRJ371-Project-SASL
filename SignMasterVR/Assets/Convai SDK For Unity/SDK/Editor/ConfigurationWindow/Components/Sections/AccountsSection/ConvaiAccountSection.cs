using Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.UserAccountInformation;
using Convai.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Account section of the Convai configuration window.
    ///     Displays account details, API key status, and usage statistics.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiAccountSection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "account";

        private readonly AccountInformationUI _accountInformationUI;
        private readonly ConfigurationWindowContext _context;
        private Label _apiKeyStatusLabel;

        public ConvaiAccountSection() : this(null)
        {
        }

        public ConvaiAccountSection(ConfigurationWindowContext context)
        {
            _context = context;
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("section-header", "Account Settings", "header"));

            VisualElement topRow = new() { name = "top-row" };
            topRow.AddToClassList("account-top-row");
            topRow.Add(CreateAccountDetailsCard());
            topRow.Add(CreateAPIKeyCard());
            Add(topRow);

            Add(CreateUsagesCard());

            _accountInformationUI = new AccountInformationUI(this, _context);
            RefreshApiKeyStatusLabel();

            if (_context != null) _context.ApiKeyAvailabilityChanged += OnApiKeyAvailabilityChanged;

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _accountInformationUI.CancelPendingOperations();
                if (_context != null) _context.ApiKeyAvailabilityChanged -= OnApiKeyAvailabilityChanged;
            });
        }

        /// <summary>Label displaying the current plan name.</summary>
        public Label PlanName { get; private set; }

        /// <summary>Label displaying the plan expiry date.</summary>
        public Label PlanExpiry { get; private set; }

        /// <summary>Label displaying the quota renewal date.</summary>
        public Label QuotaRenewal { get; private set; }

        /// <summary>Usage bar for interaction quota.</summary>
        public UsageBarUI InteractionUsageUI { get; private set; }

        /// <summary>Usage bar for ElevenLabs TTS quota.</summary>
        public UsageBarUI ElevenlabsUsageUI { get; private set; }

        /// <summary>Usage bar for Core API quota.</summary>
        public UsageBarUI CoreApiUsageUI { get; private set; }

        /// <summary>Usage bar for Pixel Streaming quota.</summary>
        public UsageBarUI PixelStreamingUsageUI { get; private set; }

        /// <summary>Inline status text for usage load state.</summary>
        public Label UsageStatusLabel { get; private set; }

        /// <summary>Retry button for usage fetch failures.</summary>
        public Button UsageRetryButton { get; private set; }

        /// <summary>
        ///     Starts fetching account usage data before the section is visible,
        ///     so the data is ready when the user navigates to this section.
        /// </summary>
        public void PreWarmData()
        {
            bool hasApiKey = _context != null
                ? _context.RefreshApiKeyAvailability(false)
                : ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            _accountInformationUI.GetUserAPIUsageData(hasApiKey);
        }

        protected override void OnSectionShown()
        {
            bool hasApiKey = _context != null
                ? _context.RefreshApiKeyAvailability(false)
                : ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            RefreshApiKeyStatusLabel();
            _accountInformationUI.GetUserAPIUsageData(hasApiKey);
        }

        protected override void OnSectionHidden() => _accountInformationUI?.CancelPendingOperations();

        private void OnApiKeyAvailabilityChanged(bool available)
        {
            _accountInformationUI.CancelPendingOperations();
            RefreshApiKeyStatusLabel();
            _accountInformationUI.GetUserAPIUsageData(available);
        }

        private void RefreshApiKeyStatusLabel()
        {
            if (_apiKeyStatusLabel == null) return;

            bool hasApiKey = ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            _apiKeyStatusLabel.text = hasApiKey
                ? "An API key is configured for this project."
                : "No API key configured yet.";
        }

        private VisualElement CreateAccountDetailsCard()
        {
            VisualElement card = new() { name = "account-details-card" };
            card.AddToClassList("card");
            card.AddToClassList("account-details-card");
            card.style.marginRight = 10;

            Label subheader =
                ConvaiVisualElementUtility.CreateLabel("account-details-header", "Account Details", "subheader");
            card.Add(subheader);

            VisualElement planRow = CreateLabelValueRow("Plan:", out Label planNameLabel, "-");
            PlanName = planNameLabel;
            card.Add(planRow);

            VisualElement expiryRow = CreateLabelValueRow("Plan Expiry:", out Label planExpiryLabel, "-");
            PlanExpiry = planExpiryLabel;
            card.Add(expiryRow);

            VisualElement renewalRow = CreateLabelValueRow("Quota Renewal:", out Label quotaRenewalLabel, "-");
            QuotaRenewal = quotaRenewalLabel;
            card.Add(renewalRow);

            return card;
        }

        private VisualElement CreateLabelValueRow(string labelText, out Label valueLabel, string defaultValue)
        {
            VisualElement row = new()
            {
                name = "label-value-row",
                style = { flexDirection = FlexDirection.Row, marginTop = 5, marginBottom = 5 }
            };

            Label label = ConvaiVisualElementUtility.CreateLabel("row-label", labelText, "label");
            label.style.minWidth = 110;
            label.style.flexShrink = 0;
            label.style.marginRight = 10;

            valueLabel = ConvaiVisualElementUtility.CreateLabel("row-value", defaultValue, "helper-text");
            valueLabel.style.flexGrow = 1;

            row.Add(label);
            row.Add(valueLabel);

            return row;
        }

        private VisualElement CreateAPIKeyCard()
        {
            VisualElement card = new() { name = "api-key-card" };
            card.AddToClassList("card");
            card.AddToClassList("account-api-key-card");

            Label subheader = ConvaiVisualElementUtility.CreateLabel("api-key-header", "API Key", "subheader");
            card.Add(subheader);

            _apiKeyStatusLabel = ConvaiVisualElementUtility.CreateLabel(
                "api-key-status", "No API key configured yet.", "helper-text");
            card.Add(_apiKeyStatusLabel);

            var manageButton = new Button(ConvaiConfigurationWindowEditor.OpenSettingsWindow)
            {
                name = "manage-api-key-button",
                text = "Manage in Settings",
                tooltip = "API key entry and validation live in the SDK Settings section."
            };
            ConvaiVisualElementUtility.AddStyles(manageButton, "button", "btn-medium");
            manageButton.style.alignSelf = Align.Center;
            manageButton.style.marginTop = 10;
            card.Add(manageButton);

            return card;
        }

        private VisualElement CreateUsagesCard()
        {
            VisualElement card = new() { name = "usages-card" };
            card.AddToClassList("card");

            Label subheader = ConvaiVisualElementUtility.CreateLabel("usages-header", "Usages", "subheader");
            card.Add(subheader);

            VisualElement statusRow = new()
            {
                name = "usage-status-row",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    marginBottom = 8
                }
            };

            UsageStatusLabel = ConvaiVisualElementUtility.CreateLabel(
                "usage-status-label",
                "Open this section to load account usage.",
                "helper-text");
            UsageStatusLabel.style.flexGrow = 1;

            UsageRetryButton = new Button(() => _accountInformationUI.GetUserAPIUsageData())
            {
                name = "usage-retry-button", text = "Retry"
            };
            UsageRetryButton.AddToClassList("button-small");
            UsageRetryButton.style.display = DisplayStyle.None;

            statusRow.Add(UsageStatusLabel);
            statusRow.Add(UsageRetryButton);
            card.Add(statusRow);

            InteractionUsageUI = new UsageBarUI("interaction-usage", "Interaction Usage");
            card.Add(InteractionUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            ElevenlabsUsageUI = new UsageBarUI("elevenlabs-usage", "Elevenlabs Usage");
            card.Add(ElevenlabsUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            CoreApiUsageUI = new UsageBarUI("core-api-usage", "Core API Usage");
            card.Add(CoreApiUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            PixelStreamingUsageUI = new UsageBarUI("pixel-streaming-usage", "Pixel Streaming Usage");
            card.Add(PixelStreamingUsageUI.Container);

            return card;
        }

        /// <summary>
        ///     UI component for displaying usage statistics with a progress bar and label.
        /// </summary>
        public class UsageBarUI
        {
            /// <summary>Container element holding the entire usage bar UI.</summary>
            public readonly VisualElement Container;

            /// <summary>Progress bar showing usage percentage.</summary>
            public readonly ProgressBar ProgressBar;

            /// <summary>Label showing the current/limit usage text.</summary>
            public readonly Label UsageLabel;

            /// <summary>
            ///     Creates a new usage bar UI component.
            /// </summary>
            /// <param name="name">Element name prefix for generated elements.</param>
            /// <param name="title">Display title for the usage bar.</param>
            public UsageBarUI(string name, string title)
            {
                Container = new VisualElement { name = $"{name}-container" };

                Label header = ConvaiVisualElementUtility.CreateLabel($"{name}-title", title, "label");
                header.style.marginBottom = 2;

                VisualElement barRow = new()
                {
                    name = $"{name}-bar-row",
                    style = { flexDirection = FlexDirection.Row, alignItems = Align.Center }
                };

                ProgressBar = new ProgressBar { name = $"{name}-progress" };
                ProgressBar.style.flexGrow = 1;
                ConvaiVisualElementUtility.AddStyles(ProgressBar, "usage-bar");

                UsageLabel = ConvaiVisualElementUtility.CreateLabel($"{name}-literal", "0 / 0", "helper-text");
                UsageLabel.style.marginLeft = 10;
                UsageLabel.style.minWidth = 100;
                UsageLabel.style.unityTextAlign = TextAnchor.MiddleRight;

                barRow.Add(ProgressBar);
                barRow.Add(UsageLabel);

                Container.Add(header);
                Container.Add(barRow);
            }

            /// <summary>
            ///     Updates the usage bar with current and limit values.
            /// </summary>
            /// <param name="current">Current usage amount.</param>
            /// <param name="limit">Maximum usage limit.</param>
            public void SetUsage(float current, float limit)
            {
                float percentage = limit > 0 ? current / limit * 100f : 0f;
                ProgressBar.value = percentage;
                UsageLabel.text = $"{current:N0} / {limit:N0}";
            }
        }
    }
}
