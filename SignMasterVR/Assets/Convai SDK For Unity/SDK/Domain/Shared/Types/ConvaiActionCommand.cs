using System;
using System.Collections.Generic;

namespace Convai.Shared.Types
{
    /// <summary>How an authored action parameter's raw text is coerced during enrichment.</summary>
    public enum ConvaiActionParameterType
    {
        /// <summary>Infer reference, number, bool, or string best-effort (in that order).</summary>
        Auto = 0,

        /// <summary>Resolve an authored object or character target by name.</summary>
        Reference = 1,

        /// <summary>Keep the raw text.</summary>
        String = 2,

        /// <summary>Parse an invariant-culture float.</summary>
        Number = 3,

        /// <summary>Parse true/yes/1 or false/no/0.</summary>
        Bool = 4,

        /// <summary>Require one of the authored choice strings; mismatch flags the value.</summary>
        Choice = 5
    }

    /// <summary>
    ///     Shared string hygiene for action names, targets, and parameter keys: null or whitespace
    ///     collapses to <see cref="string.Empty" />, everything else is trimmed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately no more than that. Cleaning up what a language model emits — the quotes it
    ///         wraps values in, the separator it echoes back off the template it was shown — is
    ///         reading the wire, and belongs on the wire-reading path rather than in a helper that
    ///         also runs over names an author typed into an inspector. Put here, it quietly rewrote
    ///         authored target names too, and every <c>Clone</c> and <c>ToString</c> on the way past.
    ///         See <c>ConvaiActionWireText</c>.
    ///     </para>
    ///     <para>
    ///         Returns the original reference when there is nothing to trim, which is the overwhelming
    ///         majority of calls — this runs on every candidate at every rung of the resolution
    ///         ladder, so its cost is multiplied by the number of targets in the scene.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionText
    {
        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && char.IsWhiteSpace(value[start])) start++;
            while (end > start && char.IsWhiteSpace(value[end])) end--;

            return start == 0 && end == value.Length - 1
                ? value
                : value.Substring(start, end - start + 1);
        }

    }

    /// <summary>
    ///     Kind of scene entity an action target resolved to.
    /// </summary>
    public enum ConvaiActionTargetKind
    {
        /// <summary>No target resolved.</summary>
        None = 0,

        /// <summary>An authored action object.</summary>
        Object = 1,

        /// <summary>An authored action character.</summary>
        Character = 2
    }

    /// <summary>
    ///     Name-and-kind handle a Reference parameter resolved to during enrichment. It is a lookup
    ///     key, not a scene binding: resolve it to live objects through
    ///     <c>ConvaiActionInvocation.GetReference(name)</c>.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionParameterReference
    {
        /// <summary>Authored target name the raw value matched (trimmed, never null).</summary>
        public string Name { get; set; }

        /// <summary>Whether the name matched an authored object or character.</summary>
        public ConvaiActionTargetKind Kind { get; set; }

        public ConvaiActionParameterReference()
        {
        }

        public ConvaiActionParameterReference(string name, ConvaiActionTargetKind kind = ConvaiActionTargetKind.None)
        {
            Name = ConvaiActionText.Normalize(name);
            Kind = kind;
        }

        public ConvaiActionParameterReference Clone() =>
            new(Name, Kind);
    }

    /// <summary>
    ///     Whether a parameter's value came from the Convai Character at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An action declaring three parameters always comes back with three, because unfilled
    ///         slots are padded to keep the values lined up with the authored order. Padding is the
    ///         right mechanic and it used to be indistinguishable from an answer, so an Action
    ///         Behavior could not tell "no destination was given" from "the destination is blank"
    ///         and quietly did something for both.
    ///     </para>
    ///     <para>
    ///         The line is <em>whether anything was assigned to the slot</em>, not whether the text
    ///         is empty. A character that names a parameter and leaves it blank has said something
    ///         about it — that is <see cref="Provided" /> with empty text, and worth seeing. A
    ///         character that said nothing the slot could be filled from is <see cref="Missing" />.
    ///     </para>
    ///     <para>
    ///         Two states, because two is what actually happens. A third — a value substituted from
    ///         some default — was considered and left out: nothing in the pipeline substitutes one.
    ///         A Choice value outside its authored list still flows through as written and is
    ///         reported by <see cref="ConvaiActionParameterValue.IsConstraintMatch" />.
    ///     </para>
    /// </remarks>
    public enum ConvaiActionParameterPresence
    {
        /// <summary>
        ///     A value was supplied for this slot. The default, so a parameter value built in your
        ///     own code means what it has always meant without setting anything.
        /// </summary>
        Provided = 0,

        /// <summary>
        ///     No value reached this slot. The parameter exists because the action declares it, not
        ///     because anything was said about it.
        /// </summary>
        Missing = 1
    }

    /// <summary>
    ///     One typed parameter after enrichment. All representations are populated best-effort
    ///     from the raw text; <see cref="Type" /> says which one the authored template intends.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionParameterValue
    {
        /// <summary>Effective type after coercion (an authored Auto resolves to a concrete type).</summary>
        public ConvaiActionParameterType Type { get; set; } = ConvaiActionParameterType.String;

        /// <summary>Trimmed raw text this value was parsed from.</summary>
        public string RawValue { get; set; }

        /// <summary>The value as text (same as <see cref="RawValue" /> after trimming).</summary>
        public string StringValue { get; set; }

        /// <summary>Parsed float, or 0 when the text is not numeric.</summary>
        public float NumberValue { get; set; }

        /// <summary>Parsed bool, or false when the text is not a recognized boolean.</summary>
        public bool BoolValue { get; set; }

        /// <summary>Matched authored target when the text named one; null otherwise.</summary>
        public ConvaiActionParameterReference ResolvedReference { get; set; }

        /// <summary>False only when a Choice parameter's text is not one of its authored choices.</summary>
        public bool IsConstraintMatch { get; set; } = true;

        /// <summary>
        ///     Whether the Convai Character supplied a value for this parameter, or the slot is only
        ///     present because the action declares it. See <see cref="ConvaiActionParameterPresence" />.
        /// </summary>
        /// <remarks>
        ///     Check this before acting on an empty value. <c>Missing</c> means nothing was said
        ///     about this parameter and an Action Behavior should decide for itself — refuse, ask,
        ///     or use its own default — rather than treat the emptiness as an instruction.
        /// </remarks>
        public ConvaiActionParameterPresence Presence { get; set; }

        public ConvaiActionParameterValue()
        {
        }

        /// <summary>Creates a deep copy of this parameter value.</summary>
        public ConvaiActionParameterValue Clone() =>
            new()
            {
                Type = Type,
                RawValue = RawValue,
                StringValue = StringValue,
                NumberValue = NumberValue,
                BoolValue = BoolValue,
                ResolvedReference = ResolvedReference?.Clone(),
                IsConstraintMatch = IsConstraintMatch,
                Presence = Presence
            };
    }

    /// <summary>
    ///     Structured action command returned by the backend for the current turn.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionCommand
    {
        /// <summary>
        ///     The parameter an action's target travels under when the action declares none of its own.
        /// </summary>
        /// <remarks>
        ///     Shared vocabulary rather than one side's private detail: the template renderer offers a
        ///     slot by this name on the way out, enrichment parks an inline target under it on the way
        ///     in, and resolution reads it. It lived on the response parser while only the inbound path
        ///     used it, which made the outbound renderer depend on the reader to name its own slot.
        /// </remarks>
        internal const string TargetParameterKey = "target";

        /// <summary>Required action name selected by the backend.</summary>
        public string Name { get; set; }

        /// <summary>Optional object or character target name resolved by the backend.</summary>
        public string Target { get; set; }

        /// <summary>Raw action string reconstructed from backend name and target.</summary>
        public string ActionString { get; set; }

        /// <summary>Typed parameters parsed from the backend response and active Unity template.</summary>
        public Dictionary<string, ConvaiActionParameterValue> Parameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the first action in a fresh batch should wait for character speech.</summary>
        public bool WaitForBotSpeech { get; set; }

        /// <summary>Optional delay after the speech gate releases.</summary>
        public float DelayAfterBotSpeechSeconds { get; set; }

        /// <summary>
        ///     True once the command has been enriched against the active action templates.
        ///     The dispatcher enriches unmarked commands exactly once before dispatch.
        /// </summary>
        public bool Enriched { get; set; }

        /// <summary>
        ///     Internal editor-tooling seam: skips the dispatcher's first-step speech gate for this
        ///     command. Editor test runs happen without a conversation, so there is no speech to
        ///     wait for. Never set by wire parsing, never serialized (internal auto-property), and
        ///     carried through <see cref="Clone" />. Not public API.
        /// </summary>
        internal bool BypassSpeechGate { get; set; }

        /// <summary>
        ///     Internal editor-tooling seam: lets one injected command run even when its action is
        ///     currently unavailable (Test Run's explicit "Run Anyway"). Never set by wire parsing,
        ///     never serialized, carried through <see cref="Clone" />. Not public API.
        /// </summary>
        internal bool BypassAvailability { get; set; }

        /// <summary>
        ///     What the admission stage decided this command's target was, by name.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A command is judged when it arrives and performed some time later — behind another
        ///         batch, behind the speech gate — and the scene can change in between. Freezing the
        ///         resolved target at admission and handing that to the executor would therefore be
        ///         wrong, not safer: the right target at dispatch is the one resolved at dispatch.
        ///     </para>
        ///     <para>
        ///         So the admission answer travels as an <em>expectation</em> instead. Dispatch
        ///         resolves again and compares; a difference is not a fault to suppress but the one
        ///         piece of information nobody had — that the world moved between the character
        ///         agreeing to do something and doing it. Names, not references, deliberately: a live
        ///         reference held across a queue is exactly the stale handle this avoids.
        ///     </para>
        ///     <para>
        ///         Internal, never serialized, carried through <see cref="Clone" />. Not public API.
        ///     </para>
        /// </remarks>
        internal string AdmittedTargetName { get; set; }

        /// <summary>Returns true when the command includes a target reference.</summary>
        public bool HasTarget => !string.IsNullOrWhiteSpace(Target);

        public ConvaiActionCommand()
        {
        }

        public ConvaiActionCommand(string name, string target = null)
        {
            Name = ConvaiActionText.Normalize(name);
            Target = ConvaiActionText.Normalize(target);
            ActionString = HasTarget ? $"{Name} {Target}".Trim() : Name;
        }

        /// <summary>
        ///     Deep-clones a batch, replacing null entries with empty commands. Each layer of the
        ///     receive→dispatch path snapshots the batch it was handed because every entry point is
        ///     public and callers may keep mutating their list.
        /// </summary>
        internal static IReadOnlyList<ConvaiActionCommand> CloneBatch(IReadOnlyList<ConvaiActionCommand> actions)
        {
            if (actions == null || actions.Count == 0)
                return Array.Empty<ConvaiActionCommand>();

            var clone = new ConvaiActionCommand[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                clone[i] = actions[i]?.Clone() ?? new ConvaiActionCommand();
            return clone;
        }

        /// <summary>Creates a normalized copy of this action command.</summary>
        public ConvaiActionCommand Clone() =>
            new(ConvaiActionText.Normalize(Name), ConvaiActionText.Normalize(Target))
            {
                ActionString = ConvaiActionText.Normalize(ActionString),
                Parameters = CloneParameters(Parameters),
                WaitForBotSpeech = WaitForBotSpeech,
                DelayAfterBotSpeechSeconds = DelayAfterBotSpeechSeconds,
                Enriched = Enriched,
                BypassSpeechGate = BypassSpeechGate,
                BypassAvailability = BypassAvailability,
                AdmittedTargetName = AdmittedTargetName
            };

        /// <inheritdoc />
        public override string ToString() => HasTarget ? $"{ConvaiActionText.Normalize(Name)} {ConvaiActionText.Normalize(Target)}" : ConvaiActionText.Normalize(Name);

        private static Dictionary<string, ConvaiActionParameterValue> CloneParameters(
            Dictionary<string, ConvaiActionParameterValue> parameters)
        {
            var clone = new Dictionary<string, ConvaiActionParameterValue>(StringComparer.OrdinalIgnoreCase);
            if (parameters == null)
                return clone;

            foreach (KeyValuePair<string, ConvaiActionParameterValue> pair in parameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                clone[ConvaiActionText.Normalize(pair.Key)] = pair.Value?.Clone() ?? new ConvaiActionParameterValue();
            }

            return clone;
        }
    }
}
