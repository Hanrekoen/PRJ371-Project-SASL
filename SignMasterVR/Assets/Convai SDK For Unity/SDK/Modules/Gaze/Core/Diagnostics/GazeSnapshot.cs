using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;
using Convai.Modules.Gaze.Components;

namespace Convai.Modules.Gaze.Core.Diagnostics
{
    /// <summary>
    ///     Mutable, reusable capture of the full gaze runtime state for HUDs and tests.
    ///     Allocate once and refill via <c>ConvaiGazeController.CaptureSnapshot</c>.
    /// </summary>
    public sealed class GazeSnapshot
    {
        /// <summary>The published gaze reading at capture time.</summary>
        public GazeReading Reading;

        /// <summary>Kind of the currently engaged target, or None when disengaged.</summary>
        public GazeTargetKind TargetKind = GazeTargetKind.None;

        /// <summary>Diagnostic name of the currently engaged target, or "-" when disengaged.</summary>
        public string TargetName = "-";

        /// <summary>Dialogue state the policy engine acted on this frame.</summary>
        public DialogueState DialogueState;

        /// <summary>Smoothed policy engagement before target commitment is applied.</summary>
        public float PolicyEngagement;

        /// <summary>Solved torso yaw/pitch contribution in degrees.</summary>
        public Vector2 TorsoAngles;

        /// <summary>Solved head (neck+head) yaw/pitch contribution in degrees.</summary>
        public Vector2 HeadAngles;

        /// <summary>
        ///     Roll (degrees) written to the head bone this frame. Non-zero only while a head
        ///     gesture asks for a tilt: the aim solve produces no roll of its own, so a reading
        ///     here with no gesture running is a composition fault, not a pose.
        /// </summary>
        public float HeadRollDegrees;

        /// <summary>
        ///     The full gaze shift still required this frame — the yaw/pitch (degrees, root
        ///     frame) from the character's eye line to the engaged target, before any actuator
        ///     takes its share. Zero while disengaged.
        /// </summary>
        /// <remarks>
        ///     Together with <see cref="HeadAngles" />, <see cref="TorsoAngles" /> and the eye
        ///     angles this is what makes the coordination measurable: the four contributions
        ///     must sum to this, and a residual that cannot be accounted for is exactly the
        ///     defect class where one actuator backs off and another silently clamps.
        /// </remarks>
        public Vector2 TargetErrorAngles;

        /// <summary>Solved left-eye yaw/pitch in degrees (orbit space).</summary>
        public Vector2 LeftEyeAngles;

        /// <summary>Solved right-eye yaw/pitch in degrees (orbit space).</summary>
        public Vector2 RightEyeAngles;

        /// <summary>Current fixation/saccade phase label ("Fixating", "Saccade", "Pursuit"…).</summary>
        public string EyePhase = "-";

        /// <summary>
        ///     Live angular error (degrees) between where the eyes aim and where the gaze
        ///     target actually is. <see cref="float.NaN" /> while disengaged (no target, or
        ///     the eye stage has no bones and no look shapes).
        /// </summary>
        public float ContactErrorDegrees = float.NaN;

        /// <summary>Whether a product-level conversational focus scope is active.</summary>
        public bool FocusActive;

        /// <summary>Precision contract applied while <see cref="FocusActive" /> is true.</summary>
        public GazeFocusFidelity FocusFidelity;

        /// <summary>Whether focus is retaining a last-known point because its anchor is unavailable.</summary>
        public bool FocusDegraded;

        /// <summary>
        ///     True when the active eye backend drives bones; false identifies the blendshape
        ///     backend. <see cref="ContactErrorDegrees" /> remains a solver-space estimate for
        ///     either backend, not a post-render eye-ray measurement.
        /// </summary>
        public bool ContactUsesBoneBackend;

        /// <summary>Normalized blink weight (0 open, 1 closed).</summary>
        public float BlinkWeight;

        /// <summary>Whether a body reorientation is currently in flight.</summary>
        public bool IsReorienting;

        /// <summary>Whether a listening backchannel nod is currently playing.</summary>
        public bool IsNodding;

        /// <summary>
        ///     Smoothed 0..1 "is the player looking at me" estimate from a Player Attention
        ///     Sensor on the character, or -1 when no sensor is present (or it is disabled).
        /// </summary>
        public float PlayerAttention = -1f;

        /// <summary>
        ///     The sensor's post-hysteresis classification (the same state that drives the
        ///     dynamic-context publishes). Only meaningful while <see cref="PlayerAttention" />
        ///     is not negative.
        /// </summary>
        public bool PlayerLooking;

        /// <summary>Whether crowd LOD is active on this character.</summary>
        public bool LodEnabled;

        /// <summary>Whether the character is in the reduced-rate far LOD band.</summary>
        public bool LodFar;

        /// <summary>Whether the solver stage is being skipped this frame (off-screen LOD).</summary>
        public bool LodExpressionSkipped;

        /// <summary>Recent transition log copied from the trace ring buffer (oldest first).</summary>
        public readonly List<GazeTraceEntry> RecentTrace = new(64);

        /// <summary>Resets all fields to their disengaged defaults.</summary>
        public void Clear()
        {
            Reading = GazeReading.None;
            TargetKind = GazeTargetKind.None;
            TargetName = "-";
            DialogueState = DialogueState.Idle;
            PolicyEngagement = 0f;
            TorsoAngles = Vector2.zero;
            HeadAngles = Vector2.zero;
            HeadRollDegrees = 0f;
            TargetErrorAngles = Vector2.zero;
            LeftEyeAngles = Vector2.zero;
            RightEyeAngles = Vector2.zero;
            EyePhase = "-";
            ContactErrorDegrees = float.NaN;
            FocusActive = false;
            FocusFidelity = GazeFocusFidelity.Social;
            FocusDegraded = false;
            ContactUsesBoneBackend = false;
            BlinkWeight = 0f;
            IsReorienting = false;
            IsNodding = false;
            PlayerAttention = -1f;
            PlayerLooking = false;
            LodEnabled = false;
            LodFar = false;
            LodExpressionSkipped = false;
            RecentTrace.Clear();
        }
    }
}
