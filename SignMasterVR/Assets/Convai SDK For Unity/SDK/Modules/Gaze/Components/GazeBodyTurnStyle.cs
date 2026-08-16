namespace Convai.Modules.Gaze.Components
{
    /// <summary>
    ///     How a character turns its body when it looks at something it cannot reach with head and
    ///     eyes alone.
    /// </summary>
    /// <remarks>
    ///     Neither option is more correct than the other — it is a look, and different projects want
    ///     different looks. A grounded, cinematic character should step around; a stylised or
    ///     first-person one often reads better turning directly, and a character whose Animation Set
    ///     has no turn clips has to.
    /// </remarks>
    public enum GazeBodyTurnStyle
    {
        /// <summary>
        ///     Plays the character's own turn animation, so the feet step round. Needs turn clips in
        ///     the Animation Set and the Body Animation module; without either, the character turns
        ///     smoothly instead rather than failing.
        /// </summary>
        SteppingTurn = 0,

        /// <summary>
        ///     Rotates the character directly, at the speed the gaze profile sets. Needs no clips and
        ///     no animation module, and never competes with a locomotion animation for the same
        ///     rotation.
        /// </summary>
        SmoothRotation = 1
    }
}
