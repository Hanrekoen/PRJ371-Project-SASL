using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Animation;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Solvers
{
    /// <summary>
    ///     Owns every eye-related blendshape submission to the facial compositor's Eyes
    ///     layer: blink weights, eyelid pitch-follow, and — when the character has no eye
    ///     bones — the EyeLook* shapes that stand in for eye rotation. Target keys are
    ///     resolved once per bind; per-frame work is weight refresh only.
    /// </summary>
    internal sealed class EyeBlendshapeWriter
    {
        private const float SubmitEpsilon = 0.0001f;

        // Aperture delta (≤ 0.5 either way) → up to ~75% shape weight at the range extremes.
        private const float SquintGain = 150f;
        private const float WideGain = 150f;

        private readonly List<BlendshapeTargetKey> _blinkTargets = new(4);
        private readonly List<BlendshapeTargetKey> _lookUpLeft = new(2);
        private readonly List<BlendshapeTargetKey> _lookDownLeft = new(2);
        private readonly List<BlendshapeTargetKey> _lookInLeft = new(2);
        private readonly List<BlendshapeTargetKey> _lookOutLeft = new(2);
        private readonly List<BlendshapeTargetKey> _lookUpRight = new(2);
        private readonly List<BlendshapeTargetKey> _lookDownRight = new(2);
        private readonly List<BlendshapeTargetKey> _lookInRight = new(2);
        private readonly List<BlendshapeTargetKey> _lookOutRight = new(2);
        private readonly List<BlendshapeTargetKey> _squintTargets = new(2);
        private readonly List<BlendshapeTargetKey> _wideTargets = new(2);

        private readonly List<BlendshapeTargetKey> _frameTargets = new(24);
        private readonly List<float> _frameWeights = new(24);

        private float _smoothedLidDown;
        private float _smoothedLidUp;

        /// <summary>Whether EyeLook* shapes were resolved (blendshape eye backend possible).</summary>
        public bool HasLookShapes { get; private set; }


        public void Bind(IStandardRigBinding rigBinding)
        {
            Clear();
            if (rigBinding == null) return;

            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeBlinkLeft, _blinkTargets);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeBlinkRight, _blinkTargets);

            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookUpLeft, _lookUpLeft);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookDownLeft, _lookDownLeft);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookInLeft, _lookInLeft);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookOutLeft, _lookOutLeft);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookUpRight, _lookUpRight);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookDownRight, _lookDownRight);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookInRight, _lookInRight);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeLookOutRight, _lookOutRight);

            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeSquintLeft, _squintTargets);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeSquintRight, _squintTargets);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeWideLeft, _wideTargets);
            BlendshapeTargetCollector.CollectAllMatches(rigBinding, StandardBlendshape.EyeWideRight, _wideTargets);

            // A blendshape backend is only viable when each eye can move in both horizontal
            // directions. Accepting a single directional shape creates a one-sided gaze that
            // looks broken as soon as the target crosses the character's centerline.
            HasLookShapes =
                _lookInLeft.Count > 0 && _lookOutLeft.Count > 0 &&
                _lookInRight.Count > 0 && _lookOutRight.Count > 0;
        }

        public void Clear()
        {
            _blinkTargets.Clear();
            _lookUpLeft.Clear();
            _lookDownLeft.Clear();
            _lookInLeft.Clear();
            _lookOutLeft.Clear();
            _lookUpRight.Clear();
            _lookDownRight.Clear();
            _lookInRight.Clear();
            _lookOutRight.Clear();
            _squintTargets.Clear();
            _wideTargets.Clear();
            _frameTargets.Clear();
            _frameWeights.Clear();
            _smoothedLidDown = 0f;
            _smoothedLidUp = 0f;
            HasLookShapes = false;
        }

        /// <summary>
        ///     Submits this frame's eye blendshape weights: blink, eyelid pitch-follow, the
        ///     emotion-driven lid aperture (squint/wide), and (when
        ///     <paramref name="driveLookShapes" /> is set) the EyeLook* rotation stand-ins
        ///     derived from the solved per-eye angles.
        /// </summary>
        public void Submit(
            FacialBlendshapeCompositorHost compositor,
            IFacialBlendshapeSource owner,
            ConvaiGazeProfile profile,
            float blinkWeight01,
            float lidApertureScale,
            Vector2 leftEyeAngles,
            Vector2 rightEyeAngles,
            bool driveLookShapes,
            float deltaTime)
        {
            if (compositor == null || owner == null || profile == null) return;

            _frameTargets.Clear();
            _frameWeights.Clear();

            if (profile.EnableBlink && blinkWeight01 > SubmitEpsilon)
                Append(_blinkTargets, blinkWeight01 * 100f);

            if (profile.EnableEyelidFollow)
                AppendEyelidFollow(profile, (leftEyeAngles.y + rightEyeAngles.y) * 0.5f, deltaTime);

            AppendAperture(lidApertureScale, blinkWeight01);

            if (driveLookShapes && HasLookShapes)
            {
                AppendLookShapes(profile, leftEyeAngles, isLeftEye: true);
                AppendLookShapes(profile, rightEyeAngles, isLeftEye: false);
            }

            if (_frameTargets.Count == 0) return;

            compositor.SubmitLayer(
                owner, FacialBlendshapeLayers.Eyes, _frameTargets, _frameWeights, _frameTargets.Count);
        }

        private void AppendEyelidFollow(ConvaiGazeProfile profile, float pitch, float deltaTime)
        {
            // Look down → upper lids lower (via LookDown + Blink fallback); look up → lids widen.
            float downTarget = Mathf.InverseLerp(4f, 22f, -pitch) * profile.EyelidFollowStrength;
            float upTarget = Mathf.InverseLerp(4f, 16f, pitch) * profile.EyelidFollowStrength;

            float alpha = 1f - Mathf.Exp(-14f * deltaTime);
            _smoothedLidDown = Mathf.Lerp(_smoothedLidDown, downTarget, alpha);
            _smoothedLidUp = Mathf.Lerp(_smoothedLidUp, upTarget, alpha);

            if (_smoothedLidDown > SubmitEpsilon)
            {
                Append(_lookDownLeft, _smoothedLidDown * 100f);
                Append(_lookDownRight, _smoothedLidDown * 100f);
                Append(_blinkTargets, _smoothedLidDown * 32f); // partial lid drop
            }

            if (_smoothedLidUp > SubmitEpsilon)
            {
                Append(_lookUpLeft, _smoothedLidUp * 55f);
                Append(_lookUpRight, _smoothedLidUp * 55f);
            }
        }

        /// <summary>
        ///     Layers the emotion-driven lid aperture under the blink: aperture &lt; 1 narrows
        ///     the lids (EyeSquint), aperture &gt; 1 widens them (EyeWide). Rigs without
        ///     squint/wide shapes simply write nothing.
        /// </summary>
        private void AppendAperture(float apertureScale, float blinkWeight01)
        {
            ResolveApertureWeights(apertureScale, blinkWeight01, out float squint, out float wide);
            if (squint > SubmitEpsilon) Append(_squintTargets, squint);
            if (wide > SubmitEpsilon) Append(_wideTargets, wide);
        }

        /// <summary>
        ///     Pure aperture → (squint, wide) blendshape-weight mapping. A blink always closes,
        ///     so both weights are scaled by the open fraction (full blink → zero aperture).
        ///     Split out so the mapping is unit-testable without a rig or compositor.
        /// </summary>
        internal static void ResolveApertureWeights(
            float apertureScale, float blinkWeight01, out float squintWeight, out float wideWeight)
        {
            squintWeight = 0f;
            wideWeight = 0f;

            float openness = 1f - Mathf.Clamp01(blinkWeight01);
            if (openness <= SubmitEpsilon) return;

            if (apertureScale < 1f - SubmitEpsilon)
                squintWeight = (1f - apertureScale) * SquintGain * openness;
            else if (apertureScale > 1f + SubmitEpsilon)
                wideWeight = (apertureScale - 1f) * WideGain * openness;
        }

        private void AppendLookShapes(ConvaiGazeProfile profile, Vector2 angles, bool isLeftEye)
        {
            float yawNorm = Mathf.Clamp01(Mathf.Abs(angles.x) / Mathf.Max(1f, profile.EyeMaxYawDegrees)) * 100f;
            float pitchLimit = angles.y >= 0f ? profile.EyeMaxPitchUpDegrees : profile.EyeMaxPitchDownDegrees;
            float pitchNorm = Mathf.Clamp01(Mathf.Abs(angles.y) / Mathf.Max(1f, pitchLimit)) * 100f;

            // Yaw > 0 = looking toward the character's right. For the LEFT eye that is
            // inward (toward the nose); for the RIGHT eye it is outward.
            bool towardRight = angles.x > 0f;
            List<BlendshapeTargetKey> yawTargets = isLeftEye
                ? (towardRight ? _lookInLeft : _lookOutLeft)
                : (towardRight ? _lookOutRight : _lookInRight);
            if (yawNorm > SubmitEpsilon) Append(yawTargets, yawNorm);

            if (pitchNorm > SubmitEpsilon)
            {
                if (angles.y >= 0f)
                    Append(isLeftEye ? _lookUpLeft : _lookUpRight, pitchNorm);
                else
                    Append(isLeftEye ? _lookDownLeft : _lookDownRight, pitchNorm);
            }
        }

        private void Append(List<BlendshapeTargetKey> targets, float weight)
        {
            if (targets.Count == 0 || weight <= SubmitEpsilon) return;

            for (int i = 0; i < targets.Count; i++)
            {
                _frameTargets.Add(targets[i]);
                _frameWeights.Add(weight);
            }
        }
    }
}
