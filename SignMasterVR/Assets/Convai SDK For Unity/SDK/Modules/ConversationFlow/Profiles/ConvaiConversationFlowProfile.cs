using Convai.Modules.ConversationFlow.Core;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.ConversationFlow.Profiles
{
    /// <summary>
    ///     ScriptableObject authoring of the conversation-flow state machine's timing
    ///     parameters. One asset per behavior preset.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Timings are exposed as a compact set of named values with sensible ranges.
    ///         Animators who want finer control can author multiple profiles (e.g. "Elder
    ///         NPC" with slower thinking, "Child NPC" with quicker reactions).
    ///     </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ConvaiConversationFlowProfile",
        menuName = "Convai/Embodiment/Conversation Flow Profile",
        order = 110)]
    public sealed class ConvaiConversationFlowProfile : ScriptableObject
    {
        [SerializeField, Range(0f, 2f)]
        [Tooltip("Duration of the linear crossfade between two states.")]
        [ConvaiInspectorSection("Transition")]
        private float transitionDuration = 0.25f;

        [SerializeField, Range(0f, 3f)]
        [Tooltip("Minimum duration the character remains in Thinking after the player commits a turn.")]
        [ConvaiInspectorSection("Thinking")]
        private float thinkingMinHold = 0.25f;

        [SerializeField, Range(0.5f, 10f)]
        [Tooltip("Maximum duration the character remains in Thinking before falling back to Attending.")]
        [ConvaiInspectorSection("Thinking")]
        private float thinkingMaxHold = 2.5f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Grace period after the player stops speaking without committing a turn.")]
        [ConvaiInspectorSection("Attending")]
        private float attendingGracePeriod = 0.3f;

        [SerializeField, Range(0f, 3f)]
        [Tooltip("Duration of the post-turn settle beat before returning to Idle.")]
        [ConvaiInspectorSection("Settling")]
        private float settlingDuration = 0.6f;

        [SerializeField, Range(0f, 120f)]
        [Tooltip("Seconds of inactivity before the character cools from Attending back to Idle (e.g. ~60 for a full minute).")]
        [ConvaiInspectorSection("Idle Return")]
        private float idleReturnDelay = 60f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Duration the character freezes after being interrupted.")]
        [ConvaiInspectorSection("Interruption")]
        private float interruptedFreezeDuration = 0.25f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("Base energy level emitted during Speaking (controls body language intensity).")]
        [ConvaiInspectorSection("Energy")]
        private float speakingBaseEnergy = 0.6f;

        /// <summary>Builds a timings struct from the authored values.</summary>
        public ConversationFlowTimings ToTimings() => new(
            transitionDuration,
            thinkingMinHold,
            thinkingMaxHold,
            attendingGracePeriod,
            settlingDuration,
            idleReturnDelay,
            interruptedFreezeDuration,
            speakingBaseEnergy);

        /// <summary>
        ///     Keeps the thinking window authorable in only the ways it actually behaves.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two holds have independent <c>[Range]</c>s — minimum 0–3s, maximum 0.5–10s —
        ///         so the Inspector happily accepts a 3-second minimum against a 0.5-second maximum.
        ///         <see cref="ConversationFlowTimings" /> already resolves that by raising the
        ///         maximum to the minimum, which is the sane reading, but it did so silently: the
        ///         asset went on displaying 0.5 while every character built from it waited 3. The
        ///         one surface an author was looking at was the one telling them something untrue.
        ///     </para>
        ///     <para>
        ///         Repairing it here rather than only clamping at read time means the asset and the
        ///         runtime agree, and the correction is visible the moment it is made.
        ///     </para>
        /// </remarks>
        private void OnValidate()
        {
            if (thinkingMaxHold < thinkingMinHold) thinkingMaxHold = thinkingMinHold;
        }

        /// <summary>Creates a runtime-default profile instance. Used by drivers with no asset assigned.</summary>
        public static ConvaiConversationFlowProfile CreateDefault()
        {
            ConvaiConversationFlowProfile instance = CreateInstance<ConvaiConversationFlowProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }
    }
}
