using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     Single writer for the character's shared additive spine/shoulder/head-gesture chain:
    ///     one guard, one restore per frame, per-channel accumulation across
    ///     every contributor, and exactly one composed write per bone per apply. Replaces the
    ///     retired BodyLanguage-private chain calibration/write-guard pair with a Runtime-owned
    ///     type both BodyLanguage (spine chain, shoulders, head-gesture fallback) and Gaze (the
    ///     late torso-aim entry) can share through
    ///     <see cref="Convai.Runtime.Embodiment.EmbodimentContext" /> without a module-to-module
    ///     reference.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Frame protocol: the BodyPose-slot owner (<c>ConvaiBodyLanguageController</c>)
    ///         calls <see cref="BeginFrame" /> once at the top of its actuation tick, issues any
    ///         number of <c>Add*</c> accumulator calls, then <see cref="ApplyAccumulated" />
    ///         once. Gaze's head/torso solver — which runs later in the frame, at the Gaze
    ///         execution order — calls <see cref="ComposeTorsoAim" /> directly; that call is
    ///         idempotence-safe even when BodyLanguage never ran this frame (disabled, inert)
    ///         because it calls <see cref="EnsureFrameStarted" /> itself.
    ///     </para>
    ///     <para>
    ///         Only one instance exists per character, owned by whichever component registers
    ///         the BodyPose slot (see <c>EmbodimentContext.RegisterProceduralPoseCompositor</c>).
    ///     </para>
    /// </remarks>
    internal sealed class ProceduralPoseCompositor
    {
        /// <summary>
        ///     Neck's share of a head-gesture pitch/yaw/roll delta; Head always gets the full
        ///     amount. Internal (not private) so <c>ConvaiBodyLanguageController</c>'s explicit
        ///     neck-lead fallback path (<see cref="AddNeckGesture" />) can share
        ///     this single owned constant rather than mirroring the value in a second place.
        /// </summary>
        internal const float NeckGestureShare = 0.4f;

        /// <summary>
        ///     Chest's share of a torso aim; UpperChest carries the exact remainder. The upper
        ///     thoracic spine rotates more freely than the lower, so the majority sits above.
        /// </summary>
        internal const float ChestAimShare = 0.45f;

        /// <summary>
        ///     Human biomechanical caps for the per-channel <see cref="MotorFilter" /> layer.
        ///     Each output channel splits into a TONIC lane (slow, postural —
        ///     posture, breath, sway, stance) and, where fast transients exist, a BALLISTIC lane
        ///     (fast, gestural — reactions, shrugs, head gestures), each filtered under its own
        ///     caps and summed AFTER filtering. In-budget producers pass through numerically
        ///     unchanged (see <see cref="MotorFilter" />); only super-human rates are eaten.
        ///     Every value here is a feel-pass tunable.
        /// </summary>
        private static class MotorLimits
        {
            /// <summary>Feel-pass tunable: tonic spine sagittal/lateral max speed (°/s).</summary>
            public const float SpineTonicMaxSpeed = 45f;

            /// <summary>Feel-pass tunable: tonic spine sagittal/lateral max acceleration (°/s²).</summary>
            public const float SpineTonicMaxAccel = 100f;

            /// <summary>Feel-pass tunable: ballistic spine sagittal/lateral max speed (°/s).</summary>
            public const float SpineBallisticMaxSpeed = 240f;

            /// <summary>Feel-pass tunable: ballistic spine sagittal/lateral max acceleration (°/s²).</summary>
            public const float SpineBallisticMaxAccel = 2000f;

            /// <summary>Feel-pass tunable: tonic shoulder lift/tension max speed (°/s).</summary>
            public const float ShoulderTonicMaxSpeed = 60f;

            /// <summary>Feel-pass tunable: tonic shoulder lift/tension max acceleration (°/s²).</summary>
            public const float ShoulderTonicMaxAccel = 240f;

            /// <summary>Feel-pass tunable: ballistic shoulder lift max speed (°/s).</summary>
            public const float ShoulderBallisticMaxSpeed = 300f;

            /// <summary>Feel-pass tunable: ballistic shoulder lift max acceleration (°/s²).</summary>
            public const float ShoulderBallisticMaxAccel = 2500f;

            /// <summary>Feel-pass tunable: head-gesture per-axis max speed (°/s) — ballistic by nature.</summary>
            public const float HeadGestureMaxSpeed = 240f;

            /// <summary>Feel-pass tunable: head-gesture per-axis max acceleration (°/s²).</summary>
            public const float HeadGestureMaxAccel = 1600f;

            /// <summary>Feel-pass tunable: pelvis lateral max speed (m/s) — tonic only.</summary>
            public const float PelvisLateralMaxSpeed = 0.06f;

            /// <summary>Feel-pass tunable: pelvis lateral max acceleration (m/s²).</summary>
            public const float PelvisLateralMaxAccel = 0.25f;

            /// <summary>Feel-pass tunable: pelvis obliquity/yaw max speed (°/s) — tonic only.</summary>
            public const float PelvisAngularMaxSpeed = 10f;

            /// <summary>Feel-pass tunable: pelvis obliquity/yaw max acceleration (°/s²).</summary>
            public const float PelvisAngularMaxAccel = 30f;
        }

        private readonly AnimatedAdditivePoseGuard _guard = new();

        private Transform _estimatorBone;
        private int _frameStamp;

        private float _spineSagittalAccum;
        private float _spineLateralAccum;
        private float _postureOpennessAccum;
        private float _postureLeanAccum;
        private float _spineSagittalBallisticAccum;
        private float _spineLateralBallisticAccum;
        private float _shoulderLiftAccum;
        private float _shoulderLiftBallisticAccum;
        private float _shoulderTensionAccum;
        private float _headPitchAccum;
        private float _headYawAccum;
        private float _headRollAccum;

        // Explicit neck-lead accumulators: a separate channel from the head's
        // own accumulators above, written only by AddNeckGesture. When nothing calls
        // AddNeckGesture this frame, the Neck bone still gets its usual share-split of the
        // filtered HEAD accumulators below (byte-compatible with every earlier caller/test).
        private float _neckPitchAccum;
        private float _neckYawAccum;
        private float _neckRollAccum;
        private bool _neckGestureExplicit;

        private float _breathSagittalForStabilization;
        private float _breathStabilization01;

        private float _pelvisLateralAccum;
        private float _pelvisObliquityAccum;
        private float _pelvisYawAccum;

        // Per-channel motor filters — persistent across frames (they carry
        // velocity state), reset ONLY in ResetFrameProtocolState (Bind/Clear paths), never per
        // frame. Torso-aim (ComposeTorsoAim) is deliberately unfiltered: Gaze springs it.
        private MotorFilter _spineSagittalTonicFilter;
        private MotorFilter _spineLateralTonicFilter;
        private MotorFilter _postureOpennessFilter;
        private MotorFilter _postureLeanFilter;
        private MotorFilter _spineSagittalBallisticFilter;
        private MotorFilter _spineLateralBallisticFilter;
        private MotorFilter _shoulderLiftTonicFilter;
        private MotorFilter _shoulderLiftBallisticFilter;
        private MotorFilter _shoulderTensionFilter;
        private MotorFilter _headGesturePitchFilter;
        private MotorFilter _headGestureYawFilter;
        private MotorFilter _headGestureRollFilter;

        // Neck's own motor filters — separate state from the head's filters
        // above so the neck's explicit lead signal is filtered independently under the same
        // ballistic head-gesture caps, never sharing (and so never fighting) the head's filter
        // velocity state.
        private MotorFilter _neckGesturePitchFilter;
        private MotorFilter _neckGestureYawFilter;
        private MotorFilter _neckGestureRollFilter;

        private MotorFilter _pelvisLateralFilter;
        private MotorFilter _pelvisObliquityFilter;
        private MotorFilter _pelvisYawFilter;

        private float _prevAppliedSpineSagittal;
        private float _prevAppliedSpineLateral;

        private float _opennessSpineWeight;
        private float _opennessChestWeight;
        private float _opennessUpperChestWeight;
        private float _leanSpineWeight;
        private float _leanChestWeight;
        private float _leanUpperChestWeight;

        /// <summary>Root/pelvis bone, when the bound rig has one.</summary>
        public Transform Hips { get; private set; }

        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform UpperChest { get; private set; }
        public Transform LeftShoulder { get; private set; }
        public Transform RightShoulder { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head { get; private set; }

        /// <summary>Left thigh bone, when the bound rig resolves a full leg chain.</summary>
        public Transform LeftUpperLeg { get; private set; }

        /// <summary>Left shin bone. See <see cref="LeftUpperLeg" />.</summary>
        public Transform LeftLowerLeg { get; private set; }

        /// <summary>Left foot bone. See <see cref="LeftUpperLeg" />.</summary>
        public Transform LeftFoot { get; private set; }

        /// <summary>Right thigh bone. See <see cref="LeftUpperLeg" />.</summary>
        public Transform RightUpperLeg { get; private set; }

        /// <summary>Right shin bone. See <see cref="LeftUpperLeg" />.</summary>
        public Transform RightLowerLeg { get; private set; }

        /// <summary>Right foot bone. See <see cref="LeftUpperLeg" />.</summary>
        public Transform RightFoot { get; private set; }

        /// <summary>
        ///     Whether all six leg bones AND Hips resolved — gates the leg-compensation pass
        ///     that re-pins the feet after a pelvis weight-shift write.
        /// </summary>
        public bool HasLegChain { get; private set; }

        /// <summary>
        ///     True when EITHER leg is at (or past) ~full extension this frame:
        ///     <c>Distance(foot, upperLeg) / (Distance(upperLeg, lowerLeg) +
        ///     Distance(lowerLeg, foot)) &gt; 0.99</c>. Recomputed once per <see cref="BeginFrame" />
        ///     from the animated/static base pose (before any write this frame), <c>false</c> when
        ///     <see cref="HasLegChain" /> is <c>false</c>. Deliberately a slightly TIGHTER
        ///     threshold than <see cref="TwoBoneLegSolver" />'s own internal 0.995 gate, so the
        ///     controller's leg-compensation decision (which reads this property to decide whether
        ///     to even attempt the pass, and how far to cap the pelvis offset when it does not)
        ///     flips to "unavailable" one step ahead of the solver's own bail — a straight-leg rig
        ///     never gets close enough to the solver's own gate to matter. ~6 <c>Vector3.Distance</c>
        ///     calls per frame when bound, zero allocation.
        /// </summary>
        public bool LegChainNearFullExtension { get; private set; }

        /// <summary>
        ///     Whether the leg-compensation pass should run this tick. Set by the controller each
        ///     frame from the profile's toggle; the compositor itself never reads a profile. When
        ///     <c>false</c> (or <see cref="HasLegChain" /> is <c>false</c>), <see cref="AddPelvis" />
        ///     still moves the pelvis — the caller is responsible for keeping the fed offset small
        ///     enough to avoid visible foot slide in that case (single responsibility: the
        ///     compositor applies what it is given).
        /// </summary>
        public bool LegCompensationEnabled { get; set; } = true;

        /// <summary>Spine's share of the total spine-chain swing.</summary>
        public float SpineWeight { get; private set; }

        /// <summary>Chest's share of the total spine-chain swing (0 when absent).</summary>
        public float ChestWeight { get; private set; }

        /// <summary>UpperChest's share of the total spine-chain swing (0 when absent).</summary>
        public float UpperChestWeight { get; private set; }

        /// <summary>Whether Hips resolved — required for the pelvis channel.</summary>
        public bool HasHips { get; private set; }

        public bool HasChest { get; private set; }
        public bool HasUpperChest { get; private set; }

        /// <summary>Whether both shoulder bones resolved — gates the shoulder lift/tension channel.</summary>
        public bool HasShoulders { get; private set; }

        /// <summary>Whether both Neck and Head resolved — gates the head-gesture fallback channel.</summary>
        public bool HasHeadChain { get; private set; }

        /// <summary>Whether the chain is usable this tick — same rule as v1's chain calibration: Spine resolved.</summary>
        public bool IsBound { get; private set; }

        /// <summary>
        ///     That tick's estimator bone's (<c>UpperChest ?? Chest ?? Spine</c>) local rotation,
        ///     sampled in <see cref="BeginFrame" /> immediately after the guard restore — the
        ///     animated/static base pose before any channel writes this frame. Kept current for
        ///     diagnostics; nothing in the write chain reads it today.
        /// </summary>
        public Quaternion AnimatedTorsoLocalRotation { get; private set; } = Quaternion.identity;

        /// <summary>Whether <see cref="AnimatedTorsoLocalRotation" /> holds a real sample (true whenever <see cref="IsBound" />).</summary>
        public bool HasAnimatedPoseSample { get; private set; }

        /// <summary>
        ///     Documented approximation of the spine-base→sternum lever arm, in meters (motion
        ///     meter): <c>distance(Spine, UpperChest ?? Chest ?? Spine) + 0.12f</c>,
        ///     computed once at <see cref="Bind" />/<see cref="BindManual" />. Used to convert a
        ///     small rotational delta at the chest into an approximate linear travel at the
        ///     sternum (<c>tan(degrees) * SternumLeverMeters</c>) for the debug HUD — not an
        ///     anatomically precise measurement. 0 when unbound.
        /// </summary>
        public float SternumLeverMeters { get; private set; }

        /// <summary>
        ///     The spine chain's combined sagittal swing (degrees) as of the LAST
        ///     <see cref="ApplyAccumulated" /> call — the post-motor-filter tonic+ballistic sum
        ///     actually written to the chain that tick (motion meter; measured after the velocity
        ///     cap). 0 when unbound or before the first apply.
        /// </summary>
        public float AppliedSpineSagittalDegrees => _prevAppliedSpineSagittal;

        /// <summary>Combined lateral counterpart of <see cref="AppliedSpineSagittalDegrees" />.</summary>
        public float AppliedSpineLateralDegrees => _prevAppliedSpineLateral;

        /// <summary>Resolves and calibrates the chain from a rig binding's semantic bones.</summary>
        public void Bind(IStandardRigBinding rigBinding)
        {
            Transform hips = null, spine = null, chest = null, upperChest = null;
            Transform leftShoulder = null, rightShoulder = null, neck = null, head = null;
            Transform leftUpperLeg = null, leftLowerLeg = null, leftFoot = null;
            Transform rightUpperLeg = null, rightLowerLeg = null, rightFoot = null;

            if (rigBinding != null)
            {
                rigBinding.TryGetBone(StandardBone.Hips, out hips);
                rigBinding.TryGetBone(StandardBone.Spine, out spine);
                rigBinding.TryGetBone(StandardBone.Chest, out chest);
                rigBinding.TryGetBone(StandardBone.UpperChest, out upperChest);
                rigBinding.TryGetBone(StandardBone.LeftShoulder, out leftShoulder);
                rigBinding.TryGetBone(StandardBone.RightShoulder, out rightShoulder);
                rigBinding.TryGetBone(StandardBone.Neck, out neck);
                rigBinding.TryGetBone(StandardBone.Head, out head);
                rigBinding.TryGetBone(StandardBone.LeftUpperLeg, out leftUpperLeg);
                rigBinding.TryGetBone(StandardBone.LeftLowerLeg, out leftLowerLeg);
                rigBinding.TryGetBone(StandardBone.LeftFoot, out leftFoot);
                rigBinding.TryGetBone(StandardBone.RightUpperLeg, out rightUpperLeg);
                rigBinding.TryGetBone(StandardBone.RightLowerLeg, out rightLowerLeg);
                rigBinding.TryGetBone(StandardBone.RightFoot, out rightFoot);
            }

            BindManual(
                spine, chest, upperChest, leftShoulder, rightShoulder, neck, head, hips,
                leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot);
        }

        /// <summary>Direct bind for tests and custom rigs (no rig binding required).</summary>
        public void BindManual(
            Transform spine,
            Transform chest,
            Transform upperChest,
            Transform leftShoulder,
            Transform rightShoulder,
            Transform neck = null,
            Transform head = null,
            Transform hips = null,
            Transform leftUpperLeg = null,
            Transform leftLowerLeg = null,
            Transform leftFoot = null,
            Transform rightUpperLeg = null,
            Transform rightLowerLeg = null,
            Transform rightFoot = null)
        {
            Hips = hips;
            Spine = spine;
            Chest = chest;
            UpperChest = upperChest;
            LeftShoulder = leftShoulder;
            RightShoulder = rightShoulder;
            Neck = neck;
            Head = head;
            LeftUpperLeg = leftUpperLeg;
            LeftLowerLeg = leftLowerLeg;
            LeftFoot = leftFoot;
            RightUpperLeg = rightUpperLeg;
            RightLowerLeg = rightLowerLeg;
            RightFoot = rightFoot;

            HasHips = hips != null;
            HasChest = chest != null;
            HasUpperChest = upperChest != null;
            HasShoulders = leftShoulder != null && rightShoulder != null;
            HasHeadChain = neck != null && head != null;
            HasLegChain = HasHips &&
                leftUpperLeg != null && leftLowerLeg != null && leftFoot != null &&
                rightUpperLeg != null && rightLowerLeg != null && rightFoot != null;

            ComputeSpineChainWeights(spine != null, chest != null, upperChest != null);
            ComputePostureDistributionWeights(spine != null, chest != null, upperChest != null);

            IsBound = spine != null;

            _estimatorBone = upperChest != null ? upperChest : (chest != null ? chest : spine);

            // Motion-meter lever arm: distance from Spine to the same
            // upperChest??chest??spine bone the estimator uses, plus a fixed sternum-offset
            // approximation. 0 when unbound (spine null).
            SternumLeverMeters = spine != null
                ? Vector3.Distance(spine.position, _estimatorBone.position) + 0.12f
                : 0f;

            ResetFrameProtocolState();
        }

        /// <summary>Resets to the unbound state (next tick applies no writes).</summary>
        public void Clear()
        {
            Hips = null;
            Spine = null;
            Chest = null;
            UpperChest = null;
            LeftShoulder = null;
            RightShoulder = null;
            Neck = null;
            Head = null;
            LeftUpperLeg = null;
            LeftLowerLeg = null;
            LeftFoot = null;
            RightUpperLeg = null;
            RightLowerLeg = null;
            RightFoot = null;

            HasHips = false;
            HasChest = false;
            HasUpperChest = false;
            HasShoulders = false;
            HasHeadChain = false;
            HasLegChain = false;
            LegChainNearFullExtension = false;
            SpineWeight = 0f;
            ChestWeight = 0f;
            UpperChestWeight = 0f;
            IsBound = false;

            _estimatorBone = null;
            AnimatedTorsoLocalRotation = Quaternion.identity;
            HasAnimatedPoseSample = false;
            SternumLeverMeters = 0f;

            ResetFrameProtocolState();
        }

        /// <summary>
        ///     Authored full-chain weights (Spine lightest, UpperChest heaviest — the swing
        ///     reads most naturally when the bone nearest the chest carries the most motion).
        ///     When a bone is missing its authored share redistributes proportionally across
        ///     the bones that remain, so the total swing amplitude stays constant.
        /// </summary>
        private void ComputeSpineChainWeights(bool hasSpine, bool hasChest, bool hasUpperChest)
        {
            const float authoredSpine = 0.25f;
            const float authoredChest = 0.35f;
            const float authoredUpperChest = 0.4f;

            float availableTotal =
                (hasSpine ? authoredSpine : 0f) +
                (hasChest ? authoredChest : 0f) +
                (hasUpperChest ? authoredUpperChest : 0f);

            if (availableTotal <= 0f)
            {
                SpineWeight = 0f;
                ChestWeight = 0f;
                UpperChestWeight = 0f;
                return;
            }

            float scale = 1f / availableTotal;
            SpineWeight = hasSpine ? authoredSpine * scale : 0f;
            ChestWeight = hasChest ? authoredChest * scale : 0f;
            UpperChestWeight = hasUpperChest ? authoredUpperChest * scale : 0f;
        }

        private void ComputePostureDistributionWeights(bool hasSpine, bool hasChest, bool hasUpperChest)
        {
            ComputeDistribution(hasSpine, hasChest, hasUpperChest, 0.10f, 0.35f, 0.55f,
                out _opennessSpineWeight, out _opennessChestWeight, out _opennessUpperChestWeight);
            ComputeDistribution(hasSpine, hasChest, hasUpperChest, 0.55f, 0.30f, 0.15f,
                out _leanSpineWeight, out _leanChestWeight, out _leanUpperChestWeight);
        }

        private static void ComputeDistribution(
            bool hasSpine, bool hasChest, bool hasUpperChest,
            float spineAuthored, float chestAuthored, float upperChestAuthored,
            out float spineWeight, out float chestWeight, out float upperChestWeight)
        {
            float total = (hasSpine ? spineAuthored : 0f) + (hasChest ? chestAuthored : 0f) +
                          (hasUpperChest ? upperChestAuthored : 0f);
            float scale = total > 0f ? 1f / total : 0f;
            spineWeight = hasSpine ? spineAuthored * scale : 0f;
            chestWeight = hasChest ? chestAuthored * scale : 0f;
            upperChestWeight = hasUpperChest ? upperChestAuthored * scale : 0f;
        }

        /// <summary>
        ///     Starts a new frame: unwinds last frame's stale writes, zeroes every accumulator,
        ///     and refreshes the animated-pose estimator sample. Always runs — idempotence
        ///     across repeated same-frame calls is <see cref="EnsureFrameStarted" />'s job, not
        ///     this method's, so tests may call it freely.
        /// </summary>
        public void BeginFrame()
        {
            _frameStamp = Time.frameCount;
            _guard.RestoreStaleWrites();

            _spineSagittalAccum = 0f;
            _spineLateralAccum = 0f;
            _postureOpennessAccum = 0f;
            _postureLeanAccum = 0f;
            _spineSagittalBallisticAccum = 0f;
            _spineLateralBallisticAccum = 0f;
            _shoulderLiftAccum = 0f;
            _shoulderLiftBallisticAccum = 0f;
            _shoulderTensionAccum = 0f;
            _headPitchAccum = 0f;
            _headYawAccum = 0f;
            _headRollAccum = 0f;
            _neckPitchAccum = 0f;
            _neckYawAccum = 0f;
            _neckRollAccum = 0f;
            _neckGestureExplicit = false;
            _breathSagittalForStabilization = 0f;
            _breathStabilization01 = 0f;
            _pelvisLateralAccum = 0f;
            _pelvisObliquityAccum = 0f;
            _pelvisYawAccum = 0f;

            if (IsBound && _estimatorBone != null)
            {
                AnimatedTorsoLocalRotation = _estimatorBone.localRotation;
                HasAnimatedPoseSample = true;
            }
            else
            {
                AnimatedTorsoLocalRotation = Quaternion.identity;
                HasAnimatedPoseSample = false;
            }

            // Recomputed once per frame, off this frame's just-restored
            // animated/static base pose — before any channel write below could move a leg bone
            // out of its true resting extension.
            LegChainNearFullExtension = HasLegChain &&
                (IsNearFullExtension(LeftUpperLeg, LeftLowerLeg, LeftFoot) ||
                 IsNearFullExtension(RightUpperLeg, RightLowerLeg, RightFoot));
        }

        /// <summary>See <see cref="LegChainNearFullExtension" />. Assumes all three transforms are non-null (only called when <see cref="HasLegChain" />).</summary>
        private static bool IsNearFullExtension(Transform upperLeg, Transform lowerLeg, Transform foot)
        {
            float totalLength = Vector3.Distance(upperLeg.position, lowerLeg.position) +
                Vector3.Distance(lowerLeg.position, foot.position);
            if (totalLength < 1e-4f) return false;

            float extension = Vector3.Distance(foot.position, upperLeg.position) / totalLength;
            return extension > 0.99f;
        }

        /// <summary>Starts a new frame only if one has not already started this Unity frame.</summary>
        private void EnsureFrameStarted()
        {
            if (_frameStamp != Time.frameCount) BeginFrame();
        }

        /// <summary>
        ///     Adds this tick's TONIC spine-chain sagittal/lateral swing contribution (degrees,
        ///     absolute for the frame): slow, postural motion — posture, breath, sway, stance
        ///     counter-curve — filtered under <see cref="MotorLimits.SpineTonicMaxSpeed" />.
        /// </summary>
        public void AddSpineChainSwing(float sagittalDegrees, float lateralDegrees)
        {
            _spineSagittalAccum += sagittalDegrees;
            _spineLateralAccum += lateralDegrees;
        }

        /// <summary>
        ///     Adds sustained posture as two anatomical intents instead of collapsing both into
        ///     one sagittal number. Openness is distributed toward UpperChest; conversational
        ///     lean is distributed toward Spine, preserving both cues when their signs oppose.
        /// </summary>
        public void AddPostureSilhouette(float opennessDegrees, float leanDegrees)
        {
            _postureOpennessAccum += opennessDegrees;
            _postureLeanAccum += leanDegrees;
        }

        /// <summary>
        ///     Adds this tick's BALLISTIC spine-chain swing contribution (degrees, absolute for
        ///     the frame): fast gestural transients — reaction flinch/bounce — filtered under
        ///     <see cref="MotorLimits.SpineBallisticMaxSpeed" /> so the tonic lane's postural
        ///     caps never blunt their attack. Summed with the filtered tonic lane per output.
        /// </summary>
        public void AddSpineChainSwingBallistic(float sagittalDegrees, float lateralDegrees)
        {
            _spineSagittalBallisticAccum += sagittalDegrees;
            _spineLateralBallisticAccum += lateralDegrees;
        }

        /// <summary>Adds this tick's TONIC shoulder lift contribution (degrees) — breath lift; same sign on both shoulders.</summary>
        public void AddShoulderLift(float sagittalDegrees) => _shoulderLiftAccum += sagittalDegrees;

        /// <summary>
        ///     Adds this tick's BALLISTIC shoulder lift contribution (degrees) — shrugs and the
        ///     startle shoulder jump; same sign on both shoulders. See
        ///     <see cref="AddSpineChainSwingBallistic" /> for the tonic/ballistic split.
        /// </summary>
        public void AddShoulderLiftBallistic(float sagittalDegrees) => _shoulderLiftBallisticAccum += sagittalDegrees;

        /// <summary>Adds this tick's shoulder tension contribution (degrees) — LEFT gets +value, RIGHT gets -value.</summary>
        public void AddShoulderTension(float lateralDegrees) => _shoulderTensionAccum += lateralDegrees;

        /// <summary>
        ///     Adds this tick's pelvis weight-shift contribution: a lateral
        ///     translation (meters, +right) plus an obliquity (hip-hike, degrees) and yaw
        ///     (degrees) rotation. Absolute for the frame, reset each <see cref="BeginFrame" />.
        ///     Applied FIRST in <see cref="ApplyAccumulated" /> — the pelvis moves everything
        ///     above (spine) and below (legs, when leg compensation is active) it.
        /// </summary>
        public void AddPelvis(float lateralOffsetMeters, float obliquityDegrees, float yawDegrees)
        {
            _pelvisLateralAccum += lateralOffsetMeters;
            _pelvisObliquityAccum += obliquityDegrees;
            _pelvisYawAccum += yawDegrees;
        }

        /// <summary>Adds this tick's head-gesture no-consumer fallback contribution (degrees).</summary>
        public void AddHeadGesture(float pitchDegrees, float yawDegrees, float rollDegrees)
        {
            _headPitchAccum += pitchDegrees;
            _headYawAccum += yawDegrees;
            _headRollAccum += rollDegrees;
        }

        /// <summary>
        ///     Adds this tick's EXPLICIT neck-lead contribution (degrees): the
        ///     caller (the head-gesture no-consumer fallback, driven by
        ///     <c>HeadGestureDirector.CurrentNeckLead</c>) has already applied whatever share of
        ///     the full gesture it wants the neck to carry — this accumulator is composed and
        ///     filtered independently of <see cref="AddHeadGesture" />'s own head accumulators,
        ///     then written to the Neck bone AS-IS (no further share split) in
        ///     <see cref="ApplyAccumulated" />, as long as this method was called at least once
        ///     this frame. Calling this at all (even with all-zero values) marks the frame as
        ///     using the explicit neck path rather than the head-share-split fallback.
        /// </summary>
        public void AddNeckGesture(float pitchDegrees, float yawDegrees, float rollDegrees)
        {
            _neckPitchAccum += pitchDegrees;
            _neckYawAccum += yawDegrees;
            _neckRollAccum += rollDegrees;
            _neckGestureExplicit = true;
        }

        /// <summary>
        ///     Sets this tick's breath head-stabilization inputs (fixes B5): real
        ///     heads stay level while the ribcage moves (the vestibulo-collic reflex), so
        ///     <see cref="ApplyAccumulated" /> counter-pitches the head/neck by a fraction of the
        ///     breath's sagittal chest swing. Gaze, when active at its own later execution order,
        ///     measures the remaining residual as animated motion and compensates it — the two
        ///     never fight. Stores (not accumulates) both values — there is exactly one breath
        ///     source per character — and both reset to 0 every <see cref="BeginFrame" />.
        /// </summary>
        public void AddBreathHeadStabilization(float breathSagittalDegrees, float stabilization01)
        {
            _breathSagittalForStabilization = breathSagittalDegrees;
            _breathStabilization01 = stabilization01;
        }

        /// <summary>
        ///     Composes and writes every accumulated channel for this frame — the pelvis, spine
        ///     chain, shoulders, and (when bound and non-trivial) the head-gesture fallback
        ///     merged with the breath head-stabilization counter-pitch — as one guarded write per
        ///     bone. Every accumulated channel first passes through its per-channel
        ///     <see cref="MotorFilter" /> under <see cref="MotorLimits" />
        ///     caps, tonic and ballistic lanes filtered separately then summed, so no producer
        ///     roughness can ever reach a bone while in-budget signals pass through numerically
        ///     unchanged. Call once per frame, after all <c>Add*</c> calls.
        /// </summary>
        public void ApplyAccumulated(float deltaTime)
        {
            if (!IsBound) return;

            // The 1/240 s floor keeps a hitch frame from granting a super-sized change budget.
            float dt = Mathf.Max(deltaTime, 1f / 240f);

            // (1) Pelvis first — it moves everything above (spine) and below (legs) it.
            // Tonic-only channels; ApplyPelvis' trivial-write gate tests these FILTERED values,
            // because a filter can still be releasing after its input drops to zero.
            float pelvisLateral = _pelvisLateralFilter.Step(
                _pelvisLateralAccum, MotorLimits.PelvisLateralMaxSpeed, MotorLimits.PelvisLateralMaxAccel, dt);
            float pelvisObliquity = _pelvisObliquityFilter.Step(
                _pelvisObliquityAccum, MotorLimits.PelvisAngularMaxSpeed, MotorLimits.PelvisAngularMaxAccel, dt);
            float pelvisYaw = _pelvisYawFilter.Step(
                _pelvisYawAccum, MotorLimits.PelvisAngularMaxSpeed, MotorLimits.PelvisAngularMaxAccel, dt);
            ApplyPelvis(pelvisLateral, pelvisObliquity, pelvisYaw);

            // (2) Motor filtering: tonic and ballistic lanes each filtered under their own caps,
            // then summed per output — the sum is what the distribution weights below apply.
            float sagittal =
                _spineSagittalTonicFilter.Step(
                    _spineSagittalAccum, MotorLimits.SpineTonicMaxSpeed, MotorLimits.SpineTonicMaxAccel, dt) +
                _spineSagittalBallisticFilter.Step(
                    _spineSagittalBallisticAccum, MotorLimits.SpineBallisticMaxSpeed, MotorLimits.SpineBallisticMaxAccel, dt);
            float lateral =
                _spineLateralTonicFilter.Step(
                    _spineLateralAccum, MotorLimits.SpineTonicMaxSpeed, MotorLimits.SpineTonicMaxAccel, dt) +
                _spineLateralBallisticFilter.Step(
                    _spineLateralBallisticAccum, MotorLimits.SpineBallisticMaxSpeed, MotorLimits.SpineBallisticMaxAccel, dt);
            float postureOpenness = _postureOpennessFilter.Step(
                _postureOpennessAccum, MotorLimits.SpineTonicMaxSpeed, MotorLimits.SpineTonicMaxAccel, dt);
            float postureLean = _postureLeanFilter.Step(
                _postureLeanAccum, MotorLimits.SpineTonicMaxSpeed, MotorLimits.SpineTonicMaxAccel, dt);
            float shoulderLift =
                _shoulderLiftTonicFilter.Step(
                    _shoulderLiftAccum, MotorLimits.ShoulderTonicMaxSpeed, MotorLimits.ShoulderTonicMaxAccel, dt) +
                _shoulderLiftBallisticFilter.Step(
                    _shoulderLiftBallisticAccum, MotorLimits.ShoulderBallisticMaxSpeed, MotorLimits.ShoulderBallisticMaxAccel, dt);
            float shoulderTension = _shoulderTensionFilter.Step(
                _shoulderTensionAccum, MotorLimits.ShoulderTonicMaxSpeed, MotorLimits.ShoulderTonicMaxAccel, dt);

            // Motion meter: the post-filter applied sums.
            _prevAppliedSpineSagittal = sagittal - postureOpenness + postureLean;
            _prevAppliedSpineLateral = lateral;

            // (3) Spine chain.
            ApplySpineBone(Spine, 1f,
                sagittal * SpineWeight - postureOpenness * _opennessSpineWeight + postureLean * _leanSpineWeight,
                lateral * SpineWeight);
            ApplySpineBone(Chest, 1f,
                sagittal * ChestWeight - postureOpenness * _opennessChestWeight + postureLean * _leanChestWeight,
                lateral * ChestWeight);
            ApplySpineBone(UpperChest, 1f,
                sagittal * UpperChestWeight - postureOpenness * _opennessUpperChestWeight + postureLean * _leanUpperChestWeight,
                lateral * UpperChestWeight);

            // (4) Shoulders.
            if (HasShoulders)
            {
                ApplyShoulder(LeftShoulder, shoulderLift, shoulderTension);
                ApplyShoulder(RightShoulder, shoulderLift, -shoulderTension);
            }

            // (5) Neck/head — head-gesture channel (gated on HasHeadChain, v1 parity) composed with the breath
            // head-stabilization counter-pitch — summed BEFORE building each
            // bone's delta so Head/Neck each still receive exactly one guarded write. The
            // HasHeadChain gate applies to the GESTURE channel only: stabilization may run with
            // just a Head (or just a Neck) bone, taking the full counter in that case.
            //
            // The gesture accumulators filter per axis (ballistic caps — head gestures are
            // ballistic by nature) BEFORE the Head/Neck share split, so both bones see the same
            // limited signal. The stabilization value is deliberately NOT filtered: it tracks
            // the breath's chest output, which already passed the tonic spine filter above —
            // filtering it again would desynchronize the counter-pitch from the very motion it
            // cancels.
            float stabilizationCounterPitch = -_breathSagittalForStabilization * Mathf.Clamp01(_breathStabilization01);

            float filteredHeadPitch = _headGesturePitchFilter.Step(
                _headPitchAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);
            float filteredHeadYaw = _headGestureYawFilter.Step(
                _headYawAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);
            float filteredHeadRoll = _headGestureRollFilter.Step(
                _headRollAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);

            // The neck's OWN filters — always stepped (fed 0 when no
            // AddNeckGesture call happened this frame) so their velocity state stays continuous
            // across explicit/non-explicit frames, exactly like every other channel here.
            float filteredNeckPitch = _neckGesturePitchFilter.Step(
                _neckPitchAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);
            float filteredNeckYaw = _neckGestureYawFilter.Step(
                _neckYawAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);
            float filteredNeckRoll = _neckGestureRollFilter.Step(
                _neckRollAccum, MotorLimits.HeadGestureMaxSpeed, MotorLimits.HeadGestureMaxAccel, dt);

            // Write-gate on the FILTERED values: a filter's release tail can outlive its input,
            // and those tail frames must still be written. Extended to also
            // consider the neck's own filtered values — otherwise an explicit neck-only signal
            // (head accumulators trivial, neck accumulators not) would have its neckPitch/Yaw/Roll
            // below force-zeroed by a gate that only ever looked at the head's own accumulators.
            bool hasGestureAccum = HasHeadChain &&
                (Mathf.Abs(filteredHeadPitch) >= 1e-5f ||
                 Mathf.Abs(filteredHeadYaw) >= 1e-5f ||
                 Mathf.Abs(filteredHeadRoll) >= 1e-5f ||
                 Mathf.Abs(filteredNeckPitch) >= 1e-5f ||
                 Mathf.Abs(filteredNeckYaw) >= 1e-5f ||
                 Mathf.Abs(filteredNeckRoll) >= 1e-5f);

            if (HasHeadChain)
            {
                float gesturePitch = hasGestureAccum ? filteredHeadPitch : 0f;
                float gestureYaw = hasGestureAccum ? filteredHeadYaw : 0f;
                float gestureRoll = hasGestureAccum ? filteredHeadRoll : 0f;

                ApplyPitchYawRoll(Head, gesturePitch + stabilizationCounterPitch * 0.6f, gestureYaw, gestureRoll);

                if (_neckGestureExplicit)
                {
                    // Explicit path: the caller already applied its own share
                    // to these values — write them AS-IS, not split again from the head's.
                    float neckPitch = hasGestureAccum ? filteredNeckPitch : 0f;
                    float neckYaw = hasGestureAccum ? filteredNeckYaw : 0f;
                    float neckRoll = hasGestureAccum ? filteredNeckRoll : 0f;

                    ApplyPitchYawRoll(Neck,
                        neckPitch + stabilizationCounterPitch * 0.4f,
                        neckYaw,
                        neckRoll);
                }
                else
                {
                    // Share-split fallback, for callers that never call AddNeckGesture: the Neck
                    // takes its fixed share of the filtered HEAD signal.
                    ApplyPitchYawRoll(Neck,
                        gesturePitch * NeckGestureShare + stabilizationCounterPitch * 0.4f,
                        gestureYaw * NeckGestureShare,
                        gestureRoll * NeckGestureShare);
                }
            }
            else if (Mathf.Abs(stabilizationCounterPitch) >= 1e-5f)
            {
                Transform stabilizationBone = Head != null ? Head : Neck;
                if (stabilizationBone != null)
                    ApplyPitchYawRoll(stabilizationBone, stabilizationCounterPitch, 0f, 0f);
            }
        }

        /// <summary>
        ///     Applies this frame's filtered pelvis rotation+translation to Hips (values already
        ///     motor-filtered by <see cref="ApplyAccumulated" />), and, when leg
        ///     compensation is active, re-pins both feet afterward via
        ///     <see cref="TwoBoneLegSolver" /> so a weight shift reads as a real stance change
        ///     instead of foot slide. Skipped entirely when Hips is unbound or the filtered
        ///     offset/angles are below the write-epsilon.
        /// </summary>
        private void ApplyPelvis(float lateralOffsetMeters, float obliquityDegrees, float yawDegrees)
        {
            if (Hips == null) return;

            bool trivial = Mathf.Abs(lateralOffsetMeters) < 1e-5f &&
                Mathf.Abs(obliquityDegrees) < 1e-4f && Mathf.Abs(yawDegrees) < 1e-4f;
            if (trivial) return;

            // The near-full-extension gate is enforced HERE too, not just
            // by the controller's own read of LegChainNearFullExtension when it sets
            // LegCompensationEnabled — a caller that flips the flag directly (tests, a future
            // consumer) must never be able to run the leg solver on a straight-leg chain either.
            bool legCompensationActive = LegCompensationEnabled && HasLegChain && !LegChainNearFullExtension;

            // Capture pre-pelvis-move foot poses BEFORE the pelvis write, so the leg solve below
            // can re-pin each foot back to exactly where it stood.
            Vector3 leftFootWorldPos = default, rightFootWorldPos = default;
            Quaternion leftFootWorldRot = default, rightFootWorldRot = default;
            if (legCompensationActive)
            {
                leftFootWorldPos = LeftFoot.position;
                leftFootWorldRot = LeftFoot.rotation;
                rightFootWorldPos = RightFoot.position;
                rightFootWorldRot = RightFoot.rotation;
            }

            Transform reference = Hips.parent != null ? Hips.parent : Hips;

            // Rotation: obliquity (hip-hike, sagittal-axis roll analog around the reference's
            // forward) + yaw, pre-multiplied via the same world-swing composition every other
            // channel uses.
            Quaternion preWriteRotation = Hips.localRotation;
            Quaternion rotationDelta = ProceduralPoseMath.PitchYawRollDelta(reference, 0f, yawDegrees, obliquityDegrees);
            if (rotationDelta != Quaternion.identity)
            {
                ProceduralPoseMath.ApplyWorldSwing(Hips, rotationDelta);
                _guard.Record(Hips, preWriteRotation);
            }

            // Translation: lateral offset along the reference's right axis, projected onto the
            // horizontal plane so a tilted reference never lifts/sinks the pelvis.
            if (Mathf.Abs(lateralOffsetMeters) >= 1e-5f)
            {
                Vector3 lateral = reference.right * lateralOffsetMeters;
                lateral -= Vector3.up * Vector3.Dot(lateral, Vector3.up);

                Vector3 preLocalPos = Hips.localPosition;
                Hips.position += lateral;
                _guard.RecordPosition(Hips, preLocalPos);
            }

            if (!legCompensationActive) return;

            TwoBoneLegSolver.Solve(LeftUpperLeg, LeftLowerLeg, LeftFoot, leftFootWorldPos, leftFootWorldRot, _guard);
            TwoBoneLegSolver.Solve(RightUpperLeg, RightLowerLeg, RightFoot, rightFootWorldPos, rightFootWorldRot, _guard);
        }

        /// <summary>Passthrough to the shared guard — used by owners' OnDisable/rebind paths.</summary>
        public void RestoreStaleWrites() => _guard.RestoreStaleWrites();

        /// <summary>
        ///     Gaze's late torso-aim entry: composes a yaw/pitch aim delta onto Chest/UpperChest
        ///     through this same shared guard, so a Body Language-disabled (or absent) character
        ///     with Gaze still alive gets exactly one restore-once-per-frame protocol, not two
        ///     competing guards. Bit-exact port of the v1 gaze torso branch
        ///     (<c>HeadTorsoSolver.Apply</c>, torso section), the only change being which guard
        ///     records the write.
        /// </summary>
        public void ComposeTorsoAim(Transform reference, float yawDegrees, float pitchDegrees)
        {
            if (reference == null) return;

            Quaternion swing = ProceduralPoseMath.TorsoAimDelta(reference, yawDegrees, pitchDegrees);
            if (swing == Quaternion.identity) return;

            if (Chest != null && UpperChest != null)
            {
                // Split along the aim's own axis rather than scaling the yaw/pitch pair twice:
                // UpperChest is a descendant of Chest, so it inherits Chest's rotation, and two
                // independently-built scaled deltas would leave a parasitic roll on it (see
                // ProceduralPoseMath.SplitAimSwing).
                ProceduralPoseMath.SplitAimSwing(swing, ChestAimShare,
                    out Quaternion chestDelta, out Quaternion upperChestDelta);
                ComposeGazeAim(chestDelta, upperChestDelta, Quaternion.identity, Quaternion.identity);
                return;
            }

            if (UpperChest != null) ComposeGazeAim(Quaternion.identity, swing, Quaternion.identity, Quaternion.identity);
            else if (Chest != null) ComposeGazeAim(swing, Quaternion.identity, Quaternion.identity, Quaternion.identity);
        }

        /// <summary>
        ///     Gaze's late aim entry for the whole chain it drives: chest, upper chest, neck and
        ///     head, as already-composed world-space deltas.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Takes deltas rather than angles because the frame they are expressed in is the
        ///         gaze module's business, not this type's: a custom rig can calibrate a gaze
        ///         frame whose forward and up are not the root's, and the previous angle-based
        ///         entry could only accept a <see cref="Transform" /> — so calibrated rigs fell
        ///         back to a second, gaze-private write guard on the same bones this one already
        ///         owns. Two restore-once-per-frame protocols over an overlapping bone set is a
        ///         latent double-unwind; this signature removes the reason for the second one.
        ///     </para>
        ///     <para>
        ///         Pass <see cref="Quaternion.identity" /> for a bone this call has nothing for.
        ///         Safe to call after <see cref="ApplyAccumulated" /> has already written the
        ///         same bones: the shared guard keeps the FIRST writer's pre-write value, so a
        ///         restore unwinds to the animated pose rather than to an intermediate composite
        ///         (see <see cref="AnimatedAdditivePoseGuard.Record" />).
        ///     </para>
        /// </remarks>
        public void ComposeGazeAim(
            Quaternion chestDelta,
            Quaternion upperChestDelta,
            Quaternion neckDelta,
            Quaternion headDelta)
        {
            EnsureFrameStarted();
            if (!IsBound) return;

            ApplyTorsoBone(Chest, chestDelta);
            ApplyTorsoBone(UpperChest, upperChestDelta);
            ApplyTorsoBone(Neck, neckDelta);
            ApplyTorsoBone(Head, headDelta);
        }

        private void ApplySpineBone(Transform bone, float weight, float sagittalDegrees, float lateralDegrees)
        {
            if (bone == null || weight <= 0f) return;

            float weightedSagittal = sagittalDegrees * weight;
            float weightedLateral = lateralDegrees * weight;
            if (Mathf.Abs(weightedSagittal) < 1e-5f && Mathf.Abs(weightedLateral) < 1e-5f) return;

            Transform reference = bone.parent != null ? bone.parent : bone;
            Quaternion preWrite = bone.localRotation;
            Quaternion delta = ProceduralPoseMath.SwingDelta(reference, weightedSagittal, weightedLateral);
            if (delta == Quaternion.identity) return;

            ProceduralPoseMath.ApplyWorldSwing(bone, delta);
            _guard.Record(bone, preWrite);
        }

        private void ApplyShoulder(Transform bone, float liftDegrees, float tensionDegrees)
        {
            if (bone == null) return;
            if (Mathf.Abs(liftDegrees) < 1e-5f && Mathf.Abs(tensionDegrees) < 1e-5f) return;

            Transform reference = bone.parent != null ? bone.parent : bone;
            Quaternion preWrite = bone.localRotation;
            Quaternion delta = ProceduralPoseMath.SwingDelta(reference, liftDegrees, tensionDegrees);
            if (delta == Quaternion.identity) return;

            ProceduralPoseMath.ApplyWorldSwing(bone, delta);
            _guard.Record(bone, preWrite);
        }

        private void ApplyPitchYawRoll(Transform bone, float pitchDegrees, float yawDegrees, float rollDegrees)
        {
            if (bone == null) return;

            Transform reference = bone.parent != null ? bone.parent : bone;
            Quaternion preWrite = bone.localRotation;
            Quaternion delta = ProceduralPoseMath.PitchYawRollDelta(reference, pitchDegrees, yawDegrees, rollDegrees);
            if (delta == Quaternion.identity) return;

            ProceduralPoseMath.ApplyWorldSwing(bone, delta);
            _guard.Record(bone, preWrite);
        }

        private void ApplyTorsoBone(Transform bone, Quaternion worldDelta)
        {
            if (bone == null || worldDelta == Quaternion.identity) return;

            Quaternion preWrite = bone.localRotation;
            ProceduralPoseMath.ApplyWorldSwing(bone, worldDelta);
            _guard.Record(bone, preWrite);
        }

        private void ResetFrameProtocolState()
        {
            // -1, not 0: Time.frameCount CAN be 0 on the very first engine frame, and a stamp
            // that accidentally matches it would make EnsureFrameStarted skip that frame's
            // guard restore.
            _frameStamp = -1;
            _spineSagittalAccum = 0f;
            _spineLateralAccum = 0f;
            _postureOpennessAccum = 0f;
            _postureLeanAccum = 0f;
            _spineSagittalBallisticAccum = 0f;
            _spineLateralBallisticAccum = 0f;
            _shoulderLiftAccum = 0f;
            _shoulderLiftBallisticAccum = 0f;
            _shoulderTensionAccum = 0f;
            _headPitchAccum = 0f;
            _headYawAccum = 0f;
            _headRollAccum = 0f;
            _neckPitchAccum = 0f;
            _neckYawAccum = 0f;
            _neckRollAccum = 0f;
            _neckGestureExplicit = false;
            _breathSagittalForStabilization = 0f;
            _breathStabilization01 = 0f;
            _pelvisLateralAccum = 0f;
            _pelvisObliquityAccum = 0f;
            _pelvisYawAccum = 0f;
            _prevAppliedSpineSagittal = 0f;
            _prevAppliedSpineLateral = 0f;

            // Motor filters reset ONLY here — Bind/Clear paths, never per
            // frame — so their velocity state survives across the frame protocol and each one's
            // next Step after a rebind snaps cleanly to its first target.
            _spineSagittalTonicFilter.Reset();
            _spineLateralTonicFilter.Reset();
            _postureOpennessFilter.Reset();
            _postureLeanFilter.Reset();
            _spineSagittalBallisticFilter.Reset();
            _spineLateralBallisticFilter.Reset();
            _shoulderLiftTonicFilter.Reset();
            _shoulderLiftBallisticFilter.Reset();
            _shoulderTensionFilter.Reset();
            _headGesturePitchFilter.Reset();
            _headGestureYawFilter.Reset();
            _headGestureRollFilter.Reset();
            _neckGesturePitchFilter.Reset();
            _neckGestureYawFilter.Reset();
            _neckGestureRollFilter.Reset();
            _pelvisLateralFilter.Reset();
            _pelvisObliquityFilter.Reset();
            _pelvisYawFilter.Reset();
        }
    }
}
