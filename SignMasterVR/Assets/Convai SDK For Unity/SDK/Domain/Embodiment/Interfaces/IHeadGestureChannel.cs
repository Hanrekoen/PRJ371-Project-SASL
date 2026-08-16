namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that produces scripted/co-speech head-gesture programs
    ///     (nod/shake/tilt) as an additive offset a head-owning consumer composes into its own
    ///     solve. Body Language implements this; the gaze module (or any custom head solver)
    ///     consumes it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A head-owning consumer claims the channel with <see cref="RegisterConsumer" />
    ///         on enable and releases it with <see cref="UnregisterConsumer" /> on disable — the
    ///         same claim/release lifecycle every other <c>EmbodimentContext</c> slot uses.
    ///         While at least one consumer is registered, the producer only <em>publishes</em>
    ///         the offset through <see cref="TryGetOffset" />; it does not write any bones
    ///         itself, since the registered consumer owns the head/neck bones end-to-end and is
    ///         responsible for composing this offset into its own post-spring gesture input
    ///         (alongside whatever internal gestures it already produces, e.g. listening
    ///         backchannel nods — the consumer arbitrates, the producer does not know about
    ///         that logic).
    ///     </para>
    ///     <para>
    ///         When <b>zero</b> consumers are registered (no head module present, or it is
    ///         disabled), the producer self-actuates the offset directly and conservatively —
    ///         see the producer's own degradation notes — so a scripted gesture still reads
    ///         even on a character with no gaze system.
    ///     </para>
    ///     <para>
    ///         Multiple consumers may register (the slot does not enforce single-ownership by
    ///         itself); implementations should treat this as a rare/advanced setup and document
    ///         their own arbitration if they allow it.
    ///     </para>
    /// </remarks>
    internal interface IHeadGestureChannel
    {
        /// <summary>
        ///     Claims the channel. Call once, typically on enable. Safe to call more than once
        ///     with the same <paramref name="consumer" /> reference (idempotent, never throws).
        /// </summary>
        void RegisterConsumer(object consumer);

        /// <summary>
        ///     Releases a previously registered consumer. Call once, typically on disable. Safe
        ///     to call with a consumer that was never registered (no-op, never throws).
        /// </summary>
        void UnregisterConsumer(object consumer);

        /// <summary>
        ///     Returns the current additive head-gesture offset. <c>false</c> means no gesture
        ///     program is active right now — the out value is <see cref="HeadGestureOffset.None" />
        ///     and the caller should treat the head as at rest for this channel.
        /// </summary>
        bool TryGetOffset(out HeadGestureOffset offset);
    }
}
