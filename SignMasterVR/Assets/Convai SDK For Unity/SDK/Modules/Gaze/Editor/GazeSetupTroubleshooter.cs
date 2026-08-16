using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Providers;
using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>Severity of a single Gaze Setup Troubleshooter finding.</summary>
    internal enum GazeSetupSeverity
    {
        Ok,
        Info,
        Warning,
        Error
    }

    /// <summary>One actionable finding from the Gaze Setup Troubleshooter.</summary>
    internal struct GazeSetupFinding
    {
        /// <summary>How serious the finding is.</summary>
        public GazeSetupSeverity Severity;

        /// <summary>Short label, e.g. "Head Bone".</summary>
        public string Title;

        /// <summary>Actionable message stating the consequence and the fix.</summary>
        public string Message;

        /// <summary>
        ///     The repair a surface can offer for this finding, or <see cref="GazeFixId.None" />
        ///     when only the user can resolve it.
        /// </summary>
        /// <remarks>
        ///     Authored here rather than joined on by each consumer: a finding and the button that
        ///     fixes it are one fact, and a surface that maps them itself is a second source of
        ///     truth waiting to drift from this one. <see cref="GazeSetupService.DescribeFix" />
        ///     turns it into the button's label.
        /// </remarks>
        public GazeFixId Fix;
    }

    /// <summary>Which mechanism actually drives a character's eyes, decided deterministically.</summary>
    internal enum GazeEyeBackend
    {
        /// <summary>A paired LeftEye/RightEye bone mapping drives the eyes.</summary>
        EyeBones,

        /// <summary>No eye bones, but a complete binocular EyeLook* blendshape set drives them.</summary>
        EyeLookBlendshapes,

        /// <summary>Neither resolved — gaze degrades to head-only motion.</summary>
        HeadOnly
    }

    /// <summary>
    ///     The bones the gaze stack resolves on a character, as transforms rather than booleans, so
    ///     a surface can name the bone it found instead of only reporting that it found one.
    /// </summary>
    /// <remarks>
    ///     Resolution order matches the runtime's: an authored <see cref="StandardRigBinding" />
    ///     mapping wins, then a Humanoid Avatar mapping, then the built-in generic name list. The
    ///     boolean fields on <see cref="GazeSetupInput" /> are derived from this, so there is one
    ///     resolution path rather than one per surface.
    /// </remarks>
    internal readonly struct GazeBoneReport
    {
        internal GazeBoneReport(
            Transform head,
            Transform neck,
            Transform leftEye,
            Transform rightEye,
            bool hasAnimator,
            bool isHumanoid,
            bool inferredFromNames,
            int rigBindingCount)
        {
            Head = head;
            Neck = neck;
            LeftEye = leftEye;
            RightEye = rightEye;
            HasAnimator = hasAnimator;
            IsHumanoid = isHumanoid;
            InferredFromNames = inferredFromNames;
            RigBindingCount = rigBindingCount;
        }

        /// <summary>The head bone gaze will rotate, or <c>null</c> when none resolved.</summary>
        public Transform Head { get; }

        /// <summary>The neck bone, or <c>null</c>. Optional — the head carries the swing without it.</summary>
        public Transform Neck { get; }

        /// <summary>The left eye bone, or <c>null</c>.</summary>
        public Transform LeftEye { get; }

        /// <summary>The right eye bone, or <c>null</c>.</summary>
        public Transform RightEye { get; }

        /// <summary>Whether an Animator was found under the character root.</summary>
        public bool HasAnimator { get; }

        /// <summary>Whether the Animator's avatar is Humanoid.</summary>
        public bool IsHumanoid { get; }

        /// <summary>Whether these came from the generic name list rather than an authored mapping.</summary>
        public bool InferredFromNames { get; }

        /// <summary>Number of authored rig bindings under the character root.</summary>
        public int RigBindingCount { get; }
    }

    /// <summary>
    ///     Everything the Gaze Setup Troubleshooter needs to evaluate a character's setup. Gathered
    ///     from the scene/asset state so <see cref="GazeSetupTroubleshooter.Evaluate" /> stays a pure,
    ///     testable function.
    /// </summary>
    internal struct GazeSetupInput
    {
        /// <summary>
        ///     The bones behind the boolean fields below. Populated by
        ///     <see cref="GazeSetupTroubleshooter.GatherFrom" />; left at its default by callers that
        ///     construct an input by hand, which <see cref="GazeSetupTroubleshooter.Evaluate" />
        ///     never reads.
        /// </summary>
        public GazeBoneReport Bones;

        /// <summary>Whether an Animator was found under the character root.</summary>
        public bool HasAnimator;

        /// <summary>Whether the Animator's avatar is Humanoid.</summary>
        public bool IsHumanoid;

        /// <summary>Whether the semantic rig binding resolves a Head bone.</summary>
        public bool HasHeadBone;

        /// <summary>Whether the semantic rig binding resolves a Neck bone.</summary>
        public bool HasNeckBone;

        /// <summary>Whether the semantic rig binding resolves a LeftEye bone.</summary>
        public bool HasLeftEyeBone;

        /// <summary>Whether the semantic rig binding resolves a RightEye bone.</summary>
        public bool HasRightEyeBone;

        /// <summary>Blendshape names containing "eyelook" (case-insensitive) across renderers.</summary>
        public int EyeLookShapeCount;

        /// <summary>Whether horizontal EyeLook shapes resolve for both left and right eyes.</summary>
        public bool HasCompleteEyeLookBackend;

        /// <summary>Whether a camera is tagged MainCamera.</summary>
        public bool HasMainCamera;

        /// <summary>Whether a Gaze Profile asset is assigned.</summary>
        public bool HasProfileAsset;

        /// <summary>Whether the controller will auto-create a Gaze Player Anchor when no provider exists.</summary>
        public bool AutoCreatePlayerAnchor;

        /// <summary>Count of <see cref="IGazeTargetProvider" /> components under the character root.</summary>
        public int ProviderCount;

        /// <summary>Number of authored semantic rig bindings under the character root.</summary>
        public int RigBindingCount;

        /// <summary>
        ///     True when the detected generic bones are name-based candidates rather than
        ///     mappings resolved by an authored semantic rig binding.
        /// </summary>
        public bool HasInferredBoneCandidates;
    }

    /// <summary>
    ///     Evaluates the setup gathered from a character's rig, camera, and profile into
    ///     actionable findings — the "SETUP" section of the <c>ConvaiGazeController</c>
    ///     inspector answers "what did the gaze stack actually resolve on THIS character?"
    /// </summary>
    internal static class GazeSetupTroubleshooter
    {
        /// <summary>
        ///     Evaluates <paramref name="input" /> into <paramref name="results" />
        ///     (cleared first). Pure and allocation-free beyond the list itself, so it is
        ///     directly unit-testable without a scene.
        /// </summary>
        internal static void Evaluate(in GazeSetupInput input, List<GazeSetupFinding> results)
        {
            results.Clear();

            if (input.RigBindingCount > 1)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Error,
                    Title = "Rig Binding Ownership",
                    Message = "Multiple Character Rig components exist under this character. Keep " +
                              "exactly one on the Embodiment Context root; root ownership wins at runtime, " +
                              "but duplicate authoring is rejected to prevent ambiguous setup.",
                    Fix = GazeFixId.RemoveDuplicateRigBinding
                });
            }

            if (!input.HasAnimator && input.HasHeadBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Info,
                    Title = input.HasInferredBoneCandidates ? "Custom Rig Candidate" : "Custom Rig",
                    Message = input.HasInferredBoneCandidates
                        ? "No Animator was found. Recognized bone names are a setup candidate, not an " +
                          "authored semantic mapping yet. Add Character Rig and verify the Head/Eye " +
                          "assignments before shipping; local +Z must be visual forward and +Y up."
                        : "No Animator was found, but the semantic Head mapping is valid. Gaze can " +
                          "drive this animator-free rig when local +Z is visual forward and +Y is up. " +
                          "Ensure another system restores the authored base pose before Convai's " +
                          "expression pass."
                });
            }
            else if (input.HasAnimator && !input.IsHumanoid && input.HasHeadBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Info,
                    Title = input.HasInferredBoneCandidates ? "Generic Rig Candidate" : "Generic Rig",
                    Message = input.HasInferredBoneCandidates
                        ? "The Generic Animator has recognized bone-name candidates, not verified semantic " +
                          "mappings. Add Character Rig and verify the Head/Eye assignments; the rig " +
                          "root's local +Z must be visual forward and +Y up."
                        : "The Generic Animator is supported through semantic bone mapping when the " +
                          "rig root's local +Z axis is the character's visual forward and +Y is up. " +
                          "Verify the Head/Eye assignments and imported root orientation before shipping."
                });
            }

            if (!input.HasHeadBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Error,
                    Title = "Head Bone",
                    Message = "No Head bone is mapped — head/eye gaze stays inert until it exists. " +
                              "Assign Head in Character Rig or use a recognized bone name.",
                    Fix = GazeFixId.AddRigBinding
                });
            }
            else if (!input.HasNeckBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Info,
                    Title = "Neck Bone",
                    Message = "No Neck bone is mapped — head rotation carries the full swing (still " +
                              "correct, slightly stiffer). Map a Neck bone in the avatar for the " +
                              "softest result."
                });
            }

            if (input.HasLeftEyeBone && input.HasRightEyeBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Ok,
                    Title = "Eye Backend",
                    Message = "Eye bones resolved — eyes are driven by bone rotation."
                });
            }
            else if (input.HasCompleteEyeLookBackend)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Info,
                    Title = "Eye Backend",
                    Message = $"No eye bones were resolved, so the EyeLook* blendshape backend will " +
                              $"drive the eyes ({input.EyeLookShapeCount} shapes found)."
                });
            }
            else if (input.HasLeftEyeBone || input.HasRightEyeBone)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Warning,
                    Title = "Eye Backend",
                    Message = "Only one eye bone resolved and no complete binocular EyeLook* backend " +
                              "was found, so gaze safely uses the head-only backend. Map the missing eye " +
                              "or provide horizontal EyeLook shapes for both eyes."
                });
            }
            else
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Warning,
                    Title = "Eye Backend",
                    Message = "No eye bones and no EyeLook* blendshapes were found — the eye stage will " +
                              "gracefully use head-only gaze. Map LeftEye/RightEye bones or provide " +
                              "EyeLook* shapes for full-fidelity eye motion."
                });
            }

            if (!input.HasMainCamera)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Warning,
                    Title = "Player Camera",
                    Message = "No camera carries the MainCamera tag — the fallback picks the first " +
                              "enabled Game-view camera, which may be wrong in multi-camera scenes. " +
                              "Tag your view camera as MainCamera or set Player Anchor Override.",
                    Fix = GazeFixId.UseSceneCameraAsPlayer
                });
            }

            if (!input.HasProfileAsset)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Info,
                    Title = "Profile",
                    Message = "No profile asset is assigned — SDK runtime defaults are used. Assign a " +
                              "Gaze Profile to author state policies and idle life.",
                    Fix = GazeFixId.AssignDefaultProfile
                });
            }

            if (input.ProviderCount == 0 && !input.AutoCreatePlayerAnchor)
            {
                results.Add(new GazeSetupFinding
                {
                    Severity = GazeSetupSeverity.Warning,
                    Title = "Target Providers",
                    Message = "No target provider exists and auto-creation is off — the character will " +
                              "only show ambient idle life. Add a Gaze Player Anchor or enable Auto " +
                              "Create Player Anchor.",
                    Fix = GazeFixId.AddPlayerAnchor
                });
            }
        }

        /// <summary>
        ///     Gathers the setup state of <paramref name="controller" />'s character straight
        ///     from the scene/assets (editor-only; not part of the runtime assembly).
        /// </summary>
        internal static GazeSetupInput GatherFrom(
            ConvaiGazeController controller, SerializedProperty profileProp, bool autoCreateAnchor)
        {
            var input = new GazeSetupInput { AutoCreatePlayerAnchor = autoCreateAnchor };
            if (controller == null) return input;

            EmbodimentContext context = controller.GetComponentInParent<EmbodimentContext>(true);
            Transform root = context != null ? context.transform : controller.transform;

            input.Bones = ResolveBones(root);
            input.HasAnimator = input.Bones.HasAnimator;
            input.IsHumanoid = input.Bones.IsHumanoid;
            input.RigBindingCount = input.Bones.RigBindingCount;
            input.HasHeadBone = input.Bones.Head != null;
            input.HasNeckBone = input.Bones.Neck != null;
            input.HasLeftEyeBone = input.Bones.LeftEye != null;
            input.HasRightEyeBone = input.Bones.RightEye != null;
            input.HasInferredBoneCandidates = input.Bones.InferredFromNames;

            StandardRigBinding binding = ResolveBinding(root);
            input.EyeLookShapeCount = Mathf.Max(CountEyeLookShapes(root), CountResolvedEyeLookShapes(binding));
            input.HasCompleteEyeLookBackend = HasCompleteEyeLookBackend(root, binding);
            input.HasMainCamera = Camera.main != null;
            input.HasProfileAsset = profileProp != null && profileProp.objectReferenceValue != null;
            input.ProviderCount = root.GetComponentsInChildren<IGazeTargetProvider>(true).Length;

            return input;
        }

        /// <summary>
        ///     Resolves the head, neck and eye bones the way the runtime does, from the character
        ///     root down. Read-only: it never adds or rebuilds a component, so it is safe to call
        ///     while merely drawing an inspector.
        /// </summary>
        internal static GazeBoneReport ResolveBones(Transform root)
        {
            if (root == null) return default;

            Animator animator = root.GetComponentInChildren<Animator>(true);
            bool hasAnimator = animator != null;
            bool isHumanoid = animator != null && animator.isHuman && animator.avatar != null;

            StandardRigBinding[] bindings = root.GetComponentsInChildren<StandardRigBinding>(true);
            StandardRigBinding binding = ResolveBinding(root, bindings);

            Transform head = null;
            Transform neck = null;
            Transform leftEye = null;
            Transform rightEye = null;
            bool inferredFromNames = false;

            if (binding != null)
            {
                binding.TryGetBone(StandardBone.Head, out head);
                binding.TryGetBone(StandardBone.Neck, out neck);
                binding.TryGetBone(StandardBone.LeftEye, out leftEye);
                binding.TryGetBone(StandardBone.RightEye, out rightEye);
            }
            else if (!isHumanoid)
            {
                // Mirror StandardRigBinding's stable generic-name fallback without adding or
                // rebuilding components while merely drawing the inspector.
                head = FindNamedTransform(root, "Head", "CC_Base_Head", "head");
                neck = FindNamedTransform(root, "Neck", "CC_Base_NeckTwist01", "neck_01");
                leftEye = FindNamedTransform(root, "LeftEye", "Eye_L", "CC_Base_L_Eye", "eye_l");
                rightEye = FindNamedTransform(root, "RightEye", "Eye_R", "CC_Base_R_Eye", "eye_r");
                inferredFromNames = head != null || neck != null || leftEye != null || rightEye != null;
            }

            if (isHumanoid)
            {
                head ??= animator.GetBoneTransform(HumanBodyBones.Head);
                neck ??= animator.GetBoneTransform(HumanBodyBones.Neck);
                leftEye ??= animator.GetBoneTransform(HumanBodyBones.LeftEye);
                rightEye ??= animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

            return new GazeBoneReport(
                head, neck, leftEye, rightEye, hasAnimator, isHumanoid, inferredFromNames, bindings.Length);
        }

        /// <summary>
        ///     Which mechanism will actually drive the eyes. Deterministic and shared, so no surface
        ///     can describe a backend the solver would not choose: a paired eye-bone mapping wins,
        ///     then a complete binocular EyeLook* set, otherwise head-only.
        /// </summary>
        internal static GazeEyeBackend ResolveEyeBackend(in GazeSetupInput input)
        {
            if (input.HasLeftEyeBone && input.HasRightEyeBone) return GazeEyeBackend.EyeBones;
            return input.HasCompleteEyeLookBackend
                ? GazeEyeBackend.EyeLookBlendshapes
                : GazeEyeBackend.HeadOnly;
        }

        /// <summary>The binding that owns the rig: a root binding is authoritative, else the first found.</summary>
        internal static StandardRigBinding ResolveBinding(Transform root) =>
            ResolveBinding(root, root != null
                ? root.GetComponentsInChildren<StandardRigBinding>(true)
                : Array.Empty<StandardRigBinding>());

        private static StandardRigBinding ResolveBinding(Transform root, StandardRigBinding[] bindings)
        {
            StandardRigBinding binding = root != null ? root.GetComponent<StandardRigBinding>() : null;
            if (binding == null && bindings.Length > 0) binding = bindings[0];
            return binding;
        }

        private static int CountEyeLookShapes(Transform root)
        {
            int count = 0;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                if (mesh == null) continue;

                int blendShapeCount = mesh.blendShapeCount;
                for (int b = 0; b < blendShapeCount; b++)
                {
                    string shapeName = mesh.GetBlendShapeName(b);
                    if (IsRecognizedEyeLookShape(shapeName))
                        count++;
                }
            }

            return count;
        }

        private static bool IsRecognizedEyeLookShape(string shapeName)
        {
            if (string.IsNullOrEmpty(shapeName)) return false;
            if (shapeName.IndexOf("eyelook", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return string.Equals(shapeName, "Eye_L_Look_R", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(shapeName, "Eye_L_Look_L", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(shapeName, "Eye_R_Look_L", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(shapeName, "Eye_R_Look_R", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountResolvedEyeLookShapes(StandardRigBinding binding)
        {
            if (binding == null) return 0;

            StandardBlendshape[] semantics =
            {
                StandardBlendshape.EyeLookUpLeft,
                StandardBlendshape.EyeLookDownLeft,
                StandardBlendshape.EyeLookInLeft,
                StandardBlendshape.EyeLookOutLeft,
                StandardBlendshape.EyeLookUpRight,
                StandardBlendshape.EyeLookDownRight,
                StandardBlendshape.EyeLookInRight,
                StandardBlendshape.EyeLookOutRight
            };

            int count = 0;
            for (int i = 0; i < semantics.Length; i++)
                if (binding.TryGetBlendshape(semantics[i], out _, out _)) count++;
            return count;
        }

        private static Transform FindNamedTransform(Transform root, params string[] names)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < names.Length; n++)
                for (int i = 0; i < transforms.Length; i++)
                    if (string.Equals(transforms[i].name, names[n], StringComparison.OrdinalIgnoreCase))
                        return transforms[i];
            return null;
        }

        private static bool HasCompleteEyeLookBackend(Transform root, StandardRigBinding binding)
        {
            if (binding != null)
            {
                return binding.TryGetBlendshape(StandardBlendshape.EyeLookInLeft, out _, out _) &&
                       binding.TryGetBlendshape(StandardBlendshape.EyeLookOutLeft, out _, out _) &&
                       binding.TryGetBlendshape(StandardBlendshape.EyeLookInRight, out _, out _) &&
                       binding.TryGetBlendshape(StandardBlendshape.EyeLookOutRight, out _, out _);
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool rawInLeft = HasAnyShapeNamed(renderers, "eyeLookInLeft", "Eye_L_Look_R");
            bool rawOutLeft = HasAnyShapeNamed(renderers, "eyeLookOutLeft", "Eye_L_Look_L");
            bool rawInRight = HasAnyShapeNamed(renderers, "eyeLookInRight", "Eye_R_Look_L");
            bool rawOutRight = HasAnyShapeNamed(renderers, "eyeLookOutRight", "Eye_R_Look_R");
            return rawInLeft && rawOutLeft && rawInRight && rawOutRight;
        }

        private static bool HasAnyShapeNamed(SkinnedMeshRenderer[] renderers, params string[] names)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                if (mesh == null) continue;
                for (int n = 0; n < names.Length; n++)
                    if (mesh.GetBlendShapeIndex(names[n]) >= 0) return true;
            }
            return false;
        }
    }
}
