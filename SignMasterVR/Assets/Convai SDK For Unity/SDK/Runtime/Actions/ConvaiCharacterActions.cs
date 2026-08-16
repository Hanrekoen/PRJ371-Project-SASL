using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Read-only snapshot of one target visible to a character's merged action config, as
    ///     exposed by <see cref="ConvaiCharacterActions.Targets" />. Covers authored targets,
    ///     explicitly-registered runtime targets, and polled <see cref="ConvaiActionTarget" />
    ///     components alike.
    /// </summary>
    public readonly struct ConvaiActionTargetSnapshot
    {
        /// <summary>Target name.</summary>
        public string Name { get; }

        /// <summary>Whether this is an object or character target.</summary>
        public ConvaiActionTargetKind Kind { get; }

        /// <summary>Object description or character bio, depending on <see cref="Kind" />.</summary>
        public string Description { get; }

        /// <summary>Alternate names the resolution ladder matches exactly.</summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>Scene object bound to this target, when any.</summary>
        public GameObject GameObjectReference { get; }

        /// <summary>Explicit interaction point, when any.</summary>
        public Transform InteractionPoint { get; }

        /// <summary>Whether this target currently resolves (unavailable targets are skipped by the ladder).</summary>
        public bool Available { get; }

        internal ConvaiActionTargetSnapshot(
            string name,
            ConvaiActionTargetKind kind,
            string description,
            IReadOnlyList<string> aliases,
            GameObject gameObjectReference,
            Transform interactionPoint,
            bool available)
        {
            Name = name;
            Kind = kind;
            Description = description;
            Aliases = aliases ?? Array.Empty<string>();
            GameObjectReference = gameObjectReference;
            InteractionPoint = interactionPoint;
            Available = available;
        }
    }

    /// <summary>
    ///     Runtime target-registration surface for one <see cref="ConvaiCharacter" />
    ///     (<c>character.Actions</c>). Lets game code register/unregister objects and characters as
    ///     action grounding targets after connect, on top of the authored
    ///     <see cref="ConvaiActionConfigSource" /> list and any enabled <see cref="ConvaiActionTarget" />
    ///     components. An authored target always wins a name collision.
    /// </summary>
    public sealed class ConvaiCharacterActions
    {
        private readonly ConvaiCharacter _character;
        private readonly Dictionary<string, bool> _availabilityOverrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _actionAvailabilityOverrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedAuthoredCollisions = new(StringComparer.OrdinalIgnoreCase);
        private bool _emptyNameWarned;

        internal ConvaiCharacterActions(ConvaiCharacter character) => _character = character;

        /// <summary>Explicitly-registered runtime targets for this character (not authored, not component-polled).</summary>
        internal ConvaiActionTargetRegistry Registry { get; } = new();

        /// <summary>
        ///     Per-character availability overrides from <see cref="SetTargetAvailable" />, applied
        ///     over any source (authored, explicit registration, or a polled <see cref="ConvaiActionTarget" />).
        /// </summary>
        internal bool TryGetAvailabilityOverride(string name, out bool available) =>
            _availabilityOverrides.TryGetValue(name, out available);

        /// <summary>
        ///     Registers a <see cref="ConvaiActionTarget" /> component's current field values as a
        ///     one-time snapshot entry for this character, independent of the component's own
        ///     enable/disable lifecycle and <see cref="ConvaiActionTarget.ApplyTo" /> scope.
        /// </summary>
        public void RegisterTarget(ConvaiActionTarget target)
        {
            if (target == null) return;

            string name = target.TargetName;
            if (!ValidateName(name, nameof(RegisterTarget))) return;

            WarnIfAuthoredCollision(name);
            Registry.Add(new ConvaiActionTargetEntry
            {
                Name = name.Trim(),
                Kind = target.Kind,
                Description = target.Kind == ConvaiActionTargetKind.Character ? target.Bio : target.Description,
                Aliases = target.Aliases != null ? new List<string>(target.Aliases) : Array.Empty<string>(),
                GameObjectReference = target.gameObject,
                InteractionPoint = target.InteractionPoint,
                Available = true
            });
        }

        /// <summary>Registers a runtime object target (e.g. a spawned prefab) available for action grounding.</summary>
        public void RegisterObject(string name, string description, GameObject gameObject = null)
        {
            if (!ValidateName(name, nameof(RegisterObject))) return;

            WarnIfAuthoredCollision(name);
            Registry.Add(new ConvaiActionTargetEntry
            {
                Name = name.Trim(),
                Kind = ConvaiActionTargetKind.Object,
                Description = description,
                GameObjectReference = gameObject,
                Available = true
            });
        }

        /// <summary>Registers a runtime character target available for action grounding.</summary>
        public void RegisterCharacter(string name, string bio, GameObject gameObject = null)
        {
            if (!ValidateName(name, nameof(RegisterCharacter))) return;

            WarnIfAuthoredCollision(name);
            Registry.Add(new ConvaiActionTargetEntry
            {
                Name = name.Trim(),
                Kind = ConvaiActionTargetKind.Character,
                Description = bio,
                GameObjectReference = gameObject,
                Available = true
            });
        }

        /// <summary>
        ///     Removes every explicitly-registered entry matching <paramref name="name" />
        ///     (case-insensitive) — including every duplicate-named instance registered through
        ///     <see cref="RegisterTarget" />/<see cref="RegisterObject" />/<see cref="RegisterCharacter" />
        ///     on this character. Does not affect authored targets or polled
        ///     <see cref="ConvaiActionTarget" /> components — disable those instead.
        /// </summary>
        public void UnregisterTarget(string name) => Registry.RemoveAllNamed(name);

        /// <summary>
        ///     Marks a target (by name) available or unavailable for resolution. Applies as an
        ///     overlay over any source — authored, explicitly registered, or a polled
        ///     <see cref="ConvaiActionTarget" /> — so a despawned object can be marked unavailable
        ///     without waiting for its component to disable.
        /// </summary>
        public void SetTargetAvailable(string name, bool available)
        {
            if (!ValidateName(name, nameof(SetTargetAvailable))) return;

            _availabilityOverrides[name.Trim()] = available;
            Registry.NotifyChanged();
        }

        /// <summary>Read-only snapshot of every target currently visible to this character's merged config.</summary>
        public IReadOnlyList<ConvaiActionTargetSnapshot> Targets => BuildSnapshot();

        /// <summary>
        ///     Marks an action (by name, case-insensitive) available or unavailable for this
        ///     character for the rest of the session, overriding the authored
        ///     <see cref="ConvaiActionDefinition.Enabled" /> flag in either direction. An
        ///     unavailable action is excluded from the <c>action_config</c> the backend sees — the
        ///     change is staged through the same batched <c>context-update</c> sync target
        ///     registration uses, so the Convai Character genuinely stops (or starts) offering it
        ///     mid-conversation. A stale backend command for an unavailable action is reported as
        ///     unhandled instead of executing.
        /// </summary>
        public void SetActionAvailable(string actionName, bool available)
        {
            if (!ValidateName(actionName, nameof(SetActionAvailable))) return;

            _actionAvailabilityOverrides[actionName.Trim()] = available;
            _character?.MarkPendingActionConfigSync();
        }

        /// <summary>
        ///     Whether an action (by name, case-insensitive) is currently available on this
        ///     character. Precedence: a session override from <see cref="SetActionAvailable" />
        ///     wins; otherwise the authored <see cref="ConvaiActionDefinition.Enabled" /> flag of
        ///     the matching definition applies. Returns <c>false</c> for names matching no known
        ///     action definition and no override.
        /// </summary>
        public bool IsActionAvailable(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return false;

            string trimmed = actionName.Trim();
            if (_actionAvailabilityOverrides.TryGetValue(trimmed, out bool overridden))
                return overridden;

            // The full catalog, not the session's availability-filtered active list: a disabled
            // action must report false, not fall through as "no such action".
            IReadOnlyList<ConvaiActionDefinition> definitions = _character?.GetRuntimeActionDefinitionCatalog();
            if (definitions == null) return false;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                if (definition != null &&
                    string.Equals(
                        ConvaiActionDefinition.NormalizeActionName(definition.ActionName),
                        trimmed,
                        StringComparison.OrdinalIgnoreCase))
                    return definition.Enabled;
            }

            return false;
        }

        /// <summary>
        ///     Per-character action availability overrides from <see cref="SetActionAvailable" />,
        ///     applied over the authored <see cref="ConvaiActionDefinition.Enabled" /> flag.
        /// </summary>
        internal bool TryGetActionAvailabilityOverride(string actionName, out bool available)
        {
            available = false;
            return !string.IsNullOrWhiteSpace(actionName) &&
                   _actionAvailabilityOverrides.TryGetValue(actionName.Trim(), out available);
        }

        /// <summary>
        ///     Effective availability of one definition on this character: session override first,
        ///     authored <see cref="ConvaiActionDefinition.Enabled" /> otherwise.
        /// </summary>
        internal bool IsDefinitionAvailable(ConvaiActionDefinition definition)
        {
            if (definition == null) return false;

            string actionName = ConvaiActionDefinition.NormalizeActionName(definition.ActionName);
            if (!string.IsNullOrEmpty(actionName) &&
                _actionAvailabilityOverrides.TryGetValue(actionName, out bool overridden))
                return overridden;

            return definition.Enabled;
        }

        /// <summary>
        ///     Reports two different things answering to one name, once per name.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only a genuine contest reaches here: the same object described twice, once in Scene
        ///         Knowledge and once by a component on it, is completed rather than reported. What is
        ///         left is two <em>different</em> objects claiming one name, and only one of them can
        ///         ever be reached — every request for that name goes to the authored entry, and the
        ///         other might as well not exist.
        ///     </para>
        ///     <para>
        ///         Raised to a warning because it is silent otherwise and impossible to deduce from
        ///         the symptom: the character acts, confidently, on the wrong thing.
        ///     </para>
        /// </remarks>
        internal void LogAuthoredCollisionOnce(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !_loggedAuthoredCollisions.Add(name.Trim()))
                return;

            ConvaiLogger.Warning(
                $"[ConvaiCharacterActions] Two different things on '{_character?.name}' answer to the " +
                $"name '{name}': one written in Scene Knowledge and one in the scene. Requests for it " +
                "always go to the Scene Knowledge entry, so the other can never be acted on. Rename " +
                "one of them, or clear the Scene Knowledge entry's own object so the two combine.",
                LogCategory.Actions);
        }

        private void WarnIfAuthoredCollision(string name)
        {
            ConvaiActionConfigSource source = _character?.GetActionConfigSource();
            if (source == null) return;

            if (NameListContains(source.Objects, name) || NameListContains(source.Characters, name))
                LogAuthoredCollisionOnce(name);
        }

        private static bool NameListContains<T>(IReadOnlyList<T> items, string name) where T : class
        {
            if (items == null) return false;

            for (int i = 0; i < items.Count; i++)
            {
                string candidate = items[i] switch
                {
                    ConvaiActionObjectDefinition actionObject => actionObject?.Name,
                    ConvaiActionCharacterDefinition actionCharacter => actionCharacter?.Name,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(candidate) &&
                    string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ValidateName(string name, string apiName)
        {
            if (!string.IsNullOrWhiteSpace(name)) return true;

            if (!_emptyNameWarned)
            {
                _emptyNameWarned = true;
                ConvaiLogger.Warning(
                    $"[ConvaiCharacterActions] {apiName} called with an empty name on '{_character?.name}'; ignoring.",
                    LogCategory.Character);
            }

            return false;
        }

        private IReadOnlyList<ConvaiActionTargetSnapshot> BuildSnapshot()
        {
            ConvaiActionConfig merged = _character?.GetRuntimeActionConfig();
            if (merged == null) return Array.Empty<ConvaiActionTargetSnapshot>();

            var snapshot = new List<ConvaiActionTargetSnapshot>(merged.Objects.Count + merged.Characters.Count);
            for (int i = 0; i < merged.Objects.Count; i++)
            {
                ConvaiActionObjectDefinition o = merged.Objects[i];
                if (o == null) continue;
                snapshot.Add(new ConvaiActionTargetSnapshot(
                    o.Name, ConvaiActionTargetKind.Object, o.Description, o.Aliases, o.GameObjectReference,
                    o.InteractionPoint, o.Available));
            }

            for (int i = 0; i < merged.Characters.Count; i++)
            {
                ConvaiActionCharacterDefinition c = merged.Characters[i];
                if (c == null) continue;
                snapshot.Add(new ConvaiActionTargetSnapshot(
                    c.Name, ConvaiActionTargetKind.Character, c.Bio, c.Aliases, c.GameObjectReference,
                    c.InteractionPoint, c.Available));
            }

            return snapshot;
        }
    }
}
