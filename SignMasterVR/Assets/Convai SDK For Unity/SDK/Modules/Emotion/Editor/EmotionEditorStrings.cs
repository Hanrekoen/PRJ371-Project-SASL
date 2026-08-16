using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Every user-visible string in the Emotion editor window, in one place.
    /// </summary>
    /// <remarks>
    ///     Separated from the drawing code for the same reason the other module windows do it: so
    ///     the product's wording can be reviewed as prose, without reading GUI layout around it.
    /// </remarks>
    internal static class EmotionEditorStrings
    {
        internal const string WindowTitle = "Convai Emotions";

        internal static readonly GUIContent HeroTitle = new("Emotions");
        internal static readonly GUIContent HeroSubtitle =
            new("Facial expression, mood and reactions");

        internal static readonly GUIContent ModeSetup = new("Setup",
            "Is this character ready, and what is missing.");
        internal static readonly GUIContent ModeFeel = new("Feel",
            "Every setting behind the inspector's handful of controls.");
        internal static readonly GUIContent ModeExpressions = new("Expressions",
            "What each emotion does to this character's face.");
        internal static readonly GUIContent ModeLive = new("Live",
            "What the character is feeling right now.");

        internal const string LeftPaneTitle = "CHARACTERS";

        internal const string NoControllersTitle = "No characters with emotions";
        internal const string NoControllersBody =
            "Add a Convai Emotion Controller to a character, then come back here.";

        internal const string CardReady = "Ready";
        internal const string CardNeedsAttention = "Needs attention";
        internal const string CardNotSetUp = "Not set up";
        internal const string GreyCardHint =
            "This character has no personality yet. Select it and run setup from its Inspector.";

        internal const string SetupIntro =
            "Everything this character needs before its face can react to the conversation.";
        internal const string FeelIntro =
            "The complete settings surface. The Inspector carries the handful that matter most; " +
            "everything else lives here.";
        internal const string ExpressionsIntro =
            "Expressions describe what should move — a smile, a raised brow — rather than naming " +
            "blendshapes on one particular mesh. Convai resolves them against whichever shapes this " +
            "character's own face has, so one personality works across every supported face rig.";
        internal const string LiveIntro =
            "Live readings come from the running character. Enter Play Mode to see them.";

        internal const string NoProfileTitle = "No personality assigned";
        internal const string NoProfileBody =
            "This character is using the built-in defaults, so there is nothing to tune here. Run " +
            "setup from the character's Inspector to give it a personality of its own.";
    }
}
