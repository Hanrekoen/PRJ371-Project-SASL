using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     A single resolved action step handed to an <see cref="IConvaiActionExecutor" />:
    ///     the backend command, its matched local definition, the resolved target, and batch context.
    /// </summary>
    public sealed class ConvaiActionInvocation
    {
        /// <summary>Structured backend command for this step.</summary>
        public ConvaiActionCommand Command { get; }

        /// <summary>Matched local action definition; null when the action is unknown.</summary>
        public ConvaiActionDefinition Definition { get; }

        /// <summary>Resolved object or character target; null when unresolved or not required.</summary>
        public ConvaiResolvedActionTarget ResolvedTarget { get; }

        /// <summary>Character this invocation executes on.</summary>
        public ConvaiCharacter Character { get; }

        /// <summary>Monotonic index of the batch this step belongs to.</summary>
        public int BatchIndex { get; }

        /// <summary>Zero-based index of this step within its batch.</summary>
        public int StepIndex { get; }

        internal ConvaiActionInvocation(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiResolvedActionTarget resolvedTarget,
            ConvaiCharacter character,
            int batchIndex,
            int stepIndex)
        {
            Command = command;
            Definition = definition;
            ResolvedTarget = resolvedTarget;
            Character = character;
            BatchIndex = batchIndex;
            StepIndex = stepIndex;
        }

        /// <inheritdoc />
        public override string ToString() =>
            $"[{BatchIndex}:{StepIndex}] {Command} (def={Definition?.ActionName ?? "?"})";

        /// <summary>Attempts to read a typed parameter by name (case-insensitive).</summary>
        public bool TryGetParameter(string name, out ConvaiActionParameterValue value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(name) || Command?.Parameters == null)
                return false;

            return Command.Parameters.TryGetValue(name.Trim(), out value);
        }

        /// <summary>Reads a string parameter, returning <paramref name="fallback" /> when absent.</summary>
        public string GetString(string name, string fallback = "") =>
            TryGetParameter(name, out ConvaiActionParameterValue value)
                ? value?.StringValue ?? fallback
                : fallback;

        /// <summary>Reads a numeric parameter, returning <paramref name="fallback" /> when absent.</summary>
        public float GetNumber(string name, float fallback = 0f) =>
            TryGetParameter(name, out ConvaiActionParameterValue value)
                ? value?.NumberValue ?? fallback
                : fallback;

        /// <summary>Reads a boolean parameter, returning <paramref name="fallback" /> when absent.</summary>
        public bool GetBool(string name, bool fallback = false) =>
            TryGetParameter(name, out ConvaiActionParameterValue value)
                ? value?.BoolValue ?? fallback
                : fallback;

        /// <summary>
        ///     Resolves a reference parameter against the character's action config,
        ///     falling back to the definition's target requirement when the parameter carries no explicit kind.
        /// </summary>
        public ConvaiResolvedActionTarget GetReference(string name)
        {
            if (!TryGetParameter(name, out ConvaiActionParameterValue value))
                return null;

            Vector3? origin = Character != null ? Character.transform.position : (Vector3?)null;
            return ConvaiActionTargetReferenceResolver.Resolve(
                value,
                (Character as IConvaiActionResolutionSource)?.ResolutionActionConfig ?? Character?.ActionConfig,
                Definition?.TargetRequirement,
                origin);
        }
    }

    /// <summary>
    ///     Shared resolution of reference parameter values against an action config.
    /// </summary>
    internal static class ConvaiActionTargetReferenceResolver
    {
        /// <summary>
        ///     Resolves a parameter value to a target: prefers the backend-resolved reference name and kind,
        ///     then falls back to the raw string value and the supplied target requirement.
        /// </summary>
        internal static ConvaiResolvedActionTarget Resolve(
            ConvaiActionParameterValue value,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement? fallbackRequirement,
            Vector3? origin = null)
        {
            if (value == null)
                return null;

            string referenceName = value.ResolvedReference?.Name;
            if (string.IsNullOrWhiteSpace(referenceName))
                referenceName = value.StringValue;

            ConvaiActionTargetKind kind = value.ResolvedReference?.Kind ?? ConvaiActionTargetKind.None;
            return kind != ConvaiActionTargetKind.None
                ? ConvaiResolvedActionTarget.Resolve(referenceName, actionConfig, kind, origin)
                : ConvaiResolvedActionTarget.Resolve(referenceName, actionConfig, fallbackRequirement, origin);
        }
    }
}
