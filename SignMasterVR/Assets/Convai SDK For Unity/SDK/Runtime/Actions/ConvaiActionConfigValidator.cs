using System;
using System.Collections.Generic;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Runtime.Actions
{
    /// <summary>How seriously a <see cref="ConvaiActionConfigDiagnostic" /> should be treated.</summary>
    public enum ConvaiActionConfigDiagnosticSeverity
    {
        /// <summary>Advisory only; the config still connects and executes.</summary>
        Info = 0,

        /// <summary>Likely authoring mistake (e.g. missing description); the config still works.</summary>
        Warning = 1,

        /// <summary>The config is broken (e.g. unbound executor) and the flagged part will not work.</summary>
        Error = 2
    }

    /// <summary>
    ///     One authoring finding from <see cref="ConvaiActionConfigValidator.Validate" />, with the
    ///     severity, an actionable message, and the config region it refers to.
    /// </summary>
    public sealed class ConvaiActionConfigDiagnostic
    {
        /// <summary>How seriously to treat this finding.</summary>
        public ConvaiActionConfigDiagnosticSeverity Severity { get; }

        /// <summary>Actionable description of the problem.</summary>
        public string Message { get; }

        /// <summary>Config region the finding refers to (e.g. "Actionable object #2"); may be empty.</summary>
        public string Context { get; }

        public ConvaiActionConfigDiagnostic(
            ConvaiActionConfigDiagnosticSeverity severity,
            string message,
            string context = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Context = context ?? string.Empty;
        }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Context) ? $"{Severity}: {Message}" : $"{Severity}: {Context}: {Message}";
    }

    /// <summary>
    ///     Authoring-time validation for a <see cref="ConvaiActionConfigSource" />: surfaces
    ///     broken executor bindings, empty names, duplicate targets, and unmatched initial
    ///     attention before they turn into silent runtime failures. Used by the inspector and
    ///     the Actions Editor; safe to call from user tooling.
    /// </summary>
    public static class ConvaiActionConfigValidator
    {
        /// <summary>
        ///     Validates the authored config and returns every finding (possibly empty, never null),
        ///     judging unlinked Known entries against the <see cref="ConvaiActionTarget" /> components
        ///     currently registered in the scene.
        /// </summary>
        /// <remarks>
        ///     Only enabled, registered targets are visible here, which is exactly right at run time
        ///     and useless at edit time — Unity never calls <c>OnEnable</c> on a plain MonoBehaviour
        ///     outside play mode, so the registry is empty in the editor. Editor tooling therefore
        ///     calls the <see cref="Validate(ConvaiActionConfigSource, IReadOnlyList{ConvaiActionTarget})" />
        ///     overload with the targets it swept out of the open scene itself.
        /// </remarks>
        public static IReadOnlyList<ConvaiActionConfigDiagnostic> Validate(ConvaiActionConfigSource source) =>
            Validate(source, ConvaiActionTarget.ActiveTargets);

        /// <summary>
        ///     Validates the authored config against a caller-supplied set of scene
        ///     <see cref="ConvaiActionTarget" /> components — the seam that lets editor tooling
        ///     validate a scene whose targets have not registered themselves yet.
        /// </summary>
        /// <param name="source">The config being validated.</param>
        /// <param name="sceneTargets">
        ///     Scene targets to consider. Entries that do not apply to <paramref name="source" />'s
        ///     character are ignored, so passing every target in the scene is safe.
        /// </param>
        /// <remarks>
        ///     A Known entry with no scene object of its own is not broken when a same-named target
        ///     stands in the scene: the runtime merge completes the entry from that component rather
        ///     than dropping it (<c>ConvaiCharacter.CompleteAuthoredObject</c>). Reporting an error
        ///     for that setup told the author to fix something that already works, which is why this
        ///     check needs to see the scene at all.
        /// </remarks>
        public static IReadOnlyList<ConvaiActionConfigDiagnostic> Validate(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            var diagnostics = new List<ConvaiActionConfigDiagnostic>();
            if (source == null)
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Error,
                    "This Convai Character has no Convai Actions component, so it " +
                    "declares no actions. Add one to give it actions.",
                    "Actions"));
                return diagnostics;
            }

            ValidateActionExecution(source, diagnostics);
            ValidateDefinitions(source, sceneTargets, diagnostics);
            ValidateActionSets(source, sceneTargets, diagnostics);
            ValidateCatalogGrammar(source, diagnostics);
            ValidateActionSetCollisions(source, diagnostics);
            ValidateActionAvailability(source, diagnostics);
            ValidateObjects(source, source.Objects, sceneTargets, diagnostics);
            ValidateCharacters(source, source.Characters, sceneTargets, diagnostics);
            ValidateDuplicateTargetNames(source.Objects, source.Characters, diagnostics);
            ValidateInitialAttention(source.InitialAttentionObject, source.Objects, diagnostics);
            return diagnostics;
        }

        /// <summary>
        ///     Whether a scene target of this kind answers to <paramref name="name" /> and applies to
        ///     <paramref name="source" />'s character — the edit-time mirror of the runtime completion
        ///     step, matched the same way (trimmed, case-insensitive) and gated on the same two
        ///     conditions the merge gates on (registers itself, and is in scope for this character).
        /// </summary>
        private static bool IsCompletedBySceneTarget(
            ConvaiActionConfigSource source,
            string name,
            ConvaiActionTargetKind kind,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            if (sceneTargets == null || sceneTargets.Count == 0 || string.IsNullOrWhiteSpace(name))
                return false;

            ConvaiCharacter owner = source.GetComponent<ConvaiCharacter>();
            if (owner == null)
                return false;

            string trimmed = name.Trim();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                ConvaiActionTarget target = sceneTargets[i];
                if (target == null || target.Kind != kind || !target.RegisterOnEnable)
                    continue;

                if (!target.AppliesToCharacter(owner))
                    continue;

                string targetName = target.TargetName;
                if (!string.IsNullOrWhiteSpace(targetName) &&
                    string.Equals(targetName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static bool HasErrors(IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (diagnostics == null)
                return false;

            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i]?.Severity == ConvaiActionConfigDiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }

        internal static bool IsExecutableDefinition(ConvaiActionDefinition definition) =>
            definition?.Executor is IConvaiActionExecutor;

        /// <summary>
        ///     Checks that something is set up to actually run the commands this character receives.
        ///     Declaring actions and running them are separate steps, and a character that declares
        ///     them without either step-two is the one setup mistake that produces no symptom at all:
        ///     the Convai Character offers the action, the backend sends the command, and nothing
        ///     happens, silently.
        /// </summary>
        /// <remarks>
        ///     Only the <see cref="ConvaiActionExecutionMode.ConvaiActionDispatcher" /> mode can be
        ///     checked here. Custom Code runs from a subscription that does not exist until Play
        ///     mode, so there is nothing truthful to assert about it at authoring time — the runtime
        ///     "nobody was listening" report on <c>ConvaiCharacter</c> covers that mode instead, and
        ///     covers the dispatcher mode too.
        /// </remarks>
        private static void ValidateActionExecution(
            ConvaiActionConfigSource source,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (source.ActionExecutionMode != ConvaiActionExecutionMode.ConvaiActionDispatcher)
                return;

            // Children too, matching how the dispatcher is found everywhere else in the tooling.
            if (source.GetComponentInChildren<ConvaiActionDispatcher>(true) != null)
                return;

            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                ConvaiActionConfigDiagnosticSeverity.Error,
                $"'{source.name}' has no Convai Action Runner, so the actions it offers will be " +
                "received and then ignored. Add the Convai Action Runner component to this " +
                "Convai Character, or — if your own script handles the commands instead — set " +
                "Actions Are Run By to Custom Code.",
                "Running actions"));
        }

        private static void ValidateDefinitions(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionTarget> sceneTargets,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            IReadOnlyList<ConvaiActionDefinition> definitions = source.Definitions;
            IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
            IReadOnlyList<ConvaiActionCharacterDefinition> characters = source.Characters;
            if (definitions == null || definitions.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string actionName = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                string context = $"Action definition #{i + 1}";
                if (string.IsNullOrEmpty(actionName))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        "Action definition has a blank action name.",
                        context));
                    continue;
                }

                if (!seen.Add(actionName))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Duplicate action definition '{actionName}'.",
                        context));
                }

                if (!IsExecutableDefinition(definition))
                    ValidateExecutorBinding(source, definition, actionName, context, diagnostics);

                ValidateWireGrammar(definition, context, diagnostics);
                ValidateTargetAvailability(
                    source, definition, actionName, context, objects, characters, sceneTargets, diagnostics);
                ValidateTargetRequirementAgainstBehavior(definition, actionName, context, diagnostics);
                ValidateParameters(definition, actionName, diagnostics);
            }
        }

        /// <summary>
        ///     Reports an action that demands a target its own behavior has said it does not need.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two are read at different moments and only one of them can refuse. The
        ///         admission stage reads the <em>definition</em>: a command that names nothing for an
        ///         action whose <see cref="ConvaiActionDefinition.TargetRequirement" /> is not
        ///         <see cref="ConvaiActionTargetRequirement.None" /> is dropped there, before any
        ///         behavior is asked. So a behavior that overrides <c>RequiresTarget</c> to
        ///         <c>false</c> — because it can find what to act on itself, or acts on the player,
        ///         or acts on nothing — never gets the chance.
        ///     </para>
        ///     <para>
        ///         <b>Nothing about that reads as a mismatch.</b> The drop report says the Convai
        ///         Character "named nothing", which sends whoever is diagnosing to the action's
        ///         wording and the model's behavior — the two places the fault is not. Measured
        ///         twice in one session on this SDK's own demo, on two different actions, and both
        ///         times the behavior was written specifically to work without a target and said so
        ///         in a comment.
        ///     </para>
        ///     <para>
        ///         A warning rather than an error: the pairing is legal and occasionally deliberate
        ///         — an author may want the stricter rule and be happy for those commands to be
        ///         turned away. It is reported so that choice is made rather than discovered.
        ///     </para>
        /// </remarks>
        private static void ValidateTargetRequirementAgainstBehavior(
            ConvaiActionDefinition definition,
            string actionName,
            string context,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (definition == null ||
                definition.TargetRequirement == ConvaiActionTargetRequirement.None ||
                definition.Executor is not ConvaiTargetedActionExecutor behavior ||
                behavior.NeedsTargetToRun)
                return;

            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                ConvaiActionConfigDiagnosticSeverity.Warning,
                $"Action '{actionName}' asks for a target, but its action behavior " +
                $"({behavior.GetType().Name}) works without one. A command that names nothing is " +
                "turned away before the behavior is asked, and the reason given will say the Convai " +
                "Character named nothing — which points at the wording rather than at this. Set the " +
                "action's target to None if the behavior should decide, or leave it if commands " +
                "without a target really should be refused.",
                context));
        }

        /// <summary>
        ///     Reports authored text the wire format cannot carry unambiguously.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The fault this catches produces no symptom at all until it produces a baffling
        ///         one. An action named <c>Sit - Chair</c> renders as <c>Sit - Chair - …</c>, and
        ///         every consumer that recovers a name from a rendered string reads it as
        ///         <c>Sit</c> — so the availability filter and the mid-session config sync address
        ///         an action nobody defined, while the character is offered one it can never be
        ///         asked to perform. Nothing logs, because from each component's point of view
        ///         nothing went wrong.
        ///     </para>
        ///     <para>
        ///         Reported as an error, and at authoring time, because the alternative is a support
        ///         ticket about a character that "ignores one action". The rule is deliberately
        ///         narrow — see <see cref="ConvaiActionWireGrammar" />: a plain dash is legal
        ///         everywhere, so choices like <c>path-blocked</c> are unaffected.
        ///     </para>
        /// </remarks>
        private static void ValidateWireGrammar(
            ConvaiActionDefinition definition,
            string context,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            IReadOnlyList<ConvaiActionGrammarViolation> violations =
                ConvaiActionWireGrammar.Validate(definition);
            for (int i = 0; i < violations.Count; i++)
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Error,
                    violations[i].Explanation,
                    context));
            }
        }

        /// <summary>
        ///     Diagnoses why an unbound (<c>Executor == null</c>) definition cannot execute: an
        ///     empty <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> keeps the existing
        ///     missing-executor error; a hint that matches no loaded
        ///     <see cref="IConvaiActionExecutor" /> type is a new, more specific error; a hint that
        ///     resolves to a type with no matching component in <paramref name="source" />'s
        ///     hierarchy is only a warning, since it is still bindable at runtime once the
        ///     component is added.
        /// </summary>
        private static void ValidateExecutorBinding(
            ConvaiActionConfigSource source,
            ConvaiActionDefinition definition,
            string actionName,
            string context,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            string hint = ConvaiActionParameterDefinition.Normalize(definition?.ExecutorTypeHint);
            if (string.IsNullOrEmpty(hint))
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Error,
                    $"Action '{actionName}' has no action behavior bound to it, so nothing will run " +
                    "when the Convai Character performs it.",
                    context));
                return;
            }

            if (!ConvaiActionExecutorBinder.TryResolveType(hint, out Type executorType))
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Error,
                    $"Action '{actionName}' names the action behavior '{hint}', but no such behavior " +
                    "exists in this project. It was probably renamed or deleted — pick the action's " +
                    "behavior again.",
                    context));
                return;
            }

            if (source != null && source.GetComponentInChildren(executorType, true) == null)
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Warning,
                    $"Action '{actionName}' resolves executor type hint '{hint}' to {executorType.Name}, but no such component was found on '{source.name}' or its children. It is bindable at runtime once the component is added.",
                    context));
            }
        }

        /// <summary>
        ///     Validates each <see cref="ConvaiActionConfigSource.ActionSets" /> entry's definitions:
        ///     blank names and the same executor-hint diagnostics as inline definitions
        ///     (<see cref="ValidateExecutorBinding" />). Set entries always have a null
        ///     <see cref="ConvaiActionDefinition.Executor" /> by design (assets cannot hold scene
        ///     references), so they always go through the hint path.
        /// </summary>
        private static void ValidateActionSets(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionTarget> sceneTargets,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            IReadOnlyList<ConvaiActionSet> actionSets = source.ActionSets;
            if (actionSets == null || actionSets.Count == 0)
                return;

            IReadOnlyList<ConvaiActionObjectDefinition> objects = source.Objects;
            IReadOnlyList<ConvaiActionCharacterDefinition> characters = source.Characters;

            for (int s = 0; s < actionSets.Count; s++)
            {
                ConvaiActionSet actionSet = actionSets[s];
                if (actionSet == null)
                    continue;

                string setName = GetActionSetDisplayName(actionSet, s);
                IReadOnlyList<ConvaiActionDefinition> setDefinitions = actionSet.Definitions;
                for (int i = 0; i < setDefinitions.Count; i++)
                {
                    ConvaiActionDefinition definition = setDefinitions[i];
                    string actionName = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                    string context = $"Action set '{setName}' definition #{i + 1}";
                    if (string.IsNullOrEmpty(actionName))
                    {
                        diagnostics.Add(new ConvaiActionConfigDiagnostic(
                            ConvaiActionConfigDiagnosticSeverity.Error,
                            "Action definition has a blank action name.",
                            context));
                        continue;
                    }

                    if (!IsExecutableDefinition(definition))
                        ValidateExecutorBinding(source, definition, actionName, context, diagnostics);

                    ValidateWireGrammar(definition, context, diagnostics);
                    ValidateTargetAvailability(
                        source,
                        definition,
                        actionName,
                        context,
                        objects,
                        characters,
                        sceneTargets,
                        diagnostics);
                }
            }
        }

        /// <summary>Display name for an action set asset in diagnostics; falls back to a positional label for unnamed/in-memory instances.</summary>
        private static string GetActionSetDisplayName(ConvaiActionSet actionSet, int index) =>
            string.IsNullOrEmpty(actionSet.name) ? $"Action Set #{index + 1}" : actionSet.name;

        /// <summary>
        ///     Reports name collisions across <see cref="ConvaiActionConfigSource.ActionSets" /> and
        ///     the inline <see cref="ConvaiActionConfigSource.Definitions" /> as warnings (not
        ///     errors) — precedence resolves them deterministically at runtime (inline wins over
        ///     every set; an earlier set wins over a later one), so this is advisory, not broken.
        /// </summary>
        private static void ValidateActionSetCollisions(
            ConvaiActionConfigSource source,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            IReadOnlyList<ConvaiActionSet> actionSets = source.ActionSets;
            if (actionSets == null || actionSets.Count == 0)
                return;

            var inlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ConvaiActionDefinition> inlineDefinitions = source.Definitions;
            if (inlineDefinitions != null)
            {
                for (int i = 0; i < inlineDefinitions.Count; i++)
                {
                    string name = ConvaiActionDefinition.NormalizeActionName(inlineDefinitions[i]?.ActionName);
                    if (!string.IsNullOrEmpty(name))
                        inlineNames.Add(name);
                }
            }

            // Keyed by set index (not display name) so two distinct, identically- or blank-named
            // set assets are still correctly recognized as different owners.
            var firstSetOwnerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var alreadyWarnedInline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alreadyWarnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int s = 0; s < actionSets.Count; s++)
            {
                ConvaiActionSet actionSet = actionSets[s];
                if (actionSet == null)
                    continue;

                string setName = GetActionSetDisplayName(actionSet, s);
                IReadOnlyList<ConvaiActionDefinition> setDefinitions = actionSet.Definitions;
                for (int d = 0; d < setDefinitions.Count; d++)
                {
                    string actionName = ConvaiActionDefinition.NormalizeActionName(setDefinitions[d]?.ActionName);
                    if (string.IsNullOrEmpty(actionName))
                        continue;

                    if (inlineNames.Contains(actionName))
                    {
                        if (alreadyWarnedInline.Add(actionName))
                        {
                            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                                ConvaiActionConfigDiagnosticSeverity.Warning,
                                $"Action '{actionName}' is defined both inline and in action set '{setName}'. The inline definition wins.",
                                $"Action set '{setName}'"));
                        }

                        continue;
                    }

                    if (firstSetOwnerIndex.TryGetValue(actionName, out int owningSetIndex))
                    {
                        if (owningSetIndex != s && alreadyWarnedSet.Add(actionName))
                        {
                            string owningSetName = GetActionSetDisplayName(actionSets[owningSetIndex], owningSetIndex);
                            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                                ConvaiActionConfigDiagnosticSeverity.Warning,
                                $"Action '{actionName}' is defined in both action set '{owningSetName}' and action set '{setName}'. '{owningSetName}' wins (earlier in the list).",
                                $"Action set '{setName}'"));
                        }

                        continue;
                    }

                    firstSetOwnerIndex[actionName] = s;
                }
            }
        }

        /// <summary>
        ///     Reports the wire-format faults that only exist between two definitions.
        /// </summary>
        /// <remarks>
        ///     Walks the inline and set lists with the same first-occurrence-wins dedup the
        ///     effective merge uses, deliberately without calling <c>GetEffectiveDefinitions</c> —
        ///     validation runs on every inspector and window repaint and must never trigger that
        ///     method's duplicate-collision logging.
        /// </remarks>
        private static void ValidateCatalogGrammar(
            ConvaiActionConfigSource source,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ConvaiActionDefinition> catalog = null;

            void Consider(ConvaiActionDefinition definition)
            {
                string actionName = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName) || !seen.Add(actionName))
                    return;

                catalog ??= new List<ConvaiActionDefinition>();
                catalog.Add(definition);
            }

            IReadOnlyList<ConvaiActionDefinition> inlineDefinitions = source.Definitions;
            for (int i = 0; inlineDefinitions != null && i < inlineDefinitions.Count; i++)
                Consider(inlineDefinitions[i]);

            IReadOnlyList<ConvaiActionSet> actionSets = source.ActionSets;
            for (int s = 0; actionSets != null && s < actionSets.Count; s++)
            {
                IReadOnlyList<ConvaiActionDefinition> setDefinitions = actionSets[s]?.Definitions;
                for (int d = 0; setDefinitions != null && d < setDefinitions.Count; d++)
                    Consider(setDefinitions[d]);
            }

            IReadOnlyList<ConvaiActionGrammarViolation> violations =
                ConvaiActionWireGrammar.ValidateCatalog(catalog);
            for (int i = 0; i < violations.Count; i++)
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Error,
                    violations[i].Explanation,
                    "Actions"));
            }
        }

        /// <summary>
        ///     Warns when every authored action is disabled: the config still connects, but the
        ///     Convai Character will not know about or offer a single action — almost always an
        ///     authoring oversight rather than intent. Walks the raw inline + set lists with the
        ///     same first-occurrence-wins dedup as the effective merge, deliberately without
        ///     calling <c>GetEffectiveDefinitions</c> — validation runs on every inspector/window
        ///     repaint and must never trigger that method's duplicate-collision logging.
        /// </summary>
        private static void ValidateActionAvailability(
            ConvaiActionConfigSource source,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalCount = 0;
            int disabledCount = 0;

            void Consider(ConvaiActionDefinition definition)
            {
                string actionName = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName) || !seen.Add(actionName))
                    return;

                totalCount++;
                if (!definition.Enabled)
                    disabledCount++;
            }

            IReadOnlyList<ConvaiActionDefinition> inlineDefinitions = source.Definitions;
            for (int i = 0; inlineDefinitions != null && i < inlineDefinitions.Count; i++)
                Consider(inlineDefinitions[i]);

            IReadOnlyList<ConvaiActionSet> actionSets = source.ActionSets;
            for (int s = 0; actionSets != null && s < actionSets.Count; s++)
            {
                IReadOnlyList<ConvaiActionDefinition> setDefinitions = actionSets[s]?.Definitions;
                for (int d = 0; setDefinitions != null && d < setDefinitions.Count; d++)
                    Consider(setDefinitions[d]);
            }

            if (totalCount == 0 || disabledCount < totalCount)
                return;

            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                ConvaiActionConfigDiagnosticSeverity.Warning,
                $"All {totalCount} action(s) are disabled, so the Convai Character will not know about or offer any action. Tick at least one action's checkbox in the Actions Editor.",
                "Action availability"));
        }

        /// <summary>Counts definitions whose authored <see cref="ConvaiActionDefinition.Enabled" /> flag is off.</summary>
        internal static int CountDisabled(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            int disabled = 0;
            if (definitions == null)
                return disabled;

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] is { Enabled: false })
                    disabled++;
            }

            return disabled;
        }

        private static void ValidateParameters(
            ConvaiActionDefinition definition,
            string actionName,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (definition.Parameters == null || definition.Parameters.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(definition.Description))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Action '{actionName}' has no description or parameters, so backend intent may be vague."));
                }

                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definition.Parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = definition.Parameters[i];
                string paramName = ConvaiActionParameterDefinition.Normalize(parameter?.Name);
                string context = $"Action '{actionName}' parameter #{i + 1}";
                if (string.IsNullOrEmpty(paramName))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        "Parameter has a blank name.",
                        context));
                    continue;
                }

                if (!seen.Add(paramName))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Duplicate parameter '{paramName}'.",
                        context));
                }

                if (i == 0 && !string.IsNullOrWhiteSpace(parameter.Connector))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Parameter '{paramName}' cannot have a connector because it is the first parameter.",
                        context));
                }

                if (parameter.Type == ConvaiActionParameterType.Choice && !HasChoices(parameter.Choices))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Choice parameter '{paramName}' must define at least one choice.",
                        context));
                }

                if (string.IsNullOrWhiteSpace(parameter.Description))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Parameter '{paramName}' is missing description.",
                        context));
                }
            }
        }

        private static bool HasChoices(IReadOnlyList<string> choices)
        {
            if (choices == null)
                return false;

            for (int i = 0; i < choices.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(choices[i]))
                    return true;
            }

            return false;
        }

        private static void ValidateTargetAvailability(
            ConvaiActionConfigSource source,
            ConvaiActionDefinition definition,
            string actionName,
            string context,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            IReadOnlyList<ConvaiActionTarget> sceneTargets,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            bool hasObjects = HasNamedTargets(objects) ||
                              HasApplicableSceneTarget(source, ConvaiActionTargetKind.Object, sceneTargets);
            bool hasCharacters = HasNamedTargets(characters) ||
                                 HasApplicableSceneTarget(source, ConvaiActionTargetKind.Character, sceneTargets);
            switch (definition.TargetRequirement)
            {
                case ConvaiActionTargetRequirement.Object when !hasObjects:
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Action '{actionName}' needs an object target, but this character does not " +
                        "know any objects yet. Add one in Scene Knowledge before using this action; " +
                        "other actions are unaffected.",
                        context));
                    break;
                case ConvaiActionTargetRequirement.Character when !hasCharacters:
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Action '{actionName}' needs another character as its target, but this character " +
                        "does not know any other characters yet. Add one in Scene Knowledge before using " +
                        "this action; other actions are unaffected.",
                        context));
                    break;
                case ConvaiActionTargetRequirement.Either when !hasObjects && !hasCharacters:
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Action '{actionName}' needs an object or another character as its target, but " +
                        "this character does not know any yet. Add one in Scene Knowledge before using " +
                        "this action; other actions are unaffected.",
                        context));
                    break;
            }
        }

        /// <summary>
        ///     Whether the character knows at least one target accepted by an action. This is the
        ///     shared decision used by validation and editor guidance, so an auto-registering scene
        ///     target never produces a false "add a target" warning.
        /// </summary>
        internal static bool HasTargetForRequirement(
            ConvaiActionConfigSource source,
            ConvaiActionTargetRequirement requirement,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            if (requirement == ConvaiActionTargetRequirement.None)
                return true;

            bool hasObjects = HasNamedTargets(source?.Objects) ||
                              HasApplicableSceneTarget(source, ConvaiActionTargetKind.Object, sceneTargets);
            bool hasCharacters = HasNamedTargets(source?.Characters) ||
                                 HasApplicableSceneTarget(source, ConvaiActionTargetKind.Character, sceneTargets);
            return requirement switch
            {
                ConvaiActionTargetRequirement.Object => hasObjects,
                ConvaiActionTargetRequirement.Character => hasCharacters,
                ConvaiActionTargetRequirement.Either => hasObjects || hasCharacters,
                _ => true
            };
        }

        private static bool HasApplicableSceneTarget(
            ConvaiActionConfigSource source,
            ConvaiActionTargetKind kind,
            IReadOnlyList<ConvaiActionTarget> sceneTargets)
        {
            if (source == null || sceneTargets == null || sceneTargets.Count == 0)
                return false;

            ConvaiCharacter owner = source.GetComponent<ConvaiCharacter>();
            if (owner == null)
                return false;

            for (int i = 0; i < sceneTargets.Count; i++)
            {
                ConvaiActionTarget target = sceneTargets[i];
                if (target == null || target.Kind != kind || !target.RegisterOnEnable ||
                    string.IsNullOrWhiteSpace(target.TargetName) || !target.AppliesToCharacter(owner))
                    continue;

                return true;
            }

            return false;
        }

        private static void ValidateObjects(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionTarget> sceneTargets,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (objects == null)
                return;

            for (int i = 0; i < objects.Count; i++)
            {
                ConvaiActionObjectDefinition actionObject = objects[i];
                string name = NormalizeName(actionObject?.Name);
                string context = $"Actionable object #{i + 1}";
                if (string.IsNullOrEmpty(name))
                    continue;

                if (string.IsNullOrWhiteSpace(actionObject.Description))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Actionable object '{name}' is missing object description.",
                        context));
                }

                // An entry declared text-only has no object on purpose: the character can talk
                // about it and nothing can be performed on it. Only an entry that still claims to be
                // actionable is broken by a missing link — and only when no scene target completes it.
                if (actionObject.GameObjectReference == null &&
                    !actionObject.TextOnly &&
                    !IsCompletedBySceneTarget(source, name, ConvaiActionTargetKind.Object, sceneTargets))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Actionable object '{name}' has no scene object linked to it, so this " +
                        "character has nothing to act on for it. Link a scene object, or mark the " +
                        "entry as text only.",
                        context));
                }
            }
        }

        private static void ValidateCharacters(
            ConvaiActionConfigSource source,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            IReadOnlyList<ConvaiActionTarget> sceneTargets,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            if (characters == null)
                return;

            for (int i = 0; i < characters.Count; i++)
            {
                ConvaiActionCharacterDefinition character = characters[i];
                string name = NormalizeName(character?.Name);
                string context = $"Actionable character #{i + 1}";
                if (string.IsNullOrEmpty(name))
                    continue;

                if (string.IsNullOrWhiteSpace(character.Bio))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Warning,
                        $"Actionable character '{name}' is missing character bio.",
                        context));
                }

                if (character.GameObjectReference == null &&
                    !character.TextOnly &&
                    !IsCompletedBySceneTarget(source, name, ConvaiActionTargetKind.Character, sceneTargets))
                {
                    diagnostics.Add(new ConvaiActionConfigDiagnostic(
                        ConvaiActionConfigDiagnosticSeverity.Error,
                        $"Actionable character '{name}' has no scene object linked to it, so this " +
                        "character has nothing to act on for it. Link a scene object, or mark the " +
                        "entry as text only.",
                        context));
                }
            }
        }

        private static void ValidateDuplicateTargetNames(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    string name = NormalizeName(objects[i]?.Name);
                    AddTargetName(name, diagnostics, names);
                }
            }

            if (characters == null)
                return;

            for (int i = 0; i < characters.Count; i++)
            {
                string name = NormalizeName(characters[i]?.Name);
                AddTargetName(name, diagnostics, names);
            }
        }

        private static void AddTargetName(
            string name,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics,
            ISet<string> names)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (names.Add(name))
                return;

            diagnostics.Add(new ConvaiActionConfigDiagnostic(
                ConvaiActionConfigDiagnosticSeverity.Error,
                $"Duplicate target name '{name}' across actionable target lists."));
        }

        private static void ValidateInitialAttention(
            string initialAttentionObject,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            ICollection<ConvaiActionConfigDiagnostic> diagnostics)
        {
            string attentionName = NormalizeName(initialAttentionObject);
            if (string.IsNullOrEmpty(attentionName))
                return;

            if (!ContainsName(objects, attentionName))
            {
                diagnostics.Add(new ConvaiActionConfigDiagnostic(
                    ConvaiActionConfigDiagnosticSeverity.Warning,
                    $"Initial attention object '{attentionName}' does not match any authored action object."));
            }
        }

        private static bool HasNamedTargets<T>(IReadOnlyList<T> targets)
        {
            if (targets == null)
                return false;

            for (int i = 0; i < targets.Count; i++)
            {
                string name = targets[i] switch
                {
                    ConvaiActionObjectDefinition actionObject => actionObject?.Name,
                    ConvaiActionCharacterDefinition character => character?.Name,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(name))
                    return true;
            }

            return false;
        }

        private static bool ContainsName(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            string name)
        {
            if (objects == null)
                return false;

            for (int i = 0; i < objects.Count; i++)
            {
                if (string.Equals(NormalizeName(objects[i]?.Name), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string NormalizeName(string value) => ConvaiActionText.Normalize(value);
    }
}
