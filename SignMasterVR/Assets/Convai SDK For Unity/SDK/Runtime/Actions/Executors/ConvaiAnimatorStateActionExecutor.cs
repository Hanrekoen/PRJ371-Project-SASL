using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>One "action name → Animator trigger" row for <see cref="ConvaiAnimatorStateActionExecutor" />.</summary>
    [Serializable]
    internal sealed class ConvaiAnimatorActionBinding
    {
        [Tooltip("The action name to answer, spelled the way it is in the Action Set. Capitalisation does not matter.")]
        public string ActionName;

        [Tooltip("The Trigger parameter to set on the Animator when that action runs.")]
        public string TriggerName;

        [Tooltip("Optional. The Animator state tag to wait for before the action counts as finished — " +
                 "tag the state in the Animator with the same word. Leave empty to finish as soon as " +
                 "the trigger is set.")]
        public string WaitForStateTag;

        [Range(0f, 1f)]
        [Tooltip("How far through the tagged state counts as finished. Only used when a state tag is set above.")]
        public float NormalizedExitTime = 0.95f;
    }

    /// <summary>
    ///     <b>For characters that animate with their own Animator Controller, not with the Convai
    ///     Body Animation module.</b> Sets one of the Animator's Trigger parameters, and can wait for
    ///     the resulting state to finish. It needs nothing but the Animator that is already on the
    ///     character — no Convai animation content, no extra module.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Which one do I want?</b> If your character has a
    ///         <c>ConvaiBodyAnimationController</c>, you want <c>Play Gesture</c> instead: it plays
    ///         clips from the character's Animation Set and blends them with everything else the body
    ///         is doing. This behavior talks to a plain Animator, which the Body Animation module
    ///         drives itself — the two would fight over the same rig. It exists for the many projects
    ///         that bring their own animation setup and still want the character to act.
    ///     </para>
    ///     <para>
    ///         An action name with no row in the list is reported as
    ///         <see cref="ConvaiActionExecutionResult.Unhandled" /> rather than failed, so another
    ///         Action Behavior on the character still gets its chance to answer it.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Play Animator State (own Animator Controller)")]
    [ConvaiActionArchetype(
        "Play Animator State",
        ActionName = "Play Animator State",
        Description = "Sets a Trigger on the character's own Animator Controller, and can wait for " +
                      "the animation to finish. For characters that do NOT use the Convai Body " +
                      "Animation module — those want Play Gesture instead.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        RequiredPeerHint = "Animator")]
    public sealed class ConvaiAnimatorStateActionExecutor : ConvaiCharacterActionExecutor<Animator>
    {
        /// <summary>
        ///     How long to wait for a tagged state before giving up. A safety net, not a tuning knob:
        ///     without it a mistyped tag or an Animator transition that never happens would hold the
        ///     action open until the whole batch times out, with nothing pointing at the cause.
        /// </summary>
        private const float StateWaitTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("Which action plays which Animator Trigger. Add one row per action you want this " +
                 "character to be able to perform.")]
        private List<ConvaiAnimatorActionBinding> _bindings = new();

        private Dictionary<string, ConvaiAnimatorActionBinding> _lookup;
        private int _lookupBuiltForCount = -1;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            Animator animator,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            string actionName = invocation?.Definition?.ActionName ?? invocation?.Command?.Name;
            if (!TryGetBinding(actionName, out ConvaiAnimatorActionBinding binding))
                return ConvaiActionExecutionResult.Unhandled(DescribeMissingBinding(actionName));

            animator.SetTrigger(binding.TriggerName);

            if (string.IsNullOrWhiteSpace(binding.WaitForStateTag))
                return ConvaiActionExecutionResult.Succeeded();

            string stateTag = binding.WaitForStateTag;
            float normalizedExitTime = binding.NormalizedExitTime;
            bool finished = await ConvaiActionAsyncUtility.WaitUntilAsync(
                () => IsTaggedStateAtOrPastExit(animator, stateTag, normalizedExitTime),
                cancellationToken,
                StateWaitTimeoutSeconds);

            return finished
                ? ConvaiActionExecutionResult.Succeeded()
                : ConvaiActionExecutionResult.Failed(
                    $"The Animator state tagged '{stateTag}' did not play within {StateWaitTimeoutSeconds:0} seconds. " +
                    "Check that a state with that tag is reachable from the trigger.",
                    ConvaiActionFailureReason.Timeout);
        }

        /// <summary>
        ///     Drops the cached lookup after any Inspector edit, and clamps each row's exit time. The
        ///     <c>Range</c> attribute only constrains the Inspector slider, so a script or a stale
        ///     asset can still write a value outside 0–1.
        /// </summary>
        private void OnValidate()
        {
            _lookup = null;

            if (_bindings == null)
                return;

            for (int i = 0; i < _bindings.Count; i++)
            {
                ConvaiAnimatorActionBinding binding = _bindings[i];
                if (binding != null)
                    binding.NormalizedExitTime = Mathf.Clamp01(binding.NormalizedExitTime);
            }
        }

        /// <summary>
        ///     Explains an unmatched action name — and, when the character clearly animates through
        ///     the Body Animation module, says so and names the behavior that belongs on it instead.
        /// </summary>
        /// <remarks>
        ///     This is the moment the author finds out they picked the wrong one of two similar
        ///     behaviors, so it is worth spending a sentence on. The module is detected by component
        ///     name rather than by type: <c>Convai.Runtime</c> cannot reference a module assembly,
        ///     and this check runs only on the decline path, so the string comparison costs nothing
        ///     that matters.
        /// </remarks>
        private string DescribeMissingBinding(string actionName)
        {
            string basicMessage = $"No Animator trigger is set up for the action '{actionName}'.";

            Component[] peers = GetComponentsInParent<Component>(true);
            for (int i = 0; i < peers.Length; i++)
            {
                if (peers[i] == null || peers[i].GetType().Name != BodyAnimationControllerTypeName)
                    continue;

                return basicMessage +
                       " This character animates through the Convai Body Animation module, which drives " +
                       "its Animator itself — use the Play Gesture behavior instead. Play Animator State " +
                       "is for characters that bring their own Animator Controller.";
            }

            return basicMessage + " Add a row mapping this action name to one of the Animator's Trigger parameters.";
        }

        /// <summary>Name of the Body Animation controller component, matched without referencing its assembly.</summary>
        private const string BodyAnimationControllerTypeName = "ConvaiBodyAnimationController";

        private bool TryGetBinding(string actionName, out ConvaiAnimatorActionBinding binding)
        {
            EnsureLookup();
            if (!string.IsNullOrWhiteSpace(actionName) && _lookup.TryGetValue(actionName.Trim(), out binding))
                return true;

            binding = null;
            return false;
        }

        private void EnsureLookup()
        {
            int bindingCount = _bindings?.Count ?? 0;
            if (_lookup != null && _lookupBuiltForCount == bindingCount)
                return;

            _lookup = new Dictionary<string, ConvaiAnimatorActionBinding>(bindingCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bindingCount; i++)
            {
                ConvaiAnimatorActionBinding candidate = _bindings[i];
                if (candidate == null ||
                    string.IsNullOrWhiteSpace(candidate.ActionName) ||
                    string.IsNullOrWhiteSpace(candidate.TriggerName))
                    continue;

                _lookup[candidate.ActionName.Trim()] = candidate;
            }

            _lookupBuiltForCount = bindingCount;
        }

        private static bool IsTaggedStateAtOrPastExit(Animator animator, string stateTag, float normalizedExitTime)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsTag(stateTag))
                return false;

            float progressThroughCurrentLoop = state.normalizedTime - Mathf.Floor(state.normalizedTime);
            return state.normalizedTime >= 1f || progressThroughCurrentLoop >= normalizedExitTime;
        }
    }
}
