using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.SceneMetadata;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Marks an authored Convai world object (a <see cref="ConvaiObjectMetadata" />) as
    ///     an ambient gaze candidate: nearby characters glance at it while idle, and the
    ///     dynamic-context bridge reports it to the backend when it wins attention.
    /// </summary>
    /// <remarks>
    ///     Lives on the world object, not the character — enabled instances register into a
    ///     scene-wide list every <c>ConvaiGazeController</c> polls with per-character
    ///     distance relevance. The default priority sits below the player anchor, so the
    ///     player always wins while a conversation is engaged.
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/World Object Target")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConvaiObjectMetadata))]
    public sealed class WorldObjectGazeTargetProvider : MonoBehaviour
    {
        private static readonly List<WorldObjectGazeTargetProvider> Registry = new(16);

        /// <summary>Enabled providers in the scene (polled by gaze controllers).</summary>
        internal static IReadOnlyList<WorldObjectGazeTargetProvider> ActiveProviders => Registry;

        [SerializeField]
        [Tooltip("Priority tier. Keep below the player anchor (10) so the player wins during conversation.")]
        private int priority = 5;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Base relevance when the object is inside the full-relevance distance.")]
        private float baseRelevance = 0.75f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) beyond which the object is not a gaze candidate.")]
        private float maxDistance = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) below which relevance is at its maximum.")]
        private float fullRelevanceDistance = 3f;

        private ConvaiObjectMetadata _metadata;

        private void Awake() => _metadata = GetComponent<ConvaiObjectMetadata>();

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable() => Registry.Remove(this);

        internal bool TryGetCandidate(Transform characterRoot, out GazeTargetCandidate candidate)
        {
            candidate = default;
            if (_metadata == null || !_metadata.IsValid) return false;

            Vector3 worldPoint = transform.position;
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
                _metadata.ObjectName);
            return true;
        }
    }
}
