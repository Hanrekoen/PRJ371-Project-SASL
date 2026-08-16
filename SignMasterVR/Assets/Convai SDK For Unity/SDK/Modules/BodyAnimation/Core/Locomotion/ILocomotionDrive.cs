using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     NavMesh movement authority contract consumed by the locomotion state machine.
    ///     Extracted from <see cref="Components.ConvaiNavMeshLocomotion" /> so the layer can
    ///     be driven by a stub in tests.
    /// </summary>
    internal interface ILocomotionDrive
    {
        /// <summary>True while the agent is displacing the character (or about to).</summary>
        bool IsMoving { get; }

        /// <summary>Live horizontal speed (m/s).</summary>
        float Speed { get; }

        /// <summary>Commanded travel speed (m/s) for the current move.</summary>
        float DesiredSpeed { get; }

        /// <summary>
        ///     Remaining travel distance, or 0 when idle.
        /// </summary>
        float RemainingDistance { get; }

        /// <summary>
        ///     Signed yaw (degrees, +right/−left) from the character's forward to the current
        ///     steering direction. 0 when idle. Drives directional start/turn selection.
        /// </summary>
        float SignedAngleToSteering { get; }

        /// <summary>Current destination while moving.</summary>
        Vector3 Destination { get; }

        /// <summary>True while the animation system is slaving the agent's speed to a clip.</summary>
        bool InManagedMotion { get; }

        /// <summary>
        ///     When true, path-follow rotation is suspended: the animation system (turn
        ///     clips, directional starts) is rotating the character instead.
        /// </summary>
        bool RotationDrivenExternally { get; set; }

        /// <summary>True while the agent exists and its path is still being computed.</summary>
        bool PathPending { get; }

        /// <summary>Raised when movement ends. True = destination reached, false = canceled.</summary>
        event Action<bool> MoveEnded;

        /// <summary>Cancels the current move immediately.</summary>
        void Stop();

        /// <summary>
        ///     Freezes/unfreezes the agent in place (turn-in-place: the NavMeshAgent must not
        ///     translate while the turn clip rotates the character).
        /// </summary>
        void FreezeAgent(bool frozen);

        /// <summary>
        ///     Begins clip-slaved movement: rotation follows the animation, auto-braking is
        ///     suspended, and acceleration is raised so speed commands track tightly.
        /// </summary>
        void BeginManagedMotion();

        /// <summary>Commands the agent speed (m/s) for the current managed frame.</summary>
        void SetManagedSpeed(float speed);

        /// <summary>Ends clip-slaved movement and restores normal path following.</summary>
        void EndManagedMotion();

        /// <summary>Releases a held animation-start gate (see <see cref="SetAnimationStartGate" />).</summary>
        void ReleaseAnimationStartGate();

        /// <summary>Completes the current move from the animation side (planted stop landed).</summary>
        void CompleteMoveFromAnimation();

        /// <summary>
        ///     Lets the body animation layer hold brand-new paths until it has selected and
        ///     entered the matching start/turn/move state.
        /// </summary>
        void SetAnimationStartGate(bool enabled);

        /// <summary>Pushes authoritative speeds from the body animation config.</summary>
        void ConfigureSpeeds(float walkSpeed, float jogSpeed);
    }
}
