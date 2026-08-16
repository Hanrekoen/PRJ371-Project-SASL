using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     The name a player's transcript is attributed to when nobody has chosen one, and the one
    ///     rule that decides whether a name came from the developer or from this fallback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two places decide the same thing about a player's transcript line: the adapter that
    ///         publishes the turn, and the room engine that builds the actor it is filed under. They
    ///         each used to carry their own copy of the string and their own version of the rule,
    ///         which is how they came to disagree — the same speaker could be labelled one way in
    ///         the published event and another way in the room's actor list.
    ///     </para>
    ///     <para>
    ///         The rule itself: a name the developer configured is authoritative and the backend
    ///         never overrides it, because the server's speaker directory does not know what the
    ///         game calls its player. <see cref="Default" /> is not such a name — it is the absence
    ///         of one — so it yields to the backend's speaker name when the backend supplies one.
    ///         That is what keeps a second speaker in a multi-user room from being labelled with
    ///         the local placeholder.
    ///     </para>
    /// </remarks>
    internal static class PlayerDisplayName
    {
        /// <summary>The name shown for a player whose display name nobody configured.</summary>
        internal const string Default = "You";

        /// <summary>
        ///     Whether <paramref name="name" /> is a display name someone actually chose, as
        ///     opposed to blank or the <see cref="Default" /> placeholder.
        /// </summary>
        internal static bool IsAuthored(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            !string.Equals(name.Trim(), Default, StringComparison.OrdinalIgnoreCase);
    }
}
