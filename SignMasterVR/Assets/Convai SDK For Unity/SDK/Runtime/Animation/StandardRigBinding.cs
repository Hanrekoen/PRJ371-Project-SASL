using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Logging;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Default <see cref="IStandardRigBinding" /> implementation that inspects the
    ///     character hierarchy at setup time and caches bone / blendshape resolution tables.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The binding resolves bones via Unity's <see cref="HumanBodyBones" /> system
    ///         when the character has a humanoid <see cref="Animator" />. For generic rigs it
    ///         falls back to name-based lookup using the convention tables in
    ///         <see cref="RigConventionMaps" />.
    ///     </para>
    ///     <para>
    ///         Blendshape resolution is convention-driven: the binding asks
    ///         <see cref="RigConventionResolver" /> which rig this is, consults the convention
    ///         table, and resolves the named blendshape on the most appropriate mesh
    ///         (preferring the mesh that owns the most matches ; i.e. the face mesh, not
    ///         costumes).
    ///     </para>
    ///     <para>
    ///         Call <see cref="Rebuild" /> whenever the character is re-skinned or outfits
    ///         are hot-swapped at runtime; otherwise the binding is safe to keep for the full
    ///         character lifetime.
    ///     </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Convai/Embodiment/Character Rig")]
    public sealed class StandardRigBinding : MonoBehaviour, IStandardRigBinding
    {
        [Tooltip("Leave empty to auto-detect every SkinnedMeshRenderer in this hierarchy.")]
        [SerializeField] private List<SkinnedMeshRenderer> facialMeshes = new();

        [Tooltip("Optional convention override. When set to a non-Unknown value, auto detection is skipped.")]
        [SerializeField] private RigConvention conventionOverride = RigConvention.Unknown;

        [Tooltip("Required when Convention Override is Custom. Maps Convai semantic blendshapes to this rig's names.")]
        [SerializeField] private CustomRigConventionMap customConventionMap;

        // No [Header] here or on the Gaze block below: this component is drawn by a Convai inspector
        // that owns its own section vocabulary, so an attribute-authored grouping would only ever be
        // a second, divergent set of headings visible in the debug inspector.
        [Tooltip("Optional explicit Hips mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform hipsOverride;
        [Tooltip("Optional explicit Spine mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform spineOverride;
        [Tooltip("Optional explicit Chest mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform chestOverride;
        [Tooltip("Optional explicit Upper Chest mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform upperChestOverride;
        [Tooltip("Optional explicit Neck mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform neckOverride;
        [Tooltip("Optional explicit Head mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform headOverride;
        [Tooltip("Optional explicit Left Eye mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftEyeOverride;
        [Tooltip("Optional explicit Right Eye mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightEyeOverride;
        [Tooltip("Optional explicit Left Shoulder mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftShoulderOverride;
        [Tooltip("Optional explicit Right Shoulder mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightShoulderOverride;
        [Tooltip("Optional explicit Left Upper Arm mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftUpperArmOverride;
        [Tooltip("Optional explicit Right Upper Arm mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightUpperArmOverride;
        [Tooltip("Optional explicit Left Upper Leg mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftUpperLegOverride;
        [Tooltip("Optional explicit Left Lower Leg mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftLowerLegOverride;
        [Tooltip("Optional explicit Left Foot mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform leftFootOverride;
        [Tooltip("Optional explicit Right Upper Leg mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightUpperLegOverride;
        [Tooltip("Optional explicit Right Lower Leg mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightLowerLegOverride;
        [Tooltip("Optional explicit Right Foot mapping. The transform must belong to this character hierarchy.")]
        [SerializeField] private Transform rightFootOverride;

        [Tooltip("Uses the authored local forward/up axes below for Gaze. Leave disabled for a rig built on the usual +Z forward, +Y up convention.")]
        [SerializeField] private bool gazeAxisCalibrationEnabled;
        [Tooltip("Character-root local direction that represents looking straight ahead.")]
        [SerializeField] private Vector3 gazeRootForwardLocal = Vector3.forward;
        [Tooltip("Character-root local direction that represents up for Gaze. It is orthonormalized against Forward at runtime.")]
        [SerializeField] private Vector3 gazeRootUpLocal = Vector3.up;
        [Tooltip("Left eye local direction that represents its optical forward axis.")]
        [SerializeField] private Vector3 leftEyeForwardLocal = Vector3.forward;
        [Tooltip("Right eye local direction that represents its optical forward axis.")]
        [SerializeField] private Vector3 rightEyeForwardLocal = Vector3.forward;

        private readonly Dictionary<StandardBone, Transform> _boneCache = new();
        private readonly Dictionary<StandardBone, BoneSource> _boneSourceCache = new();
        private readonly Dictionary<StandardBlendshape, BlendshapeResolution> _blendshapeCache = new();

        /// <summary>
        ///     Semantics already reported as missing, so the warning is written once per component
        ///     lifetime rather than once per resolution table.
        /// </summary>
        /// <remarks>
        ///     Deliberately <b>not</b> cleared by <see cref="Rebuild" />. The caches are, so without
        ///     this every rebuild re-narrated every gap: a rig missing ten blendshapes wrote ten fresh
        ///     warnings each time an outfit was swapped, or — in the editor — each time the user
        ///     touched a field on this component. The second warning about a gap that has not changed
        ///     carries no information the first did not.
        /// </remarks>
        private readonly HashSet<StandardBone> _reportedBoneMisses = new();

        /// <inheritdoc cref="_reportedBoneMisses" />
        private readonly HashSet<StandardBlendshape> _reportedBlendshapeMisses = new();

        private RigConvention _detectedConvention = RigConvention.Unknown;
        private Animator _animator;
        private Transform[] _hierarchyTransformCache;
        private bool _resolutionTablesBuilt;

        /// <inheritdoc />
        public Transform Root
        {
            get
            {
                _animator ??= GetComponentInChildren<Animator>(true);
                return _animator != null ? _animator.transform : transform;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<SkinnedMeshRenderer> FacialMeshes => facialMeshes;

        /// <inheritdoc />
        public RigConvention DetectedConvention => _detectedConvention;

        /// <summary>Custom semantic-to-blendshape map used when DetectedConvention is Custom.</summary>
        public CustomRigConventionMap CustomConventionMap => customConventionMap;

        /// <summary>Confidence score of the last detection pass in <c>[0, 1]</c>.</summary>
        public float DetectionConfidence { get; private set; }

        /// <summary>
        ///     Gets the optional private Gaze calibration. This deliberately stays outside
        ///     <see cref="IStandardRigBinding"/> so a customer's own implementation keeps the
        ///     uncalibrated solver path exactly as it is.
        /// </summary>
        internal bool TryGetGazeAxisCalibration(
            out Vector3 rootForwardLocal,
            out Vector3 rootUpLocal,
            out Vector3 leftEyeForward,
            out Vector3 rightEyeForward)
        {
            rootForwardLocal = gazeRootForwardLocal;
            rootUpLocal = gazeRootUpLocal;
            leftEyeForward = leftEyeForwardLocal;
            rightEyeForward = rightEyeForwardLocal;
            return gazeAxisCalibrationEnabled &&
                   rootForwardLocal.sqrMagnitude > 1e-6f &&
                   rootUpLocal.sqrMagnitude > 1e-6f &&
                   Vector3.Cross(rootForwardLocal, rootUpLocal).sqrMagnitude > 1e-6f &&
                   leftEyeForward.sqrMagnitude > 1e-6f &&
                   rightEyeForward.sqrMagnitude > 1e-6f;
        }

        private void Awake()
        {
            Rebuild();
        }

        /// <summary>
        ///     Re-scans the hierarchy and rebuilds resolution tables. Safe to call when
        ///     outfits change, a mesh is replaced, or the user applies a convention override.
        /// </summary>
        public void Rebuild()
        {
            _boneCache.Clear();
            _boneSourceCache.Clear();
            _blendshapeCache.Clear();
            _resolutionTablesBuilt = true;

            if (facialMeshes == null || facialMeshes.Count == 0)
                AutoDetectFacialMeshes();
            else
            {
                PruneNullMeshes();
                if (facialMeshes.Count == 0)
                    AutoDetectFacialMeshes();
            }

            _animator = GetComponentInChildren<Animator>(true);
            _hierarchyTransformCache = GetComponentsInChildren<Transform>(true);

            if (conventionOverride != RigConvention.Unknown)
            {
                _detectedConvention = conventionOverride;
                DetectionConfidence = 1f;
            }
            else
            {
                _detectedConvention = RigConventionResolver.Detect(facialMeshes, out float confidence);
                DetectionConfidence = confidence;
            }

            SortFacialMeshesByResolutionPriority();

            NotifyContextRigBindingChanged();
        }

        /// <inheritdoc />
        public bool TryGetBone(StandardBone semantic, out Transform bone)
        {
            if (TryPeekBone(semantic, out bone))
                return true;

            if (_reportedBoneMisses.Add(semantic))
            {
                ConvaiLogger.Warning(
                    $"[{name}] This rig has no '{semantic}' bone, so anything that needs it stays " +
                    "inactive. Assign it under Custom Rig Setup on the Character Rig component, " +
                    "or use a rig whose humanoid avatar maps that bone.",
                    LogCategory.Character);
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryGetBlendshape(
            StandardBlendshape semantic,
            out SkinnedMeshRenderer mesh,
            out int blendshapeIndex)
        {
            if (TryPeekBlendshape(semantic, out mesh, out blendshapeIndex))
                return true;

            if (_reportedBlendshapeMisses.Add(semantic))
            {
                ConvaiLogger.Warning(
                    $"[{name}] No mesh on this character has a '{semantic}' blendshape, so anything " +
                    "that drives it stays inactive. Add the blendshape to the face mesh, or map its " +
                    "actual name under Custom Convention Map.",
                    LogCategory.Character);
            }

            return false;
        }

        /// <summary>
        ///     Resolves a bone exactly as <see cref="TryGetBone" /> does, but reports nothing when it
        ///     is missing.
        /// </summary>
        /// <remarks>
        ///     For editor surfaces that display the whole resolution table. They already show every
        ///     gap on screen, next to the semantic name and in context — a console warning saying the
        ///     same thing is not a second chance to notice, it is noise over the table the user is
        ///     reading. Runtime callers want <see cref="TryGetBone" />, which does report.
        /// </remarks>
        internal bool TryPeekBone(StandardBone semantic, out Transform bone)
        {
            if (_boneCache.TryGetValue(semantic, out bone))
                return bone != null;

            bone = ResolveBone(semantic, out BoneSource source);

            // Cache the miss as well as the hit: it stops an unresolvable bone re-walking the
            // hierarchy on every frame that asks for it. Rebuild() clears the cache, so a rig that
            // gains the bone later resolves it normally.
            _boneCache[semantic] = bone;
            _boneSourceCache[semantic] = source;

            return bone != null;
        }

        /// <inheritdoc cref="TryPeekBone" />
        internal bool TryPeekBlendshape(
            StandardBlendshape semantic,
            out SkinnedMeshRenderer mesh,
            out int blendshapeIndex)
        {
            if (_blendshapeCache.TryGetValue(semantic, out BlendshapeResolution cached))
            {
                mesh = cached.Mesh;
                blendshapeIndex = cached.Index;
                return cached.Mesh != null && cached.Index >= 0;
            }

            if (!ResolveBlendshape(semantic, out mesh, out blendshapeIndex))
            {
                _blendshapeCache[semantic] = new BlendshapeResolution(null, -1);
                return false;
            }

            _blendshapeCache[semantic] = new BlendshapeResolution(mesh, blendshapeIndex);
            return true;
        }

        /// <summary>
        ///     Reports which of the three resolution routes actually supplied a bone.
        /// </summary>
        /// <remarks>
        ///     Answered here rather than re-derived by the caller: this component owns the resolution
        ///     order, and an editor that re-implemented it to label its own table would be a second
        ///     copy of that order, free to drift from this one.
        /// </remarks>
        internal BoneSource GetBoneSource(StandardBone semantic)
        {
            TryPeekBone(semantic, out _);
            return _boneSourceCache.TryGetValue(semantic, out BoneSource source)
                ? source
                : BoneSource.Unresolved;
        }

        /// <summary>
        ///     Builds the resolution tables if nothing has yet. Edit Mode never runs
        ///     <see cref="Awake" /> for this component, so an editor reading
        ///     <see cref="DetectedConvention" /> straight after a scene load or a domain reload would
        ///     otherwise report <see cref="RigConvention.Unknown" /> at zero confidence for a rig that
        ///     is perfectly healthy — and every blendshape as unresolved, because no convention had
        ///     been chosen to look them up under.
        /// </summary>
        internal void EnsureResolutionTables()
        {
            if (_resolutionTablesBuilt) return;
            Rebuild();
        }

        private Transform ResolveBone(StandardBone semantic, out BoneSource source)
        {
            Transform explicitOverride = MapToExplicitOverride(semantic);
            if (IsValidExplicitOverride(explicitOverride))
            {
                source = BoneSource.Manual;
                return explicitOverride;
            }

            if (_animator != null && _animator.isHuman)
            {
                HumanBodyBones? human = MapToHumanBodyBones(semantic);
                if (human.HasValue)
                {
                    Transform bone = _animator.GetBoneTransform(human.Value);
                    if (bone != null)
                    {
                        source = BoneSource.HumanoidAvatar;
                        return bone;
                    }
                }
            }

            // Generic fallback ; bone name lookup.
            string[] candidates = MapToFallbackNames(semantic);
            if (candidates != null)
            {
                Transform[] all = _hierarchyTransformCache ?? GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < candidates.Length; i++)
                {
                    string target = candidates[i];
                    for (int j = 0; j < all.Length; j++)
                    {
                        Transform t = all[j];
                        if (t != null && string.Equals(t.name, target, System.StringComparison.OrdinalIgnoreCase))
                        {
                            source = BoneSource.NameMatch;
                            return t;
                        }
                    }
                }
            }

            source = BoneSource.Unresolved;
            return null;
        }

        private bool IsValidExplicitOverride(Transform candidate) =>
            candidate != null && (candidate == transform || candidate.IsChildOf(transform));

        private Transform MapToExplicitOverride(StandardBone semantic)
        {
            return semantic switch
            {
                StandardBone.Hips => hipsOverride,
                StandardBone.Spine => spineOverride,
                StandardBone.Chest => chestOverride,
                StandardBone.UpperChest => upperChestOverride,
                StandardBone.Neck => neckOverride,
                StandardBone.Head => headOverride,
                StandardBone.LeftEye => leftEyeOverride,
                StandardBone.RightEye => rightEyeOverride,
                StandardBone.LeftShoulder => leftShoulderOverride,
                StandardBone.RightShoulder => rightShoulderOverride,
                StandardBone.LeftUpperArm => leftUpperArmOverride,
                StandardBone.RightUpperArm => rightUpperArmOverride,
                StandardBone.LeftUpperLeg => leftUpperLegOverride,
                StandardBone.LeftLowerLeg => leftLowerLegOverride,
                StandardBone.LeftFoot => leftFootOverride,
                StandardBone.RightUpperLeg => rightUpperLegOverride,
                StandardBone.RightLowerLeg => rightLowerLegOverride,
                StandardBone.RightFoot => rightFootOverride,
                _ => null
            };
        }

        private bool ResolveBlendshape(
            StandardBlendshape semantic,
            out SkinnedMeshRenderer mesh,
            out int blendshapeIndex)
        {
            mesh = null;
            blendshapeIndex = -1;

            if (!TryResolveBlendshapeName(semantic, out string blendshapeName))
                return false;

            for (int i = 0; i < facialMeshes.Count; i++)
            {
                SkinnedMeshRenderer smr = facialMeshes[i];
                if (smr == null || smr.sharedMesh == null) continue;

                int index = smr.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (index < 0) continue;

                mesh = smr;
                blendshapeIndex = index;
                return true;
            }

            return false;
        }

        private bool TryResolveBlendshapeName(StandardBlendshape semantic, out string blendshapeName)
        {
            blendshapeName = null;

            if (_detectedConvention == RigConvention.Custom)
                return customConventionMap != null &&
                       customConventionMap.TryGetBlendshapeName(semantic, out blendshapeName);

            IReadOnlyDictionary<StandardBlendshape, string> map =
                RigConventionMaps.ForConvention(_detectedConvention);
            return map.TryGetValue(semantic, out blendshapeName);
        }

        private void AutoDetectFacialMeshes()
        {
            facialMeshes.Clear();
            SkinnedMeshRenderer[] found = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                SkinnedMeshRenderer smr = found[i];
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                    facialMeshes.Add(smr);
            }
        }

        private void SortFacialMeshesByResolutionPriority()
        {
            if (facialMeshes == null || facialMeshes.Count < 2) return;

            var scores = new Dictionary<SkinnedMeshRenderer, MeshResolutionScore>();
            for (int i = 0; i < facialMeshes.Count; i++)
            {
                SkinnedMeshRenderer mesh = facialMeshes[i];
                if (mesh == null || mesh.sharedMesh == null) continue;

                scores[mesh] = new MeshResolutionScore(
                    CountSemanticBlendshapeMatches(mesh),
                    ResolveMeshNamePriority(mesh),
                    i);
            }

            facialMeshes.Sort((a, b) =>
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                MeshResolutionScore scoreA = scores.TryGetValue(a, out MeshResolutionScore aScore)
                    ? aScore
                    : new MeshResolutionScore(0, int.MaxValue, int.MaxValue);
                MeshResolutionScore scoreB = scores.TryGetValue(b, out MeshResolutionScore bScore)
                    ? bScore
                    : new MeshResolutionScore(0, int.MaxValue, int.MaxValue);

                int coverageCompare = scoreB.SemanticMatchCount.CompareTo(scoreA.SemanticMatchCount);
                if (coverageCompare != 0) return coverageCompare;

                int priorityCompare = scoreA.NamePriority.CompareTo(scoreB.NamePriority);
                if (priorityCompare != 0) return priorityCompare;

                return scoreA.OriginalIndex.CompareTo(scoreB.OriginalIndex);
            });
        }

        private int CountSemanticBlendshapeMatches(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null) return 0;

            int count = 0;
            if (_detectedConvention == RigConvention.Custom)
            {
                IReadOnlyList<CustomRigConventionMap.BlendshapeMapping> mappings =
                    customConventionMap != null ? customConventionMap.Blendshapes : null;
                if (mappings == null) return 0;

                for (int i = 0; i < mappings.Count; i++)
                {
                    string blendshapeName = mappings[i].BlendshapeName;
                    if (!string.IsNullOrWhiteSpace(blendshapeName) &&
                        renderer.sharedMesh.GetBlendShapeIndex(blendshapeName) >= 0)
                    {
                        count++;
                    }
                }

                return count;
            }

            IReadOnlyDictionary<StandardBlendshape, string> map =
                RigConventionMaps.ForConvention(_detectedConvention);
            foreach (KeyValuePair<StandardBlendshape, string> kvp in map)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value) &&
                    renderer.sharedMesh.GetBlendShapeIndex(kvp.Value) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int ResolveMeshNamePriority(SkinnedMeshRenderer renderer)
        {
            string meshName = renderer != null ? renderer.name : string.Empty;
            if (ContainsMeshPattern(meshName, "head") || ContainsMeshPattern(meshName, "face")) return 0;
            if (ContainsMeshPattern(meshName, "teeth") || ContainsMeshPattern(meshName, "tooth")) return 1;
            if (ContainsMeshPattern(meshName, "tongue")) return 2;
            return 3;
        }

        private static bool ContainsMeshPattern(string value, string pattern) =>
            !string.IsNullOrEmpty(value) &&
            value.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private void PruneNullMeshes()
        {
            for (int i = facialMeshes.Count - 1; i >= 0; i--)
                if (facialMeshes[i] == null) facialMeshes.RemoveAt(i);
        }

        private void NotifyContextRigBindingChanged()
        {
            EmbodimentContext context = GetComponentInParent<EmbodimentContext>(true);
            if (context == null) return;

            context.NotifyRigBindingChanged(this);
        }

        private static HumanBodyBones? MapToHumanBodyBones(StandardBone semantic)
        {
            return semantic switch
            {
                StandardBone.Hips => HumanBodyBones.Hips,
                StandardBone.Spine => HumanBodyBones.Spine,
                StandardBone.Chest => HumanBodyBones.Chest,
                StandardBone.UpperChest => HumanBodyBones.UpperChest,
                StandardBone.Neck => HumanBodyBones.Neck,
                StandardBone.Head => HumanBodyBones.Head,
                StandardBone.LeftEye => HumanBodyBones.LeftEye,
                StandardBone.RightEye => HumanBodyBones.RightEye,
                StandardBone.LeftShoulder => HumanBodyBones.LeftShoulder,
                StandardBone.RightShoulder => HumanBodyBones.RightShoulder,
                StandardBone.LeftUpperArm => HumanBodyBones.LeftUpperArm,
                StandardBone.RightUpperArm => HumanBodyBones.RightUpperArm,
                StandardBone.LeftUpperLeg => HumanBodyBones.LeftUpperLeg,
                StandardBone.LeftLowerLeg => HumanBodyBones.LeftLowerLeg,
                StandardBone.LeftFoot => HumanBodyBones.LeftFoot,
                StandardBone.RightUpperLeg => HumanBodyBones.RightUpperLeg,
                StandardBone.RightLowerLeg => HumanBodyBones.RightLowerLeg,
                StandardBone.RightFoot => HumanBodyBones.RightFoot,
                _ => null
            };
        }

        private static readonly string[] FallbackHips = { "Hips", "CC_Base_Hip", "pelvis" };
        private static readonly string[] FallbackSpine = { "Spine", "CC_Base_Spine01", "spine_01" };
        private static readonly string[] FallbackChest = { "Chest", "Spine1", "CC_Base_Spine02", "spine_02" };
        private static readonly string[] FallbackUpperChest = { "UpperChest", "Spine2", "spine_03" };
        private static readonly string[] FallbackNeck = { "Neck", "CC_Base_NeckTwist01", "neck_01" };
        private static readonly string[] FallbackHead = { "Head", "CC_Base_Head", "head" };
        private static readonly string[] FallbackLeftEye = { "LeftEye", "Eye_L", "CC_Base_L_Eye", "eye_l" };
        private static readonly string[] FallbackRightEye = { "RightEye", "Eye_R", "CC_Base_R_Eye", "eye_r" };
        private static readonly string[] FallbackLeftShoulder = { "LeftShoulder", "CC_Base_L_Clavicle", "clavicle_l" };
        private static readonly string[] FallbackRightShoulder = { "RightShoulder", "CC_Base_R_Clavicle", "clavicle_r" };
        private static readonly string[] FallbackLeftUpperArm = { "LeftArm", "CC_Base_L_Upperarm", "upperarm_l" };
        private static readonly string[] FallbackRightUpperArm = { "RightArm", "CC_Base_R_Upperarm", "upperarm_r" };
        private static readonly string[] FallbackLeftUpperLeg = { "LeftUpLeg", "Left leg", "L_Thigh", "CC_Base_L_Thigh", "thigh_l" };
        private static readonly string[] FallbackLeftLowerLeg = { "LeftLeg", "Left knee", "L_Calf", "CC_Base_L_Calf", "calf_l" };
        private static readonly string[] FallbackLeftFoot = { "LeftFoot", "Left ankle", "L_Foot", "CC_Base_L_Foot", "foot_l" };
        private static readonly string[] FallbackRightUpperLeg = { "RightUpLeg", "Right leg", "R_Thigh", "CC_Base_R_Thigh", "thigh_r" };
        private static readonly string[] FallbackRightLowerLeg = { "RightLeg", "Right knee", "R_Calf", "CC_Base_R_Calf", "calf_r" };
        private static readonly string[] FallbackRightFoot = { "RightFoot", "Right ankle", "R_Foot", "CC_Base_R_Foot", "foot_r" };

        private static string[] MapToFallbackNames(StandardBone semantic)
        {
            return semantic switch
            {
                StandardBone.Hips => FallbackHips,
                StandardBone.Spine => FallbackSpine,
                StandardBone.Chest => FallbackChest,
                StandardBone.UpperChest => FallbackUpperChest,
                StandardBone.Neck => FallbackNeck,
                StandardBone.Head => FallbackHead,
                StandardBone.LeftEye => FallbackLeftEye,
                StandardBone.RightEye => FallbackRightEye,
                StandardBone.LeftShoulder => FallbackLeftShoulder,
                StandardBone.RightShoulder => FallbackRightShoulder,
                StandardBone.LeftUpperArm => FallbackLeftUpperArm,
                StandardBone.RightUpperArm => FallbackRightUpperArm,
                StandardBone.LeftUpperLeg => FallbackLeftUpperLeg,
                StandardBone.LeftLowerLeg => FallbackLeftLowerLeg,
                StandardBone.LeftFoot => FallbackLeftFoot,
                StandardBone.RightUpperLeg => FallbackRightUpperLeg,
                StandardBone.RightLowerLeg => FallbackRightLowerLeg,
                StandardBone.RightFoot => FallbackRightFoot,
                _ => null
            };
        }

        /// <summary>Which resolution route supplied a bone. See <see cref="GetBoneSource" />.</summary>
        internal enum BoneSource
        {
            /// <summary>No route found this bone on this rig.</summary>
            Unresolved = 0,

            /// <summary>An explicit override assigned on this component.</summary>
            Manual = 1,

            /// <summary>The character's humanoid avatar mapped it.</summary>
            HumanoidAvatar = 2,

            /// <summary>Matched by name against the known rig conventions.</summary>
            NameMatch = 3
        }

        private readonly struct BlendshapeResolution
        {
            public BlendshapeResolution(SkinnedMeshRenderer mesh, int index)
            {
                Mesh = mesh;
                Index = index;
            }

            public SkinnedMeshRenderer Mesh { get; }
            public int Index { get; }
        }

        private readonly struct MeshResolutionScore
        {
            public MeshResolutionScore(int semanticMatchCount, int namePriority, int originalIndex)
            {
                SemanticMatchCount = semanticMatchCount;
                NamePriority = namePriority;
                OriginalIndex = originalIndex;
            }

            public int SemanticMatchCount { get; }
            public int NamePriority { get; }
            public int OriginalIndex { get; }
        }
    }
}
