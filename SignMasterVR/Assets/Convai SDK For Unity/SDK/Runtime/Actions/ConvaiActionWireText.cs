using System;
using System.Collections.Generic;
using Convai.Shared.Actions;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Cleans up the decoration a language model puts around the values it sends.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only ever applied while reading the wire. Every repair here is a correction to how a
    ///         model writes, not to what an author typed, and applying them anywhere else quietly
    ///         rewrites authored names — a target legitimately called <c>"Q"</c> or <c>- Special</c>
    ///         stops being callable by its own name.
    ///     </para>
    ///     <para>
    ///         <b>The rule that bounds all of them (R-3).</b> Each repair is a guess that some
    ///         punctuation is decoration, and each one can destroy a real name. So before every repair
    ///         the text is offered to the character's own vocabulary: if it already names an object or
    ///         a character this action config offers, it is not decoration and nothing is removed.
    ///         That is what turns an open-ended list of repairs into a bounded one — a repair added
    ///         later, however aggressive, still cannot take a name away from something that exists.
    ///     </para>
    ///     <para>
    ///         <b>The rule is carried by <see cref="Reading" />, not by remembering it.</b> Every
    ///         repair below is an instance method on that cursor, and a cursor cannot be constructed
    ///         without a vocabulary. The rule was missed three times in one session while it was a
    ///         convention — in the separator strip, in the brace-slot strip, and in the loop that
    ///         drives them, where the first fix stated it a second time instead of once. It was missed
    ///         because a repair was reachable with nothing but a string in hand. Now there is nowhere
    ///         to put one where the vocabulary is not already there, and
    ///         <c>ConvaiActionRepairBoundingGuardTests</c> fails the build if a repair appears outside
    ///         the cursor.
    ///     </para>
    ///     <para>
    ///         <b>The separator.</b> Actions are presented to the model as <c>Name - description</c>,
    ///         and models complete the pattern they are shown: asked to walk somewhere, one answers
    ///         <c>Walk To - The Gallery</c>. Stripping the action name off the front leaves the
    ///         separator glued to the target, which then matches nothing. Only a separator followed by
    ///         whitespace is removed, which is what makes it safe — <c>X-Ray</c> and <c>Bay-2</c> are
    ///         never touched, because only the first character is ever considered.
    ///     </para>
    ///     <para>
    ///         <b>The quotes.</b> Backends quote values and models quote them again, so a Choice
    ///         authored as <c>follow|stop</c> arrives as <c>'follow'</c>, matches no authored choice,
    ///         and falls back to the default in silence. Only a matching pair wrapping the whole value
    ///         is removed, so an apostrophe inside a name and a one-sided quote both survive intact.
    ///     </para>
    ///     <para>
    ///         <b>The template slot.</b> Shown <c>{target: reference}</c>, a model fills the slot in
    ///         place and answers <c>{target: "The Rooms"}</c>. The braces are always syntax; the label
    ///         inside them usually is. Unwrapping and dropping the label are two steps precisely so
    ///         the vocabulary gets a say between them.
    ///     </para>
    ///     <para>
    ///         This runs on every name, target and parameter of every command, and almost none of
    ///         them carry decoration. A cheap look at the ends of the text answers that, and the
    ///         vocabulary is only consulted once something might actually be removed.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionWireText
    {
        /// <summary>
        ///     Trims and repairs, but never rewrites text that already names something this
        ///     character has.
        /// </summary>
        /// <param name="value">Raw text as the Convai Character sent it.</param>
        /// <param name="vocabulary">
        ///     What this character can act on. Consulted before any repair is kept; pass the
        ///     resolution view of the action config wherever one exists.
        /// </param>
        /// <remarks>
        ///     The lookup costs nothing on the ordinary path: <see cref="Reading.CouldRepair" />
        ///     answers from the two ends of the text, and the vocabulary is only reached once a
        ///     repair might actually apply.
        /// </remarks>
        internal static string Clean(string value, ConvaiActionConfig vocabulary)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var reading = new Reading(value, vocabulary);
            if (!reading.CouldRepair)
                return reading.Text;

            // The rule, stated once, as the condition of the loop: before every repair the text is
            // offered to the vocabulary, and if it already names something this character has, it is
            // not decoration and nothing more is removed. Each repair can expose the next — a model
            // emits '- {target: "The Gallery"}' as readily as any one of them alone — so they
            // alternate, bounded by the text shrinking on every pass.
            while (!reading.NamesSomethingKnown && reading.TryRepair())
            {
            }

            return reading.Text;
        }

        /// <summary>
        ///     Cleans text in a context that has no character, and therefore no vocabulary to check
        ///     against.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Named the long way round deliberately. This is the unbounded form of
        ///         <see cref="Clean(string, ConvaiActionConfig)" /> — every repair is applied on the
        ///         strength of the guess alone, so a target really called <c>- Special</c> or
        ///         <c>{Annex}</c> loses its name here. That is acceptable only where there is no
        ///         character to ask, and a call site that uses it is saying so.
        ///     </para>
        ///     <para>
        ///         It replaced a plain <c>Clean(value)</c> overload. An overload is the wrong shape
        ///         for this: omitting an argument reads as brevity, not as a decision, and R-3 was
        ///         switched off at a call site exactly that way.
        ///     </para>
        /// </remarks>
        internal static string CleanWithoutVocabulary(string value) => Clean(value, null);

        /// <summary>
        ///     Whether the text is, exactly, the name or an alternate name of something this action
        ///     config offers.
        /// </summary>
        /// <remarks>
        ///     Deliberately exact rather than going through the resolution ladder's fuzzier rungs.
        ///     This decides whether to leave punctuation alone, and a <c>contains</c> match would
        ///     start protecting text that merely resembles a real name — which would defeat the
        ///     repairs instead of bounding them.
        /// </remarks>
        internal static bool NamesSomething(ConvaiActionConfig vocabulary, string candidate)
        {
            if (vocabulary == null || string.IsNullOrEmpty(candidate))
                return false;

            IReadOnlyList<ConvaiActionObjectDefinition> objects = vocabulary.Objects;
            for (int i = 0; objects != null && i < objects.Count; i++)
            {
                ConvaiActionObjectDefinition entry = objects[i];
                if (entry != null && entry.Available &&
                    (Matches(entry.Name, candidate) || MatchesAlias(entry.Aliases, candidate)))
                    return true;
            }

            IReadOnlyList<ConvaiActionCharacterDefinition> characters = vocabulary.Characters;
            for (int i = 0; characters != null && i < characters.Count; i++)
            {
                ConvaiActionCharacterDefinition entry = characters[i];
                if (entry != null && entry.Available &&
                    (Matches(entry.Name, candidate) || MatchesAlias(entry.Aliases, candidate)))
                    return true;
            }

            return false;
        }

        /// <summary>Whether this character is any half of any quote pair this reader knows.</summary>
        /// <remarks>
        ///     Shared rather than spelled again by every caller that has to see past a quote a model
        ///     added. It was written a second time once, inside the response parser, and the two
        ///     lists immediately disagreed about the typographic pairs — so a value the cleaner could
        ///     unwrap was a value the splitter could not find a label in.
        /// </remarks>
        internal static bool IsQuote(char c) =>
            c is '\'' or '"' or '`' or '‘' or '’' or '“' or '”' or '«' or '»';

        private static bool Matches(string authored, string candidate) =>
            !string.IsNullOrWhiteSpace(authored) &&
            string.Equals(authored.Trim(), candidate, StringComparison.OrdinalIgnoreCase);

        private static bool MatchesAlias(IReadOnlyList<string> aliases, string candidate)
        {
            for (int i = 0; aliases != null && i < aliases.Count; i++)
            {
                if (Matches(aliases[i], candidate))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     One value being read off the wire, and the vocabulary it is allowed to be measured
        ///     against — the two things every repair needs, held together so a repair cannot be
        ///     written without both.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>This type is the enforcement of R-3.</b> Every repair is an instance method
        ///         here, so the vocabulary is in scope by construction rather than by a parameter
        ///         somebody has to remember to thread through. There is no constructor that omits it.
        ///     </para>
        ///     <para>
        ///         A <c>struct</c> holding two indices rather than a rewritten string: the repairs
        ///         only ever narrow the text, so they are index arithmetic, and this runs for every
        ///         name, target and parameter of every command.
        ///     </para>
        /// </remarks>
        private struct Reading
        {
            private readonly string _value;
            private readonly ConvaiActionConfig _vocabulary;
            private int _start;
            private int _end;
            private bool _unwrappedBraces;

            internal Reading(string value, ConvaiActionConfig vocabulary)
            {
                _value = value;
                _vocabulary = vocabulary;
                _start = 0;
                _end = value.Length - 1;
                _unwrappedBraces = false;
                TrimWhitespace();
            }

            /// <summary>The text as it currently stands.</summary>
            internal string Text =>
                _start > _end
                    ? string.Empty
                    : _start == 0 && _end == _value.Length - 1
                        ? _value
                        : _value.Substring(_start, _end - _start + 1);

            /// <summary>
            ///     Whether any repair could possibly apply — a cheap look at the ends of the text.
            /// </summary>
            /// <remarks>
            ///     This runs on every name, target and parameter of every command, and almost none of
            ///     them carry decoration. Without this gate the vocabulary would be walked for all of
            ///     them; with it, the cost is paid only where there is actually a decision to make.
            /// </remarks>
            internal bool CouldRepair
            {
                get
                {
                    if (_end - _start < 1)
                        return false;

                    return (IsSeparator(_value[_start]) && char.IsWhiteSpace(_value[_start + 1])) ||
                           IsPairedQuote(_value[_start], _value[_end]) ||
                           (_value[_start] == ConvaiActionWireGrammar.SlotOpen &&
                            _value[_end] == ConvaiActionWireGrammar.SlotClose);
                }
            }

            /// <summary>
            ///     Whether the text as it currently stands already names something this character
            ///     has, in which case it is not decoration and nothing more may be removed.
            /// </summary>
            internal bool NamesSomethingKnown => NamesSomething(_vocabulary, Text);

            /// <summary>
            ///     Applies the first repair that fits, and reports whether anything changed.
            /// </summary>
            /// <remarks>
            ///     The one place the repairs are listed. Adding one here is the only way to add one
            ///     at all, and anything added here is behind the caller's vocabulary check by
            ///     construction — which is the whole point of the type.
            /// </remarks>
            internal bool TryRepair() =>
                TryStripSeparator() ||
                TryStripQuotePair() ||
                TryUnwrapSlotBraces() ||
                (_unwrappedBraces && TryDropSlotLabel());

            /// <summary>
            ///     Removes one leading separator, but only when whitespace follows it — a name that
            ///     genuinely begins with punctuation keeps it.
            /// </summary>
            private bool TryStripSeparator()
            {
                if (_end - _start < 1 || !IsSeparator(_value[_start]) ||
                    !char.IsWhiteSpace(_value[_start + 1]))
                    return false;

                _start++;
                TrimWhitespace();
                return true;
            }

            /// <summary>Removes one pair of matching quotes wrapping the whole value.</summary>
            /// <remarks>
            ///     <para>
            ///         <b>One quoted value, not two sitting next to each other.</b> Shown a slot, a
            ///         model may answer with a whole JSON object — <c>{"gesture": "wave"}</c> — and
            ///         once the braces come off, what is left begins with a quote and ends with a
            ///         quote while being two separate strings. Taking those for a wrapping pair
            ///         splices the key onto the value and yields <c>gesture": "wave</c>, which names
            ///         nothing: measured live, and the reason a character stood still after saying it
            ///         would wave.
            ///     </para>
            ///     <para>
            ///         <b>What separates the two is where the opening quote closes</b>, not whether
            ///         the value contains another quote. Merely banning interior quotes reads
            ///         <c>''follow''</c> and <c>"It's fine"</c> as hazards too, and both are ordinary
            ///         wrapped values — the first was already a passing test when that shortcut was
            ///         tried. <see cref="WrapsWholeValue" /> asks the structural question instead.
            ///     </para>
            /// </remarks>
            private bool TryStripQuotePair()
            {
                if (!WrapsWholeValue(_start, _end))
                    return false;

                _start++;
                _end--;
                TrimWhitespace();
                return true;
            }

            /// <summary>
            ///     Whether the quotes at these two ends are one pair enclosing everything between
            ///     them, rather than the opener of one string and the closer of another.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         A quote wraps the whole value when the first character that could close it is
            ///         the last character — <c>"It's fine"</c> qualifies, because an apostrophe cannot
            ///         close a double quote, while <c>"gesture": "wave"</c> does not, because its
            ///         opener closes after <c>gesture</c> and a second string follows.
            ///     </para>
            ///     <para>
            ///         Closing early is not decisive on its own: <c>''follow''</c> closes at the
            ///         second character and is still one value, wrapped twice. So a pair that closes
            ///         early is accepted only if what it wraps is itself a wrapped value, which is the
            ///         loop — each turn peels one layer and asks the same question of what is left.
            ///     </para>
            ///     <para>
            ///         Asked before anything is removed, so this reads the ends without moving them.
            ///     </para>
            /// </remarks>
            private bool WrapsWholeValue(int start, int end)
            {
                while (end - start >= 1 && IsPairedQuote(_value[start], _value[end]))
                {
                    char closer = _value[end];
                    for (int i = start + 1; i <= end; i++)
                    {
                        if (_value[i] != closer)
                            continue;

                        if (i == end)
                            return true;

                        break;
                    }

                    start++;
                    end--;
                }

                return false;
            }

            /// <summary>
            ///     Removes one pair of braces wrapping the whole value.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         An action is presented as <c>Count In Group {target: reference} - …</c>, and a
            ///         model asked to use it answers <c>{target: "The Rooms"}</c> — filling the slot
            ///         in place rather than replacing it. The braces are template syntax and never
            ///         part of a name.
            ///     </para>
            ///     <para>
            ///         Only one slot spanning the whole value, not several in a row. A parameter blob
            ///         like <c>{Cube} {2.5} {yes}</c> also starts with a brace and ends with one, and
            ///         unwrapping that pair would splice the first value onto the last and destroy
            ///         every field between them.
            ///     </para>
            ///     <para>
            ///         Separate from <see cref="TryDropSlotLabel" /> deliberately: unwrapping is
            ///         certain and dropping the label is a guess, so the vocabulary gets a say in
            ///         between. A target really called <c>Bay2:North</c> is only reachable because
            ///         these are two steps.
            ///     </para>
            /// </remarks>
            private bool TryUnwrapSlotBraces()
            {
                // The slot characters come from the grammar that wrote them, so this cannot go on
                // stripping a shape the renderer has stopped producing.
                const char slotOpen = ConvaiActionWireGrammar.SlotOpen;
                const char slotClose = ConvaiActionWireGrammar.SlotClose;

                if (_end - _start < 1 || _value[_start] != slotOpen || _value[_end] != slotClose)
                    return false;

                for (int i = _start + 1; i < _end; i++)
                {
                    if (_value[i] == slotOpen || _value[i] == slotClose)
                        return false;
                }

                _start++;
                _end--;
                TrimWhitespace();
                _unwrappedBraces = true;
                return true;
            }

            /// <summary>
            ///     Drops a slot's own name from the front of the value it was filled with.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         Only after braces came off, because that is the only shape this was ever
            ///         observed in — a bare <c>target: East Hall</c> outside braces is a parameter
            ///         blob, and splitting it belongs to the reader that knows the action's slots.
            ///     </para>
            ///     <para>
            ///         The name is only dropped when it looks like one: a single unspaced word
            ///         followed by a colon. That keeps a value that merely contains a colon — a room
            ///         called <c>Bay 2: North</c> — intact, because its first word is followed by a
            ///         space rather than by the colon.
            ///     </para>
            ///     <para>
            ///         A room called <c>Bay2:North</c> has no such tell. Sent bare it is never at
            ///         risk, because nothing at the ends of it looks like decoration and the cheap
            ///         gate stops before any repair runs. Sent as <c>{Bay2:North}</c> it does reach
            ///         here, and then only the vocabulary saves it — which is why unwrapping and
            ///         dropping the label are two steps with a check in between.
            ///     </para>
            /// </remarks>
            private bool TryDropSlotLabel()
            {
                for (int i = _start; i <= _end; i++)
                {
                    if (_value[i] == ':')
                    {
                        if (i == _start)
                            return false;

                        _start = i + 1;
                        TrimWhitespace();
                        return true;
                    }

                    // A space before any colon means this is a value, not a slot name.
                    if (char.IsWhiteSpace(_value[i]))
                        return false;
                }

                return false;
            }

            private void TrimWhitespace()
            {
                while (_start <= _end && char.IsWhiteSpace(_value[_start])) _start++;
                while (_end > _start && char.IsWhiteSpace(_value[_end])) _end--;
            }

            /// <summary>Characters a model uses to separate a name from what follows it.</summary>
            private static bool IsSeparator(char c) =>
                c is '-' or '–' or '—' or ':' or '=' or '>';

            /// <summary>
            ///     Whether these two characters are the opening and closing halves of one quote pair.
            /// </summary>
            private static bool IsPairedQuote(char open, char close) =>
                (open == '\'' && close == '\'') ||
                (open == '"' && close == '"') ||
                (open == '`' && close == '`') ||
                (open == '‘' && close == '’') ||
                (open == '“' && close == '”') ||
                (open == '«' && close == '»');
        }
    }
}
