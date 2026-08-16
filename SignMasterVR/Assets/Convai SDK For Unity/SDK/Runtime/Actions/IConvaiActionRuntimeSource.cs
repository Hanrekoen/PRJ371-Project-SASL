using System.Collections.Generic;
using Convai.Shared.Actions;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Read side of a character's live action authoring: what the backend was told it can do
    ///     (<see cref="ActionConfig" />) and the local definitions that execute it. Implemented by
    ///     <c>ConvaiCharacter</c>; consumed by enrichment and dispatch so both always see the
    ///     session's effective configuration rather than raw inspector state.
    /// </summary>
    public interface IConvaiActionRuntimeSource
    {
        /// <summary>
        ///     Effective wire-facing action config for the current session (actions, objects,
        ///     characters, initial attention), or null when the character declares no actions.
        /// </summary>
        ConvaiActionConfig ActionConfig { get; }

        /// <summary>
        ///     Effective local action definitions (deduplicated, executable) used to enrich and
        ///     dispatch backend commands. Never null; empty when the character declares no actions.
        /// </summary>
        IReadOnlyList<ConvaiActionDefinition> ActionDefinitions { get; }
    }

    /// <summary>
    ///     Internal safety view containing every locally executable definition available for the
    ///     session, including definitions outside the confirmed request-level action subset.
    /// </summary>
    internal interface IConvaiActionDefinitionCatalogSource
    {
        IReadOnlyList<ConvaiActionDefinition> ActionDefinitionCatalog { get; }
    }

    /// <summary>
    ///     Internal local-resolution view: the confirmed <see cref="IConvaiActionRuntimeSource.ActionConfig" />
    ///     widened with this scene's local-only lookup aids — runtime target registrations, enabled
    ///     <c>ConvaiActionTarget</c> components, target groups, aliases, interaction points and
    ///     availability overrides. Consumed when translating a name the backend already used into a
    ///     scene object; never serialized, so it cannot desynchronize the character from the backend.
    /// </summary>
    internal interface IConvaiActionResolutionSource
    {
        ConvaiActionConfig ResolutionActionConfig { get; }

        /// <summary>
        ///     Where this character is standing, used to break ties between same-named targets.
        /// </summary>
        /// <remarks>
        ///     Part of the resolution view because resolution needs it and the response filter has no
        ///     other way to reach it. Without an origin the ladder keeps the first candidate it meets,
        ///     so a filter that judged one "Solar Panel" while the dispatcher walked to a nearer one
        ///     admitted a command and then acted on something else — silently, and only in the scenes
        ///     where it matters.
        /// </remarks>
        Vector3? ResolutionOrigin { get; }
    }
}
