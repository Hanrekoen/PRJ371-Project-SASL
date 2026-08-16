using System.Collections.Generic;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     One thing this character could be talking about, read the same way whether it is an
    ///     object or a character.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> An action target is one idea — a name, some alternate names,
    ///         maybe something in the scene behind it, a point to stand at, and whether it is
    ///         currently offered. The SDK stores that idea in two structurally identical types,
    ///         <see cref="ConvaiActionObjectDefinition" /> and
    ///         <see cref="ConvaiActionCharacterDefinition" />, which differ only in whether the prose
    ///         field is called Description or Bio. Every piece of logic that reads a target therefore
    ///         had to be written twice, and the resolution ladder was: eight matching methods, two
    ///         ambiguity reporters and two lookups, in two copies that could — and did — drift.
    ///     </para>
    ///     <para>
    ///         <b>Why the two types are not merged instead.</b> Both are <c>[Serializable]</c> and
    ///         live in customer scenes and prefabs. Merging them would compile and could still lose
    ///         authored data on load, which is a worse outcome than the duplication. So the idea is
    ///         unified where the reading happens and the storage is left exactly as it is: this is a
    ///         view over one entry or the other, holding no state of its own.
    ///     </para>
    ///     <para>
    ///         A <c>readonly struct</c> because the ladder builds one per candidate per rung and
    ///         allocating there would put garbage on a path that runs for every command.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiActionTargetCandidate
    {
        private readonly ConvaiActionObjectDefinition _object;
        private readonly ConvaiActionCharacterDefinition _character;

        internal ConvaiActionTargetCandidate(ConvaiActionObjectDefinition entry)
        {
            _object = entry;
            _character = null;
        }

        internal ConvaiActionTargetCandidate(ConvaiActionCharacterDefinition entry)
        {
            _object = null;
            _character = entry;
        }

        /// <summary>Whether this is a placeholder rather than a candidate — the "no match" value.</summary>
        internal bool IsNull => _object == null && _character == null;

        /// <summary>Whether this entry is an object or a character.</summary>
        internal ConvaiActionTargetKind Kind =>
            _object != null ? ConvaiActionTargetKind.Object :
            _character != null ? ConvaiActionTargetKind.Character :
            ConvaiActionTargetKind.None;

        /// <summary>The authored name.</summary>
        internal string Name => _object != null ? _object.Name : _character?.Name;

        /// <summary>Alternate names this entry also answers to.</summary>
        internal IReadOnlyList<string> Aliases => _object != null ? _object.Aliases : _character?.Aliases;

        /// <summary>Whether this entry is currently offered; unavailable entries never match.</summary>
        internal bool Available => _object?.Available ?? _character?.Available ?? false;

        /// <summary>The scene object behind this entry, when one is linked.</summary>
        internal GameObject Binding =>
            _object != null ? _object.GameObjectReference : _character?.GameObjectReference;

        /// <summary>The explicit point to act at, when one is authored.</summary>
        internal Transform InteractionPoint =>
            _object != null ? _object.InteractionPoint : _character?.InteractionPoint;

        /// <summary>
        ///     Where this entry is in the world, or null when nothing stands behind it.
        /// </summary>
        /// <remarks>
        ///     Having a position is exactly the same question as being actionable — the interaction
        ///     point or the scene object is what a behavior would move to — which is why the ladder
        ///     can use it both to break a tie and to prefer a bound entry over an unbound one.
        /// </remarks>
        internal Vector3? AnchorPosition
        {
            get
            {
                Transform point = InteractionPoint;
                if (point != null) return point.position;

                GameObject binding = Binding;
                return binding != null ? binding.transform.position : (Vector3?)null;
            }
        }

        /// <summary>Converts this view into the resolved target handed to callers.</summary>
        internal ConvaiResolvedActionTarget ToResolved() =>
            _object != null
                ? ConvaiResolvedActionTarget.FromObject(_object)
                : ConvaiResolvedActionTarget.FromCharacter(_character);
    }
}
