namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     Canonical semantic identifiers for character rig bones used by embodiment modules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Modules MUST NOT reference rig bones by name or path. They resolve bones through
    ///         <c>IStandardRigBinding.TryGetBone</c> using one of these semantic identifiers.
    ///         The rig binding is responsible for mapping these to the underlying skeleton,
    ///         which allows the same module code to drive ARKit, CC3, MetaHuman, or custom rigs
    ///         without modification.
    ///     </para>
    ///     <para>
    ///         Only bones that embodiment modules need to drive are listed. Finger and toe chains,
    ///         full IK goal helpers, and rig-specific nodes should remain outside of this enum
    ///         and are the responsibility of body animation clips.
    ///     </para>
    /// </remarks>
    public enum StandardBone
    {
        Hips = 0,
        Spine = 1,
        Chest = 2,
        UpperChest = 3,
        Neck = 4,
        Head = 5,
        LeftEye = 6,
        RightEye = 7,
        LeftShoulder = 8,
        RightShoulder = 9,
        LeftUpperArm = 10,
        RightUpperArm = 11,

        /// <summary>Left thigh bone — stance/weight-shift leg compensation.</summary>
        LeftUpperLeg = 12,

        /// <summary>Left shin bone — stance/weight-shift leg compensation.</summary>
        LeftLowerLeg = 13,

        /// <summary>Left foot bone — stance/weight-shift leg compensation.</summary>
        LeftFoot = 14,

        /// <summary>Right thigh bone — stance/weight-shift leg compensation.</summary>
        RightUpperLeg = 15,

        /// <summary>Right shin bone — stance/weight-shift leg compensation.</summary>
        RightLowerLeg = 16,

        /// <summary>Right foot bone — stance/weight-shift leg compensation.</summary>
        RightFoot = 17
    }
}
