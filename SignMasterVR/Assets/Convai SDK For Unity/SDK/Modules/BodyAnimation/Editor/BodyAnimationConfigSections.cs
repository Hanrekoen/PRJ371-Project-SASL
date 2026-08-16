using System.Collections.Generic;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>One named group of config fields, worded for someone who has never tuned an animation system.</summary>
    internal readonly struct BodyAnimationConfigSection
    {
        public BodyAnimationConfigSection(
            string id, string title, string summary, bool expandedByDefault, params string[] fields)
        {
            Id = id;
            Title = title;
            Summary = summary;
            ExpandedByDefault = expandedByDefault;
            Fields = fields;
        }

        /// <summary>Stable id used for section-expansion persistence.</summary>
        public string Id { get; }

        /// <summary>Section header, in plain language.</summary>
        public string Title { get; }

        /// <summary>One line explaining what the section is for, shown under the header.</summary>
        public string Summary { get; }

        public bool ExpandedByDefault { get; }

        /// <summary>Serialized field names, in the order they should be drawn.</summary>
        public string[] Fields { get; }
    }

    /// <summary>
    ///     The complete, ordered map of <c>ConvaiBodyAnimationConfig</c>'s ~100 serialized fields into
    ///     ten named sections. Every surface that edits a config renders this same table, so the
    ///     asset inspector and the editor window can never drift apart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The previous inspector curated roughly a third of the fields and dumped the rest into
    ///         a raw "All Runtime Fields" property iterator. Everything that makes a character feel
    ///         like a person — the persona scalars, walk-and-talk behaviour, listening and thinking,
    ///         beat and referential gestures, ambient life, proximity, emotional gait — lived in that
    ///         dump, which is where settings go to never be found.
    ///     </para>
    ///     <para>
    ///         <c>BodyAnimationConfigSectionCompletenessTests</c> asserts that every serialized field
    ///         appears in exactly one section, so a field added later cannot quietly become
    ///         unreachable again — the same deliberate review gate the architecture guard tests use.
    ///     </para>
    /// </remarks>
    internal static class BodyAnimationConfigSections
    {
        internal const string Personality = "Personality";
        internal const string Talking = "Talking";
        internal const string TalkingWhileWalking = "TalkingWhileWalking";
        internal const string ListeningThinking = "ListeningThinking";
        internal const string Reacting = "Reacting";
        internal const string Presence = "Presence";
        internal const string Walking = "Walking";
        internal const string Transitions = "Transitions";
        internal const string AdvancedCoSpeech = "AdvancedCoSpeech";
        internal const string Integration = "Integration";
        internal const string Diagnostics = "Diagnostics";

        /// <summary>
        ///     Fields intentionally excluded from every section: internal bookkeeping the user must
        ///     never see or edit.
        /// </summary>
        internal static readonly string[] HiddenFields =
        {
            "_schemaVersion",
            // V4→V5 migration carrier only: OnAfterDeserialize folds this into
            // _movingTalkMode and clears it on load, so there is never a meaningful value left
            // to show or edit. Kept serialized for one release past 4.4 to migrate any
            // not-yet-resaved V4 asset.
        };

        internal static readonly BodyAnimationConfigSection[] Sections =
        {
            new(Personality, "Personality",
                "How this character carries itself. One animation library, many different people.",
                true,
                "_gestureLiveliness",
                "_calmness"),

            new(Talking, "Talking",
                "How the character gestures while it speaks.",
                true,
                "_talkFadeInSeconds",
                "_talkFadeOutSeconds",
                "_talkReleaseDelaySeconds",
                "_talkReleasePlaybackSpeed",
                "_useSpeechEnergy",
                "_talkWeightAtLowEnergy",
                "_talkOverlayWeight",
                "_switchTalkVariantOnLoop",
                "_talkVariantCrossfadeSeconds",
                "_talkOutroMaxSeconds"),

            new(TalkingWhileWalking, "Talking While Walking",
                "Talk gestures are authored standing still. This decides what happens when the " +
                "character gestures and walks at the same time.",
                false,
                "_movingTalkMode",
                "_movingTalkWeight",
                "_movingTalkOverrideWeight",
                "_movingTalkBlendSeconds"),

            new(ListeningThinking, "Listening & Thinking",
                "Poses held while the player is speaking, and during the pause before a reply. " +
                "Needs Listen/Think clips in the animation set.",
                false,
                "_listenFadeInSeconds",
                "_thinkingEnterDelaySeconds"),

            new(Reacting, "Reacting",
                "Being interrupted, accenting speech rhythm, and gesturing at what it says. " +
                "The gesture features need tagged clips in the animation set.",
                false,
                "_interruptedFreezeSeconds",
                "_interruptedReleaseScale",
                "_enableBeatGestures",
                "_beatRefractorySeconds",
                "_beatWeightScale",
                "_beatLayerWeight",
                "_enableReferentialGestures",
                "_referentialGestureRefractorySeconds",
                "_referentialGestureClassCooldownSeconds",
                "_referentialGestureWeight"),

            new(Presence, "Presence",
                "How the character behaves around the player when nothing else is happening.",
                false,
                "_proximityExpressiveness",
                "_proximityNearDistance",
                "_proximityNearScale",
                "_proximityFarDistance",
                "_proximityFarScale",
                "_proximitySmoothingSeconds",
                "_enableAmbientActivities",
                "_ambientStartDelaySeconds",
                "_ambientIntervalSeconds",
                "_ambientSuppressDistance",
                "_enableSocialSpacing",
                "_comfortRadius",
                "_comfortHoldSeconds",
                "_maxRepositionsPerMinute",
                "_enablePointGlance",
                "_pointGlanceSeconds",
                "_pointingFadeSeconds",
                "_pointingReaimCrossfadeSeconds",
                "_pointingLayerWeight"),

            new(Walking, "Walking & Running",
                "Travel speeds, when the character turns in place, and which arrival performances " +
                "it is allowed to play.",
                false,
                "_walkSpeed",
                "_jogSpeed",
                "_speedDampingSeconds",
                "_turnInPlaceMinAngle",
                "_turn180MinAngle",
                "_rateWarpMin",
                "_rateWarpMax",
                "_motionHandoffNormalizedTime",
                "_lowSpeedStopFraction",
                "_plantedStopMinTravel",
                "_enableTurnInPlace",
                "_enableDirectionalStarts",
                "_enablePlantedStops",
                "_plantedStopsWhileWalking",
                "_enableSpeedChangeClips",
                "_enableSpeedWarping",
                "_enableFootIK",
                "_enableEmotionalGait",
                "_emotionGaitRange"),

            new(Transitions, "Transitions & Idle",
                "How long blends take, and how often the character shifts its idle pose.",
                false,
                "_idleCrossfadeSeconds",
                "_idleVariantIntervalMin",
                "_idleVariantIntervalMax",
                "_actionFadeInSeconds",
                "_actionFadeOutSeconds",
                "_actionChainCrossfadeSeconds",
                "_actionLayerWeight",
                "_locomotionCrossfadeSeconds",
                "_blendCurve"),

            new(AdvancedCoSpeech, "Advanced Co-Speech",
                "Procedural speech-timed accents. Off by default; the values below only apply " +
                "while it is on.",
                false,
                "_enableAdvancedCoSpeech",
                "_coSpeechMinimumAccentEnergy",
                "_coSpeechEmphasisDerivative",
                "_coSpeechAccentProbability",
                "_coSpeechAccentRefractorySeconds",
                "_coSpeechPhraseEnergyMargin",
                "_coSpeechPreparationSeconds",
                "_coSpeechStrokeSeconds",
                "_coSpeechReferentialHoldSeconds",
                "_coSpeechRetractionSeconds"),

            new(Integration, "Integration",
                "Signals this module publishes for other Convai systems to consume.",
                false,
                "_publishExertion",
                "_exertionRiseSeconds",
                "_exertionRecoverySeconds"),

            new(Diagnostics, "Diagnostics",
                "How much this character reports to the console while it runs.",
                false,
                "_traceVerbosity",
                "_firehoseIntervalSeconds")
        };

        /// <summary>The field that gates a section, or <c>null</c> when the whole section always applies.</summary>
        internal static string GateFieldFor(string sectionId) => sectionId switch
        {
            AdvancedCoSpeech => "_enableAdvancedCoSpeech",
            _ => null
        };

        /// <summary>Every field name the section table covers, for the completeness guard.</summary>
        internal static void CollectMappedFields(HashSet<string> destination)
        {
            if (destination == null) return;
            destination.Clear();

            for (int i = 0; i < Sections.Length; i++)
            {
                string[] fields = Sections[i].Fields;
                for (int f = 0; f < fields.Length; f++) destination.Add(fields[f]);
            }

            for (int i = 0; i < HiddenFields.Length; i++) destination.Add(HiddenFields[i]);
        }

    }
}
