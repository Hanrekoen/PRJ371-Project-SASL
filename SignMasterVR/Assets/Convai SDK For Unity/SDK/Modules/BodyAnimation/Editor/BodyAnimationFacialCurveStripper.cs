using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Strips facial channels from the shipped female body-animation clips at import: humanoid
    ///     jaw/eye muscle curves and blendshape curves. At runtime the face is owned
    ///     exclusively by LipSync / Emotion / Gaze — mocap jaw or eye motion baked
    ///     into body clips fights them (double mouth movement while the character speaks).
    /// </summary>
    internal sealed class BodyAnimationFacialCurveStripper : AssetPostprocessor
    {
        /// <summary>Humanoid muscle curves that belong to the face pipeline, never to body clips.</summary>
        private static readonly string[] FacialMuscleNames =
        {
            "Jaw Close",
            "Jaw Left-Right",
            "Left Eye Down-Up",
            "Left Eye In-Out",
            "Right Eye Down-Up",
            "Right Eye In-Out"
        };

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!assetPath.StartsWith(
                    BodyAnimationImportNormalizer.FemaleLibraryRoot, StringComparison.OrdinalIgnoreCase))
                return;

            int stripped = StripFacialCurves(clip);
            if (stripped > 0)
            {
                ConvaiLogger.Info(
                    $"[BodyAnimationFacialCurveStripper] '{clip.name}': stripped {stripped} facial " +
                    "curve(s) — the face is driven by LipSync/Emotion/Gaze, not body clips.",
                    LogCategory.Animation);
            }
        }

        /// <summary>Removes facial curves from a clip. Returns the number of curves removed.</summary>
        internal static int StripFacialCurves(AnimationClip clip)
        {
            int stripped = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!IsFacialBinding(binding)) continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                stripped++;
            }

            return stripped;
        }

        /// <summary>True for humanoid jaw/eye muscle curves and any blendshape curve.</summary>
        internal static bool IsFacialBinding(EditorCurveBinding binding)
        {
            if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                return true;

            if (binding.type != typeof(Animator)) return false;
            for (int i = 0; i < FacialMuscleNames.Length; i++)
            {
                if (string.Equals(binding.propertyName, FacialMuscleNames[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
