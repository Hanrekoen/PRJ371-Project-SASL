using System;
using System.Collections.Generic;
using Convai.Shared.Actions;

namespace Convai.Runtime.Actions
{
    internal readonly struct ConvaiActionConfigReconciliation
    {
        internal ConvaiActionConfigReconciliation(
            ConvaiActionConfig snapshot,
            ConvaiActionConfigPatch patch,
            string topLevelAttentionObject)
        {
            Snapshot = snapshot;
            Patch = patch;
            TopLevelAttentionObject = topLevelAttentionObject;
        }

        public ConvaiActionConfig Snapshot { get; }
        public ConvaiActionConfigPatch Patch { get; }
        public string TopLevelAttentionObject { get; }
    }

    /// <summary>
    ///     Pure runtime action-config patch validation and merge logic. The same function prepares
    ///     outbound data and applies acknowledged state so both paths share one contract.
    /// </summary>
    internal static class ConvaiActionConfigPatchReconciler
    {
        public static bool TryReconcile(
            ConvaiActionConfig current,
            ConvaiActionConfigPatch patch,
            object topLevelAttentionObject,
            IReadOnlyList<ConvaiActionDefinition> executableDefinitions,
            out ConvaiActionConfigReconciliation reconciliation,
            out string error)
        {
            reconciliation = default;
            error = string.Empty;

            ConvaiActionConfig snapshot = current?.Clone() ?? new ConvaiActionConfig();
            ConvaiActionConfigPatch normalizedPatch = patch?.Clone();

            if (!TryNormalizeExistingActions(snapshot.Actions, out List<string> currentActions, out error) ||
                !TryNormalizeObjects(snapshot.Objects, out List<ConvaiActionObjectDefinition> currentObjects,
                    out error) ||
                !TryNormalizeCharacters(snapshot.Characters,
                    out List<ConvaiActionCharacterDefinition> currentCharacters, out error))
                return false;

            snapshot.Actions = currentActions;
            snapshot.Objects = currentObjects;
            snapshot.Characters = currentCharacters;

            if (normalizedPatch != null)
            {
                if (!TryNormalizeActions(normalizedPatch.Actions, executableDefinitions,
                        out List<string> actions, out error))
                    return false;

                if (!TryNormalizeObjects(normalizedPatch.Objects, out List<ConvaiActionObjectDefinition> objects,
                        out error))
                    return false;

                if (!TryNormalizeCharacters(normalizedPatch.Characters,
                        out List<ConvaiActionCharacterDefinition> characters, out error))
                    return false;

                if (normalizedPatch.Actions != null)
                {
                    normalizedPatch.Actions = actions;
                    snapshot.Actions = new List<string>(actions);
                }

                if (normalizedPatch.Objects != null)
                {
                    PreserveObjectBindings(snapshot.Objects, objects);
                    normalizedPatch.Objects = objects;
                    snapshot.Objects = CloneObjects(objects);
                }

                if (normalizedPatch.Characters != null)
                {
                    PreserveCharacterBindings(snapshot.Characters, characters);
                    normalizedPatch.Characters = characters;
                    snapshot.Characters = CloneCharacters(characters);
                }
            }

            if (!TryValidateTargetNames(snapshot.Objects, snapshot.Characters, out error))
                return false;

            bool hasTopLevelAttention = topLevelAttentionObject != null;
            if (!TryReadAttentionName(topLevelAttentionObject, out string topLevelName, out error))
                return false;

            string requestedAttention = hasTopLevelAttention
                ? topLevelName
                : normalizedPatch?.CurrentAttentionObject;
            bool hasRequestedAttention = hasTopLevelAttention || normalizedPatch?.CurrentAttentionObject != null;

            if (hasRequestedAttention)
            {
                requestedAttention = Normalize(requestedAttention);
                if (requestedAttention.Length == 0)
                {
                    snapshot.CurrentAttentionObject = null;
                    if (!hasTopLevelAttention && normalizedPatch != null)
                        normalizedPatch.CurrentAttentionObject = string.Empty;
                }
                else if (TryResolveObjectName(snapshot.Objects, requestedAttention, out string resolvedName))
                {
                    snapshot.CurrentAttentionObject = resolvedName;
                    if (hasTopLevelAttention)
                        topLevelName = resolvedName;
                    else if (normalizedPatch != null)
                        normalizedPatch.CurrentAttentionObject = resolvedName;
                }
                else
                {
                    error = $"current_attention_object '{requestedAttention}' is not present in action_config.objects";
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.CurrentAttentionObject))
            {
                snapshot.CurrentAttentionObject = TryResolveObjectName(
                    snapshot.Objects,
                    snapshot.CurrentAttentionObject,
                    out string resolvedName)
                    ? resolvedName
                    : null;
            }

            reconciliation = new ConvaiActionConfigReconciliation(
                snapshot,
                normalizedPatch,
                hasTopLevelAttention ? topLevelName : null);
            return true;
        }

        public static bool TryResolveObjectName(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            string requestedName,
            out string resolvedName)
        {
            resolvedName = null;
            string normalized = Normalize(requestedName);
            if (normalized.Length == 0 || objects == null)
                return false;

            for (int i = 0; i < objects.Count; i++)
            {
                string candidate = Normalize(objects[i]?.Name);
                if (!string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolvedName = candidate;
                return true;
            }

            return false;
        }

        private static bool TryNormalizeActions(
            IReadOnlyList<string> source,
            IReadOnlyList<ConvaiActionDefinition> executableDefinitions,
            out List<string> normalized,
            out string error)
        {
            normalized = source == null ? null : new List<string>(source.Count);
            error = string.Empty;
            if (source == null)
                return true;

            // Matched by the whole rendered string first: these strings were produced by these very
            // definitions, so parsing a name back out of them is both unnecessary and unreliable.
            // An action named 'Walk' whose first parameter carries the connector 'to' renders as
            // "Walk to {destination: reference}" and reads back as 'Walk to', which matched nothing
            // — and this method answers a miss by rejecting the entire patch, so registering a
            // runtime target silently stopped working for any character owning such an action.
            Dictionary<string, ConvaiActionDefinition> renderedLookup =
                ConvaiActionDefinition.BuildRenderedLookup(executableDefinitions);
            Dictionary<string, ConvaiActionDefinition> nameLookup =
                ConvaiActionDefinition.BuildLookup(executableDefinitions);

            for (int i = 0; i < source.Count; i++)
            {
                string action = Normalize(source[i]);
                if (action.Length == 0)
                {
                    error = $"action_config.actions[{i}] cannot be blank";
                    return false;
                }

                ConvaiActionDefinition definition = ConvaiActionDefinition.ResolveRendered(
                    action, renderedLookup, nameLookup, out string canonicalName);
                if (!ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                {
                    error = $"action_config action '{canonicalName}' has no local executable definition";
                    return false;
                }

                normalized.Add(action);
            }

            return true;
        }

        private static bool TryNormalizeExistingActions(
            IReadOnlyList<string> source,
            out List<string> normalized,
            out string error)
        {
            normalized = new List<string>(source?.Count ?? 0);
            error = string.Empty;
            if (source == null)
                return true;

            for (int i = 0; i < source.Count; i++)
            {
                string action = Normalize(source[i]);
                if (action.Length == 0)
                {
                    error = $"action_config.actions[{i}] cannot be blank";
                    return false;
                }

                normalized.Add(action);
            }

            return true;
        }

        private static bool TryNormalizeObjects(
            IReadOnlyList<ConvaiActionObjectDefinition> source,
            out List<ConvaiActionObjectDefinition> normalized,
            out string error)
        {
            normalized = source == null ? null : new List<ConvaiActionObjectDefinition>(source.Count);
            error = string.Empty;
            if (source == null)
                return true;

            for (int i = 0; i < source.Count; i++)
            {
                ConvaiActionObjectDefinition item = source[i];
                string name = Normalize(item?.Name);
                if (name.Length == 0)
                {
                    error = $"action_config.objects[{i}] must have a name";
                    return false;
                }

                // Clone first so every authored field the patch did not touch — TextOnly, Aliases,
                // InteractionPoint, Available — survives normalization; only Name and Description
                // are rewritten to their normalized text.
                ConvaiActionObjectDefinition normalizedItem = item.Clone();
                normalizedItem.Name = name;
                normalizedItem.Description = Normalize(item.Description);
                normalized.Add(normalizedItem);
            }

            return true;
        }

        private static bool TryNormalizeCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition> source,
            out List<ConvaiActionCharacterDefinition> normalized,
            out string error)
        {
            normalized = source == null ? null : new List<ConvaiActionCharacterDefinition>(source.Count);
            error = string.Empty;
            if (source == null)
                return true;

            for (int i = 0; i < source.Count; i++)
            {
                ConvaiActionCharacterDefinition item = source[i];
                string name = Normalize(item?.Name);
                if (name.Length == 0)
                {
                    error = $"action_config.characters[{i}] must have a name";
                    return false;
                }

                // Clone first so every authored field the patch did not touch — TextOnly, Aliases,
                // InteractionPoint, Available — survives normalization; only Name and Bio are
                // rewritten to their normalized text.
                ConvaiActionCharacterDefinition normalizedItem = item.Clone();
                normalizedItem.Name = name;
                normalizedItem.Bio = Normalize(item.Bio);
                normalized.Add(normalizedItem);
            }

            return true;
        }

        private static bool TryValidateTargetNames(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            out string error)
        {
            error = string.Empty;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryAddTargetNames(objects, names, "object", out error))
                return false;

            return TryAddTargetNames(characters, names, "character", out error);
        }

        private static bool TryAddTargetNames<T>(
            IReadOnlyList<T> targets,
            ISet<string> names,
            string kind,
            out string error)
        {
            error = string.Empty;
            if (targets == null)
                return true;

            for (int i = 0; i < targets.Count; i++)
            {
                string name = targets[i] switch
                {
                    ConvaiActionObjectDefinition actionObject => Normalize(actionObject?.Name),
                    ConvaiActionCharacterDefinition character => Normalize(character?.Name),
                    _ => string.Empty
                };
                if (name.Length == 0)
                {
                    error = $"action_config {kind} target at index {i} must have a name";
                    return false;
                }

                if (names.Add(name))
                    continue;

                error = $"duplicate action target name '{name}'";
                return false;
            }

            return true;
        }

        private static bool TryReadAttentionName(object value, out string name, out string error)
        {
            error = string.Empty;
            switch (value)
            {
                case null:
                    name = null;
                    return true;
                case string stringName:
                    name = Normalize(stringName);
                    return true;
                case ConvaiActionObjectDefinition actionObject:
                    name = Normalize(actionObject.Name);
                    if (name.Length > 0)
                        return true;

                    error = "current_attention_object payload must have a name";
                    return false;
                default:
                    name = null;
                    error = "current_attention_object must be a string or ConvaiActionObjectDefinition";
                    return false;
            }
        }

        private static List<ConvaiActionObjectDefinition> CloneObjects(
            IReadOnlyList<ConvaiActionObjectDefinition> source)
        {
            var clone = new List<ConvaiActionObjectDefinition>(source?.Count ?? 0);
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
                clone.Add(source[i]?.Clone() ?? new ConvaiActionObjectDefinition());

            return clone;
        }

        private static void PreserveObjectBindings(
            IReadOnlyList<ConvaiActionObjectDefinition> current,
            IReadOnlyList<ConvaiActionObjectDefinition> replacements)
        {
            if (current == null || replacements == null)
                return;

            for (int i = 0; i < replacements.Count; i++)
            {
                ConvaiActionObjectDefinition replacement = replacements[i];
                if (replacement?.GameObjectReference != null)
                    continue;

                for (int j = 0; j < current.Count; j++)
                {
                    ConvaiActionObjectDefinition existing = current[j];
                    if (!string.Equals(
                            replacement?.Name,
                            existing?.Name,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    replacement.GameObjectReference = existing.GameObjectReference;
                    break;
                }
            }
        }

        private static List<ConvaiActionCharacterDefinition> CloneCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition> source)
        {
            var clone = new List<ConvaiActionCharacterDefinition>(source?.Count ?? 0);
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
                clone.Add(source[i]?.Clone() ?? new ConvaiActionCharacterDefinition());

            return clone;
        }

        private static void PreserveCharacterBindings(
            IReadOnlyList<ConvaiActionCharacterDefinition> current,
            IReadOnlyList<ConvaiActionCharacterDefinition> replacements)
        {
            if (current == null || replacements == null)
                return;

            for (int i = 0; i < replacements.Count; i++)
            {
                ConvaiActionCharacterDefinition replacement = replacements[i];
                if (replacement?.GameObjectReference != null)
                    continue;

                for (int j = 0; j < current.Count; j++)
                {
                    ConvaiActionCharacterDefinition existing = current[j];
                    if (!string.Equals(
                            replacement?.Name,
                            existing?.Name,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    replacement.GameObjectReference = existing.GameObjectReference;
                    break;
                }
            }
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
