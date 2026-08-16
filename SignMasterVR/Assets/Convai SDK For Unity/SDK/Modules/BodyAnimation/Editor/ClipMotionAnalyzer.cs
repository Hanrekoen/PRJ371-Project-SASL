using System.Collections.Generic;
using System.Text;
using Convai.Domain.Logging;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Measures the motion hidden inside in-place Humanoid clips and writes it into
    ///     <see cref="ClipMotionMetadata" /> — the data the runtime needs for zero-slide
    ///     NavMesh sync. For every locomotion clip in a set it samples the clip over the
    ///     clip's own source rig (via a manually evaluated PlayableGraph) and derives:
    ///     ground speed and distance from stance-foot displacement (an in-place clip's
    ///     planted foot slides backwards under the root at exactly ground speed), the yaw
    ///     curve from hips heading, and foot-plant times from foot-height contact analysis.
    /// </summary>
    public static class ClipMotionAnalyzer
    {
        private const int SamplesPerSecond = 60;
        private const float ContactHeightThreshold = 0.025f; // meters above per-foot minimum
        private const int ContactDebounceSamples = 3;

        /// <summary>
        ///     Analyzes whichever Animation Set is selected in the Project window.
        /// </summary>
        /// <remarks>
        ///     No longer a menu entry. Every set the analysis applies to is an asset you select to
        ///     reach in the first place, and its inspector — plus the Set Builder — already runs
        ///     this from a button that is visible at exactly that moment. The menu row asked the
        ///     user to select the asset, then look away from it to a menu that was greyed out
        ///     whenever they had not.
        /// </remarks>
        public static void AnalyzeSelectedSetMenu()
        {
            if (Selection.activeObject is not ConvaiBodyAnimationSet set)
                return;

            int analyzed = AnalyzeSet(set);
            EditorUtility.DisplayDialog(
                "Convai Body Animation",
                $"Clip motion analysis complete.\nAnalyzed {analyzed} locomotion clip(s) — see Console for the report.",
                "OK");
        }

        /// <summary>
        ///     True when the Project window's selection is an Animation Set, and so when
        ///     <see cref="AnalyzeSelectedSetMenu" /> would do something.
        /// </summary>
        /// <remarks>
        ///     This was the validator for the menu entry that used to sit above it. It is kept as
        ///     public surface — it shipped in 4.5.0 — and still guards the same call for anyone
        ///     driving the analyzer from their own tooling.
        /// </remarks>
        public static bool AnalyzeSelectedSetMenuValidate() => Selection.activeObject is ConvaiBodyAnimationSet;

        /// <summary>
        ///     Analyzes every assigned locomotion clip in the set, writes the metadata, saves
        ///     the asset, and logs a per-clip report. Returns the number of clips analyzed.
        ///     Pass <paramref name="confirm" /> false for automated pipelines that must not
        ///     show the overwrite-confirmation dialog.
        /// </summary>
        public static int AnalyzeSet(ConvaiBodyAnimationSet set, bool confirm = true)
        {
            if (confirm && !UnityEngine.Application.isBatchMode)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Convai Body Animation — Analyze Clip Motion",
                    $"This will overwrite the locomotion clip motion metadata on '{set.DisplayName}'. " +
                    "Any existing or hand-tuned metadata (speed, distance, yaw, foot plants) will be " +
                    "replaced. This cannot be undone from this dialog (Ctrl+Z after the fact will work).\n\n" +
                    "Continue?",
                    "Analyze", "Cancel");
                if (!proceed) return 0;
            }

            var slots = new List<(string slot, LocomotionClip clip)>();
            set.Locomotion.CollectAssigned(slots);

            Undo.RecordObject(set, "Analyze Locomotion Clip Metadata");

            var report = new StringBuilder();
            report.AppendLine($"[ClipMotionAnalyzer] Report for set '{set.DisplayName}':");
            report.AppendLine(
                "slot | clip | len(s) | speed(m/s) | dist(m) | yaw(°) | L-plants | R-plants");

            int analyzed = 0;
            try
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    (string slot, LocomotionClip clip) = slots[i];
                    EditorUtility.DisplayProgressBar(
                        "Convai Body Animation — Analyzing Clip Motion",
                        clip.ClipName, (float)i / slots.Count);

                    if (!TryAnalyzeClip(clip.Clip, out ClipMotionResult result, out string failureReason))
                    {
                        report.AppendLine($"{slot} | {clip.ClipName} | FAILED: {failureReason}");
                        continue;
                    }

                    clip.Metadata.SetAnalyzed(
                        result.AverageSpeed,
                        result.TotalDistance,
                        result.TotalYaw,
                        result.DistanceCurve,
                        result.YawCurve,
                        result.LeftPlants,
                        result.RightPlants,
                        authoredMotionScale: result.AuthoredMotionScale);
                    analyzed++;

                    report.AppendLine(
                        $"{slot} | {clip.ClipName} | {clip.Length:F2} | {result.AverageSpeed:F2} | " +
                        $"{result.TotalDistance:F2} | {result.TotalYaw:F0} | " +
                        $"{result.LeftPlants.Length} | {result.RightPlants.Length} | " +
                        $"scale={result.AuthoredMotionScale:F2}");

                    WarnOnSuspiciousData(slot, clip, result, report);

                    if (result.NonUniformSampleRigScale)
                    {
                        report.AppendLine(
                            $"  {ConvaiEditorGlyphs.Status.Warn} {clip.ClipName}: sample rig has non-uniform scale (x/y/z differ by " +
                            "more than 1%) — the recorded rig motion scale may be inaccurate. Verify " +
                            "the sample rig's transform scale is uniform.");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            ConvaiLogger.Info(report.ToString(), LogCategory.Animation);
            return analyzed;
        }

        private static void WarnOnSuspiciousData(
            string slot, LocomotionClip clip, in ClipMotionResult result, StringBuilder report)
        {
            bool isTurn = slot.StartsWith("Turn");
            if (isTurn && Mathf.Abs(result.TotalYaw) < 20f)
            {
                report.AppendLine(
                    $"  {ConvaiEditorGlyphs.Status.Warn} {clip.ClipName}: turn clip measured only {result.TotalYaw:F0}° yaw — " +
                    "the retarget may have dropped root rotation. Set Authored Yaw manually.");
            }

            bool isCycle = slot is "Walk" or "Jog";
            if (isCycle && result.AverageSpeed < 0.3f)
            {
                report.AppendLine(
                    $"  {ConvaiEditorGlyphs.Status.Warn} {clip.ClipName}: movement cycle measured {result.AverageSpeed:F2} m/s — " +
                    "contact detection may have failed; check the clip import.");
            }
        }

        // ------------------------------------------------------------------ sampling

        private struct ClipMotionResult
        {
            public float AverageSpeed;
            public float TotalDistance;
            public float TotalYaw;
            public AnimationCurve DistanceCurve;
            public AnimationCurve YawCurve;
            public float[] LeftPlants;
            public float[] RightPlants;

            /// <summary>
            ///     The sample rig's combined motion scale (humanScale × uniform lossyScale
            ///     component) at analysis time — see <see cref="ClipMotionMetadata.AuthoredMotionScale" />.
            /// </summary>
            public float AuthoredMotionScale;

            /// <summary>True when the sample rig instance's lossyScale was not uniform enough to trust.</summary>
            public bool NonUniformSampleRigScale;
        }

        private static bool TryAnalyzeClip(
            AnimationClip clip, out ClipMotionResult result, out string failureReason)
        {
            result = default;
            failureReason = null;

            if (clip == null || clip.length <= 0.01f)
            {
                failureReason = "clip is missing or shorter than 0.01s";
                return false;
            }

            GameObject rig = ResolveSampleRig(clip, out Avatar avatarOverride, out failureReason);
            if (rig == null) return false;

            GameObject instance = Object.Instantiate(rig);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                if ((animator.avatar == null || !animator.avatar.isValid) && avatarOverride != null)
                    animator.avatar = avatarOverride;

                if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                {
                    failureReason = $"sample rig '{rig.name}' has no valid Humanoid avatar";
                    return false;
                }

                // The combined normalizing quantity for this measurement: humanScale alone is
                // not enough because the foot positions below are sampled in WORLD space on
                // this instantiated rig, so the recorded metres already include the instance's
                // transform scale too (see ClipMotionMetadata.AuthoredMotionScale docs).
                float uniformInstanceScale = UniformScaleOf(animator.transform.lossyScale, out bool nonUniformScale);
                float authoredMotionScale = animator.humanScale * uniformInstanceScale;

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // Root motion must NOT move the sampled instance: hips are measured in
                // world space, so an applied root rotation would be counted once in the
                // hips heading AND once again in the deltaRotation fold (turns measured
                // exactly 2× when the sample rig prefab shipped with root motion on).
                animator.applyRootMotion = false;

                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (leftFoot == null || rightFoot == null || hips == null)
                {
                    failureReason = $"sample rig '{rig.name}' has unmapped hips/feet bones";
                    return false;
                }

                int sampleCount = Mathf.Max(8, Mathf.CeilToInt(clip.length * SamplesPerSecond)) + 1;
                var leftPos = new Vector3[sampleCount];
                var rightPos = new Vector3[sampleCount];
                var hipsYaw = new float[sampleCount];

                PlayableGraph graph = PlayableGraph.Create("ClipMotionAnalyzer");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    AnimationPlayableOutput output =
                        AnimationPlayableOutput.Create(graph, "sample", animator);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    output.SetSourcePlayable(playable);
                    graph.Play();

                    float previousRawYaw = 0f;
                    float accumulatedYaw = 0f;

                    for (int i = 0; i < sampleCount; i++)
                    {
                        float time = clip.length * i / (sampleCount - 1);
                        playable.SetTime(time);
                        graph.Evaluate(0f);

                        leftPos[i] = leftFoot.position;
                        rightPos[i] = rightFoot.position;

                        Vector3 forward = hips.forward;
                        forward.y = 0f;
                        float rawYaw = forward.sqrMagnitude > 1e-6f
                            ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
                            : previousRawYaw;

                        if (i == 0)
                        {
                            previousRawYaw = rawYaw;
                        }
                        else
                        {
                            accumulatedYaw += Mathf.DeltaAngle(previousRawYaw, rawYaw);
                            previousRawYaw = rawYaw;

                            // Rotation that is NOT baked into the pose leaves the hips
                            // stationary and surfaces as root motion instead — fold the
                            // evaluated root delta in so the measured yaw is invariant
                            // under the clip's Bake Into Pose import setting.
                            accumulatedYaw += Mathf.DeltaAngle(0f, animator.deltaRotation.eulerAngles.y);
                        }

                        hipsYaw[i] = accumulatedYaw;
                    }
                }
                finally
                {
                    graph.Destroy();
                }

                result = PostProcess(clip, leftPos, rightPos, hipsYaw);
                result.AuthoredMotionScale = authoredMotionScale;
                result.NonUniformSampleRigScale = nonUniformScale;
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        ///     Resolves the Humanoid rig to sample the clip on. Animation-only FBX files often
        ///     import with "Copy From Other Avatar" (the shipped female library copies from the sample
        ///     Camila model), in which case their own prefab has no usable Animator — so the
        ///     avatar SOURCE model is preferred, falling back to the clip's own FBX prefab.
        /// </summary>
        private static GameObject ResolveSampleRig(
            AnimationClip clip, out Avatar avatarOverride, out string failureReason)
        {
            avatarOverride = null;
            failureReason = null;

            string clipPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(clipPath))
            {
                failureReason = "clip has no asset path (runtime clip?)";
                return null;
            }

            if (AssetImporter.GetAtPath(clipPath) is ModelImporter { sourceAvatar: { } sourceAvatar })
            {
                avatarOverride = sourceAvatar;
                string sourcePath = AssetDatabase.GetAssetPath(sourceAvatar);
                var sourceRig = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (sourceRig != null) return sourceRig;
            }

            var ownRig = AssetDatabase.LoadAssetAtPath<GameObject>(clipPath);
            if (ownRig == null)
                failureReason = $"no model prefab at '{clipPath}' and no avatar source model";
            return ownRig;
        }

        /// <summary>
        ///     Averages the three components of a lossyScale into one uniform factor and
        ///     reports whether they disagreed by more than ~1% — a non-uniformly scaled
        ///     sample rig makes the recorded motion scale ambiguous, so the caller surfaces
        ///     that as a warning rather than silently trusting a possibly-wrong number.
        /// </summary>
        private static float UniformScaleOf(Vector3 lossyScale, out bool nonUniform)
        {
            float average = (lossyScale.x + lossyScale.y + lossyScale.z) / 3f;
            nonUniform = false;
            if (average > 1e-5f)
            {
                float maxDeviation = Mathf.Max(
                    Mathf.Abs(lossyScale.x - average),
                    Mathf.Max(Mathf.Abs(lossyScale.y - average), Mathf.Abs(lossyScale.z - average)));
                nonUniform = maxDeviation / average > 0.01f;
            }
            return average;
        }

        // ------------------------------------------------------------------ post-processing

        private static ClipMotionResult PostProcess(
            AnimationClip clip, Vector3[] left, Vector3[] right, float[] hipsYaw)
        {
            int count = left.Length;
            float dt = clip.length / (count - 1);

            bool[] leftContact = ComputeContacts(left);
            bool[] rightContact = ComputeContacts(right);

            // Ground speed per interval from stance-foot horizontal displacement.
            var speeds = new float[count];
            float lastSpeed = 0f;
            for (int i = 1; i < count; i++)
            {
                float speed;
                bool hasLeft = leftContact[i] && leftContact[i - 1];
                bool hasRight = rightContact[i] && rightContact[i - 1];

                if (hasLeft && hasRight)
                    speed = (HorizontalSpeed(left, i, dt) + HorizontalSpeed(right, i, dt)) * 0.5f;
                else if (hasLeft)
                    speed = HorizontalSpeed(left, i, dt);
                else if (hasRight)
                    speed = HorizontalSpeed(right, i, dt);
                else
                    speed = lastSpeed; // airborne (jog flight phase): carry momentum

                speeds[i] = speed;
                lastSpeed = speed;
            }

            Smooth(speeds, 2);

            // Integrate to distance, build reduced curves.
            var distanceCurve = new AnimationCurve();
            var yawCurve = new AnimationCurve();
            float distance = 0f;
            int keyStride = Mathf.Max(1, count / 32);

            distanceCurve.AddKey(0f, 0f);
            yawCurve.AddKey(0f, 0f);
            for (int i = 1; i < count; i++)
            {
                distance += speeds[i] * dt;
                if (i % keyStride == 0 || i == count - 1)
                {
                    float t = (float)i / (count - 1);
                    distanceCurve.AddKey(t, distance);
                    yawCurve.AddKey(t, hipsYaw[i]);
                }
            }

            return new ClipMotionResult
            {
                AverageSpeed = clip.length > 0f ? distance / clip.length : 0f,
                TotalDistance = distance,
                TotalYaw = hipsYaw[count - 1],
                DistanceCurve = distanceCurve,
                YawCurve = yawCurve,
                LeftPlants = FindPlants(leftContact, count),
                RightPlants = FindPlants(rightContact, count)
            };
        }

        private static bool[] ComputeContacts(Vector3[] positions)
        {
            int count = positions.Length;
            float minY = float.MaxValue;
            for (int i = 0; i < count; i++)
                minY = Mathf.Min(minY, positions[i].y);

            var contact = new bool[count];
            for (int i = 0; i < count; i++)
                contact[i] = positions[i].y <= minY + ContactHeightThreshold;

            // Debounce: drop contact runs shorter than the minimum sample count.
            int runStart = -1;
            for (int i = 0; i <= count; i++)
            {
                bool value = i < count && contact[i];
                if (value && runStart < 0)
                {
                    runStart = i;
                }
                else if (!value && runStart >= 0)
                {
                    if (i - runStart < ContactDebounceSamples)
                    {
                        for (int j = runStart; j < i; j++)
                            contact[j] = false;
                    }
                    runStart = -1;
                }
            }

            return contact;
        }

        private static float HorizontalSpeed(Vector3[] positions, int index, float dt)
        {
            Vector3 delta = positions[index] - positions[index - 1];
            delta.y = 0f;
            return delta.magnitude / dt;
        }

        private static void Smooth(float[] values, int radius)
        {
            var source = (float[])values.Clone();
            for (int i = 0; i < values.Length; i++)
            {
                float sum = 0f;
                int n = 0;
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(values.Length - 1, i + radius); j++)
                {
                    sum += source[j];
                    n++;
                }
                values[i] = sum / n;
            }
        }

        private static float[] FindPlants(bool[] contact, int count)
        {
            var plants = new List<float>(4);
            for (int i = 1; i < count; i++)
            {
                if (contact[i] && !contact[i - 1])
                    plants.Add((float)i / (count - 1));
            }

            // A cycle that starts mid-stance counts the initial contact as a plant at 0.
            if (plants.Count == 0 && contact[0])
                plants.Add(0f);

            return plants.ToArray();
        }
    }
}
