using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Configuration;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiSceneConfig" /> — an optional shared asset that lists
    ///     which characters to auto-connect or instantiate when a scene loads, for use with
    ///     ConvaiSceneInstaller.
    /// </summary>
    [CustomEditor(typeof(ConvaiSceneConfig))]
    internal sealed class ConvaiSceneConfigEditor : ConvaiInspectorEditor
    {
        private SerializedProperty _characterIdsProp;
        private SerializedProperty _characterPrefabsProp;

        protected override string Title => "Scene Configuration";
        protected override string Subtitle => "Convai Scene Config";

        protected override string Purpose =>
            "The characters this scene should bring in when it loads — either prefabs you drop in " +
            "here, or characters named by their Convai ID. Hand this asset to a Convai Scene " +
            "Installer to use it.";

        protected override string EditorStateHostId => "ConvaiSceneConfigEditor";

        protected override GUIContent StatusChip =>
            HasAnyCharacters ? Chips.Ready.Content : Chips.NotSetUp.Content;

        protected override Color StatusChipTint =>
            HasAnyCharacters ? Chips.Ready.Tint : Chips.NotSetUp.Tint;

        private bool HasAnyCharacters =>
            (_characterIdsProp != null && _characterIdsProp.arraySize > 0) ||
            (_characterPrefabsProp != null && _characterPrefabsProp.arraySize > 0);

        protected override void OnEnable()
        {
            base.OnEnable();

            _characterIdsProp = serializedObject.FindProperty("_characterIds");
            _characterPrefabsProp = serializedObject.FindProperty("_characterPrefabs");
        }
    }
}
