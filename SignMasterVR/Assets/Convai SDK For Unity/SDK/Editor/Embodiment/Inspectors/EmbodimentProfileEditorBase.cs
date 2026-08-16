using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Shared base for the embodiment profile/config asset inspectors: the Convai header plus the
    ///     small property helpers each one repeats.
    /// </summary>
    internal abstract class ConvaiEmbodimentProfileEditorBase<TProfile> : ConvaiInspectorEditor
        where TProfile : UnityEngine.Object
    {
        protected TProfile Profile => (TProfile)target;

        protected virtual string HeaderSubtitle => "Embodiment Profile";
        protected virtual string HeaderStatus => "Asset";
        protected virtual Color HeaderStatusColor => ConvaiEditorTheme.AccentBright;

        protected sealed override string Title => HeaderTitle;
        protected sealed override string Subtitle => HeaderSubtitle;

        /// <summary>
        ///     Reused header-chip content. <c>StatusChip</c> is read once per repaint, so building a
        ///     <see cref="GUIContent" /> in the getter allocated on every one of them; the text is
        ///     refreshed in place instead, and only when it has actually changed.
        /// </summary>
        private readonly GUIContent _statusChip = new();

        protected sealed override GUIContent StatusChip
        {
            get
            {
                string status = HeaderStatus;
                if (string.IsNullOrWhiteSpace(status))
                    return null;

                if (!string.Equals(_statusChip.text, status, System.StringComparison.Ordinal))
                    _statusChip.text = status;

                return _statusChip;
            }
        }

        protected sealed override Color StatusChipTint => HeaderStatusColor;

        /// <summary>Profile inspectors are hand-authored; the generic field renderer never runs.</summary>
        /// <remarks>
        ///     Every settings asset the SDK ships is reachable here, so the read-only guard belongs on
        ///     the base rather than in nine inspectors that would each have to remember it — which is
        ///     precisely what none of them did. A character's inspector needs no such guard: there,
        ///     copy-on-write makes SDK-owned settings the user's the moment they change anything.
        /// </remarks>
        protected sealed override void DrawBody()
        {
            using (Ownership.ConvaiOwnershipNotice.BeginAssetEdit(target))
                DrawProfileInspector();
        }

        protected abstract string HeaderTitle { get; }
        protected abstract void DrawProfileInspector();

        protected void DrawProperties(params SerializedProperty[] properties)
        {
            if (properties == null) return;

            for (int i = 0; i < properties.Length; i++)
            {
                SerializedProperty property = properties[i];
                if (property != null)
                    EditorGUILayout.PropertyField(property, true);
            }
        }

        protected void DrawProperty(string propertyName, string label = null, bool includeChildren = true)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                WarningBox("Missing Serialized Field", $"{typeof(TProfile).Name}.{propertyName} was not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(label))
                EditorGUILayout.PropertyField(property, includeChildren);
            else
                EditorGUILayout.PropertyField(property, new GUIContent(label), includeChildren);
        }

        protected SerializedProperty Find(string propertyName) => serializedObject.FindProperty(propertyName);

        protected int ArraySize(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null && property.isArray ? property.arraySize : 0;
        }
    }
}
