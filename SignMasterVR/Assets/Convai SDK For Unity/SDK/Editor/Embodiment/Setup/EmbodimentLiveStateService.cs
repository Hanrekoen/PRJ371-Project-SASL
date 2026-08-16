using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Editor.Embodiment.Setup
{
    /// <summary>One emotion label and how strongly the character is feeling it.</summary>
    internal readonly struct EmbodimentEmotionScore
    {
        internal EmbodimentEmotionScore(string label, float score)
        {
            Label = label;
            Score = score;
        }

        internal string Label { get; }
        internal float Score { get; }
    }

    /// <summary>
    ///     What a running character is doing right now: where it is in the conversation, and what it
    ///     is feeling.
    /// </summary>
    internal readonly struct EmbodimentLiveState
    {
        internal EmbodimentLiveState(
            bool hasConversationFlow,
            DialogueStateReading conversation,
            bool hasEmotion,
            IReadOnlyList<EmbodimentEmotionScore> emotions)
        {
            HasConversationFlow = hasConversationFlow;
            Conversation = conversation;
            HasEmotion = hasEmotion;
            Emotions = emotions ?? System.Array.Empty<EmbodimentEmotionScore>();
        }

        /// <summary>Whether Conversation Flow is on this character and publishing.</summary>
        internal bool HasConversationFlow { get; }

        /// <summary>The conversational turn state. Meaningless unless <see cref="HasConversationFlow" />.</summary>
        internal DialogueStateReading Conversation { get; }

        /// <summary>Whether Emotions is on this character and publishing.</summary>
        internal bool HasEmotion { get; }

        /// <summary>Emotion scores, strongest first. Empty when nothing has been detected yet.</summary>
        internal IReadOnlyList<EmbodimentEmotionScore> Emotions { get; }
    }

    /// <summary>
    ///     Reads a running character's live embodiment state, once, for whoever is showing it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The Convai Embodiment window's <b>Live</b> tab and the <c>Convai.DiagnoseEmbodiment</c>
    ///         tool answer the same question for the same character, so they read it through one
    ///         function rather than each reaching into <see cref="EmbodimentContext" /> with its own
    ///         idea of which contracts matter and how to order the scores.
    ///     </para>
    ///     <para>
    ///         Read-only and allocation-light: one list sized to the score count, built only when
    ///         asked. Safe to call per repaint, though callers that repaint at speed should still
    ///         throttle — the window does.
    ///     </para>
    /// </remarks>
    internal static class EmbodimentLiveStateService
    {
        /// <summary>
        ///     Reads what <paramref name="characterRoot" /> is doing, or a default state when it is
        ///     not a running Convai character. Outside Play Mode there is nothing to read and the
        ///     result reports both sources absent — that is the normal answer, not an error.
        /// </summary>
        internal static EmbodimentLiveState Read(GameObject characterRoot) =>
            Read(characterRoot == null
                ? null
                : characterRoot.GetComponentInChildren<EmbodimentContext>(true));

        /// <summary>Reads what the character owning <paramref name="context" /> is doing.</summary>
        internal static EmbodimentLiveState Read(EmbodimentContext context)
        {
            if (context == null) return default;

            IConversationFlowSource flow = context.ConversationFlowSource;
            IEmotionStateSource emotion = context.EmotionStateSource;

            return new EmbodimentLiveState(
                flow != null,
                flow?.Current ?? default,
                emotion != null,
                emotion == null ? null : RankScores(emotion.Current.AllScores));
        }

        /// <summary>
        ///     Orders the scores strongest first. A small, bounded set — one entry per emotion label —
        ///     so a local list and an insertion sort beat allocating LINQ machinery on a path the
        ///     window calls ten times a second.
        /// </summary>
        private static IReadOnlyList<EmbodimentEmotionScore> RankScores(
            IReadOnlyDictionary<string, float> scores)
        {
            if (scores == null || scores.Count == 0) return System.Array.Empty<EmbodimentEmotionScore>();

            var ordered = new List<EmbodimentEmotionScore>(scores.Count);
            foreach (KeyValuePair<string, float> pair in scores)
                ordered.Add(new EmbodimentEmotionScore(pair.Key, pair.Value));

            ordered.Sort((a, b) => b.Score.CompareTo(a.Score));
            return ordered;
        }
    }
}
