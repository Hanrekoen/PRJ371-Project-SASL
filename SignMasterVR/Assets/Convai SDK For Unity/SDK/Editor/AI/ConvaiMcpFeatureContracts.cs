using Convai.Runtime.Actions;
using Convai.Shared.Types;
using System.Collections.Generic;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Convai.Editor.AI
{
    public enum ConvaiTranscriptToolMode { EventRelay = 1, ChatUI = 2, WorldSpaceChatUI = 3 }

    public sealed class ConvaiActionDefinitionInput
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ConvaiActionParameterInput[] Parameters { get; set; } = System.Array.Empty<ConvaiActionParameterInput>();
        public ConvaiActionTargetRequirement TargetRequirement { get; set; }
        public long ExecutorInstanceId { get; set; }
        public float TimeoutSeconds { get; set; }
        public bool WaitForBotSpeech { get; set; }
        public float DelayAfterBotSpeechSeconds { get; set; }

        /// <summary>
        ///     Availability flag: whether the Convai Character knows about and offers
        ///     this action. Null leaves the authored value unchanged (new definitions default to
        ///     enabled), mirroring <c>ConvaiActionDefinition.Enabled</c>.
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        ///     Authoring category this action is filed under in the Actions Editor, mirroring
        ///     <c>ConvaiActionDefinition.Category</c>. Null leaves the authored value unchanged; an
        ///     empty string files the action as uncategorized. Organization only — the category is
        ///     never sent to Convai and never changes what the character does.
        /// </summary>
        public string Category { get; set; }
    }

    public sealed class ConvaiActionParameterInput
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ConvaiActionParameterType Type { get; set; } = ConvaiActionParameterType.Auto;
        public string Connector { get; set; } = string.Empty;
        public string[] Choices { get; set; } = System.Array.Empty<string>();
    }

    public sealed class ConvaiActionTargetInput
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long GameObjectInstanceId { get; set; }
    }

    public sealed class ConvaiConfigureActionsRequest
    {
        [McpDescription("Character GameObject instance ID.")]
        public long CharacterInstanceId { get; set; }
        public ConvaiActionDefinitionInput[] Definitions { get; set; } = System.Array.Empty<ConvaiActionDefinitionInput>();
        public ConvaiActionTargetInput[] Objects { get; set; } = System.Array.Empty<ConvaiActionTargetInput>();
        public ConvaiActionTargetInput[] Characters { get; set; } = System.Array.Empty<ConvaiActionTargetInput>();
        public string InitialAttentionObject { get; set; } = string.Empty;
        public bool DryRun { get; set; } = true;
    }

    public sealed class ConvaiDiagnoseActionsRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeInactive { get; set; } = true;
    }

    public sealed class ConvaiSimulateActionRequest
    {
        public long CharacterInstanceId { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
        public float TimeoutSeconds { get; set; } = 10f;
    }

    public sealed class ConvaiConfigureTranscriptsRequest
    {
        public long ManagerInstanceId { get; set; }
        public long HostInstanceId { get; set; }
        public ConvaiTranscriptToolMode Mode { get; set; } = ConvaiTranscriptToolMode.EventRelay;
        public bool FinalOnly { get; set; }
        public bool IgnoreInterim { get; set; } = true;
        public string CharacterIdFilter { get; set; } = string.Empty;
        public bool DryRun { get; set; } = true;
    }

    public sealed class ConvaiDiagnoseTranscriptsRequest
    {
        public long ManagerInstanceId { get; set; }
        public bool IncludeText { get; set; }
    }
}
