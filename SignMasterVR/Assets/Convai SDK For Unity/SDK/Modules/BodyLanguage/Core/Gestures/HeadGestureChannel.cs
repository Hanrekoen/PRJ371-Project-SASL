using Convai.Domain.Embodiment.Interfaces;

namespace Convai.Modules.BodyLanguage.Core.Gestures
{
    /// <summary>
    ///     The controller-owned <see cref="IHeadGestureChannel" /> implementation: tracks the
    ///     registered-consumer count and forwards <see cref="TryGetOffset" /> to a
    ///     <see cref="HeadGestureDirector" />. Kept as a small standalone object (rather than an
    ///     explicit interface implementation on <c>ConvaiBodyLanguageController</c>) so the
    ///     consumer-count bookkeeping is unit-testable without a MonoBehaviour lifecycle and so
    ///     the controller can query <see cref="ConsumerCount" /> directly to decide whether the
    ///     no-consumer fallback should self-actuate this frame.
    /// </summary>
    /// <remarks>
    ///     Main-thread only, like every other embodiment POCO in this stack — no locking.
    ///     Register/unregister are idempotent by design (double-register of the same consumer,
    ///     or unregister of a consumer that was never registered, are both no-ops) so a consumer
    ///     can never push the count negative or double-count itself.
    /// </remarks>
    internal sealed class HeadGestureChannel : IHeadGestureChannel
    {
        // Small fixed set is expected (typically exactly one: the gaze module). A List keeps
        // registration order stable for diagnostics without needing a bigger collection type.
        private readonly System.Collections.Generic.List<object> _consumers = new(2);

        private HeadGestureDirector _director;

        /// <summary>Number of currently registered consumers.</summary>
        public int ConsumerCount => _consumers.Count;

        /// <summary>
        ///     The director this channel reads <see cref="TryGetOffset" /> from. Assigned once
        ///     by the owning controller; a channel with no director assigned always reports no
        ///     active offset.
        /// </summary>
        public void BindDirector(HeadGestureDirector director) => _director = director;

        public void RegisterConsumer(object consumer)
        {
            if (consumer == null) return;
            if (_consumers.Contains(consumer)) return;
            _consumers.Add(consumer);
        }

        public void UnregisterConsumer(object consumer)
        {
            if (consumer == null) return;
            _consumers.Remove(consumer);
        }

        public bool TryGetOffset(out HeadGestureOffset offset)
        {
            if (_director == null || !_director.IsPlaying)
            {
                offset = HeadGestureOffset.None;
                return false;
            }

            offset = _director.Current;
            return offset.Weight > 0f;
        }

        /// <summary>
        ///     Mirrors <see cref="TryGetOffset" /> for <see cref="HeadGestureDirector.CurrentNeckLead" />
        ///     (neck-lead sequencing) — kept as a pass-through so callers (the
        ///     controller's fallback actuation) keep talking to the channel, not the director,
        ///     exactly like every other read on this type.
        /// </summary>
        public bool TryGetNeckLeadOffset(out HeadGestureOffset offset)
        {
            if (_director == null || !_director.IsPlaying)
            {
                offset = HeadGestureOffset.None;
                return false;
            }

            offset = _director.CurrentNeckLead;
            return offset.Weight > 0f;
        }
    }
}
