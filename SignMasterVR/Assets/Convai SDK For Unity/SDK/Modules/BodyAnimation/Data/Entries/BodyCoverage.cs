namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     How much of the skeleton a talk variant is allowed to drive while it overlays the
    ///     base/locomotion pose.
    /// </summary>
    public enum BodyCoverage
    {
        /// <summary>
        ///     Clip drives only the upper body (via the set's upper-body mask). Legs and root
        ///     stay owned by the base layer, so the variant is safe while standing or moving.
        /// </summary>
        UpperBody = 0,

        /// <summary>
        ///     Clip drives the whole skeleton. Only sensible while the character is stationary;
        ///     the talk layer automatically falls back to upper-body coverage during locomotion.
        /// </summary>
        FullBody = 1
    }
}
