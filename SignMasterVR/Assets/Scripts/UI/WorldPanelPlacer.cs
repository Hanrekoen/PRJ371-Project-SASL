using Unity.XR.CoreUtils;
using UnityEngine;

namespace SignMasterVR.UI
{
    /// <summary>
    /// Puts a world-space UI panel where the learner is actually looking.
    ///
    /// Why this exists rather than a hard-coded transform: a panel pinned to a
    /// fixed world position is a guess about two things we do not control. The
    /// first is the XR Origin's tracking origin mode -- in Floor mode the
    /// camera sits at the learner's real standing height, in Device mode it
    /// starts at y = 0, so the same world Y is either eye level or the floor.
    /// The first build of the VR menu put the panel at y = 0 and it landed at
    /// the learner's feet; the scene rendered perfectly and the menu was
    /// simply out of view below them. The second is which way they happen to
    /// be facing when the app starts, which the headset decides, not us.
    ///
    /// Placing relative to the live camera pose removes both guesses.
    ///
    /// The head pose is usually not valid on the very first frame, so this
    /// keeps re-placing for <see cref="settleSeconds"/> and then stops --
    /// otherwise the panel would chase the learner's head forever, which is
    /// nauseating. Call <see cref="Place"/> again to recentre on demand.
    /// </summary>
    [AddComponentMenu("SignMasterVR/World Panel Placer")]
    public class WorldPanelPlacer : MonoBehaviour
    {
        [Tooltip("Metres in front of the learner. 1.6 is about arm's length plus a bit -- close enough to read, far enough not to strain the eyes.")]
        public float distance = 1.6f;

        [Tooltip("Metres above (+) or below (-) eye level. Slightly below reads more comfortably than dead level, because people look very slightly down at rest.")]
        public float verticalOffset = -0.15f;

        [Tooltip("Keep re-placing for this many seconds after enable, to catch the frames before the headset reports a real head pose. Then freeze, so the panel stays put instead of following your head.")]
        public float settleSeconds = 1f;

        private Camera _camera;
        private float _elapsed;
        private bool _placedAtLeastOnce;

        private void OnEnable()
        {
            _elapsed = 0f;
            _placedAtLeastOnce = false;
        }

        private void LateUpdate()
        {
            // LateUpdate, not Update: the tracked-pose driver writes the camera
            // transform in Update, so reading it here gets this frame's real
            // pose instead of last frame's.
            if (_placedAtLeastOnce && _elapsed > settleSeconds) return;
            _elapsed += Time.unscaledDeltaTime;
            Place();
        }

        /// <summary>Centre this panel in front of the learner, at eye height, facing them.</summary>
        public void Place()
        {
            Camera cam = ResolveCamera();
            if (cam == null) return;

            // Flatten the forward vector so the panel never tilts, however the
            // learner's head is pitched when the scene loads.
            Vector3 flatForward = cam.transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward; // straight up or down
            flatForward.Normalize();

            transform.position = cam.transform.position + flatForward * distance + Vector3.up * verticalOffset;

            // A world-space Canvas is readable from its -Z side, so the panel's
            // own forward must point AWAY from the learner, i.e. along
            // flatForward. Getting this backwards shows mirrored text.
            transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

            _placedAtLeastOnce = true;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null) return _camera;

            // Prefer the XR Origin's own camera over Camera.main: the MainCamera
            // tag is not reliably set on every version of the XR rig prefab.
            XROrigin origin = FindFirstObjectByType<XROrigin>();
            _camera = origin != null && origin.Camera != null ? origin.Camera : Camera.main;
            return _camera;
        }
    }
}
