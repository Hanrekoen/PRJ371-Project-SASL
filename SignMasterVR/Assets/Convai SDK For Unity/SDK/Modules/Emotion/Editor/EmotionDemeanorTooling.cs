using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Applies one of the four character types onto an existing
    ///     <see cref="ConvaiEmotionProfile" /> asset, and answers whether an asset already matches
    ///     one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both operations read <see cref="EmotionPersonalityTable" />, the same table the public
    ///         <c>Create*Preset</c> factories build from. Before that table existed, the writer set a
    ///         subset of what the factories set — it never wrote the response speeds, so applying
    ///         <em>Reserved</em> to a warm profile left it reacting at the warm speed — and the
    ///         comparison looked at a different, smaller subset again.
    ///     </para>
    ///     <para>
    ///         What is written: the personality — response speeds, the kick as an emotion lands,
    ///         resting mood and drift, mixing, small movements and beat reactions, picking up other
    ///         characters' moods, and the per-emotion strength/timing tables. What is deliberately
    ///         left alone: the emotion vocabulary, the expression recipes, the material output, and
    ///         the author's own global strength trim — a temperament change must never discard
    ///         authored content.
    ///     </para>
    ///     <para>
    ///         The caller owns <see cref="SerializedObject.Update" />/
    ///         <see cref="SerializedObject.ApplyModifiedProperties" /> so the change lands as a
    ///         single undoable step.
    ///     </para>
    /// </remarks>
    internal static class EmotionDemeanorTooling
    {
        /// <summary>
        ///     Writes <paramref name="characterType" />'s personality values onto
        ///     <paramref name="serializedObject" />'s target.
        /// </summary>
        /// <param name="taxonomy">
        ///     Vocabulary the per-emotion entries resolve against. An entry naming an emotion this
        ///     vocabulary does not define is skipped rather than authored.
        /// </param>
        internal static void Apply(
            SerializedObject serializedObject, CharacterDemeanor characterType, EmotionTaxonomyAsset taxonomy)
        {
            if (serializedObject == null) return;

            EmotionPersonalityValues values = EmotionPersonalityTable.For(characterType);

            Write(serializedObject, "lerpSpeed", values.LerpSpeed);
            Write(serializedObject, "decaySpeed", values.DecaySpeed);
            Write(serializedObject, "prosodyCoupling", values.ProsodyCoupling);

            Write(serializedObject, "microBurstEnabled", values.MicroBurstEnabled);
            Write(serializedObject, "microBurstDuration", values.MicroBurstDuration);
            Write(serializedObject, "microBurstOvershoot", values.MicroBurstOvershoot);
            Write(serializedObject, "microBurstThreshold", values.MicroBurstThreshold);

            Write(serializedObject, "baselineEmotionLabel", values.BaselineEmotionLabel);
            Write(serializedObject, "baselineIntensity", values.BaselineIntensity);
            Write(serializedObject, "moodDriftEnabled", values.MoodDriftEnabled);
            Write(serializedObject, "moodDriftRate", values.MoodDriftRate);
            Write(serializedObject, "moodRecoveryRate", values.MoodRecoveryRate);
            Write(serializedObject, "moodDriftMaxIntensity", values.MoodDriftMaxIntensity);

            Write(serializedObject, "contagionEnabled", values.ContagionEnabled);
            Write(serializedObject, "contagionStrength", values.ContagionStrength);
            Write(serializedObject, "contagionRadius", values.ContagionRadius);
            Write(serializedObject, "contagionMaxIntensity", values.ContagionMaxIntensity);

            Write(serializedObject, "enableEmotionBlending", values.EnableEmotionBlending);
            Write(serializedObject, "emotionSwitchDwell", values.EmotionSwitchDwell);
            Write(serializedObject, "emotionSwitchMargin", values.EmotionSwitchMargin);
            Write(serializedObject, "complementBlendScale", values.ComplementBlendScale);
            serializedObject.FindProperty("maxSimultaneousEmotions").intValue = values.MaxSimultaneousEmotions;

            Write(serializedObject, "microExpressionsEnabled", values.MicroExpressionsEnabled);
            Write(serializedObject, "microExpressionAmplitude", values.MicroExpressionAmplitude);
            Write(serializedObject, "speechAccentStrength", values.SpeechAccentStrength);
            Write(serializedObject, "microExpressionStillness", values.MicroExpressionStillness);
            Write(serializedObject, "listeningReactionStrength", values.ListeningReactionStrength);
            Write(serializedObject, "thinkingReactionStrength", values.ThinkingReactionStrength);
            Write(serializedObject, "reactingAccentStrength", values.ReactingAccentStrength);
            Write(serializedObject, "interruptedFlinchStrength", values.InterruptedFlinchStrength);

            WriteExpressiveness(serializedObject.FindProperty("expressiveness"), values.Expressiveness, taxonomy);
            WriteDynamics(serializedObject.FindProperty("emotionDynamics"), values.Dynamics, taxonomy);
        }

        /// <summary>
        ///     Whether <paramref name="profile" />'s current values are <paramref name="type" />'s.
        /// </summary>
        /// <remarks>
        ///     Compares exactly the fields <see cref="Apply" /> writes, so "what a type sets" and
        ///     "what counts as being that type" cannot disagree — the earlier version compared four
        ///     of them, and three of Convai's own shipped personalities matched no type at all,
        ///     leaving the Inspector's picker with nothing highlighted. Reads struct fields only: no
        ///     <c>ScriptableObject</c> is built, which matters because an inspector calls this once
        ///     per character type per repaint.
        /// </remarks>
        internal static bool Matches(ConvaiEmotionProfile profile, CharacterDemeanor type)
        {
            if (profile == null) return false;

            EmotionPersonalityValues v = EmotionPersonalityTable.For(type);

            return Same(profile.LerpSpeed, v.LerpSpeed) &&
                   Same(profile.DecaySpeed, v.DecaySpeed) &&
                   Same(profile.ProsodyCoupling, v.ProsodyCoupling) &&
                   profile.MicroBurstEnabled == v.MicroBurstEnabled &&
                   Same(profile.MicroBurstDuration, v.MicroBurstDuration) &&
                   Same(profile.MicroBurstOvershoot, v.MicroBurstOvershoot) &&
                   Same(profile.MicroBurstThreshold, v.MicroBurstThreshold) &&
                   SameLabel(profile.BaselineEmotionLabel, v.BaselineEmotionLabel) &&
                   Same(profile.BaselineIntensity, v.BaselineIntensity) &&
                   profile.MoodDriftEnabled == v.MoodDriftEnabled &&
                   Same(profile.MoodDriftRate, v.MoodDriftRate) &&
                   Same(profile.MoodRecoveryRate, v.MoodRecoveryRate) &&
                   Same(profile.MoodDriftMaxIntensity, v.MoodDriftMaxIntensity) &&
                   profile.ContagionEnabled == v.ContagionEnabled &&
                   Same(profile.ContagionStrength, v.ContagionStrength) &&
                   Same(profile.ContagionRadius, v.ContagionRadius) &&
                   Same(profile.ContagionMaxIntensity, v.ContagionMaxIntensity) &&
                   profile.EnableEmotionBlending == v.EnableEmotionBlending &&
                   Same(profile.EmotionSwitchDwell, v.EmotionSwitchDwell) &&
                   Same(profile.EmotionSwitchMargin, v.EmotionSwitchMargin) &&
                   Same(profile.ComplementBlendScale, v.ComplementBlendScale) &&
                   profile.MaxSimultaneousEmotions == v.MaxSimultaneousEmotions &&
                   profile.MicroExpressionsEnabled == v.MicroExpressionsEnabled &&
                   Same(profile.MicroExpressionAmplitude, v.MicroExpressionAmplitude) &&
                   Same(profile.SpeechAccentStrength, v.SpeechAccentStrength) &&
                   Same(profile.MicroExpressionStillness, v.MicroExpressionStillness) &&
                   Same(profile.ListeningReactionStrength, v.ListeningReactionStrength) &&
                   Same(profile.ThinkingReactionStrength, v.ThinkingReactionStrength) &&
                   Same(profile.ReactingAccentStrength, v.ReactingAccentStrength) &&
                   Same(profile.InterruptedFlinchStrength, v.InterruptedFlinchStrength) &&
                   ExpressivenessMatches(profile, v.Expressiveness) &&
                   DynamicsMatch(profile, v.Dynamics);
        }

        /// <summary>
        ///     The character type <paramref name="profile" /> currently is, or <c>null</c> when an
        ///     author has tuned it away from all four.
        /// </summary>
        internal static CharacterDemeanor? Identify(ConvaiEmotionProfile profile)
        {
            if (profile == null) return null;

            IReadOnlyList<CharacterDemeanor> order = EmotionPersonalityTable.Order;
            for (int i = 0; i < order.Count; i++)
                if (Matches(profile, order[i]))
                    return order[i];

            return null;
        }

        // ------------------------------------------------------------------ comparison helpers

        /// <summary>
        ///     Tolerance for a serialized float against its authored source. Wide enough to survive
        ///     YAML round-tripping, far narrower than any authorable step on these ranges.
        /// </summary>
        private const float Tolerance = 1e-4f;

        private static bool Same(float a, float b) => Mathf.Abs(a - b) <= Tolerance;

        /// <summary>Empty and whitespace both mean "no resting mood", so they compare equal.</summary>
        private static bool SameLabel(string a, string b)
        {
            bool aEmpty = string.IsNullOrWhiteSpace(a);
            bool bEmpty = string.IsNullOrWhiteSpace(b);
            if (aEmpty || bEmpty) return aEmpty && bEmpty;
            return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Order-sensitive, because <see cref="Apply" /> writes the table's order verbatim and a
        ///     reordered list is a list the author has touched.
        /// </summary>
        private static bool ExpressivenessMatches(
            ConvaiEmotionProfile profile, PersonalityExpressiveness[] expected)
        {
            System.Collections.Generic.IReadOnlyList<EmotionExpressivenessEntry> actual = profile.Expressiveness;
            int actualCount = actual?.Count ?? 0;
            if (actualCount != expected.Length) return false;

            for (int i = 0; i < expected.Length; i++)
            {
                EmotionExpressivenessEntry entry = actual[i];
                if (entry == null) return false;
                if (!SameLabel(entry.Label, expected[i].Label)) return false;
                if (!Same(entry.Gain, expected[i].Gain)) return false;
            }
            return true;
        }

        private static bool DynamicsMatch(ConvaiEmotionProfile profile, PersonalityDynamics[] expected)
        {
            System.Collections.Generic.IReadOnlyList<EmotionDynamicsEntry> actual = profile.EmotionDynamics;
            int actualCount = actual?.Count ?? 0;
            if (actualCount != expected.Length) return false;

            for (int i = 0; i < expected.Length; i++)
            {
                EmotionDynamicsEntry entry = actual[i];
                if (entry == null) return false;
                if (!SameLabel(entry.Label, expected[i].Label)) return false;
                if (!Same(entry.AttackSpeed, expected[i].AttackSpeed)) return false;
                if (!Same(entry.DecaySpeed, expected[i].DecaySpeed)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ write helpers

        private static void Write(SerializedObject serialized, string field, float value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null) property.floatValue = value;
        }

        private static void Write(SerializedObject serialized, string field, bool value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null) property.boolValue = value;
        }

        private static void Write(SerializedObject serialized, string field, string value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null) property.stringValue = value ?? string.Empty;
        }

        /// <summary>
        ///     Whether <paramref name="taxonomy" /> defines <paramref name="label" />. A null
        ///     vocabulary means the built-in set, which defines every label these tables use, so the
        ///     entry is kept.
        /// </summary>
        private static bool Defines(EmotionTaxonomyAsset taxonomy, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            if (taxonomy == null) return true;
            taxonomy.EnsureBuilt();
            return taxonomy.TryResolve(label, out _);
        }

        private static void WriteExpressiveness(
            SerializedProperty listProperty, PersonalityExpressiveness[] entries, EmotionTaxonomyAsset taxonomy)
        {
            if (listProperty == null) return;

            listProperty.ClearArray();
            int written = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!Defines(taxonomy, entries[i].Label)) continue;

                listProperty.InsertArrayElementAtIndex(written);
                SerializedProperty element = listProperty.GetArrayElementAtIndex(written);
                element.FindPropertyRelative("label").stringValue = entries[i].Label;
                element.FindPropertyRelative("gain").floatValue = entries[i].Gain;
                written++;
            }
        }

        private static void WriteDynamics(
            SerializedProperty listProperty, PersonalityDynamics[] entries, EmotionTaxonomyAsset taxonomy)
        {
            if (listProperty == null) return;

            listProperty.ClearArray();
            int written = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!Defines(taxonomy, entries[i].Label)) continue;

                listProperty.InsertArrayElementAtIndex(written);
                SerializedProperty element = listProperty.GetArrayElementAtIndex(written);
                element.FindPropertyRelative("label").stringValue = entries[i].Label;
                element.FindPropertyRelative("attackSpeed").floatValue = entries[i].AttackSpeed;
                element.FindPropertyRelative("decaySpeed").floatValue = entries[i].DecaySpeed;
                written++;
            }
        }
    }
}
