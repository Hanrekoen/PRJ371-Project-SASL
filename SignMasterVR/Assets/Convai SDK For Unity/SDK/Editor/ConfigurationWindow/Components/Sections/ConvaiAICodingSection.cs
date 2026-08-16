using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     AI coding setup inside the Convai configuration window. Reuses the
    ///     standalone setup window's health checks and repair operations.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiAICodingSection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "ai-coding";

        private const double RefreshIntervalSeconds = 0.5d;

        private readonly StatusRow _assistantStatus;
        private readonly List<ClientRow> _clientRows = new();
        private readonly Label _repairMessage;
        private readonly StatusRow _skillStatus;
        private readonly StatusRow _toolsStatus;
        private readonly StatusRow _unityStatus;
        private double _nextRefreshAt;

        /// <summary>Creates an AI coding section.</summary>
        public ConvaiAICodingSection()
        {
            AddToClassList("section-card");
            AddToClassList("ai-coding-section");
            Add(ConvaiVisualElementUtility.CreateLabel("section-header", "AI Coding", "header"));

            var intro = new Label(
                "Unity owns MCP client configuration. Convai adds SDK tools, a packaged skill, and optional managed project instructions.");
            intro.AddToClassList("ai-coding-intro");
            Add(intro);

            VisualElement healthCard = CreateCard("Setup Health", out VisualElement healthHeader);
            var refreshButton = new Button(Refresh)
            {
                text = "Refresh",
                tooltip = "Re-check Unity AI Assistant, packaged skill, and Convai MCP tool readiness."
            };
            refreshButton.AddToClassList("ai-coding-secondary-button");
            healthHeader.Add(refreshButton);

            _unityStatus = CreateStatusRow("Unity 6000+", null);
            _assistantStatus = CreateStatusRow("Unity AI Assistant", ConvaiAICodingSetupWindow.BeginAssistantRepair);
            _skillStatus = CreateStatusRow("Packaged Convai Skill", ConvaiAICodingSetupWindow.BeginSkillRepair);
            _toolsStatus = CreateStatusRow("Convai MCP Tools", ConvaiAICodingSetupWindow.BeginToolRegistrationRepair);
            healthCard.Add(_unityStatus.Root);
            healthCard.Add(_assistantStatus.Root);
            healthCard.Add(_skillStatus.Root);
            healthCard.Add(_toolsStatus.Root);

            _repairMessage = new Label();
            _repairMessage.AddToClassList("ai-coding-message");
            healthCard.Add(_repairMessage);

            var mcpActionRow = new VisualElement();
            mcpActionRow.AddToClassList("ai-coding-action-row");
            var mcpSettingsButton = new Button(ConvaiAICodingSetupWindow.OpenUnityMcpSettings)
            {
                text = "Open Unity MCP Server Settings",
                tooltip = "Open Unity's official MCP server settings. Convai does not duplicate Unity-owned client configuration."
            };
            mcpSettingsButton.AddToClassList("button");
            mcpSettingsButton.AddToClassList("ai-coding-primary-button");
            mcpActionRow.Add(mcpSettingsButton);
            healthCard.Add(mcpActionRow);
            Add(healthCard);

            VisualElement instructionsCard = CreateCard("Managed Project Instructions", out _);
            var instructionsHelp = new Label(
                "Writes only after an Install, Update, or Remove click. Existing non-Convai content stays intact.");
            instructionsHelp.AddToClassList("helper-text");
            instructionsHelp.AddToClassList("ai-coding-card-help");
            instructionsCard.Add(instructionsHelp);

            foreach (ConvaiAgentClient client in Enum.GetValues(typeof(ConvaiAgentClient)))
            {
                ClientRow row = CreateClientRow(client);
                _clientRows.Add(row);
                instructionsCard.Add(row.Root);
            }

            Add(instructionsCard);
            RegisterCallback<DetachFromPanelEvent>(_ => StopPolling());
            Refresh();
        }

        protected override void OnSectionShown()
        {
            Refresh();
            StartPolling();
        }

        protected override void OnSectionHidden() => StopPolling();

        private static VisualElement CreateCard(string title, out VisualElement header)
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList("ai-coding-card");

            header = new VisualElement();
            header.AddToClassList("ai-coding-card-header");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ai-coding-card-title");
            header.Add(titleLabel);
            card.Add(header);
            return card;
        }

        private static StatusRow CreateStatusRow(string name, Action repairAction)
        {
            var root = new VisualElement();
            root.AddToClassList("ai-coding-status-row");

            var dot = new VisualElement();
            dot.AddToClassList("ai-coding-status-dot");
            root.Add(dot);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("ai-coding-status-name");
            root.Add(nameLabel);

            var detail = new Label();
            detail.AddToClassList("ai-coding-status-detail");
            root.Add(detail);

            var state = new Label();
            state.AddToClassList("ai-coding-status-state");
            root.Add(state);

            Button fixButton = null;
            if (repairAction != null)
            {
                fixButton = new Button(repairAction) { text = "Fix" };
                fixButton.AddToClassList("ai-coding-secondary-button");
                fixButton.AddToClassList("ai-coding-fix-button");
                root.Add(fixButton);
            }

            return new StatusRow(root, dot, detail, state, fixButton);
        }

        private ClientRow CreateClientRow(ConvaiAgentClient client)
        {
            var root = new VisualElement();
            root.AddToClassList("ai-coding-client-row");

            var name = new Label(ConvaiAICodingSetupWindow.GetClientLabel(client));
            name.AddToClassList("ai-coding-client-name");
            root.Add(name);

            var path = new Label(ConvaiAgentInstructionsInstaller.GetRelativePath(client));
            path.AddToClassList("ai-coding-client-path");
            root.Add(path);

            var state = new Label();
            state.AddToClassList("ai-coding-client-state");
            root.Add(state);

            var actions = new VisualElement();
            actions.AddToClassList("ai-coding-client-actions");

            var installButton = new Button(() => RunInstructionAction(
                () => ConvaiAgentInstructionsInstaller.Upsert(ConvaiAICodingSetupWindow.GetProjectRoot(), client)))
            {
                text = "Install"
            };
            installButton.AddToClassList("ai-coding-secondary-button");
            actions.Add(installButton);

            var removeButton = new Button(() => RunInstructionAction(
                () => ConvaiAgentInstructionsInstaller.Remove(ConvaiAICodingSetupWindow.GetProjectRoot(), client)))
            {
                text = "Remove"
            };
            removeButton.AddToClassList("ai-coding-secondary-button");
            actions.Add(removeButton);
            root.Add(actions);

            return new ClientRow(client, root, state, installButton, removeButton);
        }

        private void Refresh()
        {
            ConvaiAICodingSetupState state = ConvaiAICodingSetupWindow.GetSetupState();
            SetStatus(_unityStatus, state.UnityReady, state.UnityVersion, false, state.RepairRunning);
            SetStatus(
                _assistantStatus,
                state.AssistantReady,
                state.AssistantVersion ?? "Not installed",
                ConvaiAICodingSetupWindow.CanRepairAssistant(state.AssistantVersion),
                state.RepairRunning);
            SetStatus(
                _skillStatus,
                state.SkillReady,
                "AIAssistantSkills/convai-unity-sdk/SKILL.md",
                !state.SkillReady,
                state.RepairRunning);

            string toolsDetail = state.AssistantReady
                ? state.ToolsReady
                    ? $"{state.ToolCount}/{ConvaiAICodingSetupWindow.ExpectedToolCount} registered"
                    : $"{state.ToolCount}/{ConvaiAICodingSetupWindow.ExpectedToolCount}; {state.ToolIssue}"
                : $"{state.ToolCount}/{ConvaiAICodingSetupWindow.ExpectedToolCount}; install Unity AI Assistant first";
            SetStatus(
                _toolsStatus,
                state.ToolsReady,
                toolsDetail,
                ConvaiAICodingSetupWindow.CanRepairToolRegistration(state.AssistantVersion, state.ToolsReady),
                state.RepairRunning);

            RefreshRepairMessage(state);
            foreach (ClientRow row in _clientRows) RefreshClientRow(row);
        }

        private static void SetStatus(
            StatusRow row,
            bool ready,
            string detail,
            bool canRepair,
            bool repairRunning)
        {
            row.Dot.EnableInClassList("ai-coding-status-dot--ok", ready);
            row.Dot.EnableInClassList("ai-coding-status-dot--attention", !ready);
            row.Detail.text = detail ?? string.Empty;
            row.State.text = ready ? "Ready" : "Needs attention";
            row.State.EnableInClassList("ai-coding-status-state--ok", ready);
            row.State.EnableInClassList("ai-coding-status-state--attention", !ready);
            if (row.FixButton == null) return;

            row.FixButton.style.display = ready ? DisplayStyle.None : DisplayStyle.Flex;
            row.FixButton.SetEnabled(canRepair && !repairRunning);
        }

        private void RefreshRepairMessage(ConvaiAICodingSetupState state)
        {
            bool visible = !string.IsNullOrWhiteSpace(state.RepairMessage);
            _repairMessage.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;

            _repairMessage.text = state.RepairMessage;
            _repairMessage.EnableInClassList(
                "ai-coding-message--warning",
                state.RepairMessageType == MessageType.Warning);
            _repairMessage.EnableInClassList(
                "ai-coding-message--error",
                state.RepairMessageType == MessageType.Error);
        }

        private static void RefreshClientRow(ClientRow row)
        {
            bool installed;
            try
            {
                installed = ConvaiAgentInstructionsInstaller.ContainsManagedBlock(
                    ConvaiAICodingSetupWindow.GetProjectRoot(),
                    row.Client);
                row.State.tooltip = string.Empty;
                row.InstallButton.SetEnabled(true);
            }
            catch (InvalidOperationException exception)
            {
                installed = false;
                row.State.text = "Unreadable";
                row.State.tooltip = exception.Message;
                row.State.EnableInClassList("ai-coding-client-state--installed", false);
                row.State.EnableInClassList("ai-coding-client-state--error", true);
                row.InstallButton.SetEnabled(false);
                row.RemoveButton.SetEnabled(false);
                return;
            }

            row.State.text = installed ? "Installed" : "Not installed";
            row.State.EnableInClassList("ai-coding-client-state--installed", installed);
            row.State.EnableInClassList("ai-coding-client-state--error", false);
            row.InstallButton.text = installed ? "Update" : "Install";
            row.RemoveButton.SetEnabled(installed);
        }

        private void RunInstructionAction(Action action)
        {
            try
            {
                action();
                AssetDatabase.Refresh();
                Refresh();
            }
            catch (Exception exception)
            {
                ConvaiLogger.Error($"AI Coding Setup failed: {exception.Message}", LogCategory.Editor);
                EditorUtility.DisplayDialog("Convai AI Coding Setup", exception.Message, "OK");
            }
        }

        private void StartPolling()
        {
            EditorApplication.update -= PollSetupState;
            _nextRefreshAt = 0d;
            EditorApplication.update += PollSetupState;
        }

        private void StopPolling() => EditorApplication.update -= PollSetupState;

        private void PollSetupState()
        {
            if (!IsSectionVisible || EditorApplication.timeSinceStartup < _nextRefreshAt) return;
            _nextRefreshAt = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
            Refresh();
        }

        private sealed class StatusRow
        {
            internal StatusRow(
                VisualElement root,
                VisualElement dot,
                Label detail,
                Label state,
                Button fixButton)
            {
                Root = root;
                Dot = dot;
                Detail = detail;
                State = state;
                FixButton = fixButton;
            }

            internal VisualElement Root { get; }
            internal VisualElement Dot { get; }
            internal Label Detail { get; }
            internal Label State { get; }
            internal Button FixButton { get; }
        }

        private sealed class ClientRow
        {
            internal ClientRow(
                ConvaiAgentClient client,
                VisualElement root,
                Label state,
                Button installButton,
                Button removeButton)
            {
                Client = client;
                Root = root;
                State = state;
                InstallButton = installButton;
                RemoveButton = removeButton;
            }

            internal ConvaiAgentClient Client { get; }
            internal VisualElement Root { get; }
            internal Label State { get; }
            internal Button InstallButton { get; }
            internal Button RemoveButton { get; }
        }
    }
}
