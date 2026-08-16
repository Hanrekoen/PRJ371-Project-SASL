using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Movement/root-write authority consumed by <see cref="PlayActionAtRunner" />:
    ///     command a walk to a point, observe the planted-stop settle, then take direct write
    ///     authority over the root position/yaw for the alignment lerp. Extracted so the
    ///     runner can be driven by a stub in tests, same pattern as
    ///     <see cref="ILocomotionDrive" /> for <c>LocomotionLayer</c>.
    /// </summary>
    internal interface IAnchorMovementDrive
    {
        /// <summary>Commands a walk to a world position. False when no path is available.</summary>
        bool MoveTo(Vector3 worldPosition);

        /// <summary>Cancels the current move immediately.</summary>
        void Stop();

        /// <summary>Raised when a commanded move ends. True = destination reached, false = canceled.</summary>
        event Action<bool> MoveEnded;

        /// <summary>
        ///     True once the locomotion state machine has planted its stop and settled in Idle
        ///     — the "planted stop" guarantee <c>PlayActionAt</c> waits on before aligning.
        ///     Only meaningful once a move has ended; false while displacing.
        /// </summary>
        bool IsSettled { get; }

        /// <summary>Current world position of the character root.</summary>
        Vector3 RootPosition { get; }

        /// <summary>Current yaw (degrees) of the character root.</summary>
        float RootYawDegrees { get; }

        /// <summary>
        ///     Takes root-write authority for the alignment lerp: freezes the NavMeshAgent and
        ///     suspends its path-follow rotation so direct root writes never fight it — the
        ///     same coordination turn-in-place already uses.
        /// </summary>
        void BeginAlignment();

        /// <summary>Writes the root position/yaw for the current alignment frame.</summary>
        void SetAlignmentPose(Vector3 position, float yawDegrees);

        /// <summary>Releases root-write authority, restoring normal agent control.</summary>
        void EndAlignment();
    }
}
