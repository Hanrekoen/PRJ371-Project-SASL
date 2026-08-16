namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     Canonical semantic identifiers for facial blendshapes used by embodiment modules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Values are named after the ARKit 52-blendshape convention because it is the most
    ///         widely adopted industry standard. Other conventions (Reallusion CC3, Epic
    ///         MetaHuman, custom) are mapped to these semantic names by rig convention
    ///         adapters. Modules MUST reference blendshapes via these identifiers rather than
    ///         raw mesh blendshape names.
    ///     </para>
    ///     <para>
    ///         Not every rig provides every blendshape. Consumers must use
    ///         <c>IStandardRigBinding.TryGetBlendshape</c> and gracefully no-op when a
    ///         blendshape is unavailable.
    ///     </para>
    /// </remarks>
    public enum StandardBlendshape
    {
        // Eye
        EyeBlinkLeft = 0,
        EyeBlinkRight = 1,
        EyeLookDownLeft = 2,
        EyeLookDownRight = 3,
        EyeLookInLeft = 4,
        EyeLookInRight = 5,
        EyeLookOutLeft = 6,
        EyeLookOutRight = 7,
        EyeLookUpLeft = 8,
        EyeLookUpRight = 9,
        EyeSquintLeft = 10,
        EyeSquintRight = 11,
        EyeWideLeft = 12,
        EyeWideRight = 13,
        EyeUpperLidDownLeft = 14,
        EyeUpperLidDownRight = 15,
        EyeUpperLidUpLeft = 16,
        EyeUpperLidUpRight = 17,
        EyeLowerLidUpLeft = 18,
        EyeLowerLidUpRight = 19,

        // Brow
        BrowDownLeft = 20,
        BrowDownRight = 21,
        BrowInnerUp = 22,
        BrowOuterUpLeft = 23,
        BrowOuterUpRight = 24,

        // Cheek / Nose
        CheekPuff = 30,
        CheekSquintLeft = 31,
        CheekSquintRight = 32,
        NoseSneerLeft = 33,
        NoseSneerRight = 34,

        // Jaw
        JawForward = 40,
        JawLeft = 41,
        JawRight = 42,
        JawOpen = 43,

        // Mouth
        MouthClose = 50,
        MouthFunnel = 51,
        MouthPucker = 52,
        MouthLeft = 53,
        MouthRight = 54,
        MouthSmileLeft = 55,
        MouthSmileRight = 56,
        MouthFrownLeft = 57,
        MouthFrownRight = 58,
        MouthDimpleLeft = 59,
        MouthDimpleRight = 60,
        MouthStretchLeft = 61,
        MouthStretchRight = 62,
        MouthRollLower = 63,
        MouthRollUpper = 64,
        MouthShrugLower = 65,
        MouthShrugUpper = 66,
        MouthPressLeft = 67,
        MouthPressRight = 68,
        MouthLowerDownLeft = 69,
        MouthLowerDownRight = 70,
        MouthUpperUpLeft = 71,
        MouthUpperUpRight = 72,

        // Tongue
        TongueOut = 80
    }
}
