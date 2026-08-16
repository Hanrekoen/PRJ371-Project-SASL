using UnityEngine;

namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     What the rig currently looks like, relative to what the gaze wants: the shift still
    ///     required to put the eye line on the target, and how far the animation has already
    ///     moved the head off its neutral.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Angles are degrees in the character-root frame — yaw positive to the character's
    ///         right, pitch positive upward — which is the one frame every stage of the gaze
    ///         chain agrees on.
    ///     </para>
    ///     <para>
    ///         Measuring once, here, and handing the result to the actuator ladder is what makes
    ///         the coordination invariants checkable. When each stage measured for itself they
    ///         could disagree about the size of the shift they were dividing up, and the
    ///         difference had nowhere to go but the eyes' clamp.
    ///     </para>
    /// </remarks>
    internal readonly struct GazeShiftMeasurement
    {
        /// <summary>Yaw (degrees) from the eye line to the target.</summary>
        public readonly float RequiredYaw;

        /// <summary>Pitch (degrees) from the eye line to the target.</summary>
        public readonly float RequiredPitch;

        /// <summary>
        ///     Yaw (degrees) the animation has already turned the head by, measured on the
        ///     head-carried "straight ahead". The reflex that cancels it is not part of the
        ///     shift, so it is reported separately rather than folded into the requirement.
        /// </summary>
        public readonly float AnimatedYaw;

        /// <summary>Pitch (degrees) the animation has already bowed the head by.</summary>
        public readonly float AnimatedPitch;

        /// <summary>False when the direction was degenerate and nothing should be driven from it.</summary>
        public readonly bool IsValid;

        public GazeShiftMeasurement(
            float requiredYaw, float requiredPitch, float animatedYaw, float animatedPitch)
        {
            RequiredYaw = requiredYaw;
            RequiredPitch = requiredPitch;
            AnimatedYaw = animatedYaw;
            AnimatedPitch = animatedPitch;
            IsValid = true;
        }

        /// <summary>Total angular size of the shift — what decides how deep the ladder recruits.</summary>
        public float Amplitude => Mathf.Sqrt(RequiredYaw * RequiredYaw + RequiredPitch * RequiredPitch);
    }
}
