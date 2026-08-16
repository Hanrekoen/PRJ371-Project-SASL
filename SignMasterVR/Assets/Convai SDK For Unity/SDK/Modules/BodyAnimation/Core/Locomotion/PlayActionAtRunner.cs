using System;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Drives one <c>PlayActionAt</c> request end-to-end: MoveTo the anchor's approach
    ///     point → wait for arrival and the planted-stop settle → root-align (or degrade) →
    ///     play the action. Pure state machine over <see cref="IAnchorMovementDrive" /> and an
    ///     action-play callback — no direct Unity component access, so it is fully unit
    ///     testable with a stub drive (same pattern as <c>LocomotionLayer</c> tests).
    /// </summary>
    internal sealed class PlayActionAtRunner
    {
        private readonly ActionEntry _entry;
        private readonly ActionPlayOptions _playOptions;
        private readonly ActionAnchorOptions _options;
        private readonly AnchorPose _anchorPose;
        private readonly IAnchorMovementDrive _drive;
        private readonly Func<ActionEntry, ActionPlayOptions, BodyAnimationActionHandle> _playAction;
        private readonly Action<string> _logDegraded;

        private Vector3 _targetPosition;
        private float _targetYaw;
        private bool _hasTargetYaw;
        private Vector3 _alignFromPosition;
        private float _alignFromYaw;
        private float _alignTimer;
        private bool _waitingForSettle;
        private bool _moveEndedHooked;
        private BodyAnimationActionHandle _innerHandle;

        public PlayActionAtHandle Handle { get; }

        public PlayActionAtRunner(
            ActionEntry entry,
            in ActionPlayOptions playOptions,
            ActionAnchorOptions options,
            in AnchorPose anchorPose,
            IAnchorMovementDrive drive,
            Func<ActionEntry, ActionPlayOptions, BodyAnimationActionHandle> playAction,
            Action<string> logDegraded)
        {
            _entry = entry;
            _playOptions = playOptions;
            _options = options;
            _anchorPose = anchorPose;
            _drive = drive;
            _playAction = playAction;
            _logDegraded = logDegraded;

            Handle = new PlayActionAtHandle(entry.ActionName) { CancelRequested = Cancel };
        }

        /// <summary>Issues the initial MoveTo. Call once, immediately after construction.</summary>
        public void Start()
        {
            _targetPosition = AnchorAlignmentSolver.ComputeApproachPoint(_anchorPose, _options.ApproachOffset);

            if (!_drive.MoveTo(_targetPosition))
            {
                Degrade("no path to the anchor's approach point");
                return;
            }

            _drive.MoveEnded += HandleMoveEnded;
            _moveEndedHooked = true;
        }

        /// <summary>Advances the alignment lerp / settle poll. No-op once the request is done.</summary>
        public void Tick(float deltaTime)
        {
            if (Handle.IsDone) return;

            switch (Handle.Phase)
            {
                case PlayActionAtPhase.Approaching:
                    if (_waitingForSettle && _drive.IsSettled)
                    {
                        _waitingForSettle = false;
                        CompleteApproach();
                    }
                    break;

                case PlayActionAtPhase.Aligning:
                    TickAlign(deltaTime);
                    break;

                case PlayActionAtPhase.PlayingAction:
                    TickPlayingAction();
                    break;
            }
        }

        /// <summary>
        ///     Cancels wherever the request currently is. Idempotent — safe to call repeatedly
        ///     or after the request already finished.
        /// </summary>
        public void Cancel()
        {
            if (Handle.IsDone) return;

            switch (Handle.Phase)
            {
                case PlayActionAtPhase.Approaching:
                    UnhookMoveEnded();
                    _drive.Stop();
                    break;

                case PlayActionAtPhase.Aligning:
                    // Freezes in place — the lerp simply stops where it currently is.
                    _drive.EndAlignment();
                    break;

                case PlayActionAtPhase.PlayingAction:
                    // Graceful stop (outro plays when authored), matching PlayAction's own
                    // Stop() semantics — the outer contract resolves Canceled regardless of
                    // how the inner action's own Completion eventually settles.
                    _innerHandle?.Stop();
                    break;
            }

            Resolve(PlayActionAtPhase.Canceled, false);
        }

        // ------------------------------------------------------------------ approach

        private void HandleMoveEnded(bool arrived)
        {
            UnhookMoveEnded();
            if (Handle.IsDone) return;

            if (!arrived)
            {
                Resolve(PlayActionAtPhase.Canceled, false);
                return;
            }

            _waitingForSettle = true;
        }

        private void CompleteApproach()
        {
            Vector3 currentPosition = _drive.RootPosition;
            float currentYaw = _drive.RootYawDegrees;

            _targetYaw = AnchorAlignmentSolver.ComputeTargetYaw(
                _anchorPose, _targetPosition, _options.FacingMode, currentYaw);
            _hasTargetYaw = _options.FacingMode != ActionFacingMode.None;

            bool withinEnvelope = AnchorAlignmentSolver.IsWithinEnvelope(
                currentPosition, currentYaw, _targetPosition, _targetYaw, _options.FacingMode,
                _options.MaxAlignmentDistance, _options.MaxAlignmentYawDegrees);

            if (!withinEnvelope)
            {
                Degrade("arrived outside the alignment envelope (blocked path or short leg)");
                return;
            }

            _alignFromPosition = currentPosition;
            _alignFromYaw = currentYaw;
            _alignTimer = 0f;
            _drive.BeginAlignment();
            Handle.SetPhase(PlayActionAtPhase.Aligning);
        }

        // ------------------------------------------------------------------ align

        private void TickAlign(float deltaTime)
        {
            _alignTimer += deltaTime;
            float t = Mathf.Clamp01(_alignTimer / _options.AlignmentDurationSeconds);

            Vector3 position = AnchorAlignmentSolver.LerpPosition(_alignFromPosition, _targetPosition, t);
            float yaw = _hasTargetYaw
                ? AnchorAlignmentSolver.LerpYaw(_alignFromYaw, _targetYaw, t)
                : _alignFromYaw;
            _drive.SetAlignmentPose(position, yaw);

            if (t < 1f) return;

            _drive.EndAlignment();
            StartAction();
        }

        // ------------------------------------------------------------------ act

        private void Degrade(string reason)
        {
            _logDegraded?.Invoke(reason);
            StartAction();
        }

        private void StartAction()
        {
            Handle.SetPhase(PlayActionAtPhase.PlayingAction);
            _innerHandle = _playAction(_entry, _playOptions);

            if (_innerHandle == null)
            {
                // PlayAction itself refused (busy/non-interruptible/unknown) — the whole
                // anchored request fails the same way.
                Resolve(PlayActionAtPhase.Canceled, false);
            }
        }

        private void TickPlayingAction()
        {
            if (_innerHandle == null || !_innerHandle.IsDone) return;
            Resolve(
                _innerHandle.Completion.Result ? PlayActionAtPhase.Completed : PlayActionAtPhase.Canceled,
                _innerHandle.Completion.Result);
        }

        // ------------------------------------------------------------------ shared

        private void UnhookMoveEnded()
        {
            if (!_moveEndedHooked) return;
            _drive.MoveEnded -= HandleMoveEnded;
            _moveEndedHooked = false;
        }

        private void Resolve(PlayActionAtPhase phase, bool completed)
        {
            Handle.SetPhase(phase);
            if (completed) Handle.ResolveCompleted();
            else Handle.ResolveCanceled();
        }
    }
}
