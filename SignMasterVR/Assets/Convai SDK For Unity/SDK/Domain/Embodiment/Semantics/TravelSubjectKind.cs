namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     What a journey is <em>about</em> — the thing a traveling character periodically checks on
    ///     while it watches where it is going.
    /// </summary>
    /// <remarks>
    ///     Deliberately internal. Nothing user-facing needs to name it: a caller declares a subject
    ///     by handing over a point or a transform, and the kind is inferred. Keeping it out of the
    ///     public surface avoids a compatibility promise nobody asked for.
    /// </remarks>
    internal enum TravelSubjectKind
    {
        /// <summary>
        ///     No subject. The character simply watches the path — the correct default for a move
        ///     with no declared meaning, such as a scene script calling into locomotion directly.
        /// </summary>
        None = 0,

        /// <summary>A place the character is going to. Check-ins densify as it nears.</summary>
        Destination = 1,

        /// <summary>
        ///     Someone the character is traveling with or after. Check-ins are more frequent than
        ///     for a destination — walking with a person and never looking at them reads as escort
        ///     duty rather than company.
        /// </summary>
        Companion = 2
    }
}
