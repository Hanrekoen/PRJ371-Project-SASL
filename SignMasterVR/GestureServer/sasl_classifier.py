"""
The trained model, wearing the GestureClassifier interface server.py expects.

This is the class classifier.py's docstring said to write once the ML side had
a real model: same `classify(...) -> (gesture_id, confidence)` contract as
PlaceholderClassifier, so server.py, protocol.py and Unity's LessonManager are
unaffected. Swapping it in is one line in server.py.

Two things differ from the placeholder, both deliberate:

1. It needs the RAW HandLandmarker result, not just the (21, 3) `rel` array.
   The signs in the dataset are dynamic -- Hello and Bye are both waves -- so
   where the hand travels is most of the signal, and `rel` has already had the
   wrist subtracted out. `classify()` therefore takes an optional second
   argument, and server.py passes `results` alongside `landmarks`. The
   placeholder ignores it, so both classifiers still satisfy one interface.

2. It answers from a ~3 second window, not a single frame. A frame cannot tell
   a wave from a hand held still, so the classifier buffers frames internally
   and reports a sign only once several overlapping windows agree. Between
   signs it returns "NONE", which server.py already treats as "say nothing".

The pass/fail decision still belongs to Unity: this reports the best guess and
a calibrated confidence, and LessonManager compares that against the target and
GestureData.requiredConfidence exactly as it does for FakeGestureRecognizer.
What this class adds is the ability to say nothing at all when an attempt isn't
recognisable -- see ML_MODEL.md on the confidence gate.
"""
from __future__ import annotations

import glob
import os
import re
import time

from classifier import GestureClassifier
from gesture_classifier import GestureValidator

DEFAULT_MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "models", "sasl_gesture_model.joblib")
UNITY_GESTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                 "..", "Assets", "Data", "Gestures")


def unity_gesture_ids() -> set:
    """gestureIds Unity actually has GestureData assets for. Best effort --
    returns an empty set if the Assets folder isn't where we expect."""
    found = set()
    for path in glob.glob(os.path.join(UNITY_GESTURE_DIR, "*.asset")):
        try:
            with open(path, encoding="utf-8", errors="ignore") as fh:
                m = re.search(r"^\s*gestureId:\s*(\S+)\s*$", fh.read(), re.M)
            if m:
                found.add(m.group(1))
        except OSError:
            continue
    return found


class SASLGestureClassifier(GestureClassifier):
    def __init__(self, model_path: str = DEFAULT_MODEL,
                 hold_seconds: float = 1.5,
                 votes: int = 2,
                 evaluate_every: float = 0.05,
                 min_confidence: float | None = None,
                 gesture_id_map: dict | None = None,
                 verbose: bool = True):
        """
        votes          consecutive agreeing windows before a sign is reported.
        evaluate_every how often (seconds) to score the rolling window.
                       These two were picked by sweeping both against recorded
                       clips rather than guessed: (2, 0.05) gave 43/48 correct
                       with 0 wrong, where (3, 0.15) gave 38/48 with 10 missed.
                       Scoring more often costs a few ms per frame and buys
                       detections; demanding more votes mostly just costs.
        hold_seconds   how long a recognised sign keeps being reported, so the
                       headset sees it even though the underlying event is
                       momentary. server.py re-sends every 2s, so this should
                       stay a little under that.
        gesture_id_map maps model labels to Unity gestureIds where they differ,
                       e.g. {"Hello": "HELLO"}. Unmapped labels pass through.
        """
        self._validator = GestureValidator(model_path, votes_needed=votes,
                                           evaluate_every=evaluate_every,
                                           min_confidence=min_confidence)
        self._map = dict(gesture_id_map or {})
        self._hold_seconds = hold_seconds
        self._held = None          # (gesture_id, confidence, expires_at)

        if verbose:
            meta = self._validator.model.metadata
            acc = meta.get("cv_accuracy", meta.get("loco_accuracy", 0.0))
            print(f"[SASLGestureClassifier] {os.path.basename(model_path)} -- "
                  f"{meta.get('selected_model')}, {acc:.1%} "
                  f"({meta.get('cv_scheme', 'cross-validated')})")
            print(f"[SASLGestureClassifier] Signs: {', '.join(self._validator.labels)}")
            self._warn_about_unity_ids()

    def _warn_about_unity_ids(self):
        known = unity_gesture_ids()
        if not known:
            return
        missing = [self.to_gesture_id(l) for l in self._validator.labels
                   if self.to_gesture_id(l) not in known]
        if missing:
            print(f"[SASLGestureClassifier] WARNING: no GestureData asset in Unity for "
                  f"{', '.join(missing)}. LessonManager can never match these as a "
                  f"target until someone creates Assets/Data/Gestures/Gesture_<id>.asset "
                  f"(Unity currently has {len(known)} gestures: A-Z fingerspelling). "
                  f"Alternatively map the model's labels onto existing ids with "
                  f"gesture_id_map=.")

    def to_gesture_id(self, label: str) -> str:
        return self._map.get(label, label)

    @property
    def labels(self):
        return self._validator.labels

    @property
    def status(self) -> dict:
        """Live read-out for the preview window: the current best guess and,
        when nothing is being accepted, the reason why."""
        return self._validator.status

    def reset(self):
        self._validator.reset()
        self._held = None

    def classify(self, landmarks, result=None, t=None):
        """landmarks is accepted for interface compatibility and unused --
        this model reads the raw `result` instead, for the reasons in the
        module docstring. Returns (gesture_id, confidence).

        `t` overrides the clock, so a recorded clip can be replayed through the
        full temporal path faster than real time (see test_pipeline.py). Live,
        leave it alone."""
        now = time.monotonic() if t is None else t

        if result is None:
            # Called the old single-argument way. Fail loudly rather than
            # silently reporting NONE forever, which would look like a camera
            # or lighting problem rather than a wiring mistake.
            raise TypeError(
                "SASLGestureClassifier.classify() needs the raw HandLandmarker "
                "result as its second argument: classifier.classify(landmarks, results). "
                "See server.py's camera loop.")

        event = self._validator.update(result, now)
        if event is not None:
            self._held = (self.to_gesture_id(event.label), event.confidence,
                          now + self._hold_seconds)

        if self._held and now < self._held[2]:
            return self._held[0], self._held[1]

        self._held = None
        return "NONE", 0.0
