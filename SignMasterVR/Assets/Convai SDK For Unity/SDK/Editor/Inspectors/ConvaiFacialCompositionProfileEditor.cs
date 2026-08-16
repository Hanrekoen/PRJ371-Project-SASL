using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Animation;
using UnityEditor;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiFacialCompositionProfile" /> — configures how a
    ///     character's facial blendshapes are grouped and how Emotion, LipSync and Custom layers
    ///     blend together on each region while idle and while speaking.
    /// </summary>
    [CustomEditor(typeof(ConvaiFacialCompositionProfile))]
    internal sealed class ConvaiFacialCompositionProfileEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Facial Composition";
        protected override string Subtitle => "Facial Composition Profile";
        protected override string Purpose =>
            "Controls how a character's facial blendshapes are classified and blended between " +
            "emotion, lip sync, and custom animation. Assign this asset to a character's facial " +
            "setup to change how expressive or speech-driven each face region looks.";
    }
}
