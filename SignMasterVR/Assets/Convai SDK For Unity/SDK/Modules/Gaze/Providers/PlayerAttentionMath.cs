using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Pure math for <see cref="PlayerAttentionSensor" />: the distance-aware "is the player
    ///     looking at me" cone test, the asymmetric rise/fall smoothing, the hysteresis edge
    ///     decision that drives context publishing, and the gaze-ray source resolution order.
    ///     Kept free of component state so every rule is unit-testable.
    /// </summary>
    internal static class PlayerAttentionMath
    {
        /// <summary>
        ///     Half-angle (degrees) of the acceptance cone at <paramref name="distance" />. A
        ///     nearby character subtends a large visual angle, so the cone widens by the
        ///     character's angular radius as the player approaches — but it is capped at
        ///     <paramref name="maxHalfAngleDegrees" /> so point-blank range does not turn the
        ///     whole forward hemisphere into "looking at me".
        /// </summary>
        public static float ConeHalfAngle(
            float distance, float baseHalfAngleDegrees, float characterAngularRadius, float maxHalfAngleDegrees)
        {
            if (distance <= 1e-3f) return maxHalfAngleDegrees;
            float angular = Mathf.Atan2(Mathf.Max(0f, characterAngularRadius), distance) * Mathf.Rad2Deg;
            return Mathf.Min(maxHalfAngleDegrees, baseHalfAngleDegrees + angular);
        }

        /// <summary>
        ///     Whether <paramref name="ray" /> points at <paramref name="headPivot" /> within the
        ///     distance-aware acceptance cone.
        /// </summary>
        public static bool IsLooking(
            Ray ray, Vector3 headPivot, float baseHalfAngleDegrees, float characterAngularRadius, float maxHalfAngleDegrees)
        {
            Vector3 toHead = headPivot - ray.origin;
            float distance = toHead.magnitude;
            if (distance <= 1e-4f) return true;
            if (ray.direction.sqrMagnitude < 1e-8f) return false;

            float angle = Vector3.Angle(ray.direction, toHead);
            return angle <= ConeHalfAngle(distance, baseHalfAngleDegrees, characterAngularRadius, maxHalfAngleDegrees);
        }

        /// <summary>
        ///     Exponentially eases <paramref name="current" /> toward <paramref name="target01" />
        ///     with a fast rise and a slow fall, so attention snaps on but lingers as it fades —
        ///     the way a person keeps feeling watched a moment after the other looks away.
        /// </summary>
        public static float Step(float current, float target01, float deltaTime, float riseSeconds, float fallSeconds)
        {
            float tau = target01 > current ? riseSeconds : fallSeconds;
            if (tau <= 1e-4f || deltaTime <= 0f) return target01;

            float alpha = 1f - Mathf.Exp(-deltaTime / tau);
            return current + (target01 - current) * alpha;
        }

        /// <summary>
        ///     Hysteresis edge decision for context publishing: crosses to "looking" at
        ///     <paramref name="enterThreshold" /> and back at the lower <paramref name="exitThreshold" />.
        ///     Returns <c>true</c> only on a state change (so publishes stay edge-triggered).
        /// </summary>
        public static bool ResolvePublish(
            bool wasLooking, float attention, float enterThreshold, float exitThreshold, out bool nowLooking)
        {
            nowLooking = wasLooking;
            if (!wasLooking && attention >= enterThreshold) nowLooking = true;
            else if (wasLooking && attention <= exitThreshold) nowLooking = false;
            return nowLooking != wasLooking;
        }

        /// <summary>
        ///     Resolves the player's gaze ray in priority order: an explicit per-character
        ///     source, then the shared default source (e.g. one XR adapter for every character),
        ///     then the supplied fallback camera's forward ray. Returns <c>false</c> when none
        ///     can produce a ray.
        /// </summary>
        public static bool TryResolveGazeRay(
            IPlayerGazeRaySource primary, IPlayerGazeRaySource secondary, Camera fallbackCamera, out Ray ray)
        {
            if (primary != null && primary.TryGetPlayerGazeRay(out ray)) return true;
            if (secondary != null && secondary.TryGetPlayerGazeRay(out ray)) return true;
            if (fallbackCamera != null)
            {
                Transform t = fallbackCamera.transform;
                ray = new Ray(t.position, t.forward);
                return true;
            }

            ray = default;
            return false;
        }
    }
}
