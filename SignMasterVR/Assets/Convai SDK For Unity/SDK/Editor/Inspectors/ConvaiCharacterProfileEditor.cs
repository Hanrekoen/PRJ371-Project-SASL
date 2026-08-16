using Convai.Editor.Inspectors.Framework;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiCharacterProfile" /> — the reusable identity and default
    ///     interaction settings a designer assigns to one or more characters so they share a name,
    ///     nametag color and connection defaults.
    /// </summary>
    [CustomEditor(typeof(ConvaiCharacterProfile))]
    internal sealed class ConvaiCharacterProfileEditor : ConvaiInspectorEditor
    {
        private SerializedProperty _characterIdProp;

        protected override string Title => "Character Profile";
        protected override string Subtitle => "Convai Character Profile";

        protected override string Purpose =>
            "A reusable identity and default interaction settings for a character. Assign this asset " +
            "to a Convai Character so its name, nametag color and connection defaults come from here.";

        protected override string EditorStateHostId => "ConvaiCharacterProfileEditor";

        protected override GUIContent StatusChip =>
            _characterIdProp != null && !string.IsNullOrWhiteSpace(_characterIdProp.stringValue)
                ? Chips.Ready.Content
                : Chips.NotSetUp.Content;

        protected override Color StatusChipTint =>
            _characterIdProp != null && !string.IsNullOrWhiteSpace(_characterIdProp.stringValue)
                ? Chips.Ready.Tint
                : Chips.NotSetUp.Tint;

        protected override void OnEnable()
        {
            base.OnEnable();

            _characterIdProp = serializedObject.FindProperty("_characterId");
        }
    }
}
