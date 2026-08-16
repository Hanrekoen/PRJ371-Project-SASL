using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Foot-plant phase math over <see cref="ClipMotionMetadata" />: which foot plants
    ///     next in the movement cycle, which foot a one-shot ends on, and the cycle phase a
    ///     follow-up loop should start at so strides continue seamlessly.
    /// </summary>
    internal static class FootPhaseUtil
    {
        /// <summary>
        ///     The foot that plants next after <paramref name="phase" /> in a looping cycle.
        ///     Unknown when the metadata has no plant markers.
        /// </summary>
        public static FootSide NextPlantFoot(ClipMotionMetadata meta, float phase)
        {
            if (meta == null || !meta.HasFootPlants) return FootSide.Unknown;

            float bestDelta = float.MaxValue;
            FootSide best = FootSide.Unknown;

            Scan(meta.LeftFootPlants, FootSide.Left);
            Scan(meta.RightFootPlants, FootSide.Right);
            return best;

            void Scan(float[] plants, FootSide side)
            {
                if (plants == null) return;
                for (int i = 0; i < plants.Length; i++)
                {
                    float delta = Mathf.Repeat(plants[i] - phase, 1f);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        best = side;
                    }
                }
            }
        }

        /// <summary>
        ///     The last foot a (non-looping) one-shot plants before it ends. Unknown when the
        ///     metadata has no plant markers.
        /// </summary>
        public static FootSide LastPlantFoot(ClipMotionMetadata meta)
        {
            if (meta == null || !meta.HasFootPlants) return FootSide.Unknown;

            float lastTime = -1f;
            FootSide side = FootSide.Unknown;

            if (meta.LeftFootPlants != null)
            {
                for (int i = 0; i < meta.LeftFootPlants.Length; i++)
                {
                    if (meta.LeftFootPlants[i] > lastTime)
                    {
                        lastTime = meta.LeftFootPlants[i];
                        side = FootSide.Left;
                    }
                }
            }

            if (meta.RightFootPlants != null)
            {
                for (int i = 0; i < meta.RightFootPlants.Length; i++)
                {
                    if (meta.RightFootPlants[i] > lastTime)
                    {
                        lastTime = meta.RightFootPlants[i];
                        side = FootSide.Right;
                    }
                }
            }

            return side;
        }

        /// <summary>
        ///     Cycle phase the movement loop should start at when a one-shot hands off to it:
        ///     just after the loop's plant of the one-shot's last-planted foot, so the next
        ///     stride continues with the opposite foot. Returns
        ///     <paramref name="fallbackPhase" /> when either side lacks plant markers.
        /// </summary>
        public static float HandoffPhase(
            ClipMotionMetadata oneShotMeta,
            ClipMotionMetadata loopMeta,
            float fallbackPhase = 0f,
            float stanceLead = 0.05f)
        {
            FootSide lastPlant = LastPlantFoot(oneShotMeta);
            if (lastPlant == FootSide.Unknown || loopMeta == null || !loopMeta.HasFootPlants)
                return fallbackPhase;

            float[] plants = lastPlant == FootSide.Left ? loopMeta.LeftFootPlants : loopMeta.RightFootPlants;
            if (plants == null || plants.Length == 0)
                return fallbackPhase;

            return Mathf.Repeat(plants[0] + stanceLead, 1f);
        }
    }
}
