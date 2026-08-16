using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiSceneInstaller" /> — the explicit list of characters and
    ///     players a scene registers with the agent registry on load, an IL2CPP-safe alternative to
    ///     scene discovery.
    /// </summary>
    [CustomEditor(typeof(ConvaiSceneInstaller))]
    internal sealed class ConvaiSceneInstallerEditor : ConvaiInspectorEditor
    {
        private ConvaiSceneInstaller _installer;

        protected override string Title => "Scene Installer";
        protected override string Subtitle => "Convai Scene Installer";

        protected override string Purpose =>
            "Tells Convai exactly which characters and players are in this scene, instead of letting " +
            "it search for them. Listing them here is the reliable choice for built players, where " +
            "searching can miss objects.";

        protected override string EditorStateHostId => "ConvaiSceneInstallerEditor";

        protected override GUIContent StatusChip =>
            _installer != null && _installer.IsValid ? Chips.Ready.Content : Chips.NotSetUp.Content;

        protected override Color StatusChipTint =>
            _installer != null && _installer.IsValid ? Chips.Ready.Tint : Chips.NotSetUp.Tint;

        protected override void OnEnable()
        {
            base.OnEnable();

            _installer = (ConvaiSceneInstaller)target;
        }
    }
}
