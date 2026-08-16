using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     How <see cref="ActionAnchorOptions.FacingMode" /> orients the character once it has
    ///     arrived and aligned to an anchor (see
    ///     <c>ConvaiBodyAnimationController.PlayActionAt</c>).
    /// </summary>
    public enum ActionFacingMode
    {
        /// <summary>Face the same direction as the anchor's own forward.</summary>
        AnchorForward = 0,

        /// <summary>Turn to face the anchor's position (e.g. sitting down facing a table).</summary>
        FaceAnchor = 1,

        /// <summary>Keep whatever facing the character arrived with — no yaw alignment.</summary>
        None = 2
    }

    /// <summary>
    ///     Approach and root-alignment tuning for <c>ConvaiBodyAnimationController.PlayActionAt</c>
    ///     — the "sit on the bench" / pick-up / use-prop enabler. Carried as an optional
    ///     per-<see cref="ActionEntry" /> default so content can author its own approach data
    ///     (a chair needs a different offset than a wall-mounted lever); an explicit options
    ///     argument on the call always overrides the entry's authored values.
    /// </summary>
    [Serializable]
    public sealed class ActionAnchorOptions
    {
        [SerializeField]
        [Tooltip("Local-space offset from the anchor (relative to its own rotation) where the " +
                 "character should stand before the action plays. Default: 0.5m in front (+Z).")]
        private Vector3 _approachOffset = new(0f, 0f, 0.5f);

        [SerializeField]
        [Tooltip("How the character is yaw-aligned once settled at the approach point.")]
        private ActionFacingMode _facingMode = ActionFacingMode.AnchorForward;

        [SerializeField, Min(0.01f)]
        [Tooltip("Root alignment is skipped (degrades to playing the action unaligned) when the " +
                 "character ends up farther than this from the approach point after arriving.")]
        private float _maxAlignmentDistance = 0.4f;

        [SerializeField, Range(0f, 180f)]
        [Tooltip("Root alignment is skipped when the character's facing is off by more than this " +
                 "many degrees from the target facing after arriving.")]
        private float _maxAlignmentYawDegrees = 45f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Seconds the root position/yaw lerp into precise alignment takes.")]
        private float _alignmentDurationSeconds = 0.3f;

        /// <summary>Local-space offset from the anchor where the character should stand.</summary>
        public Vector3 ApproachOffset => _approachOffset;

        /// <summary>How the character is yaw-aligned once settled at the approach point.</summary>
        public ActionFacingMode FacingMode => _facingMode;

        /// <summary>Alignment is skipped beyond this distance (meters) from the approach point.</summary>
        public float MaxAlignmentDistance => Mathf.Max(0.01f, _maxAlignmentDistance);

        /// <summary>Alignment is skipped beyond this yaw error (degrees).</summary>
        public float MaxAlignmentYawDegrees => Mathf.Clamp(_maxAlignmentYawDegrees, 0f, 180f);

        /// <summary>Duration (seconds) of the root position/yaw alignment lerp.</summary>
        public float AlignmentDurationSeconds => Mathf.Max(0.01f, _alignmentDurationSeconds);
    }
}
