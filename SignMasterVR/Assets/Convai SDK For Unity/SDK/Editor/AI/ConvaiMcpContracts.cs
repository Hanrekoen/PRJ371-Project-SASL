using Unity.AI.MCP.Editor.ToolRegistry;

namespace Convai.Editor.AI
{
    /// <summary>Guidance topics available to AI clients.</summary>
    public enum ConvaiGuidanceTopic
    {
        Overview,
        Setup,
        Actions,
        DynamicContext,
        Vision,
        Narrative,
        Embodiment,
        Events,
        Runtime,
        Gaze,
        BodyAnimation,
        BodyLanguage,
        Emotion
    }

    /// <summary>Validation scopes supported by the foundation MCP tools.</summary>
    public enum ConvaiValidationScope
    {
        All,
        Project,
        Scene
    }

    /// <summary>Input for <c>Convai.GetGuidance</c>.</summary>
    public sealed class ConvaiGuidanceRequest
    {
        [McpDescription("Convai workflow topic to load.", Default = ConvaiGuidanceTopic.Overview)]
        public ConvaiGuidanceTopic Topic { get; set; } = ConvaiGuidanceTopic.Overview;
    }

    /// <summary>Input for <c>Convai.InspectScene</c>.</summary>
    public sealed class ConvaiSceneInspectionRequest
    {
        [McpDescription("Include disabled GameObjects and components in the inspection.", Default = true)]
        public bool IncludeInactive { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.ValidateSetup</c>.</summary>
    public sealed class ConvaiValidationRequest
    {
        [McpDescription("Validation scope.", Default = ConvaiValidationScope.All)]
        public ConvaiValidationScope Scope { get; set; } = ConvaiValidationScope.All;
    }

    /// <summary>Input for <c>Convai.BootstrapScene</c>.</summary>
    public sealed class ConvaiBootstrapRequest
    {
        [McpDescription("Preview required changes without modifying the scene.", Default = false)]
        public bool DryRun { get; set; }
    }
}
