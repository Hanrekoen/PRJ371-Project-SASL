using System;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     An action target resolved against the authored/merged <see cref="ConvaiActionConfig" />
    ///     objects and characters, through a deterministic four-step ladder: (1) exact name,
    ///     (2) alias exact, (3) normalized (article-stripped, whitespace-collapsed, case-insensitive),
    ///     (4) unique contains/contained-by. Unavailable entries are excluded at every step.
    ///     On the exact, alias and normalized steps, duplicate matches are broken by picking the one
    ///     nearest the origin when one is supplied. The contains step deliberately does not guess:
    ///     a fuzzy match that fits more than one candidate is reported as ambiguous and resolves to
    ///     nothing.
    /// </summary>
    [Serializable]
    public sealed class ConvaiResolvedActionTarget
    {
        private static readonly string[] LeadingArticles = { "the ", "an ", "a " };

        /// <summary>Whether the target is an object or a character.</summary>
        public ConvaiActionTargetKind Kind { get; private set; }

        /// <summary>Authored name of the resolved target.</summary>
        public string Name { get; private set; }

        /// <summary>Authored object binding; null unless <see cref="Kind" /> is Object.</summary>
        public ConvaiActionObjectDefinition ObjectBinding { get; private set; }

        /// <summary>Authored character binding; null unless <see cref="Kind" /> is Character.</summary>
        public ConvaiActionCharacterDefinition CharacterBinding { get; private set; }

        /// <summary>Scene object of the resolved binding, when assigned by the author.</summary>
        public GameObject GameObjectReference => Kind switch
        {
            ConvaiActionTargetKind.Object => ObjectBinding?.GameObjectReference,
            ConvaiActionTargetKind.Character => CharacterBinding?.GameObjectReference,
            _ => null
        };

        /// <summary>
        ///     Point to move to / aim at: the binding's explicit interaction point when set,
        ///     otherwise <see cref="GameObjectReference" />'s transform, otherwise null.
        /// </summary>
        public Transform InteractionPoint => Kind switch
        {
            ConvaiActionTargetKind.Object => ObjectBinding?.InteractionPoint != null
                ? ObjectBinding.InteractionPoint
                : GameObjectReference != null ? GameObjectReference.transform : null,
            ConvaiActionTargetKind.Character => CharacterBinding?.InteractionPoint != null
                ? CharacterBinding.InteractionPoint
                : GameObjectReference != null ? GameObjectReference.transform : null,
            _ => null
        };

        internal static ConvaiResolvedActionTarget FromObject(ConvaiActionObjectDefinition actionObject) =>
            new()
            {
                Kind = ConvaiActionTargetKind.Object,
                Name = actionObject?.Name ?? string.Empty,
                ObjectBinding = actionObject
            };

        internal static ConvaiResolvedActionTarget FromCharacter(ConvaiActionCharacterDefinition character) =>
            new()
            {
                Kind = ConvaiActionTargetKind.Character,
                Name = character?.Name ?? string.Empty,
                CharacterBinding = character
            };

        /// <summary>
        ///     Resolves against an action's target requirement, which is a <em>preference</em>: when
        ///     the requested kind matches nothing, the other kind still comes back as the near miss
        ///     so the caller can say which it found.
        /// </summary>
        /// <remarks>
        ///     The caller is expected to check the kind it got — <c>ConvaiActionTargetResolution</c>
        ///     does, through <c>SatisfiesRequirement</c>, and turns a near miss into
        ///     <c>WrongKind</c> rather than into an execution.
        /// </remarks>
        internal static ConvaiResolvedActionTarget Resolve(
            string targetName,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement? targetRequirement,
            Vector3? origin = null) =>
            ResolveCore(targetName, actionConfig, RequiredKindOf(targetRequirement), origin, KindIsAPreference);

        /// <summary>
        ///     Resolves against a kind the caller already knows, which is a <em>constraint</em>: a
        ///     named kind that matches nothing resolves to nothing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The difference from the requirement overload above is not cosmetic and it is the
        ///         one thing about this type that is easy to get wrong — it was got wrong, by folding
        ///         both overloads onto one ladder without noticing they had never agreed.
        ///     </para>
        ///     <para>
        ///         The only caller is <c>ConvaiActionTargetReferenceResolver</c>, re-resolving a
        ///         parameter whose kind a previous read already determined. It hands the result
        ///         straight on to be checked against the <em>action's</em> requirement, which is a
        ///         weaker constraint than the kind — so an <c>Either</c> action asking again for a
        ///         reference it knows to be a character would happily accept an object of that name.
        ///         That is the silently-wrong-target class this ladder exists to close, and it can
        ///         only be closed here, where the kind is still known to be a constraint.
        ///     </para>
        /// </remarks>
        internal static ConvaiResolvedActionTarget Resolve(
            string targetName,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetKind? targetKind,
            Vector3? origin = null) =>
            ResolveCore(targetName, actionConfig, RequiredKindOf(targetKind), origin, KindIsAConstraint);

        /// <summary>
        ///     Whether a candidate of the wrong kind may be returned as the near miss when nothing of
        ///     the requested kind matched. Named rather than passed as a bare <c>bool</c>, because
        ///     the two call sites above differ by exactly this and by nothing else.
        /// </summary>
        private const bool KindIsAPreference = true;

        /// <inheritdoc cref="KindIsAPreference" />
        private const bool KindIsAConstraint = false;

        /// <summary>The kind a caller insists on, or null when either kind will do.</summary>
        private static ConvaiActionTargetKind? RequiredKindOf(ConvaiActionTargetRequirement? requirement) =>
            requirement switch
            {
                ConvaiActionTargetRequirement.Object => ConvaiActionTargetKind.Object,
                ConvaiActionTargetRequirement.Character => ConvaiActionTargetKind.Character,
                _ => null
            };

        private static ConvaiActionTargetKind? RequiredKindOf(ConvaiActionTargetKind? kind) =>
            kind is ConvaiActionTargetKind.Object or ConvaiActionTargetKind.Character ? kind : null;

        // ── The ladder ───────────────────────────────────────────────────────────────────

        private const int RungExact = 0;
        private const int RungAlias = 1;
        private const int RungNormalized = 2;
        private const int RungContains = 3;

        /// <summary>
        ///     Walks the ladder rung by rung across both kinds at once.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Rung first, kind second.</b> This used to run the whole object ladder and only
        ///         then the whole character ladder, which meant the <em>fuzziest</em> match of one
        ///         kind beat the <em>exact</em> match of the other: asked for <c>Sofia</c>, a scene
        ///         holding a character named Sofia and an object named "Sofia's Statue" resolved to
        ///         the statue, because "contains" on objects ran before "exact" on characters. It was
        ///         logged as a successful match, so it read as right rather than as wrong.
        ///     </para>
        ///     <para>
        ///         <b>Kind precedence inside a rung is unchanged, and distance never crosses it.</b>
        ///         When the action does not insist on a kind, objects are still considered before
        ///         characters — the order the two ladders ran in. It would have been easy to say
        ///         "nearest wins" across both kinds at a rung, and that would have quietly changed a
        ///         case that works today: an object named Sofia ten metres away and a character named
        ///         Sofia one metre away resolve to the object now, and still do. Proximity only ever
        ///         breaks a tie between two entries of the same kind, exactly as before.
        ///     </para>
        ///     <para>
        ///         <b>A candidate of the wrong kind never ends the search.</b> It is remembered as the
        ///         near miss — which is what lets a caller say <em>"you asked for a person and Sofia
        ///         is a statue"</em> — while the search carries on through every remaining rung
        ///         looking for the right kind. The near miss is only returned once the ladder is
        ///         exhausted, and only when the kind was a preference rather than a constraint; see
        ///         the two <c>Resolve</c> overloads.
        ///     </para>
        /// </remarks>
        private static ConvaiResolvedActionTarget ResolveCore(
            string targetName,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetKind? requiredKind,
            Vector3? origin,
            bool wrongKindIsANearMiss)
        {
            if (string.IsNullOrWhiteSpace(targetName) || actionConfig == null)
                return null;

            string normalizedQuery = NormalizeForMatch(targetName);
            bool collectNearMiss = wrongKindIsANearMiss && requiredKind.HasValue;
            var nearMiss = default(ConvaiActionTargetCandidate);

            for (int rung = RungExact; rung <= RungNormalized; rung++)
            {
                ConvaiActionTargetCandidate match = PickAtRung(
                    actionConfig, rung, targetName, normalizedQuery, requiredKind, origin);
                if (!match.IsNull)
                {
                    LogLadderMatch(rung, targetName, match.Name);
                    return match.ToResolved();
                }

                if (collectNearMiss && nearMiss.IsNull)
                {
                    nearMiss = PickAtRung(
                        actionConfig, rung, targetName, normalizedQuery, null, origin);
                }
            }

            ConvaiActionTargetCandidate contains = PickContains(
                actionConfig, normalizedQuery, requiredKind, out bool ambiguous);
            if (!contains.IsNull)
            {
                LogLadderMatch(RungContains, targetName, contains.Name);
                return contains.ToResolved();
            }

            if (collectNearMiss && nearMiss.IsNull)
                nearMiss = PickContains(actionConfig, normalizedQuery, null, out _);

            // Reported only when nothing at all came back: a request that resolved to something is
            // not a refusal to guess, whatever the fuzzy rung thought of it.
            if (ambiguous && nearMiss.IsNull && LoggingConfig.IsWarningEnabled(LogCategory.Actions))
                LogAmbiguous(targetName, actionConfig, normalizedQuery, requiredKind);

            return nearMiss.IsNull ? null : nearMiss.ToResolved();
        }

        /// <summary>
        ///     The best candidate at one rung, honouring kind precedence.
        /// </summary>
        private static ConvaiActionTargetCandidate PickAtRung(
            ConvaiActionConfig actionConfig,
            int rung,
            string query,
            string normalizedQuery,
            ConvaiActionTargetKind? requiredKind,
            Vector3? origin)
        {
            if (requiredKind.HasValue)
                return PickOfKind(actionConfig, rung, query, normalizedQuery, requiredKind.Value, origin);

            ConvaiActionTargetCandidate best = PickOfKind(
                actionConfig, rung, query, normalizedQuery, ConvaiActionTargetKind.Object, origin);
            return best.IsNull
                ? PickOfKind(actionConfig, rung, query, normalizedQuery, ConvaiActionTargetKind.Character, origin)
                : best;
        }

        /// <summary>
        ///     The one matching loop. It replaced six near-identical ones — exact, alias and
        ///     normalized, each written twice over two structurally identical entry types.
        /// </summary>
        private static ConvaiActionTargetCandidate PickOfKind(
            ConvaiActionConfig actionConfig,
            int rung,
            string query,
            string normalizedQuery,
            ConvaiActionTargetKind kind,
            Vector3? origin)
        {
            var best = default(ConvaiActionTargetCandidate);
            int count = CandidateCount(actionConfig, kind);
            for (int i = 0; i < count; i++)
            {
                ConvaiActionTargetCandidate candidate = CandidateAt(actionConfig, kind, i);
                if (candidate.IsNull || !candidate.Available)
                    continue;

                if (!MatchesAtRung(candidate, rung, query, normalizedQuery))
                    continue;

                best = PreferNearer(best, candidate, origin);
            }

            return best;
        }

        private static bool MatchesAtRung(
            in ConvaiActionTargetCandidate candidate, int rung, string query, string normalizedQuery) =>
            rung switch
            {
                RungExact => string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase),
                RungAlias => HasAliasMatch(candidate.Aliases, query),
                _ => string.Equals(
                    NormalizeForMatch(candidate.Name), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            };

        /// <summary>
        ///     The fuzzy rung, which refuses to guess between several fits.
        /// </summary>
        /// <remarks>
        ///     Uniqueness is judged <em>within</em> a kind, in kind-precedence order, which is how it
        ///     has always worked. Counting across both kinds at once would be tidier and would turn
        ///     "an object and a character both loosely match" from "resolve to the object" into
        ///     "resolve to nothing" — a command that works today, dropped. Ambiguity among objects
        ///     still does not stop characters being considered, also as before.
        /// </remarks>
        private static ConvaiActionTargetCandidate PickContains(
            ConvaiActionConfig actionConfig,
            string normalizedQuery,
            ConvaiActionTargetKind? requiredKind,
            out bool ambiguous)
        {
            if (requiredKind.HasValue)
                return PickUniqueContainsOfKind(actionConfig, normalizedQuery, requiredKind.Value, out ambiguous);

            ConvaiActionTargetCandidate best = PickUniqueContainsOfKind(
                actionConfig, normalizedQuery, ConvaiActionTargetKind.Object, out bool objectAmbiguous);
            if (!best.IsNull)
            {
                ambiguous = false;
                return best;
            }

            best = PickUniqueContainsOfKind(
                actionConfig, normalizedQuery, ConvaiActionTargetKind.Character, out bool characterAmbiguous);
            ambiguous = best.IsNull && (objectAmbiguous || characterAmbiguous);
            return best;
        }

        private static ConvaiActionTargetCandidate PickUniqueContainsOfKind(
            ConvaiActionConfig actionConfig,
            string normalizedQuery,
            ConvaiActionTargetKind kind,
            out bool ambiguous)
        {
            ambiguous = false;
            var unique = default(ConvaiActionTargetCandidate);
            if (string.IsNullOrEmpty(normalizedQuery))
                return unique;

            int matchCount = 0;
            int count = CandidateCount(actionConfig, kind);
            for (int i = 0; i < count; i++)
            {
                ConvaiActionTargetCandidate candidate = CandidateAt(actionConfig, kind, i);
                if (candidate.IsNull || !candidate.Available)
                    continue;

                if (!IsContainsMatch(NormalizeForMatch(candidate.Name), normalizedQuery))
                    continue;

                matchCount++;
                unique = candidate;
            }

            if (matchCount == 1)
                return unique;

            ambiguous = matchCount > 1;
            return default;
        }

        // ── Candidate access ─────────────────────────────────────────────────────────────

        private static int CandidateCount(ConvaiActionConfig actionConfig, ConvaiActionTargetKind kind) =>
            kind == ConvaiActionTargetKind.Object
                ? actionConfig.Objects?.Count ?? 0
                : actionConfig.Characters?.Count ?? 0;

        private static ConvaiActionTargetCandidate CandidateAt(
            ConvaiActionConfig actionConfig, ConvaiActionTargetKind kind, int index)
        {
            if (kind == ConvaiActionTargetKind.Object)
            {
                ConvaiActionObjectDefinition entry = actionConfig.Objects[index];
                return entry == null ? default : new ConvaiActionTargetCandidate(entry);
            }

            ConvaiActionCharacterDefinition character = actionConfig.Characters[index];
            return character == null ? default : new ConvaiActionTargetCandidate(character);
        }

        // ── Shared string/tie-break helpers ──────────────────────────────────────────────

        /// <summary>
        ///     Tie-break between two same-rung, same-kind matches: an entry that is bound to
        ///     something in the scene always beats one that is not; among equals, the nearest to the
        ///     origin wins, and with no origin the first match encountered is kept.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The binding rule comes first, and it is not a preference so much as a correction.
        ///         Nothing stops a project from writing a name in Scene Knowledge <em>and</em> putting
        ///         a Convai Action Target of that name on the object it describes — it is the obvious
        ///         way to do it. Whichever of the two the ladder met first used to win, so half the
        ///         time a request resolved to the entry with nothing behind it while the real object
        ///         stood right there, and the action failed for want of a target it had.
        ///     </para>
        ///     <para>
        ///         Only ever applied within one kind. Letting proximity decide between an object and
        ///         a character would change which of two same-named things a command means, silently,
        ///         based on where the character happens to be standing.
        ///     </para>
        /// </remarks>
        private static ConvaiActionTargetCandidate PreferNearer(
            ConvaiActionTargetCandidate current, ConvaiActionTargetCandidate candidate, Vector3? origin)
        {
            if (current.IsNull) return candidate;

            Vector3? currentPosition = current.AnchorPosition;
            Vector3? candidatePosition = candidate.AnchorPosition;
            if (currentPosition.HasValue != candidatePosition.HasValue)
                return currentPosition.HasValue ? current : candidate;

            if (!origin.HasValue || !candidatePosition.HasValue)
                return current;

            float currentDistanceSqr = (currentPosition.Value - origin.Value).sqrMagnitude;
            float candidateDistanceSqr = (candidatePosition.Value - origin.Value).sqrMagnitude;
            return candidateDistanceSqr < currentDistanceSqr ? candidate : current;
        }

        /// <summary>
        ///     Whether any of this entry's alternate names is the one being asked for.
        /// </summary>
        /// <remarks>
        ///     Aliases are trimmed on the way in because they are typed into a list field, where a
        ///     trailing space is invisible and never matches — a whole alias silently doing nothing,
        ///     with no way to see why from the inspector.
        /// </remarks>
        private static bool HasAliasMatch(IReadOnlyList<string> aliases, string name)
        {
            if (aliases == null) return false;
            for (int i = 0; i < aliases.Count; i++)
            {
                if (string.Equals(ConvaiActionText.Normalize(aliases[i]), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsContainsMatch(string normalizedCandidate, string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedCandidate) || string.IsNullOrEmpty(normalizedQuery))
                return false;

            return normalizedCandidate.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedQuery.IndexOf(normalizedCandidate, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Strips a leading "the"/"a"/"an" and collapses internal whitespace, case-insensitively.</summary>
        private static string NormalizeForMatch(string value)
        {
            string trimmed = ConvaiActionText.Normalize(value);
            if (trimmed.Length == 0) return trimmed;

            string collapsed = CollapseWhitespace(trimmed);
            return StripLeadingArticle(collapsed);
        }

        private static string CollapseWhitespace(string value)
        {
            bool hasRun = false;
            for (int i = 0; i < value.Length - 1; i++)
            {
                if (char.IsWhiteSpace(value[i]) && char.IsWhiteSpace(value[i + 1]))
                {
                    hasRun = true;
                    break;
                }
            }

            if (!hasRun) return value;

            var builder = new StringBuilder(value.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) builder.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
            }

            return builder.ToString();
        }

        private static string StripLeadingArticle(string value)
        {
            for (int i = 0; i < LeadingArticles.Length; i++)
            {
                string article = LeadingArticles[i];
                if (value.Length > article.Length && value.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(article.Length);
            }

            return value;
        }

        /// <summary>
        ///     Records which rung matched, at <c>Info</c>: knowing a request landed on an alias rather
        ///     than on the name itself is half the diagnosis when it lands on the wrong thing. An
        ///     exact match is not logged — there is nothing to explain about it.
        /// </summary>
        private static void LogLadderMatch(int rung, string query, string resolvedName)
        {
            if (rung == RungExact || !LoggingConfig.IsInfoEnabled(LogCategory.Actions))
                return;

            string step = rung switch
            {
                RungAlias => "alias",
                RungNormalized => "normalized",
                _ => "contains"
            };

            ConvaiLogger.Info(
                $"[ConvaiResolvedActionTarget] Target '{query}' resolved via '{step}' match to '{resolvedName}'.",
                LogCategory.Actions);
        }

        /// <summary>
        ///     Reports a refusal to guess, at <c>Warning</c>.
        /// </summary>
        /// <remarks>
        ///     This is a decision, not a trace. The ladder found several things the request could
        ///     mean and deliberately resolved to none of them, which reads from the outside as the
        ///     action doing nothing — indistinguishable from every other silent drop unless it says
        ///     so. The candidate list is the fix: one of them needs a distinguishing alias.
        /// </remarks>
        private static void LogAmbiguous(
            string query,
            ConvaiActionConfig actionConfig,
            string normalizedQuery,
            ConvaiActionTargetKind? requiredKind)
        {
            var names = new List<string>();
            CollectContainsMatches(actionConfig, normalizedQuery, ConvaiActionTargetKind.Object, requiredKind, names);
            CollectContainsMatches(actionConfig, normalizedQuery, ConvaiActionTargetKind.Character, requiredKind, names);
            if (names.Count == 0)
                return;

            ConvaiLogger.Warning(
                $"[ConvaiResolvedActionTarget] Target '{query}' matches more than one thing, so it was " +
                $"not resolved rather than guessed: {string.Join(", ", names)}. Give the " +
                "intended one an alias that only it answers to.",
                LogCategory.Actions);
        }

        private static void CollectContainsMatches(
            ConvaiActionConfig actionConfig,
            string normalizedQuery,
            ConvaiActionTargetKind kind,
            ConvaiActionTargetKind? requiredKind,
            List<string> names)
        {
            if (requiredKind.HasValue && requiredKind.Value != kind)
                return;

            int count = CandidateCount(actionConfig, kind);
            for (int i = 0; i < count; i++)
            {
                ConvaiActionTargetCandidate candidate = CandidateAt(actionConfig, kind, i);
                if (candidate.IsNull || !candidate.Available)
                    continue;

                if (IsContainsMatch(NormalizeForMatch(candidate.Name), normalizedQuery))
                    names.Add(candidate.Name);
            }
        }

        // ── Exact-name entity lookups ────────────────────────────────────────────────────

        /// <summary>
        ///     Builds an exact-name map of object targets to their bound scene objects, keeping the
        ///     first entry on a duplicate name and skipping entries with no scene binding. Unlike
        ///     the resolution ladder above this is a strict exact-name map — callers that need
        ///     alias/normalized/contains matching must go through <see cref="Resolve(string, ConvaiActionConfig, ConvaiActionTargetKind?, Vector3?)" />.
        /// </summary>
        internal static Dictionary<string, GameObject> BuildObjectEntityLookup(ConvaiActionConfig actionConfig) =>
            BuildEntityLookup(actionConfig, ConvaiActionTargetKind.Object);

        /// <summary>
        ///     Character-target counterpart of <see cref="BuildObjectEntityLookup" />, with the same
        ///     exact-name, first-wins, binding-required rules.
        /// </summary>
        internal static Dictionary<string, GameObject> BuildCharacterEntityLookup(ConvaiActionConfig actionConfig) =>
            BuildEntityLookup(actionConfig, ConvaiActionTargetKind.Character);

        private static Dictionary<string, GameObject> BuildEntityLookup(
            ConvaiActionConfig actionConfig, ConvaiActionTargetKind kind)
        {
            var lookup = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            if (actionConfig == null)
                return lookup;

            int count = CandidateCount(actionConfig, kind);
            for (int i = 0; i < count; i++)
            {
                ConvaiActionTargetCandidate candidate = CandidateAt(actionConfig, kind, i);
                if (candidate.IsNull)
                    continue;

                string name = string.IsNullOrWhiteSpace(candidate.Name) ? string.Empty : candidate.Name.Trim();
                if (name.Length == 0 || lookup.ContainsKey(name))
                    continue;

                GameObject entity = candidate.Binding;
                if (entity == null)
                    continue;

                lookup[name] = entity;
            }

            return lookup;
        }
    }
}
