using Convai.Modules.BodyAnimation.Core.Diagnostics;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     The single conversation-anchor resolution for a character: replaces three
    ///     independent <c>Camera.main</c> reads (social spacing, proximity expressiveness,
    ///     ambient suppression), each with its own cached transform and its own log-once flag.
    ///     Resolution ladder, mirroring the Gaze module's own player-anchor provider (reproduced
    ///     here rather than referenced — BodyAnimation never depends on another module): an
    ///     explicit override → <c>Camera.main</c> → the first enabled Game-view camera
    ///     (render-texture and utility cameras are skipped).
    /// </summary>
    internal sealed class ConversationAnchorResolver
    {
        private Transform _explicitAnchor;
        private Transform _cachedCameraTransform;
        private bool _degradationLogged;

        private static Camera[] _cameraScratch = System.Array.Empty<Camera>();

        /// <summary>Sets an explicit anchor override — the VR/split-screen/multiplayer answer.</summary>
        internal void SetExplicitAnchor(Transform anchor) => _explicitAnchor = anchor;

        /// <summary>Clears the explicit override; resolution falls back to the camera ladder.</summary>
        internal void Clear() => _explicitAnchor = null;

        /// <summary>
        ///     Resolves the current anchor position. Re-resolves the camera fallback only when
        ///     the cached transform is null/destroyed, so the steady-state path is a single
        ///     transform read with no allocation. Returns <c>false</c> (logging the degradation
        ///     once, through <paramref name="trace" />) when nothing is resolvable. The trace is
        ///     handed in per call rather than stored, since the owning controller replaces its
        ///     <c>AnimTrace</c> instance every rebuild while this resolver survives across them.
        /// </summary>
        internal bool TryResolve(AnimTrace trace, out Vector3 position)
        {
            if (_explicitAnchor != null)
            {
                position = _explicitAnchor.position;
                return true;
            }

            Camera main = Camera.main;
            if (main != null) _cachedCameraTransform = main.transform;

            if (_cachedCameraTransform == null || !IsUsable(_cachedCameraTransform))
                _cachedCameraTransform = ResolveFirstEnabledCamera();

            if (_cachedCameraTransform == null)
            {
                position = default;
                if (!_degradationLogged)
                {
                    _degradationLogged = true;
                    trace?.Detail(
                        "Conversation anchor: no explicit override, Camera.main unset, and no " +
                        "enabled Game-view camera — anchor-dependent features (social spacing, " +
                        "proximity expressiveness, ambient suppression) stay neutral/inert.");
                }
                return false;
            }

            position = _cachedCameraTransform.position;
            return true;
        }

        /// <summary>
        ///     Clears the cache and re-arms the log-once latch: a genuine post-rebuild
        ///     anchor regression must be able to log again.
        /// </summary>
        internal void Reset()
        {
            _cachedCameraTransform = null;
            _degradationLogged = false;
        }

        private static bool IsUsable(Transform candidate)
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
        ///     utility cameras (previews, reflections) must never become the conversation anchor.
        ///     Camera.main and an explicit anchor are deliberate designations and are not filtered.
        /// </summary>
        private static bool IsEligibleViewCamera(Camera camera) =>
            camera != null &&
            camera.isActiveAndEnabled &&
            camera.cameraType == CameraType.Game &&
            camera.targetTexture == null;
    }
}
