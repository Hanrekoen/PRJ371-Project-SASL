"""
Turns one frame's normalized hand landmarks (see hand_tracker.py) into a
(gesture_id, confidence) guess -- the same shape LessonManager already
expects from GestureResult, just produced on the laptop instead of in Unity.

PlaceholderClassifier plays the same role FakeGestureRecognizer plays on the
Unity side: a stand-in so the FULL PIPELINE (webcam -> laptop -> network ->
headset -> LessonManager) is testable tonight, without waiting on the ML
sub-team's trained model. It does NOT recognize actual ASL letters -- it only
tells an open hand from a closed fist. Hold your hand open toward the camera
to simulate "correct" for whatever sign is currently the target; make a fist
to simulate "incorrect". No hand in view reports nothing.

To swap in the real model once the ML team delivers one: write a class with
the same classify(landmarks) -> (gesture_id, confidence) signature (probably
loading a trained model in __init__ instead of taking a callback), and change
the `classifier = PlaceholderClassifier(...)` line in server.py to construct
your class instead. Nothing else in this program needs to change.
"""
import numpy as np

FINGER_TIPS = [4, 8, 12, 16, 20]
FINGER_PIPS = [3, 6, 10, 14, 18]  # the joint one below each fingertip


class GestureClassifier:
    def classify(self, landmarks):
        """landmarks: a (21, 3) wrist-relative, scale-normalized array from
        HandTracker.process(), or None if no hand was seen this frame.
        Returns (gesture_id: str, confidence: float in 0..1)."""
        raise NotImplementedError


class PlaceholderClassifier(GestureClassifier):
    def __init__(self, get_target_gesture_id):
        """get_target_gesture_id: a zero-arg callable returning the current
        target gestureId (or None), so an open hand can echo it back as a
        simulated correct match."""
        self._get_target = get_target_gesture_id

    def classify(self, landmarks):
        if landmarks is None:
            return "NONE", 0.0

        extended = 0
        for tip, pip in zip(FINGER_TIPS, FINGER_PIPS):
            # A finger counts as "extended" if its tip sits farther from the
            # wrist than its own PIP joint does -- simple and good enough for
            # a placeholder, not meant to hold up as real ASL classification.
            if np.linalg.norm(landmarks[tip][:2]) > np.linalg.norm(landmarks[pip][:2]):
                extended += 1

        if extended >= 4:
            target = self._get_target() or "NONE"
            return target, 0.95
        if extended <= 1:
            return "WRONG", 0.90
        return "NONE", 0.30
