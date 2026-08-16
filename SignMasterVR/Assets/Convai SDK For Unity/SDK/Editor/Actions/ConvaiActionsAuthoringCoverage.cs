using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>Where a serialized field's one obvious authoring home lives .</summary>
    internal enum ConvaiActionsAuthoringSurface
    {
        /// <summary>The Actions Editor window's default Actions mode (action list + detail panes).</summary>
        WindowActions = 0,

        /// <summary>The Actions Editor window's Scene Knowledge mode.</summary>
        WindowSceneKnowledge = 1,

        /// <summary>The Actions Editor window's Character Settings mode.</summary>
        WindowCharacterSettings = 2,

        /// <summary>The component's own Convai inspector.</summary>
        ComponentInspector = 3,

        /// <summary>Deliberately not authorable anywhere; requires a non-empty reason.</summary>
        Hidden = 4
    }

    /// <summary>One coverage claim: which surface authors a field, and why if hidden.</summary>
    internal readonly struct ConvaiActionsAuthoringEntry
    {
        internal ConvaiActionsAuthoringEntry(ConvaiActionsAuthoringSurface surface, string hiddenReason)
        {
            Surface = surface;
            HiddenReason = hiddenReason;
        }

        internal ConvaiActionsAuthoringSurface Surface { get; }

        /// <summary>Non-empty explanation, required when <see cref="Surface" /> is Hidden.</summary>
        internal string HiddenReason { get; }
    }

    /// <summary>
    ///     The parity map behind the "never again" guarantee: every serialized
    ///     field of every user-addable Actions component must be claimed by exactly one authoring
    ///     surface (a window mode, the component's Convai inspector, or an explicit
    ///     Hidden-with-reason entry). <c>ActionsAuthoringCoverageGuardTests</c> fails the build the
    ///     moment a new serialized field appears on a mapped component without a claim here — so
    ///     add the field's authoring UI (or a deliberate Hidden reason) in the same change.
    ///     Executors are exempt: everything deriving from <see cref="ConvaiActionExecutorBase" />
    ///     is auto-covered by the fallback Action Behavior inspector.
    /// </summary>
    internal static class ConvaiActionsAuthoringCoverage
    {
        /// <summary>Per-type, per-serialized-field-name authoring claims.</summary>
        internal static readonly IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ConvaiActionsAuthoringEntry>> Map =
            BuildMap();

        private static ConvaiActionsAuthoringEntry At(ConvaiActionsAuthoringSurface surface) =>
            new(surface, null);

        /// <summary>A field with no authoring home anywhere, and the reason that is correct.</summary>
        private static ConvaiActionsAuthoringEntry Hidden(string reason) =>
            new(ConvaiActionsAuthoringSurface.Hidden, reason);

        /// <summary>Why every one of the Debug Probe's recorded counters has no authoring UI.</summary>
        private const string ProbeReadout =
            "Recorded by the probe while it runs, never authored: the Activity section of its " +
            "inspector reads it back and the Clear button resets it.";

        private static IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ConvaiActionsAuthoringEntry>> BuildMap()
        {
            const ConvaiActionsAuthoringSurface windowActions = ConvaiActionsAuthoringSurface.WindowActions;
            const ConvaiActionsAuthoringSurface sceneKnowledge = ConvaiActionsAuthoringSurface.WindowSceneKnowledge;
            const ConvaiActionsAuthoringSurface characterSettings = ConvaiActionsAuthoringSurface.WindowCharacterSettings;
            const ConvaiActionsAuthoringSurface inspector = ConvaiActionsAuthoringSurface.ComponentInspector;

            return new Dictionary<Type, IReadOnlyDictionary<string, ConvaiActionsAuthoringEntry>>
            {
                [typeof(ConvaiActionConfigSource)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_actionSets"] = At(windowActions),
                    ["_definitions"] = At(windowActions),
                    ["_objects"] = At(sceneKnowledge),
                    ["_characters"] = At(sceneKnowledge),
                    ["_initialAttentionObject"] = At(sceneKnowledge),
                    // Sits with the dispatcher settings it governs: Character Settings is where the
                    // "no dispatcher on this character" panel appears, so the declaration that makes
                    // that panel expected or irrelevant belongs in the same place.
                    ["_actionExecutionMode"] = At(characterSettings),
                    // Authored from this component's own inspector: the behaviors strip at its
                    // bottom edge picks, copies, clears and creates the host object, and the notice
                    // above the buttons offers one once there are enough behaviors to want it.
                    ["_behaviorHost"] = At(inspector)
                },
                [typeof(ConvaiActionSet)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_definitions"] = At(windowActions)
                },
                // The nested definition type is guarded field-by-field too, so a new serialized
                // field on it (like the action-availability flag) cannot ship without an authoring
                // claim. Every field is authored in the window's Actions mode: name/description/
                // enabled in the Command card, executor + type hint in the Scene Behavior card,
                // parameters/targets/timing in Advanced.
                [typeof(ConvaiActionDefinition)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["ActionName"] = At(windowActions),
                    ["Description"] = At(windowActions),
                    ["Parameters"] = At(windowActions),
                    ["TargetRequirement"] = At(windowActions),
                    ["Executor"] = At(windowActions),
                    ["ExecutorTypeHint"] = At(windowActions),
                    ["TimeoutSeconds"] = At(windowActions),
                    ["AnswerDelivery"] = At(windowActions),
                    ["FailurePolicyOverride"] = At(windowActions),
                    ["WaitForBotSpeech"] = At(windowActions),
                    ["DelayAfterBotSpeechSeconds"] = At(windowActions),
                    ["_disabled"] = At(windowActions),

                    // Filed from the row's right-click menu, the bulk card, a category header's menu
                    // or a drag — every one of them inside the window's Actions mode.
                    ["_category"] = At(windowActions)
                },
                // The two nested Scene Knowledge entry types. They are the Known Objects / Known
                // Characters lists' actual editing surface, and they were absent from this map for
                // exactly as long as their GameObject link was absent from the UI — a serialized
                // field with nowhere to author it is the failure this map exists to make impossible,
                // so the types themselves are guarded now, not just the components holding them.
                [typeof(ConvaiActionObjectDefinition)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["<Name>k__BackingField"] = At(sceneKnowledge),
                    ["<Description>k__BackingField"] = At(sceneKnowledge),
                    ["<GameObjectReference>k__BackingField"] = At(sceneKnowledge),
                    ["<TextOnly>k__BackingField"] = At(sceneKnowledge),
                    ["<Aliases>k__BackingField"] = At(sceneKnowledge),
                    ["<InteractionPoint>k__BackingField"] = At(sceneKnowledge)
                },
                [typeof(ConvaiActionCharacterDefinition)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["<Name>k__BackingField"] = At(sceneKnowledge),
                    ["<Bio>k__BackingField"] = At(sceneKnowledge),
                    ["<GameObjectReference>k__BackingField"] = At(sceneKnowledge),
                    ["<TextOnly>k__BackingField"] = At(sceneKnowledge),
                    ["<Aliases>k__BackingField"] = At(sceneKnowledge),
                    ["<InteractionPoint>k__BackingField"] = At(sceneKnowledge)
                },
                [typeof(ConvaiActionDispatcher)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_batchPolicy"] = At(characterSettings),
                    ["_failurePolicy"] = At(characterSettings),
                    ["_speechGateTimeoutSeconds"] = At(characterSettings),
                    ["_defaultStepTimeoutSeconds"] = At(characterSettings),
                    ["_cancelOnUserSpeech"] = At(characterSettings),
                    ["_enablePerformanceReactions"] = At(characterSettings),
                    ["_onBatchStarted"] = At(inspector),
                    ["_onStepStarted"] = At(inspector),
                    ["_onStepSucceeded"] = At(inspector),
                    ["_onStepFailed"] = At(inspector),
                    ["_onStepUnhandled"] = At(inspector),
                    ["_onStepCompleted"] = At(inspector),
                    ["_onBatchCompleted"] = At(inspector),
                    ["_onBatchAborted"] = At(inspector)
                },
                [typeof(ConvaiActionFeedbackRelay)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_failureFeedbackMode"] = At(characterSettings),
                    ["_successFeedbackMode"] = At(characterSettings),
                    ["_droppedCommandFeedbackMode"] = At(characterSettings),
                    ["_cooldownSeconds"] = At(characterSettings),
                    ["_scriptedFailureLines"] = At(inspector),
                    ["_scriptedSuccessLine"] = At(inspector)
                },
                [typeof(ConvaiActionTarget)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_targetName"] = At(inspector),
                    ["_kind"] = At(inspector),
                    ["_description"] = At(inspector),
                    ["_bio"] = At(inspector),
                    ["_aliases"] = At(inspector),
                    ["_interactionPoint"] = At(inspector),
                    ["_applyTo"] = At(inspector),
                    ["_specificCharacters"] = At(inspector),
                    ["_registerOnEnable"] = At(inspector)
                },
                [typeof(ConvaiActionDebugProbe)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_character"] = At(inspector),
                    ["_dispatcher"] = At(inspector),
                    ["_logToConsole"] = At(inspector),

                    // The probe's readout. Recorded while it runs, never authored: the Activity
                    // section of its inspector reads these back and the Clear button resets them, so
                    // there is deliberately nothing here to type into. Serialized only so a domain
                    // reload does not erase what the last run reported.
                    ["_receivedBatchCount"] = Hidden(ProbeReadout),
                    ["_startedStepCount"] = Hidden(ProbeReadout),
                    ["_succeededStepCount"] = Hidden(ProbeReadout),
                    ["_failedStepCount"] = Hidden(ProbeReadout),
                    ["_unhandledStepCount"] = Hidden(ProbeReadout),
                    ["_completedStepCount"] = Hidden(ProbeReadout),
                    ["_abortedBatchCount"] = Hidden(ProbeReadout),
                    ["_lastReceivedBatch"] = Hidden(ProbeReadout),
                    ["_lastStepStarted"] = Hidden(ProbeReadout),
                    ["_lastStepSucceeded"] = Hidden(ProbeReadout),
                    ["_lastUnhandledStep"] = Hidden(ProbeReadout),
                    ["_lastStepCompleted"] = Hidden(ProbeReadout),
                    ["_lastFailedStepDetail"] = Hidden(ProbeReadout),
                    ["_lastFailureReason"] = Hidden(ProbeReadout)
                },
                [typeof(ConvaiActionTargetGroup)] = new Dictionary<string, ConvaiActionsAuthoringEntry>
                {
                    ["_groupName"] = At(inspector),
                    ["_description"] = At(inspector),
                    ["_members"] = At(inspector),
                    ["_isOrdered"] = At(inspector),
                    ["_registerOnEnable"] = At(inspector)
                }
            };
        }

        /// <summary>
        ///     Every serialized field a component carries: public or
        ///     <see cref="SerializeField" />-attributed non-public instance fields, declared anywhere
        ///     up the hierarchy below <see cref="MonoBehaviour" /> / <see cref="ScriptableObject" />,
        ///     excluding only <see cref="NonSerializedAttribute" /> ones. This is the set the
        ///     coverage map must account for.
        /// </summary>
        /// <remarks>
        ///     <see cref="HideInInspector" /> is deliberately <em>not</em> a filter here.
        ///     Hiding a field is itself an authoring decision — it is exactly the
        ///     <see cref="ConvaiActionsAuthoringSurface.Hidden" /> claim, which owes a reason — so
        ///     letting the attribute drop a field out of the guarded set would turn one attribute
        ///     into a way to ship a serialized field nobody ever has to justify. Use
        ///     <see cref="GetAuthorableSerializedFields" /> for the narrower "what Unity draws"
        ///     question.
        /// </remarks>
        internal static List<FieldInfo> GetGuardedSerializedFields(Type componentType) =>
            CollectSerializedFields(componentType, includeHiddenInInspector: true);

        /// <summary>
        ///     The subset of <see cref="GetGuardedSerializedFields" /> that Unity actually draws:
        ///     the same fields minus the <see cref="HideInInspector" /> ones. This is the set that
        ///     owes the user hover help, since a field nobody sees cannot explain itself.
        /// </summary>
        internal static List<FieldInfo> GetAuthorableSerializedFields(Type componentType) =>
            CollectSerializedFields(componentType, includeHiddenInInspector: false);

        private static List<FieldInfo> CollectSerializedFields(Type componentType, bool includeHiddenInInspector)
        {
            var fields = new List<FieldInfo>();
            for (Type type = componentType;
                 type != null &&
                 type != typeof(MonoBehaviour) &&
                 type != typeof(ScriptableObject) &&
                 type != typeof(object);
                 type = type.BaseType)
            {
                FieldInfo[] declared = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in declared)
                {
                    if (field.IsDefined(typeof(NonSerializedAttribute), inherit: false))
                        continue;

                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), inherit: false))
                        continue;

                    if (!includeHiddenInInspector && field.IsDefined(typeof(HideInInspector), inherit: false))
                        continue;

                    fields.Add(field);
                }
            }

            return fields;
        }
    }
}
