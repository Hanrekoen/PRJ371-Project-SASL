using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Where a character that has decided to stop should stop, and how much room it needs to
    ///     get there. Pure arithmetic over a path and a speed — no agent, no scene — so the rule a
    ///     graceful stop follows can be pinned down in a test instead of only in play.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a stop needs room at all.</b> Ending a walk by deleting its path stops the
    ///         animation on the frame it is asked and leaves the NavMesh agent coasting down under
    ///         its own acceleration. The two disagree for the best part of a metre, and what that
    ///         looks like is a character sliding across the floor with its feet standing still. A
    ///         stop that is walked out instead — the character keeps moving, to a point a stride
    ///         ahead — keeps both systems telling the same story.
    ///     </para>
    ///     <para>
    ///         <b>Why the path and not the forward vector.</b> A character asked to stop while
    ///         rounding a corner has a forward that points at the wall. The braking point is
    ///         measured along the path it is actually walking, so the run-out follows the corner.
    ///     </para>
    /// </remarks>
    internal static class LocomotionBraking
    {
        /// <summary>
        ///     How much path a character needs to shed <paramref name="speed" /> on its own
        ///     braking alone. The fallback for a character with no body animation content to size
        ///     the stop from.
        /// </summary>
        /// <param name="speed">Current ground speed (m/s).</param>
        /// <param name="acceleration">The agent's acceleration (m/s²); floored so it can't divide by zero.</param>
        /// <param name="minimumDistance">
        ///     The shortest run-out to ask for whatever the arithmetic says. A character barely
        ///     moving still needs a step to land on rather than stopping between footfalls.
        /// </param>
        internal static float PhysicalDistance(float speed, float acceleration, float minimumDistance)
        {
            float safeAcceleration = Mathf.Max(0.5f, acceleration);
            float physical = Mathf.Max(0f, speed) * Mathf.Max(0f, speed) / (2f * safeAcceleration);
            return Mathf.Max(Mathf.Max(0f, minimumDistance), physical);
        }

        /// <summary>
        ///     Walks <paramref name="distance" /> metres along a corner list and reports where that
        ///     lands.
        /// </summary>
        /// <param name="corners">The path corners, starting at the character.</param>
        /// <param name="cornerCount">How many entries of <paramref name="corners" /> are in use.</param>
        /// <param name="distance">How far along to travel, in metres.</param>
        /// <param name="point">Where that distance lands.</param>
        /// <returns>
        ///     True when the point sits inside the path. False when the path is shorter than the
        ///     distance asked for — <paramref name="point" /> is then the path's own end, because a
        ///     character with less path left than it needs to stop is already inside its stopping
        ///     envelope and its existing destination is the braking point.
        /// </returns>
        internal static bool TryPointAlongPath(
            Vector3[] corners, int cornerCount, float distance, out Vector3 point)
        {
            point = default;
            if (corners == null || cornerCount < 2)
                return false;

            cornerCount = Mathf.Min(cornerCount, corners.Length);
            float remaining = Mathf.Max(0f, distance);
            Vector3 previous = corners[0];
            point = previous;

            for (int i = 1; i < cornerCount; i++)
            {
                Vector3 corner = corners[i];
                float leg = Vector3.Distance(previous, corner);
                if (leg >= remaining)
                {
                    point = leg > 1e-4f ? Vector3.Lerp(previous, corner, remaining / leg) : corner;
                    return true;
                }

                remaining -= leg;
                previous = corner;
            }

            point = previous;
            return false;
        }
    }
}
