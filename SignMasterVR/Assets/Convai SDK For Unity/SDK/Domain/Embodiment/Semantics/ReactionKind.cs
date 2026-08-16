namespace Convai.Domain.Embodiment.Semantics
{
    // Stays public, deliberately: this is data carried inside public reading structs
    // (EmotionStateFrame, EmotionDescriptor, BodyLanguageReading). Internalizing it would
    // force those readings internal too, which buys nothing — a customer reading a
    // character's emotion legitimately sees this shape.
    /// <summary>
    ///     Fire-and-forget bodily reaction kinds Body Language can request. See
    ///     <c>ConvaiBodyLanguageController.TriggerReaction</c>: <see cref="SurpriseFlinch" /> and
    ///     <see cref="AmusementBounce" /> drive the procedural reaction envelope system;
    ///     <see cref="CatchBreath" /> and <see cref="Sigh" /> instead route to the breathing
    ///     system's own one-shot breath events, reusing its existing envelopes rather than
    ///     duplicating them here.
    /// </summary>
    public enum ReactionKind
    {
        /// <summary>No reaction active.</summary>
        None = 0,

        /// <summary>A quick startle: the spine briefly straightens and the shoulders jump.</summary>
        SurpriseFlinch = 1,

        /// <summary>A light amused chest bounce.</summary>
        AmusementBounce = 2,

        /// <summary>A quick, sharp intake of breath — routes to the breathing system's catch-breath event.</summary>
        CatchBreath = 3,

        /// <summary>A long, deep, slow breath — routes to the breathing system's sigh event.</summary>
        Sigh = 4
    }
}
