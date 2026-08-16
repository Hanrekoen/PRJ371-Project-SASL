using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Inspects a character's facial meshes and classifies the blendshape-naming
    ///     convention it follows.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Detection is a pure function of the mesh's blendshape names: it counts how
    ///         many signature blendshapes match each known convention (ARKit, CC3,
    ///         CC4 Extended, MetaHuman) and picks the highest-scoring one. Ambiguous rigs
    ///         fall back to <see cref="RigConvention.Unknown" /> so the user can assign
    ///         a <see cref="RigConvention.Custom" /> override asset when needed.
    ///     </para>
    ///     <para>
    ///         CC4 Extended is a strict superset of CC3; when both match, CC4 Extended wins
    ///         if and only if at least one of its distinguishing signatures is present ;
    ///         otherwise the resolver reports CC3 (since CC4 Extended rigs without those
    ///         shapes are functionally identical to CC3).
    ///     </para>
    ///     <para>
    ///         This class is deliberately stateless and allocates only for the scan.
    ///     </para>
    /// </remarks>
    public static class RigConventionResolver
    {
        // Signature names picked because they are unique-ish to each convention.
        private static readonly string[] ARKitSignatures =
        {
            "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "browInnerUp",
            "mouthSmileLeft", "mouthSmileRight", "tongueOut"
        };

        private static readonly string[] CC3Signatures =
        {
            "Eye_Blink_L", "Eye_Blink_R", "V_Open", "V_Explosive",
            "Mouth_Smile_L", "Mouth_Smile_R", "Brow_Raise_Inner_L"
        };

        // CC4 Extended-only signatures (not present in the CC3 base set). A match here
        // promotes an otherwise-CC3-looking rig to the Extended profile so behavior
        // modules can opt into the richer semantic mappings.
        private static readonly string[] CC4ExtendedSignatures =
        {
            "Mouth_Press_L", "Mouth_Press_R",
            "Mouth_Shrug_Upper", "Mouth_Shrug_Lower",
            "Mouth_Roll_Upper", "Mouth_Roll_Lower",
            "Mouth_Close", "Tongue_Out",
            "Brow_Raise_L", "Brow_Raise_R",
            "Eyelash_Upper_Down_L", "Eyelash_Upper_Down_R"
        };

        private static readonly string[] MetaHumanSignatures =
        {
            "CTRL_L_eye", "CTRL_R_eye", "CTRL_C_jaw", "CTRL_C_mouth_lipsTogether",
            "CTRL_L_brow_up", "CTRL_R_brow_up"
        };

        /// <summary>
        ///     Scans the supplied meshes and returns the best-matching rig convention, plus a
        ///     confidence score in <c>[0, 1]</c>.
        /// </summary>
        public static RigConvention Detect(IReadOnlyList<SkinnedMeshRenderer> meshes, out float confidence)
        {
            confidence = 0f;
            if (meshes == null || meshes.Count == 0)
                return RigConvention.Unknown;

            HashSet<string> allNames = CollectBlendshapeNames(meshes);
            if (allNames.Count == 0)
                return RigConvention.Unknown;

            int arkit = CountMatches(allNames, ARKitSignatures, caseInsensitive: false);
            int cc3 = CountMatches(allNames, CC3Signatures, caseInsensitive: false);
            int cc4Extended = CountMatches(allNames, CC4ExtendedSignatures, caseInsensitive: false);
            int metaHuman = CountMatches(allNames, MetaHumanSignatures, caseInsensitive: false);

            if (arkit == 0 && cc3 == 0 && cc4Extended == 0 && metaHuman == 0)
            {
                // Some exporters lowercase everything ; try case-insensitive as a fallback.
                arkit = CountMatches(allNames, ARKitSignatures, caseInsensitive: true);
                cc3 = CountMatches(allNames, CC3Signatures, caseInsensitive: true);
                cc4Extended = CountMatches(allNames, CC4ExtendedSignatures, caseInsensitive: true);
                metaHuman = CountMatches(allNames, MetaHumanSignatures, caseInsensitive: true);
            }

            // CC4 Extended is only eligible when the CC3 base shapes are also present ;
            // the Extended signatures alone are insufficient evidence because a few of
            // them (e.g. mouthClose, tongueOut) overlap with ARKit/other naming.
            int cc4ExtendedEligible = (cc3 > 0 && cc4Extended > 0) ? cc3 + cc4Extended : 0;

            int best = Mathf.Max(arkit, Mathf.Max(cc3, Mathf.Max(cc4ExtendedEligible, metaHuman)));
            if (best == 0)
                return RigConvention.Unknown;

            RigConvention result;
            int maxSignatures;
            if (best == cc4ExtendedEligible && cc4ExtendedEligible > 0)
            {
                result = RigConvention.ReallusionCC4Extended;
                maxSignatures = CC3Signatures.Length + CC4ExtendedSignatures.Length;
            }
            else if (best == arkit)
            {
                result = RigConvention.ARKit;
                maxSignatures = ARKitSignatures.Length;
            }
            else if (best == cc3)
            {
                result = RigConvention.ReallusionCC3;
                maxSignatures = CC3Signatures.Length;
            }
            else
            {
                result = RigConvention.MetaHuman;
                maxSignatures = MetaHumanSignatures.Length;
            }

            confidence = Mathf.Clamp01((float)best / maxSignatures);

            // Reject weak matches (<30 %) as Unknown so authoring can request an override.
            if (confidence < 0.3f)
            {
                confidence = 0f;
                return RigConvention.Unknown;
            }

            return result;
        }

        private static HashSet<string> CollectBlendshapeNames(IReadOnlyList<SkinnedMeshRenderer> meshes)
        {
            var set = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < meshes.Count; i++)
            {
                SkinnedMeshRenderer smr = meshes[i];
                if (smr == null || smr.sharedMesh == null) continue;
                Mesh mesh = smr.sharedMesh;
                int count = mesh.blendShapeCount;
                for (int j = 0; j < count; j++)
                    set.Add(mesh.GetBlendShapeName(j));
            }
            return set;
        }

        private static int CountMatches(HashSet<string> names, string[] signatures, bool caseInsensitive)
        {
            int count = 0;
            for (int i = 0; i < signatures.Length; i++)
            {
                string sig = signatures[i];
                if (caseInsensitive)
                {
                    foreach (string name in names)
                    {
                        if (name.IndexOf(sig, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            count++;
                            break;
                        }
                    }
                }
                else if (names.Contains(sig))
                {
                    count++;
                }
                else
                {
                    // MetaHuman signatures are typically substrings rather than exact names.
                    foreach (string name in names)
                    {
                        if (name.Contains(sig, System.StringComparison.Ordinal))
                        {
                            count++;
                            break;
                        }
                    }
                }
            }
            return count;
        }
    }
}
