using System;
using System.Collections.Generic;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     One live, explicitly-registered target entry: an object or character grounding target
    ///     added at runtime through <see cref="ConvaiCharacterActions" /> (as opposed to an
    ///     authored <c>ConvaiActionObjectDefinition</c>/<c>ConvaiActionCharacterDefinition</c>, or a
    ///     polled <see cref="ConvaiActionTarget" /> component). Duplicate names are allowed as
    ///     separate instances so the resolution ladder can disambiguate by nearest-instance.
    /// </summary>
    internal sealed class ConvaiActionTargetEntry
    {
        internal string Name;
        internal ConvaiActionTargetKind Kind;

        /// <summary>Object description or character bio, depending on <see cref="Kind" />.</summary>
        internal string Description;

        internal IReadOnlyList<string> Aliases = Array.Empty<string>();
        internal GameObject GameObjectReference;
        internal Transform InteractionPoint;
        internal bool Available = true;
    }

    /// <summary>
    ///     Per-character registry of explicitly-registered runtime action targets (see
    ///     <see cref="ConvaiCharacterActions" />). Holds only entries added through
    ///     <c>RegisterTarget</c>/<c>RegisterObject</c>/<c>RegisterCharacter</c> — targets contributed
    ///     by an enabled <see cref="ConvaiActionTarget" /> component are polled separately from its
    ///     own static active list and are not stored here.
    /// </summary>
    /// <remarks>
    ///     <see cref="Changed" /> fires on every mutation (add/remove/availability) so anything that
    ///     needs to react to the target set changing can subscribe, without this registry needing to
    ///     know about the network layer.
    /// </remarks>
    internal sealed class ConvaiActionTargetRegistry
    {
        private readonly List<ConvaiActionTargetEntry> _entries = new();

        /// <summary>Raised after every add/remove/availability mutation.</summary>
        internal event Action Changed;

        /// <summary>Live entries in registration order.</summary>
        internal IReadOnlyList<ConvaiActionTargetEntry> Entries => _entries;

        internal void Add(ConvaiActionTargetEntry entry)
        {
            if (entry == null) return;
            _entries.Add(entry);
            RaiseChanged();
        }

        /// <summary>
        ///     Removes every entry whose name matches (case-insensitive), regardless of how many
        ///     duplicate-named instances were registered. Returns the number removed.
        /// </summary>
        internal int RemoveAllNamed(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;

            int removed = _entries.RemoveAll(entry =>
                entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) RaiseChanged();
            return removed;
        }

        /// <summary>Sets <see cref="ConvaiActionTargetEntry.Available" /> on every entry matching name.</summary>
        internal bool SetAvailable(string name, bool available)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            bool any = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                ConvaiActionTargetEntry entry = _entries[i];
                if (entry == null || !string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.Available = available;
                any = true;
            }

            if (any) RaiseChanged();
            return any;
        }

        internal void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            RaiseChanged();
        }

        /// <summary>Lets an owner outside the registry (availability overlays) raise Changed explicitly.</summary>
        internal void NotifyChanged() => RaiseChanged();

        private void RaiseChanged() => Changed?.Invoke();
    }
}
