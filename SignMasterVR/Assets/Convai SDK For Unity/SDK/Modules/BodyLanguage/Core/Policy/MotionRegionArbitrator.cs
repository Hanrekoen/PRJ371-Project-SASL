using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    internal readonly struct MotionRegionWeights
    {
        public readonly float Master;
        public readonly float Posture;
        public readonly float Breath;
        public readonly float Arms;
        public readonly float LeftArm;
        public readonly float RightArm;
        public readonly float HandMicro;

        public MotionRegionWeights(float master, float posture, float breath, float arms, float handMicro)
        {
            Master = master;
            Posture = posture;
            Breath = breath;
            Arms = arms;
            LeftArm = arms;
            RightArm = arms;
            HandMicro = handMicro;
        }
    }

    /// <summary>Pure regional ownership arbitration between procedural and authored motion.</summary>
    internal static class MotionRegionArbitrator
    {
        internal const float ProceduralArmOccupancyCeiling = 0.15f;

        public static MotionRegionWeights Resolve(
            GestureSuppression suppression,
            bool hasContinuousBudget,
            float upperBodyOccupancy01,
            float retainedPostureWeight)
        {
            float occupancy = Mathf.Clamp01(upperBodyOccupancy01);
            float retained = Mathf.Clamp01(retainedPostureWeight);
            if (suppression == GestureSuppression.FullBody)
                return new MotionRegionWeights(0f, 0f, 0f, 0f, 0f);

            float posture = hasContinuousBudget
                ? 1f - occupancy * (1f - retained)
                : suppression == GestureSuppression.UpperBody ? retained : 1f;
            float arms = suppression == GestureSuppression.None &&
                         (!hasContinuousBudget || occupancy <= ProceduralArmOccupancyCeiling)
                ? 1f
                : 0f;

            return new MotionRegionWeights(1f, posture, 1f, arms, 1f - occupancy);
        }
    }
}
