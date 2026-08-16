namespace Convai.Domain.Embodiment.Semantics
{
    // Stays public, deliberately: three separate modules put these four words in front of the user,
    // and an enum only one of them can see is an enum the other two will re-type as string literals.
    // That is exactly how they drifted apart once already.
    /// <summary>
    ///     The four starting temperaments a Convai character can be given. One shared vocabulary so
    ///     that picking <em>Warm</em> on the Emotion profile, the Body Animation config, and the Body
    ///     Language profile all mean the same character.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A demeanor is an <em>authoring</em> concept, not a runtime one: applying one writes
    ///         plain values into the asset you are editing and then forgets it happened. Nothing at
    ///         runtime reads this enum, and the three modules are not coupled to each other by it —
    ///         each one interprets the same word in its own terms (Emotion sets per-emotion gains,
    ///         Body Animation sets liveliness and calmness, Body Language scales posture and fidget
    ///         biases).
    ///     </para>
    ///     <para>
    ///         Use <see cref="CharacterDemeanors.DisplayName" /> for anything a user reads. The enum
    ///         member names and the display names are deliberately identical today; routing every
    ///         label through one call is what keeps them that way.
    ///     </para>
    /// </remarks>
    public enum CharacterDemeanor
    {
        /// <summary>Receptionist, clerk, guide. Calm and even.</summary>
        Composed = 0,

        /// <summary>The default when a character is deliberately given a personality. Approachable and readable.</summary>
        Warm = 1,

        /// <summary>Host, tour guide, streamer. Big, fast reactions.</summary>
        Energetic = 2,

        /// <summary>Guard, officiant. Barely shows anything.</summary>
        Reserved = 3
    }

    /// <summary>
    ///     The presentation order and display names for <see cref="CharacterDemeanor" /> — the single
    ///     source every module's demeanor picker reads.
    /// </summary>
    public static class CharacterDemeanors
    {
        private static readonly CharacterDemeanor[] OrderedValues =
        {
            CharacterDemeanor.Composed,
            CharacterDemeanor.Warm,
            CharacterDemeanor.Energetic,
            CharacterDemeanor.Reserved
        };

        /// <summary>Every demeanor, in the order every picker presents them.</summary>
        public static System.Collections.Generic.IReadOnlyList<CharacterDemeanor> Order { get; } =
            System.Array.AsReadOnly(OrderedValues);

        /// <summary>
        ///     The one spelling of a demeanor that a user ever sees. An unrecognised value resolves to
        ///     <see cref="CharacterDemeanor.Warm" />, matching the SDK-wide default personality.
        /// </summary>
        public static string DisplayName(CharacterDemeanor demeanor) => demeanor switch
        {
            CharacterDemeanor.Composed => "Composed",
            CharacterDemeanor.Energetic => "Energetic",
            CharacterDemeanor.Reserved => "Reserved",
            _ => "Warm"
        };
    }
}
