using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Components;
using UnityEditor;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiPushToTalkController" /> — drives push-to-talk pressing
    ///     and releasing for a target character, resolving the active manager and character
    ///     automatically when no explicit references are assigned.
    /// </summary>
    [CustomEditor(typeof(ConvaiPushToTalkController))]
    internal sealed class ConvaiPushToTalkControllerEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Push To Talk";
        protected override string Subtitle => "Convai Push To Talk Controller";

        protected override string Purpose =>
            "Opens the microphone while the player holds the push-to-talk key, and closes it when " +
            "they let go. Leave the fields below empty and it will find the character the player is " +
            "talking to on its own.";

        protected override string EditorStateHostId => "ConvaiPushToTalkControllerEditor";
    }
}
