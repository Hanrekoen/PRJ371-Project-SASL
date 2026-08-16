using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Applies the mechanical repairs a <see cref="BodyAnimationTroubleshooterFinding" /> can
    ///     offer as a one-click button. Every surface that renders findings — the set inspector, the
    ///     controller inspector, the Troubleshooter window — calls through here, so a fix behaves and
    ///     is described identically wherever it is pressed.
    /// </summary>
    /// <remarks>
    ///     Fixes that need a character (assigning default content, adding movement) live in the setup
    ///     service; this class owns the ones that operate purely on an animation set asset.
    /// </remarks>
    internal static class BodyAnimationFixes
    {
        /// <summary>Asset path of the generated upper-body overlay mask, alongside its owning set.</summary>
        private const string GeneratedMaskSuffix = "_UpperBodyMask.mask";

        /// <summary>
        ///     Button text for a set-scoped fix, or <c>null</c> when <paramref name="fix" /> is not
        ///     one this class can apply (the caller then renders the finding without a button).
        /// </summary>
        internal static string DescribeSetFix(BodyAnimationFixId fix) => fix switch
        {
            BodyAnimationFixId.GenerateUpperBodyMask => "Generate Mask",
            BodyAnimationFixId.AnalyzeClipMetadata => "Measure Clips",
            _ => null
        };

        /// <summary>
        ///     Button text for a config-scoped fix, or <c>null</c> when <paramref name="fix" /> is
        ///     not one this class can apply to a config asset.
        /// </summary>
        internal static string DescribeConfigFix(BodyAnimationFixId fix) => fix switch
        {
            BodyAnimationFixId.EnableBeatGestures => "Turn On",
            BodyAnimationFixId.EnableAmbientActivities => "Turn On",
            BodyAnimationFixId.EnableReferentialGestures => "Turn On",
            _ => null
        };

        /// <summary>
        ///     Applies a config-scoped fix — turning on the switch that plays content the set
        ///     already authors. Unknown or set/character-scoped ids are ignored.
        /// </summary>
        /// <remarks>
        ///     Writes through <see cref="SerializedObject" /> with an <see cref="Undo" /> record,
        ///     like every other fix here, so the change is a single undo step and the inspector
        ///     showing the same asset refreshes with it. A config asset can be shared by several
        ///     characters — the same hazard the personality controls carry — so the log names the
        ///     asset that was changed.
        /// </remarks>
        /// <param name="owner">
        ///     The character the fix was offered for. Present, a config the SDK ships is copied for
        ///     that character before the feature is turned on - a one-click fix that reports success
        ///     while writing somewhere the write cannot survive is worse than no fix at all.
        /// </param>
        internal static void ApplyToConfig(
            ConvaiBodyAnimationConfig config, BodyAnimationFixId fix, ConvaiBodyAnimationController owner = null)
        {
            string field = fix switch
            {
                BodyAnimationFixId.EnableBeatGestures => "_enableBeatGestures",
                BodyAnimationFixId.EnableAmbientActivities => "_enableAmbientActivities",
                BodyAnimationFixId.EnableReferentialGestures => "_enableReferentialGestures",
                _ => null
            };
            if (config == null || field == null) return;

            config = BodyAnimationPersonality.EnsureWritable(config, owner);
            if (config == null) return;

            Undo.RecordObject(config, "Enable Body Animation Feature");
            var serialized = new SerializedObject(config);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) return;

            property.boolValue = true;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);

            ConvaiLogger.Info(
                $"[Body Animation] Turned on '{field}' in config '{config.name}'. Any character using " +
                "this config asset is affected.",
                LogCategory.Animation);
        }

        /// <summary>Applies a set-scoped fix. Unknown or character-scoped ids are ignored.</summary>
        internal static void ApplyToSet(ConvaiBodyAnimationSet set, BodyAnimationFixId fix)
        {
            if (set == null) return;

            switch (fix)
            {
                case BodyAnimationFixId.GenerateUpperBodyMask:
                    GenerateUpperBodyMask(set);
                    break;

                case BodyAnimationFixId.AnalyzeClipMetadata:
                    ClipMotionAnalyzer.AnalyzeSet(set);
                    break;
            }
        }

        /// <summary>
        ///     Builds the standard upper-body overlay mask (spine, head, arms, fingers, hand IK — legs
        ///     and root excluded so the overlay can never take the character's locomotion over) and
        ///     assigns it to <paramref name="set" />.
        /// </summary>
        /// <remarks>
        ///     The mask is written next to the set asset rather than into a fixed SDK folder, so a
        ///     project can carry one per character archetype and a package update never overwrites
        ///     authored content. An existing mask at the same path is refreshed in place, preserving
        ///     its GUID and therefore every reference to it.
        /// </remarks>
        internal static AvatarMask GenerateUpperBodyMask(ConvaiBodyAnimationSet set)
        {
            if (set == null) return null;

            string setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath))
            {
                ConvaiLogger.Warning(
                    "[Body Animation] Cannot generate an overlay mask for an animation set that is not " +
                    "saved as a project asset. Save the set first.",
                    LogCategory.Animation);
                return null;
            }

            string directory = System.IO.Path.GetDirectoryName(setPath)?.Replace('\\', '/');
            string maskPath = $"{directory}/{System.IO.Path.GetFileNameWithoutExtension(setPath)}{GeneratedMaskSuffix}";

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            bool isNew = mask == null;
            if (isNew) mask = new AvatarMask();

            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool active = part is AvatarMaskBodyPart.Body
                    or AvatarMaskBodyPart.Head
                    or AvatarMaskBodyPart.LeftArm
                    or AvatarMaskBodyPart.RightArm
                    or AvatarMaskBodyPart.LeftFingers
                    or AvatarMaskBodyPart.RightFingers
                    or AvatarMaskBodyPart.LeftHandIK
                    or AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, active);
            }

            if (isNew) AssetDatabase.CreateAsset(mask, maskPath);
            else EditorUtility.SetDirty(mask);

            Undo.RecordObject(set, "Generate Upper Body Mask");
            var serialized = new SerializedObject(set);
            serialized.FindProperty("_upperBodyMask").objectReferenceValue = mask;
            serialized.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(mask);

            ConvaiLogger.Info(
                $"[Body Animation] {(isNew ? "Created" : "Refreshed")} the upper-body overlay mask at " +
                $"'{maskPath}' and assigned it to '{set.DisplayName}'.",
                LogCategory.Animation);
            return mask;
        }

        /// <summary>
        ///     Human-readable coverage summary for a set's locomotion content — which advanced
        ///     features the authored clips actually unlock. Shared by the set inspector and the
        ///     Troubleshooter so both describe coverage the same way.
        /// </summary>
        internal static string DescribeLocomotionCoverage(ConvaiBodyAnimationSet set)
        {
            if (set == null) return string.Empty;

            var assigned = new List<(string slot, LocomotionClip clip)>();
            set.Locomotion.CollectAssigned(assigned);

            if (assigned.Count == 0)
                return "No locomotion clips — the character idles, talks, and gestures in place.";

            LocomotionSection locomotion = set.Locomotion;
            var parts = new List<string>(5);
            if (!locomotion.HasJog) parts.Add("no jog (walk only)");
            if (!locomotion.HasAnyWalkStart) parts.Add("no directional starts");
            if (!locomotion.HasAnyWalkStop) parts.Add("no planted stops");
            if (!locomotion.HasAnyTurn) parts.Add("no turn-in-place");

            string coverage = $"{assigned.Count} of 26 locomotion slots filled";
            return parts.Count == 0
                ? $"{coverage} — every advanced locomotion feature has content."
                : $"{coverage} — {string.Join(", ", parts)}; those features blend simply instead.";
        }
    }
}
