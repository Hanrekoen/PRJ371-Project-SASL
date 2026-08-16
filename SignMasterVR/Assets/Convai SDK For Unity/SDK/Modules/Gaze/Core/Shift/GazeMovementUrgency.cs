namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     What kind of movement a gaze shift is, which is what decides how long it takes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Amplitude alone does not describe a head movement. Drifting to a new spot on the wall
    ///         because there is nothing else to do, and turning to face someone who just spoke, are
    ///         the same size and nothing like the same movement. Running one duration law over both
    ///         makes idle life read as alert, which is the wrong personality far more often than it
    ///         is the right one.
    ///     </para>
    ///     <para>
    ///         Deliberately three coarse classes rather than a continuous scalar: the distinction
    ///         being drawn is categorical (why the character is moving), and a float invites callers
    ///         to invent gradations nobody can perceive.
    ///     </para>
    /// </remarks>
    internal enum GazeMovementUrgency
    {
        /// <summary>
        ///     Idle life — ambient exploration, and returning to neutral when there is nothing to
        ///     look at. Nothing is asking for the character's attention, so the movement takes its
        ///     time.
        /// </summary>
        Relaxed = 0,

        /// <summary>
        ///     Ordinary attention: looking at whoever is being talked to, following a target,
        ///     acting on a scripted look. <b>This includes looking at the player</b> — a person
        ///     entering conversational range is not an emergency, and treating it as one is what
        ///     makes a character read as startled by its own user.
        /// </summary>
        Neutral = 1,

        /// <summary>
        ///     Something demanded attention now: a startle reaction, or re-acquiring a target after
        ///     a camera cut or teleport. Reserved for genuine reflexes — everything voluntary is
        ///     <see cref="Neutral" />.
        /// </summary>
        Urgent = 2
    }
}
