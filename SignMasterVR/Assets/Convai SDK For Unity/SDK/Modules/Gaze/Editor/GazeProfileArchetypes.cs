using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using UnityEditor;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     Authored gaze "personalities" for the <see cref="ConvaiGazeProfile" /> inspector: one
    ///     click fills the whole per-state policy table plus the idle-life, blink-cadence, and
    ///     face-scan feel with a coherent character. Editor-only — no runtime surface. Every
    ///     value is inside the profile's own validation ranges by construction, so an applied
    ///     archetype always survives <c>OnValidate</c>.
    /// </summary>
    internal static class GazeProfileArchetypes
    {
        /// <summary>One per-state policy row of an archetype.</summary>
        internal readonly struct StateRow
        {
            public readonly DialogueState State;
            public readonly float Engagement;
            public readonly bool AllowPlayerTarget;
            public readonly float HeadContribution;
            public readonly bool AllowBodyTurn;
            public readonly GazeAversionMode AversionMode;
            public readonly float AversionStrength;
            public readonly float FixationLiveliness;

            public StateRow(
                DialogueState state, float engagement, bool allowPlayerTarget, float headContribution,
                bool allowBodyTurn, GazeAversionMode aversionMode, float aversionStrength, float fixationLiveliness)
            {
                State = state;
                Engagement = engagement;
                AllowPlayerTarget = allowPlayerTarget;
                HeadContribution = headContribution;
                AllowBodyTurn = allowBodyTurn;
                AversionMode = aversionMode;
                AversionStrength = aversionStrength;
                FixationLiveliness = fixationLiveliness;
            }
        }

        /// <summary>A complete authored personality: the state table plus idle-life feel.</summary>
        internal sealed class GazeArchetype
        {
            public string Name;
            public string Description;
            public StateRow[] States;
            public float AmbientYawRangeDegrees;
            public float AmbientIntervalMin;
            public float AmbientIntervalMax;
            public float AmbientHeadFollow;
            public float AmbientRecenterBias;
            public bool EnableCuriosityGlances;
            public float BlinkIntervalMean;
            public float FaceScanRadiusDegrees;
            public float PlanningBreakProbability;
        }

        /// <summary>
        ///     All shipped archetypes, in inspector-toolbar order. <b>Default</b> comes first and
        ///     is an exact mirror of <c>ConvaiGazeProfile</c>'s own field initializers and
        ///     <c>BuildDefaultStatePolicies()</c> — so a freshly created profile always shows one
        ///     pill already active, and "put it back the way it was" is one click rather than a
        ///     thing the user has to reconstruct by hand. <see cref="GazeProfileDefaultParityTests" />
        ///     fails if the two ever drift apart.
        /// </summary>
        internal static readonly GazeArchetype[] All =
        {
            new()
            {
                Name = "Default",
                Description = "The SDK's shipped tuning — a balanced, conversational baseline. " +
                              "Click to return a profile to it.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.35f, false, GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Attending,   0.9f,  true,  0.85f, true,  GazeAversionMode.Natural,   0.15f, 1f),
                    new StateRow(DialogueState.Listening,   0.95f, true,  0.85f, true,  GazeAversionMode.Natural,   0.08f, 1.1f),
                    new StateRow(DialogueState.Thinking,    0.7f,  true,  0.6f,  false, GazeAversionMode.Cognitive, 0.7f,  1.3f),
                    new StateRow(DialogueState.Speaking,    1f,    true,  0.85f, true,  GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Reacting,    1f,    true,  0.9f,  true,  GazeAversionMode.None,      0f,    1.2f),
                    new StateRow(DialogueState.Interrupted, 0.95f, true,  0.9f,  true,  GazeAversionMode.None,      0f,    1.1f),
                    new StateRow(DialogueState.Settling,    0.6f,  true,  0.6f,  false, GazeAversionMode.Natural,   0.25f, 0.9f),
                },
                AmbientYawRangeDegrees = 26f,
                AmbientIntervalMin = 1.7f,
                AmbientIntervalMax = 4.6f,
                AmbientHeadFollow = 0.35f,
                AmbientRecenterBias = 0.35f,
                EnableCuriosityGlances = true,
                BlinkIntervalMean = 4.2f,
                FaceScanRadiusDegrees = 2.2f,
                PlanningBreakProbability = 0.7f,
            },
            new()
            {
                Name = "Confident",
                Description = "High, steady eye contact with minimal aversion — a self-assured presence.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.35f, false, GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Attending,   0.95f, true,  0.9f,  true,  GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Listening,   1f,    true,  0.9f,  true,  GazeAversionMode.Natural,   0.03f, 1f),
                    new StateRow(DialogueState.Thinking,    0.85f, true,  0.7f,  false, GazeAversionMode.Cognitive, 0.45f, 1.1f),
                    new StateRow(DialogueState.Speaking,    1f,    true,  0.9f,  true,  GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Reacting,    1f,    true,  0.95f, true,  GazeAversionMode.None,      0f,    1.1f),
                    new StateRow(DialogueState.Interrupted, 1f,    true,  0.9f,  true,  GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Settling,    0.7f,  true,  0.6f,  false, GazeAversionMode.Natural,   0.1f,  0.9f),
                },
                AmbientYawRangeDegrees = 18f,
                AmbientIntervalMin = 2f,
                AmbientIntervalMax = 5f,
                AmbientHeadFollow = 0.3f,
                AmbientRecenterBias = 0.45f,
                EnableCuriosityGlances = false,
                BlinkIntervalMean = 4.6f,
                FaceScanRadiusDegrees = 1.8f,
                PlanningBreakProbability = 0.35f,
            },
            new()
            {
                Name = "Warm",
                Description = "High contact with natural micro-breaks and a livelier, roving face scan — friendly and engaged.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.4f,  false, GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Attending,   0.9f,  true,  0.85f, true,  GazeAversionMode.Natural,   0.18f, 1.15f),
                    new StateRow(DialogueState.Listening,   0.95f, true,  0.85f, true,  GazeAversionMode.Natural,   0.12f, 1.25f),
                    new StateRow(DialogueState.Thinking,    0.72f, true,  0.6f,  false, GazeAversionMode.Cognitive, 0.6f,  1.3f),
                    new StateRow(DialogueState.Speaking,    0.98f, true,  0.85f, true,  GazeAversionMode.Natural,   0.1f,  1.15f),
                    new StateRow(DialogueState.Reacting,    1f,    true,  0.9f,  true,  GazeAversionMode.None,      0f,    1.25f),
                    new StateRow(DialogueState.Interrupted, 0.95f, true,  0.9f,  true,  GazeAversionMode.None,      0f,    1.15f),
                    new StateRow(DialogueState.Settling,    0.6f,  true,  0.6f,  false, GazeAversionMode.Natural,   0.28f, 1f),
                },
                AmbientYawRangeDegrees = 24f,
                AmbientIntervalMin = 1.6f,
                AmbientIntervalMax = 4.4f,
                AmbientHeadFollow = 0.4f,
                AmbientRecenterBias = 0.35f,
                EnableCuriosityGlances = true,
                BlinkIntervalMean = 3.8f,
                FaceScanRadiusDegrees = 2.6f,
                PlanningBreakProbability = 0.65f,
            },
            new()
            {
                Name = "Shy",
                Description = "Lower engagement, frequent contact breaks, and more idle wandering — reserved and easily overwhelmed.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.3f,  false, GazeAversionMode.None,      0f,    1.1f),
                    new StateRow(DialogueState.Attending,   0.6f,  true,  0.55f, false, GazeAversionMode.Natural,   0.45f, 1.2f),
                    new StateRow(DialogueState.Listening,   0.7f,  true,  0.6f,  false, GazeAversionMode.Natural,   0.4f,  1.3f),
                    new StateRow(DialogueState.Thinking,    0.5f,  true,  0.5f,  false, GazeAversionMode.Cognitive, 0.8f,  1.4f),
                    new StateRow(DialogueState.Speaking,    0.8f,  true,  0.65f, false, GazeAversionMode.Natural,   0.35f, 1.2f),
                    new StateRow(DialogueState.Reacting,    0.85f, true,  0.7f,  true,  GazeAversionMode.Natural,   0.1f,  1.3f),
                    new StateRow(DialogueState.Interrupted, 0.8f,  true,  0.7f,  false, GazeAversionMode.Natural,   0.25f, 1.2f),
                    new StateRow(DialogueState.Settling,    0.45f, true,  0.5f,  false, GazeAversionMode.Natural,   0.5f,  1f),
                },
                AmbientYawRangeDegrees = 32f,
                AmbientIntervalMin = 1.3f,
                AmbientIntervalMax = 3.6f,
                AmbientHeadFollow = 0.3f,
                AmbientRecenterBias = 0.3f,
                EnableCuriosityGlances = false,
                BlinkIntervalMean = 3.5f,
                FaceScanRadiusDegrees = 2.4f,
                PlanningBreakProbability = 0.8f,
            },
            new()
            {
                Name = "Stoic",
                Description = "A steady head and low liveliness with sparse blinks — the eyes do the work, the head barely moves.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.2f,  false, GazeAversionMode.None,      0f,    0.8f),
                    new StateRow(DialogueState.Attending,   0.9f,  true,  0.4f,  false, GazeAversionMode.None,      0f,    0.75f),
                    new StateRow(DialogueState.Listening,   0.92f, true,  0.4f,  false, GazeAversionMode.None,      0f,    0.8f),
                    new StateRow(DialogueState.Thinking,    0.75f, true,  0.35f, false, GazeAversionMode.Cognitive, 0.4f,  0.85f),
                    new StateRow(DialogueState.Speaking,    1f,    true,  0.45f, false, GazeAversionMode.None,      0f,    0.75f),
                    new StateRow(DialogueState.Reacting,    0.95f, true,  0.5f,  false, GazeAversionMode.None,      0f,    0.85f),
                    new StateRow(DialogueState.Interrupted, 0.92f, true,  0.45f, false, GazeAversionMode.None,      0f,    0.8f),
                    new StateRow(DialogueState.Settling,    0.65f, true,  0.35f, false, GazeAversionMode.Natural,   0.08f, 0.75f),
                },
                AmbientYawRangeDegrees = 12f,
                AmbientIntervalMin = 2.8f,
                AmbientIntervalMax = 6.5f,
                AmbientHeadFollow = 0.15f,
                AmbientRecenterBias = 0.6f,
                EnableCuriosityGlances = false,
                BlinkIntervalMean = 6.5f,
                FaceScanRadiusDegrees = 1.5f,
                PlanningBreakProbability = 0.2f,
            },
            new()
            {
                Name = "Attentive",
                Description = "Listener-tuned: maximum engagement while listening, aware and curious, ready to acknowledge.",
                States = new[]
                {
                    new StateRow(DialogueState.Idle,        0f,    false, 0.4f,  false, GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Attending,   0.95f, true,  0.9f,  true,  GazeAversionMode.Natural,   0.05f, 1.1f),
                    new StateRow(DialogueState.Listening,   1f,    true,  0.92f, true,  GazeAversionMode.Natural,   0.03f, 1.15f),
                    new StateRow(DialogueState.Thinking,    0.75f, true,  0.65f, false, GazeAversionMode.Cognitive, 0.55f, 1.2f),
                    new StateRow(DialogueState.Speaking,    0.98f, true,  0.85f, true,  GazeAversionMode.None,      0f,    1f),
                    new StateRow(DialogueState.Reacting,    1f,    true,  0.92f, true,  GazeAversionMode.None,      0f,    1.2f),
                    new StateRow(DialogueState.Interrupted, 0.97f, true,  0.9f,  true,  GazeAversionMode.None,      0f,    1.1f),
                    new StateRow(DialogueState.Settling,    0.65f, true,  0.62f, false, GazeAversionMode.Natural,   0.2f,  0.95f),
                },
                AmbientYawRangeDegrees = 22f,
                AmbientIntervalMin = 1.8f,
                AmbientIntervalMax = 4.6f,
                AmbientHeadFollow = 0.38f,
                AmbientRecenterBias = 0.4f,
                EnableCuriosityGlances = true,
                BlinkIntervalMean = 4f,
                FaceScanRadiusDegrees = 2.2f,
                PlanningBreakProbability = 0.45f,
            },
        };

        /// <summary>
        ///     Writes <paramref name="archetype" /> into the profile's serialized fields. The
        ///     caller owns the surrounding <see cref="SerializedObject.Update" /> /
        ///     <see cref="SerializedObject.ApplyModifiedProperties" /> (so the change is a single
        ///     undoable step).
        /// </summary>
        internal static void Apply(SerializedObject serializedObject, GazeArchetype archetype)
        {
            if (serializedObject == null || archetype == null) return;

            SerializedProperty policies = GazeProfileSerializedPaths.Find(serializedObject, "statePolicies");
            policies.ClearArray();
            for (int i = 0; i < archetype.States.Length; i++)
            {
                policies.InsertArrayElementAtIndex(i);
                SerializedProperty element = policies.GetArrayElementAtIndex(i);
                StateRow row = archetype.States[i];
                element.FindPropertyRelative("State").enumValueIndex = (int)row.State;
                element.FindPropertyRelative("Engagement").floatValue = row.Engagement;
                element.FindPropertyRelative("AllowPlayerTarget").boolValue = row.AllowPlayerTarget;
                element.FindPropertyRelative("HeadContribution").floatValue = row.HeadContribution;
                element.FindPropertyRelative("AllowBodyTurn").boolValue = row.AllowBodyTurn;
                element.FindPropertyRelative("AversionMode").enumValueIndex = (int)row.AversionMode;
                element.FindPropertyRelative("AversionStrength").floatValue = row.AversionStrength;
                element.FindPropertyRelative("FixationLiveliness").floatValue = row.FixationLiveliness;
            }

            GazeProfileSerializedPaths.Find(serializedObject, "ambientYawRangeDegrees").floatValue = archetype.AmbientYawRangeDegrees;
            GazeProfileSerializedPaths.Find(serializedObject, "ambientIntervalMin").floatValue = archetype.AmbientIntervalMin;
            GazeProfileSerializedPaths.Find(serializedObject, "ambientIntervalMax").floatValue = archetype.AmbientIntervalMax;
            GazeProfileSerializedPaths.Find(serializedObject, "ambientHeadFollow").floatValue = archetype.AmbientHeadFollow;
            GazeProfileSerializedPaths.Find(serializedObject, "ambientRecenterBias").floatValue = archetype.AmbientRecenterBias;
            GazeProfileSerializedPaths.Find(serializedObject, "enableCuriosityGlances").boolValue = archetype.EnableCuriosityGlances;
            GazeProfileSerializedPaths.Find(serializedObject, "blinkIntervalMean").floatValue = archetype.BlinkIntervalMean;
            GazeProfileSerializedPaths.Find(serializedObject, "faceScanRadiusDegrees").floatValue = archetype.FaceScanRadiusDegrees;
            GazeProfileSerializedPaths.Find(serializedObject, "planningBreakProbability").floatValue = archetype.PlanningBreakProbability;
        }
    }
}
