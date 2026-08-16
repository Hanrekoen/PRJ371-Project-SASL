using System.Collections.Generic;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Answers "what is this command's target" — once, for everybody who asks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two places used to answer it independently: the response filter, deciding whether to
    ///         admit the command at all, and the dispatcher, deciding what to hand the executor. They
    ///         walked the same candidates in the same order and still disagreed in three ways — the
    ///         filter passed no origin, so with two same-named targets it judged the first while the
    ///         dispatcher walked to the nearest; the filter demanded a scene binding and the
    ///         dispatcher did not; and target-less actions were short-circuited by one and resolved
    ///         opportunistically by the other.
    ///     </para>
    ///     <para>
    ///         A gate that reaches a different conclusion from the thing it gates is not a bug that
    ///         gets fixed, it is a bug that gets re-introduced, because nothing makes the two drift
    ///         visible. So there is one ladder here and both callers climb it: admission and
    ///         execution cannot disagree, by construction rather than by discipline.
    ///     </para>
    ///     <para>
    ///         Candidate order, highest first: the command's own <c>Target</c> field, then the
    ///         implicit key enrichment parks a name under when the backend sent it inside the action
    ///         text, then the action's declared <c>Auto</c>/<c>Reference</c> parameters in authored
    ///         order. First one that satisfies the requirement wins.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionTargetResolution
    {
        /// <summary>
        ///     Resolves the target this command should act on.
        /// </summary>
        /// <param name="command">The enriched command.</param>
        /// <param name="definition">Its matched local definition; null means nothing is required.</param>
        /// <param name="actionConfig">The resolution view of what this character can act on.</param>
        /// <param name="origin">
        ///     Where the character is standing, used to break ties between same-named targets. Pass it
        ///     whenever it is known: omitting it silently changes which of two candidates wins.
        /// </param>
        /// <param name="target">The resolved target, or null.</param>
        /// <returns>
        ///     Whether the command may proceed: true when the requirement is satisfied, and true for
        ///     an action that requires nothing even if <paramref name="target" /> came back null.
        /// </returns>
        internal static bool TryResolve(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            Vector3? origin,
            out ConvaiResolvedActionTarget target)
        {
            ConvaiActionTargetRequirement requirement =
                definition?.TargetRequirement ?? ConvaiActionTargetRequirement.None;

            // An action that requires nothing still takes an explicit target when the backend named
            // one — a wave at somebody is better aimed than a wave at nobody — but never promotes one
            // of its own parameters into that role, and never fails for want of it.
            if (requirement == ConvaiActionTargetRequirement.None)
            {
                target = ConvaiResolvedActionTarget.Resolve(command?.Target, actionConfig, requirement, origin);
                return true;
            }

            // Two answers are tracked at once. The first candidate that satisfies the requirement
            // wins outright. Failing that, the first candidate that resolved *at all* is still
            // returned — because "you asked for a person and 'Sofia' is a statue" is a far more
            // useful thing for a caller to be able to say than "nothing resolved", and the caller
            // cannot say it if resolution throws the near miss away.
            ConvaiResolvedActionTarget nearMiss = null;

            target = Pick(ResolveExplicitTarget(command, actionConfig, requirement, origin), requirement, ref nearMiss)
                     ?? Pick(ResolveImplicitTarget(command, actionConfig, requirement, origin), requirement, ref nearMiss)
                     ?? PickFromParameters(command, definition, actionConfig, requirement, origin, ref nearMiss);

            if (target != null)
                return true;

            target = nearMiss;
            return false;
        }

        /// <summary>
        ///     Returns the candidate when it satisfies the requirement, otherwise remembers it as the
        ///     near miss and returns null so the search continues.
        /// </summary>
        private static ConvaiResolvedActionTarget Pick(
            ConvaiResolvedActionTarget candidate,
            ConvaiActionTargetRequirement requirement,
            ref ConvaiResolvedActionTarget nearMiss)
        {
            if (candidate == null)
                return null;

            if (SatisfiesRequirement(candidate, requirement))
                return candidate;

            nearMiss ??= candidate;
            return null;
        }

        /// <summary>The name the backend put in the command's own target field.</summary>
        private static ConvaiResolvedActionTarget ResolveExplicitTarget(
            ConvaiActionCommand command,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement requirement,
            Vector3? origin) =>
            ConvaiResolvedActionTarget.Resolve(command?.Target, actionConfig, requirement, origin);

        /// <summary>
        ///     The name the backend sent inside the action text rather than in its own field.
        /// </summary>
        /// <remarks>
        ///     <c>Walk To The Gallery</c> arrives as one string; enrichment strips the action name and
        ///     parks the rest under <see cref="ConvaiActionCommand.TargetParameterKey" />. An
        ///     action that declares no parameters — the ordinary shape of "walk to somewhere" — has
        ///     nothing in the loop below to look at, so a command was once dropped as target-less
        ///     while holding a perfectly good target.
        /// </remarks>
        private static ConvaiResolvedActionTarget ResolveImplicitTarget(
            ConvaiActionCommand command,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement requirement,
            Vector3? origin)
        {
            if (command?.Parameters == null ||
                !command.Parameters.TryGetValue(
                    ConvaiActionCommand.TargetParameterKey, out ConvaiActionParameterValue implicitTarget))
                return null;

            return ConvaiActionTargetReferenceResolver.Resolve(
                implicitTarget, actionConfig, requirement, origin);
        }

        /// <summary>The action's own Auto/Reference parameters, in authored order.</summary>
        private static ConvaiResolvedActionTarget PickFromParameters(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement requirement,
            Vector3? origin,
            ref ConvaiResolvedActionTarget nearMiss)
        {
            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition?.Parameters;
            if (parameters == null || command?.Parameters == null)
                return null;

            for (int i = 0; i < parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = parameters[i];
                if (parameter == null ||
                    parameter.Type is not (ConvaiActionParameterType.Auto or ConvaiActionParameterType.Reference))
                    continue;

                string parameterName = ConvaiActionParameterDefinition.Normalize(parameter.Name);
                if (parameterName.Length == 0 ||
                    !command.Parameters.TryGetValue(parameterName, out ConvaiActionParameterValue value))
                    continue;

                ConvaiResolvedActionTarget candidate = Pick(
                    ConvaiActionTargetReferenceResolver.Resolve(value, actionConfig, requirement, origin),
                    requirement,
                    ref nearMiss);
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        /// <summary>Whether a candidate is the kind of thing this action asked for.</summary>
        /// <remarks>
        ///     Deliberately says nothing about whether the target has a scene object behind it. What
        ///     a command <em>meant</em> and whether it can be <em>performed</em> are two questions,
        ///     and a resolver that conflates them takes the name away from every executor that only
        ///     needs the name — talking about a place, remembering it, referring to it. The
        ///     performability question belongs to whoever is about to perform: see
        ///     <see cref="IsActionable" />.
        /// </remarks>
        internal static bool SatisfiesRequirement(
            ConvaiResolvedActionTarget target,
            ConvaiActionTargetRequirement requirement)
        {
            if (target == null)
                return requirement == ConvaiActionTargetRequirement.None;

            return requirement switch
            {
                ConvaiActionTargetRequirement.None => true,
                ConvaiActionTargetRequirement.Object => target.Kind == ConvaiActionTargetKind.Object,
                ConvaiActionTargetRequirement.Character => target.Kind == ConvaiActionTargetKind.Character,
                ConvaiActionTargetRequirement.Either =>
                    target.Kind is ConvaiActionTargetKind.Object or ConvaiActionTargetKind.Character,
                _ => false
            };
        }

        /// <summary>
        ///     Whether there is something in the scene behind this target — the object itself, or an
        ///     interaction point standing in for it.
        /// </summary>
        /// <remarks>
        ///     The admission question, kept apart from the resolution question above. A command that
        ///     names a real entry with nothing built behind it resolved correctly and still cannot be
        ///     carried out, and the response filter refuses it on those grounds so it is reported as
        ///     an authoring gap rather than reaching a behavior and failing there — where the message
        ///     would read like a fault in the behavior.
        /// </remarks>
        internal static bool IsActionable(ConvaiResolvedActionTarget target) =>
            target != null && (target.GameObjectReference != null || target.InteractionPoint != null);

        /// <summary>
        ///     Resolves and additionally requires that the target can actually be acted on. Used by
        ///     the response filter, which must not admit a command no behavior could carry out.
        /// </summary>
        internal static ConvaiActionTargetOutcome ResolveForDispatch(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            Vector3? origin,
            out ConvaiResolvedActionTarget target)
        {
            // TryResolve reports a near miss through `target` while returning false, so the flag is
            // the authority here, not whether something came back.
            bool satisfied = TryResolve(command, definition, actionConfig, origin, out target);

            if (!satisfied)
                return target == null
                    ? ConvaiActionTargetOutcome.NothingMatched
                    : ConvaiActionTargetOutcome.WrongKind;

            // A target-less action is satisfied by nothing at all, so there is nothing to perform on
            // and nothing to check.
            if ((definition?.TargetRequirement ?? ConvaiActionTargetRequirement.None) ==
                ConvaiActionTargetRequirement.None)
                return ConvaiActionTargetOutcome.Resolved;

            return IsActionable(target)
                ? ConvaiActionTargetOutcome.Resolved
                : ConvaiActionTargetOutcome.NotInTheScene;
        }

        /// <summary>Convenience wrapper for callers that only need yes or no.</summary>
        internal static bool TryResolveActionable(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiActionConfig actionConfig,
            Vector3? origin,
            out ConvaiResolvedActionTarget target)
        {
            if (ResolveForDispatch(command, definition, actionConfig, origin, out target) ==
                ConvaiActionTargetOutcome.Resolved)
                return true;

            target = null;
            return false;
        }
    }

    /// <summary>
    ///     Why a command's target did or did not come out usable.
    /// </summary>
    /// <remarks>
    ///     Three ways of failing that look identical from outside — the action does nothing — and
    ///     have three different fixes. Telling them apart is the difference between a message that
    ///     names the repair and one that only confirms something is wrong.
    /// </remarks>
    internal enum ConvaiActionTargetOutcome
    {
        /// <summary>A usable target was found, or none was required.</summary>
        Resolved = 0,

        /// <summary>The name matched nothing this character offers. Fixed with an alias or a rename.</summary>
        NothingMatched = 1,

        /// <summary>
        ///     The name matched, but the wrong sort of thing — a prop where the action needs a
        ///     person, or the reverse. Fixed by pointing the action at the right entry, or by
        ///     changing what kind of target the action asks for.
        /// </summary>
        WrongKind = 2,

        /// <summary>
        ///     The name matched an entry that has nothing in the scene behind it. Fixed by linking a
        ///     GameObject to it — or, if that is deliberate, by marking the entry Text Only so the
        ///     character talks about it without being asked to act on it.
        /// </summary>
        NotInTheScene = 3
    }
}
