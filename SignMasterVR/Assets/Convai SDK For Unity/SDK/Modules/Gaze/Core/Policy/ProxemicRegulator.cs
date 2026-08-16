using UnityEngine;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     Proxemic intimacy regulation: equilibrium theory holds that closeness and eye
    ///     contact trade off — as the player leans in very close (VR lean-in), unbroken eye
    ///     contact reads as an unnerving fixed stare rather than presence, so contact softens
    ///     instead of intensifying. Produces a smoothed 0–1 closeness factor and three output
    ///     scales (aversion floor, face-scan radius, blink rate) that the controller composes
    ///     into its existing policy pipeline. Backing off restores full contact. Pure math, no
    ///     randomness, no allocation.
    /// </summary>
    internal sealed class ProxemicRegulator
    {
        /// <summary>Distance band (meters) over which closeness ramps from 0 to 1 below the close-distance threshold.</summary>
        private const float FalloffMeters = 0.25f;

        /// <summary>Exponential response time (seconds) of the smoothed closeness factor — settles in ~0.5 s, no flicker at the boundary.</summary>
        private const float SmoothingSeconds = 0.5f;

        /// <summary>Aversion-strength floor at full closeness/intensity (Natural-mode-only consumption; the controller applies it as <c>max(strength, floor)</c>).</summary>
        private const float MaxAversionFloor = 0.2f;

        /// <summary>Face-scan radius boost at full closeness/intensity (multiplier reaches 1.5).</summary>
        private const float MaxFaceScanBoost = 0.5f;

        /// <summary>Blink-rate boost at full closeness/intensity (multiplier reaches 1.2).</summary>
        private const float MaxBlinkBoost = 0.2f;

        private float _smoothedCloseness;

        /// <summary>Smoothed closeness factor 0..1 (diagnostics/tests).</summary>
        public float Closeness => _smoothedCloseness;

        /// <summary>Aversion-strength floor (0 = no floor) — the controller applies it only outside an eye-contact lock.</summary>
        public float AversionFloor { get; private set; }

        /// <summary>Multiplier on face-scan radius (1 = unmodified).</summary>
        public float FaceScanRadiusScale { get; private set; } = 1f;

        /// <summary>Multiplier on blink rate (1 = unmodified).</summary>
        public float BlinkRateScale { get; private set; } = 1f;

        public void Reset()
        {
            _smoothedCloseness = 0f;
            AversionFloor = 0f;
            FaceScanRadiusScale = 1f;
            BlinkRateScale = 1f;
        }

        /// <summary>
        ///     Advances the smoothed closeness factor toward this tick's instantaneous reading
        ///     and recomputes the three output scales. Safe to tick every frame regardless of
        ///     an active eye-contact lock — the controller decides whether to apply the outputs;
        ///     ticking unconditionally keeps the smoother continuous so there is no jump the
        ///     moment a lock releases.
        /// </summary>
        /// <param name="regulationEnabled">Profile opt-out (<c>ConvaiGazeProfile.EnableProxemicRegulation</c>) — false decays straight to neutral.</param>
        /// <param name="hasDistance">Whether a live player distance reading is available this tick (false = player absent, decays to neutral).</param>
        /// <param name="distanceMeters">Distance (meters) from the character's gaze origin to the player. Ignored when <paramref name="hasDistance" /> is false.</param>
        /// <param name="closeDistanceMeters">Distance at which closeness starts ramping in (profile-authored).</param>
        /// <param name="intensity">Overall effect strength 0..1 (profile-authored).</param>
        /// <param name="deltaTime">Frame delta time (seconds).</param>
        public void Tick(
            bool regulationEnabled,
            bool hasDistance,
            float distanceMeters,
            float closeDistanceMeters,
            float intensity,
            float deltaTime)
        {
            float targetCloseness = regulationEnabled && hasDistance
                ? Mathf.Clamp01((closeDistanceMeters - distanceMeters) / FalloffMeters)
                : 0f;

            // Exponential smoothing toward the target: bounded per-tick delta (no step change)
            // and small enough movement per tick that a ±2 cm oscillation around the threshold
            // never reads as flicker in the composed outputs.
            float rate = deltaTime > 0f ? 1f - Mathf.Exp(-deltaTime / SmoothingSeconds) : 1f;
            _smoothedCloseness = Mathf.Lerp(_smoothedCloseness, targetCloseness, rate);

            float weighted = _smoothedCloseness * Mathf.Clamp01(intensity);
            AversionFloor = MaxAversionFloor * weighted;
            FaceScanRadiusScale = 1f + MaxFaceScanBoost * weighted;
            BlinkRateScale = 1f + MaxBlinkBoost * weighted;
        }
    }
}
