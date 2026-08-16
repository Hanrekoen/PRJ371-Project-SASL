namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns the character's authored upper-body co-speech
    ///     overlay (Body Animation's talk layer) and reports how much of that overlay is
    ///     currently visible, so a peer conversational-behavior module (Body Language) can
    ///     negotiate shared use of the upper body instead of ducking away from it entirely.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="UpperBodyOccupancy01" /> is deliberately the LIVE, energy-scaled
    ///         overlay weight — it dips toward zero in speech pauses even while the character
    ///         keeps talking, and those dips are exactly the windows a peer module may use for
    ///         its own upper-body motion (semantic gesture clips, procedural accents). This is a
    ///         continuous negotiation signal, not a binary busy/free flag.
    ///     </para>
    ///     <para>
    ///         <see cref="HardSuppression" /> is the subset of suppression no budget negotiation
    ///         overrides — a full-body action, locomotion, a turn-in-place, or full-body talk
    ///         coverage. A caller that finds a budget registered should use
    ///         <see cref="HardSuppression" /> in place of a raw
    ///         <see cref="IConversationalGesturePerformer.CurrentSuppression" /> read: the
    ///         upper-body-talk-alone case that the older suppression reports is deliberately
    ///         <see cref="GestureSuppression.None" /> here, because <see cref="UpperBodyOccupancy01" />
    ///         already covers that case at finer granularity.
    ///     </para>
    ///     <para>
    ///         <see cref="ReportConversationalIntensity" /> lets a peer module (Body Language)
    ///         report its own emotion-derived conversational intensity so the budget owner MAY
    ///         scale its authored overlay to match (e.g. a subdued emotional state also subdues
    ///         the talk-clip amplitude). The contract is a standing report, not a one-shot event:
    ///         a caller re-reports every cognition tick, and reports <c>1f</c> (neutral) once when
    ///         it disables — the budget owner never infers absence from silence and always holds
    ///         the last reported value.
    ///     </para>
    ///     <para>
    ///         When no budget is registered on a character's <c>EmbodimentContext</c>, callers
    ///         degrade to the older <see cref="IConversationalGesturePerformer" />-only contract
    ///         (binary suppression, no occupancy negotiation, no intensity report) — the module
    ///         this contract lives on must work identically whether or not a peer implements it.
    ///     </para>
    /// </remarks>
    internal interface IConversationalMotionBudget
    {
        /// <summary>0 = upper body free for a peer module to use … 1 = fully owned by authored content (talk overlay at full weight).</summary>
        float UpperBodyOccupancy01 { get; }

        /// <summary>Hard suppression that no budget negotiation overrides (full-body action/locomotion/full-body talk).</summary>
        GestureSuppression HardSuppression { get; }

        /// <summary>
        ///     Peer modules report their desired conversational motion intensity (1 = neutral); the
        ///     budget owner MAY use it to scale its own authored overlay. Values are clamped by the owner.
        /// </summary>
        void ReportConversationalIntensity(float intensityScale);
    }
}
