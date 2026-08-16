using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private ConvaiCharacterActions _actions;
        private bool _hasPendingActionConfigSync;

        /// <summary>
        ///     Runtime target-registration surface for this character (see
        ///     <see cref="ConvaiCharacterActions" />): register/unregister objects and characters as
        ///     action grounding targets after connect, on top of the authored
        ///     <see cref="ConvaiActionConfigSource" /> list and any enabled <see cref="ConvaiActionTarget" />
        ///     components. Lazily created on first access; creation also subscribes this character to
        ///     the registry's <c>Changed</c> event so runtime target mutations stage a backend
        ///     <c>context-update</c> sync (see <see cref="MarkPendingActionConfigSync" />).
        /// </summary>
        public ConvaiCharacterActions Actions
        {
            get
            {
                if (_actions != null) return _actions;

                _actions = new ConvaiCharacterActions(this);
                _actions.Registry.Changed += OnActionTargetRegistryChanged;
                return _actions;
            }
        }

        private void OnActionTargetRegistryChanged() => MarkPendingActionConfigSync();

        /// <summary>
        ///     Stages a mid-session backend sync of this character's runtime action-target registry
        ///     through the existing dynamic-context batching window (see
        ///     <see cref="ScheduleDynamicContextFlush" />), so rapid registration bursts (e.g. a
        ///     spawn wave) coalesce into a single <c>context-update</c> on flush.
        /// </summary>
        internal void MarkPendingActionConfigSync()
        {
            _hasPendingActionConfigSync = true;
            ScheduleDynamicContextFlush();
        }

        /// <summary>
        ///     Stages one initial action-config sync on character-ready whenever this character can
        ///     act on something the backend was not told about at connect.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A fresh backend session has no memory of anything, so this cannot rely on a
        ///         registry <c>Changed</c> event having fired during this connection.
        ///     </para>
        ///     <para>
        ///         <b>It used to ask whether the runtime registry was non-empty, and that was the
        ///         wrong question.</b> The registry is only one of the sources that feed the merged
        ///         config; a <see cref="ConvaiActionTarget" /> component deliberately does not join
        ///         it — by its own design note, it keeps a polled list rather than pushing itself
        ///         into every applicable character. So a scene whose targets are all components
        ///         staged no sync at all, and the model was never told they existed: it would answer
        ///         <em>"that is not in our environment"</em> about an object sitting in front of it
        ///         that the resolution ladder could find perfectly well. Two correct designs with no
        ///         connection between them.
        ///     </para>
        ///     <para>
        ///         The question that actually matters is whether the merged view holds a name the
        ///         connect payload did not carry, so that is what is asked. It covers the registry,
        ///         scene components and target groups at once, because all three arrive the same way,
        ///         and it cannot fall behind a fourth source added later.
        ///     </para>
        /// </remarks>
        internal void StageActionConfigSyncIfBackendIsMissingTargets()
        {
            if (!HasTargetsTheConnectPayloadDidNotCarry()) return;

            MarkPendingActionConfigSync();
        }

        /// <summary>
        ///     Whether the merged runtime config names an object or character that the authored
        ///     config sent at connect does not.
        /// </summary>
        /// <remarks>
        ///     Internal rather than private so the PlayMode composition tests can assert it directly.
        ///     The behaviour it gates — a mid-session sync going out — is only observable through a
        ///     live backend, and a guard that needs a conversation to run is a guard that never runs.
        /// </remarks>
        internal bool HasTargetsTheConnectPayloadDidNotCarry()
        {
            ConvaiActionConfig merged = GetRuntimeActionConfig();
            if (merged == null) return false;

            ConvaiActionConfigSource source = GetActionConfigSource();
            ConvaiActionConfig authored = source != null ? source.BuildRuntimeResolutionConfig() : null;

            return NamesSomethingAuthoredDoesNot(merged.Objects, authored?.Objects) ||
                   NamesSomethingAuthoredDoesNot(merged.Characters, authored?.Characters);
        }

        /// <summary>
        ///     Whether <paramref name="merged" /> holds a usable name absent from
        ///     <paramref name="authored" />, compared the way the resolution ladder compares names.
        /// </summary>
        private static bool NamesSomethingAuthoredDoesNot<T>(
            IReadOnlyList<T> merged, IReadOnlyList<T> authored) where T : class
        {
            if (merged == null || merged.Count == 0) return false;

            HashSet<string> authoredNames = null;
            for (int i = 0; authored != null && i < authored.Count; i++)
            {
                string name = NameOf(authored[i]);
                if (name.Length == 0) continue;

                authoredNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                authoredNames.Add(name);
            }

            for (int i = 0; i < merged.Count; i++)
            {
                string name = NameOf(merged[i]);
                if (name.Length == 0) continue;

                if (authoredNames == null || !authoredNames.Contains(name))
                    return true;
            }

            return false;
        }

        /// <summary>Trimmed name of either target entry type; empty when it has none.</summary>
        private static string NameOf(object entry) =>
            entry switch
            {
                ConvaiActionObjectDefinition o => o.Name?.Trim() ?? string.Empty,
                ConvaiActionCharacterDefinition c => c.Name?.Trim() ?? string.Empty,
                _ => string.Empty
            };

        /// <summary>
        ///     Sends the staged action-config sync (if any) when the character is in conversation,
        ///     through the same acknowledged runtime action-state pipeline as every other runtime
        ///     action mutation: the patch is reconciled against the server-shared config, sent, and
        ///     only committed locally once the backend acknowledges it. A rejected reconciliation or
        ///     a failed send leaves the sync staged so the next flush retries it.
        /// </summary>
        /// <remarks>
        ///     The patch always carries the full current action/object/character lists rather than a
        ///     delta — backend list fields are replaced wholesale — but it travels as a
        ///     <see cref="ConvaiActionConfigPatch" /> so omitted-versus-empty semantics stay intact
        ///     for the fields this sync does not own (notably the attention object).
        /// </remarks>
        private void FlushPendingActionConfigSync()
        {
            if (!_hasPendingActionConfigSync) return;
            if (!IsInConversation) return;

            ConvaiActionConfigPatch patch = BuildActionConfigWirePatch();
            if (patch == null)
            {
                _hasPendingActionConfigSync = false;
                return;
            }

            var update = new ConvaiDynamicContextUpdate(
                null,
                ConvaiContextUpdateMode.Append,
                ConvaiRespondMode.Silent,
                actionConfig: patch,
                updateId: CreateDynamicContextUpdateId());

            if (!TryPrepareRuntimeActionStateUpdate(
                    update,
                    out ConvaiDynamicContextUpdate preparedUpdate,
                    out ConvaiActionConfigReconciliation reconciliation,
                    out bool hasRuntimeActionState,
                    out string error))
            {
                _hasPendingActionConfigSync = false;
                Logger?.Warning(
                    $"[{_characterName}] Action target registry sync rejected (invalid_action_patch): {error}");
                return;
            }

            if (!TrySendDynamicContextUpdate(preparedUpdate, "action target registry sync"))
                return;

            _hasPendingActionConfigSync = false;
            if (hasRuntimeActionState)
                RegisterPendingRuntimeActionStateUpdate(preparedUpdate, reconciliation);
        }

        /// <summary>
        ///     Builds the wire-shaped action-config patch sent to the backend: rendered action
        ///     strings plus the full current set of available objects/characters from the merged
        ///     runtime config. Unavailable actions (authored
        ///     <see cref="ConvaiActionDefinition.Enabled" /> off without an enabling
        ///     <see cref="ConvaiCharacterActions.SetActionAvailable" /> override, or a disabling
        ///     override) and unavailable/duplicate-/empty-named targets are excluded (the backend
        ///     rejects duplicate/empty names outright); local-only fields never serialize
        ///     (<see cref="ConvaiActionObjectDefinition" />/<see cref="ConvaiActionCharacterDefinition" />
        ///     already mark them <c>[JsonIgnore]</c>).
        /// </summary>
        private ConvaiActionConfigPatch BuildActionConfigWirePatch()
        {
            ConvaiActionConfig merged = GetRuntimeActionConfig();
            if (merged == null) return null;

            var snapshot = new ConvaiActionConfigPatch
            {
                Actions = BuildAvailableWireActionStrings(merged.Actions),
                Objects = new List<ConvaiActionObjectDefinition>(),
                Characters = new List<ConvaiActionCharacterDefinition>()
            };

            var seenObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < merged.Objects.Count; i++)
            {
                ConvaiActionObjectDefinition o = merged.Objects[i];
                if (o == null || !o.Available) continue;
                if (!TryReserveWireName(seenObjectNames, o.Name, "object")) continue;

                snapshot.Objects.Add(new ConvaiActionObjectDefinition
                {
                    Name = o.Name.Trim(),
                    Description = o.Description
                });
            }

            var seenCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < merged.Characters.Count; i++)
            {
                ConvaiActionCharacterDefinition c = merged.Characters[i];
                if (c == null || !c.Available) continue;
                if (!TryReserveWireName(seenCharacterNames, c.Name, "character")) continue;

                snapshot.Characters.Add(new ConvaiActionCharacterDefinition
                {
                    Name = c.Name.Trim(),
                    Bio = c.Bio
                });
            }

            return snapshot;
        }

        /// <summary>
        ///     Filters the merged config's rendered action strings down to the currently available
        ///     ones for the wire: each string's canonical action name is mapped back to its
        ///     definition (session or authored), then a
        ///     <see cref="ConvaiCharacterActions.SetActionAvailable" /> override wins over the
        ///     authored <see cref="ConvaiActionDefinition.Enabled" /> flag. Strings matching no
        ///     known definition (override-config sessions) keep their override, if any, and are
        ///     otherwise passed through unchanged.
        /// </summary>
        private List<string> BuildAvailableWireActionStrings(IReadOnlyList<string> renderedActions)
        {
            var wireActions = new List<string>(renderedActions?.Count ?? 0);
            if (renderedActions == null || renderedActions.Count == 0) return wireActions;

            // The catalog, not the session's active list: the active list is already availability
            // filtered, so a disabled action would miss the lookup and fall through as "available".
            IReadOnlyList<ConvaiActionDefinition> catalog = GetRuntimeActionDefinitionCatalog();

            // Matched by the whole rendered string, not by a name recovered from it. Recovery is a
            // parse, and it is wrong for an ordinary authoring shape — an action named 'Walk' whose
            // first parameter carries the connector 'to' renders as "Walk to {…}", which reads back
            // as 'Walk to'. That missed here and the miss defaulted to available, so an action the
            // author had disabled was offered to the Convai Character anyway.
            Dictionary<string, ConvaiActionDefinition> renderedLookup =
                ConvaiActionDefinition.BuildRenderedLookup(catalog);
            Dictionary<string, ConvaiActionDefinition> nameLookup =
                ConvaiActionDefinition.BuildLookup(catalog);

            for (int i = 0; i < renderedActions.Count; i++)
            {
                string rendered = renderedActions[i];
                ConvaiActionDefinition definition = ConvaiActionDefinition.ResolveRendered(
                    rendered, renderedLookup, nameLookup, out string canonicalName);

                bool available = true;
                if (_actions != null &&
                    _actions.TryGetActionAvailabilityOverride(canonicalName, out bool overridden))
                    available = overridden;
                else if (definition != null)
                    available = definition.Enabled;

                if (available)
                    wireActions.Add(rendered);
            }

            return wireActions;
        }

        private bool TryReserveWireName(HashSet<string> seenNames, string name, string kindLabel)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                ConvaiLogger.Debug(
                    $"[ConvaiCharacter] [{_characterName}] Skipping unnamed runtime {kindLabel} target from action-config backend sync.",
                    LogCategory.Character);
                return false;
            }

            string trimmed = name.Trim();
            if (!seenNames.Add(trimmed))
            {
                ConvaiLogger.Debug(
                    $"[ConvaiCharacter] [{_characterName}] Skipping duplicate-named runtime {kindLabel} target '{trimmed}' from action-config backend sync; keeping the first instance.",
                    LogCategory.Character);
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Builds the merged action config seen by the dispatcher, parser coercion, and
        ///     invocation reference resolution: the authored/session base config, plus this
        ///     character's explicitly-registered runtime targets (<see cref="Actions" />), plus
        ///     every enabled <see cref="ConvaiActionTarget" /> applicable to this character
        ///     (<see cref="ConvaiActionTarget.ActiveTargets" />). An authored entry always wins a
        ///     name collision; <see cref="ConvaiCharacterActions.SetTargetAvailable" /> overrides
        ///     apply last, over any source.
        /// </summary>
        /// <remarks>
        ///     Recomputed on every call rather than cached: this is invoked at most a few times per
        ///     dispatched action batch (never a per-frame path), so a fresh merge keeps runtime
        ///     target changes trivially correct with no invalidation bookkeeping. The registry's
        ///     <c>Changed</c> event (see <see cref="Actions" />) drives the separate backend
        ///     wire-sync path (<see cref="MarkPendingActionConfigSync" />/
        ///     <see cref="BuildActionConfigWirePatch" />), even though this merge does not need it
        ///     for its own correctness.
        /// </remarks>
        private ConvaiActionConfig BuildMergedRuntimeActionConfig()
        {
            // Widest-available base, in order: the session's local-resolution config (set at
            // connect, keeps disabled actions), else the confirmed server-shared config (sessions
            // configured directly, e.g. runtime-injected characters and tests), else the authored
            // source. Only the local-only lookup aids are layered on top of it below.
            ConvaiActionConfig baseConfig =
                (_hasSessionLocalResolutionConfig ? _sessionLocalResolutionConfig : null) ??
                (_hasResolvedSessionActionConfig ? _resolvedSessionActionConfig : null) ??
                GetActionConfigSource()?.BuildRuntimeResolutionConfig();
            if (baseConfig == null)
                return null;

            ConvaiActionConfig merged = baseConfig.Clone();

            var authoredObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < merged.Objects.Count; i++)
            {
                string objectName = merged.Objects[i]?.Name;
                if (!string.IsNullOrWhiteSpace(objectName)) authoredObjectNames.Add(objectName);
            }

            var authoredCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < merged.Characters.Count; i++)
            {
                string characterName = merged.Characters[i]?.Name;
                if (!string.IsNullOrWhiteSpace(characterName)) authoredCharacterNames.Add(characterName);
            }

            // The claimed-name sets are carried forward through every source, not rebuilt from the
            // base. They used to hold only the base config's names, so a source could not see what
            // the source before it had already contributed: a runtime-registered target and a
            // ConvaiActionTarget component of the same name both got appended, and the merged config
            // carried two entries under one name. Which one a command then meant was decided by
            // whichever the ladder happened to prefer — that is, by where things stood in the scene.
            AppendRegistryEntries(merged, authoredObjectNames, authoredCharacterNames);
            AppendComponentTargets(merged, authoredObjectNames, authoredCharacterNames);
            AppendGroupTargets(merged, authoredObjectNames);
            ApplyAvailabilityOverrides(merged);

            return merged;
        }

        /// <summary>
        ///     The names claimed up to this point, frozen so a source can tell what it inherited from
        ///     what it is adding itself.
        /// </summary>
        /// <remarks>
        ///     Small by construction — one entry per authored or registered target — and built once
        ///     per source rather than per entry.
        /// </remarks>
        private static HashSet<string> Snapshot(HashSet<string> claimedNames) =>
            new(claimedNames, StringComparer.OrdinalIgnoreCase);

        private void AppendRegistryEntries(
            ConvaiActionConfig merged,
            HashSet<string> authoredObjectNames,
            HashSet<string> authoredCharacterNames)
        {
            if (_actions == null) return;

            // Tested against the names taken *before this source ran*, and claimed into the shared
            // set as we go. The two are not the same thing, and collapsing them costs a shipped
            // feature: registering three chairs under the name "chair" and letting the ladder walk
            // to the nearest is what the registry is for, so entries within one source may share a
            // name. What may not happen is a later source taking a name an earlier one already
            // owns — that is the duplicate-key defect, and it is a cross-source problem only.
            HashSet<string> objectNamesTakenEarlier = Snapshot(authoredObjectNames);
            HashSet<string> characterNamesTakenEarlier = Snapshot(authoredCharacterNames);

            IReadOnlyList<ConvaiActionTargetEntry> entries = _actions.Registry.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ConvaiActionTargetEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;

                if (entry.Kind == ConvaiActionTargetKind.Object)
                {
                    if (objectNamesTakenEarlier.Contains(entry.Name)) continue;
                    authoredObjectNames.Add(entry.Name);
                    merged.Objects.Add(new ConvaiActionObjectDefinition
                    {
                        Name = entry.Name,
                        Description = entry.Description,
                        GameObjectReference = entry.GameObjectReference,
                        Aliases = new List<string>(entry.Aliases),
                        InteractionPoint = entry.InteractionPoint,
                        Available = entry.Available
                    });
                }
                else if (entry.Kind == ConvaiActionTargetKind.Character)
                {
                    if (characterNamesTakenEarlier.Contains(entry.Name)) continue;
                    authoredCharacterNames.Add(entry.Name);
                    merged.Characters.Add(new ConvaiActionCharacterDefinition
                    {
                        Name = entry.Name,
                        Bio = entry.Description,
                        GameObjectReference = entry.GameObjectReference,
                        Aliases = new List<string>(entry.Aliases),
                        InteractionPoint = entry.InteractionPoint,
                        Available = entry.Available
                    });
                }
            }
        }

        private void AppendComponentTargets(
            ConvaiActionConfig merged,
            HashSet<string> authoredObjectNames,
            HashSet<string> authoredCharacterNames)
        {
            // See AppendRegistryEntries: an earlier source's claim blocks this one, but two
            // Convai Action Target components in the scene may carry the same name — two doors, two
            // chairs — and the ladder tells them apart by proximity.
            HashSet<string> objectNamesTakenEarlier = Snapshot(authoredObjectNames);
            HashSet<string> characterNamesTakenEarlier = Snapshot(authoredCharacterNames);

            IReadOnlyList<ConvaiActionTarget> activeTargets = ConvaiActionTarget.ActiveTargets;
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ConvaiActionTarget target = activeTargets[i];
                if (target == null || !target.AppliesToCharacter(this)) continue;

                string targetName = target.TargetName;
                if (string.IsNullOrWhiteSpace(targetName)) continue;

                if (target.Kind == ConvaiActionTargetKind.Object)
                {
                    if (objectNamesTakenEarlier.Contains(targetName))
                    {
                        // The authored entry keeps its own text and takes this component's object when
                        // it has none of its own - see CompleteAuthoredObject.
                        if (!CompleteAuthoredObject(merged, targetName, target))
                            Actions.LogAuthoredCollisionOnce(targetName);
                        continue;
                    }

                    authoredObjectNames.Add(targetName);
                    merged.Objects.Add(new ConvaiActionObjectDefinition
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
                    if (characterNamesTakenEarlier.Contains(targetName))
                    {
                        if (!CompleteAuthoredCharacter(merged, targetName, target))
                            Actions.LogAuthoredCollisionOnce(targetName);
                        continue;
                    }

                    authoredCharacterNames.Add(targetName);
                    merged.Characters.Add(new ConvaiActionCharacterDefinition
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
        }

        /// <summary>
        ///     Folds every enabled <see cref="ConvaiActionTargetGroup" /> into the merged config's
        ///     object list, as an object entry whose <c>GameObjectReference</c> is the group's own
        ///     GameObject: this lets the group's name resolve through the exact same ladder as an
        ///     authored object (<see cref="ConvaiResolvedActionTarget" /> needs no group-specific
        ///     branch), while a consumer that resolves the target can read
        ///     <see cref="ConvaiActionTargetGroup.Members" />/<see cref="ConvaiActionTargetGroup.IsOrdered" />
        ///     straight off the resolved <c>GameObjectReference</c>.
        /// </summary>
        /// <summary>
        ///     Gives an authored object entry the scene object it does not have, from the same-named
        ///     <see cref="ConvaiActionTarget" /> standing in the scene. Returns true when it filled
        ///     something in - that is, when this was a completion rather than a real name collision.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         "Write the name in Scene Knowledge, put a Convai Action Target on the object" is the
        ///         obvious way to do this, and before this existed it produced a character that knew the
        ///         name and could act on nothing: the authored entry won the name, and the component
        ///         carrying the actual object was dropped. The authored entry still wins - for its text,
        ///         which is what its author wrote it for - and the component supplies the parts the
        ///         entry never had.
        ///     </para>
        ///     <para>
        ///         The object binding is the only thing a collision can be about, and only when the two
        ///         answers differ: an entry that already points at <em>this same</em> object is being
        ///         described twice, not contested. An entry marked
        ///         <see cref="ConvaiActionObjectDefinition.TextOnly" /> keeps that decision - its author
        ///         said there is nothing in the scene to act on.
        ///     </para>
        ///     <para>
        ///         <b>Aliases always merge, whatever the binding says.</b> They used to be dropped
        ///         whenever the authored entry had an object of its own, which is the setup the
        ///         <c>Convai_ConfigureActions</c> tool asks for - so following the recommended flow
        ///         silently threw away every alternate name on the component. Alternate names are not
        ///         two answers to one question; they are more ways to ask it, and more of them is never
        ///         a conflict.
        ///     </para>
        /// </remarks>
        private static bool CompleteAuthoredObject(
            ConvaiActionConfig merged, string targetName, ConvaiActionTarget target)
        {
            for (int i = 0; i < merged.Objects.Count; i++)
            {
                ConvaiActionObjectDefinition entry = merged.Objects[i];
                if (entry == null ||
                    !string.Equals(entry.Name?.Trim(), targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Merged first and unconditionally: whether the two entries agree about which
                // object they mean has no bearing on whether both spellings should work. The list is
                // created when absent, or the merge would silently do nothing.
                entry.Aliases ??= new List<string>();
                MergeAliases(entry.Aliases, target.Aliases);

                // Completed the same way the binding is, and for the same reason: the earlier
                // source's wording wins, but a blank is not wording. An entry named in Scene
                // Knowledge with no description, next to a Convai Action Target that has one, used
                // to send the character nothing — the author had written the sentence and it went
                // nowhere.
                if (string.IsNullOrWhiteSpace(entry.Description))
                    entry.Description = target.Description;

                if (entry.TextOnly)
                    return true;

                if (entry.GameObjectReference != null)
                    // Two answers only conflict when they differ; the same object described twice is
                    // the ordinary way of setting one up, not a mistake worth warning about.
                    return ReferenceEquals(entry.GameObjectReference, target.gameObject);

                entry.GameObjectReference = target.gameObject;
                entry.InteractionPoint ??= target.InteractionPoint;
                return true;
            }

            return false;
        }

        /// <summary>Character-entry counterpart of <see cref="CompleteAuthoredObject" />.</summary>
        private static bool CompleteAuthoredCharacter(
            ConvaiActionConfig merged, string targetName, ConvaiActionTarget target)
        {
            for (int i = 0; i < merged.Characters.Count; i++)
            {
                ConvaiActionCharacterDefinition entry = merged.Characters[i];
                if (entry == null ||
                    !string.Equals(entry.Name?.Trim(), targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Merged first and unconditionally: whether the two entries agree about which
                // object they mean has no bearing on whether both spellings should work. The list is
                // created when absent, or the merge would silently do nothing.
                entry.Aliases ??= new List<string>();
                MergeAliases(entry.Aliases, target.Aliases);

                // See CompleteAuthoredObject: a blank is not wording, so a later source may fill it.
                if (string.IsNullOrWhiteSpace(entry.Bio))
                    entry.Bio = target.Bio;

                if (entry.TextOnly)
                    return true;

                if (entry.GameObjectReference != null)
                    // Two answers only conflict when they differ; the same object described twice is
                    // the ordinary way of setting one up, not a mistake worth warning about.
                    return ReferenceEquals(entry.GameObjectReference, target.gameObject);

                entry.GameObjectReference = target.gameObject;
                entry.InteractionPoint ??= target.InteractionPoint;
                return true;
            }

            return false;
        }

        /// <summary>Adds the component's alternate names that the entry does not already carry.</summary>
        private static void MergeAliases(List<string> entryAliases, IReadOnlyList<string> targetAliases)
        {
            if (entryAliases == null || targetAliases == null)
                return;

            for (int i = 0; i < targetAliases.Count; i++)
            {
                string alias = targetAliases[i];
                if (string.IsNullOrWhiteSpace(alias) || entryAliases.Contains(alias))
                    continue;

                entryAliases.Add(alias);
            }
        }

        private void AppendGroupTargets(ConvaiActionConfig merged, HashSet<string> authoredObjectNames)
        {
            // Same rule as the two sources before it, for the same reason — see
            // AppendRegistryEntries. Groups were left out of that fix on the argument that two groups
            // sharing a name is genuinely ambiguous where two chairs are not. That argument does not
            // survive contact with the precedence table: a group folds in as an ordinary object
            // entry, so two of them at different distances behave exactly like two chairs, and
            // singling them out would be a fifth behaviour change nobody declared.
            HashSet<string> namesTakenEarlier = Snapshot(authoredObjectNames);

            IReadOnlyList<ConvaiActionTargetGroup> activeGroups = ConvaiActionTargetGroup.ActiveGroups;
            for (int i = 0; i < activeGroups.Count; i++)
            {
                ConvaiActionTargetGroup group = activeGroups[i];
                if (group == null) continue;

                string groupName = group.GroupName;
                if (string.IsNullOrWhiteSpace(groupName)) continue;

                if (namesTakenEarlier.Contains(groupName))
                {
                    Actions.LogAuthoredCollisionOnce(groupName);
                    continue;
                }

                authoredObjectNames.Add(groupName);

                merged.Objects.Add(new ConvaiActionObjectDefinition
                {
                    Name = groupName,
                    Description = group.Description,
                    GameObjectReference = group.gameObject,
                    Available = true
                });
            }
        }

        private void ApplyAvailabilityOverrides(ConvaiActionConfig merged)
        {
            if (_actions == null) return;

            for (int i = 0; i < merged.Objects.Count; i++)
            {
                ConvaiActionObjectDefinition o = merged.Objects[i];
                if (o != null && _actions.TryGetAvailabilityOverride(o.Name, out bool available))
                    o.Available = available;
            }

            for (int i = 0; i < merged.Characters.Count; i++)
            {
                ConvaiActionCharacterDefinition c = merged.Characters[i];
                if (c != null && _actions.TryGetAvailabilityOverride(c.Name, out bool available))
                    c.Available = available;
            }
        }
    }
}
