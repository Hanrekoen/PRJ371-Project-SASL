using System;
using System.Collections.Generic;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Editor.Actions
{
    /// <summary>How a scanned scene target relates to a character's authored Scene Knowledge.</summary>
    internal enum ConvaiSceneKnowledgeScanStatus
    {
        /// <summary>The target's name matches an authored Known Object / Known Character entry.</summary>
        KnownByEntry = 0,

        /// <summary>
        ///     No authored entry, but the target registers itself with the character automatically
        ///     while enabled (its <c>RegisterOnEnable</c> is on and its apply scope includes the
        ///     character), so the character still knows it at runtime.
        /// </summary>
        RegistersAutomatically = 1,

        /// <summary>The character has no entry for this target and it does not auto-register for it.</summary>
        NotKnown = 2
    }

    /// <summary>
    ///     One scanned scene target reduced to the two things the reach calculation needs: the name
    ///     it would be known by, and how it relates to the character's authored entries.
    /// </summary>
    /// <remarks>
    ///     A plain value rather than the <c>ConvaiActionTarget</c> component, so the calculation
    ///     stays scene-free and unit-testable — the same reason the rest of this file is.
    /// </remarks>
    internal readonly struct ConvaiScannedTargetName
    {
        internal ConvaiScannedTargetName(string name, ConvaiSceneKnowledgeScanStatus status)
        {
            Name = name;
            Status = status;
        }

        internal string Name { get; }
        internal ConvaiSceneKnowledgeScanStatus Status { get; }
    }

    /// <summary>
    ///     What a Convai Character will actually know about the scene, split by <em>when</em> each
    ///     name reaches the backend.
    /// </summary>
    /// <remarks>
    ///     The split is the point. Authored entries travel in the connect payload
    ///     (<c>ConvaiActionConfigSource.BuildActionConfig</c>); scene
    ///     <c>ConvaiActionTarget</c> components do not — they arrive one beat later, in the
    ///     <c>context-update</c> that <c>ConvaiCharacter.StageActionConfigSyncIfBackendIsMissingTargets</c>
    ///     stages once the character is in conversation. Reporting only the first group is accurate
    ///     about the connect message and wrong about the character.
    /// </remarks>
    internal readonly struct ConvaiSceneKnowledgeReach
    {
        internal ConvaiSceneKnowledgeReach(
            int atConnectCount, IReadOnlyList<string> atConversationStart, int notDeliveredCount)
        {
            AtConnectCount = atConnectCount;
            AtConversationStart = atConversationStart;
            NotDeliveredCount = notDeliveredCount;
        }

        /// <summary>Authored entries, sent with the connect payload.</summary>
        internal int AtConnectCount { get; }

        /// <summary>
        ///     Distinct names contributed by scene targets that register themselves for this
        ///     character, in scan order. Never includes a name an authored entry already carries —
        ///     that target is classified <see cref="ConvaiSceneKnowledgeScanStatus.KnownByEntry" />
        ///     and is already counted in <see cref="AtConnectCount" />.
        /// </summary>
        internal IReadOnlyList<string> AtConversationStart { get; }

        /// <summary>Scanned targets that reach this character through neither channel.</summary>
        internal int NotDeliveredCount { get; }

        /// <summary>Everything the character ends up knowing, however it got there.</summary>
        internal int TotalKnownCount => AtConnectCount + AtConversationStart.Count;
    }

    /// <summary>Validation verdict for <c>ConvaiActionConfigSource</c>'s initial attention object name.</summary>
    internal enum ConvaiInitialAttentionStatus
    {
        /// <summary>No initial attention is set (empty / whitespace).</summary>
        NotSet = 0,

        /// <summary>The stored name matches a Known Object entry (trimmed, case-insensitive).</summary>
        Known = 1,

        /// <summary>The stored name matches no Known Object entry — the runtime will omit it with a warning.</summary>
        Unknown = 2
    }

    /// <summary>
    ///     Pure, scene-free logic behind the Actions Editor window's Scene Knowledge pane: scan-row
    ///     classification (is a scene target already known to the picked character?) and
    ///     initial-attention validation. Matching mirrors the runtime's own rules
    ///     (<c>ConvaiActionConfigSource.TryFindObjectName</c>: trim, then ordinal-ignore-case
    ///     comparison) so the pane never disagrees with what happens at connect time. No
    ///     <c>UnityEditor</c>/GUI dependency, so it is directly unit-testable.
    /// </summary>
    internal static class ConvaiActionsSceneKnowledgeModel
    {
        /// <summary>
        ///     Classifies one scanned scene target against the character's authored entries.
        ///     Entry matches win over auto-registration (an authored entry is the stronger claim);
        ///     kind decides which entry list is consulted.
        /// </summary>
        internal static ConvaiSceneKnowledgeScanStatus Classify(
            string targetName,
            ConvaiActionTargetKind kind,
            bool autoRegistersForCharacter,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters)
        {
            bool knownByEntry = kind == ConvaiActionTargetKind.Character
                ? MatchesCharacterEntry(targetName, characters)
                : MatchesObjectEntry(targetName, objects);

            if (knownByEntry)
                return ConvaiSceneKnowledgeScanStatus.KnownByEntry;

            return autoRegistersForCharacter
                ? ConvaiSceneKnowledgeScanStatus.RegistersAutomatically
                : ConvaiSceneKnowledgeScanStatus.NotKnown;
        }

        /// <summary>
        ///     Works out what the picked Convai Character will actually know about the scene, and
        ///     through which channel each name arrives.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Takes already-classified scan rows rather than re-deriving them, so the reach
        ///         reported by the Sent To Convai card and the pills shown by the Find Targets card
        ///         are two readings of one classification and cannot disagree.
        ///     </para>
        ///     <para>
        ///         Deduplication mirrors <c>ConvaiCharacter.BuildActionConfigWirePatch</c>: names are
        ///         trimmed, compared case-insensitively, and blanks are dropped — the backend rejects
        ///         duplicate and empty names outright, so a preview that counted them would promise
        ///         more than the wire delivers.
        ///     </para>
        /// </remarks>
        /// <param name="authoredObjectCount">Known Objects on the character.</param>
        /// <param name="authoredCharacterCount">Known Characters on the character.</param>
        /// <param name="scannedTargets">
        ///     The last scan's rows. Pass an empty list when no scan has run — the result then
        ///     describes the connect payload alone, which is exactly what is known at that point.
        /// </param>
        internal static ConvaiSceneKnowledgeReach ComputeReach(
            int authoredObjectCount,
            int authoredCharacterCount,
            IReadOnlyList<ConvaiScannedTargetName> scannedTargets)
        {
            int atConnect = Math.Max(0, authoredObjectCount) + Math.Max(0, authoredCharacterCount);
            if (scannedTargets == null || scannedTargets.Count == 0)
                return new ConvaiSceneKnowledgeReach(atConnect, Array.Empty<string>(), 0);

            List<string> arrivingLater = null;
            HashSet<string> seen = null;
            int notDelivered = 0;

            for (int i = 0; i < scannedTargets.Count; i++)
            {
                ConvaiScannedTargetName target = scannedTargets[i];
                if (target.Status == ConvaiSceneKnowledgeScanStatus.NotKnown)
                {
                    notDelivered++;
                    continue;
                }

                // KnownByEntry is already represented by the authored entry it matched; counting it
                // here would report the same name twice.
                if (target.Status != ConvaiSceneKnowledgeScanStatus.RegistersAutomatically)
                    continue;

                string name = target.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!seen.Add(name))
                    continue;

                arrivingLater ??= new List<string>();
                arrivingLater.Add(name);
            }

            return new ConvaiSceneKnowledgeReach(
                atConnect, (IReadOnlyList<string>)arrivingLater ?? Array.Empty<string>(), notDelivered);
        }

        /// <summary>Whether <paramref name="targetName" /> matches any Known Object entry name.</summary>
        internal static bool MatchesObjectEntry(
            string targetName,
            IReadOnlyList<ConvaiActionObjectDefinition> objects)
        {
            if (objects == null)
                return false;

            for (int i = 0; i < objects.Count; i++)
            {
                if (NamesMatch(targetName, objects[i]?.Name))
                    return true;
            }

            return false;
        }

        /// <summary>Whether <paramref name="targetName" /> matches any Known Character entry name.</summary>
        internal static bool MatchesCharacterEntry(
            string targetName,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters)
        {
            if (characters == null)
                return false;

            for (int i = 0; i < characters.Count; i++)
            {
                if (NamesMatch(targetName, characters[i]?.Name))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Validates the stored initial-attention name against the Known Objects list, using the
        ///     same trim + ordinal-ignore-case matching the runtime applies when it decides whether
        ///     to send <c>current_attention_object</c> or omit it with a warning.
        /// </summary>
        internal static ConvaiInitialAttentionStatus ValidateInitialAttention(
            string storedName,
            IReadOnlyList<ConvaiActionObjectDefinition> objects)
        {
            if (string.IsNullOrWhiteSpace(storedName))
                return ConvaiInitialAttentionStatus.NotSet;

            return MatchesObjectEntry(storedName, objects)
                ? ConvaiInitialAttentionStatus.Known
                : ConvaiInitialAttentionStatus.Unknown;
        }

        /// <summary>Trim + ordinal-ignore-case name equality, mirroring the runtime's matching.</summary>
        internal static bool NamesMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
