using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Plain-English display labels and tooltips for the profile fields whose serialized names
    ///     are internal vocabulary. Unity derives a field's inspector label from its identifier,
    ///     which is fine for <c>taxonomy</c> and useless for <c>complementBlendScale</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Display only. Serialized names, public API and asset data are untouched, so this
    ///         changes nothing but what the user reads — which is the point: terms like Lerp,
    ///         Hysteresis, Prosody, Contagion, Overshoot and Dwell are engine and research
    ///         vocabulary that leaked onto a customer-facing surface.
    ///     </para>
    ///     <para>
    ///         Tooltips are overridden alongside the labels because the authored ones carry the same
    ///         vocabulary, plus development-era phrasing that dates the SDK
    ///         that means nothing to a customer.
    ///     </para>
    /// </remarks>
    internal static class EmotionConfigLabels
    {
        private readonly struct Label
        {
            internal readonly string Text;
            internal readonly string Tooltip;

            internal Label(string text, string tooltip)
            {
                Text = text;
                Tooltip = tooltip;
            }
        }

        /// <summary>Serialized field name → what the user should actually read.</summary>
        private static readonly Dictionary<string, Label> Labels = new()
        {
            // Personality. "Lerp"/"Decay" are interpolation terms; name the behaviour instead.
            {
                "intensityOffset",
                new Label("How Strongly It Shows",
                    "Nudges every expression up or down. Leave at 0 unless the character reads as " +
                    "generally too flat or too much.")
            },
            {
                "lerpSpeed",
                new Label("How Fast Emotions Appear",
                    "Higher reads as quick and reactive; lower as measured and slow to show its hand.")
            },
            {
                "decaySpeed",
                new Label("How Fast Emotions Fade",
                    "Higher lets go of a feeling sooner; lower holds onto it.")
            },
            {
                "prosodyCoupling",
                new Label("Follow Voice Energy",
                    "While speaking, the expression brightens on emphatic delivery and softens in " +
                    "lulls. 0 turns this off.")
            },

            // Resting mood. "Persona baseline" and "mood drift" are the module's own terms.
            {
                "baselineEmotionLabel",
                new Label("Resting Mood",
                    "What the face settles to when nothing is happening. Empty means a plain " +
                    "neutral rest.")
            },
            {
                "baselineIntensity",
                new Label("Resting Mood Strength",
                    "How strongly the resting mood shows. 0 means no resting mood at all.")
            },
            {
                "moodDriftEnabled",
                new Label("Mood Follows The Conversation",
                    "A long stretch of one feeling leaves the character in that mood for a while " +
                    "afterwards, instead of snapping straight back.")
            },
            {
                "moodDriftRate",
                new Label("How Quickly Mood Shifts",
                    "How fast a sustained feeling starts colouring the resting mood.")
            },
            {
                "moodRecoveryRate",
                new Label("How Long Mood Lingers",
                    "Lower values keep the shifted mood around longer once the feeling passes.")
            },
            {
                "moodDriftMaxIntensity",
                new Label("How Far Mood Can Shift",
                    "A ceiling, so a long difficult conversation can never take the character over " +
                    "completely.")
            },

            // Reactions. "Micro burst" and "flinch/accent strength" are internal names.
            {
                "microBurstEnabled",
                new Label("Extra Kick When An Emotion Arrives",
                    "The expression briefly pushes past where it will settle as a new emotion lands, " +
                    "so it reads as a reaction rather than a fade-in.")
            },
            { "microBurstDuration", new Label("Kick Length", "How long that initial push lasts before it settles.") },
            { "microBurstOvershoot", new Label("Kick Strength", "How far past the settled value the expression pushes.") },
            {
                "microBurstThreshold",
                new Label("Minimum Change To Kick",
                    "Small changes settle in quietly; only a change this large gets the extra kick.")
            },
            // Small movements, and the conversation-beat reactions the same layer composes.
            {
                "listeningReactionStrength",
                new Label("Attentive While Listening",
                    "A sustained attentive look while the player is speaking. 0 turns it off. " +
                    "Also needs the Conversation Flow module.")
            },
            {
                "thinkingReactionStrength",
                new Label("Concentrating While Thinking",
                    "A concentration look during the pause before the character replies. 0 turns " +
                    "it off. Also needs the Conversation Flow module.")
            },
            {
                "reactingAccentStrength",
                new Label("Reaction Flash",
                    "A one-off flash of expression when something notable happens. 0 turns it off. " +
                    "Also needs the Conversation Flow module.")
            },
            {
                "interruptedFlinchStrength",
                new Label("Flinch When Interrupted",
                    "A one-off flinch when the character is cut off mid-sentence. 0 turns it off. " +
                    "Also needs the Conversation Flow module.")
            },

            {
                "microExpressionsEnabled",
                new Label("Never Sits Perfectly Still",
                    "Keeps a trace of movement in the face so a resting character does not read as " +
                    "a frozen mask.")
            },
            { "microExpressionAmplitude", new Label("How Much Movement", "How large the idle movement is.") },
            {
                "microExpressionStillness",
                new Label("How Settled",
                    "Higher damps the idle movement toward stillness without turning it off.")
            },
            {
                "speechAccentStrength",
                new Label("Brow Accent While Speaking",
                    "A small brow lift on emphasis while the character talks.")
            },

            // Mixing emotions. "Complement", "hysteresis", "dwell" and "margin" all go.
            {
                "enableEmotionBlending",
                new Label("Show More Than One Emotion At Once",
                    "Real faces rarely show one pure feeling. With this on, related emotions come " +
                    "along at reduced strength — anger with a trace of disgust, fear with a trace " +
                    "of surprise.")
            },
            {
                "complementBlendScale",
                new Label("Secondary Emotion Strength",
                    "How strongly the related emotion comes along, relative to the main one.")
            },
            {
                "maxSimultaneousEmotions",
                new Label("Most Emotions At Once",
                    "Including the main one.")
            },
            {
                "emotionSwitchDwell",
                new Label("Minimum Time Before Switching",
                    "Stops the face flickering when the character's feelings change rapidly.")
            },
            {
                "emotionSwitchMargin",
                new Label("Switch Immediately If Stronger By",
                    "A clearly stronger new feeling cuts in straight away instead of waiting.")
            },

            // Other characters. "Contagion" is a research term.
            {
                "contagionEnabled",
                new Label("Picks Up Other Characters' Moods",
                    "Catches a faint echo of a strong feeling from another Convai character nearby.")
            },
            { "contagionStrength", new Label("How Strongly", "How much of the other character's feeling carries over.") },
            { "contagionRadius", new Label("Within This Distance", "In metres. The effect fades with distance.") },
            {
                "contagionMaxIntensity",
                new Label("Never Stronger Than",
                    "A ceiling, so a caught mood stays an echo rather than taking over.")
            },

            // Per-emotion tuning.
            {
                "expressiveness",
                new Label("Per-Emotion Strength",
                    "Make one emotion easier or harder for this character to show. Above 1 shows it " +
                    "more readily, below 1 less.")
            },
            {
                "emotionDynamics",
                new Label("Per-Emotion Timing",
                    "Override how fast a single emotion appears and fades, for characters whose " +
                    "anger should snap on while their sadness creeps in.")
            },

            // Vocabulary and expressions.
            {
                "taxonomy",
                new Label("Emotion Vocabulary",
                    "Which emotions this character understands, and the words the backend may use " +
                    "for each. Leave empty to use the built-in set.")
            },
            {
                "expressionRecipes",
                new Label("Expression Recipes",
                    "What each emotion does to the face, described by what should move rather than " +
                    "by blendshape names — so one profile works on any supported face rig. Leave " +
                    "empty to use Convai's built-in expressions.")
            },
            {
                "materialBinding",
                new Label("Material Effects",
                    "Optional shader effects driven by emotion, such as blush, tears or sweat. " +
                    "Needs a material with the matching properties.")
            }
        };

        /// <summary>
        ///     Cached <see cref="GUIContent" /> per field. Inspectors redraw constantly, and
        ///     building a GUIContent per field per repaint is the same class of waste the editor
        ///     style guide forbids for GUIStyle.
        /// </summary>
        private static readonly Dictionary<string, GUIContent> Cache = new();

        /// <summary>
        ///     The label to draw <paramref name="property" /> with: the plain-English override when
        ///     one exists, otherwise Unity's own derived name and the authored tooltip.
        /// </summary>
        internal static GUIContent For(SerializedProperty property)
        {
            if (property == null) return GUIContent.none;

            string fieldName = property.name;
            if (Cache.TryGetValue(fieldName, out GUIContent cached)) return cached;

            GUIContent content = Labels.TryGetValue(fieldName, out Label plain)
                ? new GUIContent(plain.Text, plain.Tooltip)
                : new GUIContent(property.displayName, property.tooltip);

            Cache[fieldName] = content;
            return content;
        }

        /// <summary>Whether this field carries a plain-English override (used by the naming guard test).</summary>
        internal static bool HasOverride(string fieldName) => Labels.ContainsKey(fieldName);

        /// <summary>Every field name this table renames, for the naming guard test.</summary>
        internal static IEnumerable<string> OverriddenFields => Labels.Keys;

        /// <summary>Every user-visible string this table produces, for the naming guard test.</summary>
        internal static IEnumerable<string> AllUserVisibleText
        {
            get
            {
                foreach (KeyValuePair<string, Label> entry in Labels)
                {
                    yield return entry.Value.Text;
                    yield return entry.Value.Tooltip;
                }
            }
        }
    }
}
