using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     Every user-visible string in the Gaze editor window, in one place.
    /// </summary>
    /// <remarks>
    ///     Separated from the drawing code for the same reason
    ///     <c>ConvaiActionsEditorStrings</c> and <c>BodyAnimationEditorStrings</c> are: wording is
    ///     the part of this surface most likely to be reviewed and revised, and hunting it through
    ///     layout code guarantees the wording quietly drifts between surfaces.
    /// </remarks>
    internal static class GazeEditorStrings
    {
        internal const string WindowTitle = "Convai Gaze";

        internal const string HeroTitle = "Convai Gaze";
        internal const string HeroSubtitle = "Where your characters look, and how it feels";

        internal static readonly GUIContent ModeSetup = new("Setup", "Rig report, what is missing, and the optional extras.");
        internal static readonly GUIContent ModeFeel = new("Feel", "The full personality surface behind the three dials.");
        internal static readonly GUIContent ModeTargets = new("Targets", "Everything in the scene worth looking at.");
        internal static readonly GUIContent ModeLive = new("Live", "What the character is doing right now.");

        internal const string LeftPaneTitle = "CHARACTERS";

        internal const string NoControllersTitle = "No characters with Gaze";
        internal const string NoControllersBody =
            "Select a Convai character and use Add Component → Convai → Embodiment → Gaze. " +
            "That is the only step — the character will look at the player as soon as you press Play.";

        internal const string CardReady = "Ready";
        internal const string CardNotWorking = "Not working";

        internal const string GreyCardHint =
            "This character's rig cannot drive gaze yet. Select it and use the Setup tab.";

        // ------------------------------------------------------------------ setup mode

        internal const string SetupChecklistTitle = "This character";
        internal const string SetupRigReportTitle = "Rig report";
        internal const string SetupExtrasTitle = "Optional extras";
        internal const string SetupExtrasBody =
            "Gaze already gives this character eyes, a head, idle life, blinking and body turns. " +
            "These are the extras — each is a small component, and none is added unless you ask.";

        internal const string ForwardAxisTitle = "Facing direction";
        internal const string ForwardAxisPass =
            "The head bone faces the same way the character does. This is the convention gaze needs.";
        internal const string ForwardAxisFail =
            "The head bone's forward axis does not match the character's. Gaze will aim in the wrong " +
            "direction. Re-export the rig with the head's local +Z pointing out of the face and +Y up.";
        internal const string ForwardAxisUnknown =
            "No head bone resolved yet, so there is nothing to measure. Assign Head on the " +
            "character's Character Rig, or use a Humanoid avatar.";
        internal const string ForwardAxisCalibrated =
            "This rig has authored gaze axes, so its facing direction is whatever the Standard Rig " +
            "Binding says it is. Nothing to infer here.";

        // ------------------------------------------------------------------ feel mode

        internal const string FeelNoProfileTitle = "Using the SDK defaults";
        internal const string FeelNoProfileBody =
            "This character has no Gaze Profile, so it uses the built-in defaults — which work. " +
            "Assign one to give it a personality of its own.";

        internal const string FeelSharedNotice =
            "This profile is shared. Changing it changes every character using it.";

        // ------------------------------------------------------------------ targets mode

        internal const string TargetsTitle = "Worth looking at";
        internal const string TargetsBody =
            "Characters glance at these while idle. The player still wins during a conversation, " +
            "unless a target is marked more important.";
        internal const string TargetsEmpty =
            "Nothing in this scene is marked as worth looking at. Select an object and press the " +
            "button below — a vase, a painting, a screen. Characters will notice it.";
        internal const string TargetsAddButton = "Mark the selected object";
        internal const string TargetsAdvancedTitle = "Advanced targeting";
        internal const string TargetsAdvancedBody =
            "Settings almost no project needs. They are here rather than on the component so the " +
            "common path stays two controls.";

        // ------------------------------------------------------------------ live mode

        internal const string LiveOfflineTitle = "Not running";
        internal const string LiveOfflineBody = "Enter Play Mode to watch what this character is looking at.";
    }
}
