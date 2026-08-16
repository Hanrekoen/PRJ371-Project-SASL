using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>Which section of a <see cref="ConvaiBodyAnimationSet" /> a matched clip belongs to.</summary>
    internal enum BodyAnimationSlotCategory
    {
        /// <summary>No slot vocabulary recognised the clip name — reported, never silently dropped.</summary>
        Unmatched = 0,
        Idle,
        Talk,
        Locomotion,
        Pointing,

        /// <summary>Everything else — proposed as a named action so nothing is ever left out.</summary>
        Action
    }

    /// <summary>
    ///     The 26 slots on <see cref="LocomotionSection" />, one entry per named property, in the
    ///     same order the section declares them. Kept as an enum (rather than the raw field-name
    ///     string) so <see cref="BodyAnimationSetBuilder" /> can switch on it without a typo risk.
    /// </summary>
    internal enum BodyAnimationLocomotionSlot
    {
        Walk,
        Jog,
        WalkStartForward,
        WalkStart90Left,
        WalkStart90Right,
        WalkStart180Left,
        WalkStart180Right,
        JogStartForward,
        JogStart90Left,
        JogStart90Right,
        JogStart180Left,
        JogStart180Right,
        WalkStopLeftPlant,
        WalkStopRightPlant,
        WalkStopLowSpeed,
        WalkStopAbrupt,
        JogStopLeftPlant,
        JogStopAbrupt,
        WalkToJogLeft,
        WalkToJogRight,
        JogToWalkLeft,
        JogToWalkRight,
        Turn90Left,
        Turn90Right,
        Turn180Left,
        Turn180Right
    }

    /// <summary>
    ///     How sure the matcher is about a proposal — shown next to every row so a reviewer knows
    ///     which ones are worth a second look before confirming.
    /// </summary>
    internal enum BodyAnimationMatchConfidence
    {
        /// <summary>No slot was matched, or a recognised prefix carried an unrecognised suffix.</summary>
        None = 0,

        /// <summary>Generic fallback — the clip name carries no recognised vocabulary at all.</summary>
        Low,

        /// <summary>A plausible alternate spelling of a known token, or a recognised gesture word
        /// used only to guess an action's masking/cue (the mapping itself, not the word, is a guess).</summary>
        Medium,

        /// <summary>An exact token from the documented Convai animation naming convention.</summary>
        High
    }

    /// <summary>
    ///     The immutable result of matching one clip name against the slot vocabulary.
    ///     <see cref="BodyAnimationClipProposal" /> wraps this with the actual clip reference and the
    ///     mutable fields a reviewer edits before anything is written to an asset.
    /// </summary>
    internal readonly struct BodyAnimationSlotMatch
    {
        internal BodyAnimationSlotCategory Category { get; }
        internal BodyAnimationLocomotionSlot LocomotionSlot { get; }
        internal string PointingDirection { get; }
        internal float PointingYaw { get; }
        internal float PointingPitch { get; }
        internal string ProposedActionName { get; }
        internal ActionMaskMode ProposedMaskMode { get; }
        internal ActionLoopMode ProposedLoopMode { get; }
        internal GestureCueKind ProposedCue { get; }
        internal string[] ProposedAliases { get; }
        internal BodyAnimationMatchConfidence Confidence { get; }

        /// <summary>Human-readable explanation shown as a tooltip — never used for matching logic.</summary>
        internal string Reason { get; }

        internal bool IsMatch => Category != BodyAnimationSlotCategory.Unmatched;

        private BodyAnimationSlotMatch(
            BodyAnimationSlotCategory category,
            BodyAnimationLocomotionSlot locomotionSlot,
            string pointingDirection,
            float pointingYaw,
            float pointingPitch,
            string proposedActionName,
            ActionMaskMode proposedMaskMode,
            ActionLoopMode proposedLoopMode,
            GestureCueKind proposedCue,
            string[] proposedAliases,
            BodyAnimationMatchConfidence confidence,
            string reason)
        {
            Category = category;
            LocomotionSlot = locomotionSlot;
            PointingDirection = pointingDirection;
            PointingYaw = pointingYaw;
            PointingPitch = pointingPitch;
            ProposedActionName = proposedActionName;
            ProposedMaskMode = proposedMaskMode;
            ProposedLoopMode = proposedLoopMode;
            ProposedCue = proposedCue;
            ProposedAliases = proposedAliases ?? Array.Empty<string>();
            Confidence = confidence;
            Reason = reason;
        }

        internal static BodyAnimationSlotMatch Unmatched(string reason) =>
            new(BodyAnimationSlotCategory.Unmatched, default, null, 0f, 0f, null,
                ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, null,
                BodyAnimationMatchConfidence.None, reason);

        internal static BodyAnimationSlotMatch ForIdle(BodyAnimationMatchConfidence confidence, string reason) =>
            new(BodyAnimationSlotCategory.Idle, default, null, 0f, 0f, null,
                ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, null, confidence, reason);

        internal static BodyAnimationSlotMatch ForTalk(BodyAnimationMatchConfidence confidence, string reason) =>
            new(BodyAnimationSlotCategory.Talk, default, null, 0f, 0f, null,
                ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, null, confidence, reason);

        internal static BodyAnimationSlotMatch ForLocomotion(
            BodyAnimationLocomotionSlot slot, BodyAnimationMatchConfidence confidence, string reason) =>
            new(BodyAnimationSlotCategory.Locomotion, slot, null, 0f, 0f, null,
                ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, null, confidence, reason);

        internal static BodyAnimationSlotMatch ForPointing(string direction, float yaw, float pitch, string reason) =>
            new(BodyAnimationSlotCategory.Pointing, default, direction, yaw, pitch, null,
                ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, null,
                BodyAnimationMatchConfidence.High, reason);

        internal static BodyAnimationSlotMatch ForAction(
            string proposedName,
            ActionMaskMode maskMode,
            ActionLoopMode loopMode,
            GestureCueKind cue,
            string[] aliases,
            BodyAnimationMatchConfidence confidence,
            string reason) =>
            new(BodyAnimationSlotCategory.Action, default, null, 0f, 0f, proposedName,
                maskMode, loopMode, cue, aliases, confidence, reason);
    }

    /// <summary>
    ///     One clip's editable proposal: the matcher's suggestion, plus whatever a reviewer changes
    ///     before confirming. Every field here is mutable by design — a proposal is data a UI edits
    ///     in place; <see cref="BodyAnimationSetBuilder" /> only ever reads confirmed
    ///     (<see cref="Included" />) proposals, and nothing is written to an animation set until the
    ///     caller explicitly builds.
    /// </summary>
    internal sealed class BodyAnimationClipProposal
    {
        internal AnimationClip Clip;

        /// <summary>The category the reviewer has settled on — starts equal to the matcher's guess.</summary>
        internal BodyAnimationSlotCategory Category;
        internal BodyAnimationLocomotionSlot LocomotionSlot;
        internal string PointingDirection;
        internal float PointingYaw;
        internal float PointingPitch;
        internal string ActionName;
        internal ActionMaskMode ActionMaskMode;
        internal ActionLoopMode ActionLoopMode;
        internal GestureCueKind ActionCue;
        internal string[] ActionAliases;

        /// <summary>
        ///     Whether this clip is written when the set is built. Defaults to <c>false</c> for a
        ///     clip the matcher could not place (<see cref="BodyAnimationSlotCategory.Unmatched" />)
        ///     so an unreviewed row can never silently produce a broken, unnamed entry — every other
        ///     category defaults to included, since a fallback Action proposal is always usable.
        /// </summary>
        internal bool Included;

        /// <summary>True once a reviewer changes anything the matcher originally proposed.</summary>
        internal bool IsOverridden;

        /// <summary>The matcher's original guess — kept so the UI can show "why" and offer a reset.</summary>
        internal BodyAnimationMatchConfidence Confidence;
        internal string Reason;

        internal static BodyAnimationClipProposal FromMatch(AnimationClip clip, BodyAnimationSlotMatch match)
        {
            return new BodyAnimationClipProposal
            {
                Clip = clip,
                Category = match.Category,
                LocomotionSlot = match.LocomotionSlot,
                PointingDirection = match.PointingDirection,
                PointingYaw = match.PointingYaw,
                PointingPitch = match.PointingPitch,
                ActionName = match.Category == BodyAnimationSlotCategory.Action
                    ? match.ProposedActionName
                    : null,
                ActionMaskMode = match.ProposedMaskMode,
                ActionLoopMode = match.ProposedLoopMode,
                ActionCue = match.ProposedCue,
                ActionAliases = (string[])match.ProposedAliases.Clone(),
                Included = match.Category != BodyAnimationSlotCategory.Unmatched,
                Confidence = match.Confidence,
                Reason = match.Reason
            };
        }
    }

    /// <summary>
    ///     Maps conventional Convai animation clip names to <see cref="ConvaiBodyAnimationSet" />
    ///     slots. Pure and UI-free by design — every rule here was generalised out of a
    ///     hand-written per-clip table, so a character
    ///     archetype that follows the same naming convention (a future male or creature library) gets
    ///     a one-click set instead of hand-filling 26 locomotion slots and 15 pointing directions.
    /// </summary>
    /// <remarks>
    ///     Matching is case- and separator-insensitive (space/dash/underscore are equivalent), ignores
    ///     a leading "Anim" prefix token, and ignores a standalone gender token ("F"/"M"). A clip name
    ///     is tokenized on its ORIGINAL separators only — compound words the source files never split
    ///     (e.g. "WalkStart", "JogStopAbrupt") stay one token, which is exactly how the shipped female
    ///     library is named and is what keeps the vocabulary unambiguous without case-boundary
    ///     guessing.
    /// </remarks>
    internal static class BodyAnimationClipMatcher
    {
        /// <summary>
        ///     Yaw/pitch for each of the 15 pointing directions, lifted verbatim from the dead
        ///     wizard's table. Suffix codes are <c>{C|D|U}{F|R|L|B|BL}</c> — Center/Down/Up ×
        ///     Forward/Right/Left/Back/Back-Left.
        /// </summary>
        private static readonly (string Suffix, float Yaw, float Pitch)[] PointingTable =
        {
            ("CF", 0f, 0f), ("CR", 90f, 0f), ("CL", -90f, 0f), ("CB", 180f, 0f), ("CBL", -135f, 0f),
            ("DF", 0f, -45f), ("DR", 90f, -45f), ("DL", -90f, -45f), ("DB", 180f, -45f), ("DBL", -135f, -45f),
            ("UF", 0f, 45f), ("UR", 90f, 45f), ("UL", -90f, 45f), ("UB", 180f, 45f), ("UBL", -135f, 45f)
        };

        /// <summary>
        ///     Recognised gesture/action tokens and the defaults their presence suggests. Generalises
        ///     the wizard's hardcoded per-clip action table (mask mode, loop mode, conversational cue,
        ///     aliases) into name-based heuristics, so a third-party clip named e.g. "Greet_Wave"
        ///     still proposes an Upper Body, Greeting-cued action instead of a bare full-body guess.
        /// </summary>
        private static readonly Dictionary<string, (ActionMaskMode Mask, ActionLoopMode Loop, GestureCueKind Cue, string[] Aliases)>
            ActionHeuristics = new(StringComparer.Ordinal)
            {
                ["hi"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "wave", "hello", "greet" }),
                ["wave"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "hi", "hello", "greet" }),
                ["hello"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "hi", "wave" }),
                ["greet"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "hi", "wave" }),
                ["bye"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "goodbye", "farewell" }),
                ["goodbye"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Greeting, new[] { "bye" }),
                ["yes"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Affirmative, new[] { "nod", "agree" }),
                ["nod"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Affirmative, new[] { "yes" }),
                ["no"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.Negative, new[] { "disagree" }),
                ["think"] = (ActionMaskMode.UpperBody, ActionLoopMode.HoldUntilStopped, GestureCueKind.Uncertain, new[] { "ponder", "hmm" }),
                ["wink"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.None, Array.Empty<string>()),
                ["like"] = (ActionMaskMode.UpperBody, ActionLoopMode.PlayOnce, GestureCueKind.None, new[] { "thumbsup" }),
                ["clap"] = (ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, new[] { "applaud" }),
                ["applaud"] = (ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, new[] { "clap" }),
                ["dance"] = (ActionMaskMode.FullBody, ActionLoopMode.HoldUntilStopped, GestureCueKind.None, Array.Empty<string>()),
                ["disco"] = (ActionMaskMode.FullBody, ActionLoopMode.HoldUntilStopped, GestureCueKind.None, new[] { "dance" }),
                ["gstyle"] = (ActionMaskMode.FullBody, ActionLoopMode.HoldUntilStopped, GestureCueKind.None, new[] { "dance" }),
                ["groove"] = (ActionMaskMode.FullBody, ActionLoopMode.HoldUntilStopped, GestureCueKind.None, new[] { "dance" }),
                ["jump"] = (ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, Array.Empty<string>()),
                ["jump360"] = (ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None, new[] { "jump" })
            };

        /// <summary>Matches every clip and appends an editable proposal for each — <paramref name="proposals" /> is cleared first.</summary>
        internal static void MatchAll(IReadOnlyList<AnimationClip> clips, List<BodyAnimationClipProposal> proposals)
        {
            if (proposals == null) return;
            proposals.Clear();
            if (clips == null) return;

            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null) continue;

                BodyAnimationSlotMatch match = Match(clip.name);
                proposals.Add(BodyAnimationClipProposal.FromMatch(clip, match));
            }
        }

        /// <summary>Matches a single clip name against the full slot vocabulary.</summary>
        internal static BodyAnimationSlotMatch Match(string clipName)
        {
            List<string> tokens = Tokenize(clipName);
            if (tokens.Count == 0)
                return BodyAnimationSlotMatch.Unmatched("Clip has no usable name.");

            // Idle / Talk pools: an exact single-token name is the documented convention (High); the
            // word appearing alongside others (a future "Idle_Bored" variant) still belongs to the
            // pool, just flagged (Medium) so a reviewer double-checks it was not meant as an action.
            if (tokens.Contains("idle"))
                return BodyAnimationSlotMatch.ForIdle(
                    tokens.Count == 1 ? BodyAnimationMatchConfidence.High : BodyAnimationMatchConfidence.Medium,
                    tokens.Count == 1 ? "Exact match on \"Idle\"." : "Contains \"Idle\" alongside other tokens.");

            if (tokens.Contains("talk"))
                return BodyAnimationSlotMatch.ForTalk(
                    tokens.Count == 1 ? BodyAnimationMatchConfidence.High : BodyAnimationMatchConfidence.Medium,
                    tokens.Count == 1 ? "Exact match on \"Talk\"." : "Contains \"Talk\" alongside other tokens.");

            // Pointing: Anim_F_Point_CF is the documented convention. "Piont" is also accepted —
            // not because the SDK ships it (it doesn't; the shipped clips were renamed off that
            // typo), but because it's an easy transposition to make in a folder of third-party
            // clips, and rejecting an otherwise-valid pointing clip over one swapped letter would
            // be needlessly strict for an authoring tool.
            if (tokens.Count == 2 && (tokens[0] == "piont" || tokens[0] == "point"))
            {
                string direction = tokens[1].ToUpperInvariant();
                if (TryResolvePointingDirection(direction, out float yaw, out float pitch))
                {
                    return BodyAnimationSlotMatch.ForPointing(direction, yaw, pitch,
                        $"Pointing direction \"{direction}\" matched from \"{tokens[0]}\".");
                }

                return BodyAnimationSlotMatch.Unmatched(
                    $"\"{tokens[0]}\" looks like a pointing clip but \"{tokens[1]}\" is not a recognised " +
                    "direction (expected {C|D|U}{F|R|L|B|BL}).");
            }

            // Locomotion — the 26-slot vocabulary. Ambiguous names (e.g. "Walking", "WalkCycle") never
            // match here by design: only the documented tokens/compounds resolve to a slot.
            if (TryMatchLocomotion(tokens, out BodyAnimationLocomotionSlot slot, out BodyAnimationMatchConfidence locomotionConfidence, out string locomotionReason))
                return BodyAnimationSlotMatch.ForLocomotion(slot, locomotionConfidence, locomotionReason);

            // Everything else is a candidate action — never dropped, always reviewable. A recognised
            // gesture token still gets sensible defaults; anything unrecognised gets a safe full-body
            // fallback flagged Low so it stands out for review.
            string canonicalName = string.Join("_", tokens);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!ActionHeuristics.TryGetValue(tokens[i], out var heuristic)) continue;

                return BodyAnimationSlotMatch.ForAction(
                    canonicalName, heuristic.Mask, heuristic.Loop, heuristic.Cue, heuristic.Aliases,
                    BodyAnimationMatchConfidence.Medium,
                    $"Recognised gesture token \"{tokens[i]}\" — masking/cue defaults applied, review before confirming.");
            }

            return BodyAnimationSlotMatch.ForAction(
                canonicalName, ActionMaskMode.FullBody, ActionLoopMode.PlayOnce, GestureCueKind.None,
                Array.Empty<string>(), BodyAnimationMatchConfidence.Low,
                "No slot vocabulary matched — proposed as a full-body action; review the name and masking.");
        }

        // ------------------------------------------------------------------ locomotion

        private static bool TryMatchLocomotion(
            List<string> tokens,
            out BodyAnimationLocomotionSlot slot,
            out BodyAnimationMatchConfidence confidence,
            out string reason)
        {
            slot = default;
            confidence = BodyAnimationMatchConfidence.None;
            reason = null;

            if (tokens.Count == 1)
            {
                if (tokens[0] == "walk")
                {
                    slot = BodyAnimationLocomotionSlot.Walk;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "Exact match on \"Walk\".";
                    return true;
                }
                if (tokens[0] == "jog")
                {
                    slot = BodyAnimationLocomotionSlot.Jog;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "Exact match on \"Jog\".";
                    return true;
                }
                return false;
            }

            if (tokens.Count != 2) return false;

            string head = tokens[0];
            string tail = tokens[1];

            switch (head)
            {
                case "walkstart":
                    return TryMatchDirectionalStart(tail, false, out slot, out confidence, out reason);
                case "jogstart":
                    return TryMatchDirectionalStart(tail, true, out slot, out confidence, out reason);

                case "walkstopabrupt":
                    return Resolve(BodyAnimationLocomotionSlot.WalkStopAbrupt, "Abrupt walk stop.", out slot, out confidence, out reason);
                case "jogstopabrupt":
                    return Resolve(BodyAnimationLocomotionSlot.JogStopAbrupt, "Abrupt jog stop.", out slot, out confidence, out reason);

                case "walkstop":
                    if (tail == "lf") return Resolve(BodyAnimationLocomotionSlot.WalkStopLeftPlant, "Left-foot plant stop.", out slot, out confidence, out reason);
                    if (tail == "rf") return Resolve(BodyAnimationLocomotionSlot.WalkStopRightPlant, "Right-foot plant stop.", out slot, out confidence, out reason);
                    if (tail == "lowspeed") return Resolve(BodyAnimationLocomotionSlot.WalkStopLowSpeed, "Low-speed stop.", out slot, out confidence, out reason);
                    reason = $"\"WalkStop\" recognised but suffix \"{tail}\" is not (expected LF/RF/LowSpeed).";
                    return false;

                case "jogstop":
                    if (tail == "lf") return Resolve(BodyAnimationLocomotionSlot.JogStopLeftPlant, "Left-foot plant stop.", out slot, out confidence, out reason);
                    reason = $"\"JogStop\" recognised but suffix \"{tail}\" has no matching slot (only the left-foot plant and Abrupt exist for Jog).";
                    return false;

                case "walktojog":
                    if (tail == "lf") return Resolve(BodyAnimationLocomotionSlot.WalkToJogLeft, "Speed change, left-foot plant.", out slot, out confidence, out reason);
                    if (tail == "rf") return Resolve(BodyAnimationLocomotionSlot.WalkToJogRight, "Speed change, right-foot plant.", out slot, out confidence, out reason);
                    return false;

                case "jogtowalk":
                    if (tail == "lf") return Resolve(BodyAnimationLocomotionSlot.JogToWalkLeft, "Speed change, left-foot plant.", out slot, out confidence, out reason);
                    if (tail == "rf") return Resolve(BodyAnimationLocomotionSlot.JogToWalkRight, "Speed change, right-foot plant.", out slot, out confidence, out reason);
                    return false;

                case "turn90":
                    if (tail == "l") return Resolve(BodyAnimationLocomotionSlot.Turn90Left, "90° left turn-in-place.", out slot, out confidence, out reason);
                    if (tail == "r") return Resolve(BodyAnimationLocomotionSlot.Turn90Right, "90° right turn-in-place.", out slot, out confidence, out reason);
                    return false;

                case "turn180":
                    if (tail == "l") return Resolve(BodyAnimationLocomotionSlot.Turn180Left, "180° left turn-in-place.", out slot, out confidence, out reason);
                    if (tail == "r") return Resolve(BodyAnimationLocomotionSlot.Turn180Right, "180° right turn-in-place.", out slot, out confidence, out reason);
                    return false;

                default:
                    return false;
            }

            static bool Resolve(
                BodyAnimationLocomotionSlot resolvedSlot, string resolvedReason,
                out BodyAnimationLocomotionSlot outSlot, out BodyAnimationMatchConfidence outConfidence, out string outReason)
            {
                outSlot = resolvedSlot;
                outConfidence = BodyAnimationMatchConfidence.High;
                outReason = resolvedReason;
                return true;
            }
        }

        /// <summary>
        ///     Resolves a directional-start suffix. "RF" (right-foot lead) is the shipped convention
        ///     for the plain forward start; "fwd"/"forward"/"f" are accepted alternate spellings a
        ///     third-party vendor might use, flagged Medium since they are heuristic rather than the
        ///     documented convention.
        /// </summary>
        private static bool TryMatchDirectionalStart(
            string tail, bool isJog,
            out BodyAnimationLocomotionSlot slot, out BodyAnimationMatchConfidence confidence, out string reason)
        {
            switch (tail)
            {
                case "90l":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStart90Left : BodyAnimationLocomotionSlot.WalkStart90Left;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "90° left start.";
                    return true;
                case "90r":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStart90Right : BodyAnimationLocomotionSlot.WalkStart90Right;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "90° right start.";
                    return true;
                case "180l":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStart180Left : BodyAnimationLocomotionSlot.WalkStart180Left;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "180° left start.";
                    return true;
                case "180r":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStart180Right : BodyAnimationLocomotionSlot.WalkStart180Right;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "180° right start.";
                    return true;
                case "rf":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStartForward : BodyAnimationLocomotionSlot.WalkStartForward;
                    confidence = BodyAnimationMatchConfidence.High;
                    reason = "Forward start (shipped convention: right-foot lead).";
                    return true;
                case "fwd":
                case "forward":
                case "f":
                    slot = isJog ? BodyAnimationLocomotionSlot.JogStartForward : BodyAnimationLocomotionSlot.WalkStartForward;
                    confidence = BodyAnimationMatchConfidence.Medium;
                    reason = "Forward start (heuristic alternate spelling).";
                    return true;
            }

            slot = default;
            confidence = BodyAnimationMatchConfidence.None;
            reason = $"Directional start suffix \"{tail}\" not recognised (expected 90L/90R/180L/180R/RF).";
            return false;
        }

        // ------------------------------------------------------------------ pointing

        internal static bool TryResolvePointingDirection(string direction, out float yaw, out float pitch)
        {
            for (int i = 0; i < PointingTable.Length; i++)
            {
                if (!string.Equals(PointingTable[i].Suffix, direction, StringComparison.OrdinalIgnoreCase)) continue;
                yaw = PointingTable[i].Yaw;
                pitch = PointingTable[i].Pitch;
                return true;
            }

            yaw = 0f;
            pitch = 0f;
            return false;
        }

        /// <summary>All 15 pointing direction codes, for a UI to offer as a picker.</summary>
        internal static IReadOnlyList<string> PointingDirections
        {
            get
            {
                var directions = new string[PointingTable.Length];
                for (int i = 0; i < PointingTable.Length; i++) directions[i] = PointingTable[i].Suffix;
                return directions;
            }
        }

        // ------------------------------------------------------------------ tokenizing

        /// <summary>
        ///     Splits a clip name on its original separators (space/dash/underscore), lowercases each
        ///     token, then drops a leading "anim" marker and any standalone gender token ("f"/"m").
        ///     Tokens are never re-split by case boundary — compound words the source files keep
        ///     glued together ("WalkStart", "JogStopAbrupt") stay one token, matching the shipped
        ///     naming convention exactly.
        /// </summary>
        private static List<string> Tokenize(string raw)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return tokens;

            var builder = new System.Text.StringBuilder(raw.Length);
            void Flush()
            {
                if (builder.Length == 0) return;
                tokens.Add(builder.ToString());
                builder.Clear();
            }

            foreach (char c in raw)
            {
                if (c is ' ' or '-' or '_')
                    Flush();
                else
                    builder.Append(char.ToLowerInvariant(c));
            }
            Flush();

            if (tokens.Count > 0 && tokens[0] == "anim")
                tokens.RemoveAt(0);

            tokens.RemoveAll(t => t is "f" or "m");

            return tokens;
        }
    }
}
