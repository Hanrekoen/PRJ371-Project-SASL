using System.Collections.Generic;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>One named group of profile fields, worded for someone who has never tuned a face rig.</summary>
    internal readonly struct EmotionConfigSection
    {
        public EmotionConfigSection(
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
    ///     The complete, ordered map of <c>ConvaiEmotionProfile</c>'s serialized fields into named
    ///     sections, plus the table saying which toggle each field depends on. Every surface that
    ///     edits a profile renders this same data, so the asset inspector and the Emotion editor
    ///     window can never drift apart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The previous inspector showed thirteen sections, eleven of them expanded, titled with
    ///         the module's internal vocabulary — Response Shaping, Blending &amp; Hysteresis,
    ///         Per-Emotion Dynamics, Demeanor Presets — above field labels Unity derived straight
    ///         from identifiers like <c>lerpSpeed</c> and <c>complementBlendScale</c>. That is a
    ///         field list, not a product surface.
    ///     </para>
    ///     <para>
    ///         <see cref="EmotionConfigSectionCompletenessTests" /> asserts that every serialized
    ///         field appears in exactly one section and that every declared gate is real, so a field
    ///         added later cannot quietly become unreachable or ungated — the same deliberate review
    ///         gate the architecture guard tests use.
    ///     </para>
    /// </remarks>
    internal static class EmotionConfigSections
    {
        internal const string Personality = "Personality";
        internal const string RestingMood = "RestingMood";
        internal const string Reactions = "Reactions";
        internal const string SmallMovements = "SmallMovements";
        internal const string MixingEmotions = "MixingEmotions";
        internal const string OtherCharacters = "OtherCharacters";
        internal const string PerEmotion = "PerEmotion";
        internal const string Vocabulary = "Vocabulary";
        internal const string Expressions = "Expressions";

        /// <summary>
        ///     Fields intentionally excluded from every section: internal bookkeeping the user must
        ///     never see or edit. Empty today; kept so a future migration carrier has a documented
        ///     home instead of quietly disappearing.
        /// </summary>
        internal static readonly string[] HiddenFields = { };

        internal static readonly EmotionConfigSection[] Sections =
        {
            new(Personality, "Personality",
                "How strongly and how quickly this character shows what it feels.",
                true,
                "intensityOffset",
                "lerpSpeed",
                "decaySpeed",
                "prosodyCoupling"),

            new(RestingMood, "Resting Mood",
                "Where the face settles when nothing is happening, and whether the conversation " +
                "can shift it.",
                true,
                "baselineEmotionLabel",
                "baselineIntensity",
                "moodDriftEnabled",
                "moodDriftRate",
                "moodRecoveryRate",
                "moodDriftMaxIntensity"),

            new(Reactions, "Reactions",
                "The extra kick as a new emotion lands, so it reads as a reaction rather than a " +
                "fade-in.",
                false,
                "microBurstEnabled",
                "microBurstDuration",
                "microBurstOvershoot",
                "microBurstThreshold"),

            // The four conversation-beat reactions live here, not under Reactions, because this
            // section's toggle is what actually governs them: they are composed by the
            // micro-expression layer, and with that layer off they are inert. They used to sit in
            // Reactions, gated by the unrelated micro-burst toggle — so they greyed out when Micro
            // Burst was switched off, and stayed live-looking while doing nothing when Small
            // Movements was.
            new(SmallMovements, "Small Movements",
                "The trace of movement that keeps a resting face from reading as frozen, plus the " +
                "reactions tied to conversation beats. The beat reactions also need the " +
                "Conversation Flow module.",
                false,
                "microExpressionsEnabled",
                "microExpressionAmplitude",
                "microExpressionStillness",
                "speechAccentStrength",
                "listeningReactionStrength",
                "thinkingReactionStrength",
                "reactingAccentStrength",
                "interruptedFlinchStrength"),

            new(MixingEmotions, "Mixing Emotions",
                "Whether more than one emotion can show at once, and how readily the character " +
                "switches between them.",
                false,
                "enableEmotionBlending",
                "complementBlendScale",
                "maxSimultaneousEmotions",
                "emotionSwitchDwell",
                "emotionSwitchMargin"),

            new(OtherCharacters, "Other Characters",
                "Whether this character picks up the mood of other Convai characters nearby. " +
                "Does nothing in a scene with only one character.",
                false,
                "contagionEnabled",
                "contagionStrength",
                "contagionRadius",
                "contagionMaxIntensity"),

            new(PerEmotion, "Per-Emotion Tuning",
                "Overrides for individual emotions — \"smiles easily\", \"anger snaps on, sadness " +
                "lingers\". Anything not listed uses the settings above.",
                false,
                "expressiveness",
                "emotionDynamics"),

            new(Vocabulary, "Emotion Vocabulary",
                "Which emotions this character understands, and the words the backend may use for " +
                "them. Leave empty for the built-in set.",
                false,
                "taxonomy"),

            new(Expressions, "Expressions",
                "What each emotion does to the face. Leave empty to use Convai's built-in " +
                "expressions, which work on any supported face rig.",
                false,
                "expressionRecipes",
                "materialBinding")
        };

        /// <summary>
        ///     Field → the serialized <c>bool</c> that must be on for it to do anything. A field
        ///     absent from this table always applies.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Gating is a property of the field, not of its position in a section. The previous
        ///         model had two mechanisms — a whole-section gate and a "gates everything after it"
        ///         partial gate — and neither could express a field whose gate lives in a different
        ///         section. That is exactly the case the conversation-beat reactions are: composed by
        ///         the micro-expression layer, but read as reactions.
        ///     </para>
        ///     <para>
        ///         Every entry here must match what the runtime actually consults. A gate that is
        ///         merely plausible is worse than none: it tells the user a control is inert when it
        ///         is not, or leaves one live-looking when it is.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<string, string> Gates = new()
        {
            // The extra kick as an emotion lands. EmotionScoreAccumulator.ConfigureMicroBurst
            // ignores all three when the toggle is off.
            { "microBurstDuration", "microBurstEnabled" },
            { "microBurstOvershoot", "microBurstEnabled" },
            { "microBurstThreshold", "microBurstEnabled" },

            // Mood drift. The drift channel never advances with the toggle off; the resting mood
            // itself (label + strength) still applies, so those two are deliberately absent.
            { "moodDriftRate", "moodDriftEnabled" },
            { "moodRecoveryRate", "moodDriftEnabled" },
            { "moodDriftMaxIntensity", "moodDriftEnabled" },

            // The micro-expression layer. With it off the controller never builds the director, so
            // TickMicroExpressions returns immediately and none of these reach the face — including
            // all four conversation-beat reactions.
            { "microExpressionAmplitude", "microExpressionsEnabled" },
            { "microExpressionStillness", "microExpressionsEnabled" },
            { "speechAccentStrength", "microExpressionsEnabled" },
            { "listeningReactionStrength", "microExpressionsEnabled" },
            { "thinkingReactionStrength", "microExpressionsEnabled" },
            { "reactingAccentStrength", "microExpressionsEnabled" },
            { "interruptedFlinchStrength", "microExpressionsEnabled" },

            // Blending. All four are documented "only consulted when blending is on".
            { "complementBlendScale", "enableEmotionBlending" },
            { "maxSimultaneousEmotions", "enableEmotionBlending" },
            { "emotionSwitchDwell", "enableEmotionBlending" },
            { "emotionSwitchMargin", "enableEmotionBlending" },

            // Picking up other characters' moods.
            { "contagionStrength", "contagionEnabled" },
            { "contagionRadius", "contagionEnabled" },
            { "contagionMaxIntensity", "contagionEnabled" }
        };

        /// <summary>
        ///     The serialized <c>bool</c> field <paramref name="fieldName" /> depends on, or
        ///     <c>null</c> when it always applies.
        /// </summary>
        internal static string GateForField(string fieldName) =>
            fieldName != null && Gates.TryGetValue(fieldName, out string gate) ? gate : null;

        /// <summary>Every (field, gate) pair, for the guard test.</summary>
        internal static IEnumerable<KeyValuePair<string, string>> AllGates => Gates;

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
