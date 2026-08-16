using Convai.Domain.Emotion;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     How the emotion detection choice is presented, and the only sanctioned mapping between a
    ///     presented option and the <see cref="EmotionDetectionMode" /> it selects.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This type exists because the previous surface used
    ///         <c>SerializedProperty.enumValueIndex</c> as an index into its own label array.
    ///         <c>enumValueIndex</c> indexes the enum's <em>declaration</em> order —
    ///         <c>Off, Llm, Nrclex</c> — while the labels were written in the order the author
    ///         wanted to read them, so the two providers were offered swapped: choosing the
    ///         word-matching option connected with the whole-reply one, and the one-click
    ///         "turn emotions on" fix selected the opposite of what its own comment claimed.
    ///     </para>
    ///     <para>
    ///         Presentation order is therefore declared here, separately from the enum, and every
    ///         read and write goes through <see cref="IndexOf" />/<see cref="ValueAt" />. Nothing
    ///         may infer one from the other.
    ///     </para>
    /// </remarks>
    internal static class EmotionDetectionModes
    {
        /// <summary>
        ///     Presentation order, most-common-first. Independent of the enum's declaration order by
        ///     design — see the type remarks.
        /// </summary>
        internal static readonly EmotionDetectionMode[] Order =
        {
            EmotionDetectionMode.Off,
            EmotionDetectionMode.Nrclex,
            EmotionDetectionMode.Llm
        };

        /// <summary>
        ///     The mode a new character starts on, and the one the troubleshooter's "turn emotions
        ///     on" fix selects: it reacts within the reply and needs nothing configured server-side.
        /// </summary>
        internal const EmotionDetectionMode Default = EmotionDetectionMode.Nrclex;

        /// <summary>Dropdown labels, aligned index-for-index with <see cref="Order" />.</summary>
        internal static readonly GUIContent[] Options =
        {
            new("Off — no emotional reaction",
                "This character's face never reacts to what is being said."),
            new("Responsive — updates while the reply is spoken",
                "Reacts almost immediately and can change more than once within a single reply."),
            new("Accurate — one reading of the whole reply",
                "Weighs the whole reply against the character's backstory, so the expression fits " +
                "what was meant.")
        };

        /// <summary>
        ///     The paragraph shown under the dropdown for the selected mode: what it does, and the
        ///     one thing it is worse at. Both modes are a real choice, so neither is described as
        ///     the compromise.
        /// </summary>
        internal static string DescriptionFor(EmotionDetectionMode mode) => mode switch
        {
            EmotionDetectionMode.Nrclex =>
                "Reads each part of the reply as it arrives, matching the wording against an emotion " +
                "vocabulary. The face can change several times within one reply and moves while the " +
                "character is still speaking. It matches wording rather than meaning, and the " +
                "vocabulary is built on English — a character speaking another language will read " +
                "less accurately.",

            EmotionDetectionMode.Llm =>
                "Reads the finished reply together with the character's backstory and settles on one " +
                "emotion for it. Because it weighs meaning rather than wording, the expression fits " +
                "what was actually said and works well in any language. There is one reading per " +
                "reply instead of several, and on a long reply it arrives near the end of it.",

            _ =>
                "This character will not react emotionally to anything. Choose Responsive or Accurate " +
                "to turn emotions on."
        };

        /// <summary>Presentation index of <paramref name="mode" />, or the index of <see cref="EmotionDetectionMode.Off" />.</summary>
        internal static int IndexOf(EmotionDetectionMode mode)
        {
            for (int i = 0; i < Order.Length; i++)
                if (Order[i] == mode)
                    return i;
            return 0;
        }

        /// <summary>The mode at a presentation index, clamped so a stale index can never write a wrong provider.</summary>
        internal static EmotionDetectionMode ValueAt(int index) =>
            index >= 0 && index < Order.Length ? Order[index] : EmotionDetectionMode.Off;

        /// <summary>Short label for the mode, without its explanatory clause. Used by status chips.</summary>
        internal static string ShortNameFor(EmotionDetectionMode mode) => mode switch
        {
            EmotionDetectionMode.Nrclex => "Responsive",
            EmotionDetectionMode.Llm => "Accurate",
            _ => "Off"
        };
    }
}
