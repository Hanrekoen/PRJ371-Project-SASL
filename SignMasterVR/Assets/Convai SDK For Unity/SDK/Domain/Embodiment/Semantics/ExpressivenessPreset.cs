namespace Convai.Domain.Embodiment.Semantics
{
    // Stays public, deliberately: it is a setting a user picks on the Body Language profile,
    // so it is part of the authoring surface, not internal plumbing.
    /// <summary>
    ///     Body Language's single expressiveness dial: one authored/runtime knob
    ///     that coherently scales how big, how frequent, and how varied the whole nonverbal
    ///     system reads — breath, posture, sway, stance, gesticulation, reactions, hand
    ///     micro-life, and head gestures all read off the SAME resolved 0..1 value (see
    ///     <c>ConvaiBodyLanguageProfile.ResolveExpressiveness</c> and
    ///     <c>ConvaiBodyLanguageController.Expressiveness</c>).
    /// </summary>
    public enum ExpressivenessPreset
    {
        /// <summary>
        ///     Minimal, understated motion — approximately the Body Language v1 look before this
        ///     dial existed: small amplitudes, slower cadences, optional/richness-gated behaviors
        ///     (shrugs, hand micro-life) mostly or fully absent.
        /// </summary>
        Subtle = 0,

        /// <summary>
        ///     The shipped default: clearly visible nonverbal behavior at a normal 2-meter
        ///     conversational camera distance, without reading as performative.
        /// </summary>
        Natural = 1,

        /// <summary>Larger, more frequent, more varied motion than <see cref="Natural" /> — an animated, lively character.</summary>
        Expressive = 2,

        /// <summary>Maximum amplitude/frequency/richness — a broad, theatrical performer.</summary>
        Theatrical = 3,

        /// <summary>
        ///     Uses the profile's own <c>CustomExpressiveness</c> scalar (0..1) instead of one of
        ///     the fixed anchor presets — full author control over the resolved dial value.
        /// </summary>
        Custom = 4
    }
}
