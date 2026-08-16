using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.Gaze.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.Gaze.Executors
{
    /// <summary>Makes the character inspect several distinct points across the surrounding environment.</summary>
    [AddComponentMenu("Convai/Actions/Scan Environment")]
    [ConvaiActionArchetype(
        "Scan Environment",
        ActionName = "Scan Environment",
        Description = "Look across the surrounding environment and pause on several distinct points. " +
                      "Use this when the character should visibly inspect the area rather than focus " +
                      "on one named target.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        RequiredPeerHint = "ConvaiGazeController",
        TimeoutSeconds = 15f,
        FailurePolicyOverride = ConvaiActionFailurePolicyOverride.ContinueBatch,
        Family = "Attention")]
    public sealed class ConvaiScanEnvironmentActionExecutor : ConvaiCharacterActionExecutor<ConvaiGazeController>
    {
        private const int ScanPriority = 10;
        private const int ColliderCapacity = 64;
        private const float MinimumCandidateDistance = 1.2f;

        [SerializeField, Min(0.5f)]
        [Tooltip("Total scan duration. The action can override this with its duration parameter.")]
        [ConvaiInspectorSection("Timing", 0)]
        private float _durationSeconds = 3.5f;

        [SerializeField, Range(2, 8)]
        [Tooltip("Number of distinct places where the gaze pauses during a scan.")]
        [ConvaiInspectorSection("Coverage", 10)]
        private int _stopCount = 4;

        [SerializeField, Range(20f, 320f)]
        [Tooltip("Horizontal field covered by the scan, centred on the character's forward direction.")]
        [ConvaiInspectorSection("Coverage", 11)]
        private float _arcDegrees = 150f;

        [SerializeField]
        [Tooltip("Allow wide scan points to turn the character's body as well as the head and eyes.")]
        [ConvaiInspectorSection("Coverage", 12)]
        private bool _allowBodyTurn;

        [SerializeField, Min(0f)]
        [Tooltip("Radius used to find scene colliders worth inspecting. Zero uses generated scan points only.")]
        [ConvaiInspectorSection("Targets", 20)]
        private float _searchRadius = 7f;

        [SerializeField]
        [Tooltip("Layers containing objects that may be selected as scan points.")]
        [ConvaiInspectorSection("Targets", 21)]
        private LayerMask _targetLayers = ~0;

        [SerializeField, Min(0.5f)]
        [Tooltip("Distance of generated scan points when no suitable scene object is available.")]
        [ConvaiInspectorSection("Targets", 22)]
        private float _fallbackDistance = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("Height of generated scan points above the character origin.")]
        [ConvaiInspectorSection("Targets", 23)]
        private float _fallbackHeight = 1.5f;

        private readonly Collider[] _colliders = new Collider[ColliderCapacity];
        private GazeHandle _activeLook;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiGazeController gaze,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (!UnityEngine.Application.isPlaying)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "Environment scanning is available in Play Mode because it drives the live gaze rig.");
            }

            Transform character = CharacterTransform;
            float duration = Mathf.Max(0.5f, GetOverride(invocation, "duration", _durationSeconds));
            IReadOnlyList<Vector3> points = ResolveScanPoints(character);
            float secondsPerPoint = duration / points.Count;

            try
            {
                for (int i = 0; i < points.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _activeLook = gaze.GazeAt(points[i], new GazeOptions
                    {
                        Priority = ScanPriority,
                        HoldSeconds = secondsPerPoint,
                        Engagement = 1f,
                        AllowBodyTurn = _allowBodyTurn
                    });

                    if (_activeLook == null)
                    {
                        return ConvaiActionExecutionResult.Failed(
                            "The gaze system could not accept the scan request.",
                            ConvaiActionFailureReason.InvalidState);
                    }

                    using (cancellationToken.Register(_activeLook.Release))
                    {
                        await ConvaiActionAsyncUtility.WaitSecondsAsync(secondsPerPoint, cancellationToken);
                    }

                    ReleaseActiveLook();
                }

                return ConvaiActionExecutionResult.Succeeded($"Scanned {points.Count} points in the environment.");
            }
            finally
            {
                ReleaseActiveLook();
            }
        }

        private IReadOnlyList<Vector3> ResolveScanPoints(Transform character)
        {
            int wanted = Mathf.Clamp(_stopCount, 2, 8);
            var candidates = new List<Vector3>(ColliderCapacity);
            if (_searchRadius > 0f)
            {
                int hitCount = Physics.OverlapSphereNonAlloc(
                    character.position,
                    _searchRadius,
                    _colliders,
                    _targetLayers,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    Collider hit = _colliders[i];
                    _colliders[i] = null;
                    if (hit == null || hit.transform.IsChildOf(character))
                        continue;

                    Vector3 point = hit.bounds.center;
                    Vector3 direction = point - character.position;
                    direction.y = 0f;
                    if (direction.magnitude < MinimumCandidateDistance)
                        continue;

                    float angle = Vector3.SignedAngle(character.forward, direction, Vector3.up);
                    if (Mathf.Abs(angle) <= _arcDegrees * 0.5f)
                        candidates.Add(point);
                }
            }

            var result = new List<Vector3>(wanted);
            for (int i = 0; i < wanted; i++)
            {
                float t = wanted == 1 ? 0.5f : i / (float)(wanted - 1);
                float desiredAngle = Mathf.Lerp(-_arcDegrees * 0.5f, _arcDegrees * 0.5f, t);
                int bestIndex = FindClosestCandidate(candidates, character, desiredAngle);
                if (bestIndex >= 0)
                {
                    result.Add(candidates[bestIndex]);
                    candidates.RemoveAt(bestIndex);
                    continue;
                }

                Vector3 direction = Quaternion.AngleAxis(desiredAngle, Vector3.up) * character.forward;
                Vector3 point = character.position + direction.normalized * _fallbackDistance;
                point.y = character.position.y + _fallbackHeight;
                result.Add(point);
            }

            return result;
        }

        private static int FindClosestCandidate(List<Vector3> candidates, Transform character, float desiredAngle)
        {
            int bestIndex = -1;
            float bestDifference = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 direction = candidates[i] - character.position;
                direction.y = 0f;
                float angle = Vector3.SignedAngle(character.forward, direction, Vector3.up);
                float difference = Mathf.Abs(Mathf.DeltaAngle(angle, desiredAngle));
                if (difference >= bestDifference)
                    continue;
                bestDifference = difference;
                bestIndex = i;
            }
            return bestIndex;
        }

        private void ReleaseActiveLook()
        {
            _activeLook?.Release();
            _activeLook = null;
        }

        private void OnDisable() => ReleaseActiveLook();

        private void OnValidate()
        {
            _durationSeconds = Mathf.Max(0.5f, _durationSeconds);
            _stopCount = Mathf.Clamp(_stopCount, 2, 8);
            _arcDegrees = Mathf.Clamp(_arcDegrees, 20f, 320f);
            _searchRadius = Mathf.Max(0f, _searchRadius);
            _fallbackDistance = Mathf.Max(0.5f, _fallbackDistance);
            _fallbackHeight = Mathf.Max(0f, _fallbackHeight);
        }
    }
}
