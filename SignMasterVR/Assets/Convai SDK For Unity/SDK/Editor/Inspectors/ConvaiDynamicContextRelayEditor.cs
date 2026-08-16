using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.DynamicContext;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiDynamicContextRelay" /> — lets a scene author wire
    ///     Unity events (button clicks, triggers, animation events) to update one character's dynamic
    ///     context without writing code.
    /// </summary>
    [CustomEditor(typeof(ConvaiDynamicContextRelay))]
    internal sealed class ConvaiDynamicContextRelayEditor : ConvaiInspectorEditor
    {
        private SerializedProperty _character;
        private SerializedProperty _autoResolveCharacter;

        protected override string Title => "Dynamic Context Relay";
        protected override string Subtitle => "Convai Dynamic Context Relay";

        protected override string Purpose =>
            "Lets Unity events call into one character's dynamic context — set a state value, add an " +
            "event, or point its attention at something — without writing code.";

        protected override string EditorStateHostId => "ConvaiDynamicContextRelayEditor";

        protected override GUIContent StatusChip => HasResolvedCharacter ? Chips.Ready.Content : Chips.NotSetUp.Content;
        protected override Color StatusChipTint => HasResolvedCharacter ? Chips.Ready.Tint : Chips.NotSetUp.Tint;

        protected override void OnEnable()
        {
            base.OnEnable();
            _character = serializedObject.FindProperty("_character");
            _autoResolveCharacter = serializedObject.FindProperty("_autoResolveCharacter");
        }

        private bool HasResolvedCharacter =>
            _character.objectReferenceValue != null ||
            (_autoResolveCharacter.boolValue &&
             ((ConvaiDynamicContextRelay)target).GetComponent<ConvaiCharacter>() != null);
    }
}
