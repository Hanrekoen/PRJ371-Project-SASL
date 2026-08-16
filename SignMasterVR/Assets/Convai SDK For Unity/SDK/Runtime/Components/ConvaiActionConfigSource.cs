using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Explicit action affordance authoring surface for a <see cref="ConvaiCharacter" />.
    /// </summary>
    [AddComponentMenu("Convai/Convai Actions")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConvaiCharacter))]
    public sealed class ConvaiActionConfigSource : MonoBehaviour
    {
        [Header("Action Definitions")]
        [SerializeField]
        [Tooltip("Reusable action sets merged before the inline definitions below (earlier sets win on name collision; inline definitions always win over any set).")]
        private List<ConvaiActionSet> _actionSets = new();

        [SerializeField]
        [Tooltip("Typed action definitions that bind backend action names to executor components.")]
        private List<ConvaiActionDefinition> _definitions = new();

        [Header("Actionable Objects")]
        [SerializeField]
        [Tooltip("Explicit object targets the backend may use for action grounding.")]
        private List<ConvaiActionObjectDefinition> _objects = new();

        [Header("Actionable Characters")]
        [SerializeField]
        [Tooltip("Explicit character targets the backend may use for action grounding.")]
        private List<ConvaiActionCharacterDefinition> _characters = new();

        [Header("Initial Attention")]
        [SerializeField]
        [Tooltip("Optional initial object name to seed current_attention_object on connect.")]
        private string _initialAttentionObject;

        // No [Header]: this field is authored in the Actions Editor window's Character Settings
        // mode, which draws it inside its own styled section — a Header decorator would render a
        // second, unstyled title there.
        [SerializeField]
        [Tooltip("Which side of the project runs the action commands this character receives. " +
                 "Convai Action Runner is the shipped component that resolves targets and runs " +
                 "bound action behaviors. Custom Code means your own script subscribes to " +
                 "ConvaiCharacter.OnActionsReceived or ConvaiManager.Events.OnCharacterActionReceived " +
                 "instead. This setting changes nothing at runtime — it only tells the SDK's setup " +
                 "checks whether a missing dispatcher is a mistake or a deliberate choice.")]
        private ConvaiActionExecutionMode _actionExecutionMode = ConvaiActionExecutionMode.ConvaiActionDispatcher;

        // No [Header]: drawn by the Actions inspector and the Actions Editor's Character Settings
        // mode inside their own styled sections, exactly like _actionExecutionMode above.
        [SerializeField]
        [Tooltip("Optional child object that holds this character's action behaviors, so the " +
                 "character's own inspector stays readable. Leave empty to keep behaviors on the " +
                 "character itself — Convai finds them either way. This only decides where newly " +
                 "added behaviors are created; it never changes how actions run.")]
        private Transform _behaviorHost;

        public IReadOnlyList<ConvaiActionDefinition> Definitions => _definitions;

        /// <summary>
        ///     The object that newly authored action behaviors are added to: the child assigned as this
        ///     character's action behaviors object, or the character itself when none is assigned.
        ///     Never null, and never an object outside this character.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Authoring-only, in the same sense as <see cref="ActionExecutionMode" />: nothing in
        ///         the runtime path reads it. Action behaviors are found by searching this character's
        ///         whole hierarchy (see
        ///         <see cref="Convai.Runtime.Actions.ConvaiActionExecutorBinder" />), so behaviors on
        ///         the character and behaviors on a child both run identically, and a character can
        ///         sit half-way between the two layouts indefinitely without anything breaking.
        ///     </para>
        ///     <para>
        ///         Falls back to the character whenever the assigned object is not part of this
        ///         character — a behavior created on some unrelated object would never be found, so
        ///         this getter refuses to name one. <see cref="HasValidBehaviorHost" /> reports that
        ///         case for setup checks.
        ///     </para>
        /// </remarks>
        public GameObject BehaviorHost => IsUsableHost(_behaviorHost) ? _behaviorHost.gameObject : gameObject;

        /// <summary>
        ///     The child object assigned to hold this character's action behaviors, or null when
        ///     behaviors live on the character itself. Unlike <see cref="BehaviorHost" /> this returns
        ///     exactly what was authored, including an invalid assignment, so setup checks can report
        ///     the problem rather than silently paper over it.
        /// </summary>
        public Transform ConfiguredBehaviorHost => _behaviorHost;

        /// <summary>
        ///     False only when an action behaviors object has been assigned but is not this character
        ///     or one of its descendants. No assignment at all is a perfectly valid setup and reports
        ///     true.
        /// </summary>
        public bool HasValidBehaviorHost => _behaviorHost == null || IsUsableHost(_behaviorHost);

        /// <summary>
        ///     Tooling/test entry point that sets the object new action behaviors are added to. Pass
        ///     null to go back to adding them to the character. Callers own Undo recording and dirty
        ///     marking.
        /// </summary>
        internal void SetBehaviorHost(Transform behaviorHost) => _behaviorHost = behaviorHost;

        /// <summary>Whether <paramref name="host" /> is this character or something under it.</summary>
        private bool IsUsableHost(Transform host) => host != null && host.IsChildOf(transform);

        /// <summary>
        ///     Warns about an action behaviors object that is not part of this character. The value is
        ///     deliberately left as authored rather than cleared: silently discarding what someone
        ///     dragged in tells them nothing, and the setup checks and the Actions inspector both
        ///     surface it where it can actually be fixed.
        /// </summary>
        private void OnValidate()
        {
            if (HasValidBehaviorHost)
                return;

            ConvaiLogger.Warning(
                $"'{name}' names '{_behaviorHost.name}' as its action behaviors object, but that object " +
                "is not part of this Convai Character. New action behaviors will be added to the " +
                "character itself until this points at the character or one of its child objects.",
                LogCategory.Character);
        }

        /// <summary>
        ///     Which side of the project is responsible for running this character's action commands
        ///     (see <see cref="ConvaiActionExecutionMode" />). Declarative only: it never changes what
        ///     happens at runtime, and exists so
        ///     <see cref="ConvaiActionConfigValidator.Validate" /> knows whether a missing
        ///     <see cref="ConvaiActionDispatcher" /> on this character is a setup error or the
        ///     intended arrangement.
        /// </summary>
        public ConvaiActionExecutionMode ActionExecutionMode => _actionExecutionMode;

        /// <summary>
        ///     Reusable action-set assets merged ahead of <see cref="Definitions" /> by
        ///     <see cref="GetEffectiveDefinitions" />, in list order (earlier sets win on a name
        ///     collision with a later set; every set loses to an inline definition of the same name).
        /// </summary>
        public IReadOnlyList<ConvaiActionSet> ActionSets => _actionSets;
        public IReadOnlyList<ConvaiActionObjectDefinition> Objects => _objects;
        public IReadOnlyList<ConvaiActionCharacterDefinition> Characters => _characters;
        public string InitialAttentionObject => _initialAttentionObject;

        /// <summary>
        ///     Builds the wire-shaped action config sent to the backend at connect. Actions whose
        ///     authored <see cref="ConvaiActionDefinition.Enabled" /> flag is off are excluded, so
        ///     the Convai Character never learns about them.
        /// </summary>
        public ConvaiActionConfig BuildActionConfig() => BuildActionConfig(null);

        /// <summary>
        ///     Runtime variant of <see cref="BuildActionConfig()" /> that additionally applies
        ///     per-session availability overrides
        ///     (<see cref="ConvaiCharacterActions.SetActionAvailable" />) on top of the authored
        ///     <see cref="ConvaiActionDefinition.Enabled" /> flag — an override can both hide an
        ///     enabled action and restore a disabled one for the connect payload.
        /// </summary>
        internal ConvaiActionConfig BuildActionConfig(ConvaiCharacterActions runtimeAvailability)
        {
            IReadOnlyList<ConvaiActionDefinition> definitions =
                FilterAvailable(GetEffectiveDefinitions(requireExecutable: true), runtimeAvailability);
            if (definitions.Count == 0)
            {
                bool hasOrphanedTargets = (_objects?.Count ?? 0) > 0 ||
                                          (_characters?.Count ?? 0) > 0 ||
                                          !string.IsNullOrWhiteSpace(_initialAttentionObject);
                if (hasOrphanedTargets)
                {
                    ConvaiLogger.Warning(
                        $"'{name}' has action targets or initial attention but no valid action definitions. Omitting action_config.",
                        LogCategory.Character);
                }

                return null;
            }

            return BuildActionConfigCore(definitions);
        }

        /// <summary>
        ///     Builds the same merged config as <see cref="BuildActionConfig()" /> but never omits it
        ///     for having zero valid action definitions — used as the base for
        ///     <see cref="Convai.Runtime.Components.ConvaiCharacter" />'s internal runtime target
        ///     resolution (dispatcher target/parameter reference resolution), which must still find
        ///     authored/runtime-registered objects and characters even on a character whose actions
        ///     all live elsewhere (e.g. a pure "receiver"/"container" prop character). The
        ///     zero-definitions omission in <see cref="BuildActionConfig()" /> is specifically about
        ///     what gets sent to the backend as <c>action_config</c>, not about what this character's
        ///     own executors can resolve locally. Deliberately ignores action availability
        ///     (<see cref="ConvaiActionDefinition.Enabled" /> and runtime overrides): a disabled
        ///     action must stay locally resolvable so a stale backend command for it can be
        ///     identified and reported as unhandled instead of vanishing as unmatched.
        /// </summary>
        internal ConvaiActionConfig BuildRuntimeResolutionConfig() =>
            BuildActionConfigCore(GetEffectiveDefinitions(requireExecutable: true));

        /// <summary>
        ///     Drops definitions that are unavailable — session override
        ///     (<paramref name="runtimeAvailability" />, when supplied) over the authored
        ///     <see cref="ConvaiActionDefinition.Enabled" /> flag. Returns the input list unchanged
        ///     when nothing is filtered.
        /// </summary>
        private static IReadOnlyList<ConvaiActionDefinition> FilterAvailable(
            IReadOnlyList<ConvaiActionDefinition> definitions,
            ConvaiCharacterActions runtimeAvailability)
        {
            List<ConvaiActionDefinition> filtered = null;
            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                bool available = runtimeAvailability != null
                    ? runtimeAvailability.IsDefinitionAvailable(definition)
                    : definition?.Enabled == true;

                if (available)
                {
                    filtered?.Add(definition);
                    continue;
                }

                if (filtered != null)
                    continue;

                filtered = new List<ConvaiActionDefinition>(definitions.Count);
                for (int kept = 0; kept < i; kept++)
                    filtered.Add(definitions[kept]);
            }

            return filtered ?? definitions;
        }

        private ConvaiActionConfig BuildActionConfigCore(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var config = new ConvaiActionConfig();
            for (int i = 0; i < definitions.Count; i++)
                config.Actions.Add(definitions[i].ToActionConfigString());

            if (_objects != null)
            {
                foreach (ConvaiActionObjectDefinition actionObject in _objects)
                    config.Objects.Add(actionObject?.Clone() ?? new ConvaiActionObjectDefinition());
            }

            if (_characters != null)
            {
                foreach (ConvaiActionCharacterDefinition character in _characters)
                    config.Characters.Add(character?.Clone() ?? new ConvaiActionCharacterDefinition());
            }

            string initialAttentionObject = NormalizeName(_initialAttentionObject);
            if (!string.IsNullOrEmpty(initialAttentionObject))
            {
                if (TryFindObjectName(config.Objects, initialAttentionObject, out string resolvedObjectName))
                {
                    config.CurrentAttentionObject = resolvedObjectName;
                }
                else
                {
                    ConvaiLogger.Warning(
                        $"Initial attention object '{initialAttentionObject}' on '{name}' does not match any authored action object. Omitting current_attention_object.",
                        LogCategory.Character);
                }
            }

            return config;
        }

        /// <summary>
        ///     Editor-tooling entry point that replaces the authored definitions wholesale.
        ///     Callers own Undo recording and dirty marking.
        /// </summary>
        internal void ReplaceDefinitions(List<ConvaiActionDefinition> definitions) =>
            _definitions = definitions ?? new List<ConvaiActionDefinition>();

        /// <summary>Editor-tooling/test entry point that replaces the authored action sets wholesale.</summary>
        internal void ReplaceActionSets(List<ConvaiActionSet> actionSets) =>
            _actionSets = actionSets ?? new List<ConvaiActionSet>();

        /// <summary>
        ///     Tooling/test entry point that sets who is responsible for running this character's
        ///     actions (see <see cref="ActionExecutionMode" />). Callers own Undo recording and
        ///     dirty marking.
        /// </summary>
        internal void SetActionExecutionMode(ConvaiActionExecutionMode mode) => _actionExecutionMode = mode;

        /// <summary>Tooling/test entry point that replaces the authored objects wholesale.</summary>
        internal void ReplaceObjects(List<ConvaiActionObjectDefinition> objects) =>
            _objects = objects ?? new List<ConvaiActionObjectDefinition>();

        /// <summary>Tooling/test entry point that replaces the authored characters wholesale.</summary>
        internal void ReplaceCharacters(List<ConvaiActionCharacterDefinition> characters) =>
            _characters = characters ?? new List<ConvaiActionCharacterDefinition>();

        /// <summary>
        ///     Builds the effective action definition list seen by the dispatcher, validator,
        ///     preview, and MCP tooling: reusable <see cref="ActionSets" /> merged in list order,
        ///     with the inline <see cref="Definitions" /> concatenated last so an inline definition
        ///     always wins a name collision against any set, and an earlier set always wins against
        ///     a later one. Every result then gets one auto-bind pass
        ///     (<see cref="ConvaiActionExecutorBinder.TryBind" />) for definitions whose
        ///     <see cref="ConvaiActionDefinition.Executor" /> is null and
        ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> is set, so a set-authored
        ///     definition (which cannot hold a scene executor reference) resolves against this
        ///     character's hierarchy before <paramref name="requireExecutable" /> filtering runs.
        /// </summary>
        internal IReadOnlyList<ConvaiActionDefinition> GetEffectiveDefinitions(
            IReadOnlyList<string> allowedActionNames = null,
            bool requireExecutable = false)
        {
            List<ConvaiActionDefinition> merged = BuildMergedSourceDefinitions(out Dictionary<string, string> ownerByActionName);

            // Definitions that are neither already executable nor bindable (no hint) can never
            // become executable, so drop them before dedup exactly like the pre-merge behavior:
            // a hopeless duplicate must not "consume" the dedup slot from a usable duplicate of
            // the same name. Definitions carrying a hint survive this pre-pass even though they
            // are not executable yet — binding happens after dedup/clone below.
            if (requireExecutable)
                merged = FilterPotentiallyExecutable(merged);

            List<ConvaiActionDefinition> effective = ConvaiActionDefinition.FilterAndClone(
                merged,
                allowedActionNames,
                actionName => ConvaiLogger.Warning(
                    $"Duplicate action definition '{actionName}' on '{name}'. Keeping the first definition only.",
                    LogCategory.Character),
                requireExecutable: false);

            for (int i = 0; i < effective.Count; i++)
            {
                string ownerLabel = null;
                string actionName = ConvaiActionDefinition.NormalizeActionName(effective[i]?.ActionName);
                if (!string.IsNullOrEmpty(actionName))
                    ownerByActionName.TryGetValue(actionName, out ownerLabel);

                ConvaiActionExecutorBinder.TryBind(effective[i], gameObject, ownerLabel);
            }

            if (!requireExecutable)
                return effective;

            var executable = new List<ConvaiActionDefinition>(effective.Count);
            for (int i = 0; i < effective.Count; i++)
            {
                if (ConvaiActionConfigValidator.IsExecutableDefinition(effective[i]))
                    executable.Add(effective[i]);
            }

            return executable;
        }

        private static List<ConvaiActionDefinition> FilterPotentiallyExecutable(
            List<ConvaiActionDefinition> definitions)
        {
            var filtered = new List<ConvaiActionDefinition>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                bool potentiallyExecutable = ConvaiActionConfigValidator.IsExecutableDefinition(definition) ||
                                              !string.IsNullOrWhiteSpace(definition?.ExecutorTypeHint);
                if (potentiallyExecutable)
                    filtered.Add(definition);
            }

            return filtered;
        }

        /// <summary>
        ///     Concatenates the inline <see cref="Definitions" /> first so
        ///     <see cref="ConvaiActionDefinition.FilterAndClone" />'s first-occurrence-wins
        ///     deduplication makes an inline definition win over any set entry of the same name;
        ///     <see cref="ActionSets" /> entries follow in list order, so an earlier set also wins
        ///     over a later one.
        /// </summary>
        /// <param name="ownerByActionName">
        ///     Best-effort map of normalized action name to the name of the <see cref="ConvaiActionSet" />
        ///     asset that authored it (first-occurrence-wins, matching the dedup semantics above).
        ///     Inline <see cref="Definitions" /> are not attributed to any asset and are omitted. Used
        ///     only to make an unresolved <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> warning
        ///     actionable — see <see cref="ConvaiActionExecutorBinder.TryBind" />.
        /// </param>
        private List<ConvaiActionDefinition> BuildMergedSourceDefinitions(out Dictionary<string, string> ownerByActionName)
        {
            ownerByActionName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int capacity = _definitions?.Count ?? 0;
            if (_actionSets != null)
            {
                for (int i = 0; i < _actionSets.Count; i++)
                    capacity += _actionSets[i] != null ? _actionSets[i].Definitions.Count : 0;
            }

            var merged = new List<ConvaiActionDefinition>(capacity);
            if (_definitions != null)
                merged.AddRange(_definitions);

            if (_actionSets != null)
            {
                for (int i = 0; i < _actionSets.Count; i++)
                {
                    ConvaiActionSet actionSet = _actionSets[i];
                    if (actionSet == null)
                        continue;

                    IReadOnlyList<ConvaiActionDefinition> setDefinitions = actionSet.Definitions;
                    for (int d = 0; d < setDefinitions.Count; d++)
                    {
                        ConvaiActionDefinition definition = setDefinitions[d];
                        merged.Add(definition);

                        string actionName = ConvaiActionDefinition.NormalizeActionName(definition?.ActionName);
                        if (!string.IsNullOrEmpty(actionName) && !ownerByActionName.ContainsKey(actionName))
                            ownerByActionName[actionName] = actionSet.name;
                    }
                }
            }

            return merged;
        }

        private static bool TryFindObjectName(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            string objectName,
            out string resolvedObjectName)
        {
            resolvedObjectName = null;
            if (objects == null)
                return false;

            for (int i = 0; i < objects.Count; i++)
            {
                string candidateName = NormalizeName(objects[i]?.Name);
                if (string.IsNullOrEmpty(candidateName) ||
                    !string.Equals(candidateName, objectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolvedObjectName = candidateName;
                return true;
            }

            return false;
        }

        private static string NormalizeName(string value) => ConvaiActionText.Normalize(value);
    }
}
