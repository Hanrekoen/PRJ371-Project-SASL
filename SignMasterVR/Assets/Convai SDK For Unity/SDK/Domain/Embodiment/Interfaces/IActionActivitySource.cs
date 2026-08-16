namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Publishes whether the character is currently carrying out something it was asked to
    ///     do, so the rest of the embodiment stack can treat that as being engaged with the
    ///     player.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implemented by <c>Convai.Runtime.Actions.ConvaiActionDispatcher</c> and registered
    ///         on the character's embodiment context.
    ///     </para>
    ///     <para>
    ///         It exists because "engaged" was measured only in speech. A character told to walk
    ///         somewhere and then left to walk it says nothing for several seconds, so the
    ///         conversation state decayed to Idle mid-errand — and every module that reads the
    ///         state acted accordingly, most visibly Gaze, which stopped treating the player as
    ///         worth looking at. The character walked up to you on your instruction and then
    ///         looked at the wall. Doing what you asked is engagement; only the state machine
    ///         had no way to know it was happening.
    ///     </para>
    ///     <para>
    ///         Absence degrades to a single null check with no behaviour change (mirrors
    ///         <see cref="ITravelIntentSource" />): a character with no Action Runner is
    ///         exactly as engaged as it was before actions existed.
    ///     </para>
    /// </remarks>
    internal interface IActionActivitySource
    {
        /// <summary>
        ///     True while the character is executing, or is about to execute, work it was given.
        ///     Read once per tick.
        /// </summary>
        bool IsPerformingAction { get; }
    }
}
