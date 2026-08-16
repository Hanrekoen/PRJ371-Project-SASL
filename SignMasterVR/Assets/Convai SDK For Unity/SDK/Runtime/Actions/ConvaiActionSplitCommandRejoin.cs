using System;
using System.Collections.Generic;
using Convai.Shared.Actions;
using Convai.Shared.Types;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Puts back together a command a Convai Character wrote as several entries in the actions
    ///     list instead of one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>What this is fixing.</b> Asked to turn the gallery lights on, a character sent
    ///         <c>["Light The Room", "on", "Gallery Lights"]</c> — the action, the value for its
    ///         Choice, and the target, as three list entries. Every one of them then failed on its
    ///         own terms: the action for naming nothing to act on, and the other two for not being
    ///         actions. Measured live on 2026-08-12, where it accounted for every failure in the
    ///         run.
    ///     </para>
    ///     <para>
    ///         <b>Why the client can fix it and nothing upstream can.</b> The list arrives whole,
    ///         and only this side knows what the action's slots are and what this character can
    ///         actually be asked to act on. The backend sees three strings and has no grounds to
    ///         join them.
    ///     </para>
    ///     <para>
    ///         <b>Three structural conditions, no guessing.</b> An entry is absorbed only when it
    ///         names no action of its own, the entry before it arrived carrying nothing at all, and
    ///         its text is admissible for one of that action's still-empty slots — an authored
    ///         Choice value, or the name of something this character has. Anything else is left to
    ///         be dropped and explained exactly as before.
    ///     </para>
    ///     <para>
    ///         <b>The join is written in the wire grammar</b> rather than assembled by hand:
    ///         <c>Light The Room</c> plus <c>on</c> plus <c>Gallery Lights</c> becomes
    ///         <c>Light The Room {mode: on} {target: Gallery Lights}</c>, which is a shape the
    ///         reader already understands. So there is no second parser to keep in step with the
    ///         first — the rejoin decides only <em>which slot</em>, and the ordinary reading does
    ///         the rest.
    ///     </para>
    ///     <para>
    ///         <b>Why the base must have arrived empty.</b> A command that already carries a value
    ///         is one the character filled in, and appending a slot to it could fill the same slot
    ///         twice or contradict what was sent. The split shape is precisely the one where the
    ///         first entry is a bare action name — which is why the condition costs nothing and
    ///         removes the whole class of double-filling.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionSplitCommandRejoin
    {
        /// <summary>
        ///     Returns the batch with any entries that belong to an earlier command folded into it.
        /// </summary>
        /// <param name="commands">The batch exactly as it came off the wire.</param>
        /// <param name="vocabulary">What this character can act on. Required — see R-3.</param>
        /// <param name="definitions">The actions this character has.</param>
        /// <param name="rejoinedCount">How many entries were folded away.</param>
        /// <remarks>
        ///     Returns the original list untouched when nothing was folded, which is the ordinary
        ///     case and costs one pass and no allocation.
        /// </remarks>
        internal static IReadOnlyList<ConvaiActionCommand> Apply(
            IReadOnlyList<ConvaiActionCommand> commands,
            ConvaiActionConfig vocabulary,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            out int rejoinedCount)
        {
            rejoinedCount = 0;
            if (commands == null || commands.Count < 2 || definitions == null)
                return commands;

            List<ConvaiActionCommand> rejoined = null;
            ConvaiActionCommand basis = null;
            ConvaiActionDefinition basisDefinition = null;
            List<string> filledByAbsorption = null;
            int basisSlot = -1;

            for (int i = 0; i < commands.Count; i++)
            {
                ConvaiActionCommand command = commands[i];
                string text = ConvaiActionWireText.Clean(command?.Name, vocabulary);
                ConvaiActionDefinition definition = ConvaiActionResponseParser.FindTemplate(text, definitions);

                if (definition == null && basis != null &&
                    TryPlaceInSlot(basisDefinition, text, vocabulary, filledByAbsorption, out string slotName))
                {
                    // The list is copied and the command cloned at the first absorption, never
                    // before: this runs on every batch, almost none of which need it, and a caller
                    // who handed their own commands to ConvaiActionDispatcher must get them back
                    // unmodified.
                    rejoined ??= Copy(commands, i);
                    if (basisSlot < 0)
                    {
                        basisSlot = rejoined.LastIndexOf(basis);
                        rejoined[basisSlot] = basis = basis.Clone();
                    }

                    basis.Name = basis.Name +
                                 " " + ConvaiActionWireGrammar.SlotOpen +
                                 slotName + ConvaiActionWireGrammar.TypeMarker +
                                 text + ConvaiActionWireGrammar.SlotClose;

                    filledByAbsorption ??= new List<string>();
                    filledByAbsorption.Add(slotName);
                    rejoinedCount++;
                    continue;
                }

                rejoined?.Add(command);

                // Only an action that arrived with nothing in it can take entries after it.
                basis = ArrivedEmpty(command, text, definition) ? command : null;
                basisDefinition = basis == null ? null : definition;
                basisSlot = -1;
                filledByAbsorption?.Clear();
            }

            return rejoined ?? commands;
        }

        /// <summary>
        ///     Whether this entry is a bare action name — no target, and nothing trailing the name.
        /// </summary>
        private static bool ArrivedEmpty(
            ConvaiActionCommand command, string text, ConvaiActionDefinition definition)
        {
            if (definition == null || !string.IsNullOrWhiteSpace(command?.Target))
                return false;

            return string.Equals(
                text,
                ConvaiActionDefinition.NormalizeActionName(definition.ActionName),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Names the first still-empty slot of the previous command that this text could be the
        ///     value for.
        /// </summary>
        /// <remarks>
        ///     In template order, so a character that sent its values in the order it was shown them
        ///     gets them back in that order.
        /// </remarks>
        private static bool TryPlaceInSlot(
            ConvaiActionDefinition definition,
            string text,
            ConvaiActionConfig vocabulary,
            List<string> filled,
            out string slotName)
        {
            slotName = null;
            if (string.IsNullOrEmpty(text))
                return false;

            IReadOnlyList<ConvaiActionParameterDefinition> slots = ConvaiActionWireGrammar.SlotsOf(definition);
            for (int i = 0; i < slots.Count; i++)
            {
                ConvaiActionParameterDefinition slot = slots[i];
                string name = ConvaiActionParameterDefinition.Normalize(slot?.Name);
                if (name.Length == 0 || AlreadyFilled(filled, name) || !Fits(slot, text, vocabulary))
                    continue;

                slotName = name;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether this text could be the value the character meant for this slot.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only two kinds of slot can answer that. A Choice has a written-down set of
        ///         values, so a match is a fact. A slot that carries a target can be measured
        ///         against the vocabulary, which is the same question R-3 asks everywhere else.
        ///     </para>
        ///     <para>
        ///         Number, String and Bool slots are deliberately not offered a stray entry. A bare
        ///         <c>20</c> beside a <c>Compare Reading</c> could be the low end, the high end, or
        ///         a sentence fragment, and nothing in the batch says which — so it is left to be
        ///         dropped and explained rather than placed by position and silently believed.
        ///     </para>
        /// </remarks>
        private static bool Fits(
            ConvaiActionParameterDefinition slot, string text, ConvaiActionConfig vocabulary)
        {
            if (slot.Type == ConvaiActionParameterType.Choice)
            {
                IReadOnlyList<string> choices = slot.Choices;
                for (int i = 0; choices != null && i < choices.Count; i++)
                {
                    if (string.Equals(
                            ConvaiActionParameterDefinition.Normalize(choices[i]),
                            text,
                            StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            return slot.Type is ConvaiActionParameterType.Reference or ConvaiActionParameterType.Auto &&
                   ConvaiActionWireText.NamesSomething(vocabulary, text);
        }

        private static bool AlreadyFilled(List<string> filled, string name)
        {
            for (int i = 0; filled != null && i < filled.Count; i++)
            {
                if (string.Equals(filled[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static List<ConvaiActionCommand> Copy(IReadOnlyList<ConvaiActionCommand> commands, int upTo)
        {
            var copy = new List<ConvaiActionCommand>(commands.Count);
            for (int i = 0; i < upTo; i++)
                copy.Add(commands[i]);

            return copy;
        }
    }
}
