using Convai.Editor.UI;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Every user-facing string <see cref="ConvaiBodyAnimationEditorWindow" /> draws, in one place —
    ///     the same separation <c>ConvaiActionsEditorStrings</c> uses, so wording changes never
    ///     require hunting through draw code and every label stays in professional, plain language
    ///     (no engine jargon).
    /// </summary>
    internal static class BodyAnimationEditorStrings
    {
        // ------------------------------------------------------------------ window / hero

        internal const string WindowTitle = "Body Animation Editor";
        internal const string HeroTitle = "Body Animation Editor";
        internal const string HeroSubtitle = "Content, personality & live monitor — depth beyond the inspector.";

        internal static readonly GUIContent ModeSetup = new("Setup", "Rig, content and movement checklist — the same one the component inspector shows.");
        internal static readonly GUIContent ModeContent = new("Content", "Author pools, locomotion, actions, gestures and pointing directions.");
        internal static readonly GUIContent ModeFeel = new("Feel", "Archetypes, personality sliders, and the complete behavior config.");
        internal static readonly GUIContent ModeLive = new("Live", "Play Mode only: layer weights, foot slide, and the transition log.");

        // ------------------------------------------------------------------ left pane

        internal const string LeftPaneTitle = "Characters";
        internal const string NoControllersTitle = "No Body Animation Characters";

        internal const string NoControllersBody =
            "No character in the open scenes has a Body Animation component yet. Select a character's " +
            "GameObject and use Add Component → Convai → Embodiment → Body Animation — that is Unity's " +
            "own gesture, not something this window does for you.";

        internal const string GreyCardHint =
            "This GameObject has no Body Animation component. Add it with Add Component → Convai → " +
            "Embodiment → Body Animation, then it appears here as a configurable character.";

        internal const string CardReady = "Ready";
        internal const string CardNeedsAttention = "Needs Attention";
        internal const string CardNotSetUp = "Not Set Up";

        // ------------------------------------------------------------------ setup mode

        internal const string SetupModeTitle = "Setup & Troubleshooting";

        internal const string SetupModeIntro =
            "This mirrors the component inspector's own checklist in full width — everything here can " +
            "also be resolved directly on the inspector without ever opening this window.";

        internal const string SetupPreflightHeader = "Before Setup";
        internal const string SetupFindingsHeader = "Findings";
        internal const string SetupAllGoodMessage = "No findings — this character is fully configured.";
        internal const string SetupNoControllerMessage = "Select a character in the list on the left to see its setup status.";
        internal const string SetupIncludeMovementLabel = "Include movement (walking, turns, stops)";

        internal const string SetupIncludeMovementTooltip =
            "Adds NavMesh movement so the character can walk to places. Leave off for a stationary " +
            "character — it will still idle, talk, gesture, and point.";

        internal const string SetupRunButton = "Set Up This Character";
        internal const string SetupBlockedButton = "Resolve the blocked item above first";

        // ------------------------------------------------------------------ content mode

        internal const string ContentModeNoController = "Select a character in the list on the left to author its content.";

        internal const string ContentModeNoSet =
            "No animation content is assigned yet. Assign a Profile or Animation Set on the component " +
            "inspector, or create one from a clip folder below.";

        internal const string PoolIdleTitle = "Idle";
        internal const string PoolTalkTitle = "Talk";
        internal const string PoolListenTitle = "Listen";
        internal const string PoolThinkTitle = "Think";

        internal const string PoolListenEmptyHint = "No Listen clips authored — listening acting stays inactive.";
        internal const string PoolThinkEmptyHint = "No Think clips authored — thinking acting stays inactive.";
        internal const string PoolTalkEmptyHint = "No Talk clips authored — the character stays in its idle pose while speaking.";
        internal const string PoolIdleEmptyHint = "No Idle clips authored — the base layer has nothing to play.";

        internal const string WeightFieldLabel = "Weight";
        internal const string AdditiveFieldLabel = "Additive";
        internal const string MovingTalkClipLabel = "Walk-and-talk clip";

        internal const string MovingTalkTierAdditive =
            "Additive twin assigned — while the character walks, this gesture layers over the arm swing.";

        internal const string MovingTalkTierSelfAdditive =
            "The main clip is already additive — it is used directly while the character walks.";

        internal const string MovingTalkTierFallback =
            "No additive twin — while the character walks, the gesture is blended into the walk at reduced " +
            "weight instead. Bake an additive twin here for the stronger walk-and-talk result.";

        internal const string LoopOkLabel = "Loops";
        internal const string LoopBadLabel = "Not set to loop";
        internal const string LoopNoneLabel = "—";

        internal static readonly GUIContent PreviewButton = new("Preview", "Samples this clip on the character in Edit Mode.");
        internal static readonly GUIContent StopPreviewButton = new("Stop", "Stops the Edit-Mode preview and restores the original pose.");
        internal static readonly GUIContent LayeredPreviewButton = new("Preview With Idle", "Previews this clip layered over the resolved idle pose through the upper-body mask.");

        internal const string AddEntryButton = "+ Add";
        internal static readonly GUIContent RemoveEntryButton = new(ConvaiEditorGlyphs.Affordance.Remove, "Remove this entry.");

        internal const string LocomotionGridTitle = "Locomotion Coverage";

        internal const string LocomotionGridIntro =
            "Only Walk is required for basic movement. Every other cell unlocks one advanced feature — " +
            "an empty cell states what stays off instead of listing 26 bare fields.";

        internal const string LocomotionColLoop = "Loop";
        internal const string LocomotionColStarts = "Starts";
        internal const string LocomotionColStops = "Stops";
        internal const string LocomotionColSpeed = "Speed Changes";
        internal const string LocomotionColTurns = "Turns";
        internal const string LocomotionRowWalk = "Walk";
        internal const string LocomotionRowJog = "Jog";
        internal const string LocomotionTurnsSharedNote = "Shared with the Walk row above — one set of turn-in-place clips serves both gaits.";

        internal const string ActionsListTitle = "Actions & Gestures";
        internal const string ActionsColName = "Name";
        internal const string ActionsColClip = "Clip";
        internal const string ActionsColCue = "Cue";
        internal const string AddActionButton = "+ Add Action";
        internal const string ActionsEmptyHint = "No actions authored yet — backend action calls and beat/referential gestures have nothing to play.";

        internal const string PointingTitle = "Pointing";
        internal const string AddDirectionButton = "+ Add Direction";

        internal const string PointingEmptyHint =
            "No pointing directions authored — pointing requests fall back to a straight-arm procedural aim.";

        internal const string PointingCompassFront = "Front";
        internal const string PointingCompassBack = "Back";
        internal const string PointingCompassLeft = "Left";
        internal const string PointingCompassRight = "Right";

        internal static readonly GUIContent CreateAnimationSetButton =
            new("Create Animation Set…", "Opens the set-authoring wizard: pick a clip folder, auto-match names to slots, generate a mask, and measure clip motion.");

        internal static readonly GUIContent MeasureClipsButton =
            new("Measure Clips", "Runs the Clip Motion Analyzer over every assigned locomotion clip — required for zero-slide NavMesh sync.");

        internal const string MeasureClipsDoneTitle = "Clip Metadata";
        internal const string MeasureClipsDoneBody = "Locomotion clip motion has been measured and saved.";

        // ------------------------------------------------------------------ feel mode

        internal const string FeelModeNoController = "Select a character in the list on the left to tune its personality.";

        internal const string FeelModeNoConfig =
            "This character has no Body Animation Config asset, so built-in defaults are used. Assign a " +
            "config on the component inspector to unlock these controls.";

        internal const string FeelPersonalityHeader = "Personality";
        internal const string FeelFullConfigHeader = "Complete Behavior Config";

        internal const string FeelFullConfigIntro =
            "Every field the runtime reads, grouped in plain language. The three sliders above move a " +
            "handful of these; everything else lives only here.";


        internal const string FeelFeatureInertBadge =
            "Turned on, but this animation set has no clip tagged for it — nothing will play. " +
            "Tag a clip in the Content tab, or turn this off.";

        internal const string FeelFeatureDormantBadge =
            "This animation set HAS clips tagged for this, but the setting is off — they are never " +
            "played. Turn it on to use them.";

        internal const string FeelReferentialFallbackNote =
            "No referential clip is tagged in this set, so the cue is handed to a peer performer " +
            "(Convai Body Language performs it procedurally). Tag clips here for the stronger, " +
            "authored version.";

        internal const string FeelMovingTalkFallbackNote =
            "No talk entry has an additive walk-and-talk twin, so \"Best available\" blends the " +
            "gesture into the walk at reduced weight. That is the intended fallback, not a fault.";

        // ------------------------------------------------------------------ live mode

        internal const string LiveModeNoController = "Select a character in the list on the left to watch it live.";
        internal const string LiveNotPlayingMessage = "Enter Play Mode to see live layer weights, foot slide, and the transition log.";

        internal const string LiveNotBuiltMessage =
            "The runtime graph has not built for this character yet — check the Setup tab for a missing rig or content.";

        internal const string LiveLayerWeightsHeader = "Layer Weights";
        internal const string LiveFootSlideLabel = "Foot Slide";
        internal const string LiveDialogueStateLabel = "Dialogue";
        internal const string LiveLocomotionStateLabel = "Movement";
        internal const string LiveDesiredSpeedLabel = "Desired Speed";
        internal const string LiveRemainingDistanceLabel = "Remaining Distance";
        internal const string LiveTransitionLogHeader = "Recent Transitions";
        internal const string LiveTransitionLogEmpty = "No transitions recorded yet.";

        // ------------------------------------------------------------------ builders

        internal static string BuildLocomotionCellLabel(int filled, int total) => $"{filled}/{total}";

    }
}
