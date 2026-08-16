namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Where a command being read came from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two paths are read by the same code on purpose — two copies of "what is wrong with
    ///         this command" would answer differently within a release, and then the explanation a
    ///         developer reads while testing would stop matching what happens in a conversation. But
    ///         they are not identical, and this names the two places they differ so neither is decided
    ///         by a bare <c>bool</c> at a call site.
    ///     </para>
    ///     <para>
    ///         <b>Refusal.</b> A command off the wire may be turned away, because a stale or
    ///         hallucinated one should never reach an Action Behavior. A command written by hand in a
    ///         customer's own code is neither, and refusing it would change what existing callers get.
    ///     </para>
    ///     <para>
    ///         <b>The speech gate.</b> The backend sends a name and a target and nothing else, so a
    ///         command off the wire has no opinion about waiting for the character to finish speaking
    ///         and the definition's is the only one there is. A caller may have set one deliberately,
    ///         and it is theirs to keep.
    ///     </para>
    /// </remarks>
    internal enum ConvaiActionReadSource
    {
        /// <summary>The backend sent it. It may be refused, and the definition supplies its speech gate.</summary>
        Wire = 0,

        /// <summary>
        ///     A caller handed it to <c>ConvaiActionDispatcher.EnqueueActions</c>. It is read and
        ///     explained the same way, never refused, and keeps whatever the caller set.
        /// </summary>
        LocalCaller = 1
    }
}
