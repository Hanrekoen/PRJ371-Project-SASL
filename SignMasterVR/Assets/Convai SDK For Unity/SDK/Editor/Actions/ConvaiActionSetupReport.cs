using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using Convai.Editor.Inspectors;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Severity of a single action-setup finding.
    /// </summary>
    internal enum ConvaiActionTroubleshooterSeverity
    {
        Ok,
        Info,
        Warning,
        Error
    }

    /// <summary>
    ///     One actionable finding about a character's action setup, with an optional one-click,
    ///     Undo-recorded fix. <see cref="Fix" /> is a pure mutation: it records its own Undo but
    ///     never re-evaluates. Re-running the checks afterwards is the caller's job, so that Fix All
    ///     can apply a whole batch inside one Undo group and re-check exactly once.
    /// </summary>
    internal sealed class ConvaiActionTroubleshooterFinding
    {
        /// <summary>
        ///     Stable dotted id — <c>convai.actions.dispatcher.missing</c>. Never localised: titles are
        ///     user-facing text and get reworded, while this is what a fold state, a test, an MCP
        ///     response and a support thread use when they mean the same problem. Findings that repeat
        ///     per action or per target carry the subject in the id, because ids must be unique within
        ///     one report.
        /// </summary>
        internal string Id;

        internal ConvaiActionTroubleshooterSeverity Severity;
        internal string Title;
        internal string Message;
        internal string FixLabel;
        internal Action Fix;

        /// <summary>
        ///     What "Show Me" selects and pings — set only when this object genuinely is the subject.
        ///     Never a fallback: sending someone to the character because there was nothing better to
        ///     point at makes the report look wrong about an object that is fine.
        /// </summary>
        internal UnityEngine.Object Locate;

        /// <summary>
        ///     The authoring surface that answers this finding, for everything a scene object cannot —
        ///     "this action has no behavior" is a question about what was authored, not about a
        ///     GameObject, and the Actions Editor opened at that action is the only useful destination.
        /// </summary>
        internal string OpenLabel;

        internal Action Open;

        /// <summary>Prefix + title + message, composed once at evaluation so repaints never allocate.</summary>
        internal string DisplayText;
    }

    /// <summary>
    ///     Everything the product knows about one Convai Character's action setup, computed once:
    ///     component presence, action authoring, behavior bindings, behavior hosting, validator
    ///     diagnostics, Known-entry scene links, unrepresented scene targets, and the component
    ///     requirements the authored actions imply on the character and on its targets.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This exists because the counts disagreed. The Action Troubleshooter ran the checks
    ///         below; the Convai Actions inspector and the Actions Editor hero ran the
    ///         validator alone — so the same character truthfully reported "1 to fix" in the
    ///         inspector and "5 To Fix" in the Troubleshooter at the same moment. A user cannot act
    ///         on two numbers, and neither surface was obviously the liar. There is one engine now,
    ///         and every surface that shows a count reads it from here.
    ///     </para>
    ///     <para>
    ///         Running the checks sweeps the open scene, so this is not a per-repaint call. Windows
    ///         and inspectors cache a report and re-run it when the scene, the selection, or an Undo
    ///         says the answer may have changed.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiActionSetupReport
    {
        private static readonly ConvaiActionTroubleshooterFinding[] NoFindings =
            Array.Empty<ConvaiActionTroubleshooterFinding>();

        private ConvaiActionSetupReport(
            IReadOnlyList<ConvaiActionTroubleshooterFinding> findings,
            int errorCount,
            int warningCount,
            int fixableCount)
        {
            Findings = findings;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            FixableCount = fixableCount;
        }

        /// <summary>Every finding, in check order (worst-first is a presentation choice, not this one's).</summary>
        internal IReadOnlyList<ConvaiActionTroubleshooterFinding> Findings { get; }

        internal int ErrorCount { get; }

        internal int WarningCount { get; }

        /// <summary>How many findings carry a one-click fix (what Fix All would apply).</summary>
        internal int FixableCount { get; }

        /// <summary>
        ///     The single number every surface shows as "N to fix": errors plus warnings. Info and
        ///     Ok findings are explanation, not work, and must never inflate it.
        /// </summary>
        internal int IssueCount => ErrorCount + WarningCount;

        internal bool IsHealthy => IssueCount == 0;

        /// <summary>The report for "no character to check" — no findings, nothing to fix.</summary>
        internal static ConvaiActionSetupReport Empty { get; } = new(NoFindings, 0, 0, 0);

        /// <summary>Runs every check against <paramref name="character" />'s action setup.</summary>
        internal static ConvaiActionSetupReport Run(ConvaiCharacter character)
        {
            if (character == null)
                return Empty;

            var builder = new Builder(character);
            ConvaiActionSetupReport report = builder.Build();

            _cachedCharacter = character;
            _cachedReport = report;
            _cachedAt = EditorApplication.timeSinceStartup;
            return report;
        }

        #region Cache (for surfaces that ask on every repaint)

        private static ConvaiCharacter _cachedCharacter;
        private static ConvaiActionSetupReport _cachedReport;
        private static double _cachedAt = double.NegativeInfinity;
        private static ConvaiActionTarget[] _cachedSceneTargets;
        private static double _cachedSceneTargetsAt = double.NegativeInfinity;

        /// <summary>
        ///     How long a report may be reused before it is recomputed. Short enough that a stale
        ///     count is never something a person notices, long enough that dragging a window does
        ///     not sweep the scene sixty times a second.
        /// </summary>
        private const double CacheSeconds = 0.5d;

        /// <summary>
        ///     The report for <paramref name="character" />, recomputed only when it may have
        ///     changed. For surfaces like the Actions Editor hero that need the count on every
        ///     repaint and cannot afford a scene sweep each time.
        /// </summary>
        internal static ConvaiActionSetupReport Cached(ConvaiCharacter character)
        {
            if (character == null)
                return Empty;

            if (_cachedReport != null &&
                _cachedCharacter == character &&
                EditorApplication.timeSinceStartup - _cachedAt < CacheSeconds)
                return _cachedReport;

            return Run(character);
        }

        /// <summary>The scene-target sweep, reused across repaints on the same terms as the report.</summary>
        internal static ConvaiActionTarget[] CachedSceneTargets()
        {
            if (_cachedSceneTargets != null &&
                EditorApplication.timeSinceStartup - _cachedSceneTargetsAt < CacheSeconds)
                return _cachedSceneTargets;

            _cachedSceneTargets = SweepSceneTargets();
            _cachedSceneTargetsAt = EditorApplication.timeSinceStartup;
            return _cachedSceneTargets;
        }

        /// <summary>
        ///     Drops everything cached, so the next reader recomputes. Called after anything that
        ///     changes a character's action setup from outside the normal edit flow — a
        ///     Troubleshooter fix, an import — where waiting out the cache window would show the
        ///     user a number they just made wrong.
        /// </summary>
        internal static void Invalidate()
        {
            _cachedReport = null;
            _cachedCharacter = null;
            _cachedAt = double.NegativeInfinity;
            _cachedSceneTargets = null;
            _cachedSceneTargetsAt = double.NegativeInfinity;
        }

        #endregion

        /// <summary>
        ///     Runs every check against the character owning <paramref name="source" />. A config
        ///     source on a GameObject that is not a Convai Character can still be validated, but the
        ///     character-scoped checks (dispatcher, scene-target scope) have nothing to check against.
        /// </summary>
        internal static ConvaiActionSetupReport RunFor(ConvaiActionConfigSource source) =>
            source == null ? Empty : Run(source.GetComponent<ConvaiCharacter>());

        /// <summary>
        ///     The scene targets an editor surface should validate against: every
        ///     <see cref="ConvaiActionTarget" /> in the open scene, including inactive ones. Unity
        ///     never calls <c>OnEnable</c> on a plain MonoBehaviour outside play mode, so
        ///     <c>ConvaiActionTarget.ActiveTargets</c> — the list the runtime validator path reads —
        ///     is always empty in the editor and would make every unlinked entry look broken.
        /// </summary>
        internal static ConvaiActionTarget[] SweepSceneTargets() =>
            ConvaiObjectFind.All<ConvaiActionTarget>(FindObjectsInactive.Include);

        /// <summary>
        ///     Validates <paramref name="source" /> the way every editor surface must: against the
        ///     scene as it stands, not against an empty runtime registry.
        /// </summary>
        internal static IReadOnlyList<ConvaiActionConfigDiagnostic> Validate(ConvaiActionConfigSource source) =>
            ConvaiActionConfigValidator.Validate(source, SweepSceneTargets());

        /// <summary>
        ///     <see cref="Validate" /> for callers that validate on every repaint. The validator
        ///     itself is cheap; the scene sweep it now needs is not, so the sweep is the part that
        ///     is reused.
        /// </summary>
        internal static IReadOnlyList<ConvaiActionConfigDiagnostic> ValidateCached(ConvaiActionConfigSource source) =>
            ConvaiActionConfigValidator.Validate(source, CachedSceneTargets());

        /// <summary>
        ///     Mutable state for one evaluation pass. A class rather than a pile of static methods
        ///     threading eight parameters: the checks share the character, the scene sweeps, and the
        ///     growing findings list, and every sweep is paid for once per run.
        /// </summary>
        private sealed class Builder
        {
            private readonly ConvaiCharacter _character;
            private readonly List<ConvaiActionTroubleshooterFinding> _findings = new();
            private readonly ConvaiActionTarget[] _sceneTargets;

            /// <summary>
            ///     Every GameObject in the scene, swept at most once per run and only when an
            ///     unlinked entry actually needs a name lookup. The previous code swept the whole
            ///     scene once per unlinked entry.
            /// </summary>
            private GameObject[] _sceneObjects;

            /// <summary>
            ///     Known entries this run can offer a one-click link for, keyed by the validator's
            ///     own positional context ("Actionable object #3"). Built before the validator
            ///     diagnostics are mapped, so the link fix lands on the validator's row instead of
            ///     adding a second row about the same entry.
            /// </summary>
            private readonly Dictionary<string, LinkCandidate> _linkCandidates =
                new(StringComparer.Ordinal);

            /// <summary>
            ///     Actions the behavior-binding pass has already spoken for, so the validator's
            ///     duplicate of the same fact can be dropped. Populated before the validator runs —
            ///     see <see cref="Build" /> for why that order is deliberate.
            /// </summary>
            private readonly HashSet<string> _bindingReportedActions = new(StringComparer.OrdinalIgnoreCase);

            internal Builder(ConvaiCharacter character)
            {
                _character = character;
                _sceneTargets = SweepSceneTargets();
            }

            internal ConvaiActionSetupReport Build()
            {
                GameObject go = _character.gameObject;
                var source = go.GetComponent<ConvaiActionConfigSource>();
                var dispatcher = go.GetComponent<ConvaiActionDispatcher>();

                EvaluateComponentPresence(go, source, dispatcher);
                EvaluateFeedbackRelayPresence(go, dispatcher);
                if (source != null)
                {
                    EvaluateDefinitionsPresence(source);
                    EvaluateBehaviorHost(source);
                    EvaluateExecutorBindings(go, source);

                    // Link states first: the validator's unlinked-entry errors are retitled and given
                    // their fix from what this pass learns, rather than being answered by a second row.
                    EvaluateAnswerDelivery(go, source);

                    CollectTargetLinkStates(source);
                    EvaluateValidatorDiagnostics(source);
                    EvaluateSceneTargets(source);
                    EvaluatePeerRequirements(go, source);
                    EvaluateTargetComponentRequirements(go, source);
                }

                return Finish();
            }

            /// <summary>
            ///     Caches each finding's display text and counts errors, warnings and fixes, so
            ///     repaints neither allocate nor recount.
            /// </summary>
            private ConvaiActionSetupReport Finish()
            {
                int errors = 0;
                int warnings = 0;
                int fixable = 0;

                for (int i = 0; i < _findings.Count; i++)
                {
                    ConvaiActionTroubleshooterFinding finding = _findings[i];
                    string prefix = finding.Severity switch
                    {
                        ConvaiActionTroubleshooterSeverity.Ok => $"{ConvaiEditorGlyphs.Status.Ok} ",
                        ConvaiActionTroubleshooterSeverity.Error => $"{ConvaiEditorGlyphs.Status.Fail} ",
                        ConvaiActionTroubleshooterSeverity.Warning => $"{ConvaiEditorGlyphs.Status.Warn} ",
                        _ => $"{ConvaiEditorGlyphs.Status.Info} "
                    };

                    finding.DisplayText = $"{prefix}{finding.Title}: {finding.Message}";

                    if (finding.Severity == ConvaiActionTroubleshooterSeverity.Error) errors++;
                    else if (finding.Severity == ConvaiActionTroubleshooterSeverity.Warning) warnings++;

                    if (finding.Fix != null)
                        fixable++;
                }

                return new ConvaiActionSetupReport(_findings, errors, warnings, fixable);
            }

            #region Known-entry scene links

            /// <summary>What this run can do about one Known entry that carries no scene object.</summary>
            private readonly struct LinkCandidate
            {
                internal LinkCandidate(string entryName, GameObject target, Action<GameObject> applyLink)
                {
                    EntryName = entryName;
                    Target = target;
                    ApplyLink = applyLink;
                }

                internal string EntryName { get; }

                /// <summary>The one scene object that answers this entry's name, or null when the scene is ambiguous.</summary>
                internal GameObject Target { get; }

                internal Action<GameObject> ApplyLink { get; }
            }

            /// <summary>
            ///     Classifies every Known entry against the scene and records what could be linked.
            ///     Nothing is reported here: an entry that is linked or deliberately text-only is
            ///     finished, an entry a scene target answers gets its own Info row below, and an
            ///     entry that is genuinely broken is already the validator's error to report.
            /// </summary>
            private void CollectTargetLinkStates(ConvaiActionConfigSource source)
            {
                IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
                for (int i = 0; objects != null && i < objects.Count; i++)
                {
                    ConvaiActionObjectDefinition actionObject = objects[i];
                    if (actionObject == null || string.IsNullOrWhiteSpace(actionObject.Name))
                        continue;

                    ConvaiActionObjectDefinition captured = actionObject;
                    ConsiderEntry(
                        source,
                        ConvaiSceneKnowledgeLinkModel.ClassifyObject(actionObject, _sceneTargets),
                        actionObject.Name,
                        ConvaiActionTargetKind.Object,
                        $"Actionable object #{i + 1}",
                        linked => captured.GameObjectReference = linked);
                }

                IReadOnlyList<ConvaiActionCharacterDefinition> characters = source.Characters;
                for (int i = 0; characters != null && i < characters.Count; i++)
                {
                    ConvaiActionCharacterDefinition character = characters[i];
                    if (character == null || string.IsNullOrWhiteSpace(character.Name))
                        continue;

                    ConvaiActionCharacterDefinition captured = character;
                    ConsiderEntry(
                        source,
                        ConvaiSceneKnowledgeLinkModel.ClassifyCharacter(character, _sceneTargets),
                        character.Name,
                        ConvaiActionTargetKind.Character,
                        $"Actionable character #{i + 1}",
                        linked => captured.GameObjectReference = linked);
                }
            }

            private void ConsiderEntry(
                ConvaiActionConfigSource source,
                ConvaiKnownEntryLinkState state,
                string entryName,
                ConvaiActionTargetKind kind,
                string validatorContext,
                Action<GameObject> applyLink)
            {
                if (state == ConvaiKnownEntryLinkState.Linked || state == ConvaiKnownEntryLinkState.TextOnly)
                    return;

                if (state == ConvaiKnownEntryLinkState.AnsweredByTarget)
                {
                    ConvaiActionTarget answering =
                        ConvaiSceneKnowledgeLinkModel.FindTargetByName(entryName, kind, _sceneTargets);
                    if (answering == null)
                        return;

                    // Not a problem: the runtime completes this entry from the component. Said out
                    // loud anyway, because "I never linked that and it works" deserves an answer,
                    // and because linking it permanently is one click away.
                    GameObject answeringObject = answering.gameObject;
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.known-entry.answered.{Slug(entryName)}",
                        Severity = ConvaiActionTroubleshooterSeverity.Info,
                        Title = $"Target — '{entryName}'",
                        Message = ConvaiActionsEditorStrings.BuildKnownEntryAnsweredStatus(answeringObject.name).text,
                        FixLabel = ConvaiActionsEditorStrings.KnownEntryLinkTargetButton.text,
                        Fix = () => LinkKnownEntry(source, applyLink, answeringObject),
                        Locate = answeringObject
                    });
                    return;
                }

                // Unlinked. The validator already reports this entry as an error; all this pass adds
                // is the one thing the validator cannot see — whether the scene answers the name.
                _sceneObjects ??= ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Include);
                List<GameObject> matches =
                    ConvaiSceneKnowledgeLinkModel.FindObjectsByName(entryName, _sceneObjects);

                _linkCandidates[validatorContext] = new LinkCandidate(
                    entryName, matches.Count == 1 ? matches[0] : null, applyLink);
            }

            private static void LinkKnownEntry(
                ConvaiActionConfigSource source, Action<GameObject> applyLink, GameObject linked)
            {
                Undo.RecordObject(source, "Link Known Entry");
                applyLink(linked);
                EditorUtility.SetDirty(source);
            }

            #endregion

            #region Validator diagnostics

            /// <summary>
            ///     Maps every validator diagnostic into a finding (this is where the "all actions
            ///     disabled" warning surfaces) and attaches the mechanically safe one-click fixes:
            ///     removing a later duplicate inline definition, clearing an unknown starting-focus
            ///     name, and linking a Known entry the scene answers unambiguously. Attachment is
            ///     double-keyed — a structural re-check plus the validator's context/message shape —
            ///     so a fix can never land on the wrong diagnostic.
            /// </summary>
            private void EvaluateValidatorDiagnostics(ConvaiActionConfigSource source)
            {
                HashSet<int> duplicateInlineIndices = FindDuplicateInlineIndices(source);
                bool attentionUnknown =
                    ConvaiActionsSceneKnowledgeModel.ValidateInitialAttention(
                        source.InitialAttentionObject, source.Objects) == ConvaiInitialAttentionStatus.Unknown;

                IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics =
                    ConvaiActionConfigValidator.Validate(source, _sceneTargets);
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    ConvaiActionConfigDiagnostic diagnostic = diagnostics[i];

                    // The validator and the behavior-binding pass above answer the same question about
                    // an unbound action, and the pass above answers it better: it names the behavior
                    // in the words the Inspector uses and can offer to add it. Reporting both put the
                    // same sentence on screen twice, once without a fix.
                    if (IsAlreadyReportedByBindingPass(diagnostic))
                        continue;

                    var finding = new ConvaiActionTroubleshooterFinding
                    {
                        // The validator's own context ("Action definition #2") is what makes two
                        // diagnostics about different entries distinguishable, so it is what the id is
                        // built from; the special cases below give the ones with fixes a real name.
                        Id = $"convai.actions.validation.{Slug(diagnostic.Context)}",
                        Severity = diagnostic.Severity switch
                        {
                            ConvaiActionConfigDiagnosticSeverity.Error => ConvaiActionTroubleshooterSeverity.Error,
                            ConvaiActionConfigDiagnosticSeverity.Warning => ConvaiActionTroubleshooterSeverity.Warning,
                            _ => ConvaiActionTroubleshooterSeverity.Info
                        },
                        Title = BuildValidatorTitle(source, diagnostic),
                        Message = diagnostic.Message
                    };

                    AttachAuthoringDestination(source, diagnostic, finding);

                    if (TryGetDuplicateInlineIndex(diagnostic, duplicateInlineIndices, out int duplicateIndex))
                    {
                        finding.Id = $"convai.actions.definition.duplicate.{duplicateIndex}";
                        finding.FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixRemoveDuplicate.text;
                        finding.Fix = () => RemoveInlineDefinitionAt(source, duplicateIndex);
                    }
                    else if (attentionUnknown &&
                             diagnostic.Severity == ConvaiActionConfigDiagnosticSeverity.Warning &&
                             diagnostic.Message.StartsWith("Initial attention object", StringComparison.Ordinal))
                    {
                        finding.Id = "convai.actions.initial-attention.unknown";
                        finding.FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixClearAttention.text;
                        finding.Fix = () => ClearInitialAttention(source);
                    }
                    else
                    {
                        ApplyKnownEntryLink(source, diagnostic, finding);
                    }

                    _findings.Add(finding);
                }
            }

            /// <summary>
            ///     A title a person can read, from a context written for a machine.
            /// </summary>
            /// <remarks>
            ///     The validator labels its findings by position — <c>Action definition #1</c> — which
            ///     is exactly what it needs internally and exactly what a first-time user cannot use:
            ///     it names a row index in a list they have never seen. Every positional context is
            ///     resolved back to the thing it counts, so the row says <c>Action — 'Look At'</c>.
            ///     Contexts that are already plain English are passed through untouched.
            /// </remarks>
            private static string BuildValidatorTitle(
                ConvaiActionConfigSource source, ConvaiActionConfigDiagnostic diagnostic)
            {
                string context = diagnostic.Context;
                if (string.IsNullOrEmpty(context))
                    return "Action Setup";

                if (TryResolveIndexedContext(context, "Action definition #", out int index))
                {
                    string name = NameAt(source.Definitions, index, static definition => definition?.ActionName);
                    return name == null ? "Action" : $"Action — '{name}'";
                }

                if (TryResolveIndexedContext(context, "Actionable object #", out index))
                {
                    string name = NameAt(source.Objects, index, static entry => entry?.Name);
                    return name == null ? "Target" : $"Target — '{name}'";
                }

                if (TryResolveIndexedContext(context, "Actionable character #", out index))
                {
                    string name = NameAt(source.Characters, index, static entry => entry?.Name);
                    return name == null ? "Target" : $"Target — '{name}'";
                }

                return context;
            }

            /// <summary>Reads the 1-based index off a positional context, if it is one.</summary>
            private static bool TryResolveIndexedContext(string context, string prefix, out int index)
            {
                index = -1;
                if (!context.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(context.Substring(prefix.Length), out int oneBased))
                    return false;

                index = oneBased - 1;
                return index >= 0;
            }

            private static string NameAt<T>(IReadOnlyList<T> entries, int index, Func<T, string> nameOf)
            {
                if (entries == null || index >= entries.Count)
                    return null;

                string name = nameOf(entries[index]);
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }

            /// <summary>
            ///     Whether the behavior-binding pass has already reported this diagnostic's subject.
            /// </summary>
            /// <remarks>
            ///     Both passes ask "can this action actually run?" and reach the same three answers.
            ///     Only one of them can also offer to add the missing behavior and name it the way the
            ///     Inspector does, so that one wins and this one is dropped — rather than showing a
            ///     user the same sentence twice and letting them wonder which row to act on.
            /// </remarks>
            private bool IsAlreadyReportedByBindingPass(ConvaiActionConfigDiagnostic diagnostic)
            {
                if (_bindingReportedActions.Count == 0 ||
                    diagnostic.Severity == ConvaiActionConfigDiagnosticSeverity.Info)
                    return false;

                string message = diagnostic.Message;
                if (message.IndexOf("action behavior", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                foreach (string actionName in _bindingReportedActions)
                {
                    if (message.IndexOf($"'{actionName}'", StringComparison.Ordinal) >= 0)
                        return true;
                }

                return false;
            }

            /// <summary>
            ///     Gives a validator finding the authoring surface that answers it.
            /// </summary>
            /// <remarks>
            ///     These findings are about what was authored, not about an object in the scene, so
            ///     they get a destination rather than a <see cref="ConvaiActionTroubleshooterFinding.Locate" />
            ///     target. Pointing "Show Me" at the character here was actively misleading — it
            ///     selected an object that is perfectly fine and said nothing about the action.
            /// </remarks>
            private static void AttachAuthoringDestination(
                ConvaiActionConfigSource source,
                ConvaiActionConfigDiagnostic diagnostic,
                ConvaiActionTroubleshooterFinding finding)
            {
                if (!TryResolveIndexedContext(diagnostic.Context, "Action definition #", out int index))
                {
                    finding.OpenLabel = ConvaiActionsEditorStrings.InspectorOpenWindowButton.text;
                    finding.Open = () => ConvaiActionsEditorWindow.ShowWindowFor(source);
                    return;
                }

                IReadOnlyList<ConvaiActionDefinition> definitions = source.Definitions;
                ConvaiActionDefinition definition =
                    definitions != null && index < definitions.Count ? definitions[index] : null;

                finding.OpenLabel = ConvaiActionsEditorStrings.InspectorOpenWindowButton.text;
                finding.Open = definition == null
                    ? () => ConvaiActionsEditorWindow.ShowWindowFor(source)
                    : () => ConvaiActionsEditorWindow.ShowWindowFor(source, definition);
            }

            /// <summary>
            ///     Turns the validator's positional unlinked-entry error into the row a person can
            ///     act on: titled with the entry's name rather than its index, and carrying the Link
            ///     fix when the scene holds exactly one object answering to that name.
            /// </summary>
            private void ApplyKnownEntryLink(
                ConvaiActionConfigSource source,
                ConvaiActionConfigDiagnostic diagnostic,
                ConvaiActionTroubleshooterFinding finding)
            {
                if (diagnostic.Severity != ConvaiActionConfigDiagnosticSeverity.Error ||
                    diagnostic.Message.IndexOf("no scene object linked", StringComparison.Ordinal) < 0 ||
                    !_linkCandidates.TryGetValue(diagnostic.Context, out LinkCandidate candidate))
                    return;

                finding.Id = $"convai.actions.known-entry.unlinked.{Slug(candidate.EntryName)}";
                finding.Title = $"Target — '{candidate.EntryName}'";
                if (candidate.Target == null)
                    return;

                GameObject target = candidate.Target;
                Action<GameObject> applyLink = candidate.ApplyLink;
                finding.Locate = target;
                finding.FixLabel = ConvaiActionsEditorStrings.BuildKnownEntryLinkSuggestion(target.name).text;
                finding.Fix = () => LinkKnownEntry(source, applyLink, target);
            }

            /// <summary>
            ///     Inline definition indices whose normalized name already appeared earlier in the
            ///     inline list — the exact set the validator reports "Duplicate action definition"
            ///     errors for (same normalization, same first-wins order).
            /// </summary>
            private static HashSet<int> FindDuplicateInlineIndices(ConvaiActionConfigSource source)
            {
                var duplicates = new HashSet<int>();
                IReadOnlyList<ConvaiActionDefinition> definitions = source.Definitions;
                if (definitions == null)
                    return duplicates;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < definitions.Count; i++)
                {
                    string name = ConvaiActionDefinition.NormalizeActionName(definitions[i]?.ActionName);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (!seen.Add(name))
                        duplicates.Add(i);
                }

                return duplicates;
            }

            private static bool TryGetDuplicateInlineIndex(
                ConvaiActionConfigDiagnostic diagnostic,
                HashSet<int> duplicateInlineIndices,
                out int index)
            {
                index = -1;
                if (diagnostic.Severity != ConvaiActionConfigDiagnosticSeverity.Error ||
                    !diagnostic.Message.StartsWith("Duplicate action definition", StringComparison.Ordinal))
                    return false;

                // The validator's inline-definition context is "Action definition #<1-based index>".
                const string contextPrefix = "Action definition #";
                if (!diagnostic.Context.StartsWith(contextPrefix, StringComparison.Ordinal) ||
                    !int.TryParse(diagnostic.Context.Substring(contextPrefix.Length), out int oneBased))
                    return false;

                index = oneBased - 1;
                return duplicateInlineIndices.Contains(index);
            }

            /// <summary>Removes the later duplicate; the first definition with that name stays untouched.</summary>
            private static void RemoveInlineDefinitionAt(ConvaiActionConfigSource source, int index)
            {
                var list = new List<ConvaiActionDefinition>(source.Definitions);
                if (index < 0 || index >= list.Count)
                    return;

                Undo.RecordObject(source, "Remove Duplicate Action");
                list.RemoveAt(index);
                source.ReplaceDefinitions(list);
                EditorUtility.SetDirty(source);
            }

            /// <summary>
            ///     Clears the starting-focus name through a one-shot <see cref="SerializedObject" />
            ///     (the component has no tooling seam for this field); ApplyModifiedProperties
            ///     registers Undo and dirties the scene itself.
            /// </summary>
            private static void ClearInitialAttention(ConvaiActionConfigSource source)
            {
                using var serialized = new SerializedObject(source);
                SerializedProperty property = serialized.FindProperty("_initialAttentionObject");
                if (property == null)
                    return;

                property.stringValue = string.Empty;
                serialized.ApplyModifiedProperties();
            }

            #endregion

            #region Component presence

            private void EvaluateComponentPresence(
                GameObject go, ConvaiActionConfigSource source, ConvaiActionDispatcher dispatcher)
            {
                if (source == null)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.config-source.missing",
                        Severity = ConvaiActionTroubleshooterSeverity.Error,
                        Title = ConvaiActionsEditorStrings.TroubleshooterActionsEnabledTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterActionsEnabledMissingMessage,
                        FixLabel = ConvaiActionsEditorStrings.EnableActionsButton.text,
                        Fix = () => AddComponentFix<ConvaiActionConfigSource>(go),
                        Locate = go
                    });
                }
                else
                {
                    _findings.Add(Ok(
                        "convai.actions.config-source.ok",
                        ConvaiActionsEditorStrings.TroubleshooterActionsEnabledTitle,
                        ConvaiActionsEditorStrings.TroubleshooterActionsEnabledReadyMessage));
                }

                // A character whose actions are run by project code is correctly set up without a
                // dispatcher, so reporting one as missing would be a false alarm the user cannot fix.
                if (dispatcher == null &&
                    source != null &&
                    source.ActionExecutionMode == ConvaiActionExecutionMode.CustomCode)
                {
                    _findings.Add(Ok(
                        "convai.actions.dispatcher.custom-code",
                        ConvaiActionsEditorStrings.TroubleshooterRunningActionsTitle,
                        ConvaiActionsEditorStrings.TroubleshooterRunningActionsCustomCodeMessage));
                }
                else if (dispatcher == null)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.dispatcher.missing",
                        Severity = ConvaiActionTroubleshooterSeverity.Error,
                        Title = ConvaiActionsEditorStrings.TroubleshooterRunningActionsTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterRunningActionsMissingMessage,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixSetUpActionRunning.text,
                        Fix = () => AddComponentFix<ConvaiActionDispatcher>(go),
                        Locate = go
                    });
                }
                else
                {
                    _findings.Add(Ok(
                        "convai.actions.dispatcher.ok",
                        ConvaiActionsEditorStrings.TroubleshooterRunningActionsTitle,
                        ConvaiActionsEditorStrings.TroubleshooterRunningActionsReadyMessage));
                }
            }

            /// <summary>
            ///     A missing feedback relay means the LLM never learns when an action fails or
            ///     succeeds. Info-level (not an error) since a character can be perfectly functional
            ///     without spoken outcome feedback.
            /// </summary>
            private void EvaluateFeedbackRelayPresence(GameObject go, ConvaiActionDispatcher dispatcher)
            {
                if (dispatcher == null) return;

                if (go.GetComponent<ConvaiActionFeedbackRelay>() != null)
                {
                    _findings.Add(Ok(
                        "convai.actions.feedback-relay.ok",
                        ConvaiActionsEditorStrings.TroubleshooterSpokenFeedbackTitle,
                        ConvaiActionsEditorStrings.TroubleshooterSpokenFeedbackReadyMessage));
                    return;
                }

                _findings.Add(new ConvaiActionTroubleshooterFinding
                {
                    Id = "convai.actions.feedback-relay.missing",
                    Severity = ConvaiActionTroubleshooterSeverity.Info,
                    Title = ConvaiActionsEditorStrings.TroubleshooterSpokenFeedbackTitle,
                    Message = ConvaiActionsEditorStrings.TroubleshooterSpokenFeedbackMissingMessage,
                    FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixAddSpokenFeedback.text,
                    Fix = () => AddComponentFix<ConvaiActionFeedbackRelay>(go),
                    Locate = go
                });
            }

            /// <summary>
            ///     Reports actions whose answer delivery disagrees with the character's own relay
            ///     setting — an action told to say what it found on a character that speaks a fixed
            ///     scripted line, and the like.
            /// </summary>
            /// <remarks>
            ///     The verdict is <see cref="ConvaiActionAnswerDeliveryExplanations.FindAdvisory" />,
            ///     the same call the Actions Editor's Command card makes. Restating the rule here
            ///     would be a second opinion about one project, which is exactly how the same setup
            ///     came to report a different number of problems depending on which window was open.
            /// </remarks>
            private void EvaluateAnswerDelivery(GameObject go, ConvaiActionConfigSource source)
            {
                IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions();
                if (definitions == null || definitions.Count == 0) return;

                var relay = go.GetComponent<ConvaiActionFeedbackRelay>();
                ConvaiActionFeedbackMode? characterMode = relay == null ? null : relay.SuccessFeedbackMode;

                // A missing relay is already reported once, by EvaluateFeedbackRelayPresence. Saying
                // it again per action would bury the finding under its own repetitions.
                if (characterMode == null) return;

                for (int i = 0; i < definitions.Count; i++)
                {
                    ConvaiActionDefinition definition = definitions[i];
                    if (definition == null) continue;

                    ConvaiActionAnswerAdvisory advisory = ConvaiActionAnswerDeliveryExplanations.FindAdvisory(
                        definition.AnswerDelivery, characterMode, _character.name);
                    if (!advisory.Exists || !advisory.IsWarning) continue;

                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.answer-delivery.{definition.ActionName}",
                        Severity = ConvaiActionTroubleshooterSeverity.Warning,
                        Title = advisory.Title,
                        Message = $"{definition.ActionName}: {advisory.Message}",
                        Locate = source
                    });
                }
            }

            private void EvaluateDefinitionsPresence(ConvaiActionConfigSource source)
            {
                IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions();
                if (definitions == null || definitions.Count == 0)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.definitions.none",
                        Severity = ConvaiActionTroubleshooterSeverity.Info,
                        Title = ConvaiActionsEditorStrings.TroubleshooterActionsAuthoredTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterNoActionsAuthoredMessage,
                        Locate = source
                    });
                    return;
                }

                int disabledCount = ConvaiActionConfigValidator.CountDisabled(definitions);
                string summary = disabledCount > 0
                    ? $"{definitions.Count} action definition(s) authored ({disabledCount} disabled)."
                    : $"{definitions.Count} action definition(s) authored.";
                _findings.Add(Ok(
                    "convai.actions.definitions.ok",
                    ConvaiActionsEditorStrings.TroubleshooterActionsAuthoredTitle, summary));
            }

            #endregion

            #region Behavior hosting

            /// <summary>
            ///     Checks the optional child object that holds this character's action behaviors.
            /// </summary>
            /// <remarks>
            ///     Silent when no such object is assigned: behaviors on the Convai Character itself
            ///     are the default and a perfectly good layout, so a character that has not adopted
            ///     the idea must not be told anything about it here. Everything below therefore only
            ///     applies once someone has deliberately assigned one.
            /// </remarks>
            private void EvaluateBehaviorHost(ConvaiActionConfigSource source)
            {
                Transform host = source.ConfiguredBehaviorHost;
                if (host == null)
                    return;

                if (!source.HasValidBehaviorHost)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.behavior-host.outside",
                        Severity = ConvaiActionTroubleshooterSeverity.Error,
                        Title = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostOutsideMessage,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixClearBehaviorHost.text,
                        Fix = () => ConvaiActionBehaviorHosting.SetBehaviorHost(source, null),
                        Locate = host.gameObject
                    });
                    return;
                }

                if (!host.gameObject.activeInHierarchy)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.behavior-host.inactive",
                        Severity = ConvaiActionTroubleshooterSeverity.Error,
                        Title = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostInactiveMessage,
                        Locate = host.gameObject,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixActivateBehaviorHost.text,
                        Fix = () =>
                        {
                            Undo.RecordObject(host.gameObject, "Activate Action Behaviors Object");
                            host.gameObject.SetActive(true);
                            EditorUtility.SetDirty(host.gameObject);
                        }
                    });
                    return;
                }

                if (host != source.transform && !IsIdentityLocalTransform(host))
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.behavior-host.offset",
                        Severity = ConvaiActionTroubleshooterSeverity.Warning,
                        Title = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostTitle,
                        Message = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostOffsetMessage,
                        Locate = host.gameObject,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixResetBehaviorHost.text,
                        Fix = () =>
                        {
                            Undo.RecordObject(host, "Reset Action Behaviors Object");
                            host.localPosition = Vector3.zero;
                            host.localRotation = Quaternion.identity;
                            host.localScale = Vector3.one;
                            EditorUtility.SetDirty(host);
                        }
                    });
                    return;
                }

                int onCharacter = CountBehaviorsOn(source.gameObject);
                if (onCharacter > 0 && host != source.transform)
                {
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = "convai.actions.behavior-host.split",
                        Severity = ConvaiActionTroubleshooterSeverity.Info,
                        Title = ConvaiActionsEditorStrings.TroubleshooterBehaviorHostTitle,
                        Message = ConvaiActionsEditorStrings.BuildTroubleshooterBehaviorHostSplitMessage(
                            onCharacter, CountBehaviorsOn(host.gameObject)),
                        Locate = host.gameObject
                    });
                    return;
                }

                _findings.Add(Ok(
                    "convai.actions.behavior-host.ok",
                    ConvaiActionsEditorStrings.TroubleshooterBehaviorHostTitle,
                    ConvaiActionsEditorStrings.TroubleshooterBehaviorHostReadyMessage));
            }

            private static bool IsIdentityLocalTransform(Transform transform) =>
                transform.localPosition == Vector3.zero &&
                transform.localRotation == Quaternion.identity &&
                transform.localScale == Vector3.one;

            /// <summary>Action behaviors sitting directly on <paramref name="host" />, not on its children.</summary>
            private static int CountBehaviorsOn(GameObject host)
            {
                var components = host.GetComponents<MonoBehaviour>();
                int count = 0;
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is IConvaiActionExecutor)
                        count++;
                }

                return count;
            }

            #endregion

            #region Executor bindings

            private void EvaluateExecutorBindings(GameObject go, ConvaiActionConfigSource source)
            {
                IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions();
                for (int i = 0; i < definitions.Count; i++)
                {
                    ConvaiActionDefinition definition = definitions[i];
                    if (definition == null || string.IsNullOrWhiteSpace(definition.ActionName))
                        continue;

                    if (definition.Executor is IConvaiActionExecutor)
                        continue;

                    string hint = definition.ExecutorTypeHint;
                    if (string.IsNullOrWhiteSpace(hint))
                    {
                        // Which behavior should perform this action is an authoring decision — no
                        // mechanical fix; the Actions Editor's behavior picker is the repair path.
                        // Which behavior should perform this action is an authoring decision, so the
                        // destination is the Actions Editor opened at this action — not a scene
                        // object, and certainly not the character, which is not what is wrong.
                        ConvaiActionDefinition unboundDefinition = definition;
                        _bindingReportedActions.Add(definition.ActionName);
                        _findings.Add(new ConvaiActionTroubleshooterFinding
                        {
                            Id = $"convai.actions.behavior.unbound.{Slug(definition.ActionName)}",
                            Severity = ConvaiActionTroubleshooterSeverity.Error,
                            Title = $"Action Behavior — '{definition.ActionName}'",
                            Message = ConvaiActionsEditorStrings.TroubleshooterBehaviorUnboundMessage,
                            OpenLabel = ConvaiActionsEditorStrings.InspectorOpenWindowButton.text,
                            Open = () => ConvaiActionsEditorWindow.ShowWindowFor(source, unboundDefinition)
                        });
                        continue;
                    }

                    if (!ConvaiActionExecutorBinder.TryResolveType(hint, out Type executorType))
                    {
                        // The authored name matches no known behavior at all (typo, rename, or an
                        // uninstalled module) — distinct from "resolves fine but not present on this
                        // character" below. No mechanical fix: only the author knows what was meant.
                        ConvaiActionDefinition unresolvedDefinition = definition;
                        _bindingReportedActions.Add(definition.ActionName);
                        _findings.Add(new ConvaiActionTroubleshooterFinding
                        {
                            Id = $"convai.actions.behavior.unresolved.{Slug(definition.ActionName)}",
                            Severity = ConvaiActionTroubleshooterSeverity.Error,
                            Title = $"Action Behavior — '{definition.ActionName}'",
                            Message = string.Format(
                                ConvaiActionsEditorStrings.TroubleshooterBehaviorHintUnresolvedMessageFormat,
                                hint.Trim()),
                            OpenLabel = ConvaiActionsEditorStrings.InspectorOpenWindowButton.text,
                            Open = () => ConvaiActionsEditorWindow.ShowWindowFor(source, unresolvedDefinition)
                        });
                        continue;
                    }

                    if (go.GetComponentInChildren(executorType, true) != null)
                        continue; // Bindable at runtime; the validator reports nothing here either.

                    // Never show the raw component type name to a beginner: prefer the archetype
                    // catalog's curated display name, falling back to the brand-stripped resolver
                    // name for the rare behavior with no catalog entry.
                    string behaviorDisplayName =
                        ConvaiActionArchetypeCatalog.FindByExecutorType(executorType)?.DisplayName;
                    if (string.IsNullOrWhiteSpace(behaviorDisplayName))
                        behaviorDisplayName = ConvaiComponentTypeResolver.DisplayName(executorType);

                    Type executorTypeCaptured = executorType;
                    _bindingReportedActions.Add(definition.ActionName);
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.behavior.missing.{Slug(definition.ActionName)}",
                        Severity = ConvaiActionTroubleshooterSeverity.Warning,
                        Title = $"Action Behavior — '{definition.ActionName}'",
                        Message = string.Format(
                            ConvaiActionsEditorStrings.TroubleshooterBehaviorMissingMessageFormat, behaviorDisplayName),
                        Locate = go,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixAddBehavior.text,
                        Fix = () =>
                        {
                            // Searches the whole character so a behavior already moved onto the
                            // action behaviors object counts, and creates it through the hosting
                            // seam so this fix puts it where every other authoring path would.
                            if (go.GetComponentInChildren(executorTypeCaptured, true) == null)
                                ConvaiActionBehaviorHosting.AddBehavior(source, executorTypeCaptured);
                        }
                    });
                }
            }

            #endregion

            #region Scene targets and component requirements

            /// <summary>
            ///     Scene <see cref="ConvaiActionTarget" /> components the picked character neither has
            ///     an entry for nor learns about automatically. Info-level — the scene may
            ///     deliberately contain targets meant for other characters — with a one-click "Add To
            ///     Scene Knowledge" fix mirroring the Actions Editor's scan pane. Classification
            ///     reuses <see cref="ConvaiActionsSceneKnowledgeModel" /> so the two surfaces can
            ///     never disagree.
            /// </summary>
            private void EvaluateSceneTargets(ConvaiActionConfigSource source)
            {
                for (int i = 0; i < _sceneTargets.Length; i++)
                {
                    ConvaiActionTarget target = _sceneTargets[i];
                    if (target == null)
                        continue;

                    bool autoRegisters = target.RegisterOnEnable && target.AppliesToCharacter(_character);
                    ConvaiSceneKnowledgeScanStatus status = ConvaiActionsSceneKnowledgeModel.Classify(
                        target.TargetName, target.Kind, autoRegisters, source.Objects, source.Characters);
                    if (status != ConvaiSceneKnowledgeScanStatus.NotKnown)
                        continue;

                    ConvaiActionTarget captured = target;
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.scene-target.unknown.{Slug(target.TargetName)}",
                        Severity = ConvaiActionTroubleshooterSeverity.Info,
                        Title = $"Scene Target — '{target.TargetName}'",
                        Message = ConvaiActionsEditorStrings.TroubleshooterSceneTargetUnknownMessage,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixAddKnowledge.text,
                        Fix = () => AddKnowledgeEntryFromTarget(source, captured),
                        Locate = captured.gameObject
                    });
                }
            }

            /// <summary>Creates a Known entry from the target's name/description/bio/kind (mirrors the Scene Knowledge pane).</summary>
            private static void AddKnowledgeEntryFromTarget(
                ConvaiActionConfigSource source, ConvaiActionTarget target)
            {
                if (target == null)
                    return;

                if (target.Kind == ConvaiActionTargetKind.Character)
                {
                    Undo.RecordObject(source, "Add Known Character");
                    var characters = new List<ConvaiActionCharacterDefinition>(source.Characters)
                    {
                        new() { Name = target.TargetName, Bio = target.Bio, GameObjectReference = target.gameObject }
                    };
                    source.ReplaceCharacters(characters);
                }
                else
                {
                    Undo.RecordObject(source, "Add Known Object");
                    var objects = new List<ConvaiActionObjectDefinition>(source.Objects)
                    {
                        new()
                        {
                            Name = target.TargetName,
                            Description = target.Description,
                            GameObjectReference = target.gameObject
                        }
                    };
                    source.ReplaceObjects(objects);
                }

                EditorUtility.SetDirty(source);
            }

            /// <summary>
            ///     Archetype-driven CHARACTER-SIDE sweep: every authored action's
            ///     <see cref="ConvaiActionArchetypeAttribute.RequiredPeerHint" /> is resolved to a
            ///     real component type and checked against the character. Findings are de-duplicated
            ///     per required component — five actions needing the same missing peer produce
            ///     exactly one finding naming all five.
            /// </summary>
            private void EvaluatePeerRequirements(GameObject go, ConvaiActionConfigSource source)
            {
                Dictionary<Type, List<string>> actionsByPeerType = GroupActionsByRequiredComponent(
                    source, static entry => entry?.RequiredPeerHint);

                foreach (KeyValuePair<Type, List<string>> pair in actionsByPeerType)
                {
                    Type peerType = pair.Key;
                    if (go.GetComponentInChildren(peerType, true) != null)
                        continue;

                    string niceName = ConvaiComponentTypeResolver.DisplayName(peerType);
                    string actionList = string.Join(", ", pair.Value);
                    bool plural = pair.Value.Count > 1;

                    if (peerType == typeof(NavMeshAgent))
                    {
                        // Adding a NavMeshAgent without a baked NavMesh trades one broken state for a
                        // noisier one, and baking is a scene-authoring decision — explanation-only,
                        // regardless of which archetype's hint resolved to NavMeshAgent.
                        _findings.Add(new ConvaiActionTroubleshooterFinding
                        {
                            Id = $"convai.actions.requirement.peer.{Slug(peerType.Name)}",
                            Severity = ConvaiActionTroubleshooterSeverity.Warning,
                            Title = $"Action Requirement — {niceName}",
                            Message = $"{actionList} expect{(plural ? string.Empty : "s")} a {niceName} component, " +
                                      $"but none was found on this character. Add a {niceName} and bake a NavMesh " +
                                      "in the scene.",
                            Locate = go
                        });
                        continue;
                    }

                    Type peerTypeCaptured = peerType;
                    _findings.Add(new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.requirement.peer.{Slug(peerType.Name)}",
                        Severity = ConvaiActionTroubleshooterSeverity.Warning,
                        Title = $"Action Requirement — {niceName}",
                        Message = $"{actionList} need{(plural ? string.Empty : "s")} a {niceName} component on this " +
                                  "character to run.",
                        Locate = go,
                        FixLabel = ConvaiActionsEditorStrings.TroubleshooterFixAddComponent.text,
                        Fix = () => AddComponentFix(go, peerTypeCaptured)
                    });
                }
            }

            /// <summary>
            ///     Archetype-driven TARGET-SIDE sweep: every authored action's
            ///     <see cref="ConvaiActionArchetypeAttribute.RequiredTargetComponent" /> is checked
            ///     against this character's known targets — the GameObject references on its Known
            ///     Objects / Known Characters entries, plus any scene <see cref="ConvaiActionTarget" />
            ///     that registers itself for this character (mirrors <see cref="EvaluateSceneTargets" />'s
            ///     own notion of "known"). De-duplicated per required component, same as the peer sweep.
            /// </summary>
            private void EvaluateTargetComponentRequirements(GameObject go, ConvaiActionConfigSource source)
            {
                Dictionary<Type, List<string>> actionsByTargetType = GroupActionsByRequiredComponent(
                    source, static entry => entry?.RequiredTargetComponent);
                if (actionsByTargetType.Count == 0)
                    return;

                List<GameObject> knownTargets = CollectKnownTargets(source);

                foreach (KeyValuePair<Type, List<string>> pair in actionsByTargetType)
                {
                    Type requiredType = pair.Key;
                    if (AnyKnownTargetHas(knownTargets, requiredType))
                        continue;

                    string niceName = ConvaiComponentTypeResolver.DisplayName(requiredType);
                    string actionList = string.Join(", ", pair.Value);
                    bool plural = pair.Value.Count > 1;

                    var finding = new ConvaiActionTroubleshooterFinding
                    {
                        Id = $"convai.actions.requirement.target.{Slug(requiredType.Name)}",
                        Severity = ConvaiActionTroubleshooterSeverity.Warning,
                        Title = $"Target Requirement — {niceName}",
                        Message = $"{actionList} target{(plural ? string.Empty : "s")} objects with a {niceName} " +
                                  "component, but none of this character's known targets have one.",
                        Locate = knownTargets.Count == 1 ? knownTargets[0] : null
                    };

                    // Only unambiguous when the character knows about exactly one target — never
                    // suggest adding, say, a Controllable Light to a chair when there are several
                    // candidates and only the author knows which one this action actually means.
                    if (knownTargets.Count == 1)
                    {
                        GameObject onlyTarget = knownTargets[0];
                        Type requiredTypeCaptured = requiredType;
                        finding.FixLabel = $"Add {niceName} to '{onlyTarget.name}'";
                        finding.Fix = () => AddComponentFix(onlyTarget, requiredTypeCaptured);
                    }

                    _findings.Add(finding);
                }
            }

            /// <summary>
            ///     Shared grouping step for both requirement sweeps: walks every authored action
            ///     definition, resolves its catalog entry, and buckets action names by the component
            ///     type <paramref name="hintSelector" /> names — the single dedup mechanism both
            ///     sweeps rely on.
            /// </summary>
            private static Dictionary<Type, List<string>> GroupActionsByRequiredComponent(
                ConvaiActionConfigSource source,
                Func<ConvaiActionArchetypeCatalogEntry, string> hintSelector)
            {
                var actionsByType = new Dictionary<Type, List<string>>();
                IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions();
                for (int i = 0; i < definitions.Count; i++)
                {
                    ConvaiActionDefinition definition = definitions[i];
                    if (definition == null || string.IsNullOrWhiteSpace(definition.ActionName))
                        continue;

                    ConvaiActionArchetypeCatalogEntry entry = ConvaiActionArchetypeCatalog.FindByDefinition(definition);
                    string hint = hintSelector(entry);
                    if (string.IsNullOrWhiteSpace(hint))
                        continue;

                    Type componentType = ConvaiComponentTypeResolver.Resolve(hint.Trim());
                    if (componentType == null)
                        continue; // Free-text hint naming no loaded component type: nothing to validate.

                    if (!actionsByType.TryGetValue(componentType, out List<string> names))
                    {
                        names = new List<string>();
                        actionsByType[componentType] = names;
                    }

                    if (!names.Contains(definition.ActionName))
                        names.Add(definition.ActionName);
                }

                return actionsByType;
            }

            /// <summary>
            ///     This character's known-target GameObject set for the target-side sweep: the
            ///     GameObject references on <see cref="ConvaiActionConfigSource.Objects" /> /
            ///     <see cref="ConvaiActionConfigSource.Characters" />, plus any scene
            ///     <see cref="ConvaiActionTarget" /> that registers itself for this character.
            /// </summary>
            private List<GameObject> CollectKnownTargets(ConvaiActionConfigSource source)
            {
                var result = new List<GameObject>();

                IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
                if (objects != null)
                {
                    for (int i = 0; i < objects.Count; i++)
                    {
                        GameObject reference = objects[i]?.GameObjectReference;
                        if (reference != null && !result.Contains(reference))
                            result.Add(reference);
                    }
                }

                IReadOnlyList<ConvaiActionCharacterDefinition> characters = source.Characters;
                if (characters != null)
                {
                    for (int i = 0; i < characters.Count; i++)
                    {
                        GameObject reference = characters[i]?.GameObjectReference;
                        if (reference != null && !result.Contains(reference))
                            result.Add(reference);
                    }
                }

                for (int i = 0; i < _sceneTargets.Length; i++)
                {
                    ConvaiActionTarget sceneTarget = _sceneTargets[i];
                    if (sceneTarget == null ||
                        !sceneTarget.RegisterOnEnable ||
                        !sceneTarget.AppliesToCharacter(_character))
                        continue;

                    GameObject targetGo = sceneTarget.gameObject;
                    if (!result.Contains(targetGo))
                        result.Add(targetGo);
                }

                return result;
            }

            private static bool AnyKnownTargetHas(List<GameObject> knownTargets, Type componentType)
            {
                for (int i = 0; i < knownTargets.Count; i++)
                {
                    if (knownTargets[i] != null && knownTargets[i].GetComponent(componentType) != null)
                        return true;
                }

                return false;
            }

            #endregion

            private static void AddComponentFix<T>(GameObject go) where T : Component
            {
                if (go.GetComponent<T>() == null)
                    Undo.AddComponent<T>(go);
                EditorUtility.SetDirty(go);
            }

            private static void AddComponentFix(GameObject go, Type componentType)
            {
                if (go.GetComponent(componentType) == null)
                    Undo.AddComponent(go, componentType);
                EditorUtility.SetDirty(go);
            }

            private static ConvaiActionTroubleshooterFinding Ok(string id, string title, string message) =>
                new()
                {
                    Id = id,
                    Severity = ConvaiActionTroubleshooterSeverity.Ok,
                    Title = title,
                    Message = message
                };

            /// <summary>
            ///     The id-safe form of a user-authored name. Findings that repeat per action or per
            ///     target carry the subject in their id, and a subject is whatever the author typed —
            ///     so it is folded to lower-case, non-alphanumerics collapse to single dashes, and an
            ///     empty result becomes <c>unnamed</c> rather than an id ending in a dot.
            /// </summary>
            private static string Slug(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "unnamed";

                var builder = new System.Text.StringBuilder(value.Length);
                bool lastWasDash = false;
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    if (char.IsLetterOrDigit(character))
                    {
                        builder.Append(char.ToLowerInvariant(character));
                        lastWasDash = false;
                        continue;
                    }

                    if (lastWasDash || builder.Length == 0)
                        continue;

                    builder.Append('-');
                    lastWasDash = true;
                }

                string slug = builder.ToString().TrimEnd('-');
                return slug.Length == 0 ? "unnamed" : slug;
            }
        }
    }
}
