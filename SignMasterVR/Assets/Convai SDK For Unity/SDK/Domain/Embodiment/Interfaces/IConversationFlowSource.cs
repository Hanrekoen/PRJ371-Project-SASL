using System;
using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Authoritative view of the character's current dialogue-phase state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implemented by <c>Convai.Modules.ConversationFlow</c>. Downstream modules
    ///         (gaze, body language, emotion, facial animation) consume this interface and
    ///         MUST NOT compute their own dialogue-phase state machine: the source is the
    ///         single point of truth to keep every sub-system visually in sync.
    ///     </para>
    ///     <para>
    ///         The <see cref="Changed" /> event fires on the Unity main thread whenever
    ///         <see cref="Current" />'s <c>Primary</c> state changes. Blend-weight progress
    ///         within a transition does NOT raise the event; consumers poll <see cref="Current" />
    ///         during their tick to sample the current blend.
    ///     </para>
    /// </remarks>
    internal interface IConversationFlowSource
    {
        /// <summary>Latest reading sampled at the start of the current frame.</summary>
        DialogueStateReading Current { get; }

        /// <summary>
        ///     Raised on the main thread when <see cref="DialogueStateReading.Primary" />
        ///     transitions to a new state. Subscribers must not throw from the handler.
        /// </summary>
        event Action<DialogueStateReading> Changed;
    }
}
