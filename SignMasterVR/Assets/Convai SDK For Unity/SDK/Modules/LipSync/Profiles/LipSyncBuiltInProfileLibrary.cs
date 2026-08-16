using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;

namespace Convai.Modules.LipSync.Profiles
{
    /// <summary>
    ///     Built-in source blendshape catalogs for SDK-shipped profiles.
    /// </summary>
    public static class LipSyncBuiltInProfileLibrary
    {
        public static readonly IReadOnlyList<string> ARKitBlendshapes = new[]
        {
            // Eye controls - Left (indices 0-6)
            "EyeBlinkLeft", "EyeLookDownLeft", "EyeLookInLeft", "EyeLookOutLeft", "EyeLookUpLeft", "EyeSquintLeft",
            "EyeWideLeft",
            // Eye controls - Right (indices 7-13)
            "EyeBlinkRight", "EyeLookDownRight", "EyeLookInRight", "EyeLookOutRight", "EyeLookUpRight",
            "EyeSquintRight", "EyeWideRight",
            // Jaw controls (indices 14-17). Order is Forward, Right, Left, Open to match Unreal.
            "JawForward", "JawRight", "JawLeft", "JawOpen",
            // Mouth controls (indices 18-40)
            "MouthClose", "MouthFunnel", "MouthPucker", "MouthRight", "MouthLeft", "MouthSmileLeft", "MouthSmileRight",
            "MouthFrownLeft", "MouthFrownRight", "MouthDimpleLeft", "MouthDimpleRight", "MouthStretchLeft",
            "MouthStretchRight", "MouthRollLower", "MouthRollUpper", "MouthShrugLower", "MouthShrugUpper",
            "MouthPressLeft", "MouthPressRight", "MouthLowerDownLeft", "MouthLowerDownRight", "MouthUpperUpLeft",
            "MouthUpperUpRight",
            // Brow controls (indices 41-45)
            "BrowDownLeft", "BrowDownRight", "BrowInnerUp", "BrowOuterUpLeft", "BrowOuterUpRight",
            // Cheek controls (indices 46-48)
            "CheekPuff", "CheekSquintLeft", "CheekSquintRight",
            // Nose controls (indices 49-50)
            "NoseSneerLeft", "NoseSneerRight",
            // Tongue (index 51)
            "TongueOut",
            // Head rotation (indices 52-54) - Extended ARKit
            "HeadYaw", "HeadPitch", "HeadRoll",
            // Left eye rotation (indices 55-57) - Extended ARKit
            "LeftEyeYaw", "LeftEyePitch", "LeftEyeRoll",
            // Right eye rotation (indices 58-60) - Extended ARKit
            "RightEyeYaw", "RightEyePitch", "RightEyeRoll"
        };

        public static readonly IReadOnlyList<string> MetaHumanBlendshapes = new[]
        {
            // Brow controls
            "CTRL_expressions_browDownL", "CTRL_expressions_browDownR", "CTRL_expressions_browLateralL",
            "CTRL_expressions_browLateralR", "CTRL_expressions_browRaiseInL", "CTRL_expressions_browRaiseInR",
            "CTRL_expressions_browRaiseOuterL", "CTRL_expressions_browRaiseOuterR",
            // Ear controls
            "CTRL_expressions_earUpL", "CTRL_expressions_earUpR",
            // Eye controls
            "CTRL_expressions_eyeBlinkL", "CTRL_expressions_eyeBlinkR", "CTRL_expressions_eyeCheekRaiseL",
            "CTRL_expressions_eyeCheekRaiseR", "CTRL_expressions_eyeFaceScrunchL", "CTRL_expressions_eyeFaceScrunchR",
            // Eye lid controls
            "CTRL_expressions_eyeLidPressL", "CTRL_expressions_eyeLidPressR", "CTRL_expressions_eyeLookDownL",
            "CTRL_expressions_eyeLookDownR", "CTRL_expressions_eyeLookLeftL", "CTRL_expressions_eyeLookLeftR",
            "CTRL_expressions_eyeLookRightL", "CTRL_expressions_eyeLookRightR", "CTRL_expressions_eyeLookUpL",
            "CTRL_expressions_eyeLookUpR", "CTRL_expressions_eyeLowerLidDownL", "CTRL_expressions_eyeLowerLidDownR",
            "CTRL_expressions_eyeLowerLidUpL", "CTRL_expressions_eyeLowerLidUpR",
            "CTRL_expressions_eyeParallelLookDirection",
            // Eye pupil controls
            "CTRL_expressions_eyePupilNarrowL", "CTRL_expressions_eyePupilNarrowR", "CTRL_expressions_eyePupilWideL",
            "CTRL_expressions_eyePupilWideR", "CTRL_expressions_eyeRelaxL", "CTRL_expressions_eyeRelaxR",
            "CTRL_expressions_eyeSquintInnerL", "CTRL_expressions_eyeSquintInnerR", "CTRL_expressions_eyeUpperLidUpL",
            "CTRL_expressions_eyeUpperLidUpR", "CTRL_expressions_eyeWidenL", "CTRL_expressions_eyeWidenR",
            // Eyelash controls
            "CTRL_expressions_eyelashesDownINL", "CTRL_expressions_eyelashesDownINR",
            "CTRL_expressions_eyelashesDownOUTL", "CTRL_expressions_eyelashesDownOUTR",
            "CTRL_expressions_eyelashesUpINL", "CTRL_expressions_eyelashesUpINR", "CTRL_expressions_eyelashesUpOUTL",
            "CTRL_expressions_eyelashesUpOUTR",
            // Jaw controls
            "CTRL_expressions_jawBack", "CTRL_expressions_jawChinCompressL", "CTRL_expressions_jawChinCompressR",
            "CTRL_expressions_jawChinRaiseDL", "CTRL_expressions_jawChinRaiseDR", "CTRL_expressions_jawChinRaiseUL",
            "CTRL_expressions_jawChinRaiseUR", "CTRL_expressions_jawClenchL", "CTRL_expressions_jawClenchR",
            "CTRL_expressions_jawFwd", "CTRL_expressions_jawLeft", "CTRL_expressions_jawOpen",
            "CTRL_expressions_jawOpenExtreme", "CTRL_expressions_jawRight",
            // Mouth cheek controls
            "CTRL_expressions_mouthCheekBlowL", "CTRL_expressions_mouthCheekBlowR", "CTRL_expressions_mouthCheekSuckL",
            "CTRL_expressions_mouthCheekSuckR",
            // Mouth corner controls
            "CTRL_expressions_mouthCornerDepressL", "CTRL_expressions_mouthCornerDepressR",
            "CTRL_expressions_mouthCornerDownL", "CTRL_expressions_mouthCornerDownR",
            "CTRL_expressions_mouthCornerNarrowL", "CTRL_expressions_mouthCornerNarrowR",
            "CTRL_expressions_mouthCornerPullL", "CTRL_expressions_mouthCornerPullR",
            "CTRL_expressions_mouthCornerRounderDL", "CTRL_expressions_mouthCornerRounderDR",
            "CTRL_expressions_mouthCornerRounderUL", "CTRL_expressions_mouthCornerRounderUR",
            "CTRL_expressions_mouthCornerSharpenDL", "CTRL_expressions_mouthCornerSharpenDR",
            "CTRL_expressions_mouthCornerSharpenUL", "CTRL_expressions_mouthCornerSharpenUR",
            "CTRL_expressions_mouthCornerUpL", "CTRL_expressions_mouthCornerUpR", "CTRL_expressions_mouthCornerWideL",
            "CTRL_expressions_mouthCornerWideR",
            // Mouth dimple controls
            "CTRL_expressions_mouthDimpleL", "CTRL_expressions_mouthDimpleR",
            // Mouth position controls
            "CTRL_expressions_mouthDown", "CTRL_expressions_mouthFunnelDL", "CTRL_expressions_mouthFunnelDR",
            "CTRL_expressions_mouthFunnelUL", "CTRL_expressions_mouthFunnelUR", "CTRL_expressions_mouthLeft",
            // Mouth lips blow controls
            "CTRL_expressions_mouthLipsBlowL", "CTRL_expressions_mouthLipsBlowR",
            // Mouth lips press controls
            "CTRL_expressions_mouthLipsPressL", "CTRL_expressions_mouthLipsPressR",
            // Mouth lips pull controls
            "CTRL_expressions_mouthLipsPullDL", "CTRL_expressions_mouthLipsPullDR", "CTRL_expressions_mouthLipsPullUL",
            "CTRL_expressions_mouthLipsPullUR",
            // Mouth lips purse controls
            "CTRL_expressions_mouthLipsPurseDL", "CTRL_expressions_mouthLipsPurseDR",
            "CTRL_expressions_mouthLipsPurseUL", "CTRL_expressions_mouthLipsPurseUR",
            // Mouth lips push controls
            "CTRL_expressions_mouthLipsPushDL", "CTRL_expressions_mouthLipsPushDR", "CTRL_expressions_mouthLipsPushUL",
            "CTRL_expressions_mouthLipsPushUR",
            // Mouth lips sticky controls
            "CTRL_expressions_mouthLipsStickyLPh1", "CTRL_expressions_mouthLipsStickyLPh2",
            "CTRL_expressions_mouthLipsStickyLPh3", "CTRL_expressions_mouthLipsStickyRPh1",
            "CTRL_expressions_mouthLipsStickyRPh2", "CTRL_expressions_mouthLipsStickyRPh3",
            // Mouth lips thick controls
            "CTRL_expressions_mouthLipsThickDL", "CTRL_expressions_mouthLipsThickDR",
            "CTRL_expressions_mouthLipsThickInwardDL", "CTRL_expressions_mouthLipsThickInwardDR",
            "CTRL_expressions_mouthLipsThickInwardUL", "CTRL_expressions_mouthLipsThickInwardUR",
            "CTRL_expressions_mouthLipsThickUL", "CTRL_expressions_mouthLipsThickUR",
            // Mouth lips thin controls
            "CTRL_expressions_mouthLipsThinDL", "CTRL_expressions_mouthLipsThinDR",
            "CTRL_expressions_mouthLipsThinInwardDL", "CTRL_expressions_mouthLipsThinInwardDR",
            "CTRL_expressions_mouthLipsThinInwardUL", "CTRL_expressions_mouthLipsThinInwardUR",
            "CTRL_expressions_mouthLipsThinUL", "CTRL_expressions_mouthLipsThinUR",
            // Mouth lips tighten controls
            "CTRL_expressions_mouthLipsTightenDL", "CTRL_expressions_mouthLipsTightenDR",
            "CTRL_expressions_mouthLipsTightenUL", "CTRL_expressions_mouthLipsTightenUR",
            // Mouth lips together controls
            "CTRL_expressions_mouthLipsTogetherDL", "CTRL_expressions_mouthLipsTogetherDR",
            "CTRL_expressions_mouthLipsTogetherUL", "CTRL_expressions_mouthLipsTogetherUR",
            // Mouth lips towards controls
            "CTRL_expressions_mouthLipsTowardsDL", "CTRL_expressions_mouthLipsTowardsDR",
            "CTRL_expressions_mouthLipsTowardsUL", "CTRL_expressions_mouthLipsTowardsUR",
            // Mouth lower lip controls
            "CTRL_expressions_mouthLowerLipBiteL", "CTRL_expressions_mouthLowerLipBiteR",
            "CTRL_expressions_mouthLowerLipDepressL", "CTRL_expressions_mouthLowerLipDepressR",
            "CTRL_expressions_mouthLowerLipRollInL", "CTRL_expressions_mouthLowerLipRollInR",
            "CTRL_expressions_mouthLowerLipRollOutL", "CTRL_expressions_mouthLowerLipRollOutR",
            "CTRL_expressions_mouthLowerLipShiftLeft", "CTRL_expressions_mouthLowerLipShiftRight",
            "CTRL_expressions_mouthLowerLipTowardsTeethL", "CTRL_expressions_mouthLowerLipTowardsTeethR",
            // Mouth press controls
            "CTRL_expressions_mouthPressDL", "CTRL_expressions_mouthPressDR", "CTRL_expressions_mouthPressUL",
            "CTRL_expressions_mouthPressUR", "CTRL_expressions_mouthRight",
            // Mouth sharp corner pull controls
            "CTRL_expressions_mouthSharpCornerPullL", "CTRL_expressions_mouthSharpCornerPullR",
            // Mouth sticky controls
            "CTRL_expressions_mouthStickyDC", "CTRL_expressions_mouthStickyDINL", "CTRL_expressions_mouthStickyDINR",
            "CTRL_expressions_mouthStickyDOUTL", "CTRL_expressions_mouthStickyDOUTR", "CTRL_expressions_mouthStickyUC",
            "CTRL_expressions_mouthStickyUINL", "CTRL_expressions_mouthStickyUINR", "CTRL_expressions_mouthStickyUOUTL",
            "CTRL_expressions_mouthStickyUOUTR",
            // Mouth stretch controls
            "CTRL_expressions_mouthStretchL", "CTRL_expressions_mouthStretchLipsCloseL",
            "CTRL_expressions_mouthStretchLipsCloseR", "CTRL_expressions_mouthStretchR", "CTRL_expressions_mouthUp",
            // Mouth upper lip controls
            "CTRL_expressions_mouthUpperLipBiteL", "CTRL_expressions_mouthUpperLipBiteR",
            "CTRL_expressions_mouthUpperLipRaiseL", "CTRL_expressions_mouthUpperLipRaiseR",
            "CTRL_expressions_mouthUpperLipRollInL", "CTRL_expressions_mouthUpperLipRollInR",
            "CTRL_expressions_mouthUpperLipRollOutL", "CTRL_expressions_mouthUpperLipRollOutR",
            "CTRL_expressions_mouthUpperLipShiftLeft", "CTRL_expressions_mouthUpperLipShiftRight",
            "CTRL_expressions_mouthUpperLipTowardsTeethL", "CTRL_expressions_mouthUpperLipTowardsTeethR",
            // Neck controls
            "CTRL_expressions_neckDigastricDown", "CTRL_expressions_neckDigastricUp",
            "CTRL_expressions_neckMastoidContractL", "CTRL_expressions_neckMastoidContractR",
            "CTRL_expressions_neckStretchL", "CTRL_expressions_neckStretchR", "CTRL_expressions_neckSwallowPh1",
            "CTRL_expressions_neckSwallowPh2", "CTRL_expressions_neckSwallowPh3", "CTRL_expressions_neckSwallowPh4",
            "CTRL_expressions_neckThroatDown", "CTRL_expressions_neckThroatExhale", "CTRL_expressions_neckThroatInhale",
            "CTRL_expressions_neckThroatUp",
            // Nose controls
            "CTRL_expressions_noseNasolabialDeepenL", "CTRL_expressions_noseNasolabialDeepenR",
            "CTRL_expressions_noseNostrilCompressL", "CTRL_expressions_noseNostrilCompressR",
            "CTRL_expressions_noseNostrilDepressL", "CTRL_expressions_noseNostrilDepressR",
            "CTRL_expressions_noseNostrilDilateL", "CTRL_expressions_noseNostrilDilateR",
            "CTRL_expressions_noseWrinkleL", "CTRL_expressions_noseWrinkleR", "CTRL_expressions_noseWrinkleUpperL",
            "CTRL_expressions_noseWrinkleUpperR",
            // Teeth controls
            "CTRL_expressions_teethBackD", "CTRL_expressions_teethBackU", "CTRL_expressions_teethDownD",
            "CTRL_expressions_teethDownU", "CTRL_expressions_teethFwdD", "CTRL_expressions_teethFwdU",
            "CTRL_expressions_teethLeftD", "CTRL_expressions_teethLeftU", "CTRL_expressions_teethRightD",
            "CTRL_expressions_teethRightU", "CTRL_expressions_teethUpD", "CTRL_expressions_teethUpU",
            // Tongue controls
            "CTRL_expressions_tongueBendDown", "CTRL_expressions_tongueBendUp", "CTRL_expressions_tongueDown",
            "CTRL_expressions_tongueIn", "CTRL_expressions_tongueLeft", "CTRL_expressions_tongueNarrow",
            "CTRL_expressions_tongueOut", "CTRL_expressions_tonguePress", "CTRL_expressions_tongueRight",
            "CTRL_expressions_tongueRoll", "CTRL_expressions_tongueThick", "CTRL_expressions_tongueThin",
            "CTRL_expressions_tongueTipDown", "CTRL_expressions_tongueTipLeft", "CTRL_expressions_tongueTipRight",
            "CTRL_expressions_tongueTipUp", "CTRL_expressions_tongueTwistLeft", "CTRL_expressions_tongueTwistRight",
            "CTRL_expressions_tongueUp", "CTRL_expressions_tongueWide"
        };

        public static readonly IReadOnlyList<string> CC4ExtendedBlendshapes = new[]
        {
            "Mouth_Drop_Lower", "Mouth_Up_Upper_L", "Mouth_Up_Upper_R", "Mouth_Contract", "Tongue_Out", "Tongue_In",
            "Tongue_Up", "Tongue_Down", "Tongue_Mid_Up", "Tongue_Tip_Up", "Tongue_Tip_Down", "Tongue_Narrow",
            "Tongue_Wide", "Tongue_Roll", "Tongue_L", "Tongue_R", "Tongue_Tip_L", "Tongue_Tip_R", "Tongue_Twist_L",
            "Tongue_Twist_R", "Tongue_Bulge_L", "Tongue_Bulge_R", "Tongue_Extend", "Tongue_Enlarge", "Jaw_Forward",
            "Jaw_L", "Jaw_R", "Jaw_Up", "Jaw_Down", "Head_Turn_Up", "Head_Turn_Down", "Head_Tilt_L", "Head_L",
            "Head_Backward", "Brow_Raise_Inner_L", "Brow_Raise_Inner_R", "Brow_Raise_Outer_L", "Brow_Raise_Outer_R",
            "Brow_Drop_L", "Brow_Drop_R", "Brow_Compress_L", "Brow_Compress_R", "Eye_Blink_L", "Eye_Blink_R",
            "Eye_Squint_L", "Eye_Squint_R", "Eye_Wide_L", "Eye_Wide_R", "Eye_L_Look_L", "Eye_R_Look_L",
            "Eye_L_Look_R", "Eye_R_Look_R", "Eye_L_Look_Up", "Eye_R_Look_Up", "Eye_L_Look_Down", "Eye_R_Look_Down",
            "Eyelash_Upper_Up_L", "Eyelash_Upper_Down_L", "Eyelash_Upper_Up_R", "Eyelash_Upper_Down_R",
            "Eyelash_Lower_Up_L", "Eyelash_Lower_Down_L", "Eyelash_Lower_Up_R", "Eyelash_Lower_Down_R", "Ear_Up_L",
            "Ear_Up_R", "Ear_Down_L", "Ear_Down_R", "Ear_Out_L", "Ear_Out_R", "Nose_Sneer_L", "Nose_Sneer_R",
            "Nose_Nostril_Raise_L", "Nose_Nostril_Raise_R", "Nose_Nostril_Dilate_L", "Nose_Nostril_Dilate_R",
            "Nose_Crease_L", "Nose_Crease_R", "Nose_Nostril_Down_L", "Nose_Nostril_Down_R", "Nose_Nostril_In_L",
            "Nose_Nostril_In_R", "Nose_Tip_L", "Nose_Tip_R", "Nose_Tip_Up", "Nose_Tip_Down", "Cheek_Raise_L",
            "Cheek_Raise_R", "Cheek_Suck_L", "Cheek_Suck_R", "Cheek_Puff_L", "Cheek_Puff_R", "Mouth_Smile_L",
            "Mouth_Smile_R", "Mouth_Smile_Sharp_L", "Mouth_Smile_Sharp_R", "Mouth_Frown_L", "Mouth_Frown_R",
            "Mouth_Stretch_L", "Mouth_Stretch_R", "Mouth_Dimple_L", "Mouth_Dimple_R", "Mouth_Press_L",
            "Mouth_Press_R", "Mouth_Tighten_L", "Mouth_Tighten_R", "Mouth_Blow_L", "Mouth_Blow_R",
            "Mouth_Pucker_Up_L", "Mouth_Pucker_Up_R", "Mouth_Pucker_Down_L", "Mouth_Pucker_Down_R",
            "Mouth_Funnel_Up_L", "Mouth_Funnel_Up_R", "Mouth_Funnel_Down_L", "Mouth_Funnel_Down_R",
            "Mouth_Roll_In_Upper_L", "Mouth_Roll_In_Upper_R", "Mouth_Roll_In_Lower_L", "Mouth_Roll_In_Lower_R",
            "Mouth_Roll_Out_Upper_L", "Mouth_Roll_Out_Upper_R", "Mouth_Roll_Out_Lower_L", "Mouth_Roll_Out_Lower_R",
            "Mouth_Push_Upper_L", "Mouth_Push_Upper_R", "Mouth_Push_Lower_L", "Mouth_Push_Lower_R",
            "Mouth_Pull_Upper_L", "Mouth_Pull_Upper_R", "Mouth_Pull_Lower_L", "Mouth_Pull_Lower_R", "Mouth_Up",
            "Mouth_Down", "Mouth_L", "Mouth_R", "Mouth_Upper_L", "Mouth_Upper_R", "Mouth_Lower_L", "Mouth_Lower_R",
            "Mouth_Shrug_Upper", "Mouth_Shrug_Lower", "Mouth_Drop_Upper", "Mouth_Down_Lower_L",
            "Mouth_Down_Lower_R", "Mouth_Chin_Up", "Mouth_Close", "Jaw_Open", "Jaw_Backward", "Neck_Swallow_Up",
            "Neck_Swallow_Down", "Neck_Tighten_L", "Neck_Tighten_R", "Head_Turn_L", "Head_Turn_R", "Head_Tilt_R",
            "Head_R", "Head_Forward", "eye_shape_L", "eye_shape_R", "eye_shape_angry_L", "eye_shape_angry_R",
            "double_eyelid_up_L", "double_eyelid_up_R", "lips_smooth_lower", "lips_shape_lower",
            "eyes_smile_shape_L", "eyes_smile_shape_R", "smile_coner_shape_L", "smile_coner_shape_R"
        };

        private static readonly Dictionary<string, IReadOnlyList<string>> BuiltInBlendshapeMap =
            new(StringComparer.Ordinal)
            {
                [LipSyncProfileId.ARKitValue] = ARKitBlendshapes,
                [LipSyncProfileId.MetaHumanValue] = MetaHumanBlendshapes,
                [LipSyncProfileId.Cc4ExtendedValue] = CC4ExtendedBlendshapes
            };

        public static bool TryGetSourceBlendshapeNames(LipSyncProfileId profileId, out IReadOnlyList<string> names) =>
            BuiltInBlendshapeMap.TryGetValue(profileId.Value, out names);

        public static IReadOnlyList<string> GetSourceBlendshapeNamesOrEmpty(LipSyncProfileId profileId)
        {
            return TryGetSourceBlendshapeNames(profileId, out IReadOnlyList<string> names)
                ? names
                : Array.Empty<string>();
        }
    }
}
