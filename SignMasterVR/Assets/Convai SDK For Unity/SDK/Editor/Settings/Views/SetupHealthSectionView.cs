using System.Collections.Generic;
using Convai.Editor.ConfigurationWindow.Services;
using Convai.Editor.Settings.Services;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings.Views
{
    /// <summary>
    ///     Setup Health section: project-configuration checks with fix-it buttons.
    /// </summary>
    public sealed class SetupHealthSectionView : ConvaiSettingsSectionView
    {
        private readonly VisualElement _itemsContainer;

        public SetupHealthSectionView(ConvaiSettingsViewContext context)
            : base(context, "Setup Health", supportsReset: false)
        {
            var refreshButton = new Button(Refresh)
            {
                text = "Refresh",
                tooltip = "Re-run the project health checks."
            };
            refreshButton.AddToClassList("convai-settings-reset-button");
            HeaderActions.Add(refreshButton);

            _itemsContainer = new VisualElement();
            Body.Add(_itemsContainer);

            Context.CredentialsChanged += Refresh;
        }

        public override void Activate() => Refresh();

        private void Refresh()
        {
            _itemsContainer.Clear();

            IReadOnlyList<ProjectHealthItem> items = ProjectSetupHealthService.BuildProjectReport();
            foreach (ProjectHealthItem item in items)
                _itemsContainer.Add(CreateItemRow(item));
        }

        private VisualElement CreateItemRow(ProjectHealthItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("convai-settings-health-row");

            VisualElement badge = ConvaiSettingsUi.CreateStatusBadge(out VisualElement dot, out Label text);
            badge.AddToClassList("convai-settings-health-badge");
            ConvaiSettingsUi.SetBadgeState(dot, item.Result.Status switch
            {
                SetupHealthStatus.Healthy => ConvaiSettingsBadgeState.Ok,
                SetupHealthStatus.Warning => ConvaiSettingsBadgeState.Warning,
                _ => ConvaiSettingsBadgeState.Error
            });
            text.text = item.Result.Title;
            row.Add(badge);

            var message = new Label(item.Result.Message);
            message.AddToClassList("convai-settings-health-message");
            row.Add(message);

            if (item.Fix != null)
            {
                var fixButton = new Button(() =>
                {
                    item.Fix();
                    Refresh();
                })
                {
                    text = item.FixLabel ?? "Fix"
                };
                fixButton.AddToClassList("convai-settings-inline-button");
                row.Add(fixButton);
            }

            return row;
        }
    }
}
