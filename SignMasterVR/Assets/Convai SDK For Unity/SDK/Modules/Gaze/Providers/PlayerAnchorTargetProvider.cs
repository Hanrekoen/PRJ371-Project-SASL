using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Components;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     The out-of-the-box "look at the player" provider. Resolves, in order: an explicit
    ///     anchor transform, <c>Camera.main</c>, then the first enabled Game-view camera
    ///     (render-texture and utility cameras are skipped) — which makes it correct on
    ///     desktop and in XR (the XR Origin camera carries the MainCamera tag and sits
    ///     exactly at the user's eyes).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A camera transform IS the player's eye position, so no head-height offset is
    ///         applied to camera targets; explicit non-camera anchors (e.g. an avatar root)
    ///         are lifted by <see cref="eyeLineOffset" /> so gaze lands on the implied
    ///         eye-line rather than the feet.
    ///     </para>
    ///     <para>
    ///         The <c>ConvaiGazeController</c> auto-provisions one of these at runtime when
    ///         the character has no other provider, and pushes the profile's distance tuning
    ///         through <see cref="Configure" />. Add the component manually to override the
    ///         anchor per character (split-screen, multiplayer, cutscene rigs).
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Player Anchor")]
    [DisallowMultipleComponent]
    public sealed class PlayerAnchorTargetProvider : MonoBehaviour, IGazeTargetProvider
    {
        // No [Header] on serialized fields: the Convai inspector groups these into its own
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Static priority tier compared across providers (higher wins).")]
        private int priority = 10;

        [SerializeField]
        [Tooltip("Explicit player anchor. When empty, Camera.main (then any enabled camera) is used.")]
        private Transform explicitAnchor;

        [SerializeField, Min(0f)]
        [Tooltip("Vertical lift applied to explicit NON-camera anchors so gaze lands on the eye line.")]
        private float eyeLineOffset = 1.6f;

        // Shown, not hidden: an anchor you place yourself is authored right here. The Gaze
        // component owns these for the anchor it provisions itself, and overwrites them on a
        // hand-placed one only when it carries a non-default aim of its own.
        [SerializeField]
        [Tooltip("Where on the anchor gaze aims. Auto picks the camera when the anchor is a camera and the object's own origin otherwise.")]
        private GazeAnchorAimMode aimMode = GazeAnchorAimMode.Auto;

        [SerializeField]
        [Tooltip("Anchor-local aim offset used by Local Offset mode.")]
        private Vector3 localAimOffset;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) beyond which the player is no longer a gaze candidate.")]
        private float maxDistance = 8f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) below which relevance is at its maximum.")]
        private float fullRelevanceDistance = 4f;

        [SerializeField]
        [Tooltip("Require an unobstructed line to the player: when a wall interposes, relevance " +
                 "decays and gaze plays the natural 'lost you' beat; when the player reappears the " +
                 "normal acquisition saccade fires. Off by default.")]
        private bool checkLineOfSight;

        [SerializeField]
        [Tooltip("Layers treated as vision obstructions for the line-of-sight test.")]
        private LayerMask obstructionMask = Physics.DefaultRaycastLayers;

        [SerializeField, Min(0.02f)]
        [Tooltip("Seconds between line-of-sight raycasts (throttled — at most 1/interval per second).")]
        private float lineOfSightInterval = 0.1f;

        [SerializeField, Min(0f)]
        [Tooltip("Eye-line height (meters) above the character root the vision ray starts from.")]
        private float lineOfSightOriginHeight = 1.6f;

        private Transform _cachedCameraTransform;
        private static Camera[] _cameraScratch = System.Array.Empty<Camera>();

        /// <summary>Smoothed visibility below which the player stops being a candidate at all.</summary>
        private const float VisibilityFloor = 0.05f;

        private readonly RaycastHit[] _lineOfSightHits = new RaycastHit[8];
        private float _visibility = 1f;
        private float _lineOfSightTimer;
        private bool _lastOccluded;

        /// <summary>
        ///     Smoothed line-of-sight visibility of the player, 0 (fully occluded) to 1
        ///     (clear). Always 1 while the line-of-sight check is disabled.
        /// </summary>
        public float Visibility => checkLineOfSight ? _visibility : 1f;

        /// <summary>Whether the last line-of-sight raycast found an obstruction (trace seam).</summary>
        internal bool LineOfSightOccluded => checkLineOfSight && _lastOccluded;

        /// <summary>Explicit anchor override (API equivalent of the inspector field).</summary>
        public Transform ExplicitAnchor
        {
            get => explicitAnchor;
            set => explicitAnchor = value;
        }

        /// <summary>How this provider derives its focus point from the resolved anchor.</summary>
        public GazeAnchorAimMode AimMode
        {
            get => aimMode;
            set => aimMode = value;
        }

        /// <summary>Anchor-local aim offset used when <see cref="AimMode" /> is Local Offset.</summary>
        public Vector3 LocalAimOffset
        {
            get => localAimOffset;
            set => localAimOffset = value;
        }

        /// <summary>Applies profile-driven distance and line-of-sight tuning (used by the auto-provisioned instance).</summary>
        public void Configure(
            float configuredMaxDistance,
            float configuredFullRelevanceDistance,
            bool configuredCheckLineOfSight,
            int configuredObstructionMask)
        {
            maxDistance = Mathf.Max(0f, configuredMaxDistance);
            fullRelevanceDistance = Mathf.Clamp(configuredFullRelevanceDistance, 0f, maxDistance);
            checkLineOfSight = configuredCheckLineOfSight;
            obstructionMask = configuredObstructionMask;
        }

        /// <inheritdoc />
        public bool TryGetCandidate(Transform characterRoot, out GazeTargetCandidate candidate)
        {
            candidate = default;

            Transform anchor = ResolveAnchor(out bool anchorIsCamera);
            if (anchor == null) return false;

            Vector3 worldPoint = ResolveWorldPoint(anchor, anchorIsCamera);

            float distance = characterRoot != null
                ? Vector3.Distance(characterRoot.position, worldPoint)
                : 0f;

            float relevance = ComputeRelevance(distance);
            if (relevance <= 0f) return false;

            if (checkLineOfSight)
            {
                float visibility = UpdateVisibility(characterRoot, anchor, worldPoint);
                if (visibility <= VisibilityFloor) return false;
                relevance *= visibility;
            }

            candidate = new GazeTargetCandidate(
                GazeTargetKind.Player,
                priority,
                relevance,
                anchor,
                worldPoint,
                explicitAnchor != null ? explicitAnchor.name : "Player");
            return true;
        }

        /// <summary>
        ///     Resolves a contractual player candidate without relevance, range, or line-of-sight
        ///     rejection. Focus modes use this path so environmental sensing cannot silently
        ///     hand ownership to an unrelated target.
        /// </summary>
        internal bool TryGetFocusCandidate(out GazeTargetCandidate candidate)
        {
            candidate = default;
            Transform anchor = ResolveAnchor(out bool anchorIsCamera);
            if (anchor == null) return false;

            candidate = new GazeTargetCandidate(
                GazeTargetKind.Player, int.MaxValue, 1f, anchor,
                ResolveWorldPoint(anchor, anchorIsCamera),
                explicitAnchor != null ? explicitAnchor.name : "Player");
            return true;
        }

        internal bool TryResolveFocusPoint(out Vector3 worldPoint)
        {
            Transform anchor = ResolveAnchor(out bool anchorIsCamera);
            if (anchor == null)
            {
                worldPoint = default;
                return false;
            }

            worldPoint = ResolveWorldPoint(anchor, anchorIsCamera);
            return true;
        }

        private Vector3 ResolveWorldPoint(Transform anchor, bool anchorIsCamera) => aimMode switch
        {
            GazeAnchorAimMode.ExactTransform => anchor.position,
            GazeAnchorAimMode.LocalOffset => anchor.TransformPoint(localAimOffset),
            _ => anchor.position + (!anchorIsCamera && eyeLineOffset > 0f
                ? Vector3.up * eyeLineOffset
                : Vector3.zero)
        };

        private Transform ResolveAnchor(out bool anchorIsCamera)
        {
            if (explicitAnchor != null)
            {
                anchorIsCamera = explicitAnchor.TryGetComponent<Camera>(out _);
                return explicitAnchor;
            }

            anchorIsCamera = true;

            Camera main = Camera.main;
            if (main != null)
            {
                _cachedCameraTransform = main.transform;
                return _cachedCameraTransform;
            }

            if (_cachedCameraTransform != null && IsUsableCamera(_cachedCameraTransform))
                return _cachedCameraTransform;

            _cachedCameraTransform = ResolveFirstEnabledCamera();
            return _cachedCameraTransform;
        }

        private static bool IsUsableCamera(Transform candidate)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) return false;
            return candidate.TryGetComponent(out Camera camera) && IsEligibleViewCamera(camera);
        }

        private static Transform ResolveFirstEnabledCamera()
        {
            int count = Camera.allCamerasCount;
            if (count <= 0) return null;

            if (_cameraScratch.Length < count)
                _cameraScratch = new Camera[Mathf.NextPowerOfTwo(count)];

            Camera.GetAllCameras(_cameraScratch);
            Transform result = null;
            for (int i = 0; i < count; i++)
            {
                Camera candidate = _cameraScratch[i];
                if (result == null && IsEligibleViewCamera(candidate))
                    result = candidate.transform;
            }

            System.Array.Clear(_cameraScratch, 0, count);
            return result;
        }

        /// <summary>
        ///     Whether a camera is eligible to be picked by the automatic fallback resolve: an
        ///     active, enabled Game-view camera rendering to the screen. Render-texture and
        ///     utility cameras (previews, reflections) must never become the gaze target.
        ///     Camera.main and an explicit anchor are deliberate user designations and are not
        ///     filtered.
        /// </summary>
        internal static bool IsEligibleViewCamera(Camera camera) =>
            camera != null &&
            camera.isActiveAndEnabled &&
            camera.cameraType == CameraType.Game &&
            camera.targetTexture == null;

        /// <summary>
        ///     Refreshes the throttled occlusion raycast (at most 1/<see cref="lineOfSightInterval" />
        ///     per second) and returns the smoothed 0..1 visibility for this frame. Hits under
        ///     the character or the anchor's own hierarchy are excluded so neither occludes the
        ///     player. With no physics scene / no colliders the ray simply misses → fully visible.
        /// </summary>
        private float UpdateVisibility(Transform characterRoot, Transform anchor, Vector3 targetPoint)
        {
            float deltaTime = Time.deltaTime;

            _lineOfSightTimer -= deltaTime;
            if (_lineOfSightTimer <= 0f)
            {
                _lineOfSightTimer = Mathf.Max(0.02f, lineOfSightInterval);

                Transform selfRoot = characterRoot != null ? characterRoot : transform;
                Vector3 origin = selfRoot.position + Vector3.up * lineOfSightOriginHeight;
                Transform anchorRoot = anchor != null ? anchor.root : null;

                _lastOccluded = GazeLineOfSight.Occluded(
                    origin, targetPoint, obstructionMask, selfRoot, anchorRoot, _lineOfSightHits);
            }

            _visibility = GazeLineOfSight.StepVisibility(_visibility, _lastOccluded ? 0f : 1f, deltaTime);
            return _visibility;
        }

        private float ComputeRelevance(float distance)
        {
            if (maxDistance <= 0f) return 1f;
            if (distance <= fullRelevanceDistance) return 1f;
            if (distance >= maxDistance) return 0f;

            return 1f - Mathf.InverseLerp(fullRelevanceDistance, maxDistance, distance);
        }
    }
}
