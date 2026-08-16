using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Marks any GameObject as a gaze candidate for every Convai character — drag-drop, no
    ///     scene metadata required. The priority tier decides who wins: the player anchor
    ///     publishes at 10, so the default 5 yields to the player during conversation; raise
    ///     above 10 to make this target outrank even the player — the "focus here" story.
    /// </summary>
    [AddComponentMenu("Convai/Gaze/Target")]
    [DisallowMultipleComponent]
    public sealed class ConvaiGazeTarget : MonoBehaviour
    {
        private static readonly List<ConvaiGazeTarget> Registry = new(16);

        /// <summary>Enabled targets in the scene (polled by gaze controllers).</summary>
        internal static IReadOnlyList<ConvaiGazeTarget> ActiveTargets => Registry;

        [SerializeField]
        [Tooltip("Priority tier. The player anchor publishes at 10 — keep below it so the player " +
                 "wins during conversation, or raise above 10 to make this target outrank even the player.")]
        private int priority = 5;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Base relevance when the target is inside the full-relevance distance.")]
        private float baseRelevance = 0.75f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) beyond which the target is not a gaze candidate.")]
        private float maxDistance = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) below which relevance is at its maximum.")]
        private float fullRelevanceDistance = 3f;

        [SerializeField]
        [Tooltip("Local-space offset from the transform to the exact point the eyes aim at " +
                 "(e.g. the top of a painting).")]
        private Vector3 aimOffset;

        private bool _registered;

        /// <summary>Priority tier. Higher tiers always win over lower ones.</summary>
        public int Priority
        {
            get => priority;
            set => priority = value;
        }

        /// <summary>Base relevance when the target is inside the full-relevance distance.</summary>
        public float BaseRelevance
        {
            get => baseRelevance;
            set => baseRelevance = Mathf.Clamp01(value);
        }

        /// <summary>Distance (meters) beyond which the target is not a gaze candidate.</summary>
        public float MaxDistance
        {
            get => maxDistance;
            set
            {
                maxDistance = Mathf.Max(0f, value);
                fullRelevanceDistance = Mathf.Clamp(fullRelevanceDistance, 0f, maxDistance);
            }
        }

        /// <summary>Distance (meters) below which relevance is at its maximum.</summary>
        public float FullRelevanceDistance
        {
            get => fullRelevanceDistance;
            set => fullRelevanceDistance = Mathf.Clamp(value, 0f, maxDistance);
        }

        /// <summary>
        ///     Local-space offset from the transform to the exact point the eyes aim at (e.g.
        ///     the top of a painting).
        /// </summary>
        public Vector3 AimOffset
        {
            get => aimOffset;
            set => aimOffset = value;
        }

        private void OnEnable() => HandleEnable();

        private void OnDisable() => HandleDisable();

        private void OnValidate() => fullRelevanceDistance = Mathf.Clamp(fullRelevanceDistance, 0f, maxDistance);

        /// <summary>
        ///     Full enable path (registration). Internal seam so EditMode tests can drive the
        ///     lifecycle explicitly — Unity does not invoke <c>OnEnable</c> for plain
        ///     MonoBehaviours outside play mode.
        /// </summary>
        internal void HandleEnable()
        {
            if (_registered) return;
            Registry.Add(this);
            _registered = true;
        }

        /// <summary>Disable counterpart of <see cref="HandleEnable" /> (test seam).</summary>
        internal void HandleDisable()
        {
            if (!_registered) return;
            Registry.Remove(this);
            _registered = false;
        }

        internal bool TryGetCandidate(Transform characterRoot, out GazeTargetCandidate candidate)
        {
            candidate = default;

            Vector3 worldPoint = transform.TransformPoint(aimOffset);
            float distance = characterRoot != null
                ? Vector3.Distance(characterRoot.position, worldPoint)
                : 0f;
            if (distance > maxDistance) return false;

            float relevance = baseRelevance;
            if (maxDistance > fullRelevanceDistance && distance > fullRelevanceDistance)
                relevance *= 1f - Mathf.InverseLerp(fullRelevanceDistance, maxDistance, distance);

            if (relevance <= 0f) return false;

            candidate = new GazeTargetCandidate(
                GazeTargetKind.WorldObject,
                priority,
                relevance,
                transform,
                worldPoint,
                gameObject.name);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Draws the notice radius and the exact aim point while this target is selected.
        /// </summary>
        /// <remarks>
        ///     Editor-only by compilation, not just by Unity stripping the callback from players:
        ///     runtime assemblies in this SDK guard their gizmo and diagnostic code with
        ///     <c>UNITY_EDITOR</c> so nothing authoring-related reaches a shipped build. The colours
        ///     are the editor theme's brand green written out literally, because a runtime assembly
        ///     cannot reference the editor's token table.
        /// </remarks>
        private void OnDrawGizmosSelected()
        {
            Vector3 aimPoint = transform.TransformPoint(aimOffset);

            Gizmos.color = new Color(0.322f, 0.718f, 0.533f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, maxDistance);

            Gizmos.color = new Color(0.322f, 0.718f, 0.533f, 0.9f);
            Gizmos.DrawWireSphere(aimPoint, 0.06f);
        }
#endif
    }
}
