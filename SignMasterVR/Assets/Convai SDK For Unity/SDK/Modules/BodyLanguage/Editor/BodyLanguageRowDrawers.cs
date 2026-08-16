using System.Collections.Generic;
using Convai.Modules.BodyLanguage.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Editor
{
    /// <summary>
    ///     Shared drawing for the profile's two authored tables — the per-state policies and the
    ///     per-emotion adjustments.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both are lists of serializable structs, so Unity draws each row as "Element 0" and each
    ///         field under its own nicified name. That costs a customer twice: the row headers say
    ///         nothing about which state or emotion they configure, and the fields inside read in
    ///         anatomy — "Sagittal Lean Bias", "Breath Rate Cpm".
    ///     </para>
    ///     <para>
    ///         These drawers title each row with the thing it configures and relabel its fields. As on
    ///         the profile itself this is presentation only: the struct fields are public serialized
    ///         API and are deliberately not renamed, so no asset re-serializes and no customer's code
    ///         breaks.
    ///     </para>
    /// </remarks>
    internal static class BodyLanguageRowLabels
    {
        /// <summary>
        ///     Built once per field name. These drawers run for every visible row on every repaint, so
        ///     a fresh <see cref="GUIContent" /> per child would allocate continuously while a profile
        ///     is merely on screen.
        /// </summary>
        private static readonly Dictionary<string, GUIContent> Cache = new();

        internal static GUIContent Label(SerializedProperty child)
        {
            if (Cache.TryGetValue(child.name, out GUIContent cached)) return cached;

            var content = new GUIContent(BodyLanguageLabels.ForRowField(child.name), child.tooltip);

            Cache[child.name] = content;
            return content;
        }

        /// <summary>Draws one struct row as a foldout titled by <paramref name="title" />.</summary>
        internal static void DrawRow(Rect position, SerializedProperty property, string title)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(position.x, position.y, position.width, line);

            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, title, true);
            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                row.y += line + spacing;
                GUIContent childLabel = Label(child);
                row.height = EditorGUI.GetPropertyHeight(child, childLabel, true);
                EditorGUI.PropertyField(row, child, childLabel, true);
                row.height = line;
            }

            EditorGUI.indentLevel--;
        }

        internal static float RowHeight(SerializedProperty property)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded) return line;

            float height = line;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                height += spacing + EditorGUI.GetPropertyHeight(child, Label(child), true);
            }

            return height;
        }
    }

    /// <summary>Draws one per-state policy row, titled by the dialogue state it configures.</summary>
    [CustomPropertyDrawer(typeof(BodyLanguageStatePolicy))]
    internal sealed class BodyLanguageStatePolicyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty state = property.FindPropertyRelative("State");
            string title = state != null && state.enumDisplayNames.Length > 0
                ? state.enumDisplayNames[Mathf.Clamp(state.enumValueIndex, 0, state.enumDisplayNames.Length - 1)]
                : label.text;

            BodyLanguageRowLabels.DrawRow(position, property, title);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            BodyLanguageRowLabels.RowHeight(property);
    }

    /// <summary>Draws one per-emotion adjustment row, titled by the emotion it applies to.</summary>
    [CustomPropertyDrawer(typeof(BodyLanguageEmotionModifier))]
    internal sealed class BodyLanguageEmotionModifierDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty emotion = property.FindPropertyRelative("EmotionLabel");
            string authored = emotion != null ? emotion.stringValue : null;
            string title = string.IsNullOrWhiteSpace(authored) ? "(no emotion set)" : authored;

            BodyLanguageRowLabels.DrawRow(position, property, title);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            BodyLanguageRowLabels.RowHeight(property);
    }
}
