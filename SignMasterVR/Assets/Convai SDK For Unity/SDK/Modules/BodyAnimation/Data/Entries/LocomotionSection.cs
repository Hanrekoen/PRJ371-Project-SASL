using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     All locomotion content for one character set. Only <see cref="Walk" /> is required
    ///     for basic movement; every other slot unlocks an advanced, individually optional
    ///     feature (directional starts, planted stops, speed-change clips, turn-in-place).
    /// </summary>
    [Serializable]
    public sealed class LocomotionSection
    {
        [Header("Movement Loops")]
        [SerializeField] private LocomotionClip _walk = new();
        [SerializeField] private LocomotionClip _jog = new();

        [Header("Walk Starts (in-place, scripted root drive)")]
        [SerializeField] private LocomotionClip _walkStartForward = new();
        [SerializeField] private LocomotionClip _walkStart90Left = new();
        [SerializeField] private LocomotionClip _walkStart90Right = new();
        [SerializeField] private LocomotionClip _walkStart180Left = new();
        [SerializeField] private LocomotionClip _walkStart180Right = new();

        [Header("Jog Starts")]
        [SerializeField] private LocomotionClip _jogStartForward = new();
        [SerializeField] private LocomotionClip _jogStart90Left = new();
        [SerializeField] private LocomotionClip _jogStart90Right = new();
        [SerializeField] private LocomotionClip _jogStart180Left = new();
        [SerializeField] private LocomotionClip _jogStart180Right = new();

        [Header("Walk Stops")]
        [SerializeField] private LocomotionClip _walkStopLeftPlant = new();
        [SerializeField] private LocomotionClip _walkStopRightPlant = new();
        [SerializeField] private LocomotionClip _walkStopLowSpeed = new();
        [SerializeField] private LocomotionClip _walkStopAbrupt = new();

        [Header("Jog Stops")]
        [SerializeField] private LocomotionClip _jogStopLeftPlant = new();
        [SerializeField] private LocomotionClip _jogStopAbrupt = new();

        [Header("Speed Changes")]
        [SerializeField] private LocomotionClip _walkToJogLeft = new();
        [SerializeField] private LocomotionClip _walkToJogRight = new();
        [SerializeField] private LocomotionClip _jogToWalkLeft = new();
        [SerializeField] private LocomotionClip _jogToWalkRight = new();

        [Header("Turn In Place")]
        [SerializeField] private LocomotionClip _turn90Left = new();
        [SerializeField] private LocomotionClip _turn90Right = new();
        [SerializeField] private LocomotionClip _turn180Left = new();
        [SerializeField] private LocomotionClip _turn180Right = new();

        public LocomotionClip Walk => _walk;
        public LocomotionClip Jog => _jog;

        public LocomotionClip WalkStartForward => _walkStartForward;
        public LocomotionClip WalkStart90Left => _walkStart90Left;
        public LocomotionClip WalkStart90Right => _walkStart90Right;
        public LocomotionClip WalkStart180Left => _walkStart180Left;
        public LocomotionClip WalkStart180Right => _walkStart180Right;

        public LocomotionClip JogStartForward => _jogStartForward;
        public LocomotionClip JogStart90Left => _jogStart90Left;
        public LocomotionClip JogStart90Right => _jogStart90Right;
        public LocomotionClip JogStart180Left => _jogStart180Left;
        public LocomotionClip JogStart180Right => _jogStart180Right;

        public LocomotionClip WalkStopLeftPlant => _walkStopLeftPlant;
        public LocomotionClip WalkStopRightPlant => _walkStopRightPlant;
        public LocomotionClip WalkStopLowSpeed => _walkStopLowSpeed;
        public LocomotionClip WalkStopAbrupt => _walkStopAbrupt;

        public LocomotionClip JogStopLeftPlant => _jogStopLeftPlant;
        public LocomotionClip JogStopAbrupt => _jogStopAbrupt;

        public LocomotionClip WalkToJogLeft => _walkToJogLeft;
        public LocomotionClip WalkToJogRight => _walkToJogRight;
        public LocomotionClip JogToWalkLeft => _jogToWalkLeft;
        public LocomotionClip JogToWalkRight => _jogToWalkRight;

        public LocomotionClip Turn90Left => _turn90Left;
        public LocomotionClip Turn90Right => _turn90Right;
        public LocomotionClip Turn180Left => _turn180Left;
        public LocomotionClip Turn180Right => _turn180Right;

        /// <summary>Minimum viable locomotion: a walk loop.</summary>
        public bool HasMovement => _walk.IsValid;

        /// <summary>True when both movement loops exist (enables the walk↔jog speed axis).</summary>
        public bool HasJog => _jog.IsValid;

        public bool HasAnyWalkStart =>
            _walkStartForward.IsValid || _walkStart90Left.IsValid || _walkStart90Right.IsValid ||
            _walkStart180Left.IsValid || _walkStart180Right.IsValid;

        public bool HasAnyWalkStop =>
            _walkStopLeftPlant.IsValid || _walkStopRightPlant.IsValid ||
            _walkStopLowSpeed.IsValid || _walkStopAbrupt.IsValid;

        public bool HasAnyTurn =>
            _turn90Left.IsValid || _turn90Right.IsValid ||
            _turn180Left.IsValid || _turn180Right.IsValid;

        /// <summary>Enumerates every assigned slot with a stable diagnostic label.</summary>
        public void CollectAssigned(List<(string slot, LocomotionClip clip)> destination)
        {
            if (destination == null) return;
            destination.Clear();

            void Add(string slot, LocomotionClip clip)
            {
                if (clip.IsValid) destination.Add((slot, clip));
            }

            Add(nameof(Walk), _walk);
            Add(nameof(Jog), _jog);
            Add(nameof(WalkStartForward), _walkStartForward);
            Add(nameof(WalkStart90Left), _walkStart90Left);
            Add(nameof(WalkStart90Right), _walkStart90Right);
            Add(nameof(WalkStart180Left), _walkStart180Left);
            Add(nameof(WalkStart180Right), _walkStart180Right);
            Add(nameof(JogStartForward), _jogStartForward);
            Add(nameof(JogStart90Left), _jogStart90Left);
            Add(nameof(JogStart90Right), _jogStart90Right);
            Add(nameof(JogStart180Left), _jogStart180Left);
            Add(nameof(JogStart180Right), _jogStart180Right);
            Add(nameof(WalkStopLeftPlant), _walkStopLeftPlant);
            Add(nameof(WalkStopRightPlant), _walkStopRightPlant);
            Add(nameof(WalkStopLowSpeed), _walkStopLowSpeed);
            Add(nameof(WalkStopAbrupt), _walkStopAbrupt);
            Add(nameof(JogStopLeftPlant), _jogStopLeftPlant);
            Add(nameof(JogStopAbrupt), _jogStopAbrupt);
            Add(nameof(WalkToJogLeft), _walkToJogLeft);
            Add(nameof(WalkToJogRight), _walkToJogRight);
            Add(nameof(JogToWalkLeft), _jogToWalkLeft);
            Add(nameof(JogToWalkRight), _jogToWalkRight);
            Add(nameof(Turn90Left), _turn90Left);
            Add(nameof(Turn90Right), _turn90Right);
            Add(nameof(Turn180Left), _turn180Left);
            Add(nameof(Turn180Right), _turn180Right);
        }
    }
}
