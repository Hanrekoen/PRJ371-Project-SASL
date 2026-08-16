using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Modules.Emotion.Profiles
{
    /// <summary>One authored per-emotion strength entry in a character type's table.</summary>
    internal readonly struct PersonalityExpressiveness
    {
        internal readonly string Label;
        internal readonly float Gain;

        internal PersonalityExpressiveness(string label, float gain)
        {
            Label = label;
            Gain = gain;
        }
    }

    /// <summary>One authored per-emotion attack/decay entry in a character type's table.</summary>
    internal readonly struct PersonalityDynamics
    {
        internal readonly string Label;
        internal readonly float AttackSpeed;
        internal readonly float DecaySpeed;

        internal PersonalityDynamics(string label, float attackSpeed, float decaySpeed)
        {
            Label = label;
            AttackSpeed = attackSpeed;
            DecaySpeed = decaySpeed;
        }
    }

    /// <summary>
    ///     Every profile field a character type owns, as immutable data.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This type exists because "what a character type is" had three definitions that had
    ///         already drifted apart: the <c>Create*Preset</c> factories, the editor's
    ///         apply-a-character-type writer, and the editor's does-this-profile-match-a-type
    ///         comparison. The writer never wrote the response speeds the factories set, so applying
    ///         <em>Reserved</em> to a warm profile left it reacting at the warm speed; and the four
    ///         shipped personality assets had been hand-tuned away from the factories, so three of
    ///         them matched no type at all and the Inspector's character-type picker showed nothing
    ///         selected on Convai's own sample characters.
    ///     </para>
    ///     <para>
    ///         There is now one table. The factories build from it, the editor writes from it, and
    ///         the editor compares against it, so "what a type sets", "what gets written" and "what
    ///         counts as a match" are the same list by construction. A guard test asserts each
    ///         shipped asset still equals its type.
    ///     </para>
    ///     <para>
    ///         Deliberately <em>not</em> owned by a character type, and never written when one is
    ///         applied: the emotion vocabulary, the expression recipes and the material output —
    ///         those are authored content, and a temperament change must not discard them — and the
    ///         overall strength trim, which is the author's own global adjustment rather than part
    ///         of the character.
    ///     </para>
    ///     <para>
    ///         Readonly fields set through a defaulted constructor rather than an object
    ///         initializer: Unity's C# profile has no <c>init</c> accessor, and settable properties
    ///         would make a table that must never change after construction look mutable. Call sites
    ///         use named arguments, which read the same and cannot silently mis-order.
    ///     </para>
    /// </remarks>
    internal readonly struct EmotionPersonalityValues
    {
        // How strongly and how quickly the character shows what it feels
        internal readonly float LerpSpeed;
        internal readonly float DecaySpeed;
        internal readonly float ProsodyCoupling;

        // The extra kick as a new emotion lands
        internal readonly bool MicroBurstEnabled;
        internal readonly float MicroBurstDuration;
        internal readonly float MicroBurstOvershoot;
        internal readonly float MicroBurstThreshold;

        // Where the face settles, and whether the conversation can shift it
        internal readonly string BaselineEmotionLabel;
        internal readonly float BaselineIntensity;
        internal readonly bool MoodDriftEnabled;
        internal readonly float MoodDriftRate;
        internal readonly float MoodRecoveryRate;
        internal readonly float MoodDriftMaxIntensity;

        // Picking up other characters' moods
        internal readonly bool ContagionEnabled;
        internal readonly float ContagionStrength;
        internal readonly float ContagionRadius;
        internal readonly float ContagionMaxIntensity;

        // Showing more than one emotion at once
        internal readonly bool EnableEmotionBlending;
        internal readonly float EmotionSwitchDwell;
        internal readonly float EmotionSwitchMargin;
        internal readonly float ComplementBlendScale;
        internal readonly int MaxSimultaneousEmotions;

        // Small movements, and the reactions tied to conversation beats
        internal readonly bool MicroExpressionsEnabled;
        internal readonly float MicroExpressionAmplitude;
        internal readonly float SpeechAccentStrength;
        internal readonly float MicroExpressionStillness;
        internal readonly float ListeningReactionStrength;
        internal readonly float ThinkingReactionStrength;
        internal readonly float ReactingAccentStrength;
        internal readonly float InterruptedFlinchStrength;

        // Per-emotion overrides. Never null — an absent table is an empty one.
        internal readonly PersonalityExpressiveness[] Expressiveness;
        internal readonly PersonalityDynamics[] Dynamics;

        internal EmotionPersonalityValues(
            float lerpSpeed,
            float decaySpeed,
            float prosodyCoupling,
            float microBurstDuration,
            float microBurstOvershoot,
            float microBurstThreshold,
            string baselineEmotionLabel,
            float baselineIntensity,
            bool moodDriftEnabled,
            float moodDriftRate,
            float moodRecoveryRate,
            float moodDriftMaxIntensity,
            bool contagionEnabled,
            float contagionStrength,
            float contagionRadius,
            float contagionMaxIntensity,
            float emotionSwitchDwell,
            float emotionSwitchMargin,
            float complementBlendScale,
            float microExpressionAmplitude,
            float speechAccentStrength,
            float microExpressionStillness,
            float listeningReactionStrength,
            float thinkingReactionStrength,
            float reactingAccentStrength,
            float interruptedFlinchStrength,
            PersonalityExpressiveness[] expressiveness = null,
            PersonalityDynamics[] dynamics = null,
            bool microBurstEnabled = true,
            bool enableEmotionBlending = true,
            bool microExpressionsEnabled = true,
            int maxSimultaneousEmotions = 2)
        {
            LerpSpeed = lerpSpeed;
            DecaySpeed = decaySpeed;
            ProsodyCoupling = prosodyCoupling;

            MicroBurstEnabled = microBurstEnabled;
            MicroBurstDuration = microBurstDuration;
            MicroBurstOvershoot = microBurstOvershoot;
            MicroBurstThreshold = microBurstThreshold;

            BaselineEmotionLabel = baselineEmotionLabel ?? string.Empty;
            BaselineIntensity = baselineIntensity;
            MoodDriftEnabled = moodDriftEnabled;
            MoodDriftRate = moodDriftRate;
            MoodRecoveryRate = moodRecoveryRate;
            MoodDriftMaxIntensity = moodDriftMaxIntensity;

            ContagionEnabled = contagionEnabled;
            ContagionStrength = contagionStrength;
            ContagionRadius = contagionRadius;
            ContagionMaxIntensity = contagionMaxIntensity;

            EnableEmotionBlending = enableEmotionBlending;
            EmotionSwitchDwell = emotionSwitchDwell;
            EmotionSwitchMargin = emotionSwitchMargin;
            ComplementBlendScale = complementBlendScale;
            MaxSimultaneousEmotions = maxSimultaneousEmotions;

            MicroExpressionsEnabled = microExpressionsEnabled;
            MicroExpressionAmplitude = microExpressionAmplitude;
            SpeechAccentStrength = speechAccentStrength;
            MicroExpressionStillness = microExpressionStillness;
            ListeningReactionStrength = listeningReactionStrength;
            ThinkingReactionStrength = thinkingReactionStrength;
            ReactingAccentStrength = reactingAccentStrength;
            InterruptedFlinchStrength = interruptedFlinchStrength;

            Expressiveness = expressiveness ?? Array.Empty<PersonalityExpressiveness>();
            Dynamics = dynamics ?? Array.Empty<PersonalityDynamics>();
        }
    }

    /// <summary>The four character-type tables, and the lookup every surface reads them through.</summary>
    /// <remarks>
    ///     These are the values a customer meets: the four shipped sample personalities are these
    ///     tables saved to disk, and pressing a character type in the Inspector writes these numbers.
    ///     Changing one changes both, which is the point.
    /// </remarks>
    internal static class EmotionPersonalityTable
    {
        /// <summary>
        ///     The strength to seed a resting mood at when a user picks one and has not chosen a
        ///     strength yet — the inspector dropdowns, the troubleshooter's fixes and the MCP tools
        ///     all start here.
        /// </summary>
        /// <remarks>
        ///     Every one of those surfaces used to seed 0.22 (or 0.25), which drives the smile
        ///     shapes to roughly fifteen units out of a hundred: a user picked "Joy", looked at the
        ///     character, saw no change, and concluded the setting did nothing. Seeding at a value
        ///     that visibly lands is what makes picking a resting mood a one-click operation
        ///     instead of the first step of a slider hunt.
        /// </remarks>
        internal const float DefaultRestingMoodIntensity = 0.45f;

        /// <summary>
        ///     Calm and even — a receptionist, clerk or guide, and the values a character falls back
        ///     to when no personality asset is assigned at all. Alive rather than inert: small
        ///     movements, mixing and the beat reactions are all on, just quiet. Rests on a faint
        ///     <c>trust</c> rather than on nothing: that recipe is a closed-lip pleasantness, which
        ///     is what "civil" looks like on a face. An absolute zero rest reads as vacant, and a
        ///     receptionist who reads as vacant is the wrong default for the whole SDK.
        /// </summary>
        /// <remarks>
        ///     0.45 on <c>trust</c> is deliberately not the same face as 0.45 on <c>joy</c>: equal
        ///     intensities are not equal expressions, because each emotion's recipe carries its own
        ///     channel weights. Trust drives the smile shapes at 31/33 against joy's 68/71, so this
        ///     lands at roughly fourteen units of smile where <see cref="Warm" /> lands at
        ///     thirty-eight — present, unmistakably quieter. Reading the number alone and calling
        ///     the two types equally warm is the mistake this remark exists to prevent.
        /// </remarks>
        internal static readonly EmotionPersonalityValues Composed = new(
            lerpSpeed: 4.5f,
            decaySpeed: 1.8f,
            prosodyCoupling: 0.15f,
            microBurstDuration: 0.28f,
            microBurstOvershoot: 1.25f,
            microBurstThreshold: 0.18f,
            baselineEmotionLabel: "trust",
            baselineIntensity: 0.45f,
            moodDriftEnabled: false,
            moodDriftRate: 0.02f,
            moodRecoveryRate: 0.05f,
            moodDriftMaxIntensity: 0.25f,
            contagionEnabled: false,
            contagionStrength: 0.3f,
            contagionRadius: 4f,
            contagionMaxIntensity: 0.2f,
            emotionSwitchDwell: 0.5f,
            emotionSwitchMargin: 0.15f,
            complementBlendScale: 0.25f,
            microExpressionAmplitude: 0.12f,
            speechAccentStrength: 0.22f,
            microExpressionStillness: 0.55f,
            listeningReactionStrength: 0.18f,
            thinkingReactionStrength: 0.15f,
            reactingAccentStrength: 0.18f,
            interruptedFlinchStrength: 0.2f);

        /// <summary>
        ///     Approachable and easy to read: a visible resting <c>joy</c>, smiles and trusts
        ///     readily, frowns and angers less so, a joy that arrives quickly and a sadness that
        ///     lets go gently.
        /// </summary>
        /// <remarks>
        ///     The resting joy is 0.55 rather than the 0.22 this shipped as. Baseline intensity
        ///     drives the smile shapes directly — <c>MouthSmile*</c> at full weight 68/71 through a
        ///     linear curve — so 0.22 put roughly fifteen units of smile on a hundred-unit
        ///     blendshape, which no one could see. "The default character type" has to actually
        ///     read as warm on the first click, not after an author goes hunting for the slider
        ///     that does it. Expressiveness gain does not help here: it is applied to incoming
        ///     detection events only, never to the resting fold.
        /// </remarks>
        internal static readonly EmotionPersonalityValues Warm = new(
            lerpSpeed: 5.5f,
            decaySpeed: 1.5f,
            prosodyCoupling: 0.3f,
            microBurstDuration: 0.28f,
            microBurstOvershoot: 1.3f,
            microBurstThreshold: 0.16f,
            baselineEmotionLabel: "joy",
            baselineIntensity: 0.55f,
            moodDriftEnabled: true,
            moodDriftRate: 0.08f,
            moodRecoveryRate: 0.12f,
            moodDriftMaxIntensity: 0.22f,
            contagionEnabled: true,
            contagionStrength: 0.3f,
            contagionRadius: 4f,
            contagionMaxIntensity: 0.18f,
            emotionSwitchDwell: 0.35f,
            emotionSwitchMargin: 0.15f,
            complementBlendScale: 0.4f,
            microExpressionAmplitude: 0.18f,
            speechAccentStrength: 0.35f,
            microExpressionStillness: 0.4f,
            listeningReactionStrength: 0.35f,
            thinkingReactionStrength: 0.3f,
            reactingAccentStrength: 0.3f,
            interruptedFlinchStrength: 0.3f,
            expressiveness: new[]
            {
                new PersonalityExpressiveness("joy", 1.45f),
                new PersonalityExpressiveness("trust", 1.3f),
                new PersonalityExpressiveness("sadness", 0.8f),
                new PersonalityExpressiveness("anger", 0.75f)
            },
            dynamics: new[]
            {
                new PersonalityDynamics("joy", 6f, 1.5f),
                new PersonalityDynamics("sadness", 5.5f, 1.2f)
            });

        /// <summary>
        ///     Big, fast reactions: everything amplified, a quick attack, a pronounced small-movement
        ///     layer, and anger and surprise that snap on. The most openly cheerful of the four at
        ///     rest, which is the whole point of a host or a tour guide.
        /// </summary>
        /// <remarks>
        ///     The resting joy is 0.6 rather than the 0.12 this shipped as. At 0.12 the loudest
        ///     character type had the flattest resting face in the SDK — flatter than
        ///     <see cref="Warm" /> — because its 1.6 joy gain only ever reached transients. It read
        ///     as energetic while speaking and blank the moment it stopped.
        /// </remarks>
        internal static readonly EmotionPersonalityValues Energetic = new(
            lerpSpeed: 6f,
            decaySpeed: 1.8f,
            prosodyCoupling: 0.45f,
            microBurstDuration: 0.28f,
            microBurstOvershoot: 1.35f,
            microBurstThreshold: 0.18f,
            baselineEmotionLabel: "joy",
            baselineIntensity: 0.6f,
            moodDriftEnabled: true,
            moodDriftRate: 0.1f,
            moodRecoveryRate: 0.14f,
            moodDriftMaxIntensity: 0.28f,
            contagionEnabled: true,
            contagionStrength: 0.45f,
            contagionRadius: 4f,
            contagionMaxIntensity: 0.2f,
            emotionSwitchDwell: 0.3f,
            emotionSwitchMargin: 0.12f,
            complementBlendScale: 0.4f,
            microExpressionAmplitude: 0.28f,
            speechAccentStrength: 0.5f,
            microExpressionStillness: 0.3f,
            listeningReactionStrength: 0.5f,
            thinkingReactionStrength: 0.45f,
            reactingAccentStrength: 0.5f,
            interruptedFlinchStrength: 0.5f,
            expressiveness: new[]
            {
                new PersonalityExpressiveness("joy", 1.6f),
                new PersonalityExpressiveness("surprise", 1.6f),
                new PersonalityExpressiveness("anticipation", 1.45f),
                new PersonalityExpressiveness("trust", 1.4f),
                new PersonalityExpressiveness("sadness", 1.25f),
                new PersonalityExpressiveness("anger", 1.25f),
                new PersonalityExpressiveness("fear", 1.2f),
                new PersonalityExpressiveness("disgust", 1.2f)
            },
            dynamics: new[]
            {
                new PersonalityDynamics("anger", 8f, 1.8f),
                new PersonalityDynamics("surprise", 8f, 1.8f)
            });

        /// <summary>
        ///     Barely shows anything, without reading as frozen: every emotion uniformly damped, slow
        ///     to arrive and slow to let go, long to switch, and a small-movement layer that is
        ///     present but only just.
        /// </summary>
        /// <remarks>
        ///     The only character type that rests at nothing, and deliberately so — a guard or an
        ///     officiant giving you a resting face is the one thing this type is chosen to avoid.
        ///     It is the bottom of the resting-warmth ladder the four types form: none, a faint
        ///     civility, a clear warmth, an open cheerfulness.
        /// </remarks>
        internal static readonly EmotionPersonalityValues Reserved = new(
            lerpSpeed: 3.5f,
            decaySpeed: 1.2f,
            prosodyCoupling: 0.08f,
            microBurstDuration: 0.32f,
            microBurstOvershoot: 1.15f,
            microBurstThreshold: 0.22f,
            baselineEmotionLabel: "",
            baselineIntensity: 0f,
            moodDriftEnabled: true,
            moodDriftRate: 0.05f,
            moodRecoveryRate: 0.08f,
            moodDriftMaxIntensity: 0.14f,
            contagionEnabled: false,
            contagionStrength: 0.3f,
            contagionRadius: 4f,
            contagionMaxIntensity: 0.2f,
            emotionSwitchDwell: 0.7f,
            emotionSwitchMargin: 0.18f,
            complementBlendScale: 0.22f,
            microExpressionAmplitude: 0.06f,
            speechAccentStrength: 0.12f,
            microExpressionStillness: 0.8f,
            listeningReactionStrength: 0.12f,
            thinkingReactionStrength: 0.22f,
            reactingAccentStrength: 0.12f,
            interruptedFlinchStrength: 0.16f,
            expressiveness: new[]
            {
                new PersonalityExpressiveness("joy", 0.7f),
                new PersonalityExpressiveness("trust", 0.7f),
                new PersonalityExpressiveness("sadness", 0.7f),
                new PersonalityExpressiveness("anger", 0.7f),
                new PersonalityExpressiveness("fear", 0.7f),
                new PersonalityExpressiveness("surprise", 0.7f),
                new PersonalityExpressiveness("disgust", 0.7f),
                new PersonalityExpressiveness("anticipation", 0.7f)
            });

        /// <summary>
        ///     Every character type, in presentation order — deferred to
        ///     <see cref="CharacterDemeanors.Order" /> so Emotion, Body Animation and Body Language
        ///     cannot present the same four temperaments in three different orders.
        /// </summary>
        internal static IReadOnlyList<CharacterDemeanor> Order => CharacterDemeanors.Order;

        /// <summary>The values for a character type. An unrecognised value resolves to <see cref="Warm" />.</summary>
        internal static EmotionPersonalityValues For(CharacterDemeanor type) => type switch
        {
            CharacterDemeanor.Composed => Composed,
            CharacterDemeanor.Energetic => Energetic,
            CharacterDemeanor.Reserved => Reserved,
            _ => Warm
        };
    }
}
