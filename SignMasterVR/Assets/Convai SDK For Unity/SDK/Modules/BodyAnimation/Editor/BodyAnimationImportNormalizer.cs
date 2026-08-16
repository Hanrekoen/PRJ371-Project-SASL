using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Normalizes the shipped female motion FBX imports so the runtime and diagnostics get clean data:
    ///     every clip is renamed from Unreal's default take name ("Unreal Take") to the FBX
    ///     file name, loop flags are set per motion category, and root translation/rotation is
    ///     pinned (Based Upon: Original) since the clips are authored in-place.
    /// </summary>
    /// <remarks>
    ///     Idempotent: unchanged importers are skipped, so re-running after adding new FBX
    ///     files only touches the new ones. Clip renames change the FBX sub-asset ids, which
    ///     is why this must run BEFORE the default set asset is built.
    /// </remarks>
    public static class BodyAnimationImportNormalizer
    {
        /// <summary>Package-relative root of the shipped female motion library.</summary>
        public const string FemaleLibraryRoot =
            "Packages/com.convai.convai-sdk-for-unity/SamplesShared/Art/Animations/Female";

        /// <summary>
        ///     Clips that must loop: movement cycles, idle/talk cycles, and every clip that an
        ///     action plays under <c>HoldUntilStopped</c> (dances, think loop).
        /// </summary>
        private static readonly HashSet<string> LoopingClipNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Anim_F_Idle",
            "Anim_F_Talk",
            "Anim_F_Walk",
            "Anim_F_Jog",
            "Anim_F_Think_Loop",
            "Anim_F_Disco",
            "Anim_F_GStyle",
            "Anim_F_Groove"
        };

        /// <summary>
        ///     Clips played as ADDITIVE overlays: an additive reference pose (their own first
        ///     frame) is baked so the runtime can layer the delta over the base posture.
        ///     Empty for the shipped female library — its mocap clips have no neutral reference frame
        ///     (additive deltas double finger curls into clenched fists); reserved for
        ///     properly authored additive gesture clips.
        /// </summary>
        private static readonly HashSet<string> AdditiveClipNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Clips whose root rotation must stay OUT of the pose (Bake Into Pose off):
        ///     the turn-in-place family and the directional (90/180) starts. Their yaw is
        ///     applied by the runtime's scripted root drive from the analyzed yaw curves.
        ///     Baking the rotation into the pose either turns the visible skeleton a second
        ///     time on top of the scripted drive (turns: double turn + exit snap-back) or
        ///     swallows the rotation so the analyzer measures near-zero yaw and the runtime
        ///     cannot rotate at all (starts: the character walks off facing the wrong way).
        ///     The extracted root motion is discarded at runtime (root motion is disabled),
        ///     so the pose stays root-aligned and the drive is the single yaw authority.
        /// </summary>
        private static readonly HashSet<string> RootRotationDrivenClipNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Anim_F_Turn90_L",
            "Anim_F_Turn90_R",
            "Anim_F_Turn180_L",
            "Anim_F_Turn180_R",
            "Anim_F_WalkStart_90L",
            "Anim_F_WalkStart_90R",
            "Anim_F_WalkStart_180L",
            "Anim_F_WalkStart_180R",
            "Anim_F_JogStart_90L",
            "Anim_F_JogStart_90R",
            "Anim_F_JogStart_180L",
            "Anim_F_JogStart_180R"
        };

        /// <summary>
        ///     Applies naming/loop/root settings to every model under <paramref name="rootFolder" />.
        ///     Returns the number of files that actually changed and were reimported.
        /// </summary>
        public static int Normalize(string rootFolder)
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { rootFolder });
            int changedCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                        continue;

                    EditorUtility.DisplayProgressBar(
                        "Convai Body Animation — Normalizing Imports",
                        Path.GetFileName(path),
                        (float)i / guids.Length);

                    if (!ApplyToImporter(importer, Path.GetFileNameWithoutExtension(path)))
                    {
                        // Importer settings are already correct, but clips imported before
                        // the facial-curve stripper existed may still carry jaw/eye/blendshape
                        // curves — force one reimport so the postprocessor cleans them.
                        if (HasFacialCurves(path))
                        {
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                            changedCount++;
                        }

                        continue;
                    }

                    importer.SaveAndReimport();
                    changedCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            ConvaiLogger.Info(
                $"[BodyAnimationImportNormalizer] Normalized {changedCount}/{guids.Length} FBX imports under '{rootFolder}'.",
                LogCategory.Animation);
            return changedCount;
        }

        // ------------------------------------------------------------ additive talk variants

        /// <summary>Package-relative folder containing the shipped female talk clips.</summary>
        private const string TalkFolder = FemaleLibraryRoot + "/Talk";

        private const string AdditiveVariantSuffix = "_Additive";

        /// <summary>
        ///     Path of the shipped default Female set asset. Written out as a literal because
        ///     the tool that authors the asset lives outside the package and its path constant is
        ///     not reachable from here. Update this if the shipped asset ever moves.
        /// </summary>
        private const string DefaultSetAssetPath =
            "Packages/com.convai.convai-sdk-for-unity/SamplesShared/Profiles/Embodiment/BodyAnimation/ConvaiBodyAnimationSet_Female.asset";

        /// <summary>
        ///     For every talk clip FBX under <see cref="TalkFolder" />, duplicates each imported
        ///     clip-animation entry as an additive-baked "<c>_Additive</c>" twin (delta from the
        ///     clip's own first frame) so the walk-and-talk overlay can layer the gesture over
        ///     the gait. Idempotent: an entry that already has an "<c>_Additive</c>" twin, or is
        ///     itself an "<c>_Additive</c>" twin, is skipped. Returns the number of files changed
        ///     and the number of variant entries added.
        /// </summary>
        /// <remarks>
        ///     This pass does NOT wire the generated clips into any <c>TalkEntry.AdditiveClip</c>
        ///     on the sample set asset — that wiring is a later pass. To wire manually: load the
        ///     "<c>_Additive</c>" animation sub-asset from the talk clip's FBX and assign it via
        ///     the internal <c>TalkEntry.SetAdditiveClip</c> from an editor script or the wizard.
        /// </remarks>
        public static (int filesChanged, int variantsAdded) GenerateAdditiveTalkVariants()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { TalkFolder });
            int filesChanged = 0;
            int variantsAdded = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                        continue;

                    ModelImporterClipAnimation[] takes = importer.clipAnimations;
                    if (takes == null || takes.Length == 0)
                        takes = importer.defaultClipAnimations;
                    if (takes == null || takes.Length == 0)
                        continue;

                    var existingNames = new HashSet<string>(StringComparer.Ordinal);
                    for (int t = 0; t < takes.Length; t++)
                        existingNames.Add(takes[t].name);

                    var additions = new List<ModelImporterClipAnimation>();
                    for (int t = 0; t < takes.Length; t++)
                    {
                        ModelImporterClipAnimation take = takes[t];
                        if (take.name.EndsWith(AdditiveVariantSuffix, StringComparison.Ordinal))
                            continue; // already an additive twin — never double it up

                        string variantName = take.name + AdditiveVariantSuffix;
                        if (existingNames.Contains(variantName))
                            continue; // idempotent: the twin already exists

                        ModelImporterClipAnimation variant = CloneClipAnimation(take);
                        variant.name = variantName;
                        variant.hasAdditiveReferencePose = true;
                        variant.additiveReferencePoseFrame = take.firstFrame;

                        additions.Add(variant);
                        existingNames.Add(variantName);
                    }

                    if (additions.Count == 0)
                        continue;

                    var combined = new ModelImporterClipAnimation[takes.Length + additions.Count];
                    takes.CopyTo(combined, 0);
                    for (int a = 0; a < additions.Count; a++)
                        combined[takes.Length + a] = additions[a];

                    importer.clipAnimations = combined;
                    importer.SaveAndReimport();

                    filesChanged++;
                    variantsAdded += additions.Count;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            ConvaiLogger.Info(
                $"[BodyAnimationImportNormalizer] Generated {variantsAdded} additive talk variant(s) " +
                $"across {filesChanged} file(s) under '{TalkFolder}'.",
                LogCategory.Animation);

            int wired = WireAdditiveVariantsIntoDefaultSet();
            if (wired > 0)
            {
                ConvaiLogger.Info(
                    $"[BodyAnimationImportNormalizer] Wired {wired} additive clip(s) into the " +
                    "default set's talk entries.",
                    LogCategory.Animation);
            }

            return (filesChanged, variantsAdded);
        }

        /// <summary>
        ///     Assigns each default-set Talk/Listen/Think entry's <c>AdditiveClip</c> from the
        ///     "<c>_Additive</c>" sub-asset living next to its main clip, enabling the
        ///     walk-and-talk overlay's additive tier out of the box. Idempotent; entries
        ///     whose FBX has no additive twin are left unchanged (they fall back to the
        ///     softened override while moving). Returns the number of entries wired.
        /// </summary>
        /// <remarks>
        ///     Listen/Think pools get the exact same main-clip wiring treatment as
        ///     Talk. An entry's Intro/Outro Clip is a stationary one-shot bracket
        ///     that is never played on the moving additive overlay, so it has no additive-twin
        ///     field to wire here; if it lives under <see cref="FemaleLibraryRoot" /> like every other library
        ///     clip, its loop/root import settings are already normalized identically to a main
        ///     clip's by <see cref="Normalize" />'s blind per-model walk — nothing extra needed.
        /// </remarks>
        private static int WireAdditiveVariantsIntoDefaultSet()
        {
            var set = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationSet>(DefaultSetAssetPath);
            if (set == null) return 0;

            int wired = 0;
            wired += WireAdditiveClipsInPool(set.Talks);
            wired += WireAdditiveClipsInPool(set.Listens);
            wired += WireAdditiveClipsInPool(set.Thinks);

            if (wired > 0)
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
            }

            return wired;
        }

        /// <summary>Wires the main-clip additive twin (see <see cref="WireAdditiveVariantsIntoDefaultSet" />) across one Talk/Listen/Think pool.</summary>
        private static int WireAdditiveClipsInPool(IReadOnlyList<TalkEntry> entries)
        {
            if (entries == null) return 0;

            int wired = 0;
            foreach (TalkEntry talk in entries)
            {
                if (talk.Clip == null || talk.AdditiveClip != null) continue;

                string clipPath = AssetDatabase.GetAssetPath(talk.Clip);
                if (string.IsNullOrEmpty(clipPath)) continue;

                string wantedName = talk.Clip.name + AdditiveVariantSuffix;
                foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(clipPath))
                {
                    if (sub is AnimationClip additive &&
                        string.Equals(additive.name, wantedName, StringComparison.Ordinal))
                    {
                        talk.SetAdditiveClip(additive);
                        wired++;
                        break;
                    }
                }
            }

            return wired;
        }

        /// <summary>
        ///     Deep-copies a <see cref="ModelImporterClipAnimation" /> via its public read/write
        ///     properties. <see cref="ModelImporterClipAnimation" /> is a reference type, so a
        ///     plain assignment would alias the source entry — mutating the clone would also
        ///     mutate the original take instead of producing an independent duplicate.
        /// </summary>
        private static ModelImporterClipAnimation CloneClipAnimation(ModelImporterClipAnimation source)
        {
            var clone = new ModelImporterClipAnimation();
            PropertyInfo[] properties =
                typeof(ModelImporterClipAnimation).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                    continue;

                try
                {
                    property.SetValue(clone, property.GetValue(source));
                }
                catch
                {
                    // A small number of properties are get/set-restricted (e.g. superseded
                    // aliases) and throw rather than no-op; skip those, they carry no data the
                    // additive twin needs beyond what the other properties already cover.
                }
            }

            return clone;
        }

        private static bool ApplyToImporter(ModelImporter importer, string fileName)
        {
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
                return false;

            var desired = new ModelImporterClipAnimation[takes.Length];
            for (int i = 0; i < takes.Length; i++)
            {
                ModelImporterClipAnimation take = takes[i];
                take.name = takes.Length == 1 ? fileName : $"{fileName}_{i}";
                take.loopTime = LoopingClipNames.Contains(take.name);

                bool additive = AdditiveClipNames.Contains(take.name);
                take.hasAdditiveReferencePose = additive;
                if (additive)
                    take.additiveReferencePoseFrame = take.firstFrame;

                // In-place clips: pin root motion so retarget noise can never translate the
                // rig — EXCEPT the turn/start clips' rotation, which must be extracted out
                // of the pose so the scripted root drive stays the single yaw authority.
                take.lockRootRotation = !RootRotationDrivenClipNames.Contains(take.name);
                take.lockRootHeightY = true;
                take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = true;
                take.keepOriginalPositionY = true;
                take.keepOriginalPositionXZ = true;

                desired[i] = take;
            }

            if (!ClipSettingsDiffer(importer.clipAnimations, desired))
                return false;

            importer.clipAnimations = desired;
            return true;
        }

        private static bool HasFacialCurves(string path)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not AnimationClip clip ||
                    clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    continue;

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (BodyAnimationFacialCurveStripper.IsFacialBinding(binding))
                        return true;
                }
            }

            return false;
        }

        private static bool ClipSettingsDiffer(
            ModelImporterClipAnimation[] current,
            ModelImporterClipAnimation[] desired)
        {
            if (current == null || current.Length != desired.Length)
                return true;

            for (int i = 0; i < desired.Length; i++)
            {
                ModelImporterClipAnimation a = current[i];
                ModelImporterClipAnimation b = desired[i];

                if (!string.Equals(a.name, b.name, StringComparison.Ordinal) ||
                    a.loopTime != b.loopTime ||
                    a.hasAdditiveReferencePose != b.hasAdditiveReferencePose ||
                    a.lockRootRotation != b.lockRootRotation ||
                    a.lockRootHeightY != b.lockRootHeightY ||
                    a.lockRootPositionXZ != b.lockRootPositionXZ ||
                    a.keepOriginalOrientation != b.keepOriginalOrientation ||
                    a.keepOriginalPositionY != b.keepOriginalPositionY ||
                    a.keepOriginalPositionXZ != b.keepOriginalPositionXZ)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
