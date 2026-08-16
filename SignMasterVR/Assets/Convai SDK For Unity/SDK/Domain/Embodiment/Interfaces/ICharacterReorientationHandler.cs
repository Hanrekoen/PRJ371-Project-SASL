using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns full-body reorientation (turn-in-place).
    ///     The gaze module requests turns through this interface instead of referencing the
    ///     body animation assembly directly; when no handler is registered it falls back to
    ///     its own procedural root-yaw driver.
    /// </summary>
    /// <remarks>
    ///     Implementations must be safe to call every frame: <see cref="TryReorient" />
    ///     while a turn is already in flight is a <em>re-aim</em> — the handler should steer
    ///     the remaining rotation toward the new direction (targets move mid-turn) and
    ///     return <c>true</c> without restarting the motion. Returning <c>false</c> signals
    ///     the request cannot be honored right now (feature disabled, clips missing,
    ///     locomotion busy) so the caller can degrade gracefully.
    /// </remarks>
    internal interface ICharacterReorientationHandler
    {
        /// <summary>Whether a reorientation is currently being performed by this handler.</summary>
        bool IsReorienting { get; }

        /// <summary>
        ///     Requests the character rotate to face <paramref name="worldDirection" />
        ///     (projected onto the ground plane). Returns <c>false</c> when the handler
        ///     cannot honor the request, in which case the caller should fall back.
        /// </summary>
        /// <param name="worldDirection">Desired facing direction in world space.</param>
        /// <param name="reason">Human-readable reason recorded in diagnostics.</param>
        bool TryReorient(Vector3 worldDirection, string reason);

        /// <summary>
        ///     Cancels an in-flight reorientation, blending back to idle. Safe to call when
        ///     nothing is running.
        /// </summary>
        /// <param name="reason">Human-readable reason recorded in diagnostics.</param>
        void CancelReorientation(string reason);
    }
}
