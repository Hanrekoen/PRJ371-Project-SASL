using System;
using System.Collections.Generic;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Target constraint an action definition imposes on incoming commands.
    /// </summary>
    public enum ConvaiActionTargetRequirement
    {
        /// <summary>The action does not use a target.</summary>
        None = 0,

        /// <summary>The action requires a resolved object target.</summary>
        Object = 1,

        /// <summary>The action requires a resolved character target.</summary>
        Character = 2,

        /// <summary>The action accepts either an object or a character target.</summary>
        Either = 3
    }

    /// <summary>
    ///     Per-action override of the dispatcher-wide batch failure policy.
    /// </summary>
    public enum ConvaiActionFailurePolicyOverride
    {
        /// <summary>Follow the <see cref="ConvaiActionDispatcher" /> failure policy.</summary>
        UseDispatcherDefault = 0,

        /// <summary>A non-success result aborts the remaining batch.</summary>
        StopBatch = 1,

        /// <summary>A non-success result lets the remaining batch continue.</summary>
        ContinueBatch = 2
    }

    /// <summary>
    ///     What a Convai Character does with the answer an action found — the
    ///     <see cref="ConvaiActionExecutionResult.Answer" /> it returned.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Authored per action, because whether an action answers a question lives in its nature
    ///         rather than in its result: <c>Read Status</c> always produces an answer and
    ///         <c>Walk To</c> never does. Nothing here is inferred — two results that look identical
    ///         to the SDK (<c>"arrived at the gallery"</c> and <c>"battery A reads 80%"</c>) want
    ///         opposite treatment, and a guess would be silently wrong on exactly the ones that
    ///         matter.
    ///     </para>
    ///     <para>
    ///         <see cref="UseCharacterSetting" /> is the default, so an action nobody has configured
    ///         behaves exactly as it did before this setting existed. Whatever is chosen here is what
    ///         happens: an Action Behavior cannot quietly overrule it.
    ///     </para>
    ///     <para>
    ///         In a batch, the most talkative answering step decides for the batch — a visitor who
    ///         asked a question gets their answer even when the character also walked somewhere on
    ///         the way. An explicit <see cref="RememberOnly" /> silences the batch only when every
    ///         answering step in it agrees.
    ///     </para>
    /// </remarks>
    public enum ConvaiActionAnswerDelivery
    {
        /// <summary>
        ///     Defer to the character's <see cref="ConvaiActionFeedbackRelay" />. The default, and the
        ///     value a definition serialized before this setting existed reads as.
        /// </summary>
        UseCharacterSetting = 0,

        /// <summary>
        ///     The character keeps what the action found without saying it out loud. The answer still
        ///     reaches its own memory, so it can bring the fact up later if it becomes relevant.
        /// </summary>
        RememberOnly = 1,

        /// <summary>
        ///     The character decides for itself whether the answer is worth bringing up. Right for
        ///     background observation, wrong for a direct question — a character that abstains from
        ///     answering is the failure this setting exists to prevent.
        /// </summary>
        MentionIfRelevant = 2,

        /// <summary>
        ///     The character says what the action found. Use this for actions that answer a question:
        ///     reading a gauge, counting things, measuring a distance.
        /// </summary>
        TellThePlayer = 3
    }

    /// <summary>
    ///     Authoring definition for a single typed action parameter.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionParameterDefinition
    {
        /// <summary>Parameter name used as the wire key and template anchor.</summary>
        public string Name;

        /// <summary>Optional description sent to the backend for grounding.</summary>
        public string Description;

        /// <summary>Declared parameter type; <see cref="ConvaiActionParameterType.Auto" /> infers from the value.</summary>
        public ConvaiActionParameterType Type = ConvaiActionParameterType.Auto;

        /// <summary>Optional connector word rendered before the parameter in the wire template (for example "on" or "in").</summary>
        public string Connector;

        /// <summary>Allowed values when <see cref="Type" /> is <see cref="ConvaiActionParameterType.Choice" />.</summary>
        public List<string> Choices = new();

        /// <summary>Creates a normalized copy of this parameter definition.</summary>
        public ConvaiActionParameterDefinition Clone() =>
            new()
            {
                Name = Normalize(Name),
                Description = Normalize(Description),
                Type = Type,
                Connector = Normalize(Connector),
                Choices = Choices == null ? new List<string>() : new List<string>(Choices)
            };

        internal static string Normalize(string value) => ConvaiActionText.Normalize(value);
    }

    /// <summary>
    ///     Authoring definition that binds a backend action name to a local executor,
    ///     its typed parameters, and its dispatch behavior.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionDefinition
    {
        /// <summary>Canonical action name matched against backend commands (case-insensitive).</summary>
        [Tooltip("The name the Convai Character responds to (for example 'Move To'). Matched " +
                 "case-insensitively against the action the character decides to perform.")]
        public string ActionName;

        /// <summary>Optional description sent to the backend for grounding.</summary>
        [Tooltip("A short sentence sent to Convai so the character understands what this action " +
                 "does and when to use it.")]
        public string Description;

        /// <summary>Ordered typed parameters rendered into the wire template.</summary>
        [Tooltip("Ordered, typed inputs for this action (for example a target name or a number), " +
                 "sent to Convai as part of the action's command template.")]
        public List<ConvaiActionParameterDefinition> Parameters = new();

        /// <summary>Target constraint validated before the executor runs.</summary>
        [Tooltip("What kind of target this action needs before it can run: none, an object, a " +
                 "character, or either.")]
        public ConvaiActionTargetRequirement TargetRequirement;

        /// <summary>Executor component; must implement <see cref="IConvaiActionExecutor" />.</summary>
        [Tooltip("The scene component that performs this action. An explicit reference here always " +
                 "wins over the type hint below. (API name for engineers: IConvaiActionExecutor.)")]
        public MonoBehaviour Executor;

        /// <summary>
        ///     Optional short or full type name of an <see cref="IConvaiActionExecutor" /> component
        ///     to auto-bind on the character's hierarchy when <see cref="Executor" /> is null (for
        ///     example a definition authored inside a <see cref="ConvaiActionSet" /> asset, which
        ///     cannot hold a scene reference). An explicit <see cref="Executor" /> reference always
        ///     wins; the hint is only consulted when <see cref="Executor" /> is null. Resolution is
        ///     performed by <see cref="ConvaiActionExecutorBinder" />.
        /// </summary>
        [Tooltip("Type name of the behavior component to bind automatically when no explicit " +
                 "component is assigned above — used by Action Set assets, which cannot reference " +
                 "scene objects.")]
        public string ExecutorTypeHint;

        /// <summary>Per-step timeout in seconds; zero or less disables the timeout.</summary>
        [Tooltip("Maximum seconds this action may run before it is reported as failed. Zero or " +
                 "less means no time limit.")]
        public float TimeoutSeconds;

        /// <summary>Per-action override of the dispatcher batch failure policy.</summary>
        [Tooltip("What happens to the rest of a multi-step batch when this action fails. Use " +
                 "Dispatcher Default unless this action needs its own rule.")]
        public ConvaiActionFailurePolicyOverride FailurePolicyOverride;

        /// <summary>
        ///     What the Convai Character does with the answer this action found. Defaults to
        ///     <see cref="ConvaiActionAnswerDelivery.UseCharacterSetting" />, so an action nobody has
        ///     configured behaves exactly as it did before this setting existed.
        /// </summary>
        /// <remarks>
        ///     Only meaningful for an Action Behavior that returns
        ///     <see cref="ConvaiActionExecutionResult.Answered" />. An action that answers nothing has
        ///     nothing to deliver, whatever this is set to. Authoring only — never sent to Convai and
        ///     never rendered into the wire template by <see cref="ToActionConfigString" />.
        /// </remarks>
        [Tooltip("What the Convai Character does with what this action found out. Leave it on Use " +
                 "Character Setting unless this action answers a question the player asked.")]
        public ConvaiActionAnswerDelivery AnswerDelivery;

        /// <summary>Whether the first step of a fresh batch waits for character speech.</summary>
        // The gate releases on OnSpeechStarted as well as OnSpeechStopped
        // (ConvaiActionDispatcher.WaitForBotSpeechAsync), so this holds the action until the reply
        // *begins*, not until it ends. The tooltip says so; do not restore the old "finishes
        // speaking" wording without changing the gate to match it.
        [Tooltip("Tick to hold the first action of a batch until the Convai Character starts " +
                 "speaking its reply, so the action runs with the reply rather than ahead of it.")]
        public bool WaitForBotSpeech;

        /// <summary>Optional delay after the speech gate releases.</summary>
        [Tooltip("Extra seconds to wait after the character finishes speaking before the action " +
                 "starts.")]
        public float DelayAfterBotSpeechSeconds;

        // Serialized inverted so definitions authored before this field existed (scenes, prefabs,
        // ConvaiActionSet assets) deserialize with the missing bool as false — i.e. enabled. Never
        // rename this field or flip its meaning; both would silently disable upgraded projects.
        [SerializeField]
        [Tooltip("Unticked: the Convai Character will not know about or offer this action.")]
        private bool _disabled;

        // Behind a property rather than a plain public field so every assignment is normalized once,
        // here, instead of at each of the call sites that set it.
        [SerializeField]
        [Tooltip("Optional label you file this action under in the Actions Editor (for example " +
                 "'Counter'). Organization only — it is never sent to Convai and never changes what " +
                 "the character does.")]
        private string _category;

        /// <summary>
        ///     Optional authoring category this action is filed under — a label the user chooses, used
        ///     to group the list in the Actions Editor and in the Convai Actions
        ///     inspector. Empty means uncategorized.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Organization only. The category is not part of the <c>action_config</c> sent to the
        ///         backend, is not rendered into the wire template by
        ///         <see cref="ToActionConfigString" />, and therefore cannot change which action a
        ///         Convai Character chooses or how it performs it.
        ///     </para>
        ///     <para>
        ///         Assigned values are normalized (trimmed, inner whitespace collapsed, capped in
        ///         length), and two names that differ only in casing are the same category.
        ///     </para>
        /// </remarks>
        public string Category
        {
            get => _category ?? string.Empty;
            set => _category = ConvaiActionCategory.Normalize(value);
        }

        /// <summary>
        ///     Normalizes a category name the way <see cref="Category" /> does — trimmed, inner
        ///     whitespace collapsed, capped in length, and <see cref="string.Empty" /> for "no
        ///     category". Exposed so tooling that writes definitions through Unity's serialization
        ///     (which bypasses the property) files actions under exactly the same names authoring does.
        /// </summary>
        public static string NormalizeCategory(string value) => ConvaiActionCategory.Normalize(value);

        /// <summary>Whether two category names refer to the same category (case-insensitive).</summary>
        public static bool IsSameCategory(string left, string right) => ConvaiActionCategory.AreSame(left, right);

        /// <summary>
        ///     Authored availability of this action. A disabled action is excluded from the
        ///     <c>action_config</c> sent to the backend (at connect and in mid-session re-syncs), so
        ///     the Convai Character does not know about or offer it; a stale backend command for a
        ///     disabled action is reported as unhandled instead of executing. Defaults to
        ///     <c>true</c>, including for definitions serialized before this property existed.
        ///     <see cref="ConvaiCharacterActions.SetActionAvailable" /> can override this per
        ///     session at runtime.
        /// </summary>
        public bool Enabled
        {
            get => !_disabled;
            set => _disabled = !value;
        }

        /// <summary>Creates a normalized deep copy of this definition (executor reference is shared).</summary>
        public ConvaiActionDefinition Clone() =>
            new()
            {
                ActionName = NormalizeActionName(ActionName),
                Description = ConvaiActionParameterDefinition.Normalize(Description),
                Parameters = CloneParameters(Parameters),
                TargetRequirement = TargetRequirement,
                Executor = Executor,
                ExecutorTypeHint = ConvaiActionParameterDefinition.Normalize(ExecutorTypeHint),
                TimeoutSeconds = TimeoutSeconds,
                FailurePolicyOverride = FailurePolicyOverride,
                AnswerDelivery = AnswerDelivery,
                WaitForBotSpeech = WaitForBotSpeech,
                DelayAfterBotSpeechSeconds = DelayAfterBotSpeechSeconds,
                Enabled = Enabled,
                Category = Category
            };

        /// <summary>Renders the wire template string sent to the backend for this definition.</summary>
        public string ToActionConfigString() => ConvaiActionTemplateRenderer.Render(this);

        /// <summary>Creates a normalized deep copy of a definition list.</summary>
        public static List<ConvaiActionDefinition> CloneList(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var clone = new List<ConvaiActionDefinition>(definitions?.Count ?? 0);
            if (definitions == null)
                return clone;

            for (int i = 0; i < definitions.Count; i++)
                clone.Add(definitions[i]?.Clone());

            return clone;
        }

        internal static List<ConvaiActionDefinition> FilterAndClone(
            IReadOnlyList<ConvaiActionDefinition> definitions,
            IReadOnlyList<string> allowedActionNames = null,
            Action<string> onDuplicate = null,
            bool requireExecutable = false)
        {
            if (definitions == null || definitions.Count == 0)
                return new List<ConvaiActionDefinition>();

            HashSet<string> allowed = BuildAllowedNameSet(allowedActionNames);
            var filtered = new List<ConvaiActionDefinition>(definitions.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string actionName = NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName))
                    continue;

                if (!IsActionAllowed(definition, actionName, allowed))
                    continue;

                if (requireExecutable && !ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                    continue;

                if (!seen.Add(actionName))
                {
                    onDuplicate?.Invoke(actionName);
                    continue;
                }

                filtered.Add(definition.Clone());
            }

            return filtered;
        }

        /// <summary>
        ///     Maps each definition's whole rendered wire string back to the definition that
        ///     produced it. First occurrence wins on a collision.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The exact way to answer "which definition is this wire string". Recovering a
        ///         canonical name from the string and looking that up is a parse, and a parse can be
        ///         wrong: an action named <c>Walk</c> whose first parameter carries the connector
        ///         <c>to</c> renders as <c>Walk to {destination: reference}</c>, whose recovered name
        ///         is <c>Walk to</c>. That missed the lookup — and a miss is not harmless, it is
        ///         wrong in two specific ways. The availability filter treats a miss as "no opinion"
        ///         and sends the action anyway, so an action the author disabled was still offered
        ///         to the Convai Character; and the runtime patch reconciler treats it as "no local
        ///         definition" and rejects the entire mid-session sync, so registering a target
        ///         stopped working for that character.
        ///     </para>
        ///     <para>
        ///         Both callers hold the catalog that produced the strings, so neither has to parse
        ///         at all. <see cref="ExtractCanonicalActionName" /> remains the fallback for strings
        ///         that did not come from a local definition — an override action config — where a
        ///         best-effort read is all there is.
        ///     </para>
        /// </remarks>
        internal static Dictionary<string, ConvaiActionDefinition> BuildRenderedLookup(
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var lookup = new Dictionary<string, ConvaiActionDefinition>(StringComparer.Ordinal);
            if (definitions == null)
                return lookup;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string rendered = definition?.ToActionConfigString();
                if (string.IsNullOrEmpty(rendered) || lookup.ContainsKey(rendered))
                    continue;

                lookup[rendered] = definition;
            }

            return lookup;
        }

        /// <summary>
        ///     Resolves a rendered wire string to its definition: exactly when the string came from
        ///     one of these definitions, by best-effort name recovery when it did not.
        /// </summary>
        internal static ConvaiActionDefinition ResolveRendered(
            string renderedAction,
            Dictionary<string, ConvaiActionDefinition> renderedLookup,
            Dictionary<string, ConvaiActionDefinition> nameLookup,
            out string canonicalName)
        {
            string normalized = NormalizeActionName(renderedAction);
            if (renderedLookup != null &&
                renderedLookup.TryGetValue(normalized, out ConvaiActionDefinition exact))
            {
                canonicalName = NormalizeActionName(exact.ActionName);
                return exact;
            }

            canonicalName = ExtractCanonicalActionName(normalized);
            if (nameLookup != null &&
                nameLookup.TryGetValue(canonicalName ?? string.Empty, out ConvaiActionDefinition byName))
                return byName;

            return null;
        }

        internal static Dictionary<string, ConvaiActionDefinition> BuildLookup(
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var lookup = new Dictionary<string, ConvaiActionDefinition>(StringComparer.OrdinalIgnoreCase);
            if (definitions == null)
                return lookup;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string actionName = NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName) || lookup.ContainsKey(actionName))
                    continue;

                lookup[actionName] = definition;
            }

            return lookup;
        }

        internal static string NormalizeActionName(string actionName) => ConvaiActionText.Normalize(actionName);

        private static List<ConvaiActionParameterDefinition> CloneParameters(
            IReadOnlyList<ConvaiActionParameterDefinition> parameters)
        {
            var clone = new List<ConvaiActionParameterDefinition>(parameters?.Count ?? 0);
            if (parameters == null)
                return clone;

            for (int i = 0; i < parameters.Count; i++)
                clone.Add(parameters[i]?.Clone());

            return clone;
        }

        private static HashSet<string> BuildAllowedNameSet(IReadOnlyList<string> allowedActionNames)
        {
            if (allowedActionNames == null || allowedActionNames.Count == 0)
                return null;

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allowedActionNames.Count; i++)
            {
                string actionString = NormalizeActionName(allowedActionNames[i]);
                if (string.IsNullOrEmpty(actionString))
                    continue;

                allowed.Add(actionString);

                string canonicalName = ExtractCanonicalActionName(actionString);
                if (!string.IsNullOrEmpty(canonicalName))
                    allowed.Add(canonicalName);
            }

            return allowed;
        }

        private static bool IsActionAllowed(
            ConvaiActionDefinition definition,
            string actionName,
            HashSet<string> allowed)
        {
            if (allowed == null)
                return true;

            if (allowed.Contains(actionName))
                return true;

            string rendered = NormalizeActionName(definition?.ToActionConfigString());
            return !string.IsNullOrEmpty(rendered) && allowed.Contains(rendered);
        }

        /// <summary>
        ///     Extracts the canonical action name from a rendered action-config string. Used to map
        ///     rendered wire strings back to definitions — by the availability filter when building
        ///     the connect payload, and by runtime action-config patch reconciliation.
        /// </summary>
        /// <remarks>
        ///     Reads the delimiters from <see cref="ConvaiActionWireGrammar" /> rather than naming
        ///     them again here. It named them again here, and that is precisely how a third
        ///     description of one grammar came to exist alongside the renderer and the response
        ///     reader — each able to change without the others noticing.
        /// </remarks>
        internal static string ExtractCanonicalActionName(string actionString) =>
            ConvaiActionWireGrammar.CanonicalNameOf(actionString);
    }
}
