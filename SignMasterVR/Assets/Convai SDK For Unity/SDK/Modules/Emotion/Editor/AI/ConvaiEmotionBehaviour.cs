using System.Collections.Generic;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;

namespace Convai.Modules.Emotion.Editor.AI
{
    /// <summary>
    ///     One thing this character does or does not do, in the label the Inspector shows for it,
    ///     and why.
    /// </summary>
    internal readonly struct ConvaiEmotionBehaviour
    {
        internal ConvaiEmotionBehaviour(string label, bool effective, string why)
        {
            Label = label;
            Effective = effective;
            Why = why;
        }

        /// <summary>The Inspector's own wording, e.g. "Never sits perfectly still".</summary>
        internal string Label { get; }

        /// <summary>Whether a user watching this character would actually see it.</summary>
        internal bool Effective { get; }

        /// <summary>Why it is off, in plain language; empty when it is on.</summary>
        internal string Why { get; }
    }

    /// <summary>
    ///     Turns a personality's settings into what a user actually observes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This type exists because a raw field value on this module is not the answer to
    ///         "is this on?", for two separate reasons:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <b>Some settings are gated by another.</b> The four conversation-beat reactions
    ///             are only consulted when small movements are on. A strength of <c>0.35</c> with
    ///             that toggle off is a stored <c>0.35</c> and an observed nothing.
    ///         </item>
    ///         <item>
    ///             <b>Some settings are gated by the scene.</b> Picking up other characters' moods
    ///             is observably off in a scene holding one character, whatever the toggle says.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         There was a third: the serialized defaults for mixing and small movements disagreed
    ///         with what every character type sets, so a hand-made personality read <c>false</c>
    ///         while the documentation promised otherwise. That one is fixed at source rather than
    ///         reported around, and a guard test holds the two creation paths together.
    ///     </para>
    ///     <para>
    ///         So nothing above this layer ever reads a field directly. Reporting the stored value
    ///         is how a diagnostic tells a user a setting is on while they watch it do nothing.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEmotionBehaviours
    {
        /// <summary>Describes every feel switch on <paramref name="report" />, effect first.</summary>
        internal static IReadOnlyList<ConvaiEmotionBehaviour> Describe(in ConvaiEmotionReport report)
        {
            var described = new List<ConvaiEmotionBehaviour>(9);
            ConvaiEmotionProfile profile = report.Profile;
            ConvaiEmotionController controller = report.Controller;

            if (profile == null)
            {
                described.Add(new ConvaiEmotionBehaviour(
                    "Personality", false,
                    "This character has no personality assigned, so it runs on the Convai defaults. " +
                    "It still expresses what it feels — the defaults drive a face — but nothing " +
                    "below is tuned for it."));
                return described;
            }

            bool smallMovements = profile.MicroExpressionsEnabled;

            described.Add(new ConvaiEmotionBehaviour(
                "Never sits perfectly still", smallMovements,
                smallMovements
                    ? string.Empty
                    : "Turned off on this personality, so the face holds perfectly still between " +
                      "expressions and reads as a mask."));

            described.Add(new ConvaiEmotionBehaviour(
                "Shows more than one emotion at once", profile.EnableEmotionBlending,
                profile.EnableEmotionBlending
                    ? string.Empty
                    : "Turned off on this personality, so exactly one emotion shows at a time and a " +
                      "new one replaces it outright."));

            described.Add(new ConvaiEmotionBehaviour(
                "Mood follows the conversation", profile.MoodDriftEnabled,
                profile.MoodDriftEnabled
                    ? string.Empty
                    : "Turned off on this personality, so its resting mood never shifts on its own."));

            // Two independent gates, and the scene one is the surprising half: the toggle can be
            // on and the behaviour still be impossible.
            bool hasOthers = EmotionPersonality.HasOtherCharacters(controller);
            described.Add(new ConvaiEmotionBehaviour(
                "Picks up other characters' moods", profile.ContagionEnabled && hasOthers,
                !profile.ContagionEnabled
                    ? "Turned off on this personality."
                    : !hasOthers
                        ? "Turned on, but the open scenes hold no other Convai character, so there " +
                          "is nobody to pick a mood up from."
                        : string.Empty));

            AddBeatReaction(described, "Listening reactions", profile.ListeningReactionStrength,
                smallMovements, "while the player is speaking");
            AddBeatReaction(described, "Thinking reactions", profile.ThinkingReactionStrength,
                smallMovements, "while the character is working out its reply");
            AddBeatReaction(described, "Reacting flash", profile.ReactingAccentStrength,
                smallMovements, "when the character reacts to something");
            AddBeatReaction(described, "Interrupted flinch", profile.InterruptedFlinchStrength,
                smallMovements, "when the character is cut off mid-sentence");

            return described;
        }

        /// <summary>
        ///     A conversation-beat reaction, which needs both a strength above zero and the
        ///     small-movement layer that composes it.
        /// </summary>
        private static void AddBeatReaction(
            List<ConvaiEmotionBehaviour> described,
            string label,
            float strength,
            bool smallMovements,
            string when)
        {
            bool authored = strength > 0f;
            described.Add(new ConvaiEmotionBehaviour(
                label, authored && smallMovements,
                !authored
                    ? $"Set to 0 on this personality, so nothing shows {when}."
                    : !smallMovements
                        ? $"Set to {strength:0.00}, but 'Never sits perfectly still' is off and that " +
                          "layer is what plays it, so it never shows."
                        : string.Empty));
        }
    }
}
