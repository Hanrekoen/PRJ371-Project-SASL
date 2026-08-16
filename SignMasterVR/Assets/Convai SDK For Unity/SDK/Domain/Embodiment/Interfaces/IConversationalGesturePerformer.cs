using System;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns semantic hand/body gesture clips (e.g. wave, nod,
    ///     shake-head) and reports how much of the skeleton is currently busy. Body Animation
    ///     implements this; the body language module (or any other conversational-behavior
    ///     system) consumes it to fire gesture cues and to gate its own procedural output.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Gesture cues are always explicit — this contract never infers a gesture from
    ///         speech energy or timing on its own. Callers decide when a <see cref="GestureCue" />
    ///         fires (a scripted call, a backend action, an affirm/negate reaction beat, …); the
    ///         performer only resolves the cue to content and plays it, respecting whatever
    ///         interrupt/suppression policy the underlying animation stack already enforces.
    ///     </para>
    ///     <para>
    ///         <see cref="CurrentSuppression" /> lets a consumer avoid fighting the performer's
    ///         owner for the same bones: <see cref="GestureSuppression.FullBody" /> means the
    ///         whole skeleton is currently owned elsewhere (a full-body action, a turn-in-place,
    ///         or full-body talk coverage) and procedural posture/breath should fade out;
    ///         <see cref="GestureSuppression.UpperBody" /> means clips are off but posture/breath
    ///         can continue at reduced weight.
    ///     </para>
    ///     <para>
    ///         When no performer is registered on a character's <c>EmbodimentContext</c>, callers
    ///         should treat gesture cues as always refused and suppression as
    ///         <see cref="GestureSuppression.None" /> — procedural channels carry the system.
    ///     </para>
    /// </remarks>
    internal interface IConversationalGesturePerformer
    {
        /// <summary>How much of the skeleton is currently unavailable to procedural systems.</summary>
        GestureSuppression CurrentSuppression { get; }

        /// <summary>
        ///     Attempts to perform the given gesture cue now. Returns <c>false</c> when the cue
        ///     is refused: <see cref="GestureCueKind.None" />, no content is tagged for this cue
        ///     kind, suppression makes clips ineligible, or the performer's underlying playback
        ///     is busy with a non-interruptible entry. Never throws.
        /// </summary>
        bool TryPerform(in GestureCue cue);

        /// <summary>
        ///     Raised when a previously accepted <see cref="TryPerform" /> call finishes, with
        ///     the original cue and the terminal <see cref="GesturePerformanceResult" />.
        /// </summary>
        event Action<GestureCue, GesturePerformanceResult> Completed;
    }
}
