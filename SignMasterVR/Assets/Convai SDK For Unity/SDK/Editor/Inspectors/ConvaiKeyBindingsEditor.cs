using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Configuration;
using UnityEditor;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiKeyBindings" /> — the keyboard shortcuts Convai runtime
    ///     components read for push-to-talk and opening the settings UI.
    /// </summary>
    [CustomEditor(typeof(ConvaiKeyBindings))]
    internal sealed class ConvaiKeyBindingsEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Key Bindings";
        protected override string Subtitle => "Convai Key Bindings";

        protected override string Purpose =>
            "The keys players press to talk to your characters and to open the in-game settings " +
            "panel. Keep this asset in a Resources folder and Convai will pick it up when the game " +
            "starts.";

        protected override string EditorStateHostId => "ConvaiKeyBindingsEditor";
    }
}
