using Convai.Runtime.Actions;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    // Scene-view half of ConvaiActionTargetEditor : a draggable
    // position handle for the interaction point. The selected/active gizmo drawing itself lives
    // in the sibling ConvaiActionTargetGizmoDrawer static class below, since [DrawGizmo] callbacks
    // must be static methods, not Editor instance methods.
    internal sealed partial class ConvaiActionTargetEditor
    {
        private void OnSceneGUI()
        {
            // Scene-view callbacks must read the single `target`, never `targets` or
            // `serializedObject` : Unity logs a warning for either inside OnSceneGUI.
            if (target is not ConvaiActionTarget actionTarget)
                return;

            Transform point = actionTarget.InteractionPoint;
            if (point == null)
                return;

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(point.position, point.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(point, "Move Convai Action Target Interaction Point");
                point.position = newPosition;
            }
        }
    }

    /// <summary>
    ///     Selected/active scene-view gizmo for <see cref="ConvaiActionTarget" />: a disc at the
    ///     effective interaction point, a connecting line back to the
    ///     owning GameObject when the interaction point is a separate child, and a small label
    ///     with the target's effective name.
    /// </summary>
    internal static class ConvaiActionTargetGizmoDrawer
    {
        private const float DiscRadius = 0.22f;

        [DrawGizmo(GizmoType.Selected | GizmoType.Active, typeof(ConvaiActionTarget))]
        private static void DrawGizmos(ConvaiActionTarget target, GizmoType gizmoType)
        {
            if (target == null)
                return;

            Transform owner = target.transform;
            Transform point = target.InteractionPoint;
            Vector3 anchor = point != null ? point.position : owner.position;

            Color previous = Handles.color;
            Handles.color = Theme.Accent;

            Handles.DrawWireDisc(anchor, Vector3.up, DiscRadius);

            if (point != null && point != owner)
                Handles.DrawLine(owner.position, anchor);

            Handles.color = previous;
            Handles.Label(anchor + Vector3.up * (DiscRadius + 0.08f), target.TargetName);
        }
    }
}
