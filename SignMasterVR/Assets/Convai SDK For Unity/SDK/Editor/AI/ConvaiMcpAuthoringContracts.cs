using Convai.Runtime.Room;
using Convai.Runtime.Vision.Context;
using Convai.Shared.Types;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Convai.Editor.AI
{
    /// <summary>Configuration source used by Convai authoring tools.</summary>
    public enum ConvaiToolConfigurationMode
    {
        Inline,
        ExistingProfile
    }

    /// <summary>Input for <c>Convai.ConfigureRoom</c>.</summary>
    public sealed class ConvaiConfigureRoomRequest
    {
        [McpDescription("GameObject instance ID that owns or will own ConvaiManager and ConvaiRoomManager.")]
        public long TargetInstanceId { get; set; }

        [McpDescription("Use inline scene settings or assign an existing profile.", Default = ConvaiToolConfigurationMode.Inline)]
        public ConvaiToolConfigurationMode ConfigurationMode { get; set; } = ConvaiToolConfigurationMode.Inline;

        [McpDescription("Existing ConvaiRoomManagerProfile asset path. Required in ExistingProfile mode.")]
        public string ProfileAssetPath { get; set; } = string.Empty;

        [McpDescription("Inline connection type.", Default = ConvaiConnectionType.Audio)]
        public ConvaiConnectionType ConnectionType { get; set; } = ConvaiConnectionType.Audio;

        [McpDescription("Inline conversation input mode.", Default = ConversationInputMode.HandsFree)]
        public ConversationInputMode InputMode { get; set; } = ConversationInputMode.HandsFree;

        [McpDescription("Connect automatically when the scene starts.", Default = true)]
        public bool ConnectOnStart { get; set; } = true;

        [McpDescription("Core-service endpoint.", Default = ConvaiServerEndpoint.Connect)]
        public ConvaiServerEndpoint ServerEndpoint { get; set; } = ConvaiServerEndpoint.Connect;

        [McpDescription("Dynamic vision policy.", Default = ConvaiVisionContextMode.Auto)]
        public ConvaiVisionContextMode VisionMode { get; set; } = ConvaiVisionContextMode.Auto;

        [McpDescription("Unity KeyCode name used by push-to-talk.", Default = "T")]
        public string PushToTalkKey { get; set; } = "T";

        [McpDescription("Preview changes without modifying the scene.", Default = true)]
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.ConfigurePlayer</c>.</summary>
    public sealed class ConvaiConfigurePlayerRequest
    {
        [McpDescription("Target player GameObject instance ID.")]
        public long TargetInstanceId { get; set; }

        [McpDescription("Optional manager GameObject instance ID. Zero auto-resolves one manager in the target scene.")]
        public long ManagerInstanceId { get; set; }

        [McpDescription("Player display name.", Default = "Player")]
        public string PlayerName { get; set; } = "Player";

        [McpDescription("Optional local transcript attribution ID. Defaults to player name.")]
        public string PlayerId { get; set; } = string.Empty;

        [McpDescription("Preview changes without modifying the scene.", Default = true)]
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.ConfigureCharacter</c>.</summary>
    public sealed class ConvaiConfigureCharacterRequest
    {
        [McpDescription("Target character GameObject instance ID.")]
        public long TargetInstanceId { get; set; }

        [McpDescription("Optional manager GameObject instance ID. Zero auto-resolves one manager in the target scene.")]
        public long ManagerInstanceId { get; set; }

        [McpDescription("Use inline scene settings or assign an existing profile.", Default = ConvaiToolConfigurationMode.Inline)]
        public ConvaiToolConfigurationMode ConfigurationMode { get; set; } = ConvaiToolConfigurationMode.Inline;

        [McpDescription("Existing ConvaiCharacterProfile asset path. Required in ExistingProfile mode.")]
        public string ProfileAssetPath { get; set; } = string.Empty;

        [McpDescription("Convai dashboard Character ID. May be omitted while authoring an incomplete placeholder.")]
        public string CharacterId { get; set; } = string.Empty;

        [McpDescription("Character display name. Defaults to target GameObject name.")]
        public string CharacterName { get; set; } = string.Empty;

        [McpDescription("Ensure AudioSource and ConvaiAudioOutput companions.", Default = true)]
        public bool AddAudioOutput { get; set; } = true;

        [McpDescription("Preview changes without modifying the scene.", Default = true)]
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.SetupConversationScene</c>.</summary>
    public sealed class ConvaiSetupConversationSceneRequest
    {
        [McpDescription("Optional manager target GameObject instance ID.")]
        public long ManagerInstanceId { get; set; }

        [McpDescription("Optional player target GameObject instance ID.")]
        public long PlayerInstanceId { get; set; }

        [McpDescription("Optional character target GameObject instance ID.")]
        public long CharacterInstanceId { get; set; }

        [McpDescription("Optional existing ConvaiRoomManagerProfile path.")]
        public string RoomProfileAssetPath { get; set; } = string.Empty;

        [McpDescription("Optional existing ConvaiCharacterProfile path.")]
        public string CharacterProfileAssetPath { get; set; } = string.Empty;

        [McpDescription("Convai dashboard Character ID. May be omitted until all independent setup is complete.")]
        public string CharacterId { get; set; } = string.Empty;

        [McpDescription("Character display name.", Default = "Convai Character")]
        public string CharacterName { get; set; } = "Convai Character";

        [McpDescription("Player display name.", Default = "Player")]
        public string PlayerName { get; set; } = "Player";

        [McpDescription("Optional local transcript attribution ID.")]
        public string PlayerId { get; set; } = string.Empty;

        [McpDescription("Recommended room input mode.", Default = ConversationInputMode.HandsFree)]
        public ConversationInputMode InputMode { get; set; } = ConversationInputMode.HandsFree;

        [McpDescription("Connect automatically when the scene starts.", Default = true)]
        public bool ConnectOnStart { get; set; } = true;

        [McpDescription("Create standalone player and capsule character placeholders when none exist.", Default = true)]
        public bool CreatePlaceholders { get; set; } = true;

        [McpDescription("Preview changes without modifying the scene.", Default = true)]
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseConversation</c>.</summary>
    public sealed class ConvaiDiagnoseConversationRequest
    {
        [McpDescription("Optional character GameObject instance ID to focus diagnostics on.")]
        public long CharacterInstanceId { get; set; }

        [McpDescription("Include inactive GameObjects and components.", Default = true)]
        public bool IncludeInactive { get; set; } = true;
    }
}
