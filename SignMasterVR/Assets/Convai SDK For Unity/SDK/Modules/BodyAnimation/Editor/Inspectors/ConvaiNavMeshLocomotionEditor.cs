using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.BodyAnimation.Editor.Inspectors
{
    /// <summary>Inspector for <see cref="ConvaiNavMeshLocomotion" /> — movement settings and live state.</summary>
    [CustomEditor(typeof(ConvaiNavMeshLocomotion))]
    internal sealed class ConvaiNavMeshLocomotionEditor : ConvaiInspectorEditor
    {
        private const string SectionMovement = "Movement";
        private const string SectionLive = "LiveMovement";

        private SerializedProperty _agent;
        private SerializedProperty _speedProfile;
        private SerializedProperty _autoJogDistance;
        private SerializedProperty _minJogDistance;
        private SerializedProperty _acceleration;
        private SerializedProperty _walkSpeed;
        private SerializedProperty _jogSpeed;
        private SerializedProperty _rotationDegreesPerSecond;
        private SerializedProperty _drawGizmos;

        protected override void OnEnable()
        {
            base.OnEnable();
            _agent = serializedObject.FindProperty("_agent");
            _speedProfile = serializedObject.FindProperty("_speedProfile");
            _autoJogDistance = serializedObject.FindProperty("_autoJogDistance");
            _minJogDistance = serializedObject.FindProperty("_minJogDistance");
            _acceleration = serializedObject.FindProperty("_acceleration");
            _walkSpeed = serializedObject.FindProperty("_walkSpeed");
            _jogSpeed = serializedObject.FindProperty("_jogSpeed");
            _rotationDegreesPerSecond = serializedObject.FindProperty("_rotationDegreesPerSecond");
            _drawGizmos = serializedObject.FindProperty("_drawGizmos");
        }

        protected override string Title => "NavMesh Locomotion";

        protected override string Subtitle => "Movement authority";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private ConvaiEditorChip CurrentChip
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready;
                return ConvaiEditorChips.Running(((ConvaiNavMeshLocomotion)target).IsMoving);
            }
        }

        /// <summary>Keeps the live movement readout updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void DrawBody()
        {
            InfoBox(
                "What this does",
                "Owns the character's movement: call MoveTo(position) from any script and the " +
                "NavMeshAgent walks or jogs there while the Body Animation controller keeps the " +
                "animation in perfect sync (no foot slide, animated turns, starts, and stops). " +
                "Rotation is animation-driven — the agent only moves the capsule.");

            DrawMovementSection();
            DrawLiveMovementSection((ConvaiNavMeshLocomotion)target);
        }

        private void DrawMovementSection()
        {
            if (!DrawSection(SectionMovement, "Movement", ConvaiEditorGlyphs.Motion)) return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_agent, new GUIContent("Nav Mesh Agent",
                    "The NavMeshAgent this character moves with. Leave empty and Convai will use " +
                    "(or add) one automatically."));
                EditorGUILayout.PropertyField(_speedProfile, new GUIContent("Speed Profile",
                    "Choose how fast the character travels: always walk, always jog, or jog only " +
                    "when the destination is far enough away (Auto)."));
                // Labels kept short enough to survive a narrow inspector; the condition each one
                // describes lives in the tooltip, which has room for it.
                EditorGUILayout.PropertyField(_autoJogDistance, new GUIContent("Jog Beyond (m)",
                    "Used only by the Auto speed profile: destinations farther away than this are " +
                    "jogged to, nearer ones are walked."));
                EditorGUILayout.PropertyField(_minJogDistance, new GUIContent("Never Jog Under (m)",
                    "Destinations closer than this are always walked, even on the Jog profile — a jog " +
                    "needs room to speed up, cruise, and come to a stop convincingly."));
                EditorGUILayout.PropertyField(_acceleration, new GUIContent("Acceleration",
                    "How quickly the character speeds up or slows down. Lower values feel more " +
                    "human; higher values snap to speed almost instantly."));
                EditorGUILayout.PropertyField(_rotationDegreesPerSecond, new GUIContent("Rotation Speed",
                    "How fast the character turns to face its direction of travel, in degrees per " +
                    "second, while following a path."));
                EditorGUILayout.PropertyField(_walkSpeed, new GUIContent("Walk Speed",
                    "How fast the character walks, in meters per second. Overridden automatically " +
                    "once a Body Animation controller is present on the character."));
                EditorGUILayout.PropertyField(_jogSpeed, new GUIContent("Jog Speed",
                    "How fast the character jogs, in meters per second. Overridden automatically " +
                    "once a Body Animation controller is present on the character."));
                EditorGUILayout.PropertyField(_drawGizmos, new GUIContent("Draw Path Gizmos",
                    "Shows the character's planned path, destination, and velocity as lines in the " +
                    "Scene view. For debugging only — has no effect on gameplay."));

                InfoBox(
                    "Speeds are animation-synced",
                    "When a Body Animation controller runs on this character, walk/jog speeds are " +
                    "overridden by the animation clips' measured ground speeds so the feet never " +
                    "slide. Tune gait choice with the profile and distances above.");
            });
        }

        private void DrawLiveMovementSection(ConvaiNavMeshLocomotion locomotion)
        {
            if (!DrawSection(SectionLive, "Live", ConvaiEditorGlyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying)
                {
                    OfflinePlaceholder();
                    return;
                }

                NavMeshAgent agent = locomotion.Agent;
                EditorGUILayout.BeginHorizontal();
                LiveCell("State", locomotion.IsMoving ? "Moving" : "Idle",
                    locomotion.IsMoving ? Theme.AccentBright : Theme.StatusIdle, 110f);
                LiveCell("Speed", $"{locomotion.Speed:0.00} m/s", Theme.StatusInfo, 110f);
                LiveCell("Remaining", $"{locomotion.RemainingDistance:0.00} m", Theme.TextPrimary, 110f);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Commanded Speed", $"{locomotion.DesiredSpeed:0.00} m/s");
                if (locomotion.IsMoving)
                    EditorGUILayout.LabelField("Destination", FormatVector3(locomotion.Destination));
                if (agent != null && !agent.isOnNavMesh)
                    WarningBox(
                        "Not On NavMesh",
                        "The agent is not standing on a baked NavMesh — MoveTo() calls will fail. " +
                        "Bake a NavMesh that covers the character's position.");
            });
        }
    }
}
