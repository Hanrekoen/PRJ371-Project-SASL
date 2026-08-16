namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for a module that drives a character's per-eye visual appearance from a
    ///     normalized arousal signal. Gaze publishes pupil dilation through this interface every
    ///     tick when an implementation is registered on the character's <c>EmbodimentContext</c>;
    ///     the seam is entirely optional — with no implementation registered, publishing costs a
    ///     single null check and nothing is written.
    /// </summary>
    /// <remarks>
    ///     A concrete implementation (e.g. a shader-property material driver) owns the mapping
    ///     from the normalized signal to its own physical range (percentage, UV scale, etc.) and
    ///     must be safe to call every frame, including with repeated identical values.
    /// </remarks>
    internal interface IEyeAppearanceDriver
    {
        /// <summary>
        ///     Sets the current pupil dilation, normalized to <c>[0, 1]</c> where 0 is resting
        ///     (no dilation) and 1 is the maximum modeled dilation. Callers pass an already
        ///     clamped, already smoothed value; implementations should not re-smooth it.
        /// </summary>
        /// <param name="normalized01">Pupil dilation in <c>[0, 1]</c>.</param>
        void SetPupilDilation(float normalized01);
    }
}
