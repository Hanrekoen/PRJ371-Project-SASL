"""
Webcam hand tracking via MediaPipe's HandLandmarker (the current "Tasks" API).
Turns raw camera frames into per-frame landmark data, normalized so a
classifier can compare hand shapes regardless of how far the hand is from
the camera or where in frame it is.

NOTE: this uses mp.tasks.vision.HandLandmarker, not the older mp.solutions.hands
API you'll see in a lot of older tutorials/StackOverflow answers. Google
removed mp.solutions (including its drawing_utils) from recent mediapipe
releases (0.10.30+) -- see https://github.com/google-ai-edge/mediapipe/issues/6192
-- and pinning back to an old version wasn't an option since mediapipe only
publishes wheels for this project's Python version from 0.10.30 onward. The
Tasks API is the maintained replacement and works the same either way from
this file's point of view; draw() below draws the skeleton by hand with cv2
since drawing_utils isn't available anymore either.

IMPORTANT for the ML team: this changes the gesture-recognition input spec.
The project's earlier plan (see the "ml-spec" item in the build tracker) was
26 joints/hand, wrist-relative, in metres -- that was the headset's own
OpenXR hand tracking. Since recognition now runs on a laptop webcam instead,
the input is MediaPipe Hands: 21 landmarks/hand (see LANDMARK_NAMES below),
wrist-relative, and scale-normalized (unitless -- divided by hand size in
the image, not a real-world metric distance) instead of metres. Any model
trained against the old 26-joint spec needs retraining or remapping against
this one -- please flag this to the ML sub-team.
"""
import os
import time
import urllib.request

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

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

# Bone pairs for the debug-preview skeleton drawing (replaces the old
# mp.solutions.drawing_utils, which no longer ships in this mediapipe version).
_HAND_CONNECTIONS = [
    (0, 1), (1, 2), (2, 3), (3, 4),          # thumb
    (0, 5), (5, 6), (6, 7), (7, 8),          # index
    (5, 9), (9, 10), (10, 11), (11, 12),     # middle
    (9, 13), (13, 14), (14, 15), (15, 16),   # ring
    (13, 17), (17, 18), (18, 19), (19, 20),  # pinky
    (0, 17),                                 # palm base
]

_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
_MODEL_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models")
_MODEL_PATH = os.path.join(_MODEL_DIR, "hand_landmarker.task")


def _ensure_model():
    """Downloads the ~10MB hand landmark model next to this file the first
    time it's needed. Safe to call every run -- does nothing once it exists."""
    if os.path.exists(_MODEL_PATH):
        return _MODEL_PATH
    os.makedirs(_MODEL_DIR, exist_ok=True)
    print(f"[HandTracker] Downloading hand landmark model (one-time, ~10MB) to {_MODEL_PATH} ...")
    try:
        urllib.request.urlretrieve(_MODEL_URL, _MODEL_PATH)
        print("[HandTracker] Model downloaded.")
    except Exception as e:
        raise SystemExit(
            f"[HandTracker] Could not download the hand landmark model automatically ({e}).\n"
            f"This usually means no internet access, or a firewall/campus network blocking "
            f"storage.googleapis.com. Download it manually from:\n  {_MODEL_URL}\n"
            f"and save it as:\n  {_MODEL_PATH}"
        )
    return _MODEL_PATH


class HandTracker:
    def __init__(self, max_hands=2, detection_confidence=0.6, tracking_confidence=0.6):
        model_path = _ensure_model()
        options = vision.HandLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=model_path),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=max_hands,
            min_hand_detection_confidence=detection_confidence,
            min_tracking_confidence=tracking_confidence,
        )
        self._landmarker = vision.HandLandmarker.create_from_options(options)
        self._start_time = time.monotonic()
        self._last_timestamp_ms = -1

    def _next_timestamp_ms(self):
        # detect_for_video() requires strictly increasing timestamps.
        ts = int((time.monotonic() - self._start_time) * 1000)
        if ts <= self._last_timestamp_ms:
            ts = self._last_timestamp_ms + 1
        self._last_timestamp_ms = ts
        return ts

    def process(self, bgr_frame):
        """Runs hand detection on one frame.

        Returns (result, landmarks_or_None):
          - result: the raw HandLandmarker result, for draw() / debugging.
          - landmarks: a (21, 3) numpy float32 array -- wrist-relative x/y/z,
            with x/y scale-normalized by hand size -- or None if no hand was
            found in this frame.
        """
        rgb = cv2.cvtColor(bgr_frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        result = self._landmarker.detect_for_video(mp_image, self._next_timestamp_ms())

        if not result.hand_landmarks:
            return result, None

        hand = result.hand_landmarks[0]  # first detected hand, 21 landmarks
        pts = np.array([[lm.x, lm.y, lm.z] for lm in hand], dtype=np.float32)

        wrist = pts[0].copy()
        rel = pts - wrist  # wrist-relative

        # Scale-normalize by wrist-to-middle-finger-MCP distance (a stable
        # reference length across hand poses) so hand size / distance from
        # the camera doesn't skew downstream comparisons.
        scale = float(np.linalg.norm(rel[9][:2]))
        if scale > 1e-6:
            rel[:, :2] /= scale

        return result, rel

    def draw(self, bgr_frame, result):
        """Draws the tracked skeleton onto bgr_frame in place, for the debug preview window."""
        if not result.hand_landmarks:
            return
        h, w = bgr_frame.shape[:2]
        for hand_landmarks in result.hand_landmarks:
            pts_px = [(int(lm.x * w), int(lm.y * h)) for lm in hand_landmarks]
            for a, b in _HAND_CONNECTIONS:
                cv2.line(bgr_frame, pts_px[a], pts_px[b], (0, 200, 0), 2)
            for x, y in pts_px:
                cv2.circle(bgr_frame, (x, y), 4, (0, 255, 255), -1)

    def close(self):
        self._landmarker.close()
