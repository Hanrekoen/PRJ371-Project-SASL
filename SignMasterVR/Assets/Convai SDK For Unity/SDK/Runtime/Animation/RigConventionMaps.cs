using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Lookup tables that translate <see cref="StandardBlendshape" /> semantic identifiers
    ///     into the concrete blendshape name each supported rig convention uses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Tables are intentionally kept as plain <see cref="Dictionary{TKey,TValue}" />
    ///         rather than ScriptableObjects so they are trivially testable and versionable.
    ///         Users who need a custom mapping author a <c>CustomRigConventionMap</c> asset,
    ///         which is added to the binding at setup time.
    ///     </para>
    /// </remarks>
    internal static class RigConventionMaps
    {
        public static IReadOnlyDictionary<StandardBlendshape, string> ForConvention(RigConvention convention)
        {
            return convention switch
            {
                RigConvention.ARKit => ARKit,
                RigConvention.ReallusionCC3 => CC3,
                RigConvention.ReallusionCC4Extended => CC4Extended,
                RigConvention.MetaHuman => MetaHuman,
                _ => EmptyMap
            };
        }

        private static readonly Dictionary<StandardBlendshape, string> EmptyMap = new(0);

        private static readonly Dictionary<StandardBlendshape, string> ARKit = new()
        {
            { StandardBlendshape.EyeBlinkLeft, "eyeBlinkLeft" },
            { StandardBlendshape.EyeBlinkRight, "eyeBlinkRight" },
            { StandardBlendshape.EyeLookDownLeft, "eyeLookDownLeft" },
            { StandardBlendshape.EyeLookDownRight, "eyeLookDownRight" },
            { StandardBlendshape.EyeLookInLeft, "eyeLookInLeft" },
            { StandardBlendshape.EyeLookInRight, "eyeLookInRight" },
            { StandardBlendshape.EyeLookOutLeft, "eyeLookOutLeft" },
            { StandardBlendshape.EyeLookOutRight, "eyeLookOutRight" },
            { StandardBlendshape.EyeLookUpLeft, "eyeLookUpLeft" },
            { StandardBlendshape.EyeLookUpRight, "eyeLookUpRight" },
            { StandardBlendshape.EyeSquintLeft, "eyeSquintLeft" },
            { StandardBlendshape.EyeSquintRight, "eyeSquintRight" },
            { StandardBlendshape.EyeWideLeft, "eyeWideLeft" },
            { StandardBlendshape.EyeWideRight, "eyeWideRight" },

            { StandardBlendshape.BrowDownLeft, "browDownLeft" },
            { StandardBlendshape.BrowDownRight, "browDownRight" },
            { StandardBlendshape.BrowInnerUp, "browInnerUp" },
            { StandardBlendshape.BrowOuterUpLeft, "browOuterUpLeft" },
            { StandardBlendshape.BrowOuterUpRight, "browOuterUpRight" },

            { StandardBlendshape.CheekPuff, "cheekPuff" },
            { StandardBlendshape.CheekSquintLeft, "cheekSquintLeft" },
            { StandardBlendshape.CheekSquintRight, "cheekSquintRight" },
            { StandardBlendshape.NoseSneerLeft, "noseSneerLeft" },
            { StandardBlendshape.NoseSneerRight, "noseSneerRight" },

            { StandardBlendshape.JawForward, "jawForward" },
            { StandardBlendshape.JawLeft, "jawLeft" },
            { StandardBlendshape.JawRight, "jawRight" },
            { StandardBlendshape.JawOpen, "jawOpen" },

            { StandardBlendshape.MouthClose, "mouthClose" },
            { StandardBlendshape.MouthFunnel, "mouthFunnel" },
            { StandardBlendshape.MouthPucker, "mouthPucker" },
            { StandardBlendshape.MouthLeft, "mouthLeft" },
            { StandardBlendshape.MouthRight, "mouthRight" },
            { StandardBlendshape.MouthSmileLeft, "mouthSmileLeft" },
            { StandardBlendshape.MouthSmileRight, "mouthSmileRight" },
            { StandardBlendshape.MouthFrownLeft, "mouthFrownLeft" },
            { StandardBlendshape.MouthFrownRight, "mouthFrownRight" },
            { StandardBlendshape.MouthDimpleLeft, "mouthDimpleLeft" },
            { StandardBlendshape.MouthDimpleRight, "mouthDimpleRight" },
            { StandardBlendshape.MouthStretchLeft, "mouthStretchLeft" },
            { StandardBlendshape.MouthStretchRight, "mouthStretchRight" },
            { StandardBlendshape.MouthRollLower, "mouthRollLower" },
            { StandardBlendshape.MouthRollUpper, "mouthRollUpper" },
            { StandardBlendshape.MouthShrugLower, "mouthShrugLower" },
            { StandardBlendshape.MouthShrugUpper, "mouthShrugUpper" },
            { StandardBlendshape.MouthPressLeft, "mouthPressLeft" },
            { StandardBlendshape.MouthPressRight, "mouthPressRight" },
            { StandardBlendshape.MouthLowerDownLeft, "mouthLowerDownLeft" },
            { StandardBlendshape.MouthLowerDownRight, "mouthLowerDownRight" },
            { StandardBlendshape.MouthUpperUpLeft, "mouthUpperUpLeft" },
            { StandardBlendshape.MouthUpperUpRight, "mouthUpperUpRight" },

            { StandardBlendshape.TongueOut, "tongueOut" }
        };

        private static readonly Dictionary<StandardBlendshape, string> CC3 = new()
        {
            { StandardBlendshape.EyeBlinkLeft, "Eye_Blink_L" },
            { StandardBlendshape.EyeBlinkRight, "Eye_Blink_R" },
            { StandardBlendshape.EyeLookDownLeft, "Eye_L_Look_Down" },
            { StandardBlendshape.EyeLookDownRight, "Eye_R_Look_Down" },
            { StandardBlendshape.EyeLookInLeft, "Eye_L_Look_R" },
            { StandardBlendshape.EyeLookInRight, "Eye_R_Look_L" },
            { StandardBlendshape.EyeLookOutLeft, "Eye_L_Look_L" },
            { StandardBlendshape.EyeLookOutRight, "Eye_R_Look_R" },
            { StandardBlendshape.EyeLookUpLeft, "Eye_L_Look_Up" },
            { StandardBlendshape.EyeLookUpRight, "Eye_R_Look_Up" },
            { StandardBlendshape.EyeSquintLeft, "Eye_Squint_L" },
            { StandardBlendshape.EyeSquintRight, "Eye_Squint_R" },
            { StandardBlendshape.EyeWideLeft, "Eye_Wide_L" },
            { StandardBlendshape.EyeWideRight, "Eye_Wide_R" },

            { StandardBlendshape.BrowDownLeft, "Brow_Drop_L" },
            { StandardBlendshape.BrowDownRight, "Brow_Drop_R" },
            { StandardBlendshape.BrowInnerUp, "Brow_Raise_Inner_L" },
            { StandardBlendshape.BrowOuterUpLeft, "Brow_Raise_Outer_L" },
            { StandardBlendshape.BrowOuterUpRight, "Brow_Raise_Outer_R" },

            { StandardBlendshape.CheekPuff, "Cheek_Puff" },
            { StandardBlendshape.CheekSquintLeft, "Cheek_Raise_L" },
            { StandardBlendshape.CheekSquintRight, "Cheek_Raise_R" },
            { StandardBlendshape.NoseSneerLeft, "Nose_Sneer_L" },
            { StandardBlendshape.NoseSneerRight, "Nose_Sneer_R" },

            { StandardBlendshape.JawForward, "V_Jaw_Forward" },
            { StandardBlendshape.JawOpen, "V_Open" },

            { StandardBlendshape.MouthSmileLeft, "Mouth_Smile_L" },
            { StandardBlendshape.MouthSmileRight, "Mouth_Smile_R" },
            { StandardBlendshape.MouthFrownLeft, "Mouth_Frown_L" },
            { StandardBlendshape.MouthFrownRight, "Mouth_Frown_R" },
            { StandardBlendshape.MouthDimpleLeft, "Mouth_Dimple_L" },
            { StandardBlendshape.MouthDimpleRight, "Mouth_Dimple_R" },
            { StandardBlendshape.MouthStretchLeft, "Mouth_Stretch_L" },
            { StandardBlendshape.MouthStretchRight, "Mouth_Stretch_R" },
            { StandardBlendshape.MouthPucker, "V_Tight_O" },
            { StandardBlendshape.MouthFunnel, "V_Wide" }
        };

        // Reallusion CC4 Extended facial profile ; strict superset of the CC3 table with
        // additional per-side shapes (press, stretch, cheek raise, brow raise, etc.) that
        // ship with Reallusion's Extended facial profile. Entries are aligned with the
        // semantic key list exposed to embodiment modules; missing blendshapes on a given
        // mesh fall back gracefully via the shared resolver.
        private static readonly Dictionary<StandardBlendshape, string> CC4Extended = new()
        {
            { StandardBlendshape.EyeBlinkLeft, "Eye_Blink_L" },
            { StandardBlendshape.EyeBlinkRight, "Eye_Blink_R" },
            { StandardBlendshape.EyeLookDownLeft, "Eye_L_Look_Down" },
            { StandardBlendshape.EyeLookDownRight, "Eye_R_Look_Down" },
            { StandardBlendshape.EyeLookInLeft, "Eye_L_Look_R" },
            { StandardBlendshape.EyeLookInRight, "Eye_R_Look_L" },
            { StandardBlendshape.EyeLookOutLeft, "Eye_L_Look_L" },
            { StandardBlendshape.EyeLookOutRight, "Eye_R_Look_R" },
            { StandardBlendshape.EyeLookUpLeft, "Eye_L_Look_Up" },
            { StandardBlendshape.EyeLookUpRight, "Eye_R_Look_Up" },
            { StandardBlendshape.EyeSquintLeft, "Eye_Squint_L" },
            { StandardBlendshape.EyeSquintRight, "Eye_Squint_R" },
            { StandardBlendshape.EyeWideLeft, "Eye_Wide_L" },
            { StandardBlendshape.EyeWideRight, "Eye_Wide_R" },
            { StandardBlendshape.EyeUpperLidDownLeft, "Eyelash_Upper_Down_L" },
            { StandardBlendshape.EyeUpperLidDownRight, "Eyelash_Upper_Down_R" },
            { StandardBlendshape.EyeUpperLidUpLeft, "Eyelash_Upper_Up_L" },
            { StandardBlendshape.EyeUpperLidUpRight, "Eyelash_Upper_Up_R" },
            { StandardBlendshape.EyeLowerLidUpLeft, "Eyelash_Lower_Up_L" },
            { StandardBlendshape.EyeLowerLidUpRight, "Eyelash_Lower_Up_R" },

            { StandardBlendshape.BrowDownLeft, "Brow_Drop_L" },
            { StandardBlendshape.BrowDownRight, "Brow_Drop_R" },
            { StandardBlendshape.BrowInnerUp, "Brow_Raise_Inner_L" },
            { StandardBlendshape.BrowOuterUpLeft, "Brow_Raise_Outer_L" },
            { StandardBlendshape.BrowOuterUpRight, "Brow_Raise_Outer_R" },

            { StandardBlendshape.CheekPuff, "Cheek_Puff_L" },
            { StandardBlendshape.CheekSquintLeft, "Cheek_Raise_L" },
            { StandardBlendshape.CheekSquintRight, "Cheek_Raise_R" },
            { StandardBlendshape.NoseSneerLeft, "Nose_Sneer_L" },
            { StandardBlendshape.NoseSneerRight, "Nose_Sneer_R" },

            { StandardBlendshape.JawForward, "Jaw_Forward" },
            { StandardBlendshape.JawLeft, "Jaw_L" },
            { StandardBlendshape.JawRight, "Jaw_R" },
            { StandardBlendshape.JawOpen, "V_Open" },

            { StandardBlendshape.MouthClose, "Mouth_Close" },
            { StandardBlendshape.MouthFunnel, "V_Wide" },
            { StandardBlendshape.MouthPucker, "V_Tight_O" },
            { StandardBlendshape.MouthLeft, "Mouth_L" },
            { StandardBlendshape.MouthRight, "Mouth_R" },
            { StandardBlendshape.MouthSmileLeft, "Mouth_Smile_L" },
            { StandardBlendshape.MouthSmileRight, "Mouth_Smile_R" },
            { StandardBlendshape.MouthFrownLeft, "Mouth_Frown_L" },
            { StandardBlendshape.MouthFrownRight, "Mouth_Frown_R" },
            { StandardBlendshape.MouthDimpleLeft, "Mouth_Dimple_L" },
            { StandardBlendshape.MouthDimpleRight, "Mouth_Dimple_R" },
            { StandardBlendshape.MouthStretchLeft, "Mouth_Stretch_L" },
            { StandardBlendshape.MouthStretchRight, "Mouth_Stretch_R" },
            { StandardBlendshape.MouthRollLower, "Mouth_Roll_Lower" },
            { StandardBlendshape.MouthRollUpper, "Mouth_Roll_Upper" },
            { StandardBlendshape.MouthShrugLower, "Mouth_Shrug_Lower" },
            { StandardBlendshape.MouthShrugUpper, "Mouth_Shrug_Upper" },
            { StandardBlendshape.MouthPressLeft, "Mouth_Press_L" },
            { StandardBlendshape.MouthPressRight, "Mouth_Press_R" },
            { StandardBlendshape.MouthLowerDownLeft, "Mouth_Down_Lower_L" },
            { StandardBlendshape.MouthLowerDownRight, "Mouth_Down_Lower_R" },
            { StandardBlendshape.MouthUpperUpLeft, "Mouth_Up_Upper_L" },
            { StandardBlendshape.MouthUpperUpRight, "Mouth_Up_Upper_R" },

            { StandardBlendshape.TongueOut, "Tongue_Out" }
        };

        // MetaHuman native rig uses control board names which frequently appear as suffixes;
        // authoritative mapping is resource-intensive (requires DNA asset parsing), so this
        // table is populated with the most common glue names for blend-shape-baked rigs.
        private static readonly Dictionary<StandardBlendshape, string> MetaHuman = new()
        {
            { StandardBlendshape.EyeBlinkLeft, "CTRL_L_eye_blink" },
            { StandardBlendshape.EyeBlinkRight, "CTRL_R_eye_blink" },
            { StandardBlendshape.BrowInnerUp, "CTRL_C_brow_up" },
            { StandardBlendshape.JawOpen, "CTRL_C_jaw" },
            { StandardBlendshape.MouthSmileLeft, "CTRL_L_mouth_smile" },
            { StandardBlendshape.MouthSmileRight, "CTRL_R_mouth_smile" },
            { StandardBlendshape.MouthFrownLeft, "CTRL_L_mouth_down" },
            { StandardBlendshape.MouthFrownRight, "CTRL_R_mouth_down" }
        };
    }
}
