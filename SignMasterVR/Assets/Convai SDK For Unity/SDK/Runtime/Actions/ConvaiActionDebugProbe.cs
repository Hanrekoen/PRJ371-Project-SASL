using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Small runtime probe for validating action batches and local dispatcher behavior in-scene.
    /// </summary>
    [AddComponentMenu("Convai/Actions/Diagnostics/Convai Action Monitor")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConvaiCharacter))]
    public sealed class ConvaiActionDebugProbe : MonoBehaviour
    {
        // No [Header] on serialized fields: the Convai inspector groups these into its own
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Convai Character this probe watches. Auto-detected from this GameObject when left empty.")]
        private ConvaiCharacter _character;

        [SerializeField]
        [Tooltip("Action Runner this monitor listens to. Auto-detected from this GameObject when left empty.")]
        private ConvaiActionDispatcher _dispatcher;

        [SerializeField]
        [Tooltip("Also print every recorded event to the Console window as it happens.")]
        private bool _logToConsole = true;

        // Everything below is recorded by the probe while it runs, never authored: the Activity
        // section of its inspector is how it is read, and the Clear button is how it is reset. It
        // stays serialized so a domain reload does not erase what the last run reported, and is
        // hidden so the inspector cannot offer it as something to type into.
        //
        // Hidden rather than merely read-only, because these were drawn as plain fields — and the
        // six [TextArea] strings among them drew a stray, clipped prefix label ("Las…") in the
        // inspector's left margin: Unity's TextArea drawer resolves its own label rect, and inside
        // the Convai inspector frame that rect landed outside the frame, on the header row.
        [SerializeField, HideInInspector]
        private int _receivedBatchCount;

        [SerializeField, HideInInspector]
        private int _startedStepCount;

        [SerializeField, HideInInspector]
        private int _succeededStepCount;

        [SerializeField, HideInInspector]
        private int _failedStepCount;

        [SerializeField, HideInInspector]
        private int _unhandledStepCount;

        [SerializeField, HideInInspector]
        private int _completedStepCount;

        [SerializeField, HideInInspector]
        private int _abortedBatchCount;

        [SerializeField, HideInInspector]
        private string _lastReceivedBatch;

        [SerializeField, HideInInspector]
        private string _lastStepStarted;

        [SerializeField, HideInInspector]
        private string _lastStepSucceeded;

        [SerializeField, HideInInspector]
        private string _lastUnhandledStep;

        [SerializeField, HideInInspector]
        private string _lastStepCompleted;

        [SerializeField, HideInInspector]
        private string _lastFailedStepDetail;

        [SerializeField, HideInInspector]
        private string _lastFailureReason;

        private void Reset() => AutoResolveReferences();
        private void Awake() => AutoResolveReferences();
        private void OnValidate() => AutoResolveReferences();

        private void OnEnable()
        {
            AutoResolveReferences();

            if (_character != null)
                _character.OnActionsReceived += HandleActionsReceived;

            if (_dispatcher == null)
                return;

            _dispatcher.OnBatchStarted.AddListener(HandleBatchStarted);
            _dispatcher.OnStepStarted.AddListener(HandleStepStarted);
            _dispatcher.OnStepSucceeded.AddListener(HandleStepSucceeded);
            _dispatcher.OnStepFailed.AddListener(HandleStepFailed);
            _dispatcher.OnStepUnhandled.AddListener(HandleUnhandledStep);
            _dispatcher.OnStepCompleted.AddListener(HandleStepCompleted);
            _dispatcher.OnBatchCompleted.AddListener(HandleBatchCompleted);
            _dispatcher.OnBatchAborted.AddListener(HandleBatchAborted);
        }

        private void OnDisable()
        {
            if (_character != null)
                _character.OnActionsReceived -= HandleActionsReceived;

            if (_dispatcher == null)
                return;

            _dispatcher.OnBatchStarted.RemoveListener(HandleBatchStarted);
            _dispatcher.OnStepStarted.RemoveListener(HandleStepStarted);
            _dispatcher.OnStepSucceeded.RemoveListener(HandleStepSucceeded);
            _dispatcher.OnStepFailed.RemoveListener(HandleStepFailed);
            _dispatcher.OnStepUnhandled.RemoveListener(HandleUnhandledStep);
            _dispatcher.OnStepCompleted.RemoveListener(HandleStepCompleted);
            _dispatcher.OnBatchCompleted.RemoveListener(HandleBatchCompleted);
            _dispatcher.OnBatchAborted.RemoveListener(HandleBatchAborted);
        }

        /// <summary>
        ///     Sends this character its own first available action as a local batch, so the
        ///     dispatcher, the bound behavior, and every event this probe records can be exercised
        ///     without a backend turn.
        /// </summary>
        public void InjectTestBatch()
        {
            AutoResolveReferences();
            if (_dispatcher == null)
            {
                ConvaiLogger.Warning(
                    "[ConvaiActionDebugProbe] Cannot inject a test batch: this GameObject has no Convai Action Runner.",
                    LogCategory.Character);
                return;
            }

            // The character's own first action, not a fixed name: a hardcoded verb is unhandled on
            // every character that does not happen to define it, which makes the probe's very first
            // result a false negative.
            string actionName = ResolveTestActionName();
            if (string.IsNullOrEmpty(actionName))
            {
                ConvaiLogger.Warning(
                    $"[ConvaiActionDebugProbe] Cannot inject a test batch: '{name}' has no available actions to send.",
                    LogCategory.Character);
                return;
            }

            _dispatcher.EnqueueActions(new[] { new ConvaiActionCommand(actionName, ResolveTestTargetName()) });
        }

        public void ResetProbeState()
        {
            _receivedBatchCount = 0;
            _startedStepCount = 0;
            _succeededStepCount = 0;
            _failedStepCount = 0;
            _unhandledStepCount = 0;
            _completedStepCount = 0;
            _abortedBatchCount = 0;
            _lastReceivedBatch = string.Empty;
            _lastStepStarted = string.Empty;
            _lastStepSucceeded = string.Empty;
            _lastUnhandledStep = string.Empty;
            _lastStepCompleted = string.Empty;
            _lastFailedStepDetail = string.Empty;
            _lastFailureReason = string.Empty;
        }

        private void HandleActionsReceived(IReadOnlyList<ConvaiActionCommand> actions)
        {
            _receivedBatchCount++;
            _lastReceivedBatch = FormatBatch(actions);

            if (_logToConsole)
                ConvaiLogger.Debug($"[ConvaiActionDebugProbe] Received action batch #{_receivedBatchCount}: {_lastReceivedBatch}", LogCategory.Character);
        }

        private void HandleBatchStarted()
        {
            if (_logToConsole)
                ConvaiLogger.Debug("[ConvaiActionDebugProbe] Dispatcher batch started.", LogCategory.Character);
        }

        private void HandleStepStarted(ConvaiActionInvocation invocation)
        {
            _startedStepCount++;
            _lastStepStarted = FormatInvocation(invocation);

            if (_logToConsole)
                ConvaiLogger.Debug($"[ConvaiActionDebugProbe] Step started #{_startedStepCount}: {_lastStepStarted}", LogCategory.Character);
        }

        private void HandleStepSucceeded(ConvaiActionInvocation invocation)
        {
            _succeededStepCount++;
            _lastStepSucceeded = FormatInvocation(invocation);

            if (_logToConsole)
                ConvaiLogger.Debug($"[ConvaiActionDebugProbe] Step succeeded #{_succeededStepCount}: {_lastStepSucceeded}", LogCategory.Character);
        }

        private void HandleStepFailed(ConvaiActionInvocation invocation)
        {
            _failedStepCount++;
            _lastFailedStepDetail = FormatInvocation(invocation);

            if (_logToConsole)
                ConvaiLogger.Warning($"[ConvaiActionDebugProbe] Step failed #{_failedStepCount}: {_lastFailedStepDetail}", LogCategory.Character);
        }

        private void HandleUnhandledStep(ConvaiActionInvocation invocation)
        {
            _unhandledStepCount++;
            _lastUnhandledStep = FormatInvocation(invocation);

            if (_logToConsole)
                ConvaiLogger.Warning(
                    $"[ConvaiActionDebugProbe] Step unhandled #{_unhandledStepCount}: {_lastUnhandledStep}",
                    LogCategory.Character);
        }

        private void HandleStepCompleted(ConvaiActionStepReport report)
        {
            _completedStepCount++;
            _lastStepCompleted = FormatReport(report);
            _lastFailureReason = report?.FailureMessage ?? string.Empty;

            if (!_logToConsole)
                return;

            if (report == null || report.Result.Status == ConvaiActionExecutionStatus.Succeeded)
            {
                ConvaiLogger.Debug($"[ConvaiActionDebugProbe] Step completed #{_completedStepCount}: {_lastStepCompleted}", LogCategory.Character);
                return;
            }

            ConvaiLogger.Warning(
                $"[ConvaiActionDebugProbe] Step completed #{_completedStepCount}: {_lastStepCompleted}",
                LogCategory.Character);
        }

        private void HandleBatchCompleted()
        {
            if (_logToConsole)
                ConvaiLogger.Debug("[ConvaiActionDebugProbe] Dispatcher batch completed.", LogCategory.Character);
        }

        private void HandleBatchAborted()
        {
            _abortedBatchCount++;

            if (_logToConsole)
                ConvaiLogger.Warning($"[ConvaiActionDebugProbe] Dispatcher batch aborted #{_abortedBatchCount}.", LogCategory.Character);
        }

        private void AutoResolveReferences()
        {
            if (_character == null)
                _character = GetComponent<ConvaiCharacter>();

            if (_dispatcher == null)
                _dispatcher = GetComponent<ConvaiActionDispatcher>();
        }

        /// <summary>
        ///     The first action this character can actually perform: the live session catalog when
        ///     one exists, else the authored config, skipping anything currently unavailable so the
        ///     injected batch is not reported unhandled for a reason that has nothing to do with
        ///     what is being tested. Empty when the character declares no runnable action.
        /// </summary>
        private string ResolveTestActionName()
        {
            IReadOnlyList<ConvaiActionDefinition> definitions =
                _character?.GetRuntimeActionDefinitionCatalog();

            if (definitions == null || definitions.Count == 0)
                definitions = _character?.GetActionConfigSource()?.GetEffectiveDefinitions(requireExecutable: true);

            if (definitions == null)
                return string.Empty;

            for (int i = 0; i < definitions.Count; i++)
            {
                string actionName = ConvaiActionDefinition.NormalizeActionName(definitions[i]?.ActionName);
                if (string.IsNullOrEmpty(actionName))
                    continue;

                if (_character?.Actions?.IsActionAvailable(actionName) == false)
                    continue;

                return actionName;
            }

            return string.Empty;
        }

        private string ResolveTestTargetName()
        {
            ConvaiActionConfig actionConfig = _character?.GetRuntimeActionConfig();
            IReadOnlyList<ConvaiActionObjectDefinition> objects = actionConfig?.Objects;
            if (objects == null || objects.Count == 0)
                return string.Empty;

            for (int i = 0; i < objects.Count; i++)
            {
                string objectName = objects[i]?.Name;
                if (!string.IsNullOrWhiteSpace(objectName))
                    return objectName.Trim();
            }

            return string.Empty;
        }

        private static string FormatBatch(IReadOnlyList<ConvaiActionCommand> actions)
        {
            if (actions == null)
                return "<null>";

            if (actions.Count == 0)
                return "[]";

            var batch = new JArray();
            for (int i = 0; i < actions.Count; i++)
            {
                ConvaiActionCommand action = actions[i];
                var actionObject = new JObject
                {
                    ["name"] = action?.Name ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(action?.Target))
                    actionObject["target"] = action.Target;

                batch.Add(actionObject);
            }

            return batch.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string FormatInvocation(ConvaiActionInvocation invocation)
        {
            if (invocation == null)
                return "<null>";

            string targetKind = invocation.ResolvedTarget?.Kind.ToString() ?? "None";
            string targetName = invocation.ResolvedTarget?.Name ?? "<none>";
            string defName = invocation.Definition?.ActionName ?? "<unresolved>";
            return $"cmd='{invocation.Command}', def='{defName}', target={targetKind}:{targetName}";
        }

        private static string FormatReport(ConvaiActionStepReport report)
        {
            if (report == null)
                return "<null>";

            string invocation = FormatInvocation(report.Invocation);
            string abort = report.BatchAborted ? "abort" : "continue";
            string failure = string.IsNullOrWhiteSpace(report.FailureMessage)
                ? string.Empty
                : $", reason='{report.FailureMessage}'";
            return $"{invocation}, result={report.Result.Status}, batch={abort}{failure}";
        }
    }
}
