using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Emotion.Outputs;
using Convai.Modules.Emotion.Taxonomy;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Emotion.Profiles
{
    /// <summary>
    ///     A single per-emotion expressiveness (score gain) authoring entry.
    /// </summary>
    /// <remarks>
    ///     Lets an author say "this character smiles easily" (joy gain &gt; 1) or "rarely frowns"
    ///     (anger gain &lt; 1) per canonical taxonomy label. Applied to incoming transient scores
    ///     at event time. Labels absent from the list default to gain <c>1</c> (unchanged).
        /// </remarks>
    [System.Serializable]
    public sealed class EmotionExpressivenessEntry
    {
        [SerializeField, ConvaiEmotionLabel]
        [Tooltip("The emotion this applies to.")]
        private string label = string.Empty;

        [SerializeField, Range(0f, 2f), Tooltip("How much more, or less, readily this character shows that emotion. 1 leaves it alone.")]
        private float gain = 1f;

        public string Label => label;
        public float Gain => gain;

        public EmotionExpressivenessEntry()
        {
        }

        public EmotionExpressivenessEntry(string label, float gain)
        {
            this.label = label;
            this.gain = gain;
        }

        internal void ClampInPlace()
        {
            gain = Mathf.Clamp(gain, 0f, 2f);
        }
    }

    /// <summary>
    ///     A single per-emotion attack/decay smoothing-speed override.
    /// </summary>
    /// <remarks>
    ///     Lets an author say "anger snaps on, sadness creeps on and lingers" instead of every
    ///     emotion sharing one global attack/decay speed. Labels absent from the owning profile's
    ///     <c>emotionDynamics</c> list use the profile's global <see cref="ConvaiEmotionProfile.LerpSpeed" />/
    ///     <see cref="ConvaiEmotionProfile.DecaySpeed" /> unchanged.
    /// </remarks>
    [System.Serializable]
    public sealed class EmotionDynamicsEntry
    {
        [SerializeField, ConvaiEmotionLabel]
        [Tooltip("The emotion this timing applies to.")]
        private string label = string.Empty;

        [SerializeField, Range(0.1f, 20f), Tooltip("How fast this emotion arrives on the face. Higher snaps on; lower creeps in.")]
        private float attackSpeed = 5f;

        [SerializeField, Range(0.1f, 20f), Tooltip("How fast this emotion fades once it passes. Higher lets go sooner.")]
        private float decaySpeed = 2f;

        public string Label => label;
        public float AttackSpeed => attackSpeed;
        public float DecaySpeed => decaySpeed;

        public EmotionDynamicsEntry()
        {
        }

        public EmotionDynamicsEntry(string label, float attackSpeed, float decaySpeed)
        {
            this.label = label;
            this.attackSpeed = attackSpeed;
            this.decaySpeed = decaySpeed;
        }

        internal void ClampInPlace()
        {
            if (float.IsNaN(attackSpeed)) attackSpeed = 5f;
            if (float.IsNaN(decaySpeed)) decaySpeed = 2f;
            attackSpeed = Mathf.Clamp(attackSpeed, 0.1f, 20f);
            decaySpeed = Mathf.Clamp(decaySpeed, 0.1f, 20f);
        }
    }

    /// <summary>
    ///     Authoring asset bundling the emotion vocabulary, response shaping, resting mood,
    ///     micro-expression life, and the rig-independent expression recipes that drive the face.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The profile is the single authored surface for emotion behavior, and it is
    ///         rig-independent: recipes name what should move in semantic terms
    ///         (<c>MouthSmileLeft</c>, <c>BrowOuterUpRight</c>, …) and the runtime resolves those
    ///         to whichever blendshapes the character's own mesh actually has. One profile
    ///         therefore works across every supported rig convention with no per-character
    ///         authoring.
    ///     </para>
    ///     <para>
    ///         An empty recipe list uses Convai's production-safe default library, so a profile
    ///         always drives a face. <see cref="MaterialBinding" /> is an optional extra output for
    ///         shader effects (blush, tears, sweat) and stays empty unless authored.
    ///     </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ConvaiEmotionProfile",
        menuName = "Convai/Embodiment/Emotion Profile",
        order = 130)]
    public sealed class ConvaiEmotionProfile : ScriptableObject
    {
        [SerializeField, Tooltip("Which emotions this character understands. Leave empty for the built-in set.")]
        private EmotionTaxonomyAsset taxonomy;
        [SerializeField, Range(0.1f, 20f)]
        [Tooltip("How fast an emotion appears. Higher reads as quick and reactive.")]
        private float lerpSpeed = 5f;

        [SerializeField, Range(0.1f, 20f)]
        [Tooltip("How fast an emotion fades once it passes. Higher lets go sooner.")]
        private float decaySpeed = 2f;

        [SerializeField, Range(-0.25f, 0.25f)]
        [Tooltip("Nudges every expression up or down. Leave at 0 unless the character reads as too flat or too much.")]
        private float intensityOffset;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("While speaking, the expression brightens on emphatic delivery and softens in lulls. " +
                 "0 turns this off.")]
        private float prosodyCoupling;
        [SerializeField]
        [Tooltip("The expression briefly pushes past where it will settle as a new emotion lands, so it " +
                 "reads as a reaction rather than a fade-in.")]
        private bool microBurstEnabled = true;

        [SerializeField, Range(0.05f, 1.5f)]
        [Tooltip("How long that initial push lasts before it settles, in seconds.")]
        private float microBurstDuration = 0.25f;

        [SerializeField, Range(1f, 3f)]
        [Tooltip("How far past the settled value the expression pushes.")]
        private float microBurstOvershoot = 1.4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Small changes settle in quietly; only a change this large gets the extra kick.")]
        private float microBurstThreshold = 0.15f;
        [SerializeField, ConvaiEmotionLabel("None — a plain neutral rest")]
        [Tooltip("What the face settles to when nothing is happening.")]
        private string baselineEmotionLabel = string.Empty;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How strongly the resting mood shows. 0 means no resting mood at all.")]
        private float baselineIntensity;

        [SerializeField]
        [Tooltip("Make one emotion easier or harder for this character to show. Anything not listed " +
                 "uses the settings above.")]
        private List<EmotionExpressivenessEntry> expressiveness = new();

        [SerializeField]
        [Tooltip("Override how fast a single emotion appears and fades. Anything not listed uses the " +
                 "settings above.")]
        private List<EmotionDynamicsEntry> emotionDynamics = new();
        [SerializeField]
        [Tooltip("A long stretch of one feeling leaves the character in that mood for a while " +
                 "afterwards, instead of snapping straight back.")]
        private bool moodDriftEnabled;

        [SerializeField, Range(0.001f, 0.5f)]
        [Tooltip("How fast a sustained feeling starts colouring the resting mood.")]
        private float moodDriftRate = 0.02f;

        [SerializeField, Range(0.001f, 1f)]
        [Tooltip("Lower values keep the shifted mood around longer once the feeling passes.")]
        private float moodRecoveryRate = 0.05f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A ceiling, so a long difficult conversation can never take the character over completely.")]
        private float moodDriftMaxIntensity = 0.25f;
        [SerializeField]
        [Tooltip("Catches a faint echo of a strong feeling from another Convai character nearby.")]
        private bool contagionEnabled;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How much of the other character's feeling carries over.")]
        private float contagionStrength = 0.3f;

        [SerializeField, Range(0.5f, 20f)]
        [Tooltip("In metres. The effect fades with distance.")]
        private float contagionRadius = 4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A ceiling, so a caught mood stays an echo rather than taking over.")]
        private float contagionMaxIntensity = 0.2f;
        [SerializeField]
        [Tooltip("Real faces rarely show one pure feeling. With this on, related emotions come along " +
                 "at reduced strength — anger with a trace of disgust, fear with a trace of surprise.")]
        // On, so that a personality created straight from the Create menu behaves the same as one
        // the setup button makes. The two used to disagree: the field defaulted off while every
        // character type turns it on, so a hand-made personality was quietly the flattest one in
        // the project and nothing said why.
        private bool enableEmotionBlending = true;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Stops the face flickering when the character's feelings change rapidly.")]
        private float emotionSwitchDwell = 0.35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A clearly stronger new feeling cuts in straight away instead of waiting.")]
        private float emotionSwitchMargin = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How strongly the related emotion comes along, relative to the main one.")]
        private float complementBlendScale = 0.35f;

        [SerializeField, Range(1, 4)]
        [Tooltip("Including the main one.")]
        private int maxSimultaneousEmotions = 2;
        [SerializeField]
        [Tooltip("Keeps a trace of movement in the face so a resting character does not read as a frozen mask.")]
        // On, for the same reason as blending above: every character type turns it on, so leaving
        // the field off made a hand-made personality the only one whose face sits perfectly still.
        private bool microExpressionsEnabled = true;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How large the idle movement is.")]
        private float microExpressionAmplitude = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A small brow lift on emphasis while the character talks.")]
        private float speechAccentStrength = 0.3f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Higher damps the idle movement toward stillness without turning it off.")]
        private float microExpressionStillness = 0.5f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A sustained attentive look while the player is speaking. 0 turns it off. Needs " +
                 "Small Movements on and the Conversation Flow module.")]
        private float listeningReactionStrength;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A concentration look during the pause before the character replies. 0 turns it " +
                 "off. Needs Small Movements on and the Conversation Flow module.")]
        private float thinkingReactionStrength;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A one-off flash of expression when something notable happens. 0 turns it off. " +
                 "Needs Small Movements on and the Conversation Flow module.")]
        private float reactingAccentStrength;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("A one-off flinch when the character is cut off mid-sentence. 0 turns it off. " +
                 "Needs Small Movements on and the Conversation Flow module.")]
        private float interruptedFlinchStrength;
        [SerializeField, Tooltip("What each emotion does to the face, described by what should move rather " +
        "than by blendshape names. Leave empty to use Convai's built-in expressions.")]
        private List<EmotionExpressionRecipe> expressionRecipes = new();

        [SerializeField, Tooltip("Optional shader effects driven by emotion, such as blush, tears or sweat.")]
        private MaterialPropertyEmotionBinding materialBinding = new();

        public EmotionTaxonomyAsset Taxonomy => taxonomy;
        public float LerpSpeed => lerpSpeed;
        public float DecaySpeed => decaySpeed;
        public float IntensityOffset => intensityOffset;

        /// <summary>
        ///     Strength of the prosody-coupled expression effect in <c>[0, 1]</c>: how much
        ///     expression intensity subtly follows the live speech-energy envelope while the
        ///     character speaks (brighter on emphatic delivery, softer in lulls). At full
        ///     coupling the effective intensity gain ranges <c>[0.85, 1.15]</c>. <c>0</c> disables the effect entirely.
        /// </summary>
        public float ProsodyCoupling => prosodyCoupling;

        public bool MicroBurstEnabled => microBurstEnabled;
        public float MicroBurstDuration => microBurstDuration;
        public float MicroBurstOvershoot => microBurstOvershoot;
        public float MicroBurstThreshold => microBurstThreshold;

        /// <summary>
        ///     Canonical taxonomy label of the character's resting mood. Empty/whitespace means no
        ///     resting mood. See <see cref="BaselineIntensity" /> for how strongly it shows.
        /// </summary>
        public string BaselineEmotionLabel => baselineEmotionLabel;

        /// <summary>
        ///     Resting intensity of the persona baseline in <c>[0, 1]</c>. <c>0</c> means no resting mood at all.
        /// </summary>
        public float BaselineIntensity => baselineIntensity;

        /// <summary>Authored per-emotion expressiveness (score gain) entries.</summary>
        public IReadOnlyList<EmotionExpressivenessEntry> Expressiveness => expressiveness;

        /// <summary>
        ///     Returns the authored expressiveness gain for <paramref name="canonicalLabel" />, or
        ///     <c>1</c> (unchanged) when the label has no authored entry.
        /// </summary>
        public float GetExpressivenessGain(string canonicalLabel)
        {
            if (string.IsNullOrEmpty(canonicalLabel) || expressiveness == null) return 1f;

            for (int i = 0; i < expressiveness.Count; i++)
            {
                EmotionExpressivenessEntry entry = expressiveness[i];
                if (entry != null && string.Equals(entry.Label, canonicalLabel, System.StringComparison.OrdinalIgnoreCase))
                    return entry.Gain;
            }
            return 1f;
        }

        /// <summary>Authored per-emotion attack/decay smoothing-speed overrides.</summary>
        public IReadOnlyList<EmotionDynamicsEntry> EmotionDynamics => emotionDynamics;

        /// <summary>
        ///     Returns the authored attack/decay smoothing speeds for <paramref name="canonicalLabel" />.
        ///     Returns <c>false</c> (and leaves <paramref name="attack" />/<paramref name="decay" /> at
        ///     <c>0</c>) when the label has no authored override, in which case callers should fall back
        ///     to <see cref="LerpSpeed" />/<see cref="DecaySpeed" />.
        /// </summary>
        public bool TryGetDynamics(string canonicalLabel, out float attack, out float decay)
        {
            attack = 0f;
            decay = 0f;
            if (string.IsNullOrEmpty(canonicalLabel) || emotionDynamics == null) return false;

            for (int i = 0; i < emotionDynamics.Count; i++)
            {
                EmotionDynamicsEntry entry = emotionDynamics[i];
                if (entry != null && string.Equals(entry.Label, canonicalLabel, System.StringComparison.OrdinalIgnoreCase))
                {
                    attack = entry.AttackSpeed;
                    decay = entry.DecaySpeed;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        ///     When <c>true</c>, sustained conversational emotions slowly tint the resting mood via
        ///     a separate drift channel. <c>false</c> keeps the resting mood fixed — the drift channel contributes nothing.
        /// </summary>
        public bool MoodDriftEnabled => moodDriftEnabled;

        /// <summary>
        ///     Exponential rate/s the drift intensity approaches its target while sustained by a
        ///     dominant transient. Only consulted when <see cref="MoodDriftEnabled" /> is <c>true</c>.
        /// </summary>
        public float MoodDriftRate => moodDriftRate;

        /// <summary>
        ///     Exponential rate/s the drift intensity decays back toward 0 once its sustaining
        ///     transient fades or switches label. Only consulted when <see cref="MoodDriftEnabled" />
        ///     is <c>true</c>.
        /// </summary>
        public float MoodRecoveryRate => moodRecoveryRate;

        /// <summary>
        ///     Cap on drift intensity regardless of how strong or long the sustaining transient is.
        ///     Only consulted when <see cref="MoodDriftEnabled" /> is <c>true</c>.
        /// </summary>
        public float MoodDriftMaxIntensity => moodDriftMaxIntensity;

        /// <summary>
        ///     When <c>true</c>, this character can pick up a low-intensity, capped facial echo of
        ///     a nearby OTHER Convai character's strong dominant emotion.
        ///     <c>false</c> means the echo channel never advances and contributes nothing.
        /// </summary>
        public bool ContagionEnabled => contagionEnabled;

        /// <summary>
        ///     How strongly a witnessed other character's emotion carries over in <c>[0, 1]</c>,
        ///     before distance falloff and <see cref="ContagionMaxIntensity" />. Only consulted
        ///     when <see cref="ContagionEnabled" /> is <c>true</c>.
        /// </summary>
        public float ContagionStrength => contagionStrength;

        /// <summary>
        ///     Maximum distance (meters) at which another character's emotion can be witnessed,
        ///     falling off linearly to <c>0</c> at this radius. Only consulted when
        ///     <see cref="ContagionEnabled" /> is <c>true</c>.
        /// </summary>
        public float ContagionRadius => contagionRadius;

        /// <summary>
        ///     Hard cap on the echoed intensity in <c>[0, 1]</c>, regardless of how strong the
        ///     witnessed emotion or how close the other character is. Only consulted when
        ///     <see cref="ContagionEnabled" /> is <c>true</c>.
        /// </summary>
        public float ContagionMaxIntensity => contagionMaxIntensity;

        /// <summary>
        ///     When <c>true</c>, the character can express a primary transient emotion plus taxonomy
        ///     complements simultaneously, with anti-flicker guards. <c>false</c> is a strict
    ///     winner-takes-all pipeline: one non-neutral emotion at a time.
        /// </summary>
        public bool EnableEmotionBlending => enableEmotionBlending;

        /// <summary>
        ///     Minimum time (seconds) before a weaker new label may supplant the current primary
        ///     emotion. Only consulted when <see cref="EnableEmotionBlending" /> is <c>true</c>.
        /// </summary>
        public float EmotionSwitchDwell => emotionSwitchDwell;

        /// <summary>
        ///     A new label bypasses <see cref="EmotionSwitchDwell" /> if its score exceeds the
        ///     current primary's score by at least this margin. Only consulted when
        ///     <see cref="EnableEmotionBlending" /> is <c>true</c>.
        /// </summary>
        public float EmotionSwitchMargin => emotionSwitchMargin;

        /// <summary>
        ///     Weight of a co-occurring taxonomy complement relative to the primary emotion's score.
        ///     Only consulted when <see cref="EnableEmotionBlending" /> is <c>true</c>.
        /// </summary>
        public float ComplementBlendScale => complementBlendScale;

        /// <summary>
        ///     Maximum number of simultaneous transient emotions (primary + complements). Only
        ///     consulted when <see cref="EnableEmotionBlending" /> is <c>true</c>.
        /// </summary>
        public int MaxSimultaneousEmotions => maxSimultaneousEmotions;

        /// <summary>
        ///     When <c>true</c>, the controller runs the micro-expression "life" layer
        ///     (idle drift + speech-coupled brow accent). <c>false</c> (the default) means the
        ///     controller never creates the layer's director/source and never submits to the
        ///     compositor's <c>EmotionMicro</c> layer at all.
        /// </summary>
        public bool MicroExpressionsEnabled => microExpressionsEnabled;

        /// <summary>Idle-drift amplitude in <c>[0, 1]</c>. Only consulted when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.</summary>
        public float MicroExpressionAmplitude => microExpressionAmplitude;

        /// <summary>Speech-coupled brow-raise accent strength in <c>[0, 1]</c>. Only consulted when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.</summary>
        public float SpeechAccentStrength => speechAccentStrength;

        /// <summary>Global idle-drift damp in <c>[0, 1]</c>. Only consulted when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.</summary>
        public float MicroExpressionStillness => microExpressionStillness;

        /// <summary>
        ///     Strength of the sustained attentive lift + sparse accent bursts applied while the
        ///     player is speaking (Listening dialogue state), in <c>[0, 1]</c>. <c>0</c> disables listening reactions entirely. Only consulted when <see cref="MicroExpressionsEnabled" /> is
        ///     <c>true</c>; degrades to always-off when no <c>IConversationFlowSource</c> is
        ///     registered.
        /// </summary>
        public float ListeningReactionStrength => listeningReactionStrength;

        /// <summary>
        ///     Strength of the sustained concentration look (brow-down + faint squint) applied
        ///     during the Thinking dialogue state, in <c>[0, 1]</c>. <c>0</c> disables it entirely. Only consulted
        ///     when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.
        /// </summary>
        public float ThinkingReactionStrength => thinkingReactionStrength;

        /// <summary>
        ///     Peak strength of the one-shot brow flash played on entering the Reacting dialogue
        ///     beat, in <c>[0, 1]</c>. <c>0</c> disables it entirely. Only
        ///     consulted when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.
        /// </summary>
        public float ReactingAccentStrength => reactingAccentStrength;

        /// <summary>
        ///     Peak strength of the one-shot flinch played on entering the Interrupted dialogue
        ///     beat, in <c>[0, 1]</c>. <c>0</c> disables it entirely. Only
        ///     consulted when <see cref="MicroExpressionsEnabled" /> is <c>true</c>.
        /// </summary>
        public float InterruptedFlinchStrength => interruptedFlinchStrength;

        /// <summary>
        ///     Rig-independent expression recipes. An empty list (the default) uses Convai's
        ///     production-safe default recipe library, so a profile always drives a face.
        /// </summary>
        public IReadOnlyList<EmotionExpressionRecipe> ExpressionRecipes => expressionRecipes;

        /// <summary>
        ///     Shader material-property output (blush, tear glisten, sweat, etc.). Empty (the
        ///     default) drives no shader properties.
        /// </summary>
        public MaterialPropertyEmotionBinding MaterialBinding => materialBinding;

        /// <summary>
        ///     Creates a character-scoped runtime copy of the authored material-property binding.
        ///     Runtime state must never live on the shared profile asset itself.
        /// </summary>
        public MaterialPropertyEmotionBinding CreateMaterialRuntimeBinding() =>
            materialBinding != null ? materialBinding.CreateRuntimeCopy() : null;

        /// <summary>
        ///     Resolves the emotion vocabulary, synthesizing the built-in set when the
        ///     author left the field empty.
        /// </summary>
        public EmotionTaxonomyAsset ResolveTaxonomyOrDefault(out bool synthesized)
        {
            if (taxonomy != null)
            {
                taxonomy.EnsureBuilt();
                synthesized = false;
                return taxonomy;
            }

            synthesized = true;
            return EmotionTaxonomyAsset.CreateDefault();
        }

        private void OnValidate()
        {
            expressionRecipes ??= new List<EmotionExpressionRecipe>();

            // NaN guards come first and cover every float below. Mathf.Max/Mathf.Clamp propagate
            // NaN (their comparisons are false), so a NaN written by code or carried by a corrupted
            // asset would otherwise survive validation and poison the accumulator's smoothing math
            // for the whole session. The later fields already did this; these did not.
            if (float.IsNaN(lerpSpeed)) lerpSpeed = 5f;
            if (float.IsNaN(decaySpeed)) decaySpeed = 2f;
            if (float.IsNaN(intensityOffset)) intensityOffset = 0f;
            if (float.IsNaN(prosodyCoupling)) prosodyCoupling = 0f;
            if (float.IsNaN(microBurstDuration)) microBurstDuration = 0.25f;
            if (float.IsNaN(microBurstOvershoot)) microBurstOvershoot = 1.4f;
            if (float.IsNaN(microBurstThreshold)) microBurstThreshold = 0.15f;
            if (float.IsNaN(baselineIntensity)) baselineIntensity = 0f;

            lerpSpeed = Mathf.Clamp(lerpSpeed, 0.1f, 20f);
            decaySpeed = Mathf.Clamp(decaySpeed, 0.1f, 20f);
            intensityOffset = Mathf.Clamp(intensityOffset, -0.25f, 0.25f);
            prosodyCoupling = Mathf.Clamp01(prosodyCoupling);
            microBurstDuration = Mathf.Clamp(microBurstDuration, 0.05f, 1.5f);
            microBurstOvershoot = Mathf.Clamp(microBurstOvershoot, 1f, 3f);
            microBurstThreshold = Mathf.Clamp01(microBurstThreshold);

            baselineIntensity = Mathf.Clamp01(baselineIntensity);
            if (expressiveness != null)
            {
                for (int i = 0; i < expressiveness.Count; i++)
                    expressiveness[i]?.ClampInPlace();
            }

            if (emotionDynamics != null)
            {
                for (int i = 0; i < emotionDynamics.Count; i++)
                    emotionDynamics[i]?.ClampInPlace();
            }

            if (float.IsNaN(moodDriftRate)) moodDriftRate = 0.02f;
            if (float.IsNaN(moodRecoveryRate)) moodRecoveryRate = 0.05f;
            if (float.IsNaN(moodDriftMaxIntensity)) moodDriftMaxIntensity = 0.25f;
            moodDriftRate = Mathf.Clamp(moodDriftRate, 0.001f, 0.5f);
            moodRecoveryRate = Mathf.Clamp(moodRecoveryRate, 0.001f, 1f);
            moodDriftMaxIntensity = Mathf.Clamp01(moodDriftMaxIntensity);

            if (float.IsNaN(contagionStrength)) contagionStrength = 0.3f;
            if (float.IsNaN(contagionRadius)) contagionRadius = 4f;
            if (float.IsNaN(contagionMaxIntensity)) contagionMaxIntensity = 0.2f;
            contagionStrength = Mathf.Clamp01(contagionStrength);
            contagionRadius = Mathf.Clamp(contagionRadius, 0.5f, 20f);
            contagionMaxIntensity = Mathf.Clamp01(contagionMaxIntensity);

            if (float.IsNaN(emotionSwitchDwell)) emotionSwitchDwell = 0.35f;
            if (float.IsNaN(emotionSwitchMargin)) emotionSwitchMargin = 0.15f;
            if (float.IsNaN(complementBlendScale)) complementBlendScale = 0.35f;

            emotionSwitchDwell = Mathf.Clamp(emotionSwitchDwell, 0f, 2f);
            emotionSwitchMargin = Mathf.Clamp01(emotionSwitchMargin);
            complementBlendScale = Mathf.Clamp01(complementBlendScale);
            maxSimultaneousEmotions = Mathf.Clamp(maxSimultaneousEmotions, 1, 4);

            if (float.IsNaN(microExpressionAmplitude)) microExpressionAmplitude = 0.15f;
            if (float.IsNaN(speechAccentStrength)) speechAccentStrength = 0.3f;
            if (float.IsNaN(microExpressionStillness)) microExpressionStillness = 0.5f;
            microExpressionAmplitude = Mathf.Clamp01(microExpressionAmplitude);
            speechAccentStrength = Mathf.Clamp01(speechAccentStrength);
            microExpressionStillness = Mathf.Clamp01(microExpressionStillness);

            if (float.IsNaN(listeningReactionStrength)) listeningReactionStrength = 0f;
            listeningReactionStrength = Mathf.Clamp01(listeningReactionStrength);

            if (float.IsNaN(thinkingReactionStrength)) thinkingReactionStrength = 0f;
            if (float.IsNaN(reactingAccentStrength)) reactingAccentStrength = 0f;
            if (float.IsNaN(interruptedFlinchStrength)) interruptedFlinchStrength = 0f;
            thinkingReactionStrength = Mathf.Clamp01(thinkingReactionStrength);
            reactingAccentStrength = Mathf.Clamp01(reactingAccentStrength);
            interruptedFlinchStrength = Mathf.Clamp01(interruptedFlinchStrength);
        }

        /// <summary>
        ///     Creates the runtime-default profile instance used when a character has no profile
        ///     asset wired up.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Returns a fully configured profile, not a blank one. A bare
        ///         <see cref="ScriptableObject.CreateInstance{T}" /> here meant a character with an
        ///         Emotion Controller and no assigned profile installed no expression recipes and
        ///         therefore drove no output at all — the module's single most damaging defect, since
        ///         it was the default path.
        ///     </para>
        ///     <para>
        ///         Uses the calm, even <see cref="CharacterDemeanor.Composed" /> temperament rather
        ///         than the warmer one the setup button offers: a character whose personality nobody
        ///         authored should not arrive with a resting mood its author never chose. It is still
        ///         alive — small movements, blending and the conversation-beat reactions are all on.
        ///     </para>
        /// </remarks>
        public static ConvaiEmotionProfile CreateDefault()
        {
            ConvaiEmotionProfile instance = CreatePreset(CharacterDemeanor.Composed, null);
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        /// <summary>
        ///     Builds a profile from a character-type value table, skipping per-emotion entries whose
        ///     label the supplied vocabulary does not define.
        /// </summary>
        /// <remarks>
        ///     The single place a character type becomes a profile. Every public preset factory and
        ///     the editor's apply-a-character-type writer resolve through
        ///     <see cref="EmotionPersonalityTable" />, so what a type means cannot differ between
        ///     the two.
        /// </remarks>
        internal static ConvaiEmotionProfile CreateFrom(
            in EmotionPersonalityValues values, EmotionTaxonomyAsset taxonomy)
        {
            ConvaiEmotionProfile instance = CreateInstance<ConvaiEmotionProfile>();
            instance.taxonomy = taxonomy;
            instance.expressionRecipes = EmotionExpressionRecipe.CreateDefaultSet();

            // intensityOffset is deliberately not part of a character type — it is the author's own
            // global trim, and applying a temperament must not silently discard it.
            instance.intensityOffset = 0f;

            instance.lerpSpeed = values.LerpSpeed;
            instance.decaySpeed = values.DecaySpeed;
            instance.prosodyCoupling = values.ProsodyCoupling;

            instance.microBurstEnabled = values.MicroBurstEnabled;
            instance.microBurstDuration = values.MicroBurstDuration;
            instance.microBurstOvershoot = values.MicroBurstOvershoot;
            instance.microBurstThreshold = values.MicroBurstThreshold;

            instance.baselineEmotionLabel = values.BaselineEmotionLabel;
            instance.baselineIntensity = values.BaselineIntensity;
            instance.moodDriftEnabled = values.MoodDriftEnabled;
            instance.moodDriftRate = values.MoodDriftRate;
            instance.moodRecoveryRate = values.MoodRecoveryRate;
            instance.moodDriftMaxIntensity = values.MoodDriftMaxIntensity;

            instance.contagionEnabled = values.ContagionEnabled;
            instance.contagionStrength = values.ContagionStrength;
            instance.contagionRadius = values.ContagionRadius;
            instance.contagionMaxIntensity = values.ContagionMaxIntensity;

            instance.enableEmotionBlending = values.EnableEmotionBlending;
            instance.emotionSwitchDwell = values.EmotionSwitchDwell;
            instance.emotionSwitchMargin = values.EmotionSwitchMargin;
            instance.complementBlendScale = values.ComplementBlendScale;
            instance.maxSimultaneousEmotions = values.MaxSimultaneousEmotions;

            instance.microExpressionsEnabled = values.MicroExpressionsEnabled;
            instance.microExpressionAmplitude = values.MicroExpressionAmplitude;
            instance.speechAccentStrength = values.SpeechAccentStrength;
            instance.microExpressionStillness = values.MicroExpressionStillness;
            instance.listeningReactionStrength = values.ListeningReactionStrength;
            instance.thinkingReactionStrength = values.ThinkingReactionStrength;
            instance.reactingAccentStrength = values.ReactingAccentStrength;
            instance.interruptedFlinchStrength = values.InterruptedFlinchStrength;

            var expressivenessList = new List<EmotionExpressivenessEntry>();
            PersonalityExpressiveness[] gains = values.Expressiveness;
            for (int i = 0; i < gains.Length; i++)
            {
                if (!TaxonomyHasLabel(taxonomy, gains[i].Label)) continue;
                expressivenessList.Add(new EmotionExpressivenessEntry(gains[i].Label, gains[i].Gain));
            }
            instance.expressiveness = expressivenessList;

            var dynamicsList = new List<EmotionDynamicsEntry>();
            PersonalityDynamics[] dynamics = values.Dynamics;
            for (int i = 0; i < dynamics.Length; i++)
            {
                if (!TaxonomyHasLabel(taxonomy, dynamics[i].Label)) continue;
                dynamicsList.Add(new EmotionDynamicsEntry(
                    dynamics[i].Label, dynamics[i].AttackSpeed, dynamics[i].DecaySpeed));
            }
            instance.emotionDynamics = dynamicsList;

            return instance;
        }

        /// <summary>
        ///     Returns <c>true</c> when <paramref name="label" /> exists in
        ///     <paramref name="taxonomy" />, or when no taxonomy is supplied — in which case the
        ///     label is accepted as-is so callers without a taxonomy reference still get usable
        ///     presets. Lets a character type skip per-emotion entries naming an emotion a custom
        ///     vocabulary does not define, rather than authoring an entry that resolves to nothing.
        /// </summary>
        private static bool TaxonomyHasLabel(EmotionTaxonomyAsset taxonomy, string label)
        {
            if (taxonomy == null) return true;
            if (string.IsNullOrWhiteSpace(label)) return false;
            taxonomy.EnsureBuilt();
            return taxonomy.TryResolve(label, out _);
        }

        /// <summary>
        ///     Creates a profile with one of the four starting temperaments, with the rig-independent
        ///     expression recipe library installed so it drives a face on any supported rig without
        ///     per-rig authoring.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         What each temperament reads as:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <see cref="CharacterDemeanor.Composed" /> — calm and even, resting on a faint
        ///             <c>trust</c>; a receptionist, clerk or guide. Small movements, emotion blending and
        ///             the conversation-beat reactions are on but quiet, so the character reads as
        ///             composed rather than as a frozen mask.
        ///         </item>
        ///         <item>
        ///             <see cref="CharacterDemeanor.Warm" /> — a visible resting <c>joy</c>; smiles and
        ///             trusts readily while frowning and angering less so, a joy that arrives quickly
        ///             and a sadness that lets go gently, and a lively but not distracting
        ///             small-movement layer.
        ///         </item>
        ///         <item>
        ///             <see cref="CharacterDemeanor.Energetic" /> — the most openly cheerful resting mood,
        ///             every emotion amplified, a fast attack, anger and surprise that snap on, and a
        ///             pronounced small-movement layer; a host, tour guide or streamer.
        ///         </item>
        ///         <item>
        ///             <see cref="CharacterDemeanor.Reserved" /> — no resting mood, every emotion
        ///             uniformly damped, slow to arrive and slow to let go, and a small-movement
        ///             layer that is present but only just; a guard or an officiant.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         One method rather than one per temperament, deliberately. The four used to be
        ///         separate factories carrying the temperament in their names, so renaming a
        ///         temperament meant a breaking API change and the names fell out of step with the
        ///         Inspector's. The temperament is a parameter now, and its spelling has a single
        ///         owner in <see cref="CharacterDemeanors" />.
        ///     </para>
        /// </remarks>
        /// <param name="demeanor">
        ///     Which temperament to build. An unrecognised value resolves to
        ///     <see cref="CharacterDemeanor.Warm" />, matching the SDK-wide default personality.
        /// </param>
        /// <param name="taxonomy">
        ///     Taxonomy asset the per-emotion entry labels resolve against, or <c>null</c> to accept
        ///     the built-in labels as authored.
        /// </param>
        public static ConvaiEmotionProfile CreatePreset(
            CharacterDemeanor demeanor, EmotionTaxonomyAsset taxonomy) =>
            CreateFrom(EmotionPersonalityTable.For(demeanor), taxonomy);
    }
}
