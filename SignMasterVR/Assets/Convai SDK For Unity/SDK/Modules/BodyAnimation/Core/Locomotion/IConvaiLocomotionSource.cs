using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>Minimal public movement data consumed by Body Animation.</summary>
    public interface IConvaiLocomotionSource
    {
        bool IsMoving { get; }
        bool PathPending { get; }
        float Speed { get; }
        float DesiredSpeed { get; }
        float RemainingDistance { get; }
        float SignedAngleToSteering { get; }
        Vector3 Destination { get; }
        event Action<bool> MoveEnded;
    }

    /// <summary>Optional commands for movement providers that accept destinations.</summary>
    public interface IConvaiLocomotionCommands
    {
        bool MoveTo(Vector3 destination);
        void Stop();
    }

    /// <summary>Optional clip-synchronization controls used by advanced locomotion.</summary>
    public interface IConvaiManagedLocomotion
    {
        bool InManagedMotion { get; }
        bool RotationDrivenExternally { get; set; }
        void FreezeAgent(bool frozen);
        void BeginManagedMotion();
        void SetManagedSpeed(float speed);
        void EndManagedMotion();
        void SetAnimationStartGate(bool enabled);
        void ReleaseAnimationStartGate();
        void CompleteMoveFromAnimation();
        void ConfigureSpeeds(float walkSpeed, float jogSpeed);
    }

    /// <summary>Optional root-write lease for anchored actions such as sitting.</summary>
    public interface IConvaiAnchorAlignment
    {
        void BeginRootAlignment();
        void EndRootAlignment(Vector3 rootPosition);
    }
}
