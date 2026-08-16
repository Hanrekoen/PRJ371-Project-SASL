using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    ///     Immutable snapshot of the character's current gaze decision as published by the
    ///     gaze module through <see cref="Interfaces.IGazeSource" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reading expresses <em>what</em> the character is looking at and <em>how
    ///         committed</em> the look is; the module's solvers turn it into bone and
    ///         blendshape writes internally. Consumers (debug HUDs, the dynamic context
    ///         bridge, custom modules) should treat it as read-only telemetry.
    ///     </para>
    ///     <para>
    ///         <see cref="Target" /> may be <c>null</c> even when a target is engaged (a
    ///         world-space point without a backing transform); prefer
    ///         <see cref="WorldPoint" /> for math and use <see cref="Target" /> only for
    ///         identity/follow semantics. It can be destroyed between frames — always
    ///         null-check before dereferencing.
    ///     </para>
    /// </remarks>
    public readonly struct GazeReading
    {
        /// <summary>Source classification of the current target.</summary>
        public GazeTargetKind TargetKind { get; }

        /// <summary>Optional transform being gazed at. May be <c>null</c>.</summary>
        public Transform Target { get; }

        /// <summary>Smoothed world-space point the gaze is directed toward.</summary>
        public Vector3 WorldPoint { get; }

        /// <summary>
        ///     Effective engagement in <c>[0, 1]</c>: how strongly the eyes/head/body commit
        ///     to the target this frame (state policy × target commitment, smoothed).
        /// </summary>
        public float Engagement { get; }

        /// <summary>
        ///     <c>true</c> while an aversion beat has deliberately broken eye contact (e.g.
        ///     a Thinking look-away). The target is still owned; contact resumes after.
        /// </summary>
        public bool IsAverting { get; }

        /// <summary>
        ///     Stable id that increments whenever gaze moves to a different target, letting
        ///     consumers detect re-targets without comparing transform references.
        /// </summary>
        public int GenerationId { get; }

        public GazeReading(
            GazeTargetKind targetKind,
            Transform target,
            Vector3 worldPoint,
            float engagement,
            bool isAverting,
            int generationId)
        {
            TargetKind = targetKind;
            Target = target;
            WorldPoint = worldPoint;
            Engagement = engagement < 0f ? 0f : engagement > 1f ? 1f : engagement;
            IsAverting = isAverting;
            GenerationId = generationId;
        }

        /// <summary>Disengaged reading — no gaze target.</summary>
        public static GazeReading None => new(GazeTargetKind.None, null, Vector3.zero, 0f, false, 0);
    }
}
