using Convai.Application;
using Convai.Editor.ConfigurationWindow.Content;
using Convai.Editor.ConfigurationWindow.Services;
using UnityEditor;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Welcome section for the operational dashboard.
    ///     Shows beta info, setup health, and guided actions.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiWelcomeSection : ConvaiBaseSection
    {
        /// <summary>UXML section name.</summary>
        public const string SECTION_NAME = "welcome";

        private readonly ConvaiConfigurationContent _content;

        private readonly ConfigurationWindowContext _context;
        private readonly VisualElement _healthContainer;
        private readonly Label _healthSummaryLabel;

        private bool _isAttached;

        public ConvaiWelcomeSection() : this(null)
        {
        }

        public ConvaiWelcomeSection(ConfigurationWindowContext context)
        {
            _context = context;
            _content = ConvaiConfigurationContent.Instance;

            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("welcome-header", _content.WelcomeHeader, "header"));
            Add(ConvaiVisualElementUtility.CreateLabel("welcome-subheader", _content.WelcomeSubheader, "subheader"));

            VisualElement betaCard = CreateCard();
            betaCard.Add(ConvaiVisualElementUtility.CreateLabel("beta-title",
                $"Current SDK Version: {ConvaiSDK.Version}", "label"));
            Add(betaCard);

            VisualElement quickStartCard = CreateCard();
            quickStartCard.Add(ConvaiVisualElementUtility.CreateLabel("quick-start-title", "Quick Start", "label"));
            Label quickStartLabel = ConvaiVisualElementUtility.CreateLabel("quick-start-steps",
                _content.QuickStartInstructions, "helper-text");
            quickStartLabel.AddToClassList("welcome-body-text");
            quickStartCard.Add(quickStartLabel);
            Add(quickStartCard);

            VisualElement healthCard = CreateCard();
            healthCard.Add(ConvaiVisualElementUtility.CreateLabel("health-title", "Setup Health", "subheader"));
            _healthSummaryLabel = ConvaiVisualElementUtility.CreateLabel("health-summary", string.Empty, "helper-text");
            _healthContainer = new VisualElement { name = "setup-health-container" };
            _healthContainer.AddToClassList("setup-health-list");
            healthCard.Add(_healthSummaryLabel);
            healthCard.Add(_healthContainer);
            Add(healthCard);

            VisualElement actionsCard = CreateCard();
            actionsCard.Add(ConvaiVisualElementUtility.CreateLabel("actions-title", "Actions", "subheader"));
            actionsCard.Add(CreateActionButtons());
            Add(actionsCard);

            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        protected override void OnSectionShown() => RefreshHealthReport();

        private static VisualElement CreateCard()
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList("dashboard-card");
            return card;
        }

        private VisualElement CreateActionButtons()
        {
            var actionRow = new VisualElement { name = "welcome-action-row" };
            actionRow.AddToClassList("welcome-action-row");

            var openSettingsButton = new Button(() =>
            {
                SettingsService.OpenProjectSettings("Project/Convai SDK");
                RefreshHealthReport();
            }) { text = "Open SDK Settings" };
            ConvaiVisualElementUtility.AddStyles(openSettingsButton, "button-small", "welcome-action-button");

            var setupSceneButton = new Button(() =>
            {
                ConvaiSetupWizard.SetupRequiredComponents();
                RefreshHealthReport();
            }) { text = "Setup Required Components" };
            ConvaiVisualElementUtility.AddStyles(setupSceneButton, "button-small", "welcome-action-button");

            var validateButton = new Button(() =>
            {
                ConvaiSetupWizard.ValidateSceneSetup();
                RefreshHealthReport();
            }) { text = "Validate Scene Setup" };
            ConvaiVisualElementUtility.AddStyles(validateButton, "button-small", "welcome-action-button");

            actionRow.Add(openSettingsButton);
            actionRow.Add(setupSceneButton);
            actionRow.Add(validateButton);

            return actionRow;
        }

        private void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            if (_isAttached) return;

            if (_context != null) _context.ApiKeyAvailabilityChanged += OnApiKeyAvailabilityChanged;

            _isAttached = true;
            RefreshHealthReport();
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            if (!_isAttached) return;

            if (_context != null) _context.ApiKeyAvailabilityChanged -= OnApiKeyAvailabilityChanged;

            _isAttached = false;
        }

        private void OnApiKeyAvailabilityChanged(bool _) => RefreshHealthReport();

        private void RefreshHealthReport()
        {
            if (!IsSectionVisible && panel != null) return;

            SetupHealthReport report = SetupHealthService.BuildReport();
            _healthContainer.Clear();

            foreach (SetupHealthCheckResult result in report.Results)
            {
                var row = new VisualElement { name = $"health-row-{result.Id}" };
                row.AddToClassList("setup-health-row");

                Label nameLabel =
                    ConvaiVisualElementUtility.CreateLabel($"health-name-{result.Id}", result.Title,
                        "setup-health-name");
                Label statusLabel = ConvaiVisualElementUtility.CreateLabel(
                    $"health-status-{result.Id}",
                    GetStatusText(result.Status),
                    "setup-health-status");
                statusLabel.AddToClassList(GetStatusClass(result.Status));

                row.Add(nameLabel);
                row.Add(statusLabel);
                _healthContainer.Add(row);

                Label messageLabel = ConvaiVisualElementUtility.CreateLabel(
                    $"health-message-{result.Id}",
                    result.Message,
                    "setup-health-message");
                _healthContainer.Add(messageLabel);
            }

            _healthSummaryLabel.text = report.HasBlockingIssues
                ? "Blocking issues found. Resolve blockers before running conversations."
                : report.HasWarnings
                    ? "No blockers, but there are warnings to address."
                    : "Setup looks healthy. You are ready to test conversations.";
        }

        private static string GetStatusText(SetupHealthStatus status)
        {
            return status switch
            {
                SetupHealthStatus.Healthy => "Healthy",
                SetupHealthStatus.Warning => "Warning",
                _ => "Blocked"
            };
        }

        private static string GetStatusClass(SetupHealthStatus status)
        {
            return status switch
            {
                SetupHealthStatus.Healthy => "setup-health-status--healthy",
                SetupHealthStatus.Warning => "setup-health-status--warning",
                _ => "setup-health-status--blocked"
            };
        }
    }
}
