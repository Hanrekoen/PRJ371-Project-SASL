using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Shared tolerances for the "did I still own this transform last frame?" check used
    ///     by procedural animation drivers (eye/head actuators, breathing) so they can refresh
    ///     their cached rest pose if any other system overwrote the bone in between writes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A divergent threshold across drivers risks one driver releasing the cache
    ///         while another keeps it, producing perceptible drift on shared parents. Lift
    ///         the constants and helpers here so every ownership check uses the same
    ///         definition of "still mine".
    ///     </para>
    ///     <para>
    ///         Both helpers are allocation-free and safe to call from hot paths.
    ///     </para>
    /// </remarks>
    public static class OwnershipEpsilon
    {
        /// <summary>Maximum rotation delta (in degrees) treated as "no external write occurred".</summary>
        public const float RotationDegrees = 0.001f;

        /// <summary>Maximum translation squared-magnitude delta treated as "no external write occurred".</summary>
        public const float TranslationSqrMagnitude = 1e-7f;

        /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are within <see cref="RotationDegrees"/>.</summary>
        public static bool Approximately(Quaternion a, Quaternion b) =>
            Quaternion.Angle(a, b) <= RotationDegrees;

        /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are within <see cref="TranslationSqrMagnitude"/>.</summary>
        public static bool Approximately(Vector3 a, Vector3 b) =>
            (a - b).sqrMagnitude <= TranslationSqrMagnitude;
    }
}
