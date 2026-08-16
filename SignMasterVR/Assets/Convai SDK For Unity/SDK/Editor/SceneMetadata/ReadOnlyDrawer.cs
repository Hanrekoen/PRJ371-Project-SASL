using Convai.Runtime.SceneMetadata;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.SceneMetadata
{
    /// <summary>
    ///     Draws a field marked <see cref="ReadOnlyAttribute" /> as visible but not editable — for
    ///     values the SDK fills in and the user should be able to read.
    /// </summary>
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    internal sealed class ReadOnlyDrawer : PropertyDrawer
    {
        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // DisabledScope rather than GUI.enabled = false/true: the manual pair restored to true
            // rather than to whatever it was, so a read-only field drawn inside an already-disabled
            // group re-enabled everything drawn after it in that group.
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(position, property, label, true);
        }

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUI.GetPropertyHeight(property, label, true);
    }
}
