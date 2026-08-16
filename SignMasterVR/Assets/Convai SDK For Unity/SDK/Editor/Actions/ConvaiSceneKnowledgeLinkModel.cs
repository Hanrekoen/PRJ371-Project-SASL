using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>What a Known entry can currently do — the state its status line reports.</summary>
    internal enum ConvaiKnownEntryLinkState
    {
        /// <summary>The entry points at a scene object, so actions that need a target can use it.</summary>
        Linked = 0,

        /// <summary>
        ///     The entry has no object of its own, but a scene <see cref="ConvaiActionTarget" />
        ///     answers to its name and will supply one at run time.
        /// </summary>
        AnsweredByTarget = 1,

        /// <summary>The entry deliberately has nothing in the scene behind it.</summary>
        TextOnly = 2,

        /// <summary>The entry claims to be actionable and nothing can act on it. The broken state.</summary>
        Unlinked = 3
    }

    /// <summary>
    ///     Pure decisions behind Scene Knowledge's entry-to-scene link: what state one entry is in,
    ///     and which scene objects could answer to its name.
    /// </summary>
    /// <remarks>
    ///     Kept out of the window so the rules can be proven without a scene or a draw pass — and
    ///     because the Action Troubleshooter answers the same questions about the same entries, and
    ///     two surfaces that disagree about whether a character can act on something would be worse
    ///     than either of them being wrong.
    /// </remarks>
    internal static class ConvaiSceneKnowledgeLinkModel
    {
        /// <summary>The state of one object entry, given the scene targets currently known.</summary>
        internal static ConvaiKnownEntryLinkState ClassifyObject(
            ConvaiActionObjectDefinition entry, IReadOnlyList<ConvaiActionTarget> sceneTargets) =>
            Classify(entry?.GameObjectReference, entry?.TextOnly ?? false, entry?.Name,
                ConvaiActionTargetKind.Object, sceneTargets);

        /// <summary>The state of one character entry, given the scene targets currently known.</summary>
        internal static ConvaiKnownEntryLinkState ClassifyCharacter(
            ConvaiActionCharacterDefinition entry, IReadOnlyList<ConvaiActionTarget> sceneTargets) =>
            Classify(entry?.GameObjectReference, entry?.TextOnly ?? false, entry?.Name,
                ConvaiActionTargetKind.Character, sceneTargets);

        private static ConvaiKnownEntryLinkState Classify(
            GameObject reference,
            bool textOnly,
            string name,
            ConvaiActionTargetKind kind,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            // An explicit link wins over everything: it is what the author wrote down, and it is
            // what the runtime will use.
            if (reference != null)
                return ConvaiKnownEntryLinkState.Linked;

            if (textOnly)
                return ConvaiKnownEntryLinkState.TextOnly;

            return FindTargetByName(name, kind, sceneTargets) != null
                ? ConvaiKnownEntryLinkState.AnsweredByTarget
                : ConvaiKnownEntryLinkState.Unlinked;
        }

        /// <summary>
        ///     The scene <see cref="ConvaiActionTarget" /> of this kind that answers to
        ///     <paramref name="name" />, or null. Name matching mirrors the runtime merge:
        ///     case-insensitive, trimmed, first one wins.
        /// </summary>
        internal static ConvaiActionTarget FindTargetByName(
            string name, ConvaiActionTargetKind kind, IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            string trimmed = Trim(name);
            if (trimmed.Length == 0 || sceneTargets == null)
                return null;

            for (int i = 0; i < sceneTargets.Count; i++)
            {
                ConvaiActionTarget target = sceneTargets[i];
                if (target == null || target.Kind != kind)
                    continue;

                if (string.Equals(Trim(target.TargetName), trimmed, StringComparison.OrdinalIgnoreCase))
                    return target;
            }

            return null;
        }

        /// <summary>
        ///     Every candidate in <paramref name="sceneObjects" /> whose name matches
        ///     <paramref name="name" /> — what "Find In Scene" offers. Exact (case-insensitive) matches
        ///     only: a fuzzy match here would link the wrong object silently, which is the failure this
        ///     whole feature exists to end.
        /// </summary>
        internal static List<GameObject> FindObjectsByName(string name, IReadOnlyList<GameObject> sceneObjects)
        {
            var matches = new List<GameObject>();
            string trimmed = Trim(name);
            if (trimmed.Length == 0 || sceneObjects == null)
                return matches;

            for (int i = 0; i < sceneObjects.Count; i++)
            {
                GameObject candidate = sceneObjects[i];
                if (candidate == null)
                    continue;

                if (string.Equals(Trim(candidate.name), trimmed, StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
            }

            return matches;
        }

        /// <summary>
        ///     Whether this entry's name and its linked object have drifted apart — the case where
        ///     "Use Object's Name" is worth offering.
        /// </summary>
        internal static bool NameDiffersFromObject(string name, GameObject reference)
        {
            if (reference == null)
                return false;

            string trimmed = Trim(name);
            return trimmed.Length > 0 &&
                   !string.Equals(trimmed, Trim(reference.name), StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
