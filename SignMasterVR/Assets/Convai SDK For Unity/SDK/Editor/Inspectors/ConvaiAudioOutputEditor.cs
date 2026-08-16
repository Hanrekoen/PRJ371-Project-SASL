using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Components;
using UnityEditor;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiAudioOutput" /> — the optional companion component that
    ///     controls a character's speech volume, mute state and 3D audio falloff.
    /// </summary>
    [CustomEditor(typeof(ConvaiAudioOutput))]
    internal sealed class ConvaiAudioOutputEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Audio Output";
        protected override string Subtitle => "Convai Audio Output";

        protected override string Purpose =>
            "Controls this character's speech volume, mute state and 3D audio falloff. Requires a " +
            "Convai Character and an Audio Source on the same GameObject.";

        protected override string EditorStateHostId => "ConvaiAudioOutputEditor";
    }
}
