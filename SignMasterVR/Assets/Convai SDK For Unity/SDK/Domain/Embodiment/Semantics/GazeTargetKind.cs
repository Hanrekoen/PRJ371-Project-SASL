namespace Convai.Domain.Embodiment.Semantics
{
    /// <summary>
    ///     Classifies where the character's current gaze target came from, so consumers
    ///     (HUDs, bridges, other modules) can react to the source of attention rather than
    ///     just its position.
    /// </summary>
    public enum GazeTargetKind
    {
        /// <summary>No target — the gaze system is fully disengaged.</summary>
        None = 0,

        /// <summary>
        ///     Ambient idle exploration — a synthetic fixation point with no scene meaning.
        /// </summary>
        Ambient = 1,

        /// <summary>The player anchor (camera or explicit player transform).</summary>
        Player = 2,

        /// <summary>A scene object surfaced by a world-object gaze target provider.</summary>
        WorldObject = 3,

        /// <summary>A scripted <c>GazeAt</c> request (API call or action executor).</summary>
        Scripted = 4,

        /// <summary>
        ///     Another Convai character, surfaced by a character-to-character gaze provider:
        ///     listeners look at whoever is speaking and idle characters exchange glances.
        /// </summary>
        Character = 5,

        /// <summary>
        ///     The path ahead while the character is travelling — a point along its direction of
        ///     travel, not a thing in the scene. Distinct from <see cref="Ambient" /> because it is
        ///     purposeful rather than idle, and distinct from a scene object because there is
        ///     nothing there: consumers that resolve gaze to an object (attention reporting, dynamic
        ///     context) must skip it rather than name it.
        /// </summary>
        TravelPath = 6
    }
}
