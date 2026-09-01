"""
Webcam hand tracking via MediaPipe Hands. Turns raw camera frames into
per-frame landmark data, normalized so a classifier can compare hand shapes
regardless of how far the hand is from the camera or where in frame it is.

IMPORTANT for the ML team: this changes the gesture-recognition input spec.
The project's earlier plan (see the "ml-spec" item in the build tracker) was
26 joints/hand, wrist-relative, in metres -- that was the headset's own
OpenXR hand tracking. Since recognition now runs on a laptop webcam instead
of the headset, the input is MediaPipe Hands: 21 landmarks/hand (see
LANDMARK_NAMES below), wrist-relative, and scale-normalized (unitless --
divided by hand size in the image, not a real-world metric distance) instead
of metres. Any model trained against the old 26-joint spec needs retraining
or remapping against this one -- please flag this to the ML sub-team.
"""
import cv2
import mediapipe as mp
import numpy as np

# MediaPipe Hands landmark order (21 points/hand), for reference when writing
# a real classifier against the (21, 3) array HandTracker.process() returns.
LANDMARK_NAMES = [
    "WRIST",
    "THUMB_CMC", "THUMB_MCP", "THUMB_IP", "THUMB_TIP",
    "INDEX_MCP", "INDEX_PIP", "INDEX_DIP", "INDEX_TIP",
    "MIDDLE_MCP", "MIDDLE_PIP", "MIDDLE_DIP", "MIDDLE_TIP",
    "RING_MCP", "RING_PIP", "RING_DIP", "RING_TIP",
    "PINKY_MCP", "PINKY_PIP", "PINKY_DIP", "PINKY_TIP",
]


class HandTracker:
    def __init__(self, max_hands=1, detection_confidence=0.6, tracking_confidence=0.6):
        self._mp_hands = mp.solutions.hands
        self._hands = self._mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=max_hands,
            min_detection_confidence=detection_confidence,
            min_tracking_confidence=tracking_confidence,
        )
        self.drawing = mp.solutions.drawing_utils
        self.drawing_styles = mp.solutions.drawing_styles

    def process(self, bgr_frame):
        """Runs MediaPipe on one frame.

        Returns (mediapipe_results, landmarks_or_None):
          - mediapipe_results: raw MediaPipe output, for draw() / debugging.
          - landmarks: a (21, 3) numpy float32 array -- wrist-relative x/y/z,
            with x/y scale-normalized by hand size -- or None if no hand was
            found in this frame.
        """
        rgb = cv2.cvtColor(bgr_frame, cv2.COLOR_BGR2RGB)
        rgb.flags.writeable = False
        results = self._hands.process(rgb)

        if not results.multi_hand_landmarks:
            return results, None

        hand = results.multi_hand_landmarks[0]
        pts = np.array([[lm.x, lm.y, lm.z] for lm in hand.landmark], dtype=np.float32)

        wrist = pts[0].copy()
        rel = pts - wrist  # wrist-relative

        # Scale-normalize by wrist-to-middle-finger-MCP distance (a stable
        # reference length across hand poses) so hand size / distance from
        # the camera doesn't skew downstream comparisons.
        scale = float(np.linalg.norm(rel[9][:2]))
        if scale > 1e-6:
            rel[:, :2] /= scale

        return results, rel

    def draw(self, bgr_frame, results):
        """Draws the tracked skeleton onto bgr_frame in place, for the debug preview window."""
        if results.multi_hand_landmarks:
            for hand_landmarks in results.multi_hand_landmarks:
                self.drawing.draw_landmarks(
                    bgr_frame, hand_landmarks, self._mp_hands.HAND_CONNECTIONS,
                    self.drawing_styles.get_default_hand_landmarks_style(),
                    self.drawing_styles.get_default_hand_connections_style(),
                )

    def close(self):
        self._hands.close()
