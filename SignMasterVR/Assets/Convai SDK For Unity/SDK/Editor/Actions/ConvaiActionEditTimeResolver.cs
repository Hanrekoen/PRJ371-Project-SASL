using System;
using System.Collections.Generic;
using Convai.Editor.Inspectors;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Edit-mode resolution dry-run for the Actions Editor window's Preview panel: builds the
    ///     same merged candidate list the runtime resolution uses — authored
    ///     Scene Knowledge (via <see cref="ConvaiActionConfigSource.BuildRuntimeResolutionConfig" />)
    ///     plus every enabled <see cref="ConvaiActionTarget" /> component that would register for
    ///     the character (mirroring <c>ConvaiCharacter.BuildMergedRuntimeActionConfig</c>'s
    ///     component-target merge: enabled, auto-registering, in scope, authored names win) — and
    ///     resolves a phrase through the <em>real</em> runtime ladder
    ///     (<see cref="ConvaiResolvedActionTarget.Resolve" />). The matched ladder step is then
    ///     derived by classifying the winning entry with
    ///     <see cref="ConvaiActionTargetPhraseMatcher" />: because the ladder tries its steps in
    ///     strict order, the winner's earliest satisfied step is exactly the step the ladder
    ///     returned at.
    /// </summary>
    internal static class ConvaiActionEditTimeResolver
    {
        /// <summary>Outcome of one dry-run resolution.</summary>
        internal readonly struct DryRunResult
        {
            internal DryRunResult(
                bool matched,
                string targetName,
                ConvaiActionTargetKind kind,
                ConvaiActionTargetPhraseMatcher.MatchStep step,
                string matchedText)
            {
                Matched = matched;
                TargetName = targetName;
                Kind = kind;
                Step = step;
                MatchedText = matchedText;
            }

            /// <summary>The name/alias text the step matched (for step descriptions); null when nothing matched.</summary>
            internal string MatchedText { get; }

            /// <summary>Whether the real ladder resolved any target for the phrase.</summary>
            internal bool Matched { get; }

            /// <summary>The resolved target's name; null when nothing matched.</summary>
            internal string TargetName { get; }

            /// <summary>The resolved target's kind; <see cref="ConvaiActionTargetKind.None" /> when nothing matched.</summary>
            internal ConvaiActionTargetKind Kind { get; }

            /// <summary>Which ladder step the winner matched at.</summary>
            internal ConvaiActionTargetPhraseMatcher.MatchStep Step { get; }
        }

        /// <summary>
        ///     Builds the edit-time candidate config: the authored base (never omitted for zero
        ///     definitions — same base the runtime resolution uses) plus enabled, auto-registering,
        ///     in-scope <see cref="ConvaiActionTarget" /> components from the open scenes. Returns
        ///     null when no source exists.
        /// </summary>
        internal static ConvaiActionConfig BuildCandidateConfig(
            ConvaiCharacter character,
            ConvaiActionConfigSource source) =>
            BuildCandidateConfig(
                character,
                source,
                ConvaiObjectFind.All<ConvaiActionTarget>(FindObjectsInactive.Exclude));

        /// <summary>
        ///     Cached-scene-target overload for repaint-driven editor surfaces. The caller owns the
        ///     sweep; this method still applies the same enabled, registration, and character-scope
        ///     filters as the runtime merge.
        /// </summary>
        internal static ConvaiActionConfig BuildCandidateConfig(
            ConvaiCharacter character,
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            if (source == null)
                return null;

            ConvaiActionConfig config = source.BuildRuntimeResolutionConfig();
            if (config == null)
                return null;

            var authoredObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Objects.Count; i++)
            {
                string objectName = config.Objects[i]?.Name;
                if (!string.IsNullOrWhiteSpace(objectName))
                    authoredObjectNames.Add(objectName);
            }

            var authoredCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Characters.Count; i++)
            {
                string characterName = config.Characters[i]?.Name;
                if (!string.IsNullOrWhiteSpace(characterName))
                    authoredCharacterNames.Add(characterName);
            }

            for (int i = 0; i < (sceneTargets?.Count ?? 0); i++)
            {
                ConvaiActionTarget target = sceneTargets[i];
                if (target == null || !target.isActiveAndEnabled || !target.RegisterOnEnable ||
                    !target.AppliesToCharacter(character))
                    continue;

                string targetName = target.TargetName;
                if (string.IsNullOrWhiteSpace(targetName))
                    continue;

                if (target.Kind == ConvaiActionTargetKind.Object)
                {
                    if (authoredObjectNames.Contains(targetName))
                        continue;

                    config.Objects.Add(new ConvaiActionObjectDefinition
                    {
                        Name = targetName,
                        Description = target.Description,
                        GameObjectReference = target.gameObject,
                        Aliases = target.Aliases != null ? new List<string>(target.Aliases) : new List<string>(),
                        InteractionPoint = target.InteractionPoint,
                        Available = true
                    });
                }
                else if (target.Kind == ConvaiActionTargetKind.Character)
                {
                    if (authoredCharacterNames.Contains(targetName))
                        continue;

                    config.Characters.Add(new ConvaiActionCharacterDefinition
                    {
                        Name = targetName,
                        Bio = target.Bio,
                        GameObjectReference = target.gameObject,
                        Aliases = target.Aliases != null ? new List<string>(target.Aliases) : new List<string>(),
                        InteractionPoint = target.InteractionPoint,
                        Available = true
                    });
                }
            }

            return config;
        }

        /// <summary>Whether a built config currently has a named, available target of an accepted kind.</summary>
        internal static bool HasUsableTarget(
            ConvaiActionConfig config,
            ConvaiActionTargetRequirement requirement)
        {
            if (requirement == ConvaiActionTargetRequirement.None)
                return true;
            if (config == null)
                return false;

            bool hasObject = (requirement is ConvaiActionTargetRequirement.Object or ConvaiActionTargetRequirement.Either) &&
                             HasUsableTarget(config.Objects);
            bool hasCharacter = (requirement is ConvaiActionTargetRequirement.Character or ConvaiActionTargetRequirement.Either) &&
                                HasUsableTarget(config.Characters);
            return hasObject || hasCharacter;
        }

        private static bool HasUsableTarget<T>(IReadOnlyList<T> targets)
        {
            if (targets == null)
                return false;

            for (int i = 0; i < targets.Count; i++)
            {
                switch (targets[i])
                {
                    case ConvaiActionObjectDefinition actionObject
                        when actionObject.Available && !string.IsNullOrWhiteSpace(actionObject.Name):
                    case ConvaiActionCharacterDefinition actionCharacter
                        when actionCharacter.Available && !string.IsNullOrWhiteSpace(actionCharacter.Name):
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Resolves <paramref name="phrase" /> through the real runtime ladder against
        ///     <paramref name="config" />, honoring the action's Valid Targets setting and the
        ///     character's position for nearest-instance tie-breaks, then classifies the matched
        ///     ladder step. The phrase is trimmed first — the same normalization a wire command's
        ///     target text receives.
        /// </summary>
        internal static DryRunResult Resolve(
            string phrase,
            ConvaiActionConfig config,
            ConvaiActionTargetRequirement requirement,
            Vector3? origin)
        {
            string trimmedPhrase = phrase?.Trim() ?? string.Empty;
            if (trimmedPhrase.Length == 0 || config == null)
                return new DryRunResult(false, null, ConvaiActionTargetKind.None,
                    ConvaiActionTargetPhraseMatcher.MatchStep.None, null);

            ConvaiResolvedActionTarget resolved = ConvaiResolvedActionTarget.Resolve(
                trimmedPhrase, config, (ConvaiActionTargetRequirement?)requirement, origin);
            if (resolved == null)
                return new DryRunResult(false, null, ConvaiActionTargetKind.None,
                    ConvaiActionTargetPhraseMatcher.MatchStep.None, null);

            IReadOnlyList<string> aliases = resolved.Kind == ConvaiActionTargetKind.Character
                ? resolved.CharacterBinding?.Aliases
                : resolved.ObjectBinding?.Aliases;
            ConvaiActionTargetPhraseMatcher.MatchResult match =
                ConvaiActionTargetPhraseMatcher.Match(trimmedPhrase, resolved.Name, aliases);

            // The winner always classifies (the ladder only returns entries one of the mirrored
            // steps accepts); Contains is the safe floor for any whitespace corner case.
            ConvaiActionTargetPhraseMatcher.MatchStep step =
                match.Step == ConvaiActionTargetPhraseMatcher.MatchStep.None
                    ? ConvaiActionTargetPhraseMatcher.MatchStep.Contains
                    : match.Step;

            return new DryRunResult(true, resolved.Name, resolved.Kind, step,
                match.MatchedText ?? resolved.Name);
        }
    }
}
