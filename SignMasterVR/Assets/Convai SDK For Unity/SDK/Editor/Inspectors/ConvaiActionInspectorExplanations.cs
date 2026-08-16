using Convai.Runtime.Actions;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Plain-language, testable explanation of what each <see cref="ConvaiActionFeedbackMode" />
    ///     value does — shown under the matching dropdown in
    ///     <see cref="ConvaiActionFeedbackRelayEditor" /> . Kept as a pure
    ///     string mapping (no <c>UnityEditor</c>/<see cref="UnityEngine.GUIContent" /> dependency) so
    ///     an EditMode test can assert every enum value is covered without a GUI pass.
    /// </summary>
    internal static class ConvaiActionFeedbackModeExplanations
    {
        /// <summary>One beginner-readable sentence describing what <paramref name="mode" /> does.</summary>
        internal static string Explain(ConvaiActionFeedbackMode mode) => mode switch
        {
            ConvaiActionFeedbackMode.Off =>
                "Nothing happens — the Convai Character does not react to this outcome at all.",
            ConvaiActionFeedbackMode.SilentContext =>
                "The Convai Character quietly remembers what happened, without saying anything out loud.",
            ConvaiActionFeedbackMode.NarrateInCharacter =>
                "The Convai Character explains what happened in its own words.",
            ConvaiActionFeedbackMode.ScriptedSpeech =>
                "The Convai Character speaks one of the exact lines you write below.",
            _ => "This option has no description yet."
        };
    }

    /// <summary>
    ///     Everything the editor says about what a Convai Character does with an answer an action
    ///     found — the option hints, the resolved "right now" sentence, and the one advisory worth
    ///     raising when the settings disagree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written once, here, because both the Actions Editor's Command card and
    ///         <c>ConvaiActionSetupReport</c> have to reach the same verdict. Two copies of a check
    ///         is how the same project came to report a different number of problems depending on
    ///         which window you opened it in.
    ///     </para>
    ///     <para>
    ///         Pure strings, no <c>UnityEditor</c> or <see cref="UnityEngine.GUIContent" />
    ///         dependency, so an EditMode test can assert every enum value is covered without a GUI
    ///         pass.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionAnswerDeliveryExplanations
    {
        /// <summary>One beginner-readable sentence describing what <paramref name="delivery" /> does.</summary>
        internal static string Explain(ConvaiActionAnswerDelivery delivery) => delivery switch
        {
            ConvaiActionAnswerDelivery.UseCharacterSetting =>
                "The Convai Character does whatever its Action Feedback Relay is set to.",
            ConvaiActionAnswerDelivery.RememberOnly =>
                "The Convai Character quietly remembers what this action found, without saying it out loud.",
            ConvaiActionAnswerDelivery.MentionIfRelevant =>
                "The Convai Character decides for itself whether this is worth bringing up.",
            ConvaiActionAnswerDelivery.TellThePlayer =>
                "The Convai Character says what this action found out. Use this for actions that " +
                "answer a question — reading a gauge, counting things, measuring a distance.",
            _ => "This option has no description yet."
        };

        /// <summary>
        ///     The line that closes the loop for someone whose character went quiet. Shown only for
        ///     the option that fixes it, because that is the moment they are looking for it.
        /// </summary>
        internal static string TellThePlayerFootnote =>
            "With this off, the character performs the action and then says nothing — what it found " +
            "reaches only its own memory, never the player.";

        /// <summary>
        ///     Resolves the authored setting and the character's own setting into one sentence saying
        ///     what will actually happen, so nobody has to work the precedence out for themselves.
        /// </summary>
        /// <param name="delivery">What the action is set to.</param>
        /// <param name="characterMode">
        ///     The character's success feedback mode, or <c>null</c> when the character has no
        ///     <see cref="ConvaiActionFeedbackRelay" /> at all — in which case nothing is ever
        ///     reported, whatever either setting says.
        /// </param>
        /// <param name="characterName">The character's name, used so the sentence is about someone.</param>
        internal static string DescribeEffect(
            ConvaiActionAnswerDelivery delivery,
            ConvaiActionFeedbackMode? characterMode,
            string characterName)
        {
            string who = string.IsNullOrWhiteSpace(characterName) ? "This character" : characterName;

            if (characterMode == null)
                return $"Right now: this action still runs normally. Action Feedback is not set up, " +
                       $"so {who} will not remember or talk about what the action found.";

            return delivery switch
            {
                ConvaiActionAnswerDelivery.RememberOnly =>
                    $"Right now: {who} will quietly remember this, without saying it out loud.",
                ConvaiActionAnswerDelivery.MentionIfRelevant =>
                    $"Right now: {who} will decide for itself whether to bring this up.",
                ConvaiActionAnswerDelivery.TellThePlayer =>
                    characterMode == ConvaiActionFeedbackMode.ScriptedSpeech
                        ? $"Right now: {who} will say what this action found, in its own words — a " +
                          "scripted line cannot carry an answer."
                        : $"Right now: {who} will say what this action found.",
                _ => DescribeDeferredEffect(who, characterMode.Value)
            };
        }

        private static string DescribeDeferredEffect(string who, ConvaiActionFeedbackMode characterMode) =>
            characterMode switch
            {
                ConvaiActionFeedbackMode.Off =>
                    $"Right now: nothing happens — {who} does not report successful actions at all.",
                ConvaiActionFeedbackMode.SilentContext =>
                    $"Right now: {who} will quietly remember this, without saying it out loud.",
                ConvaiActionFeedbackMode.NarrateInCharacter =>
                    $"Right now: {who} will say what this action found.",
                ConvaiActionFeedbackMode.ScriptedSpeech =>
                    $"Right now: {who} will speak its scripted line, which cannot say what this " +
                    "action found. Choose \"Tell the player\" above if this action answers a question.",
                _ => $"Right now: {who} follows its Action Feedback Relay setting."
            };

        /// <summary>
        ///     The one thing worth telling the user when the action's setting and the character's do
        ///     not agree. <see cref="ConvaiActionAnswerAdvisory.Exists" /> is <c>false</c> when they
        ///     are consistent.
        /// </summary>
        /// <remarks>
        ///     Deliberately narrow, and deliberately not always a warning. An action set to "Tell the
        ///     player" under a character that reports nothing is <em>not</em> a fault — the action's
        ///     setting is the more specific one and wins, exactly as a per-action failure policy wins
        ///     over the dispatcher's. What the user needs to know is which one won, not that they
        ///     made a mistake.
        /// </remarks>
        internal static ConvaiActionAnswerAdvisory FindAdvisory(
            ConvaiActionAnswerDelivery delivery,
            ConvaiActionFeedbackMode? characterMode,
            string characterName)
        {
            string who = string.IsNullOrWhiteSpace(characterName) ? "This character" : characterName;

            if (characterMode == null)
                return new ConvaiActionAnswerAdvisory(
                    "Optional: Let the character remember what happened",
                    $"This action will still run normally. Action Feedback lets {who} remember " +
                    "whether it worked and talk about what happened. Add it only if you want that behavior.",
                    isWarning: false);

            if (delivery == ConvaiActionAnswerDelivery.UseCharacterSetting &&
                characterMode == ConvaiActionFeedbackMode.ScriptedSpeech)
                return new ConvaiActionAnswerAdvisory(
                    "A scripted line cannot carry an answer",
                    $"{who} speaks a fixed line after a successful action, and a fixed line cannot " +
                    "say what an action found. If this action answers a question, set it to " +
                    "\"Tell the player\".",
                    isWarning: true);

            if (delivery == ConvaiActionAnswerDelivery.TellThePlayer &&
                characterMode == ConvaiActionFeedbackMode.Off)
                return new ConvaiActionAnswerAdvisory(
                    "This action overrides the character's setting",
                    $"{who} is set not to report successful actions, but this action will still say " +
                    "what it found, because you asked it to here.",
                    isWarning: false);

            return default;
        }
    }

    /// <summary>
    ///     A disagreement between an action's answer delivery and its character's relay, phrased for
    ///     a user rather than for a log — shared verbatim by the Actions Editor and the Setup Report
    ///     so a project never reports a different problem depending on which one you opened.
    /// </summary>
    internal readonly struct ConvaiActionAnswerAdvisory
    {
        /// <summary>Short headline.</summary>
        public string Title { get; }

        /// <summary>What is happening and what to do about it.</summary>
        public string Message { get; }

        /// <summary>
        ///     Whether this needs acting on. <c>false</c> means the settings disagree but the outcome
        ///     is well defined — the user is being told which one won, not corrected.
        /// </summary>
        public bool IsWarning { get; }

        /// <summary>Whether there is anything to say at all.</summary>
        public bool Exists => !string.IsNullOrEmpty(Title);

        public ConvaiActionAnswerAdvisory(string title, string message, bool isWarning)
        {
            Title = title;
            Message = message;
            IsWarning = isWarning;
        }
    }

    /// <summary>
    ///     Plain-language, testable explanations of the <see cref="ConvaiActionDispatcher" /> policy
    ///     enums — shown under the matching dropdowns in <see cref="ConvaiActionDispatcherEditor" />
    ///     .
    /// </summary>
    internal static class ConvaiActionDispatcherPolicyExplanations
    {
        /// <summary>One beginner-readable sentence describing what <paramref name="policy" /> does.</summary>
        internal static string ExplainBatchPolicy(ConvaiActionBatchPolicy policy) => policy switch
        {
            ConvaiActionBatchPolicy.Queue =>
                "Finish what it's doing, then do this. New actions wait their turn.",
            ConvaiActionBatchPolicy.ReplaceCurrent =>
                "Stop what it's doing and do this instead. The current batch and anything waiting are cancelled.",
            ConvaiActionBatchPolicy.DropIncoming =>
                "Keep doing what it's doing. New actions are ignored until it's free again.",
            _ => "This option has no description yet."
        };

        /// <summary>One beginner-readable sentence describing what <paramref name="policy" /> does.</summary>
        internal static string ExplainFailurePolicy(ConvaiActionBatchFailurePolicy policy) => policy switch
        {
            ConvaiActionBatchFailurePolicy.StopBatch =>
                "The rest of the batch is skipped, so the Convai Character doesn't push through a failure.",
            ConvaiActionBatchFailurePolicy.ContinueBatch =>
                "The failure is reported and the Convai Character keeps going with the remaining actions.",
            _ => "This option has no description yet."
        };
    }
}
