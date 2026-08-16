using Convai.Domain.Embodiment.Semantics;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Editor
{
    /// <summary>
    ///     One-shot "starting point" demeanor presets for <c>ConvaiBodyLanguageProfile</c>, applied
    ///     via the profile inspector's Demeanor row. The four names come from
    ///     <see cref="CharacterDemeanor" />, the SDK-wide temperament vocabulary Emotion and Body
    ///     Animation also present, so one word means one character across all three inspectors.
    ///     This is EDITOR-ONLY tuning: applying a preset multiplies a handful of already-serialized
    ///     profile fields ONCE (undo-able) and writes plain values — there is no runtime coupling to
    ///     the Emotion module and no new runtime field.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Multipliers are feel-pass tunable, chosen to be directionally consistent with the
    ///         matching facial-side preset (Warm leans warmer/livelier, Reserved dampens and slows,
    ///         Energetic amplifies and quickens); Composed is the identity (no change), matching the
    ///         facial side's "behaviorally identical to the conversational default" contract.
    ///     </para>
    ///     <para>
    ///         The names are not restated here. Emotion, Body Animation and Body Language all read
    ///         one shared vocabulary, so a user comparing two inspectors on the same character
    ///         never sees two names for one idea, and <c>DemeanorVocabularyGuardTests</c> fails the
    ///         build if a module grows a private copy.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     <b>What a preset may touch.</b> <see cref="Apply" /> only ever scales the per-state
    ///     posture, fidget and ambient-drift biases plus <c>beatHeadIntensity</c> and
    ///     <c>weightShiftIntervalSeconds</c>. Fields that carry a tuned absolute default —
    ///     <c>breathHeadStabilization</c> and <c>beatMinIntervalSeconds</c> among them — have no
    ///     <see cref="Multipliers" /> entry and are deliberately never overwritten, so applying a
    ///     demeanor composes with the shipped tuning instead of replacing it.
    /// </remarks>
    internal static class BodyLanguageDemeanorPresets
    {
        /// <summary>One demeanor preset's multiplier set.</summary>
        internal readonly struct Multipliers
        {
            /// <summary>Which of the four SDK-wide temperaments this preset is.</summary>
            public readonly CharacterDemeanor Demeanor;

            /// <summary>Multiplies every per-state <c>PostureOpennessBias</c>, clamped to -1..1.</summary>
            public readonly float PostureOpennessBiasScale;

            /// <summary>Multiplies every per-state <c>SagittalLeanBias</c>, clamped to -1..1.</summary>
            public readonly float SagittalLeanBiasScale;

            /// <summary>Multiplies every per-state <c>FidgetRate</c>, clamped to 0..1.</summary>
            public readonly float FidgetRateScale;

            /// <summary>Multiplies every per-state <c>AmbientDrift</c>, clamped to 0..1.</summary>
            public readonly float AmbientDriftScale;

            /// <summary>Multiplies <c>beatHeadIntensity</c>, clamped to 0..1.</summary>
            public readonly float BeatHeadIntensityScale;

            /// <summary>Divides <c>weightShiftIntervalSeconds</c> (a livelier preset divides by &gt;1, shortening the interval).</summary>
            public readonly float WeightShiftIntervalDivisor;

            /// <summary>
            ///     The one spelling a user ever sees, read from <see cref="CharacterDemeanors" />
            ///     rather than stored here — see the type remarks for what owning a copy cost.
            /// </summary>
            public string Name => CharacterDemeanors.DisplayName(Demeanor);

            public Multipliers(
                CharacterDemeanor demeanor,
                float postureOpennessBiasScale,
                float sagittalLeanBiasScale,
                float fidgetRateScale,
                float ambientDriftScale,
                float beatHeadIntensityScale,
                float weightShiftIntervalDivisor)
            {
                Demeanor = demeanor;
                PostureOpennessBiasScale = postureOpennessBiasScale;
                SagittalLeanBiasScale = sagittalLeanBiasScale;
                FidgetRateScale = fidgetRateScale;
                AmbientDriftScale = ambientDriftScale;
                BeatHeadIntensityScale = beatHeadIntensityScale;
                WeightShiftIntervalDivisor = weightShiftIntervalDivisor;
            }
        }

        /// <summary>The four shipped demeanor presets, in dropdown order.</summary>
        internal static readonly Multipliers[] Presets =
        {
            new(CharacterDemeanor.Composed, 1f, 1f, 1f, 1f, 1f, 1f),
            new(CharacterDemeanor.Warm, 1.15f, 1.1f, 1.1f, 1.1f, 1.15f, 1.1f),
            new(CharacterDemeanor.Energetic, 1.3f, 1.25f, 1.3f, 1.3f, 1.35f, 1.4f),
            new(CharacterDemeanor.Reserved, 0.6f, 0.6f, 0.5f, 0.6f, 0.6f, 0.7f)
        };

        /// <summary>
        ///     Applies <paramref name="preset" />'s multipliers to <paramref name="serializedProfile" />'s
        ///     already-serialized fields, once, inside a single undo group named after the preset
        ///     ("one-shot tuning starting point, not a live link").
        /// </summary>
        internal static void Apply(SerializedObject serializedProfile, in Multipliers preset)
        {
            if (serializedProfile == null) return;

            Undo.SetCurrentGroupName($"Apply Body Language Demeanor: {preset.Name}");
            int undoGroup = Undo.GetCurrentGroup();

            SerializedProperty statePolicies = serializedProfile.FindProperty("statePolicies");
            if (statePolicies != null && statePolicies.isArray)
            {
                for (int i = 0; i < statePolicies.arraySize; i++)
                {
                    SerializedProperty element = statePolicies.GetArrayElementAtIndex(i);
                    ScaleClamped(element.FindPropertyRelative("PostureOpennessBias"), preset.PostureOpennessBiasScale, -1f, 1f);
                    ScaleClamped(element.FindPropertyRelative("SagittalLeanBias"), preset.SagittalLeanBiasScale, -1f, 1f);
                    ScaleClamped(element.FindPropertyRelative("FidgetRate"), preset.FidgetRateScale, 0f, 1f);
                    ScaleClamped(element.FindPropertyRelative("AmbientDrift"), preset.AmbientDriftScale, 0f, 1f);
                }
            }

            ScaleClamped(serializedProfile.FindProperty("beatHeadIntensity"), preset.BeatHeadIntensityScale, 0f, 1f);

            SerializedProperty weightShiftInterval = serializedProfile.FindProperty("weightShiftIntervalSeconds");
            if (weightShiftInterval != null && preset.WeightShiftIntervalDivisor > 0.0001f)
                weightShiftInterval.floatValue =
                    Mathf.Clamp(weightShiftInterval.floatValue / preset.WeightShiftIntervalDivisor, 6f, 90f);

            serializedProfile.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static void ScaleClamped(SerializedProperty property, float scale, float min, float max)
        {
            if (property == null) return;
            property.floatValue = Mathf.Clamp(property.floatValue * scale, min, max);
        }
    }
}
