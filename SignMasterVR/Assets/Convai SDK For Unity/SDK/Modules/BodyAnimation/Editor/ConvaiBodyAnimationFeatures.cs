using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Data;

namespace Convai.Modules.BodyAnimation.Editor.AI
{
    /// <summary>
    ///     What one of the character's behaviours is actually doing. The distinction this exists to
    ///     draw is between a module that is <em>not set up</em> and one that is set up but has no
    ///     clips for a particular behaviour — invisible from a boolean, and the difference between
    ///     "press the setup button" and "author a clip".
    /// </summary>
    internal enum BodyAnimationFeatureStateKind
    {
        /// <summary>Turned on, and this character has the content it needs.</summary>
        Working,

        /// <summary>
        ///     Set up, but this character's animation set carries no clips for it, so it does
        ///     nothing. The gap is content, not configuration.
        /// </summary>
        NeedsContent,

        /// <summary>
        ///     The clips exist, but the setting that would play them is off — the authoring mistake
        ///     that is invisible from the animation set alone.
        /// </summary>
        ContentIdle,

        /// <summary>Off, with nothing authored for it. A shipped default, not a fault.</summary>
        OffByChoice,

        /// <summary>
        ///     No setting gates this; the preferred content is absent and the documented fallback
        ///     applies. A content-coverage fact, never a defect.
        /// </summary>
        FallbackTier
    }

    /// <summary>One behaviour, its state, and what to do about it.</summary>
    internal readonly struct ConvaiBodyAnimationFeature
    {
        internal ConvaiBodyAnimationFeature(
            string name,
            BodyAnimationFeatureStateKind state,
            bool enabled,
            bool hasContent,
            string settingLabel,
            string message)
        {
            Name = name;
            State = state;
            Enabled = enabled;
            HasContent = hasContent;
            SettingLabel = settingLabel;
            Message = message;
        }

        /// <summary>What the documentation calls it.</summary>
        internal string Name { get; }

        internal BodyAnimationFeatureStateKind State { get; }

        /// <summary>Whether a setting turns it on. Always true for a behaviour with no setting.</summary>
        internal bool Enabled { get; }

        /// <summary>Whether this character's animation set carries the clips it needs.</summary>
        internal bool HasContent { get; }

        /// <summary>The setting's label in the editor, or empty when no setting gates it.</summary>
        internal string SettingLabel { get; }

        /// <summary>What is happening and what to do next, in the editor's own words.</summary>
        internal string Message { get; }
    }

    /// <summary>
    ///     Projects a character's <see cref="BodyAnimationFeatureAvailability" /> and content into
    ///     per-behaviour rows an assistant can act on.
    /// </summary>
    /// <remarks>
    ///     A projection, never a second opinion: every <c>Enabled</c> and <c>HasContent</c> value
    ///     comes from the same pure computation the running character uses to decide what it can
    ///     perform. This class only chooses the wording.
    /// </remarks>
    internal static class ConvaiBodyAnimationFeatures
    {
        private const string ContentModeHint =
            "the Body Animation Editor's Content mode (Convai → Body Animation Editor)";

        private const string FeelModeHint =
            "the Body Animation Editor's Feel mode (Convai → Body Animation Editor)";

        /// <summary>
        ///     Builds one row per behaviour. Returns an empty list when the character has no
        ///     animation content — with no set there is nothing to say about individual behaviours,
        ///     and the character-level readiness state already says why.
        /// </summary>
        internal static List<ConvaiBodyAnimationFeature> Describe(in ConvaiBodyAnimationReport report)
        {
            var features = new List<ConvaiBodyAnimationFeature>(9);
            if (report.Set == null) return features;

            BodyAnimationTroubleshooterInput input = report.Input;
            BodyAnimationFeatureAvailability availability = input.FeatureAvailability;

            features.Add(Pool("Talk Gestures", input.HasAnyTalk,
                "The character plays talking gestures while it speaks.",
                "This character's animation set authors no Talk clips, so it stays in its idle pose " +
                "while speaking. Add at least one looping talk clip in " + ContentModeHint + "."));

            features.Add(Pool("Listening", input.HasAnyListen,
                "The character holds a listening pose while the player speaks.",
                "This character's animation set authors no Listen clips, so listening acting is " +
                "inactive — the layer releases to idle instead of holding a pose. Add clips in " +
                ContentModeHint + " to enable it. The animation set the SDK ships authors none, so " +
                "this is the normal state for a stock character."));

            features.Add(Pool("Thinking", input.HasAnyThink,
                "The character holds a thinking pose while it works out a reply.",
                "This character's animation set authors no Think clips, so thinking acting is " +
                "inactive — the layer releases to idle instead of holding a pose. Add clips in " +
                ContentModeHint + " to enable it. The animation set the SDK ships authors none, so " +
                "this is the normal state for a stock character."));

            features.Add(Switched(
                "Beat Gestures", availability.BeatGestures, "Enable Beat Gestures",
                working: "Short speech-rhythm accents play from the actions tagged Beat or Emphatic.",
                needsContent:
                "Enable Beat Gestures is on, but no action in this character's animation set is " +
                "tagged with the Beat or Emphatic cue, so there is nothing to play. Tag a short " +
                "additive accent clip in " + ContentModeHint + ".",
                contentIdle:
                "This character's animation set tags an action Beat or Emphatic, but Enable Beat " +
                "Gestures is off, so those clips are never played. Turn it on in " + FeelModeHint +
                " — the setup checklist offers a one-click Turn On for exactly this case.",
                offByChoice:
                "Off, and nothing is tagged Beat or Emphatic. This is the shipped default: beat " +
                "gestures play authored clips and have no procedural stand-in, so they stay off " +
                "until you tag one. Nothing is wrong."));

            features.Add(Switched(
                "Ambient Activities", availability.AmbientActivities, "Keeps busy when alone",
                working:
                "After a while alone the character performs an Ambient-tagged activity instead of " +
                "standing motionless.",
                needsContent:
                "Keeps busy when alone is on, but no action in this character's animation set is " +
                "tagged Ambient, so the character has nothing to perform. Tag an activity clip in " +
                ContentModeHint + ".",
                contentIdle:
                "This character's animation set tags an action Ambient, but Keeps busy when alone " +
                "is off, so the character never performs it. Turn it on in the component " +
                "inspector's Personality section, or in " + FeelModeHint + ".",
                offByChoice:
                "Off, and nothing is tagged Ambient. This is the shipped default: an ambient " +
                "activity is a whole authored performance with no procedural stand-in, so the " +
                "setting stays off until you tag one. Nothing is wrong."));

            features.Add(Referential(availability.ReferentialGestures));

            features.Add(Tier(
                "Walking while talking", availability.MovingTalkAdditive.HasContent,
                working:
                "Talk entries carry an Additive Clip, so gestures layer over the walk cycle instead " +
                "of freezing the arms.",
                fallback:
                "No talk entry in this character's animation set carries an Additive Clip, so " +
                "Moving Talk Mode's Auto setting uses the softened override instead — the gait's " +
                "arm swing still bleeds through. This is a complete, intended behaviour, not a " +
                "fault. Bake additive twins for your talk clips if walk-and-talk matters to your scene."));

            features.Add(Tier(
                "Gesture brackets", availability.GestureBrackets.HasContent,
                working:
                "Talk entries carry Intro or Outro clips, so hands visibly raise into and wind down " +
                "out of gesture space.",
                fallback:
                "No talk entry carries an Intro or Outro clip, so talk gestures fade in and out by " +
                "weight instead of being bracketed. A complete, intended behaviour."));

            features.Add(Tier(
                "Semantic gesture cues", availability.CueTaggedActions.HasContent,
                working:
                "Actions are tagged with conversational cues (Affirmative, Negative, Greeting, " +
                "Uncertain), so a peer module can ask for the meaning rather than for a clip name.",
                fallback:
                "No action carries an Affirmative, Negative, Greeting or Uncertain cue tag, so those " +
                "clips are reachable only by their exact name through PlayAction or a backend " +
                "action. Add a Cue tag in " + ContentModeHint + " to make them reachable by meaning."));

            features.Add(Variety(availability));

            return features;
        }

        // ------------------------------------------------------------------ row builders

        /// <summary>
        ///     A pool that has no setting behind it and no fallback: either the clips exist or the
        ///     behaviour does not happen. This is the purest form of "set up, but this character
        ///     has no clips for it".
        /// </summary>
        private static ConvaiBodyAnimationFeature Pool(
            string name, bool hasContent, string working, string needsContent) =>
            new(name,
                hasContent ? BodyAnimationFeatureStateKind.Working : BodyAnimationFeatureStateKind.NeedsContent,
                true, hasContent, string.Empty,
                hasContent ? working : needsContent);

        private static ConvaiBodyAnimationFeature Switched(
            string name,
            BodyAnimationFeatureState state,
            string settingLabel,
            string working,
            string needsContent,
            string contentIdle,
            string offByChoice)
        {
            BodyAnimationFeatureStateKind kind =
                state.IsEffective ? BodyAnimationFeatureStateKind.Working :
                state.IsEnabledWithoutContent ? BodyAnimationFeatureStateKind.NeedsContent :
                state.IsContentWithoutEnable ? BodyAnimationFeatureStateKind.ContentIdle :
                BodyAnimationFeatureStateKind.OffByChoice;

            string message = kind switch
            {
                BodyAnimationFeatureStateKind.Working => working,
                BodyAnimationFeatureStateKind.NeedsContent => needsContent,
                BodyAnimationFeatureStateKind.ContentIdle => contentIdle,
                _ => offByChoice
            };

            return new ConvaiBodyAnimationFeature(
                name, kind, state.Enabled, state.HasContent, settingLabel, message);
        }

        /// <summary>
        ///     Referential gestures are the one switched feature with a procedural stand-in, so
        ///     "on with no clips" is not inert — the cue is handed to a peer performer. Reporting it
        ///     as needing content would be false.
        /// </summary>
        private static ConvaiBodyAnimationFeature Referential(BodyAnimationFeatureState state)
        {
            const string setting = "Enable Referential Gestures";

            if (state.IsContentWithoutEnable)
            {
                return new ConvaiBodyAnimationFeature(
                    "Referential Gestures", BodyAnimationFeatureStateKind.ContentIdle,
                    state.Enabled, state.HasContent, setting,
                    "This character's animation set tags an action with a referential cue " +
                    "(palm-to-player, hand-to-chest, indicate, enumerate), but Enable Referential " +
                    "Gestures is off, so those clips are never played. Turn it on in " + FeelModeHint + ".");
            }

            if (!state.Enabled)
            {
                return new ConvaiBodyAnimationFeature(
                    "Referential Gestures", BodyAnimationFeatureStateKind.OffByChoice,
                    false, state.HasContent, setting,
                    "Off, so the character does not gesture at what it is talking about. It ships " +
                    "on; turn it back on in " + FeelModeHint + " if that was not deliberate.");
            }

            if (state.HasContent)
            {
                return new ConvaiBodyAnimationFeature(
                    "Referential Gestures", BodyAnimationFeatureStateKind.Working,
                    true, true, setting,
                    "The character gestures at what it is talking about, playing the clips tagged " +
                    "with each referential cue.");
            }

            return new ConvaiBodyAnimationFeature(
                "Referential Gestures", BodyAnimationFeatureStateKind.FallbackTier,
                true, false, setting,
                "On, and no clip is tagged with a referential cue — so the gesture is handed to " +
                "Convai Body Language, which performs it as a short procedural arm movement. This " +
                "always resolves, which is why it ships on; with no Body Language module on the " +
                "character the request is a harmless no-op. Tagging clips in " + ContentModeHint +
                " upgrades it from procedural to authored.");
        }

        /// <summary>A content tier with no setting: present, or the documented fallback applies.</summary>
        private static ConvaiBodyAnimationFeature Tier(
            string name, bool hasContent, string working, string fallback) =>
            new(name,
                hasContent ? BodyAnimationFeatureStateKind.Working : BodyAnimationFeatureStateKind.FallbackTier,
                true, hasContent, string.Empty,
                hasContent ? working : fallback);

        private static ConvaiBodyAnimationFeature Variety(BodyAnimationFeatureAvailability availability)
        {
            bool varied = availability.IdleVariantCount > 1 || availability.TalkVariantCount > 1;
            return new ConvaiBodyAnimationFeature(
                "Idle and talk variety",
                varied ? BodyAnimationFeatureStateKind.Working : BodyAnimationFeatureStateKind.FallbackTier,
                true, varied, string.Empty,
                varied
                    ? $"{availability.IdleVariantCount} idle and {availability.TalkVariantCount} talk " +
                      "variants, so the character does not repeat one loop."
                    : $"{availability.IdleVariantCount} idle and {availability.TalkVariantCount} talk " +
                      "variants — with only one of a kind the character always plays the same clip, " +
                      "and the settings built to vary it have nothing to swap to. A working setup; " +
                      "add a second looping clip in " + ContentModeHint + " to make standing still " +
                      "read as alive.");
        }
    }
}
