using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Shows live runtime diagnostics during play mode.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiRuntimeGraphSection : ConvaiBaseSection
    {
        public const string SECTION_NAME = "live-diagnostics";

        private readonly Label _configLabel;
        private readonly Label _graphLabel;
        private readonly Label _healthLabel;
        private readonly Label _statusLabel;
        private IVisualElementScheduledItem _refreshItem;

        public ConvaiRuntimeGraphSection() : this(null)
        {
        }

        public ConvaiRuntimeGraphSection(ConfigurationWindowContext context)
        {
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("live-diagnostics-header", "Live Diagnostics", "header"));

            _statusLabel = ConvaiVisualElementUtility.CreateLabel(
                "live-diagnostics-status",
                "Enter Play Mode to inspect live runtime diagnostics.",
                "helper-text");
            Add(_statusLabel);

            _configLabel = CreateCardLabel("live-diagnostics-config");
            _graphLabel = CreateCardLabel("live-diagnostics-graph");
            _healthLabel = CreateCardLabel("live-diagnostics-health");

            Add(CreateCard("Config", _configLabel));
            Add(CreateCard("Graph", _graphLabel));
            Add(CreateCard("Health", _healthLabel));

            Refresh();
        }

        protected override void OnSectionShown()
        {
            Refresh();
            _refreshItem ??= schedule.Execute(Refresh).Every(500);
            _refreshItem.Resume();
        }

        protected override void OnSectionHidden() => _refreshItem?.Pause();

        private void Refresh()
        {
            if (!UnityEngine.Application.isPlaying)
            {
                _statusLabel.text = "Enter Play Mode to inspect live runtime diagnostics.";
                _configLabel.text = "Config: unavailable outside Play Mode.";
                _graphLabel.text = "Graph: unavailable outside Play Mode.";
                _healthLabel.text = "Health: unavailable outside Play Mode.";
                return;
            }

            var manager = Object.FindAnyObjectByType<ConvaiManager>();
            var roomManager = Object.FindAnyObjectByType<ConvaiRoomManager>();

            if (!TryCaptureDiagnostics(manager, out ConvaiRuntimeDiagnosticsSnapshot snapshot))
            {
                _statusLabel.text = manager == null
                    ? "Live diagnostics unavailable: no active ConvaiManager found."
                    : "Live diagnostics unavailable: diagnostics service not registered yet.";

                _configLabel.text =
                    $"Manager: {(manager != null ? "present" : "missing")}\nRoomManager: {(roomManager != null ? "present" : "missing")}";
                _graphLabel.text = roomManager == null
                    ? "No live room manager found."
                    : $"Session state: {roomManager.CurrentState}\nPending reconnect: {roomManager.HasPendingOwnershipReconnect}";
                _healthLabel.text = roomManager == null
                    ? "No room/session health available."
                    : $"Connect attempts: {roomManager.ConnectAttemptCount}\nReconnects: {roomManager.ReconnectCount}\nLast error: {roomManager.LastSessionErrorCode} {roomManager.LastSessionErrorMessage}"
                        .Trim();
                return;
            }

            _statusLabel.text = "Diagnostics service active. Snapshot refreshes while this section is visible.";
            _configLabel.text =
                $"Project Settings: {snapshot.Config.ProjectSettingsSource}\n" +
                $"Credentials: {snapshot.Config.RuntimeCredentialProviderType} ({(snapshot.Config.RuntimeCredentialsConfigured ? "configured" : "missing")})\n" +
                $"Identity: {snapshot.Config.EndUserIdentityProviderType}\n" +
                $"Session persistence: {snapshot.Config.SessionPersistenceType}\n" +
                $"Room Manager Profile: {snapshot.Config.RoomConfigName}\n" +
                $"Character Profile assets: {string.Join(", ", snapshot.Config.AgentDefinitionNames)}\n" +
                $"Effective room setup: {snapshot.Config.EffectiveConnectionType} / {snapshot.Config.EffectiveServerEndpoint}\n" +
                $"Connect on start: {snapshot.Config.EffectiveConnectOnStart}\n" +
                $"Reconnect policy: {snapshot.Config.EffectiveReconnectPolicy}";

            _graphLabel.text =
                $"Runtime context: {snapshot.Graph.HasRuntimeContext}\n" +
                $"Manager: {snapshot.Graph.HasManager}\n" +
                $"Room manager: {snapshot.Graph.HasRoomManager}\n" +
                $"Player: {snapshot.Graph.OwnedPlayerName}\n" +
                $"Owned characters: {string.Join(", ", snapshot.Graph.OwnedCharacterIds)}\n" +
                $"Active conversation target: {snapshot.Graph.ActiveConversationCharacterId}\n" +
                $"Modules: {string.Join(", ", snapshot.Graph.ModuleParticipants)}\n" +
                $"Room controller: {snapshot.Graph.RoomControllerType}\n" +
                $"Transport accessor: {snapshot.Graph.TransportAccessorType}";

            _healthLabel.text =
                $"Session state: {snapshot.Health.SessionState}\n" +
                $"Connected: {snapshot.Health.IsConnected}\n" +
                $"Has room details: {snapshot.Health.HasRoomDetails}\n" +
                $"Room: {snapshot.Health.RoomName}\n" +
                $"Session id: {snapshot.Health.SessionId}\n" +
                $"Character session id: {snapshot.Health.CharacterSessionId}\n" +
                $"Pending ownership reconnect: {snapshot.Health.HasPendingOwnershipReconnect}\n" +
                $"Connect attempts: {snapshot.Health.ConnectAttemptCount}\n" +
                $"Reconnect count: {snapshot.Health.ReconnectCount}\n" +
                $"Last error: {snapshot.Health.LastSessionErrorCode} {snapshot.Health.LastSessionErrorMessage}".Trim() +
                $"\nLast ownership rebind: {snapshot.Health.LastOwnershipRebindOutcome}\n" +
                $"Last successful connection: {snapshot.Health.LastSuccessfulConnectionUtc}";
        }

        private static bool TryCaptureDiagnostics(ConvaiManager manager, out ConvaiRuntimeDiagnosticsSnapshot snapshot)
        {
            snapshot = default;
            if (manager == null) return false;

            if (!manager.TryGetDiagnosticsService(out IConvaiRuntimeDiagnosticsService diagnosticsService) ||
                diagnosticsService == null)
                return false;

            snapshot = diagnosticsService.CaptureSnapshot();
            return true;
        }

        private static VisualElement CreateCard(string title, Label content)
        {
            VisualElement card = new();
            card.AddToClassList("card");
            card.style.marginTop = 12;
            card.Add(ConvaiVisualElementUtility.CreateLabel(title.ToLowerInvariant() + "-title", title, "subheader"));
            card.Add(content);
            return card;
        }

        private static Label CreateCardLabel(string name)
        {
            Label label = ConvaiVisualElementUtility.CreateLabel(name, string.Empty, "helper-text");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            return label;
        }
    }
}
