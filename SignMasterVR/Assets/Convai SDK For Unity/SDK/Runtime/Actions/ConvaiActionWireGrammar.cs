using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Convai.Shared.Types;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Which part of an action definition a piece of authored text is, for the purpose of
    ///     deciding which grammar tokens it may not contain.
    /// </summary>
    /// <remarks>
    ///     The reserved set is not one blacklist. A hyphen is the separator only with spaces around
    ///     it, so <c>path-blocked</c> is a perfectly good choice value while <c>Walk - To</c> is not
    ///     a usable action name. Banning the character everywhere would reject content this SDK
    ///     already ships (see <see cref="ConvaiActionWireGrammar" /> remarks), and a validation rule
    ///     that fires on legitimate content is a rule somebody switches off.
    /// </remarks>
    internal enum ConvaiActionGrammarSurface
    {
        /// <summary>The action's own name — the part a command is matched against.</summary>
        ActionName = 0,

        /// <summary>
        ///     A whole rendered action, judged against the rest of the catalog rather than on its
        ///     own. Some faults only exist between two definitions.
        /// </summary>
        RenderedAction = 5,

        /// <summary>A parameter's name, which is rendered inside a slot before the type marker.</summary>
        ParameterName = 1,

        /// <summary>One allowed value of a Choice parameter, rendered inside the choice block.</summary>
        ChoiceValue = 2,

        /// <summary>An action or parameter description, rendered after the separator.</summary>
        Description = 3,

        /// <summary>A connector word rendered before a parameter's slot.</summary>
        Connector = 4
    }

    /// <summary>
    ///     One piece of authored text that cannot be rendered unambiguously.
    /// </summary>
    internal readonly struct ConvaiActionGrammarViolation
    {
        /// <summary>Which part of the definition the offending text came from.</summary>
        public ConvaiActionGrammarSurface Surface { get; }

        /// <summary>The text as authored.</summary>
        public string Value { get; }

        /// <summary>The token that makes it ambiguous, as it would appear on the wire.</summary>
        public string Token { get; }

        /// <summary>A sentence naming the problem and the repair, safe to show a user verbatim.</summary>
        public string Explanation { get; }

        internal ConvaiActionGrammarViolation(
            ConvaiActionGrammarSurface surface, string value, string token, string explanation)
        {
            Surface = surface;
            Value = value;
            Token = token;
            Explanation = explanation;
        }
    }

    /// <summary>
    ///     The single owner of the string format actions travel in: it writes the format, it reads
    ///     the format back, and it says which authored text the format cannot carry.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this type exists.</b> Rendering is a grammar and reading is that grammar run
    ///         backwards, but the two were written independently and a third place —
    ///         <c>ExtractCanonicalActionName</c> — re-guessed the delimiters a fourth way. Every new
    ///         rendering decision therefore created a new way to be misread, silently, somewhere
    ///         else. The tokens are declared once here, as data, and everything that writes or reads
    ///         the format asks this type what they are.
    ///     </para>
    ///     <para>
    ///         <b>Why validation is part of it.</b> The worst ambiguity in this area was never the
    ///         model's doing. Nothing checked that an authored action name avoided the grammar's own
    ///         delimiters, so an action called <c>Sit - Chair</c> rendered as
    ///         <c>Sit - Chair - description</c> and every consumer that recovered a canonical name
    ///         from a rendered string got <c>Sit</c> — the availability filter and the mid-session
    ///         config sync then addressed an action that does not exist, with no diagnostic
    ///         anywhere. A grammar that can state what it cannot carry is the only thing that closes
    ///         that, and it belongs beside the renderer rather than in a validator that has to keep
    ///         up with it.
    ///     </para>
    ///     <para>
    ///         <b>Reserved tokens are per surface, deliberately.</b> A bare <c>-</c> is legal
    ///         everywhere: the separator is <c>" - "</c>, with spaces, and only ever recognized with
    ///         them. Choice values shipped with this SDK — <c>target-unreachable</c>,
    ///         <c>path-blocked</c>, <c>peer-missing</c> — depend on that, and a blanket character ban
    ///         would have rejected them on the day it shipped.
    ///     </para>
    ///     <para>
    ///         <b>This type does not change the wire format.</b> It renders exactly what the
    ///         previous renderer rendered, byte for byte; the tests that assert the rendered strings
    ///         are the guard for that and they were not touched.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionWireGrammar
    {
        // ── The token table ───────────────────────────────────────────────────────────────
        // This is the format. Everything that writes it, reads it back, or repairs a model's echo
        // of it takes its delimiters from here — the renderer above, CanonicalNameOf below,
        // ConvaiActionWireText's slot stripper, and the response parser's name-boundary test. A
        // token changed here changes all of them together, which is the property that was missing
        // when three files described this format separately.

        /// <summary>Separates the action's callable form from the prose that explains it.</summary>
        internal const string DescriptionSeparator = " - ";

        /// <summary>Opens a parameter slot.</summary>
        internal const char SlotOpen = '{';

        /// <summary>Closes a parameter slot.</summary>
        internal const char SlotClose = '}';

        /// <summary>Separates a slot's parameter name from its type word.</summary>
        internal const string TypeMarker = ": ";

        /// <summary>Opens the list of allowed values on a Choice slot.</summary>
        internal const char ChoiceOpen = '[';

        /// <summary>Closes the list of allowed values on a Choice slot.</summary>
        internal const char ChoiceClose = ']';

        /// <summary>Separates one allowed value from the next inside a choice list.</summary>
        internal const char ChoiceDelimiter = '|';

        /// <summary>The prefix that marks the start of a slot when scanning a rendered string.</summary>
        private const string SlotStart = " {";

        // ── Rendering ─────────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Renders the wire form of <paramref name="definition" />.
        /// </summary>
        /// <remarks>
        ///     Authored text is folded to printable ASCII on the way out. That fold is what makes
        ///     the format deterministic across locales and keyboards, and it is also why the
        ///     separator cannot be a character that is harder to collide with: anything outside
        ///     ASCII becomes a space here.
        /// </remarks>
        internal static string Render(ConvaiActionDefinition definition)
        {
            if (definition == null)
                return string.Empty;

            string actionName = FoldToWire(
                ConvaiActionDefinition.NormalizeActionName(definition.ActionName),
                ConvaiActionGrammarSurface.ActionName);
            if (string.IsNullOrEmpty(actionName))
                return string.Empty;

            var builder = new StringBuilder(actionName);
            AppendParameterSlots(builder, definition);
            AppendTargetSlot(builder, definition);
            AppendDescriptions(builder, definition);
            return builder.ToString();
        }

        private static void AppendParameterSlots(StringBuilder builder, ConvaiActionDefinition definition)
        {
            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters == null)
                return;

            for (int i = 0; i < parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = parameters[i];
                string parameterName = FoldToWire(
                    ConvaiActionParameterDefinition.Normalize(parameter?.Name),
                    ConvaiActionGrammarSurface.ParameterName);
                if (string.IsNullOrEmpty(parameterName))
                    continue;

                string connector = FoldToWire(
                    ConvaiActionParameterDefinition.Normalize(parameter.Connector),
                    ConvaiActionGrammarSurface.Connector);
                builder.Append(' ');
                if (!string.IsNullOrEmpty(connector))
                    builder.Append(connector).Append(' ');

                builder.Append(SlotOpen)
                    .Append(parameterName)
                    .Append(TypeMarker)
                    .Append(ToWireType(parameter.Type));

                AppendChoices(builder, parameter);
                builder.Append(SlotClose);
            }
        }

        private static void AppendChoices(StringBuilder builder, ConvaiActionParameterDefinition parameter)
        {
            if (parameter.Type != ConvaiActionParameterType.Choice ||
                parameter.Choices == null ||
                parameter.Choices.Count == 0)
                return;

            builder.Append(' ').Append(ChoiceOpen);
            bool wroteChoice = false;
            for (int i = 0; i < parameter.Choices.Count; i++)
            {
                string choice = FoldToWire(
                    ConvaiActionParameterDefinition.Normalize(parameter.Choices[i]),
                    ConvaiActionGrammarSurface.ChoiceValue);
                if (string.IsNullOrEmpty(choice))
                    continue;

                if (wroteChoice)
                    builder.Append(ChoiceDelimiter);

                builder.Append(choice);
                wroteChoice = true;
            }

            builder.Append(ChoiceClose);
        }

        /// <summary>
        ///     Gives an action that needs something to act on a slot to put it in.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An action's target requirement was known to the SDK and never said on the wire, so
        ///         an action that declares no parameters of its own — the ordinary shape of "walk to
        ///         somewhere" — was offered to the Convai Character as a bare name. The prose
        ///         description asked for a place; the template offered nowhere to put one.
        ///     </para>
        ///     <para>
        ///         Named <c>target</c> deliberately: that is the key enrichment already parks an
        ///         inline target under, so a filled slot and a name arriving inside the action text
        ///         land in exactly the same place.
        ///     </para>
        /// </remarks>
        private static void AppendTargetSlot(StringBuilder builder, ConvaiActionDefinition definition)
        {
            if (!RendersImplicitTargetSlot(definition))
                return;

            builder.Append(' ')
                .Append(SlotOpen)
                .Append(ConvaiActionCommand.TargetParameterKey)
                .Append(TypeMarker)
                .Append(ToWireType(ConvaiActionParameterType.Reference))
                .Append(SlotClose);
        }

        /// <summary>
        ///     Whether the wire form of this action carries a target slot the author did not declare.
        /// </summary>
        /// <remarks>
        ///     Asked by the renderer on the way out and by the reader on the way in, so the two
        ///     cannot disagree about how many slots exist. They did disagree, and it cost a whole
        ///     class of action: the template offered <c>{target: reference}</c>, the Convai Character
        ///     filled it in, and the reader — which sized its split by the <em>declared</em>
        ///     parameters — had one value too many and discarded the last one. The action was then
        ///     dropped for having no target, having been sent a perfectly good one.
        /// </remarks>
        internal static bool RendersImplicitTargetSlot(ConvaiActionDefinition definition) =>
            definition != null &&
            definition.TargetRequirement != ConvaiActionTargetRequirement.None &&
            // An action that already declares a parameter capable of carrying the target has its slot
            // — adding a second would ask for the same thing twice and invite the model to split it.
            !DeclaresTargetCapableParameter(definition);

        /// <summary>
        ///     Every slot this action's wire form presents, in the order it presents them: the
        ///     author's declared parameters, plus the implicit target slot when one is rendered.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is the list the reader must split against. Sizing a split by
        ///         <c>definition.Parameters</c> alone means reading a different template from the one
        ///         that was sent, which is the mistake this whole type exists to make impossible —
        ///         and it happened anyway, because the implicit slot was added to the renderer and
        ///         nothing told the reader.
        ///     </para>
        ///     <para>
        ///         The implicit slot is named for the key resolution already looks under, so a value
        ///         that lands in it is found without any special case downstream. Returns the
        ///         author's own list unchanged — no allocation — in the common case where no implicit
        ///         slot is rendered.
        ///     </para>
        /// </remarks>
        internal static IReadOnlyList<ConvaiActionParameterDefinition> SlotsOf(ConvaiActionDefinition definition)
        {
            IReadOnlyList<ConvaiActionParameterDefinition> declared =
                definition?.Parameters ?? (IReadOnlyList<ConvaiActionParameterDefinition>)Array.Empty<ConvaiActionParameterDefinition>();

            if (!RendersImplicitTargetSlot(definition))
                return declared;

            var slots = new List<ConvaiActionParameterDefinition>(declared.Count + 1);
            for (int i = 0; i < declared.Count; i++)
                slots.Add(declared[i]);

            slots.Add(new ConvaiActionParameterDefinition
            {
                Name = ConvaiActionCommand.TargetParameterKey,
                Type = ConvaiActionParameterType.Reference
            });

            return slots;
        }

        /// <summary>
        ///     Whether one of the action's own parameters can already carry the target — the same
        ///     Auto/Reference test the resolution ladder applies when it looks for one.
        /// </summary>
        private static bool DeclaresTargetCapableParameter(ConvaiActionDefinition definition)
        {
            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters == null)
                return false;

            for (int i = 0; i < parameters.Count; i++)
            {
                ConvaiActionParameterDefinition parameter = parameters[i];
                if (parameter == null)
                    continue;

                if (parameter.Type is ConvaiActionParameterType.Auto or ConvaiActionParameterType.Reference &&
                    !string.IsNullOrEmpty(ConvaiActionParameterDefinition.Normalize(parameter.Name)))
                    return true;
            }

            return false;
        }

        private static void AppendDescriptions(StringBuilder builder, ConvaiActionDefinition definition)
        {
            string description = FoldToWire(
                ConvaiActionParameterDefinition.Normalize(definition.Description),
                ConvaiActionGrammarSurface.Description);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(description))
                parts.Add(description);

            if (definition.Parameters != null)
            {
                for (int i = 0; i < definition.Parameters.Count; i++)
                {
                    ConvaiActionParameterDefinition parameter = definition.Parameters[i];
                    string name = FoldToWire(
                        ConvaiActionParameterDefinition.Normalize(parameter?.Name),
                        ConvaiActionGrammarSurface.ParameterName);
                    string paramDescription = FoldToWire(
                        ConvaiActionParameterDefinition.Normalize(parameter?.Description),
                        ConvaiActionGrammarSurface.Description);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(paramDescription))
                        continue;

                    parts.Add($"{name}: {paramDescription}");
                }
            }

            if (parts.Count == 0)
                return;

            builder.Append(DescriptionSeparator).Append(string.Join(" ", parts));
        }

        internal static string ToWireType(ConvaiActionParameterType type) =>
            type switch
            {
                ConvaiActionParameterType.Reference => "reference",
                ConvaiActionParameterType.String => "string",
                ConvaiActionParameterType.Number => "number",
                ConvaiActionParameterType.Bool => "bool",
                ConvaiActionParameterType.Choice => "choice",
                _ => "auto"
            };

        // ── Reading the format back ───────────────────────────────────────────────────────

        /// <summary>
        ///     Recovers the action name from a rendered wire string — everything before the first
        ///     slot or the description separator, whichever comes first.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The one implementation. It used to be a private scan inside
        ///         <c>ConvaiActionDefinition</c> that hard-coded the same two delimiters the renderer
        ///         hard-coded separately, which is how the two could drift.
        ///     </para>
        ///     <para>
        ///         It is only unambiguous because <see cref="Validate" /> refuses an action name
        ///         containing either delimiter. Without that refusal this function is a guess, and it
        ///         was one: <c>Sit - Chair</c> came back as <c>Sit</c>.
        ///     </para>
        /// </remarks>
        internal static string CanonicalNameOf(string renderedAction)
        {
            string value = ConvaiActionText.Normalize(renderedAction);
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            int slotStart = value.IndexOf(SlotStart, StringComparison.Ordinal);
            int descriptionStart = value.IndexOf(DescriptionSeparator, StringComparison.Ordinal);

            int delimiter = slotStart;
            if (descriptionStart >= 0 && (delimiter < 0 || descriptionStart < delimiter))
                delimiter = descriptionStart;

            return delimiter >= 0
                ? ConvaiActionText.Normalize(value.Substring(0, delimiter))
                : value;
        }

        // ── Saying what the format cannot carry ───────────────────────────────────────────

        /// <summary>
        ///     Reports every piece of authored text this definition cannot render unambiguously.
        /// </summary>
        /// <remarks>
        ///     Returns an empty list for a definition that is fine, which is the overwhelming
        ///     majority — the list is only allocated once something is actually wrong.
        /// </remarks>
        internal static IReadOnlyList<ConvaiActionGrammarViolation> Validate(ConvaiActionDefinition definition)
        {
            List<ConvaiActionGrammarViolation> violations = null;
            if (definition == null)
                return Array.Empty<ConvaiActionGrammarViolation>();

            Check(ref violations, ConvaiActionGrammarSurface.ActionName,
                ConvaiActionDefinition.NormalizeActionName(definition.ActionName));
            Check(ref violations, ConvaiActionGrammarSurface.Description,
                ConvaiActionParameterDefinition.Normalize(definition.Description));

            IReadOnlyList<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    ConvaiActionParameterDefinition parameter = parameters[i];
                    if (parameter == null)
                        continue;

                    Check(ref violations, ConvaiActionGrammarSurface.ParameterName,
                        ConvaiActionParameterDefinition.Normalize(parameter.Name));
                    Check(ref violations, ConvaiActionGrammarSurface.Connector,
                        ConvaiActionParameterDefinition.Normalize(parameter.Connector));
                    Check(ref violations, ConvaiActionGrammarSurface.Description,
                        ConvaiActionParameterDefinition.Normalize(parameter.Description));

                    if (parameter.Type != ConvaiActionParameterType.Choice || parameter.Choices == null)
                        continue;

                    for (int c = 0; c < parameter.Choices.Count; c++)
                    {
                        Check(ref violations, ConvaiActionGrammarSurface.ChoiceValue,
                            ConvaiActionParameterDefinition.Normalize(parameter.Choices[c]));
                    }
                }
            }

            CheckSlotNamesAreDistinct(ref violations, definition);

            return (IReadOnlyList<ConvaiActionGrammarViolation>)violations ??
                   Array.Empty<ConvaiActionGrammarViolation>();
        }

        /// <summary>
        ///     Two slots cannot share a name, because a value can only land in one of them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Reachable without doing anything odd: name a parameter <c>target</c>, give it a
        ///         type that cannot carry a target — <c>string</c>, say — and ask the action for a
        ///         target anyway. The implicit slot is added because no declared parameter can carry
        ///         one, and the action renders as <c>Say To {target: string} {target: reference}</c>.
        ///         The Convai Character is shown the same name twice with two different types, and
        ///         whichever it fills, only the first can be kept.
        ///     </para>
        ///     <para>
        ///         Checked over <see cref="SlotsOf" /> rather than over the declared parameters, so
        ///         it sees the implicit slot — which is the only way the collision arises.
        ///     </para>
        /// </remarks>
        private static void CheckSlotNamesAreDistinct(
            ref List<ConvaiActionGrammarViolation> violations, ConvaiActionDefinition definition)
        {
            IReadOnlyList<ConvaiActionParameterDefinition> slots = SlotsOf(definition);
            if (slots.Count < 2)
                return;

            HashSet<string> seen = null;
            for (int i = 0; i < slots.Count; i++)
            {
                string name = ConvaiActionParameterDefinition.Normalize(slots[i]?.Name);
                if (name.Length == 0)
                    continue;

                seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (seen.Add(name))
                    continue;

                violations ??= new List<ConvaiActionGrammarViolation>();
                violations.Add(new ConvaiActionGrammarViolation(
                    ConvaiActionGrammarSurface.ParameterName,
                    name,
                    name,
                    $"This action presents two slots called '{name}', so a value the Convai Character " +
                    $"sends for it can only reach one of them. If '{name}' is meant to be what the " +
                    "action acts on, set its type to Actor Reference and the duplicate goes away; " +
                    "otherwise rename it."));
            }
        }

        /// <summary>
        ///     Whether one piece of authored text is safe on its surface, without allocating a
        ///     report. Used by editor tooling that only needs to colour a field.
        /// </summary>
        internal static bool IsUsable(ConvaiActionGrammarSurface surface, string value) =>
            FindReservedToken(surface, ConvaiActionText.Normalize(value)) == null;

        /// <summary>
        ///     Reports the faults that only exist between definitions: two actions that render to
        ///     the same wire text.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="Validate" /> can only see one definition, and some ways of being
        ///         unreadable need two. The renderer joins an action's name to its first parameter's
        ///         connector with a single space, so <c>Give</c> with a connector of <c>to</c> and
        ///         <c>Give to</c> with no connector produce the identical string
        ///         <c>Give to {item: auto}</c>. Nothing downstream — not this SDK, and not the model,
        ///         which is shown the two as one repeated bullet — can then tell which of them a
        ///         command meant.
        ///     </para>
        ///     <para>
        ///         Only the genuine collision is reported. An action named <c>Walk</c> whose first
        ///         parameter has the connector <c>to</c> renders as <c>Walk to {…}</c>, so reading a
        ///         name back out of it gives <c>Walk to</c> rather than <c>Walk</c> — but that is a
        ///         perfectly ordinary way to author an action, and it is harmless because the
        ///         callers that map a wire string to a definition match the whole rendered string
        ///         first (see <c>ConvaiActionDefinition.BuildRenderedLookup</c>). Reporting it would
        ///         be a false alarm, and a validation rule that cries wolf is a validation rule
        ///         somebody turns off.
        ///     </para>
        /// </remarks>
        internal static IReadOnlyList<ConvaiActionGrammarViolation> ValidateCatalog(
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            if (definitions == null || definitions.Count < 2)
                return Array.Empty<ConvaiActionGrammarViolation>();

            List<ConvaiActionGrammarViolation> violations = null;
            Dictionary<string, string> renderedToName = null;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string rendered = definition?.ToActionConfigString();
                if (string.IsNullOrEmpty(rendered))
                    continue;

                string actionName = ConvaiActionDefinition.NormalizeActionName(definition.ActionName);
                renderedToName ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (renderedToName.TryGetValue(rendered, out string firstName))
                {
                    violations ??= new List<ConvaiActionGrammarViolation>();
                    violations.Add(new ConvaiActionGrammarViolation(
                        ConvaiActionGrammarSurface.RenderedAction,
                        rendered,
                        rendered,
                        $"Actions '{firstName}' and '{actionName}' are offered to the Convai Character as " +
                        $"the identical line \"{rendered}\", so a command for one of them cannot be told " +
                        "from a command for the other. Give them different names, or move the wording " +
                        "that makes them the same out of the connector."));
                    continue;
                }

                renderedToName[rendered] = actionName;
            }

            return (IReadOnlyList<ConvaiActionGrammarViolation>)violations ??
                   Array.Empty<ConvaiActionGrammarViolation>();
        }

        private static void Check(
            ref List<ConvaiActionGrammarViolation> violations,
            ConvaiActionGrammarSurface surface,
            string value)
        {
            string token = FindReservedToken(surface, value);
            if (token == null)
                return;

            violations ??= new List<ConvaiActionGrammarViolation>();
            violations.Add(new ConvaiActionGrammarViolation(surface, value, token, Explain(surface, value, token)));
        }

        /// <summary>
        ///     The reserved-token table, per surface. This is the whole rule.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Read it as "what would this text be mistaken for". An action name containing
        ///         <c>" - "</c> would be mistaken for a name plus a description; a choice value
        ///         containing <c>|</c> would be mistaken for two choices. Nothing is forbidden for
        ///         tidiness.
        ///     </para>
        ///     <para>
        ///         Allocation-free on the overwhelmingly common path where the text is fine, because
        ///         config validation runs on every inspector and window repaint. A string is only
        ///         produced once something is actually wrong, which is when somebody is about to
        ///         read it.
        ///     </para>
        /// </remarks>
        private static string FindReservedToken(ConvaiActionGrammarSurface surface, string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            switch (surface)
            {
                case ConvaiActionGrammarSurface.ActionName:
                case ConvaiActionGrammarSurface.Connector:
                    // Both are rendered bare, ahead of the description, so either delimiter would
                    // truncate what CanonicalNameOf reads back.
                    if (Has(value, DescriptionSeparator)) return DescriptionSeparator;
                    return FindSlotToken(value);

                case ConvaiActionGrammarSurface.ParameterName:
                    // Rendered inside a slot, before the type marker, so a colon reads as the marker.
                    if (Has(value, ':')) return ":";
                    return FindSlotToken(value);

                case ConvaiActionGrammarSurface.ChoiceValue:
                    if (Has(value, ChoiceDelimiter)) return ChoiceDelimiterText;
                    if (Has(value, ChoiceOpen)) return ChoiceOpenText;
                    if (Has(value, ChoiceClose)) return ChoiceCloseText;
                    return FindSlotToken(value);

                case ConvaiActionGrammarSurface.Description:
                    // Already past the separator, so " - " is harmless here. Braces are not: they
                    // would be read as a slot by anything scanning the rendered string.
                    return FindSlotToken(value);

                default:
                    return null;
            }
        }

        private static string FindSlotToken(string value)
        {
            if (Has(value, SlotOpen)) return SlotOpenText;
            if (Has(value, SlotClose)) return SlotCloseText;
            return null;
        }

        private static bool Has(string value, char token) => value.IndexOf(token) >= 0;

        // Ordinal rather than the culture-sensitive default: the separator is a fixed byte sequence
        // in a machine format, not text being compared for a reader.
        private static bool Has(string value, string token) =>
            value.IndexOf(token, StringComparison.Ordinal) >= 0;

        // Derived from the tokens above rather than spelled again, so they cannot drift from them —
        // which is the exact failure this whole type exists to remove. Computed once at type
        // initialization, so reporting a violation still allocates nothing.
        private static readonly string SlotOpenText = SlotOpen.ToString();
        private static readonly string SlotCloseText = SlotClose.ToString();
        private static readonly string ChoiceOpenText = ChoiceOpen.ToString();
        private static readonly string ChoiceCloseText = ChoiceClose.ToString();
        private static readonly string ChoiceDelimiterText = ChoiceDelimiter.ToString();

        /// <summary>
        ///     Says what is wrong and what to do about it, in the words a user of the SDK would use.
        /// </summary>
        private static string Explain(ConvaiActionGrammarSurface surface, string value, string token)
        {
            string quotedToken = token == DescriptionSeparator ? "' - ' (a dash with spaces around it)" : $"'{token}'";

            return surface switch
            {
                ConvaiActionGrammarSurface.ActionName =>
                    $"Action name '{value}' contains {quotedToken}, which Convai reads as the end of the " +
                    "name. The character would be offered an action it can never be asked to perform. " +
                    "Rename it — 'Sit On Chair' rather than 'Sit - Chair'.",

                ConvaiActionGrammarSurface.ParameterName =>
                    $"Parameter name '{value}' contains {quotedToken}, which is punctuation Convai uses to " +
                    "mark where a parameter's value begins. Values sent for it would be read as part of " +
                    "the name. Rename the parameter without it.",

                ConvaiActionGrammarSurface.ChoiceValue =>
                    $"Choice '{value}' contains {quotedToken}, which separates one choice from the next. " +
                    "It would arrive as two choices, and neither would match. Reword it — a plain dash, " +
                    "as in 'path-blocked', is fine.",

                ConvaiActionGrammarSurface.Connector =>
                    $"Connector '{value}' contains {quotedToken}, which Convai reads as punctuation rather " +
                    "than as a word joining the parameter to what came before. Use a plain word.",

                _ =>
                    $"Description '{value}' contains {quotedToken}, which Convai reads as the start of a " +
                    "parameter. Remove the braces — the description is prose, not a template."
            };
        }

        // ── Wire text ─────────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Folds authored text to the printable ASCII the wire format is defined over: accents
        ///     are dropped to their base letter, typographic punctuation is replaced by the ASCII
        ///     punctuation that means the same thing, anything else outside ASCII becomes a space,
        ///     and runs of whitespace collapse.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Deterministic and documented rather than clever, because the result is what a
        ///         Convai Character is told the action is called. A definition built at runtime cannot
        ///         be validated in an inspector, so this fold is also the last line of defence for one
        ///         — it never rejects, it only normalizes.
        ///     </para>
        ///     <para>
        ///         <b>Why punctuation is translated rather than blanked.</b> Blanking everything
        ///         outside ASCII quietly rewrote what authors had written. A description reading
        ///         "it does not build anything — say Run The Assembly for that" reached the character
        ///         as "it does not build anything say Run The Assembly for that", which is a different
        ///         and worse sentence: the aside became part of the clause. Curly quotes and ellipses
        ///         did the same to prose written in any normal editor, and nothing anywhere said so —
        ///         the author saw their own text in the inspector and the character read something
        ///         else. Each of these characters has an exact ASCII equivalent, so the honest fold is
        ///         to use it. The blank-out stays for everything that genuinely has none.
        ///     </para>
        /// </remarks>
        internal static string FoldToWire(string value, ConvaiActionGrammarSurface surface)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // A dash is prose punctuation on the surfaces that carry prose, and a reserved token on
            // the two that are rendered bare ahead of the description. See TranslatesDashes.
            bool translateDashes = TranslatesDashes(surface);

            string normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                bool isAsciiPrintable = c >= 32 && c <= 126;
                if (!isAsciiPrintable &&
                    TryTranslatePunctuation(c, out string ascii) &&
                    (translateDashes || ascii != "-"))
                {
                    builder.Append(ascii);
                    previousWasSpace = false;
                    continue;
                }

                char output = isAsciiPrintable ? c : ' ';
                if (char.IsWhiteSpace(output))
                    output = ' ';

                if (output == ' ')
                {
                    if (previousWasSpace)
                        continue;

                    previousWasSpace = true;
                }
                else
                {
                    previousWasSpace = false;
                }

                builder.Append(output);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        ///     Whether a dash may be spelled as one on this surface, or has to go the way it always
        ///     did.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A translation must not invent a reserved token.</b> An action name and a
        ///         connector are rendered bare, ahead of the description, so <see cref="CanonicalNameOf" />
        ///         reads a name back by cutting at the first <c>" - "</c> — and
        ///         <see cref="Validate" /> keeps that unambiguous by refusing an authored name that
        ///         contains one. Validation reads what the author typed, so an author who writes
        ///         <c>Sit — Chair</c> passes: the em dash is not the reserved token. Spelling it as a
        ///         dash here would create the token after validation had already approved the name,
        ///         and the action would come back as <c>Sit</c>. Nothing would report it.
        ///     </para>
        ///     <para>
        ///         So the two surfaces that reserve the separator keep the old fold, which is exactly
        ///         what they had before and is consistent with what validation promises. Prose keeps
        ///         its punctuation, because prose is where the loss was doing damage and where
        ///         <c>" - "</c> is documented as harmless — it sits past the separator already.
        ///     </para>
        /// </remarks>
        private static bool TranslatesDashes(ConvaiActionGrammarSurface surface) =>
            surface != ConvaiActionGrammarSurface.ActionName &&
            surface != ConvaiActionGrammarSurface.Connector;

        /// <summary>
        ///     The ASCII that means the same thing as a typographic character, where there is one.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only characters whose ASCII equivalent is exact are listed. A dash is a dash and a
        ///         curly quote is a quote; there is no judgement to make and nothing is lost. Anything
        ///         that would need a decision — a currency symbol, an arrow, an emoji — is deliberately
        ///         absent, because guessing at those is how a fold starts changing meaning instead of
        ///         preserving it, and the blank-out is the honest answer there.
        ///     </para>
        ///     <para>
        ///         The dashes deserve their own note: a dash written as a separator arrives as a dash,
        ///         so an aside stays an aside. The wire's own separator handling already reads a plain
        ///         hyphen — see the parser's separator strip — so this hands it the character it
        ///         understands rather than a gap where punctuation used to be.
        ///     </para>
        /// </remarks>
        private static bool TryTranslatePunctuation(char value, out string ascii)
        {
            switch (value)
            {
                // Hyphens and dashes: figure dash through horizontal bar.
                case '‐':
                case '‑':
                case '‒':
                case '–':
                case '—':
                case '―':
                case '−':
                    ascii = "-";
                    return true;

                // Single quotes and apostrophes.
                case '‘':
                case '’':
                case '‚':
                case '‛':
                case '′':
                    ascii = "'";
                    return true;

                // Double quotes.
                case '“':
                case '”':
                case '„':
                case '‟':
                case '″':
                    ascii = "\"";
                    return true;

                case '…':
                    ascii = "...";
                    return true;

                default:
                    ascii = null;
                    return false;
            }
        }
    }
}
