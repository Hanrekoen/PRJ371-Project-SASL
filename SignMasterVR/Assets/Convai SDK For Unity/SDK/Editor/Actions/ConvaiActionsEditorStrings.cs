using System.Collections.Generic;
using Convai.Runtime.Actions;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Every user-facing label and tooltip shown by <see cref="ConvaiActionsEditorWindow" />, the
    ///     shrunk <see cref="Convai.Editor.Inspectors.ConvaiActionConfigSourceEditor" /> summary card,
    ///     and the terminology touch-ups on the Action Troubleshooter. Centralizing the user-facing
    ///     text here is what makes the house naming rules checkable: there is no gaming jargon in any
    ///     label or tooltip, and the standalone words "AI" (say "Convai Character") and "executor" (say
    ///     "Action Behavior") appear nowhere in this table — engineer hints may only cite full
    ///     compound API identifiers such as "IConvaiActionExecutor" (guarded by
    ///     <c>ConvaiActionsEditorStringsTests</c>). Every field here is a cached <see cref="GUIContent" /> so drawing code
    ///     never allocates one for static text; dynamic (count/name-composed) text is built fresh by
    ///     the small helper methods at the bottom, each of which still carries a fixed, cached tooltip.
    /// </summary>
    internal static class ConvaiActionsEditorStrings
    {
        #region Toolbar

        internal static readonly GUIContent AddActionButton = new(
            "+ Add Action ▾",
            "Add a new action this Convai Character can be asked to perform. Choose a ready-made " +
            "option below, or start from a blank Custom action.");

        internal static readonly GUIContent CustomActionMenuItem = new(
            "Custom (Empty)",
            "Start a blank action with no preset name, behavior, or parameters.");

        internal const string RecommendedActionsMenuSection = "Recommended";
        internal const string ReadyMadeActionsMenuSection = "More Ready-Made Actions";
        internal const string SampleActionsMenu = "Sample Actions";
        internal const string ProjectActionsMenu = "Project & Package Actions";

        #endregion

        #region Left pane

        internal static readonly GUIContent ThisCharacterGroup = new(
            "This Character",
            "Actions authored directly on this Convai Character. Only this character can perform them.");

        internal static readonly GUIContent SelectAssetButton = new(
            "Select asset",
            "Select this Action Set asset in the Project window.");

        internal static readonly GUIContent MoveActionUpButton = new(
            Glyphs.Affordance.MoveUp,
            "Move this action earlier in the list.");

        internal static readonly GUIContent MoveActionDownButton = new(
            Glyphs.Affordance.MoveDown,
            "Move this action later in the list.");

        internal static readonly GUIContent RemoveActionButton = new(
            Glyphs.Status.Fail,
            "Remove this action from this Convai Character.");

        internal static readonly GUIContent StatusDotReady = new(
            Glyphs.Live,
            "Ready: this action has a bound scene behavior and no setup issues.");

        internal static readonly GUIContent StatusDotNeedsAttention = new(
            Glyphs.Live,
            "Needs attention: this action is missing a scene behavior binding, or has a minor " +
            "setup warning (for example, a missing description).");

        internal static readonly GUIContent StatusDotBroken = new(
            Glyphs.Live,
            "Broken: this action cannot run until a setup error is fixed. Open the Action " +
            "Troubleshooter for details.");

        internal static readonly GUIContent AddActionSetLink = new(
            "+ Use an Action Set",
            "An Action Set is a reusable group of actions you can share across several Convai " +
            "Characters — author it once, use it everywhere. Assign an existing set, or create a new one.");

        internal static readonly GUIContent RemoveActionSetIcon = new(
            Glyphs.Status.Fail,
            "Stop using this Action Set on this Convai Character. The set asset is not deleted, and " +
            "every other character using it keeps working.");

        internal static readonly GUIContent AddToSetIcon = new(
            "+",
            "Add a new action to this Action Set. The action becomes available to every Convai " +
            "Character that uses this set.");

        internal static readonly GUIContent CreateNewActionSetMenuItem = new(
            "Create New Action Set…",
            "Creates a new, empty Action Set asset and starts using it on this Convai Character.");

        internal static readonly GUIContent NoAssignableActionSetsMenuItem = new(
            "No unused Action Set assets in this project",
            "Every Action Set asset in this project is already in use by this Convai Character.");

        internal static readonly GUIContent EmptySetHint = new(
            "This Action Set is empty, so it adds nothing yet. Actions you add to it become " +
            "available to every Convai Character that uses it.",
            "Use the + button on the set's header, or the button below.");

        internal static readonly GUIContent AddFirstSetActionButton = new(
            "Add Action To Set ▾",
            "Add the first action to this Action Set.");

        /// <summary>Plain-English answer to "what is an Action Set?", shown where the choice is made.</summary>
        internal static readonly GUIContent ActionSetsExplainer = new(
            "An Action Set is a reusable group of actions, saved as its own asset. Use one to teach " +
            "several Convai Characters the same actions without redoing the work on each of them.",
            "Actions authored on this character stay on this character. Actions in a set are shared " +
            "by every character that uses that set.");

        #endregion

        #region Right pane — shared Action Set banner

        internal static readonly GUIContent SharedSetBannerSoleUser = new(
            "This action lives in a shared Action Set. No other Convai Character in your open " +
            "scenes uses this set right now.",
            "Editing it here edits the set asset itself, so any character that starts using this " +
            "set later gets the same action.");

        internal static readonly GUIContent SetActionBehaviorLabel = new(
            "Action Behavior",
            "Which behavior performs this action. A shared Action Set stores the behavior's type, " +
            "not a specific component — each Convai Character using the set supplies its own. " +
            "(API name for engineers: the IConvaiActionExecutor type hint.)");

        internal static readonly GUIContent SetBehaviorUnset = new(
            "No Action Behavior is chosen yet. This action will never run.",
            "Choose a behavior below. Every Convai Character using this set will look for a " +
            "matching component on itself.");

        internal static readonly GUIContent ChooseSetBehaviorButton = new(
            "Choose Action Behavior ▾",
            "Pick which behavior performs this action, from the behaviors this SDK ships.");

        internal static readonly GUIContent SetBehaviorUnresolvable = new(
            "This action names a behavior that no longer exists in the project.",
            "The set was authored against a behavior type that has since been renamed or removed. " +
            "Choose a current one below.");

        #endregion

        #region Right pane — Command

        internal static readonly GUIContent CommandBoxTitle = new(
            "Command",
            "What this action means to the Convai Character in conversation.");

        internal static readonly GUIContent NameField = new(
            "Name",
            "The action's name. Must be unique among this Convai Character's actions.");

        internal static readonly GUIContent DescriptionField = new(
            "Description",
            "Helps the Convai Character decide when to use this action. A clear, specific " +
            "description meaningfully improves how reliably the character reaches for it.");

        /// <summary>
        ///     Example prose drawn inside the empty Description field — same reasoning as
        ///     <see cref="KnownObjectDescriptionPlaceholder" />: it answers "what do I type here" in
        ///     the place the question is asked, and disappears the moment it is answered.
        /// </summary>
        internal static readonly GUIContent DescriptionPlaceholder =
            new("e.g. Walk over to something the player points out and stop in front of it.");

        // "When This Action Finishes" reads better and does not fit: the right pane pins
        // EditorGUIUtility.labelWidth to 120, sized for "Offer This Action", and widening it for one
        // row would push every other field in the pane right for nothing. Inside a card headed
        // Command, on a row under the action's own name, "it" is not ambiguous.
        internal static readonly GUIContent AnswerDeliveryField = new(
            "When It Finishes",
            "What the Convai Character does with what this action found out. Leave it on Use " +
            "Character Setting unless this action answers a question the player asked.");

        internal static readonly GUIContent CommandPreviewLabel = new(
            "Command Preview",
            "A live example of what this action tells the Convai Character it can do, built from " +
            "the name, description, target, and parameters below.");

        #endregion

        #region Right pane — Scene Behavior

        internal static readonly GUIContent SceneBehaviorBoxTitle = new(
            "Scene Behavior",
            "What actually happens in the scene when the Convai Character performs this action.");

        internal static readonly GUIContent ActionBehaviorMissing = new(
            "No Action Behavior is assigned yet. This action will never run.",
            "Pick a component below, or use Add Action ▾ to add and bind one automatically.");

        internal static readonly GUIContent ChooseBehaviorButton = new(
            Glyphs.Affordance.Dropdown,
            "Choose a different Action Behavior component found on this Convai Character.");

        internal static readonly GUIContent BehaviorObjectFieldFallback = new(
            "Behavior Component",
            "Drag in any component on this Convai Character that implements the Action Behavior " +
            "contract directly. (API name for engineers: IConvaiActionExecutor.)");

        internal static readonly GUIContent NoBehaviorCandidates = new(
            "No matching components found in this Convai Character's hierarchy.",
            "Add a component that implements the Action Behavior contract, then try again.");

        #endregion

        #region Right pane — Advanced

        internal static readonly GUIContent AdvancedFoldout = new(
            "Advanced",
            "Extra settings most first-time users will not need to touch.");

        internal static readonly GUIContent ParametersLabel = new(
            "Values the Convai Character fills in per command",
            "Parameters are named values the Convai Character supplies when it uses this action, " +
            "such as a target's name or a number.");

        internal static readonly GUIContent ParameterNameField = new(
            "Name",
            "The parameter's name, used as its key when the Convai Character fills it in.");

        internal static readonly GUIContent ParameterTypeField = new(
            "Type",
            "How the value should be interpreted: inferred automatically, a target reference, " +
            "plain text, a number, a yes/no, or one of a fixed list of choices.");

        internal static readonly GUIContent ParameterConnectorField = new(
            "Connector",
            "An optional linking word rendered before this parameter (for example \"on\" or " +
            "\"in\"). Not available on the first parameter.");

        internal static readonly GUIContent ParameterDescriptionField = new(
            "Description",
            "Helps the Convai Character understand what value to provide for this parameter.");

        internal static readonly GUIContent ParameterDescriptionPlaceholder =
            new("e.g. Which object to walk to.");

        internal static readonly GUIContent ParameterChoicesField = new(
            "Choices",
            "The fixed list of values the Convai Character may choose from for this parameter.");

        internal static readonly GUIContent AddParameterButton = new(
            "Add Parameter",
            "Add a new value the Convai Character can fill in for this action.");

        internal static readonly GUIContent RemoveParameterButton = new(
            Glyphs.Status.Fail,
            "Remove this parameter.");

        internal static readonly GUIContent AddChoiceButton = new(
            "Add Choice",
            "Add another allowed value for this parameter.");

        internal static readonly GUIContent ValidTargetsField = new(
            "Target",
            "What the player must name when asking for this action: no target, an object, another " +
            "character, or either one.");

        internal static readonly GUIContent[] ValidTargetOptions =
        {
            new("No target", "This action does not need the player to name anything."),
            new("Object", "The player must name an object this character knows about."),
            new("Character", "The player must name another character this character knows about."),
            new("Object or character", "The player may name either a known object or another known character.")
        };

        internal static string BuildMissingTargetTitle(string actionName) =>
            $"Add a target for \"{DisplayActionName(actionName)}\"";

        internal static string BuildMissingTargetMessage(
            string actionName,
            string characterName,
            ConvaiActionTargetRequirement requirement)
        {
            string expected = requirement switch
            {
                ConvaiActionTargetRequirement.Object => "an object",
                ConvaiActionTargetRequirement.Character => "another character",
                _ => "an object or another character"
            };
            string known = requirement switch
            {
                ConvaiActionTargetRequirement.Object => "any objects",
                ConvaiActionTargetRequirement.Character => "any other characters",
                _ => "any objects or other characters"
            };

            return $"\"{DisplayActionName(actionName)}\" expects the player to name {expected}, but " +
                   $"{DisplayCharacterName(characterName)} does not know {known} yet. Add one in Scene " +
                   "Knowledge. Until then, only this action is unavailable.";
        }

        internal static string BuildAddTargetButton(ConvaiActionTargetRequirement requirement) =>
            requirement switch
            {
                ConvaiActionTargetRequirement.Object => "Add Object",
                ConvaiActionTargetRequirement.Character => "Add Character",
                _ => "Add Target"
            };

        private static string DisplayActionName(string actionName) =>
            string.IsNullOrWhiteSpace(actionName) ? "this action" : actionName.Trim();

        private static string DisplayCharacterName(string characterName) =>
            string.IsNullOrWhiteSpace(characterName) ? "This character" : characterName.Trim();

        internal static readonly GUIContent TimeoutField = new(
            "Timeout (Seconds)",
            "How long to wait before giving up on this action. Zero or less means no timeout.");

        internal static readonly GUIContent FailurePolicyField = new(
            "If This Fails",
            "Whether the remaining actions in the same batch should stop or continue when this " +
            "one fails. Leave on the default to follow the dispatcher's overall setting.");

        internal static readonly GUIContent SpeechGateField = new(
            "Wait For Character To Finish Speaking",
            "When on, this action waits for the Convai Character to finish its current line " +
            "before it starts (useful for actions the character should narrate first).");

        internal static readonly GUIContent SpeechGateDelayField = new(
            "Extra Delay After Speaking (Seconds)",
            "Additional pause after the Convai Character finishes speaking, before this action " +
            "starts.");

        internal static readonly GUIContent ActionEnabledField = new(
            "Offer This Action",
            "Unticked: the Convai Character will not know about or offer this action. It is left " +
            "out of what is sent to Convai, and a stale conversation command for it is declined. " +
            "Game code can still override this per session with SetActionAvailable.");

        #endregion

        #region Bottom status strip

        internal static readonly GUIContent StatusStripAllReady = new(
            $"{Glyphs.Status.Ok} All actions ready",
            "Every action on this Convai Character is set up correctly.");

        #endregion

        #region Empty state

        internal static readonly GUIContent EmptyStateTitle = new(
            "Add your first action",
            "Give this Convai Character something it can do.");

        // The starter cards' names and descriptions are not authored here: each behavior declares
        // its own via ConvaiActionArchetypeAttribute, so the shipped library can change without a
        // parallel edit in this file. See ConvaiActionArchetypeCatalog.FeaturedEntries.

        internal static readonly GUIContent EmptyStateTutorialLink = new(
            "Open the step-by-step tutorial",
            "Opens the Convai Unity SDK documentation in your browser for a full walkthrough.");

        #endregion

        #region Inspector summary card

        internal static readonly GUIContent InspectorStatusCardTitle = new(
            "Actions",
            "A read-only summary. Open the Actions Editor to change anything.");

        /// <summary>
        ///     The capability phrase under the title, inside the header plate — the slot every other
        ///     Convai inspector fills ("Eye &amp; head contact", "Idle, talk, locomotion &amp;
        ///     gestures"). Saying what this component is belongs in the header with the title, not in
        ///     a sentence floating underneath it.
        /// </summary>
        internal const string InspectorHeaderSubtitle = "Named actions and their behaviors";

        internal static readonly GUIContent InspectorOpenWindowButton = new(
            "Open Actions Editor",
            "Open the Actions Editor window to add, edit, and bind this Convai Character's " +
            "actions in one place.");

        internal static readonly GUIContent InspectorTroubleshooterButton = new(
            "Troubleshooter",
            "Open the Action Troubleshooter to diagnose and one-click fix setup issues.");

        internal static readonly GUIContent InspectorValidateButton = new(
            "Validate",
            "Re-run action config validation now and refresh the summary above.");

        #endregion

        #region Window frame (hero header, search, footer, empty/onboarding states)

        internal static readonly GUIContent WindowTabTitle = new(
            "Actions Editor",
            "Author everything this Convai Character can do — in one place.");

        internal static readonly GUIContent HeroTitle = new(
            "Actions Editor",
            "Author everything this Convai Character can do — in one place.");

        internal static readonly GUIContent HeroSubtitle = new(
            "Teach your Convai Character what it can do.",
            "Actions let a Convai Character affect the scene: move, look, gesture, or anything " +
            "you define yourself.");

        internal static readonly GUIContent HeroCharacterLabel = new(
            "Character",
            "The Convai Character whose actions you are viewing and editing.");

        internal static readonly GUIContent HeroNoCharacters = new(
            "No Convai Character in the open scene(s)",
            "Add a Convai Character to a scene and this window will pick it up automatically.");

        internal static readonly GUIContent HealthChipReady = new(
            $"{Glyphs.Live}  Ready",
            "No setup issues were found. Click to open the Action Troubleshooter anyway.");

        /// <summary>
        ///     Inspector-header variant of <see cref="HealthChipReady" />. The header status pill
        ///     draws its own leading state dot, so this text must not embed one.
        /// </summary>
        internal static readonly GUIContent InspectorHealthChipReady = new(
            "Ready",
            "No setup issues were found. Click to open the Action Troubleshooter anyway.");

        /// <summary>
        ///     Painted inside the empty search box, so it carries a label and nothing else — the
        ///     guidance that used to ride along as its tooltip is <see cref="SearchFieldHelp" />,
        ///     which the field itself answers with on hover in every state.
        /// </summary>
        internal static readonly GUIContent SearchPlaceholder = new("Search actions…");

        internal const string SearchFieldHelp = "Filter the action list by name or description.";

        internal static readonly GUIContent ClearSearchButton = new(
            Glyphs.Status.Fail,
            "Clear the search filter.");

        internal static readonly GUIContent NoSearchResults = new(
            "No actions match your search.",
            "Clear the search filter above to see every action again.");

        internal static readonly GUIContent OpenTroubleshooterButton = new(
            "Troubleshooter",
            "Open the Action Troubleshooter to review and one-click fix every setup issue.");

        internal static readonly GUIContent SelectAssetIcon = new(
            Glyphs.Discovery,
            "Select this Action Set asset in the Project window so you can edit its shared actions.");

        internal static readonly GUIContent NoCharacterStateTitle = new(
            "Add a Convai Character to begin",
            "This window edits the actions of a Convai Character in your open scene.");

        internal static readonly GUIContent NoCharacterStateBody = new(
            "This window edits what a Convai Character can do in your scene. Add a Convai " +
            "Character, and it will be picked up here automatically.",
            "Use the Convai setup tools to add a character, then return to this window.");

        internal static readonly GUIContent EnableActionsTitle = new(
            "One step before actions",
            "This Convai Character needs a place to keep its actions before you can add any.");

        internal static readonly GUIContent EnableActionsBody = new(
            "This Convai Character cannot perform actions yet. Enable actions to give it a place " +
            "to keep them — one click, fully undoable.",
            "Adds a Convai Actions component, which stores this character's actions.");

        internal static readonly GUIContent EnableActionsButton = new(
            "Enable Actions",
            "Adds a Convai Actions component to this Convai Character. Undo-safe.");

        internal static readonly GUIContent RightPaneNoSelection = new(
            "Select an action on the left to edit it.",
            "Click any action card in the list to see and edit its details here.");

        internal static readonly GUIContent OverviewTileActions = new(
            "ACTIONS",
            "How many actions this Convai Character can perform, including shared Action Set actions.");

        internal static readonly GUIContent OverviewTileReady = new(
            "READY",
            "Actions with a bound scene behavior and no setup issues. These can run right now.");

        internal static readonly GUIContent OverviewTileNeedsWork = new(
            "NEEDS WORK",
            "Actions that are missing a scene behavior binding, or that have a setup problem to fix.");

        internal static readonly GUIContent OverviewBreakdownTitle = new(
            "What is in the list",
            "How the actions on the left are grouped right now. Change the grouping with the " +
            "Group control above the list.");

        internal static readonly GUIContent OverviewNextStepsTitle = new(
            "Where to go next",
            "The things most often done from here.");

        internal static readonly GUIContent OverviewAddActionButton = new(
            "+ Add Action",
            "Add another ready-made action to this Convai Character.");

        internal static readonly GUIContent OverviewSceneKnowledgeButton = new(
            "Scene Knowledge",
            "Review what this Convai Character knows about the scene it is in — the targets its " +
            "actions can be pointed at.");

        internal static readonly GUIContent OverviewTroubleshooterButton = new(
            "Troubleshooter",
            "Open the Action Troubleshooter to see every setup problem and how to fix it.");

        internal static readonly GUIContent SharedBadge = new(
            "Shared",
            "This action comes from an Action Set asset that can be shared by multiple " +
            "characters. Select the asset to edit it there.");

        internal static readonly GUIContent StatusChipReady = new(
            "Ready",
            "Ready: this action has a bound scene behavior and no setup issues.");

        internal static readonly GUIContent StatusChipNeedsAttention = new(
            "Needs attention",
            "Needs attention: this action is missing a scene behavior binding, or has a minor " +
            "setup warning (for example, a missing description).");

        internal static readonly GUIContent StatusChipBroken = new(
            "Broken",
            "Broken: this action cannot run until a setup error is fixed. Open the Action " +
            "Troubleshooter for details.");

        internal static readonly GUIContent EmptyStateSubtitle = new(
            "Give this Convai Character something it can do.",
            "Pick a ready-made starter below, or browse the full catalog.");

        internal static readonly GUIContent BrowseAllActionsLink = new(
            "Browse all ready-made actions…",
            "Open the full Add Action catalog.");

        #endregion

        #region Component inspectors (Convai Actions summary card, Action Runner)

        internal static readonly GUIContent InspectorTileActions = new(
            "ACTIONS",
            "How many actions this Convai Character can perform, including shared Action Set actions.");

        internal static readonly GUIContent InspectorTileSets = new(
            "ACTION SETS",
            "How many shared Action Set assets are assigned to this Convai Character.");

        internal static readonly GUIContent InspectorTileIssues = new(
            "ISSUES",
            "Setup problems found by validation. Zero means everything is ready.");

        internal static readonly GUIContent InspectorNoActionsBody = new(
            "No actions authored yet. Use Open Actions Editor below to add the first one.",
            "The Actions Editor window is where actions are added, edited, and bound.");

        /// <summary>
        ///     The caption half of the inspector's one-line "where the behaviors are" row. Deliberately
        ///     phrased as a fact rather than a setting: for most authors this row is something to read
        ///     once and never act on, and its full explanation lives in the tooltip so the line itself
        ///     stays out of the way.
        /// </summary>
        internal static readonly GUIContent BehaviorHostRowLabel = new(
            "Behaviors on",
            "Which object holds the components that perform this character's actions. They can sit on " +
            "the Convai Character itself or on one of its child objects — Convai finds them either way, " +
            "and actions run exactly the same.");

        /// <summary>
        ///     The object half of that row. Clicking it selects and pings the object, which answers
        ///     "where is that?" by showing it in the Hierarchy rather than by describing it.
        /// </summary>
        /// <remarks>
        ///     Just the name, with no leading mark: a section glyph set at value size reads as a
        ///     control rather than as a category, and the caption beside it already says what the
        ///     value is.
        /// </remarks>
        internal static GUIContent BuildBehaviorHostRowName(string hostName) => new(
            hostName,
            "Select this object in the Hierarchy so you can see the action behaviors on it.");

        /// <summary>
        ///     Shown on that row only while behaviors are split between the character and its behaviors
        ///     object. Not a problem — both places run identically — but it is the one state of this row
        ///     worth an author's eye, and clicking it opens the commands that finish the move.
        /// </summary>
        internal static GUIContent BuildBehaviorHostRemainingPill(int remainingOnCharacter) => new(
            $"{remainingOnCharacter} on character",
            $"{remainingOnCharacter} action behavior{(remainingOnCharacter == 1 ? " is" : "s are")} still on the " +
            "Convai Character itself. That is fine — they run exactly the same. Click for the " +
            "commands that move them across.");

        internal static readonly GUIContent BehaviorHostRowMenuButton = new(
            "…",
            "Options for where this character's action behaviors live: select the object, move the " +
            "remaining behaviors across, or go back to keeping them on the character.");

        // Menu wording stands on its own: a command read inside a menu has none of the surrounding
        // text a button next to a status line can lean on.

        internal static readonly GUIContent BehaviorHostMenuSelect = new(
            "Select the object",
            "Selects the object that holds this character's action behaviors.");

        internal static readonly GUIContent BehaviorHostMenuCopy = new(
            "Copy the character's behaviors across",
            "Copies the action behaviors still on the Convai Character onto the object above and " +
            "points this character's actions at the copies. Nothing is deleted.");

        internal static readonly GUIContent BehaviorHostMenuRemove = new(
            "Remove the copied originals",
            "Removes the behaviors left on the Convai Character that have a copy on the object above " +
            "and that nothing still points at.");

        internal static readonly GUIContent BehaviorHostMenuClear = new(
            "Use the character instead",
            "Goes back to adding new action behaviors to the Convai Character itself. Behaviors " +
            "already on the other object keep working.");

        internal static readonly GUIContent BehaviorHostField = new(
            "Action Behaviors Object",
            "Optional child object that holds this character's action behaviors, so the character's " +
            "own inspector stays readable. Leave empty to keep them on the character itself — Convai " +
            "finds them either way.");

        internal static readonly GUIContent CreateBehaviorHostButton = new(
            "Create the child object",
            "Adds a child object named 'Action Behaviors' and points this character at it. Behaviors " +
            "already on the character stay exactly where they are — only newly added ones go to the " +
            "child. Undoable.");

        internal static readonly GUIContent BehaviorHostClearButton = new(
            "Use the character",
            "Goes back to adding new action behaviors to the Convai Character itself. Behaviors " +
            "already on the child keep working.");

        internal static readonly GUIContent BehaviorHostInvalid = new(
            "The assigned Action Behaviors Object is not part of this Convai Character, so behaviors " +
            "created on it would never be found. New behaviors are going to the character itself " +
            "until this points at the character or one of its child objects.",
            "An action behaviors object must be this character or one of its child objects.");

        /// <summary>
        ///     Offer to move behaviors off the character, shown only once there are enough of them for
        ///     the character's own inspector to have become hard to read.
        /// </summary>
        internal static GUIContent BuildBehaviorHostOffer(int behaviorCount) => new(
            $"{behaviorCount} action behavior{(behaviorCount == 1 ? " is" : "s are")} on this Convai Character. " +
            "They can live on a child object instead, so this inspector stays readable. Convai finds " +
            "them either way.",
            "Nothing about how actions run changes — this is only about where the components sit.");

        /// <summary>
        ///     Result of copying behaviors across, worded so the next step is unambiguous: the
        ///     originals are still there and removing them is a separate, deliberate step.
        /// </summary>
        internal static string BuildCopyBehaviorsResult(int copied, int repointed) =>
            $"Copied {copied} action behavior{(copied == 1 ? string.Empty : "s")} onto the Action Behaviors " +
            $"Object and pointed {repointed} reference{(repointed == 1 ? string.Empty : "s")} at them. " +
            "Nothing was deleted.\n\n" +
            "Check the action list — every action should still show a bound behavior. Once it does, " +
            "use Remove the copied originals to clear the ones left on the character.";

        /// <summary>
        ///     Result of removing the copied originals. Names everything that was refused and why,
        ///     because a command that quietly skips work is worse than one that does none.
        /// </summary>
        internal static string BuildRemoveBehaviorsResult(int removed, IReadOnlyList<string> blocked, bool isPrefabInstance)
        {
            string message = removed > 0
                ? $"Removed {removed} action behavior{(removed == 1 ? string.Empty : "s")} from the Convai Character."
                : "Nothing was removed.";

            if (blocked != null && blocked.Count > 0)
            {
                message += $"\n\n{blocked.Count} {(blocked.Count == 1 ? "was" : "were")} left in place because " +
                           $"{(blocked.Count == 1 ? "it" : "they")} could not be shown to be unused:\n\n• " +
                           string.Join("\n• ", blocked) +
                           "\n\nDeal with those, then run this again.";
            }

            if (isPrefabInstance)
            {
                message += "\n\nThis character is a prefab instance. The check covers the open scene, so a " +
                           "reference from another scene or from a prefab asset that is not open would not " +
                           "have been seen. Open the prefab and check there too.";
            }

            return message;
        }

        internal const string TroubleshooterBehaviorHostTitle = "Action Behaviors Object";

        internal const string TroubleshooterBehaviorHostReadyMessage =
            "Action behaviors live on a child object of this Convai Character, and Convai finds them there.";

        internal const string TroubleshooterBehaviorHostOutsideMessage =
            "The assigned Action Behaviors Object is not part of this Convai Character. Behaviors " +
            "created on it would never be found, so new ones are going to the character itself instead.";

        internal const string TroubleshooterBehaviorHostInactiveMessage =
            "The Action Behaviors Object is deactivated. Behaviors on a deactivated object cannot run, " +
            "so every action bound to one will do nothing.";

        internal const string TroubleshooterBehaviorHostOffsetMessage =
            "The Action Behaviors Object has been moved or rotated away from the character. Shipped " +
            "behaviors read the character's own position, but a custom behavior that reads its own " +
            "will act relative to this offset instead.";

        internal static readonly GUIContent TroubleshooterFixClearBehaviorHost = new(
            "Use the character",
            "Goes back to adding new action behaviors to the Convai Character itself. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixActivateBehaviorHost = new(
            "Activate",
            "Reactivates the Action Behaviors Object so its behaviors can run. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixResetBehaviorHost = new(
            "Reset Transform",
            "Puts the Action Behaviors Object back on the character's own position and rotation. Undo-safe.");

        /// <summary>
        ///     Behaviors sitting on both the character and its behaviors object. Reported as
        ///     information and never as a problem: both places run identically, and this is exactly
        ///     the state someone is in part-way through tidying a character by hand.
        /// </summary>
        internal static string BuildTroubleshooterBehaviorHostSplitMessage(int onCharacter, int onHost) =>
            $"{onHost} action behavior{(onHost == 1 ? " is" : "s are")} on the Action Behaviors Object and " +
            $"{onCharacter} {(onCharacter == 1 ? "is" : "are")} still on the Convai Character. Both run " +
            "exactly the same — this is only worth knowing if you meant to finish moving them.";

        internal static readonly GUIContent DispatcherTitle = new(
            "Action Runner",
            "Performs the actions this Convai Character decides to take, in order, one batch at a time.");

        internal static readonly GUIContent DispatcherIntro = new(
            "Listens for the actions this Convai Character decides to take, then performs them in " +
            "order — one batch at a time. The defaults work well; tune below only if you need to.",
            "Requires Convai Actions on the same character to define what the " +
            "actions actually do.");

        internal static readonly GUIContent DispatcherDisabledBody = new(
            "This component is disabled, so incoming actions will not run.",
            "Re-enable the component to let this Convai Character perform actions.");

        internal static readonly GUIContent DispatcherRunSectionTitle = new(
            "How Actions Run",
            "Ordering, interruption, and timing rules for incoming action batches.");

        internal static readonly GUIContent DispatcherWhileBusyField = new(
            "While Busy",
            "What happens when new actions arrive while this Convai Character is still " +
            "performing earlier ones.");


        internal static readonly GUIContent DispatcherFailureField = new(
            "If A Step Fails",
            "Whether one failing action stops the rest of its batch. Individual actions can " +
            "override this in the Actions Editor's Advanced settings.");


        internal static readonly GUIContent DispatcherSpeechGateField = new(
            "Speech Wait Limit (Seconds)",
            "An action set to wait for the Convai Character to finish speaking runs anyway after " +
            "this many seconds, so a stuck line can never freeze the batch.");

        internal static readonly GUIContent DispatcherStepTimeoutField = new(
            "Action Time Limit (Seconds)",
            "Longest any action may run before it is reported as timed out. This is a safety net, " +
            "not a tuning value: without it one action behavior that never finishes holds this " +
            "character's whole action queue for the rest of the session. An action that is meant " +
            "to run longer sets its own Timeout Seconds, which always wins.");

        /// <summary>Reads the safety net back in plain language, including the off case.</summary>
        internal static string ExplainStepTimeout(float seconds) =>
            seconds > 0f
                ? $"An action still running after {seconds:0.##} seconds is reported as failed and the " +
                  "queue moves on. Actions with their own Timeout Seconds are unaffected."
                : "No limit. An action behavior that never finishes will hold this character's queue " +
                  "until play mode ends.";

        internal static readonly GUIContent DispatcherBehaviorSectionTitle = new(
            "Reactions & Interruptions",
            "Optional lifelike behavior while performing, and how the player can interrupt.");

        internal static readonly GUIContent DispatcherCancelOnSpeechField = new(
            "Stop When The Player Speaks",
            "When on, the player starting to speak cancels the current batch and clears the " +
            "queue, so the Convai Character can respond immediately.");

        internal static readonly GUIContent DispatcherReactionsField = new(
            "Lifelike Reactions",
            "While performing, the Convai Character also glances at what it acts on, gives small " +
            "acknowledging nods, and shows a brief mood on success or failure. Safely does " +
            "nothing when the matching modules are absent.");

        internal static readonly GUIContent DispatcherEventsSectionTitle = new(
            "Events",
            "Run your own logic at batch and step milestones (started, succeeded, failed, " +
            "completed, aborted).");

        internal static readonly GUIContent DispatcherLiveSectionTitle = new(
            "Live Activity",
            "What this Convai Character is performing right now.");

        internal static readonly GUIContent DispatcherLiveOffline = new(
            "Enter Play Mode to watch actions run live.",
            "This panel shows the current action, queued batches, and totals while playing.");

        internal static readonly GUIContent DispatcherLiveIdle = new(
            "Idle — waiting for actions",
            "No batch is executing right now.");

        internal static readonly GUIContent DispatcherChipReady = new(
            "Ready",
            "This component is enabled and will perform incoming actions.");

        internal static readonly GUIContent DispatcherChipDisabled = new(
            "Disabled",
            "This component is disabled, so incoming actions will not run.");

        internal static readonly GUIContent DispatcherChipIdle = new(
            "Idle",
            "No batch is executing right now.");

        internal static readonly GUIContent DispatcherChipPerforming = new(
            "Performing",
            "A batch is executing right now.");

        #endregion

        #region Dynamic (composed) content

        /// <summary>
        ///     Builds the troubleshooter-chip label for an unhealthy state. Rebuilt on demand (the count
        ///     changes), but the tooltip stays the fixed, cached string below.
        /// </summary>
        internal static GUIContent BuildTroubleshooterChipIssues(int issueCount) =>
            new($"{issueCount} issue{(issueCount == 1 ? string.Empty : "s")}", TroubleshooterChipIssuesTooltip);

        private const string TroubleshooterChipIssuesTooltip =
            "This Convai Character has setup issues. Click to open the Action Troubleshooter and fix them.";

        /// <summary>Builds the bottom status strip's issue-summary button label.</summary>
        internal static GUIContent BuildStatusStripIssues(int warningCount, int errorCount)
        {
            string text = errorCount > 0
                ? $"{warningCount} warning(s), {errorCount} error(s) — Fix in Troubleshooter"
                : $"{warningCount} warning(s) — Fix in Troubleshooter";
            return new GUIContent(text, StatusStripIssuesTooltip);
        }

        private const string StatusStripIssuesTooltip =
            "Opens the Action Troubleshooter so you can review and fix every issue at once.";

        /// <summary>Builds the live "Command Preview" sentence for a rendered action template.</summary>
        internal static GUIContent BuildCommandPreviewValue(string renderedTemplate)
        {
            string text = string.IsNullOrWhiteSpace(renderedTemplate)
                ? "This action has no valid preview yet — give it a name."
                : $"This Convai Character can be asked to: \"{renderedTemplate}\"";
            return new GUIContent(text, CommandPreviewLabel.tooltip);
        }

        /// <summary>Builds the "Action Behavior is bound" status line for a resolved component.</summary>
        internal static GUIContent BuildBehaviorBoundStatus(string archetypeDisplayName, string componentTypeName, string gameObjectName) =>
            new($"{Glyphs.Status.Ok} Ready — {archetypeDisplayName} on '{gameObjectName}'", BehaviorBoundTooltip(componentTypeName));

        private static string BehaviorBoundTooltip(string componentTypeName) =>
            $"Bound to the '{componentTypeName}' component. (API name for engineers: IConvaiActionExecutor.)";

        /// <summary>Builds the one-click "Add & Bind" button for a resolvable-but-missing behavior.</summary>
        internal static GUIContent BuildAddAndBindButton(string archetypeDisplayName) =>
            new($"Add & Bind {archetypeDisplayName}", AddAndBindTooltip);

        private const string AddAndBindTooltip =
            "Adds the matching component to this Convai Character and binds this action to it — one click, Undo-safe.";

        /// <summary>Builds the "…and N more" trailer row on the shrunk inspector's compact list.</summary>
        internal static GUIContent BuildInspectorMoreRow(int remaining) =>
            new($"…and {remaining} more", "Open Actions Editor to see and edit the full list.");

        /// <summary>Builds the shrunk inspector's one-line status summary.</summary>
        internal static GUIContent BuildInspectorSummary(int actionCount, int setCount, int warningCount, int errorCount)
        {
            string severity = errorCount > 0
                ? $"{Glyphs.Status.Fail} {errorCount} error(s)"
                : warningCount > 0
                    ? $"{Glyphs.Status.Warn} {warningCount} warning(s)"
                    : $"{Glyphs.Status.Ok} Ready";
            string text = $"{actionCount} action(s) · {setCount} action set(s) · {severity}";
            return new GUIContent(text, InspectorStatusCardTitle.tooltip);
        }

        /// <summary>Builds the hero health chip's label for an unhealthy state.</summary>
        internal static GUIContent BuildHealthChipIssues(int issueCount) =>
            new($"{Glyphs.Live}  {issueCount} to fix", TroubleshooterChipIssuesTooltip);

        /// <summary>
        ///     Inspector-header variant of <see cref="BuildHealthChipIssues" /> — the header status
        ///     pill draws its own leading state dot, so this text must not embed one.
        /// </summary>
        internal static GUIContent BuildInspectorHealthChipIssues(int issueCount) =>
            new($"{issueCount} to fix", TroubleshooterChipIssuesTooltip);

        /// <summary>
        ///     The Action Troubleshooter's own health pill. Deliberately the same sentence the other
        ///     two surfaces use: the Troubleshooter said "5 To Fix" while the inspector said "1 to
        ///     fix" about the same character, and two differently-cased numbers for the same thing
        ///     read as two different things even once the numbers agree.
        /// </summary>
        internal static string BuildTroubleshooterHealthPill(int issueCount) => $"{issueCount} to fix";

        /// <summary>Builds the footer's issue-summary text (the Troubleshooter button sits beside it).</summary>
        internal static GUIContent BuildFooterIssueSummary(int warningCount, int errorCount)
        {
            string text = errorCount > 0 && warningCount > 0
                ? $"{errorCount} error{(errorCount == 1 ? string.Empty : "s")} · {warningCount} warning{(warningCount == 1 ? string.Empty : "s")}"
                : errorCount > 0
                    ? $"{errorCount} error{(errorCount == 1 ? string.Empty : "s")}"
                    : $"{warningCount} warning{(warningCount == 1 ? string.Empty : "s")}";
            return new GUIContent(text, StatusStripIssuesTooltip);
        }

        /// <summary>Builds the hero character-picker pill label.</summary>
        internal static GUIContent BuildCharacterPill(string characterName) =>
            new($"{characterName}   ▾", HeroCharacterLabel.tooltip);

        /// <summary>Builds the overview card's title — the summary shown while no action is selected.</summary>
        internal static GUIContent BuildOverviewTitle(string characterName) =>
            new($"What '{characterName}' can do",
                "A summary of this Convai Character's actions. Select one on the left to edit it.");

        /// <summary>Builds one row of the overview's group breakdown ("6 actions", "1 action").</summary>
        internal static string BuildOverviewGroupCount(int count) =>
            count == 1 ? "1 action" : $"{count} actions";

        /// <summary>Builds the label for one overview breakdown row, tooltipped with its group name.</summary>
        internal static GUIContent BuildOverviewGroupLabel(string groupTitle) =>
            new(groupTitle, $"Actions filed under '{groupTitle}'.");

        /// <summary>Builds a group header's action-count pill.</summary>
        internal static GUIContent BuildCountPill(int count) =>
            new(count.ToString(), "Number of actions in this group.");

        /// <summary>Builds the right-pane "shared from set" pill for a read-only action.</summary>
        internal static GUIContent BuildSharedFromSet(string setName) =>
            new($"Shared — from '{setName}'", SharedBadge.tooltip);

        /// <summary>Builds an inspector preview row's clickable label (deep-links into the Actions Editor).</summary>
        internal static GUIContent BuildInspectorRowLabel(string displayName) =>
            new(displayName, "Opens the Actions Editor focused on this action.");

        /// <summary>Builds the dispatcher live panel's "currently performing" line.</summary>
        internal static GUIContent BuildDispatcherPerforming(string actionDisplayName) =>
            new($"Performing: '{actionDisplayName}'", DispatcherLiveSectionTitle.tooltip);

        /// <summary>Builds the dispatcher live panel's queue/total summary line.</summary>
        internal static GUIContent BuildDispatcherQueueSummary(int pendingBatches, int startedBatches) =>
            new($"{pendingBatches} queued · {startedBatches} started this session",
                "Batches waiting behind the current one, and batches started since this component was enabled.");

        /// <summary>Builds the resolvable-but-missing behavior explainer body.</summary>
        internal static GUIContent BuildBehaviorResolvableBody(string archetypeDisplayName) =>
            new($"This action resolves to '{archetypeDisplayName}', but that component is not on " +
                "this Convai Character yet.", AddAndBindTooltip);

        private const string SelectActionRowTooltip =
            "Select this action to edit its command, scene behavior, and advanced settings.";

        /// <summary>Builds a left-pane action card's clickable name label.</summary>
        internal static GUIContent BuildActionRowLabel(string displayName) => new(displayName, SelectActionRowTooltip);

        /// <summary>
        ///     Builds the right pane's shared-set banner for an action owned by an Action Set that other
        ///     characters in the open scenes also use. Naming the blast radius up front is what makes
        ///     editing a shared asset in place honest instead of surprising.
        /// </summary>
        internal static GUIContent BuildSharedSetBanner(string setName, int otherCharacterCount) =>
            new($"This action lives in the shared Action Set '{setName}'. Editing it here also changes " +
                $"it for {otherCharacterCount} other Convai Character{(otherCharacterCount == 1 ? string.Empty : "s")} " +
                "in your open scenes.",
                SharedSetBannerSoleUser.tooltip);

        /// <summary>Builds the set-owned action's "this character supplies the component" ready status.</summary>
        internal static GUIContent BuildSetBehaviorResolved(string archetypeDisplayName, string gameObjectName) =>
            new($"{Glyphs.Status.Ok} Ready — this character performs it with {archetypeDisplayName} on '{gameObjectName}'",
                "Found a matching component on this Convai Character. Other characters using this set " +
                "each need their own matching component.");

        /// <summary>Builds the set-owned action's "this character is missing the component" status.</summary>
        internal static GUIContent BuildSetBehaviorMissingOnCharacter(string archetypeDisplayName) =>
            new($"This character has no {archetypeDisplayName} component, so this action will not run on it.",
                "Add the component to this Convai Character. The Action Set itself stays unchanged.");

        /// <summary>Builds the one-click "add the missing component to this character" button.</summary>
        internal static GUIContent BuildAddBehaviorToCharacterButton(string archetypeDisplayName) =>
            new($"Add {archetypeDisplayName} To This Character",
                "Adds the matching component to this Convai Character so it can perform this shared " +
                "action — one click, Undo-safe. The Action Set is not modified.");

        /// <summary>Builds the currently chosen behavior label for a set-owned action.</summary>
        internal static GUIContent BuildSetBehaviorChoice(string archetypeDisplayName) =>
            new($"{archetypeDisplayName}   ▾", SetActionBehaviorLabel.tooltip);

        /// <summary>Builds the remove-button tooltip for a shared action (names the blast radius).</summary>
        internal static GUIContent BuildRemoveSharedActionButton(string setName) =>
            new(Glyphs.Status.Fail,
                $"Remove this action from the Action Set '{setName}'. Every Convai Character using " +
                "that set loses it.");

        /// <summary>Builds the group header label for an Action Set group.</summary>
        internal static GUIContent BuildActionSetGroupLabel(string setName) =>
            new(setName,
                $"Actions shared from the Action Set '{setName}'. Editing them here edits the set asset, " +
                "so every Convai Character using this set is affected.");

        #endregion

        #region Action Set asset inspector

        internal static readonly GUIContent SetInspectorTitle = new(
            "Action Set",
            "A reusable group of actions that several Convai Characters can share.");

        internal static readonly GUIContent SetInspectorIntro = new(
            "A reusable group of actions. Assign this set to a Convai Character's Actions, and that " +
            "character can perform everything listed here — author once, use on as many characters " +
            "as you like.",
            "Open the Actions Editor on a character that uses this set to add or change its actions.");

        internal static readonly GUIContent SetInspectorEmptyBody = new(
            "This Action Set has no actions yet. Use it on a Convai Character, then add actions to " +
            "it from the Actions Editor.",
            "A set is authored through the Actions Editor window, not here.");

        internal static readonly GUIContent SetInspectorOpenEditorButton = new(
            "Open Actions Editor",
            "Opens the Actions Editor on a Convai Character in your open scenes that uses this set.");

        internal static readonly GUIContent SetInspectorNoUserBody = new(
            "No Convai Character in your open scenes uses this Action Set yet.",
            "Open the Actions Editor on a character and choose \"+ Use an Action Set\" to start using it.");

        internal static readonly GUIContent SetInspectorTileActions = new(
            "ACTIONS",
            "How many actions this Action Set shares with every character using it.");

        internal static readonly GUIContent SetInspectorTileUsedBy = new(
            "USED BY",
            "How many Convai Characters in your open scenes currently use this Action Set.");

        /// <summary>Builds the set inspector's "open the editor on <character>" button.</summary>
        internal static GUIContent BuildSetInspectorOpenOnCharacter(string characterName) =>
            new($"Open Actions Editor on '{characterName}'",
                SetInspectorOpenEditorButton.tooltip);

        /// <summary>Builds a set inspector action row's read-only label.</summary>
        internal static GUIContent BuildSetInspectorRowLabel(string displayName) =>
            new(displayName, "Opens the Actions Editor focused on this shared action.");

        /// <summary>
        ///     Inline notice for one action whose authored behavior name matches nothing Convai
        ///     knows about (typo, rename, or an uninstalled module) — this action will silently
        ///     never run until it is fixed. Explanation-only: no automatic fix exists because only
        ///     the author knows which behavior was actually meant.
        /// </summary>
        internal static GUIContent BuildSetInspectorHintUnresolvedNotice(string actionName, string hint) =>
            new(
                $"'{actionName}' names a behavior called '{hint}' that Convai could not find. Check " +
                "for a typo, or make sure the module that provides it is installed.",
                "This action will not run until the behavior name is fixed. Open the Actions " +
                "Editor to change it.");

        #endregion

        #region Window modes (Actions / Scene Knowledge / Character Settings)

        internal static readonly GUIContent ModeActions = new(
            "Actions",
            "Author what this Convai Character can do: add actions, bind scene behaviors, and " +
            "tune each one.");

        internal static readonly GUIContent ModeSceneKnowledge = new(
            "Scene Knowledge",
            "Tell this Convai Character what exists in your scene — the objects and characters " +
            "it can recognize, act on, and talk about.");

        internal static readonly GUIContent ModeCharacterSettings = new(
            "Character Settings",
            "How this Convai Character runs incoming actions, handles interruptions, and " +
            "reports back what happened.");

        internal static readonly GUIContent PlayModeEditingHint = new(
            "Editing is paused during Play Mode so your changes are not lost when it stops. " +
            "Exit Play Mode to make changes here.",
            "Changes made to saved settings during Play Mode would be discarded when Play Mode " +
            "ends, so this window disables them instead.");

        #endregion

        #region Scene Knowledge pane

        internal static readonly GUIContent SceneKnowledgeIntro = new(
            "Tell this Convai Character what exists in your scene. It uses these names and " +
            "descriptions to pick the right thing to act on when you talk to it.",
            "This list is sent to Convai when the character connects, together with its actions.");

        internal static readonly GUIContent KnownObjectsTitle = new(
            "Known Objects",
            "Objects this Convai Character knows about: things it can move to, point at, pick " +
            "up, or talk about.");

        internal static readonly GUIContent KnownCharactersTitle = new(
            "Known Characters",
            "People and characters this Convai Character knows about, each with a short bio.");

        internal static readonly GUIContent AddKnownObjectButton = new(
            "+ Add Object",
            "Add an object this Convai Character should know about. Give it a name and a short " +
            "description.");

        internal static readonly GUIContent AddKnownCharacterButton = new(
            "+ Add Character",
            "Add a character this Convai Character should know about. Give it a name and a " +
            "short bio.");

        internal static readonly GUIContent KnownObjectNameField = new(
            "Name",
            "What the Convai Character calls this object. Players can use this name when asking " +
            "for it.");

        internal static readonly GUIContent KnownObjectDescriptionField = new(
            "Description",
            "A short description sent to the Convai Character so it understands what this " +
            "object is.");

        internal static readonly GUIContent KnownCharacterNameField = new(
            "Name",
            "What the Convai Character calls this character.");

        internal static readonly GUIContent KnownCharacterBioField = new(
            "Bio",
            "A short background sent to the Convai Character so it understands who this " +
            "character is.");

        /// <summary>
        ///     Examples drawn inside the empty field, italic and muted so they cannot be mistaken for a
        ///     value already filled in. Each one is written to be obviously an example rather than a
        ///     plausible entry — a name a project would really use would read as a name already there.
        /// </summary>
        /// <remarks>
        ///     These replaced the muted line that used to sit under an empty Description or Bio saying
        ///     a description helps the character choose. That line told the author it mattered without
        ///     showing what to write, spent a row per empty entry doing it, and asked the same question
        ///     the field's own tooltip already answers. An example answers "what do I type here" in the
        ///     place the answer is needed and disappears the moment it is answered.
        /// </remarks>
        internal static readonly GUIContent KnownObjectNamePlaceholder = new("e.g. workbench");

        internal static readonly GUIContent KnownCharacterNamePlaceholder = new("e.g. shopkeeper");

        internal static readonly GUIContent KnownObjectDescriptionPlaceholder =
            new("e.g. A wooden workbench with tools laid out on it.");

        internal static readonly GUIContent KnownCharacterBioPlaceholder =
            new("e.g. Runs the shop; friendly, talks about the town.");

        internal static readonly GUIContent KnownEntryNameMissing = new(
            "This entry needs a name — it will be skipped until it has one.",
            "The Convai Character cannot know about something unnamed. Type a name above.");

        internal static readonly GUIContent RemoveKnownEntryButton = new(
            Glyphs.Affordance.Remove,
            "Remove this entry from what this Convai Character knows.");

        internal static readonly GUIContent InitialAttentionTitle = new(
            "Initial Attention",
            "Optionally, the one Known Object this Convai Character is paying attention to when " +
            "the conversation starts.");

        internal static readonly GUIContent InitialAttentionField = new(
            "Looking At First",
            "The Known Object this Convai Character starts out focused on. Leave on (None) if " +
            "it should start with no particular focus.");

        internal static readonly GUIContent InitialAttentionNoneChoice = new(
            "(None)",
            "The Convai Character starts with no particular object in mind.");

        internal static readonly GUIContent InitialAttentionExplainer = new(
            "This seeds what the Convai Character believes it is looking at before anyone " +
            "speaks — useful when the scene starts with something clearly in front of it.",
            "Sent to Convai at connect as the character's starting focus.");

        internal static readonly GUIContent ScanSceneTitle = new(
            "Find Targets In Your Scene",
            "Look for Convai Action Target components already placed in your open scene(s).");

        internal static readonly GUIContent ScanSceneButton = new(
            "Scan Again",
            "Searches your open scene(s) again. The list below already refreshes on its own " +
            "whenever the scene changes; use this after editing a Target Name on a component.");

        internal static readonly GUIContent ScanSceneExplainer = new(
            "Objects with a Convai Action Target component introduce themselves to Convai " +
            "Characters automatically. Every one in your open scene(s) is listed here, with " +
            "whether this character knows it yet.",
            "Listing them never changes anything by itself — each row has its own Add button.");

        internal static readonly GUIContent ScanPrecedenceNote = new(
            "When an entry above and a Convai Action Target share a name, the entry wins: its " +
            "description is what Convai receives, and the component's is ignored.",
            "Editing the description on the component will not change anything while an entry of " +
            "the same name exists.");

        /// <summary>
        ///     The scan's outcome in one sentence that accounts for every row it found. Replaces a
        ///     bare count, which left the reader to work out whether "39 found" was good news.
        /// </summary>
        internal static string BuildScanOutcome(int total, int byEntry, int automatic, int notKnown)
        {
            if (total == 0)
                return null;

            string reached = notKnown == 0
                ? $"All {total} targets in your scene already reach this Convai Character"
                : $"{total - notKnown} of the {total} targets in your scene reach this Convai Character";

            string how = (byEntry, automatic) switch
            {
                (0, 0) => string.Empty,
                (0, _) => " — automatically, with no entry needed.",
                (_, 0) => " — each through an entry above.",
                _ => $" — {byEntry} through an entry above, {automatic} automatically."
            };

            if (notKnown == 0)
                return reached + (how.Length == 0 ? "." : how);

            string missing = notKnown == 1
                ? " One reaches it through neither, and is listed below."
                : $" {notKnown} reach it through neither, and are listed below.";

            return reached + (how.Length == 0 ? "." : how) + missing;
        }

        /// <summary>Label for the bulk add, which only ever touches the rows that reach nobody.</summary>
        internal static GUIContent BuildScanAddAllButton(int notKnown) =>
            new(
                notKnown == 1 ? "Add the 1 not known" : $"Add all {notKnown} not known",
                "Creates a Known entry for every target below that this Convai Character cannot " +
                "reach, linked to its object. Targets it already knows are left alone.");

        /// <summary>
        ///     Tooltip for a scan row's clickable name. A plain string rather than a
        ///     <see cref="GUIContent" />: the label is the target's name, which is only known at draw
        ///     time, and a static content with an empty label is exactly what
        ///     <c>EveryStaticContent_HasNonEmptyLabelAndTooltip</c> exists to reject.
        /// </summary>
        internal const string ScanRowPingTooltip = "Select this target in the Hierarchy.";

        internal static readonly GUIContent ScanEmptyResult = new(
            "No Convai Action Target components were found in your open scene(s).",
            "Add a Convai Action Target component to an object, or drag an object into the " +
            "area below.");

        internal static readonly GUIContent ScanStatusKnownPill = new(
            "Known",
            "This target's name matches an entry above, so this Convai Character already knows it.");

        internal static readonly GUIContent ScanStatusAutoPill = new(
            "Automatic",
            "This target registers itself with this Convai Character automatically while it is " +
            "enabled — no entry needed.");

        internal static readonly GUIContent ScanStatusNotKnownPill = new(
            "Not known",
            "This Convai Character has no entry for this target, and the target does not " +
            "register itself for this character automatically.");

        internal static readonly GUIContent ScanAddEntryButton = new(
            "Add",
            "Create a Known entry from this target's name and description so this Convai " +
            "Character knows about it.");

        internal static readonly GUIContent ScanKindObjectPill = new(
            "Object",
            "This target is an actionable object.");

        internal static readonly GUIContent ScanKindCharacterPill = new(
            "Character",
            "This target is an actionable character.");

        internal static readonly GUIContent DropAreaTitle = new(
            "Add From Your Scene",
            "Drag any object from your scene here to make this Convai Character aware of it.");

        internal static readonly GUIContent DropAreaBody = new(
            "Drag an object from the Hierarchy here",
            "Drop an object to choose between adding a live Convai Action Target component or " +
            "a simple described entry.");

        internal static readonly GUIContent DropChoiceExplainer = new(
            "A Convai Action Target component follows the object through spawns and despawns. " +
            "A described entry is fixed text the character always knows, even if the object " +
            "never exists at runtime.",
            "You choose between these two options when you drop an object above.");

        internal static readonly GUIContent SentToConvaiTitle = new(
            "Sent To Convai",
            "A read-only preview of exactly what this Convai Character will be told about your " +
            "scene when it connects.");

        internal static readonly GUIContent SentToConvaiExplainer = new(
            "This preview is produced by the same code that builds the real connect-time " +
            "message, so it always matches what is actually sent.",
            "If something is missing here, it will be missing at connect time too.");

        internal static readonly GUIContent SentToConvaiNothing = new(
            "Nothing will be sent yet — scene knowledge only goes out once this Convai " +
            "Character has at least one working action.",
            "Add an action in the Actions view (and bind its scene behavior) and this preview " +
            "will fill in.");

        internal static readonly GUIContent SentToConvaiEmpty = new(
            "No objects or characters are listed yet. Add some above and they will appear here.",
            "The Convai Character will connect knowing about its actions, but nothing about " +
            "your scene.");

        internal static readonly GUIContent SentToConvaiChannelExplainer = new(
            "Objects with a Convai Action Target component are not part of the connect message. " +
            "They are sent right after, as soon as the conversation starts, so this Convai " +
            "Character knows about them too.",
            "Scan your scene above to see which targets these are.");

        /// <summary>Heading for the entries that travel in the connect payload.</summary>
        internal static string BuildSentAtConnectHeading(int count) =>
            $"Sent when this Convai Character connects ({count})";

        /// <summary>Heading for the scene targets that arrive in the follow-up sync.</summary>
        internal static string BuildSentAtConversationStartHeading(int count) =>
            $"Added as soon as the conversation starts ({count})";

        // Small caps for the two dividers inside the connect group. Set in caps because they are a
        // different class of thing from the names below them — a label, not content — and the pane's
        // label style is only two points smaller than an entry name, which was not enough on its own.

        /// <summary>Divider above the object entries inside a delivery group.</summary>
        internal static string BuildPreviewObjectsLabel(int count) => $"OBJECTS · {count}";

        /// <summary>Divider above the character entries inside a delivery group.</summary>
        internal static string BuildPreviewCharactersLabel(int count) => $"CHARACTERS · {count}";

        /// <summary>Builds the "unknown initial attention" warning row.</summary>
        internal static GUIContent BuildInitialAttentionUnknown(string storedName) =>
            new($"'{storedName}' does not match any Known Object above, so no starting focus " +
                "will be sent.",
                "Pick one of the Known Objects from the dropdown, or add an object with this " +
                "name above.");

        /// <summary>Builds the initial-attention dropdown's current-choice label.</summary>
        internal static GUIContent BuildInitialAttentionChoice(string storedName) =>
            new($"{storedName}   ▾", InitialAttentionField.tooltip);

        // Section summaries. Every collapsible Scene Knowledge section reports what it holds on its
        // own header, so a folded section still answers the question the user folded it to stop
        // scrolling past — "is anything in there?" — without being opened.

        /// <summary>Summary for the Known Objects header.</summary>
        internal static string BuildKnownObjectsSummary(int count) => count switch
        {
            0 => "none yet",
            1 => "1 object",
            _ => $"{count} objects"
        };

        /// <summary>Summary for the Known Characters header.</summary>
        internal static string BuildKnownCharactersSummary(int count) => count switch
        {
            0 => "none yet",
            1 => "1 character",
            _ => $"{count} characters"
        };

        /// <summary>Summary for the Initial Attention header: the chosen object, or that there is none.</summary>
        internal static string BuildInitialAttentionSummary(string storedName) =>
            string.IsNullOrWhiteSpace(storedName) ? "none" : storedName.Trim();

        /// <summary>
        ///     Summary for the Find Targets header. Reports how many targets the last scan found and
        ///     how many of them this Convai Character still does not know, because that second number
        ///     is the only reason to open the section.
        /// </summary>
        internal static string BuildScanSummary(bool hasScanned, int found, int notKnown)
        {
            if (!hasScanned)
                return "not scanned yet";

            if (found == 0)
                return "none found";

            string foundText = found == 1 ? "1 found" : $"{found} found";
            return notKnown == 0 ? $"{foundText} · all known" : $"{foundText} · {notKnown} not known";
        }

        /// <summary>
        ///     Summary for the Sent To Convai header: how much this Convai Character will actually
        ///     be told, and — when the two channels differ — how much of that goes out at connect.
        /// </summary>
        /// <remarks>
        ///     Leads with the total because that is the question the header is read to answer
        ///     ("how much does it know?"), then qualifies it. Reporting only the connect count was
        ///     accurate about the connect message and wrong about the character: scene targets
        ///     arrive a beat later and were invisible here.
        /// </remarks>
        internal static string BuildSentToConvaiSummary(bool omitted, int atConnect, int atConversationStart)
        {
            if (omitted)
                return "nothing sent";

            int total = atConnect + atConversationStart;
            if (total == 0)
                return "nothing listed";

            string entries = total == 1 ? "1 entry" : $"{total} entries";
            return atConversationStart == 0 ? entries : $"{entries} · {atConnect} at connect";
        }

        #endregion

        #region Character Settings pane

        internal static readonly GUIContent CharacterSettingsIntro = new(
            "How this Convai Character performs incoming actions, reacts to interruptions, and " +
            "reports back what happened. The same settings appear on the components themselves — " +
            "edit them in either place.",
            "These settings live on the Convai Action Runner and Convai Action Feedback " +
            "Relay components on this character.");

        internal static readonly GUIContent ExecutionModeSectionTitle = new(
            "Running Actions",
            "Who is responsible for running the action commands this Convai Character receives.");

        internal static readonly GUIContent ExecutionModeField = new(
            "Actions Are Run By",
            "Deciding to act and acting are separate steps. This Convai Character receives its " +
            "action commands either way; this setting says what happens next. It changes nothing " +
            "at runtime — it tells the setup checks whether a missing Action Runner is a " +
            "mistake or your intention.");

        // Plain strings, like the dispatcher policy explanations they sit beside: the hint line under
        // a field is prose the user is already reading, not a control with its own hover help.
        internal const string ExecutionModeDispatcherHint =
            "The Action Runner on this Convai Character resolves each command's target, runs " +
            "the bound action behavior, and reports the outcome. This is the recommended setup.";

        internal const string ExecutionModeCustomCodeHint =
            "Your own script runs the commands, by handling ConvaiCharacter.OnActionsReceived on this " +
            "character; for every character in the room, handle " +
            "ConvaiManager.Events.OnCharacterActionReceived instead, which is only available once the " +
            "manager has initialized. The setup checks stop asking this character for an Action Runner.";

        internal static readonly GUIContent SettingsMissingDispatcherBody = new(
            "This Convai Character has no Action Runner yet, so incoming actions have " +
            "nothing to perform them. Add one to control how actions run — or, if your own " +
            "script runs them, set Actions Are Run By to Custom Code above.",
            "The Action Runner receives the actions this Convai Character decides to take " +
            "and performs them in order.");

        internal static readonly GUIContent SettingsCustomCodeNoDispatcherBody = new(
            "Your own script runs this Convai Character's actions, so there is nothing to set up " +
            "here. The settings on this page belong to the Action Runner, which this " +
            "character deliberately does not use.",
            "Set Actions Are Run By back to Convai Action Runner to use the shipped " +
            "runner and its settings instead.");

        internal static readonly GUIContent SettingsCustomCodeWithDispatcherBody = new(
            "This Convai Character has both: an Action Runner, and a declaration that your " +
            "own script runs its actions. Both will run — the runner performs every command " +
            "it has a bound behavior for, and your script sees the same commands. That is a valid " +
            "arrangement; the settings below still apply to the runner.",
            "Set Actions Are Run By back to Convai Action Runner if your script is no longer " +
            "handling these commands.");

        internal static readonly GUIContent SettingsAddDispatcherButton = new(
            "Add Action Runner",
            "Adds a Convai Action Runner component to this Convai Character. One click, " +
            "Undo-safe.");

        internal static readonly GUIContent SettingsFeedbackSectionTitle = new(
            "Action Feedback",
            "How this Convai Character reports what happened after performing actions — " +
            "out loud, silently, or not at all.");

        internal static readonly GUIContent SettingsMissingRelayBody = new(
            "Actions still run normally without this. Add Action Feedback only if you want this " +
            "Convai Character to remember how actions turned out or talk about what happened.",
            "Optional. Action Feedback connects completed action outcomes to the Convai Character's " +
            "memory and speech.");

        internal static readonly GUIContent SettingsAddRelayButton = new(
            "Add Action Feedback",
            "Optionally adds a Convai Action Feedback Relay component so this character can " +
            "remember and discuss action outcomes. One click, Undo-safe.");

        internal static readonly GUIContent SettingsEditRelayOnComponentButton = new(
            "Edit On Component",
            "Selects the Action Feedback Relay component in the Inspector, where the scripted " +
            "lines table can be edited in full.");

        internal static readonly GUIContent SettingsEventsNoteBody = new(
            "Want to run your own logic when actions start, finish, or fail? Events for every " +
            "milestone are wired on the Action Runner component itself.",
            "Event hookups are kept on the component so this pane stays focused on behavior " +
            "settings.");

        internal static readonly GUIContent SettingsSelectDispatcherButton = new(
            "Select Action Runner",
            "Selects the Action Runner component in the Inspector, where its events can " +
            "be wired up.");

        internal static readonly GUIContent SettingsScriptedLinesLabel = new(
            "Scripted Lines",
            "The exact lines spoken when a feedback mode is set to Scripted Speech. Edited on " +
            "the Action Feedback Relay component.");

        /// <summary>Builds the Character Settings pane's scripted-lines summary row.</summary>
        internal static GUIContent BuildScriptedLinesSummary(int failureLineCount) =>
            new($"{failureLineCount} scripted failure line{(failureLineCount == 1 ? string.Empty : "s")} " +
                "and 1 success line are defined on the component.",
                SettingsScriptedLinesLabel.tooltip);

        #endregion

        #region Test Run / Preview card

        internal static readonly GUIContent TestRunCardTitle = new(
            "Try It",
            "Run this action by itself — no conversation needed — and see exactly what happens.");

        internal static readonly GUIContent TestRunEditModeIntro = new(
            "Enter Play Mode to run this action for real. Meanwhile, check below which scene " +
            "target a spoken phrase would pick.",
            "Test runs work during Play Mode. The check below uses the same target-matching " +
            "steps the Convai Character uses at runtime.");

        internal static readonly GUIContent DryRunPhraseField = new(
            "Try A Target Name",
            "Type the object or character words a player might use — for example \"the red cube\" — " +
            "and see which scene target would be picked.");

        internal static readonly GUIContent DryRunCheckButton = new(
            "Check",
            "Match this phrase against your scene the same way the Convai Character would at " +
            "runtime: authored Scene Knowledge plus enabled Convai Action Target components.");

        internal const string TestRunMissingTargetTitle = "Add a target before testing";

        internal static string BuildTestRunMissingTargetMessage(ConvaiActionTargetRequirement requirement) =>
            requirement switch
            {
                ConvaiActionTargetRequirement.Object =>
                    "There are no objects to match a target name against yet. Add an object, then try a name such as \"the red cube\".",
                ConvaiActionTargetRequirement.Character =>
                    "There are no other characters to match a target name against yet. Add a character, then try its name.",
                _ =>
                    "There are no objects or characters to match a target name against yet. Add one, then try a name such as \"the red cube\"."
            };

        internal static readonly GUIContent TestRunNoTargetEditMode = new(
            "This action does not use a target. Enter Play Mode to run it with its real behavior and parameter values.",
            "Target-name matching is not part of this action, so there is nothing useful to preview in Edit Mode.");

        internal static readonly GUIContent TestRunNoTargetPlayMode = new(
            "No matching target is available in Play Mode. Exit Play Mode and add one in Scene Knowledge before testing this action.",
            "Play Mode uses the character's live target registry. Return to Edit Mode, add a matching " +
            "object or character in Scene Knowledge, then enter Play Mode again.");

        internal static readonly GUIContent TestRunTargetField = new(
            "Target",
            "Which scene target this test run aims the action at. The list shows every target " +
            "the Convai Character can currently resolve, filtered by this action's Valid " +
            "Targets setting.");

        internal static readonly GUIContent TestRunTargetNotSet = new(
            "(choose a target)   ▾",
            "No target chosen yet. Running without one shows exactly how the action fails when " +
            "the Convai Character cannot ground a target.");

        internal static readonly GUIContent TestRunNoTargetsAvailable = new(
            "No matching targets are available right now.",
            "No currently-resolvable scene target matches this action's Valid Targets setting. " +
            "Author one in Scene Knowledge, or add a Convai Action Target component.");

        internal static readonly GUIContent TestRunParametersLabel = new(
            "Values To Send",
            "The values this test run fills in for the action's parameters. Optional ones can " +
            "stay empty.");

        internal static readonly GUIContent TestRunChoiceNotSet = new(
            "(leave empty)",
            "Send no value for this parameter.");

        internal static readonly GUIContent TestRunBoolTrue = new(
            "Yes",
            "Send this parameter as true.");

        internal static readonly GUIContent TestRunBoolFalse = new(
            "No",
            "Send this parameter as false.");

        internal static readonly GUIContent TestRunInvalidNumberHint = new(
            "This is not a number, so it will be sent as plain text.",
            "Number values use digits like 1.5 (with a decimal point). Anything else passes " +
            "through as text.");

        internal static readonly GUIContent TestRunButton = new(
            $"{Glyphs.Run}  Run Now",
            "Runs this action immediately, through the exact same path a real conversation " +
            "command takes. Only the speech wait differs — there is no speech in a test run.");

        internal static readonly GUIContent TestRunNeedsDispatcher = new(
            "This Convai Character has no Action Runner, so nothing can perform a test " +
            "run. Add one in Character Settings.",
            "The Action Runner performs incoming actions. The Character Settings view can " +
            "add it in one click.");

        internal static readonly GUIContent TestRunSpeechGateNote = new(
            "Test runs happen without a conversation, so the wait for speaking is skipped — an " +
            "action set to wait for the Convai Character's line starts immediately here.",
            "In a real conversation, an action can wait for the Convai Character to finish its " +
            "current line first. A test run has no line to wait for, so that wait is skipped " +
            "entirely and the action starts right away.");

        internal static readonly GUIContent TestRunDisabledWarning = new(
            "This action is unticked, so the Convai Character does not know about it and a " +
            "conversation command for it would be declined. Run Anyway performs it once for " +
            "testing, without changing that.",
            "The action's Offer This Action checkbox is off. Tick it in the Command section to " +
            "let the Convai Character offer it again.");

        internal static readonly GUIContent TestRunRunAnywayButton = new(
            $"{Glyphs.Run}  Run Anyway",
            "Runs this unticked action once, just for this test. The action stays unticked and " +
            "the Convai Character still does not offer it.");

        internal static readonly GUIContent TestRunAddToListButton = new(
            "+ Run In Order",
            "Adds this action — with the target and values above — to the ordered run list " +
            "below, so several actions can be rehearsed back to back.");

        internal static readonly GUIContent TestRunListTitle = new(
            "Ordered Run List",
            "Actions queued to run one after another as a single batch — exactly like a " +
            "multi-step conversation command such as \"go to the door and open it\".");

        internal static readonly GUIContent TestRunRunAllButton = new(
            $"{Glyphs.Run}  Run All In Order",
            "Runs every queued action as one ordered batch, using the same step-by-step rules " +
            "as a real multi-step command.");

        internal static readonly GUIContent TestRunClearListButton = new(
            "Clear",
            "Empties the ordered run list without running it.");

        internal static readonly GUIContent TestRunRemoveFromListButton = new(
            Glyphs.Status.Fail,
            "Removes this entry from the ordered run list.");

        internal static readonly GUIContent TestRunWaitingForStart = new(
            "Waiting to start…",
            "The test run is queued. It starts as soon as the current batch (if any) finishes, " +
            "or after the speech wait limit at most.");

        internal static readonly GUIContent TestRunShowTimelineButton = new(
            "Show In Timeline",
            "Switches to the Live view's timeline, where this run's steps are recorded in full.");

        /// <summary>Builds the dry-run success line: which target matched, and at which ladder step.</summary>
        internal static GUIContent BuildDryRunMatched(string targetName, string kindLabel, string stepDescription) =>
            new($"Would pick '{targetName}' ({kindLabel}). {stepDescription}",
                "Resolved by the same target-matching steps the Convai Character uses at runtime.");

        /// <summary>Builds the dry-run miss line for a phrase nothing matched.</summary>
        internal static GUIContent BuildDryRunNoMatch(string phrase) =>
            new($"Nothing in this scene matches '{phrase}'. Try the exact name, an alias, or a " +
                "distinctive part of the name.",
                "No known object or character passed any of the runtime target-matching steps " +
                "for this phrase.");

        /// <summary>Builds the running line for an in-flight test-run step.</summary>
        internal static GUIContent BuildTestRunRunning(string actionName, double elapsedSeconds) =>
            new($"Running '{actionName}'… {elapsedSeconds:0.0}s",
                "This step is executing right now.");

        /// <summary>Builds one finished test-run step's result line.</summary>
        internal static GUIContent BuildTestRunStepOutcome(
            string actionName, string statusLabel, double durationMs, string failureReason)
        {
            string text = string.IsNullOrEmpty(failureReason)
                ? $"{statusLabel} — '{actionName}' in {durationMs:0} ms"
                : $"{statusLabel} — '{actionName}' in {durationMs:0} ms · {failureReason}";
            return new GUIContent(text, "Recorded through the same dispatch events a real conversation command raises.");
        }

        /// <summary>Beginner-readable label for a step's terminal status.</summary>
        internal static string DescribeStepStatus(Convai.Runtime.Actions.ConvaiActionExecutionStatus status) =>
            status switch
            {
                Convai.Runtime.Actions.ConvaiActionExecutionStatus.Succeeded => $"{Glyphs.Status.Ok} Done",
                Convai.Runtime.Actions.ConvaiActionExecutionStatus.Failed => $"{Glyphs.Status.Fail} Failed",
                Convai.Runtime.Actions.ConvaiActionExecutionStatus.Canceled => $"{Glyphs.Status.Neutral} Stopped",
                Convai.Runtime.Actions.ConvaiActionExecutionStatus.TimedOut => $"{Glyphs.Status.Fail} Timed out",
                Convai.Runtime.Actions.ConvaiActionExecutionStatus.Unhandled => $"{Glyphs.Status.Neutral} Not handled",
                _ => status.ToString()
            };

        #endregion

        #region Live view

        internal static readonly GUIContent ModeLive = new(
            $"{Glyphs.Live} Live",
            "Watch this Convai Character perform actions in real time: current activity, a " +
            "step-by-step timeline, live scene knowledge, and the feedback log.");

        internal static readonly GUIContent LiveNowPlayingTitle = new(
            "Now Performing",
            "What this Convai Character is doing right now.");

        internal static readonly GUIContent LiveIdleBody = new(
            "Idle — waiting for actions.",
            "No action batch is executing right now. Give a command in conversation, or use " +
            "Try It on any action in the Actions view.");

        internal static readonly GUIContent LiveNoDispatcherBody = new(
            "This Convai Character has no Action Runner, so there is nothing to watch. Add " +
            "one in Character Settings.",
            "The Action Runner performs incoming actions and raises the events this view " +
            "records.");

        internal static readonly GUIContent LiveStartingBody = new(
            "Starting — preparing the first step…",
            "The batch has begun. The first step may briefly coordinate with the Convai " +
            "Character's speech before it runs.");

        internal static readonly GUIContent LiveTimelineTitle = new(
            "Timeline",
            "The most recent action batches, step by step. Click a step for its full report.");

        internal static readonly GUIContent LiveTimelineIntro = new(
            "Newest batches appear first. Each row shows the result, the action and its target, and how long that step took.",
            "A batch is one group of actions received from conversation or started from Try It.");

        internal static readonly GUIContent LiveTimelineStatusColumn = new(
            "Result",
            "Whether this step succeeded, failed, was declined, was canceled, or is still running.");

        internal static readonly GUIContent LiveTimelineActionColumn = new(
            "Action and target",
            "The action that ran and, when applicable, the object or character it targeted.");

        internal static readonly GUIContent LiveTimelineDurationColumn = new(
            "Duration",
            "How long this step took to finish.");

        internal static readonly GUIContent LiveTimelineEmpty = new(
            "No batches have run yet this session.",
            "Batches appear here as soon as the Convai Character performs actions — from " +
            "conversation or from Try It.");

        internal static readonly GUIContent LiveStepDetailTitle = new(
            "Step Report",
            "Everything recorded about the selected step.");

        internal static readonly GUIContent LiveRegistryTitle = new(
            "Scene Knowledge — Live",
            "Every object and character this Convai Character can currently resolve: what you " +
            "authored, plus everything that registered itself at runtime.");

        internal static readonly GUIContent LiveRegistryEmpty = new(
            "This Convai Character cannot resolve any targets right now.",
            "Author entries in the Scene Knowledge view, or add Convai Action Target " +
            "components to scene objects.");

        internal static readonly GUIContent LiveFeedbackTitle = new(
            "Feedback Log",
            "What the Convai Character said — or silently noted — about how its actions turned " +
            "out.");

        internal static readonly GUIContent LiveFeedbackEmpty = new(
            "No feedback has been composed yet this session.",
            "Entries appear when the Action Feedback Relay reacts to an action outcome.");

        internal static readonly GUIContent LiveFeedbackIntro = new(
            "Newest feedback appears first. Spoken entries were said aloud; silent entries were added only to the character's context.",
            "This log records feedback composed by the Action Feedback Relay after an action outcome.");

        internal static readonly GUIContent LiveFeedbackTimeColumn = new(
            "Time",
            "The scene clock time when this feedback was composed.");

        internal static readonly GUIContent LiveFeedbackDeliveryColumn = new(
            "Delivery",
            "Whether the character said this feedback aloud or kept it as silent context.");

        internal static readonly GUIContent LiveFeedbackMessageColumn = new(
            "Feedback",
            "The exact feedback fact composed for this action outcome.");

        internal static readonly GUIContent LiveDroppedTitle = new(
            "Commands That Never Ran",
            "Actions the Convai Character asked for that were discarded before anything could run " +
            "them — the reason a character can talk normally and still appear to ignore what it " +
            "was asked to do.");

        internal static readonly GUIContent LiveDroppedEmpty = new(
            "Every action command this session reached an Action Behavior.",
            "Entries appear here when a command is discarded before it runs — an unknown action, a " +
            "target that matches nothing, or a batch that arrived while the character was busy.");

        internal static readonly GUIContent LiveDroppedPill = new(
            "Dropped",
            "This command was discarded before it reached an Action Behavior.");

        internal static readonly GUIContent LiveNarratedPill = new(
            "Spoken",
            "This feedback was delivered out loud.");

        internal static readonly GUIContent LiveSilentPill = new(
            "Silent",
            "This feedback was recorded as silent context — the Convai Character knows it, but " +
            "did not say it.");

        internal static readonly GUIContent LiveSourceConversationPill = new(
            "Conversation",
            "This batch came from the live conversation.");

        internal static readonly GUIContent LiveSourceTestRunPill = new(
            "Try It",
            "This batch was started from the editor's Try It panel, not from conversation.");

        internal static readonly GUIContent LiveAbortedPill = new(
            "Stopped early",
            "A failing step stopped the rest of this batch.");

        internal static readonly GUIContent LiveNewPill = new(
            "New",
            "This target registered a moment ago.");

        internal static readonly GUIContent LiveRemovedPill = new(
            "Removed",
            "This target unregistered a moment ago.");

        internal static readonly GUIContent LiveAvailablePill = new(
            "Available",
            "This target can currently be picked when the Convai Character grounds a command.");

        internal static readonly GUIContent LiveUnavailablePill = new(
            "Unavailable",
            "This target is currently skipped when the Convai Character grounds a command " +
            "(marked unavailable at runtime).");

        internal static readonly GUIContent LiveAuthoredPill = new(
            "Authored",
            "This entry comes from your Scene Knowledge authoring.");

        internal static readonly GUIContent LiveRuntimePill = new(
            "Runtime",
            "This entry registered itself at runtime — a Convai Action Target component or " +
            "your own registration code.");

        /// <summary>Builds a timeline batch header line.</summary>
        internal static GUIContent BuildLiveBatchHeader(int batchIndex, int stepCount, double durationMs)
        {
            string text = durationMs > 0d
                ? $"Batch #{batchIndex} · {stepCount} step{(stepCount == 1 ? string.Empty : "s")} · {durationMs:0} ms"
                : $"Batch #{batchIndex} · {stepCount} step{(stepCount == 1 ? string.Empty : "s")} · running";
            return new GUIContent(text, LiveTimelineTitle.tooltip);
        }

        /// <summary>Builds a timeline step row's label.</summary>
        internal static GUIContent BuildLiveStepLabel(
            string actionName, string targetName, string statusLabel, double durationMs)
        {
            string subject = string.IsNullOrEmpty(targetName) ? actionName : $"{actionName} → {targetName}";
            return new GUIContent($"{statusLabel} · {subject} · {durationMs:0} ms",
                "Click for this step's full report.");
        }

        internal static GUIContent BuildLiveStepSubject(string actionName, string targetName)
        {
            string action = string.IsNullOrWhiteSpace(actionName) ? "Unnamed action" : actionName.Trim();
            string text = string.IsNullOrWhiteSpace(targetName)
                ? action
                : $"{action}  →  {targetName.Trim()}";
            return new GUIContent(text, "The action that ran and the target it resolved, when this action used one.");
        }

        internal static GUIContent BuildLiveStepDuration(double durationMs) =>
            new($"{(durationMs < 0d ? 0d : durationMs):0} ms", "How long this step took to finish.");

        #endregion

        #region Live view — Advanced (raw command + resolution tester)

        internal static readonly GUIContent LiveRawCommandTitle = new(
            "Send a Raw Command",
            "Send one action command straight to the dispatcher, bypassing conversation and Try " +
            "It — the fastest way to trigger any action while testing a scene.");

        internal static readonly GUIContent LiveRawCommandIntro = new(
            "Type an action name and an optional target, then send it directly.",
            "Uses the exact same dispatch path a real command from Convai takes, so timing, " +
            "policies, and events all behave identically.");

        internal static readonly GUIContent LiveRawActionNameField = new(
            "Action Name",
            "The exact action name to send (matching is not case-sensitive).");

        internal static readonly GUIContent LiveRawTargetField = new(
            "Target / Parameters",
            "Optional target name, plus any raw parameter text this action expects.");

        internal static readonly GUIContent LiveRawSendButton = new(
            "Send",
            "Sends this command to the picked Convai Character's Action Runner right now.");

        internal static readonly GUIContent LiveRawSendToFirstObjectButton = new(
            "Send To First Known Object",
            "Sends this action name aimed at the first entry in this Convai Character's Known " +
            "Objects list.");

        internal static readonly GUIContent LiveRawAuthoredActionsLabel = new(
            "Authored Actions",
            "One click sends this action, aimed at the first Known Object.");

        internal static readonly GUIContent LivePresetsLabel = new(
            "Presets",
            "Project-specific action templates and one-click test commands registered for this " +
            "project, if any.");

        internal static readonly GUIContent LivePresetsPlayModeNotice = new(
            "Applying templates works only outside Play Mode.",
            "Templates edit the Convai Actions component, so they can only be " +
            "applied in Edit Mode.");

        internal static readonly GUIContent LiveResolutionTesterTitle = new(
            "Test Target Resolution",
            "Check which target a piece of text would resolve to, without sending an action.");

        internal static readonly GUIContent LiveResolutionQueryField = new(
            "Target Text",
            "Text to run through the same target matching Convai uses when grounding a command.");

        internal static readonly GUIContent LiveResolveButton = new(
            "Resolve",
            "Shows which target — and what kind — this text currently resolves to.");

        internal static readonly GUIContent LiveResolutionNoConfigResult = new(
            "No target data is available yet — pick a Convai Character with Known Objects or " +
            "Known Characters.",
            "Resolution needs at least one authored or runtime target to match against.");

        internal static readonly GUIContent LiveResolutionExplainer = new(
            "This shows only the resolved target, not which matching step found it (exact name, " +
            "alias, normalized text, partial match, or nearest match). Open the Console at Debug " +
            "verbosity to see the matched step logged there.",
            "The internal resolution ladder is not exposed to editor tooling.");

        /// <summary>Builds the "Apply &lt;provider&gt; Templates" button label.</summary>
        internal static GUIContent BuildLiveApplyTemplatesButton(string providerDisplayName)
        {
            string name = string.IsNullOrWhiteSpace(providerDisplayName) ? "These" : providerDisplayName.Trim();
            return new GUIContent($"Apply {name} Templates", LivePresetsLabel.tooltip);
        }

        /// <summary>Builds the resolution tester's "no match" result line.</summary>
        internal static GUIContent BuildLiveResolutionNoMatch(string query) =>
            new($"No target matched '{query}'.", LiveResolutionTesterTitle.tooltip);

        /// <summary>Builds the resolution tester's "matched" result line.</summary>
        internal static GUIContent BuildLiveResolutionMatched(string query, string resolvedName, string kind) =>
            new($"Resolved '{query}' to '{resolvedName}' ({kind}).", LiveResolutionTesterTitle.tooltip);

        #endregion

        #region Live view — Advanced (runtime session state + patch composer)

        internal static readonly GUIContent LiveRuntimePatchTitle = new(
            "Runtime Session State & Patch Composer",
            "Backend-confirmed action state, pending updates, and a composer for sending a " +
            "runtime action-config patch — for hand-testing the dynamic update wire protocol.");

        internal static readonly GUIContent LiveRuntimePatchIntro = new(
            "For hand-testing dynamic runtime updates: what the backend has confirmed right now, " +
            "and a way to compose and send a change.",
            "Most testing needs only Send a Raw Command above. Use this when you need to change " +
            "what a Convai Character knows about mid-session, not just trigger an action.");

        internal static readonly GUIContent LiveRuntimeSessionStateTitle = new(
            "Backend-Confirmed State",
            "What the backend has acknowledged for this Convai Character right now.");

        internal static readonly GUIContent LiveRuntimePatchNeedsPlayMode = new(
            "Enter Play Mode to inspect backend-confirmed action state, pending patches, and " +
            "acknowledgement metadata.",
            "This data only exists once a Convai Character has connected to a live session.");

        internal static readonly GUIContent LiveRuntimeNoCharacterBody = new(
            "No Convai Character is selected.",
            "Pick a Convai Character above to inspect its runtime action state.");

        internal static readonly GUIContent LiveRuntimeSessionReady = new(
            "Connected and ready for runtime action updates.",
            "This Convai Character's session can accept a sent patch right now.");

        internal static readonly GUIContent LiveRuntimeSessionNotReady = new(
            "Not ready for runtime action updates yet.",
            "Sending a patch needs an active, connected session.");

        internal static readonly GUIContent LiveRuntimeConfirmedSnapshotLabel = new(
            "Backend-Confirmed Snapshot",
            "The actions, objects, characters, and attention the backend has acknowledged.");

        internal static readonly GUIContent LiveRuntimeSnapshotEmpty = new(
            "No confirmed action data yet.",
            "Nothing has been acknowledged by the backend for this Convai Character yet.");

        internal static readonly GUIContent LiveRuntimeNoPendingUpdates = new(
            "None pending.",
            "No runtime action-state update sent from this window is currently awaiting " +
            "acknowledgement.");

        internal static readonly GUIContent LiveRuntimeLastAckLabel = new(
            "Last Action-Update Acknowledgement",
            "The most recent backend acknowledgement this Convai Editor session has observed.");

        internal static readonly GUIContent LiveRuntimeNoAckObserved = new(
            "No acknowledgement observed yet.",
            "Nothing has come back for a sent update yet.");

        internal static readonly GUIContent LiveRuntimePatchComposerTitle = new(
            "Compose & Send a Patch",
            "Build a runtime action-config change and send it to this Convai Character's session.");

        internal static readonly GUIContent LiveRuntimePatchComposerExplainer = new(
            "Unchecked field = omit and preserve. Checked field with no values = explicitly " +
            "clear. Confirmed state changes only after a matching acknowledgement.",
            "Every field you check replaces that part of the runtime action config; leaving a " +
            "field unchecked never touches it.");

        internal static readonly GUIContent LiveRuntimePatchIncludeActions = new(
            "Include Actions Replacement",
            "Replaces the request-level action list this Convai Character offers.");

        internal static readonly GUIContent LiveRuntimePatchActionsHint = new(
            "One action per line. Empty text clears request-level actions.",
            "Each line becomes one rendered action string sent to the backend.");

        internal static readonly GUIContent LiveRuntimePatchIncludeObjects = new(
            "Include Object Replacement",
            "Replaces the object list this Convai Character knows about.");

        internal static readonly GUIContent LiveRuntimePatchIncludeCharacters = new(
            "Include Character Replacement",
            "Replaces the character list this Convai Character knows about.");

        internal static readonly GUIContent LiveRuntimePatchIncludeNestedAttention = new(
            "Include Action Config Attention",
            "Replaces the attention object carried inside the action config payload.");

        internal static readonly GUIContent LiveRuntimePatchAttentionField = new(
            "Attention Object",
            "The name of the object or character to set as current attention.");

        internal static readonly GUIContent LiveRuntimePatchClearsOnEmpty = new(
            "Empty value clears attention.",
            "Leaving this field blank while the checkbox is on explicitly clears attention.");

        internal static readonly GUIContent LiveRuntimePatchIncludeTopLevelAttention = new(
            "Include Top-Level Attention Override",
            "A separate attention override carried outside the action config payload.");

        internal static readonly GUIContent LiveRuntimePatchTopLevelWinsHint = new(
            "Top-level value wins when nested attention is also included. Empty value clears.",
            "Use this only when the two attention fields need to disagree on purpose.");

        internal static readonly GUIContent LiveRuntimePatchReactionField = new(
            "Reaction",
            "Whether this update should make the Convai Character speak, stay silent, or decide " +
            "automatically.");

        internal static readonly GUIContent LiveRuntimePatchUpdateIdField = new(
            "Update ID",
            "Leave blank to generate one automatically.");

        internal static readonly GUIContent LiveRuntimePatchLoadConfirmedButton = new(
            "Load Confirmed",
            "Copies the current backend-confirmed snapshot into the draft, with every copied " +
            "field marked included.");

        internal static readonly GUIContent LiveRuntimePatchResetButton = new(
            "Reset Draft",
            "Clears the draft back to every field omitted.");

        internal static readonly GUIContent LiveRuntimePatchPreviewButton = new(
            "Preview",
            "Checks this patch locally without sending it.");

        internal static readonly GUIContent LiveRuntimePatchSendButton = new(
            "Send Patch",
            "Sends this patch to the connected session right now.");

        internal static readonly GUIContent LiveRuntimePatchNotConnected = new(
            "Sending a patch needs Play Mode with this Convai Character connected and ready.",
            "The Send Patch button stays disabled until then.");

        internal static readonly GUIContent LiveRuntimePatchNoConfigToLoad = new(
            "No action data is available to load yet.",
            "Pick a Convai Character with either a confirmed session snapshot or authored scene " +
            "knowledge.");

        internal static readonly GUIContent LiveRuntimePatchLoaded = new(
            "Loaded the current snapshot. Every loaded field is included as a replacement; " +
            "uncheck any field that should be preserved instead.",
            "Load Confirmed always marks every copied field included.");

        internal static readonly GUIContent LiveRuntimePatchReset = new(
            "Draft reset. All fields omitted.",
            "Nothing will be sent until at least one field is checked again.");

        internal static readonly GUIContent LiveRuntimePatchGameObjectField = new(
            "GameObject",
            "Optional scene reference this entry should carry.");

        internal static readonly GUIContent LiveRuntimePatchRemoveObjectButton = new(
            "Remove Object",
            "Removes this row from the object replacement list.");

        internal static readonly GUIContent LiveRuntimePatchAddObjectButton = new(
            "Add Object",
            "Adds a blank row to the object replacement list.");

        internal static readonly GUIContent LiveRuntimePatchNoObjectsClears = new(
            "No rows: objects will be cleared.",
            "Sending with this checked and no rows explicitly clears the object list.");

        internal static readonly GUIContent LiveRuntimePatchRemoveCharacterButton = new(
            "Remove Character",
            "Removes this row from the character replacement list.");

        internal static readonly GUIContent LiveRuntimePatchAddCharacterButton = new(
            "Add Character",
            "Adds a blank row to the character replacement list.");

        internal static readonly GUIContent LiveRuntimePatchNoCharactersClears = new(
            "No rows: characters will be cleared.",
            "Sending with this checked and no rows explicitly clears the character list.");

        /// <summary>Builds the "Pending Updates (N)" section label.</summary>
        internal static GUIContent BuildLivePendingUpdatesLabel(int count) =>
            new($"Pending Updates ({count})", LiveRuntimeSessionStateTitle.tooltip);

        /// <summary>Builds the local-rejection status line for an invalid patch.</summary>
        internal static GUIContent BuildLiveRuntimePatchRejected(string reason) =>
            new($"Patch rejected locally: {reason}", LiveRuntimePatchComposerTitle.tooltip);

        /// <summary>Builds the "not queued" status line when a send unexpectedly fails to enqueue.</summary>
        internal static GUIContent BuildLiveRuntimePatchNotQueued(string updateId) =>
            new($"Update {updateId} was not queued. Inspect Console transport warnings.",
                LiveRuntimePatchComposerTitle.tooltip);

        /// <summary>Builds the "pending acknowledgement" status line after a successful send.</summary>
        internal static GUIContent BuildLiveRuntimePatchPending(string updateId, string predictedSummary) =>
            new($"Pending acknowledgement: {updateId}. Confirmed state remains unchanged until " +
                $"commit. {predictedSummary}", LiveRuntimePatchComposerTitle.tooltip);

        /// <summary>Builds the local-preview status line.</summary>
        internal static GUIContent BuildLiveRuntimePatchPreview(string predictedSummary) =>
            new($"Valid local preview. {predictedSummary}", LiveRuntimePatchComposerTitle.tooltip);

        #endregion

        #region Insights

        internal static readonly GUIContent ModeSessionReview = new(
            $"{Glyphs.Capture} Session Review",
            "Review what the Convai Character did in the last Play session: the recorded " +
            "timeline, per-action usage insights, and the feedback log.");

        internal static readonly GUIContent InsightsCardTitle = new(
            "Insights",
            "Per-action usage this session: how often each action ran, how it went, and how " +
            "long it took. Recorded in the editor only — nothing leaves this machine.");

        internal static readonly GUIContent InsightsIntro = new(
            "All actions recorded this session are shown below. Choose how to order them; the selection never hides actions.",
            "Only completed action runs are counted. An action that is still running appears after it finishes.");

        internal static readonly GUIContent InsightsEmpty = new(
            "No actions have run yet, so there is nothing to summarize.",
            "Insights fill in as soon as the Convai Character performs actions — from " +
            "conversation or from Try It.");

        internal static readonly GUIContent InsightsCopyReportButton = new(
            "Copy Session Summary",
            "Copies every action shown below, including outcomes, timings, and latest failure details, " +
            "to the clipboard as a Markdown table. Paste it into a chat, document, or issue.");

        internal static readonly GUIContent InsightsReportCopied = new(
            "Session summary copied as Markdown",
            "The clipboard now contains the complete per-action session summary shown in Insights.");

        internal static readonly GUIContent InsightsOrderLabel = new(
            "Order by",
            "Changes the row order only. No actions are filtered out.");

        internal static readonly GUIContent InsightsSortMostFailed = new(
            "Problems First",
            "Show actions with the most failed or declined runs first. Successful actions remain in the list.");

        internal static readonly GUIContent InsightsSortMostUsed = new(
            "Most Runs",
            "Show the most frequently run actions first. No actions are hidden.");

        internal static readonly GUIContent InsightsSortByName = new(
            "A–Z",
            "Order every recorded action alphabetically by name.");

        internal static readonly GUIContent InsightsActionColumn = new(
            "Action",
            "The action name recorded in this session.");

        internal static readonly GUIContent InsightsOutcomeColumn = new(
            "Session outcomes",
            "Total completed runs, split into succeeded, failed, and declined outcomes.");

        internal static readonly GUIContent InsightsTimingColumn = new(
            "Timing",
            "Average duration and the slowest run recorded for this action.");

        internal static GUIContent BuildInsightsOrderExplanation(ConvaiActionsInsightsSort sort) =>
            sort switch
            {
                ConvaiActionsInsightsSort.MostUsed => new GUIContent(
                    "Ordered by total runs. Every recorded action is still included.", InsightsOrderLabel.tooltip),
                ConvaiActionsInsightsSort.Name => new GUIContent(
                    "Ordered alphabetically. Every recorded action is still included.", InsightsOrderLabel.tooltip),
                _ => new GUIContent(
                    "Ordered by failed and declined runs, with problems on top. Every recorded action " +
                    "is still included.", InsightsOrderLabel.tooltip)
            };

        internal static readonly GUIContent InsightsPostPlayBanner = new(
            "Play Mode has ended — this is the recorded session. It clears when the next Play " +
            "session starts.",
            "The session recording survives leaving Play Mode so results can be reviewed. " +
            "Entering Play Mode again starts a fresh recording.");

        internal static readonly GUIContent DetailHistoryLabel = new(
            "This Session",
            "How this action's most recent run went in the current (or just-ended) Play session.");

        /// <summary>Builds one insights row's explicit outcome split.</summary>
        internal static GUIContent BuildInsightsOutcomes(
            int runCount, int succeededCount, int failedCount, int unhandledCount) =>
            new($"{runCount} run{(runCount == 1 ? string.Empty : "s")} · " +
                $"{succeededCount} succeeded · {failedCount} failed · {unhandledCount} declined",
                "Completed runs recorded for this action in the current session. Declined means the scene behavior chose not to handle the action.");

        /// <summary>Builds one insights row's duration summary.</summary>
        internal static GUIContent BuildInsightsDurations(double averageMs, double maxMs) =>
            new($"Avg {averageMs:0} ms\nSlowest {maxMs:0} ms",
                "Average and slowest completed run this session.");

        /// <summary>Builds one insights row's last-failure line.</summary>
        internal static GUIContent BuildInsightsLastFailure(string reason) =>
            new($"Latest problem: {reason}",
                "The most recent recorded failure reason for this action this session.");

        /// <summary>Builds the detail pane's compact last-run strip for one action.</summary>
        internal static GUIContent BuildActionLastRunStrip(string statusLabel, double durationMs, int runCount) =>
            new($"Last run: {statusLabel} · {durationMs:0} ms · {runCount} run{(runCount == 1 ? string.Empty : "s")} this session",
                DetailHistoryLabel.tooltip);

        #endregion

        #region Productivity (row operations, multi-select, filters, import/export)

        internal static readonly GUIContent RowOverflowButton = new(
            "…",
            "More operations for this action: duplicate, copy, paste, move it into an Action " +
            "Set, or delete it.");

        internal static readonly GUIContent ToolbarOverflowButton = new(
            "…",
            "More tools: paste a copied action, or save and load this Convai Character's " +
            "actions as a file.");

        internal static readonly GUIContent RowMenuDuplicate = new(
            "Duplicate",
            "Adds a copy of this action to this Convai Character. The copy's name gets a " +
            "'Copy' suffix so the two never clash.");

        internal static readonly GUIContent RowMenuCopy = new(
            "Copy",
            "Copies this action so it can be pasted onto this or any other Convai Character. " +
            "The copy carries the behavior's type, not the scene component itself.");

        internal static readonly GUIContent RowMenuPaste = new(
            "Paste Action",
            "Adds the most recently copied action to this Convai Character. If a matching " +
            "behavior component already exists here, the pasted action binds to it " +
            "automatically.");

        internal static readonly GUIContent RowMenuDelete = new(
            "Delete",
            "Removes this action from this Convai Character. Undo-safe.");

        internal static readonly GUIContent RowMenuMakeLocalCopy = new(
            "Make Local Copy",
            "Copies this shared action onto this Convai Character as its own editable action. " +
            "The Action Set itself is not changed.");

        internal static readonly GUIContent RowMenuRemoveFromSet = new(
            "Remove From Set",
            "Removes this action from its Action Set. Every Convai Character using that set " +
            "loses it.");

        internal static readonly GUIContent ExtractMenuRoot = new(
            "Extract To Action Set",
            "Moves this Convai Character's own selected actions into a reusable Action Set " +
            "asset, so other characters can share them.");

        internal static readonly GUIContent ExtractNewSetMenuItem = new(
            "New Action Set…",
            "Creates a new Action Set asset and moves the selected actions into it. The set is " +
            "assigned to this Convai Character, so nothing stops working.");

        internal static readonly GUIContent MultiSelectionExplainer = new(
            "Changes below apply to every selected action. Ctrl-click adds or removes one " +
            "action from the selection; Shift-click selects a range.",
            "Click any single action to go back to editing one action at a time.");

        internal static readonly GUIContent MultiSharedSelectionNote = new(
            "Selected actions that live in shared Action Sets stay in their sets: Duplicate " +
            "makes local copies of them, and Extract moves only this character's own actions.",
            "Shared actions belong to their Action Set asset. Use Make Local Copy on a shared " +
            "action to get an editable copy on this character.");

        internal static readonly GUIContent MultiDuplicateButton = new(
            "Duplicate Selected",
            "Adds a copy of every selected action to this Convai Character, each with a " +
            "collision-safe 'Copy' name.");

        internal static readonly GUIContent MultiOfferButton = new(
            "Offer Selected",
            "Ticks Offer This Action on every selected action, so the Convai Character knows " +
            "about all of them.");

        internal static readonly GUIContent MultiStopOfferButton = new(
            "Stop Offering Selected",
            "Unticks Offer This Action on every selected action. The Convai Character will not " +
            "know about or offer them.");

        internal static readonly GUIContent MultiDeleteButton = new(
            "Delete Selected",
            "Removes every selected action. Actions in shared Action Sets are removed from " +
            "their set, which affects every character using it — you are asked first.");

        internal static readonly GUIContent MultiExtractButton = new(
            "Extract To Action Set ▾",
            "Moves the selected actions authored on this Convai Character into a new or " +
            "existing Action Set asset, so other characters can share them.");

        internal static readonly GUIContent MultiRunInOrderButton = new(
            $"{Glyphs.Run}  Run Selected In Order",
            "Runs the selected actions one after another as a single ordered batch — the same " +
            "step-by-step rules as a real multi-step conversation command.");

        internal static readonly GUIContent MultiRunNeedsPlayMode = new(
            "Enter Play Mode to run the selected actions in order.",
            "Test runs work during Play Mode only. The selection is kept when you enter Play " +
            "Mode.");

        internal static readonly GUIContent FilterMenuAll = new(
            "All Actions",
            "Show every action.");

        internal static readonly GUIContent FilterMenuNeedsAttention = new(
            "Needs Attention",
            "Show only actions with a setup warning or error.");

        internal static readonly GUIContent FilterMenuNotOffered = new(
            "Not Offered",
            "Show only actions whose Offer This Action checkbox is unticked.");

        internal static readonly GUIContent FilterMenuThisCharacter = new(
            "This Character",
            "Show only actions authored directly on this Convai Character.");

        internal static readonly GUIContent FilterMenuFromSets = new(
            "From Action Sets",
            "Show only actions shared from assigned Action Set assets.");

        internal static readonly GUIContent FilterChoiceAll = new(
            "Show: All   ▾",
            FilterMenuAll.tooltip);

        internal static readonly GUIContent FilterChoiceNeedsAttention = new(
            "Show: Needs Attention   ▾",
            FilterMenuNeedsAttention.tooltip);

        internal static readonly GUIContent FilterChoiceNotOffered = new(
            "Show: Not Offered   ▾",
            FilterMenuNotOffered.tooltip);

        internal static readonly GUIContent FilterChoiceThisCharacter = new(
            "Show: This Character   ▾",
            FilterMenuThisCharacter.tooltip);

        internal static readonly GUIContent FilterChoiceFromSets = new(
            "Show: From Action Sets   ▾",
            FilterMenuFromSets.tooltip);

        internal static readonly GUIContent NoFilterResults = new(
            "No actions match this filter.",
            "Switch the filter back to All Actions to see everything again.");

        internal static readonly GUIContent ExportActionsMenuItem = new(
            "Export Actions…",
            "Saves this Convai Character's own actions to a JSON file, for sharing or version " +
            "control outside Unity.");

        internal static readonly GUIContent ExportWithKnowledgeMenuItem = new(
            "Export Actions + Scene Knowledge…",
            "Saves this Convai Character's own actions plus its Known Objects, Known " +
            "Characters, and starting focus to a JSON file.");

        internal static readonly GUIContent ImportActionsMenuItem = new(
            "Import Actions…",
            "Adds actions from a previously exported JSON file. When names clash with existing " +
            "actions, you choose whether to skip, overwrite, or keep both.");

        /// <summary>Builds the multi-select summary card's title.</summary>
        internal static GUIContent BuildMultiSelectionTitle(int count) =>
            new($"{count} action{(count == 1 ? string.Empty : "s")} selected", MultiSelectionExplainer.tooltip);

        #endregion

        #region Troubleshooter findings (plain-English copy)

        // No user-visible string below names a C# component type directly. Where a finding
        // is about a specific resolved component, the window resolves its display name through
        // ConvaiComponentTypeResolver.DisplayName / the archetype catalog's DisplayName first —
        // the strings here only ever receive that already-resolved display name.

        internal const string TroubleshooterSelectCharacterPrompt = "Select a Convai Character to check.";

        // Whether this character has anywhere to keep authored actions at all.
        internal const string TroubleshooterActionsEnabledTitle = "Actions Enabled";
        internal const string TroubleshooterActionsEnabledReadyMessage = "Actions are enabled on this character.";
        internal const string TroubleshooterActionsEnabledMissingMessage = "This character can't hold any actions yet.";

        // Whether anything on this character actually performs received actions.
        internal const string TroubleshooterRunningActionsTitle = "Running Actions";
        internal const string TroubleshooterRunningActionsReadyMessage = "This character is set up to run actions.";
        internal const string TroubleshooterRunningActionsMissingMessage =
            "Nothing is set up to run actions, so this character will never do anything it's asked to do.";

        internal static readonly GUIContent TroubleshooterFixSetUpActionRunning = new(
            "Set Up Action Running",
            "Adds a component that runs this Convai Character's actions as they arrive. Undo-safe.");

        // Whether this character can report how an action turned out.
        internal const string TroubleshooterSpokenFeedbackTitle = "Action Feedback (Optional)";
        internal const string TroubleshooterSpokenFeedbackReadyMessage =
            "This character can remember how actions turned out and talk about what happened.";
        internal const string TroubleshooterSpokenFeedbackMissingMessage =
            "Actions still run normally. Add Action Feedback only if you want this character to " +
            "remember whether they worked or talk about what happened.";

        internal static readonly GUIContent TroubleshooterFixAddSpokenFeedback = new(
            "Add Action Feedback",
            "Optionally adds a component that lets this Convai Character remember and discuss " +
            "action outcomes. Undo-safe.");

        // Whether any actions have been authored yet.
        internal const string TroubleshooterActionsAuthoredTitle = "Actions Authored";
        internal const string TroubleshooterNoActionsAuthoredMessage =
            "No actions are set up yet. Open Convai > Actions Editor and use \"+ Add Action\" to author your first action.";

        // A bound action names a behavior component this character does not have yet.
        internal const string TroubleshooterBehaviorMissingMessageFormat =
            "This action is set up to use {0}, but this character doesn't have it yet.";

        // The authored behavior name matches nothing Convai knows about at all (typo, rename, or
        // an uninstalled module) — distinct from TroubleshooterBehaviorMissingMessageFormat, where
        // the name resolves fine but the character just doesn't have that behavior yet.
        // Explanation-only: only the author knows which behavior was actually meant, so there is no
        // safe automatic fix.
        internal const string TroubleshooterBehaviorHintUnresolvedMessageFormat =
            "This action is set up to use a behavior named '{0}', but Convai could not find one by " +
            "that name. Check for a typo, or make sure the module that provides it is installed.";

        internal const string TroubleshooterBehaviorUnboundMessage =
            "No behavior is chosen for this action yet, and nothing suggests one automatically. " +
            "This action will never run until you pick one in the Actions Editor.";

        // The character declares that project code runs its actions, so the shipped dispatcher is
        // deliberately absent and asking for it would be wrong.
        internal const string TroubleshooterRunningActionsCustomCodeMessage =
            "Your own script runs this character's actions, so no Action Runner is needed here. " +
            "Nothing on this character will run the commands by itself.";

        // A Known Object/Character entry has no scene object linked to it.
        internal const string TroubleshooterTargetUnlinkedMessage =
            "This entry has no object from the scene linked to it yet, so this character has nothing to act on for it.";

        // A scene target this character doesn't know about and doesn't learn about automatically.
        internal const string TroubleshooterSceneTargetUnknownMessage =
            "This scene object has a Convai Action Target component, but this character has no Known " +
            "entry for it, and it doesn't register itself for this character automatically.";

        #endregion

        #region Troubleshooter (one-click fixes)

        internal static readonly GUIContent TroubleshooterRerunButton = new(
            "Re-run Checks",
            "Diagnose this Convai Character's action setup again from scratch.");

        internal static readonly GUIContent TroubleshooterFixAllButton = new(
            "Fix All",
            "Runs every one-click fix below in order, as a single undoable step, then re-runs " +
            "the checks.");

        internal static readonly GUIContent TroubleshooterFixAddComponent = new(
            "Add Component",
            "Adds the missing component to this Convai Character. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixAddBehavior = new(
            "Add Behavior Component",
            "Adds the matching behavior component to this Convai Character so the action can " +
            "run. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixRemoveDuplicate = new(
            "Remove Duplicate",
            "Removes this later duplicate action. The first action with this name stays exactly " +
            "as authored. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixClearAttention = new(
            "Clear Starting Focus",
            "Clears the starting-focus name, since it matches no Known Object. The Convai " +
            "Character will start with no particular focus. Undo-safe.");

        internal static readonly GUIContent TroubleshooterFixAddKnowledge = new(
            "Add To Scene Knowledge",
            "Creates a Known entry from this target's name and description so this Convai " +
            "Character knows about it. Undo-safe.");

        /// <summary>Builds the post-Fix-All summary line.</summary>
        internal static GUIContent BuildTroubleshooterFixAllSummary(int appliedCount) =>
            new($"Applied {appliedCount} fix{(appliedCount == 1 ? string.Empty : "es")}. One Undo " +
                "step reverts them all.",
                TroubleshooterFixAllButton.tooltip);

        #endregion

        #region Scene Knowledge — linking an entry to the scene

        internal static readonly GUIContent KnownEntrySceneObjectField = new(
            "Scene Object",
            "The object in your scene this entry stands for. Without it the Convai Character can " +
            "talk about this entry but cannot walk to it, look at it, or use it.");

        internal static readonly GUIContent KnownEntryTextOnlyToggle = new(
            "Text only",
            "Tick when nothing in the scene answers to this entry. The Convai Character will know " +
            "the name and be able to talk about it, and will not try to act on it.");

        /// <summary>
        ///     The settled state is a badge beside the Scene Object field, not a line under it. Two
        ///     reasons, and the first one is correctness: this is a readout of that field, and the
        ///     field is all its classifier looks at — it does not know whether the entry has a name,
        ///     and a nameless entry is dropped by target resolution however well it is linked. The old
        ///     wording ("this Convai Character can act on it") therefore sat directly under
        ///     <see cref="KnownEntryNameMissing" /> contradicting it, and the green half was the false
        ///     one. Second, this is the state every healthy entry in a list is in, so a sentence here
        ///     is the same sentence N times, which is how a status column stops being read at all —
        ///     and then a genuinely broken entry no longer stands out either.
        ///     <see cref="KnownEntryUnlinkedStatus" /> and the answered-by-target line keep their full
        ///     sentences on a row of their own: those appear rarely, carry a next move, and are worth
        ///     the row. The text-only state has no status text at all — its tick box is the statement,
        ///     and repeating it back would be the same redundancy in a third form.
        /// </summary>
        internal static readonly GUIContent KnownEntryLinkedStatus = new(
            "Linked",
            "This entry points at the object beside it, so actions that need a target can resolve it.");

        internal static readonly GUIContent KnownEntryUnlinkedStatus = new(
            "No scene object yet — the Convai Character has nothing to act on for this entry.",
            "Drag an object into the field above, use Find In Scene, or tick Text only if there " +
            "really is nothing to act on.");

        internal static readonly GUIContent KnownEntryFindInSceneButton = new(
            "Find In Scene",
            "Looks for an object in your open scene(s) that answers to this name.");

        internal static readonly GUIContent KnownEntryUseObjectNameButton = new(
            "Use Object's Name",
            "Renames this entry to match the linked object.");

        /// <summary>
        ///     The one place the two extras are explained. It is the right place because it owns them:
        ///     the fields cannot be reached without passing this row, and an entry list draws this row
        ///     once per entry as structure the eye skips, never as a sentence the eye is asked to read
        ///     again. Everything inside it is therefore a bare labelled field — see
        ///     <c>DrawKnownEntryAdvanced</c>.
        /// </summary>
        internal static readonly GUIContent KnownEntryExtrasTitle = new(
            "Extras (optional)",
            "Two optional settings that stay in Unity, neither sent to Convai. Other Words adds " +
            "wording that should also match this entry; Where To Stand is the exact spot this " +
            "Convai Character goes to when it acts on it.");

        internal static readonly GUIContent KnownEntryAliasesField = new(
            "Other Words",
            "Extra wording that should match this entry when a request arrives. The name above " +
            "already matches on its own, and so does close wording — add one only for a word the " +
            "name would miss, like “lamp” for a lantern. Never sent to Convai.");

        /// <summary>Drawn inside the alias field while it is empty: the example, where it is needed.</summary>
        internal static readonly GUIContent KnownEntryAliasPlaceholder = new(
            "e.g. “lamp” for a lantern");

        internal static readonly GUIContent KnownEntryAddAliasButton = new(
            "+ Add Another Word",
            "Adds another word that should match this entry.");

        internal static readonly GUIContent KnownEntryRemoveAliasButton = new(
            Glyphs.Affordance.Remove,
            "Removes this word.");

        internal static readonly GUIContent KnownEntryObjectApproachField = new(
            "Where To Stand",
            "Where this Convai Character ends up when it acts on this object. Leave empty to use " +
            "the object itself; point it at a small empty Transform to be exact — in front of a " +
            "door rather than inside it. Never sent to Convai.");

        internal static readonly GUIContent KnownEntryCharacterApproachField = new(
            "Where To Stand",
            "Where this Convai Character stops when it approaches this character. Leave empty to " +
            "walk to them directly; point it at a small empty Transform to stop in front of them, " +
            "at talking distance. Never sent to Convai.");

        internal static readonly GUIContent KnownEntryLinkTargetButton = new(
            "Link It",
            "Points this entry at that object, so the link is written down instead of being " +
            "worked out every time the scene loads.");

        internal static readonly GUIContent KnownEntryOverridesTargetStatus = new(
            "Overrides target",
            "A Convai Action Target in your scene has this name. This entry's description is what " +
            "Convai receives; the component's own description is ignored.");

        internal static readonly GUIContent KnownListTargetNote = new(
            "An object carrying a Convai Action Target component does not need an entry here — it " +
            "introduces itself. Write an entry when you want to name or describe something in your " +
            "own words, or when it will not exist at run time.",
            "Find Targets In Your Scene, below, shows which objects already introduce themselves.");

        internal static readonly GUIContent KnownListDropHint = new(
            "Drag objects from the Hierarchy onto this list to add them, already linked.",
            "Each dropped object becomes an entry named after it, pointing at it.");

        /// <summary>Status line for an entry a scene Convai Action Target answers to by name.</summary>
        internal static GUIContent BuildKnownEntryAnsweredStatus(string objectName) =>
            new($"'{objectName}' in your scene answers to this name, so it will be used at run time.",
                "That object carries a Convai Action Target component. Link it here to make it " +
                "explicit and survive a rename.");

        /// <summary>Offer to link the one scene object that matches this entry's name.</summary>
        internal static GUIContent BuildKnownEntryLinkSuggestion(string objectName) =>
            new($"Link '{objectName}'",
                "Points this entry at that object.");

        /// <summary>Result line when a name search found nothing.</summary>
        internal static GUIContent BuildKnownEntryNoMatchFound(string entryName) =>
            new($"Nothing in your open scene(s) is called '{entryName}'.",
                "Drag the object into the field above, or rename the entry to match the object.");

        /// <summary>Result line when a name search found more than one candidate.</summary>
        internal static GUIContent BuildKnownEntryManyMatchesFound(int count) =>
            new($"{count} objects in your open scene(s) share this name — pick the right one below.",
                "Only you can tell which one this entry means.");

        #endregion

        #region Grouping

        private const string GroupByTooltip =
            "Choose how this list is arranged. Grouping only changes what you see here — it never " +
            "changes what this Convai Character can do.";

        internal static readonly GUIContent GroupChoiceSource = new("Group: Source ▾", GroupByTooltip);

        internal static readonly GUIContent GroupChoiceCategory = new("Group: Category ▾", GroupByTooltip);

        internal static readonly GUIContent GroupChoiceStatus = new("Group: Status ▾", GroupByTooltip);

        internal static readonly GUIContent GroupChoiceBehavior = new("Group: Behavior ▾", GroupByTooltip);

        internal static readonly GUIContent GroupMenuSource = new(
            "Source (this character, Action Sets)",
            "Arrange by where each action comes from.");

        internal static readonly GUIContent GroupMenuCategory = new(
            "Category (your own)",
            "Arrange by the categories you file actions under.");

        internal static readonly GUIContent GroupMenuStatus = new(
            "Status",
            "Arrange by what is ready and what still needs work.");

        internal static readonly GUIContent GroupMenuBehavior = new(
            "Behavior",
            "Arrange by the part of Convai each action uses.");

        internal static readonly GUIContent CategoryUncategorizedHeader = new(
            ConvaiActionsGrouping.UncategorizedTitle,
            "Actions you have not filed under a category yet.");

        internal static readonly GUIContent CategoryHeaderMenuRename = new(
            "Rename Category…",
            "Rename this category on every action filed under it, in one undoable step.");

        internal static readonly GUIContent CategoryHeaderMenuRemove = new(
            "Remove Category",
            "Empties this category. The actions themselves are kept — they simply go back to " +
            "being uncategorized.");

        internal static readonly GUIContent CategoryHeaderMenuSelectAll = new(
            "Select Every Action In This Category",
            "Selects them all so you can work on them together.");

        internal static readonly GUIContent RowMenuCategoryRoot = new(
            "File Under Category",
            "Choose which of your categories this action belongs to.");

        internal static readonly GUIContent RowMenuNewCategory = new(
            "New Category…",
            "Create a category and file this action under it.");

        internal static readonly GUIContent RowMenuNoCategory = new(
            "None (Uncategorized)",
            "Take this action out of its category. The action itself is untouched.");

        internal static readonly GUIContent CategoryPromptNameField = new(
            "Category Name",
            "A short label of your own — for example 'Counter' or 'Tour'. It is never sent to " +
            "Convai and never changes what this Convai Character does.");

        internal static readonly GUIContent CategoryPromptCreateButton = new(
            "Create",
            "Files the chosen actions under this category.");

        internal static readonly GUIContent CategoryPromptRenameButton = new(
            "Rename",
            "Renames the category on every action filed under it.");

        internal static readonly GUIContent CategoryPromptCancelButton = new(
            "Cancel",
            "Closes without changing anything.");

        internal static readonly GUIContent CategoryPromptExistingLabel = new(
            "Or pick one you already use",
            "Filing under a name you already use keeps your list readable.");

        internal static readonly GUIContent SuggestCategoriesButton = new(
            "Suggest Categories",
            "Proposes a starting set of categories based on what each action uses. Nothing is " +
            "written until you accept.");

        internal static readonly GUIContent SuggestCategoriesDismissButton = new(
            "No Thanks",
            "Retires this suggestion. You can still file actions under categories yourself at any " +
            "time, from any action's right-click menu.");

        internal static readonly GUIContent SuggestCategoriesExplainer = new(
            "This character has a lot of actions and none of them are filed yet. Convai can " +
            "propose a starting set of categories for you to rename or reject.",
            "Categories are yours: they organize this list and nothing else.");

        internal static readonly GUIContent SuggestPromptApplyButton = new(
            "File Them",
            "Files each action under the proposed category, in one undoable step.");

        internal static readonly GUIContent SuggestPromptExplainer = new(
            "Rename anything you like before accepting. Leave a name empty to leave those " +
            "actions uncategorized.",
            "Nothing is written until you choose to file them.");

        /// <summary>Group header pill reporting how many actions inside still need work.</summary>
        internal static GUIContent BuildGroupAttentionPill(int count) =>
            new($"{count} to fix",
                "How many actions in this group are not ready yet. Open the group to see which.");

        /// <summary>Row-menu item filing an action under one existing category.</summary>
        internal static GUIContent BuildCategoryMenuItem(string category) =>
            new(category, $"File under '{category}'.");

        /// <summary>Warning shown when a new category name is a hair away from one already in use.</summary>
        internal static GUIContent BuildCategoryNearDuplicateWarning(string existing) =>
            new($"You already use '{existing}'. Two categories this similar are hard to tell apart " +
                "later — pick the existing one unless you really mean both.",
                "This is only a warning; you can still create it.");

        /// <summary>Confirmation line for a category rename, naming how many actions follow it.</summary>
        internal static GUIContent BuildCategoryRenameSummary(string category, int actionCount) =>
            new($"Renaming '{category}' will update {actionCount} action{(actionCount == 1 ? string.Empty : "s")}.",
                CategoryHeaderMenuRename.tooltip);

        /// <summary>Confirmation line for emptying a category.</summary>
        internal static GUIContent BuildCategoryRemoveSummary(string category, int actionCount) =>
            new($"{actionCount} action{(actionCount == 1 ? string.Empty : "s")} will go back to " +
                $"being uncategorized. '{category}' itself is just a label, so nothing is deleted.",
                CategoryHeaderMenuRemove.tooltip);

        /// <summary>Header for a category group, tooltipped with what the category is for.</summary>
        internal static GUIContent BuildCategoryGroupLabel(string category) =>
            new(category,
                $"Actions you filed under '{category}'. Categories organize this list only — they are " +
                "never sent to Convai.");

        /// <summary>Note shown before a category edit that reaches beyond this character.</summary>
        internal static GUIContent BuildCategorySharedNote(int sharedCount) =>
            new($"{sharedCount} of these action{(sharedCount == 1 ? " is" : "s are")} shared from an " +
                "Action Set, so filing them changes the set for every Convai Character using it.",
                "Shared actions live in an asset, not on this character.");

        #endregion
    }
}
