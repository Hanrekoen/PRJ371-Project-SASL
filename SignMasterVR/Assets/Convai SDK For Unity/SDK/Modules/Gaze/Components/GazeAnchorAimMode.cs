namespace Convai.Modules.Gaze.Components
{
    /// <summary>How the conversational focus point is derived from its anchor transform.</summary>
    public enum GazeAnchorAimMode
    {
        /// <summary>Uses a camera's exact position and applies the conventional eye-line lift to other anchors.</summary>
        Auto = 0,

        /// <summary>Uses the anchor's exact world position.</summary>
        ExactTransform = 1,

        /// <summary>Transforms <c>PlayerAnchorAimOffset</c> from anchor-local to world space.</summary>
        LocalOffset = 2
    }
}
