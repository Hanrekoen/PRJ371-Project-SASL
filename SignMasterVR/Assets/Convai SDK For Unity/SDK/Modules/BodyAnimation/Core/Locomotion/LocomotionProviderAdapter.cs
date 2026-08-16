using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>Capability adapter that lets a minimal public provider degrade safely.</summary>
    internal sealed class LocomotionProviderAdapter :
        ILocomotionDrive,
        IConvaiLocomotionSource,
        IConvaiLocomotionCommands,
        IConvaiManagedLocomotion
    {
        private readonly IConvaiLocomotionSource _source;
        private readonly IConvaiLocomotionCommands _commands;
        private readonly IConvaiManagedLocomotion _managed;

        public LocomotionProviderAdapter(IConvaiLocomotionSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _commands = source as IConvaiLocomotionCommands;
            _managed = source as IConvaiManagedLocomotion;
        }

        public bool IsMoving => _source.IsMoving;
        public bool PathPending => _source.PathPending;
        public float Speed => _source.Speed;
        public float DesiredSpeed => _source.DesiredSpeed;
        public float RemainingDistance => _source.RemainingDistance;
        public float SignedAngleToSteering => _source.SignedAngleToSteering;
        public Vector3 Destination => _source.Destination;
        public bool InManagedMotion => _managed?.InManagedMotion ?? false;

        public bool RotationDrivenExternally
        {
            get => _managed?.RotationDrivenExternally ?? false;
            set { if (_managed != null) _managed.RotationDrivenExternally = value; }
        }

        public event Action<bool> MoveEnded
        {
            add => _source.MoveEnded += value;
            remove => _source.MoveEnded -= value;
        }

        public bool MoveTo(Vector3 destination) => _commands?.MoveTo(destination) ?? false;
        public void Stop() => _commands?.Stop();
        public void FreezeAgent(bool frozen) => _managed?.FreezeAgent(frozen);
        public void BeginManagedMotion() => _managed?.BeginManagedMotion();
        public void SetManagedSpeed(float speed) => _managed?.SetManagedSpeed(speed);
        public void EndManagedMotion() => _managed?.EndManagedMotion();
        public void ReleaseAnimationStartGate() => _managed?.ReleaseAnimationStartGate();
        public void CompleteMoveFromAnimation() => _managed?.CompleteMoveFromAnimation();
        public void SetAnimationStartGate(bool enabled) => _managed?.SetAnimationStartGate(enabled);
        public void ConfigureSpeeds(float walkSpeed, float jogSpeed) => _managed?.ConfigureSpeeds(walkSpeed, jogSpeed);
    }
}
