using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Shared, allocation-free line-of-sight math for gaze providers: whether an opaque
    ///     collider sits on the straight line from an observer's eye to a target, plus the
    ///     exponential visibility smoothing that turns a binary raycast result into the
    ///     natural "lost you / found you again" decay.
    /// </summary>
    /// <remarks>
    ///     Colliders under either excluded root (the character itself and the target's own
    ///     hierarchy) never count as obstructions, so a character cannot occlude itself and
    ///     the player's own colliders are not read as walls. Trigger colliders are ignored.
    /// </remarks>
    internal static class GazeLineOfSight
    {
        /// <summary>Exponential rate (per second) the smoothed visibility eases toward the last raycast result.</summary>
        private const float VisibilitySmoothingRate = 6f;

        /// <summary>
        ///     Returns <c>true</c> when an opaque, unexcluded collider blocks the line from
        ///     <paramref name="origin" /> to <paramref name="target" />. Uses
        ///     <see cref="Physics.RaycastNonAlloc(Vector3, Vector3, RaycastHit[], float, int, QueryTriggerInteraction)" />
        ///     into the caller-owned <paramref name="hits" /> buffer — zero per-call allocation.
        /// </summary>
        public static bool Occluded(
            Vector3 origin,
            Vector3 target,
            int obstructionMask,
            Transform excludeRootA,
            Transform excludeRootB,
            RaycastHit[] hits)
        {
            if (hits == null || hits.Length == 0) return false;

            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return false;

            Vector3 direction = delta / distance;
            int count = Physics.RaycastNonAlloc(
                origin, direction, hits, distance, obstructionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null) continue;

                Transform node = collider.transform;
                if (IsUnder(node, excludeRootA) || IsUnder(node, excludeRootB)) continue;

                return true;
            }

            return false;
        }

        /// <summary>
        ///     Eases <paramref name="current" /> toward <paramref name="target" /> (1 visible,
        ///     0 occluded) at <see cref="VisibilitySmoothingRate" />, so relevance decays over
        ///     a few tenths of a second instead of stepping. Pure — split out for unit tests.
        /// </summary>
        public static float StepVisibility(float current, float target, float deltaTime)
        {
            float alpha = 1f - Mathf.Exp(-VisibilitySmoothingRate * Mathf.Max(0f, deltaTime));
            return Mathf.Lerp(current, target, alpha);
        }

        private static bool IsUnder(Transform node, Transform root)
        {
            if (root == null || node == null) return false;
            return node == root || node.IsChildOf(root);
        }
    }
}
