namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     Identifies the blendshape / bone naming convention used by an imported character rig.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The convention is detected automatically at import or setup time by inspecting
    ///         the mesh's blendshape names. Detection happens in
    ///         <c>Convai.Runtime.Animation.RigConventionResolver</c> and the result is cached
    ///         on <c>IStandardRigBinding</c>. Modules never need to branch on this value; they
    ///         only read semantic keys through the binding.
    ///     </para>
    ///     <para>
    ///         The value is surfaced purely for diagnostics, editor tooling (e.g. the setup
    ///         wizard's confidence indicator), and for advanced users who want to author rig
    ///         overrides.
    ///     </para>
    /// </remarks>
    public enum RigConvention
    {
        /// <summary>
        ///     Detection has not been run, or the rig did not match any known convention.
        /// </summary>
        Unknown = 0,

        /// <summary>
        ///     Apple ARKit 52-blendshape convention (<c>eyeBlinkLeft</c>, <c>jawOpen</c>, ...).
        ///     Most VRoid and MetaHuman-export rigs use this.
        /// </summary>
        ARKit = 1,

        /// <summary>
        ///     Reallusion Character Creator 3 base facial rig (<c>Eye_Blink_L</c>,
        ///     <c>V_Open</c>, <c>Mouth_Smile_L</c>, ...).
        /// </summary>
        ReallusionCC3 = 2,

        /// <summary>
        ///     Epic MetaHuman native facial rig (control board suffixes such as
        ///     <c>CTRL_L_mouth_lipSideShift</c>). Usually combined with Face Control Board.
        /// </summary>
        MetaHuman = 3,

        /// <summary>
        ///     Reallusion Character Creator 4 Extended facial profile — a strict superset of
        ///     <see cref="ReallusionCC3" /> that adds per-side mouth-press, cheek-raise,
        ///     nose-sneer, and ARKit-compatible mouth shapes. Detected by the presence of
        ///     signature blendshapes that do not exist in the CC3 base set.
        /// </summary>
        ReallusionCC4Extended = 4,

        /// <summary>
        ///     Rig does not match a built-in convention; the user supplies an explicit
        ///     blendshape-to-semantic mapping through a rig override asset.
        /// </summary>
        Custom = 99
    }
}
